using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace KeyboundAssist;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Keybound Assist";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    private const string CommandName = "/keyboundassist";

    private readonly Configuration configuration;
    private readonly WindowSystem windowSystem = new("KeyboundAssist");
    private readonly AssistWindow window;

    private string status = "Ready.";
    private long nextWriteAt;
    private long nextFocusAt;
    private long nextErrorLog;

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        configuration.Normalize();

        window = new AssistWindow(configuration, SaveConfig, () => status);
        windowSystem.AddWindow(window);

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMain;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMain;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Keybound Assist. /keyboundassist on|off toggles filling prompts, " +
                          "/keyboundassist status prints what it sees.",
        });

        Framework.Update += OnUpdate;
        Log.Info("[KeyboundAssist] loaded; filling prompts = " + configuration.Enabled + ".");
    }

    public void Dispose()
    {
        SaveConfig();
        Framework.Update -= OnUpdate;
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMain;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMain;
        windowSystem.RemoveAllWindows();
    }

    private void SaveConfig()
    {
        configuration.Normalize();
        PluginInterface.SavePluginConfig(configuration);
    }

    private void ToggleMain() => window.IsOpen = !window.IsOpen;

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "on":
                configuration.Enabled = true;
                SaveConfig();
                break;
            case "off":
                configuration.Enabled = false;
                SaveConfig();
                break;
            case "status":
                Log.Info("[KeyboundAssist] status: " + status);
                ChatGui.Print("[Keybound Assist] " + status);
                break;
            case "":
                ToggleMain();
                break;
            default:
                ChatGui.Print("[Keybound Assist] Usage: " + CommandName + " [on|off|status]");
                break;
        }
    }

    private void OnUpdate(IFramework framework)
    {
        // A fault raised inside a framework handler would take the client down with every other
        // loaded plugin's session, so nothing escapes here.
        try
        {
            Tick();
        }
        catch (Exception exception)
        {
            var now = Environment.TickCount64;
            if (now < nextErrorLog) return;
            nextErrorLog = now + 10_000;
            Log.Error(exception, "[KeyboundAssist] update failed");
        }
    }

    private unsafe void Tick()
    {
        if (!configuration.Enabled)
        {
            status = "Assist is off.";
            return;
        }

        if (!ClientState.IsLoggedIn || !GameState.InGame(ClientState))
        {
            status = "Not in Keybound Brawler.";
            return;
        }

        var addon = GameState.InputAddon(GameGui);
        var input = GameState.InputComponent(addon);
        var read = GameState.Read(addon, input);
        if (read == null)
        {
            status = "Waiting for the Keybound Brawler input.";
            return;
        }

        var player = Objects.LocalPlayer;
        var prompts = GameState.Prompts(GameGui, Objects, player?.Position ?? default, player != null);
        var step = TypingPlan.Next(read.Value.Field, prompts, configuration.Priority,
            read.Value.Allowed, read.Value.Reason);
        status = step.Status;
        if (step.Kind != StepKind.Typing) return;

        // The player's Enter only reaches the field while it is the active text input.
        if (!input->IsActive)
        {
            var now = Environment.TickCount64;
            if (now < nextFocusAt) return;
            nextFocusAt = now + 250;
            if (!GameState.EnsureFocus(addon, input)) status = "Could not focus the activity input.";
            return;
        }

        var writeAt = Environment.TickCount64;
        if (writeAt < nextWriteAt) return;
        nextWriteAt = writeAt + TypingPlan.DrawDelay(Random.Shared, configuration.CharDelayMinMs, configuration.CharDelayMaxMs);
        input->SetText(read.Value.Field + step.Next);
    }
}
