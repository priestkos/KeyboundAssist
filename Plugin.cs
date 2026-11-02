using Dalamud.Game.ClientState.Conditions;
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
    [PluginService] internal static ICondition Conditions { get; private set; } = null!;

    private const string CommandName = "/keyboundassist";

    private readonly Configuration configuration;
    private readonly WindowSystem windowSystem = new("KeyboundAssist");
    private readonly AssistWindow window;

    private string status = "Ready.";
    private long nextWriteAt;
    private long nextFocusAt;
    private long nextErrorLog;
    // Everything the update loop decides between reading the field and writing to it.
    private AssistState state;

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
                ChatGui.Print($"[Keybound Assist] activity: {ActivityKeepAlive.Resets} timer resets, " +
                              $"{ActivityKeepAlive.Failures} consecutive failures, duty agent " +
                              $"{(ActivityKeepAlive.DedicatedOk ? "cleared" : "unavailable")}, last idle " +
                              $"{(float.IsNaN(ActivityKeepAlive.LastIdleSeconds) ? "unavailable" : ActivityKeepAlive.LastIdleSeconds.ToString("0.0") + "s")}.");
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
            ActivityKeepAlive.Forget();
            CastBar.Reset();
            GameState.ResetMemory();
            status = "Not in Keybound Brawler.";
            return;
        }

        var now = Environment.TickCount64;

        // Runs before the input field is even looked at: the duty's timer keeps counting in the gaps
        // between waves, which is exactly when nothing else in this plugin is doing work.
        ActivityKeepAlive.Tick(now, configuration.KeepAlive);

        var addon = GameState.InputAddon(GameGui);
        var input = GameState.InputComponent(addon);
        var read = GameState.Read(addon, input);
        if (read == null)
        {
            status = "Waiting for the Keybound Brawler input.";
            return;
        }

        var player = Objects.LocalPlayer;
        var context = new FrameContext(
            // Dynamic Sensory Jammer and Cursed Gaze punish any typing for their whole cast, and the field's
            // own lock only reports them once it has already engaged. The cast bar is the early warning.
            CastBar.Blocked(GameGui, now, out var holdReason), holdReason,
            Conditions[ConditionFlag.BetweenAreas] || Conditions[ConditionFlag.OccupiedInCutSceneEvent],
            read.Value.Field, read.Value.Allowed, read.Value.Reason,
            GameState.Prompts(GameGui, Objects, player?.Position ?? default, player != null));

        var settings = new FrameSettings(configuration.Priority, configuration.IgnorePunctuation,
            configuration.CharDelayMinMs, configuration.CharDelayMaxMs);

        // One frame, in one place: the gates, the decision (permission, ownership, backspace, barrier
        // takeover, skip) and the write itself. tests/ drives this same entry point against a recording field.
        var result = Frame.Run(context, settings, state, new NativeInputField(addon, input), now,
            ref nextWriteAt, ref nextFocusAt, Random.Shared);
        state = result.State;
        status = result.Status;
    }
}
