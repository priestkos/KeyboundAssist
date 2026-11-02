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
    private string? lastWritten;
    // Set once the field has been read back holding exactly what was written: only then can a shorter
    // field be a backspace rather than a write that never landed.
    private bool writeSeen;
    private string? currentTarget;
    private string? skipTarget;
    private long skipUntil;

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

        // Dynamic Sensory Jammer and Cursed Gaze punish any typing for their whole cast, and the field's
        // own lock only reports them once it has already engaged. The cast bar is the early warning.
        if (CastBar.Blocked(GameGui, now, out var holdReason))
        {
            status = holdReason;
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

        // A wave transition still looks like an active duty, but nobody can type during one.
        if (Conditions[ConditionFlag.BetweenAreas] || Conditions[ConditionFlag.OccupiedInCutSceneEvent])
        {
            status = "Waiting - the duty is changing scenes.";
            return;
        }

        var field = read.Value.Field;
        var allowed = read.Value.Allowed;

        var player = Objects.LocalPlayer;
        var prompts = GameState.Prompts(GameGui, Objects, player?.Position ?? default, player != null);
        if (skipTarget != null)
        {
            if (now < skipUntil) prompts = prompts.Where(p => p.Text != skipTarget).ToArray();
            else skipTarget = null;
        }

        // Ownership: the plugin only ever acts on text it wrote and has since read back from the field.
        if (lastWritten != null && string.Equals(field, lastWritten, StringComparison.OrdinalIgnoreCase))
            writeSeen = true;

        // A backspace has to be able to clear what the plugin typed, or the next frame writes the
        // character straight back and the field can never be emptied by hand. An empty field counts as a
        // deletion only while its word is still on screen: once the duty consumes a submitted word the
        // prompt is gone, and that must not skip anything.
        var wordShown = currentTarget != null &&
            prompts.Any(p => string.Equals(p.Text, currentTarget, StringComparison.OrdinalIgnoreCase));
        var deleted = writeSeen && lastWritten != null &&
            (field.Length == 0 ? wordShown : TypingPlan.PlayerDeleted(field, lastWritten));
        if (field.Length == 0)
        {
            lastWritten = null;
            writeSeen = false;
        }
        if (deleted)
        {
            // The duty prohibits typing: leave the field exactly as the player left it. Nothing below
            // writes either, so the lock is reported rather than acted on.
            if (!allowed)
            {
                currentTarget = null;
            }
            else
            {
                input->SetText("");
                skipTarget = currentTarget;
                skipUntil = now + 3000;
                currentTarget = null;
                status = skipTarget == null ? "Cleared the input." : $"Cleared the input; skipping '{skipTarget}' for a moment.";
                return;
            }
        }

        var step = TypingPlan.Next(field, prompts, configuration.Priority,
            configuration.IgnorePunctuation, allowed, read.Value.Reason);

        // A barrier word takes over mid-word, but only text this plugin wrote may be dropped, only while
        // the duty allows typing, and only when the plan really does switch to a different barrier word -
        // so re-selecting the word in progress can never loop.
        if (allowed && currentTarget != null && field.Length > 0 && step.Defense &&
            !string.Equals(step.Target, currentTarget, StringComparison.OrdinalIgnoreCase) &&
            TypingPlan.IsOwnedPrefix(field, currentTarget, configuration.IgnorePunctuation))
        {
            input->SetText("");
            lastWritten = null;
            writeSeen = false;
            currentTarget = null;
            status = "Dropping the partial word for a barrier prompt.";
            return;
        }

        status = step.Status;
        currentTarget = step.Kind is StepKind.Typing or StepKind.Ready ? step.Target : null;
        if (step.Kind != StepKind.Typing) return;

        // The player's Enter only reaches the field while it is the active text input.
        if (!input->IsActive)
        {
            if (now < nextFocusAt) return;
            nextFocusAt = now + 250;
            if (!GameState.EnsureFocus(addon, input)) status = "Could not focus the activity input.";
            return;
        }

        var writeAt = Environment.TickCount64;
        if (writeAt < nextWriteAt) return;
        nextWriteAt = writeAt + TypingPlan.DrawDelay(Random.Shared, configuration.CharDelayMinMs, configuration.CharDelayMaxMs);
        var written = field + step.Next;
        input->SetText(written);
        lastWritten = written;
        writeSeen = false;
    }
}
