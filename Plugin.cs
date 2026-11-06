using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KTGTypeWordTextData = Lumina.Excel.Sheets.KTGTypeWordTextData;

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
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;

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

    // Win sync: a request is queued from the window and sent on the framework thread, at most every 10 s.
    private bool syncRequested;
    private bool awaitingSync;
    private long nextSyncAt;
    private string syncStatus = "Not read yet - press Sync, or open Masterstroke IV in Achievements.";
    private readonly Dictionary<(uint, string), long> shownWords = [];
    private bool wordsDirty;

    // KTGTypeWordTextData rows AutoKeyboundBrawler lists as Cactuar words (Cactuar, Gold Cactuar, 1,000 Needles...).
    private static readonly uint[] CactuarRows = [501, 841, 844, 845, 950, 987, 991, 1224, 1453, 1454, 1455, 1728];
    private readonly HashSet<string> cactuarWords = new(StringComparer.OrdinalIgnoreCase);

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        configuration.Normalize();

        try
        {
            var words = DataManager.GetExcelSheet<KTGTypeWordTextData>();
            foreach (var id in CactuarRows)
                if (words.TryGetRow(id, out var row) && row.Unknown0.ExtractText().Trim() is { Length: > 0 } word)
                    cactuarWords.Add(word);
        }
        catch (Exception exception)
        {
            // The "cactuar"/"tender" name match still works without the sheet.
            Log.Warning(exception, "[KeyboundAssist] Cactuar word list unavailable");
        }

        window = new AssistWindow(configuration, SaveConfig, () => status, () => syncStatus,
            () => syncRequested = true);
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
                if (GameState.InGame(ClientState))
                {
                    var shown = GameState.Prompts(GameGui, Objects, default, false);
                    ChatGui.Print("[Keybound Assist] prompts: " + (shown.Length == 0 ? "none" :
                        string.Join(", ", shown.Select(p => $"'{p.Text}' state {p.Label}{(p.Grey ? " grey" : "")}"))) + ".");
                }
                ChatGui.Print("[Keybound Assist] casts: " + (CastBar.Casts.Length == 0 ? "none" :
                    string.Join(", ", CastBar.Casts.Select(c => $"slot {c.Slot} '{c.Spell}'"))) + ".");
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
            TrackAchievements();
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

    // Reads the clear count whenever the game holds Masterstroke progress - after Sync, or when the player opens
    // the achievement - and sends the same progress request the Achievements window does when asked.
    private unsafe void TrackAchievements()
    {
        var achievements = Achievement.Instance();
        if (achievements == null) return;
        var now = Environment.TickCount64;
        if (syncRequested)
        {
            syncRequested = false;
            if (!ClientState.IsLoggedIn) syncStatus = "Log in to sync.";
            else if (now < nextSyncAt) syncStatus = "Sync is limited to once every 10 seconds.";
            else
            {
                nextSyncAt = now + 10_000;
                awaitingSync = true;
                syncStatus = "Asked the server for Masterstroke IV progress...";
                achievements->RequestAchievementProgress(Stats.LastWinAchievement);
            }
        }

        if (achievements->ProgressRequestState != Achievement.AchievementState.Loaded) return;
        if (Stats.WinsFromProgress(achievements->ProgressAchievementId, achievements->ProgressCurrent,
                achievements->ProgressMax) is not { } wins) return;
        if (wins == configuration.AchievementWins && !awaitingSync) return;
        awaitingSync = false;
        configuration.AchievementWins = wins;
        syncStatus = $"Achievements: {wins} clears, read {DateTime.Now:HH:mm}." +
                     (wins >= 100 ? " Masterstroke IV counts to 100; past that, use a manual count." : "");
        SaveConfig();
    }

    private unsafe void Tick()
    {
        if (!ClientState.IsLoggedIn || !GameState.InGame(ClientState))
        {
            ActivityKeepAlive.Forget();
            CastBar.Reset();
            GameState.ResetMemory();
            shownWords.Clear();
            if (wordsDirty)
            {
                wordsDirty = false;
                SaveConfig();
            }
            status = configuration.Enabled ? "Not in Keybound Brawler." : "Assist is off.";
            return;
        }

        var now = Environment.TickCount64;
        if (!configuration.Enabled)
        {
            // Words are still counted while the player types them all himself.
            wordsDirty |= Stats.Observe(configuration.WordsSeen, shownWords,
                GameState.Prompts(GameGui, Objects, default, false), now);
            status = "Assist is off.";
            return;
        }

        // Runs before the input field is even looked at: the duty's timer keeps counting in the gaps
        // between waves, which is exactly when nothing else in this plugin is doing work.
        ActivityKeepAlive.Tick(now);

        var addon = GameState.InputAddon(GameGui);
        var input = GameState.InputComponent(addon);
        var read = GameState.Read(addon, input);
        if (read == null)
        {
            status = "Waiting for the Keybound Brawler input.";
            return;
        }

        var player = Objects.LocalPlayer;
        // Dynamic Sensory Jammer and Cursed Gaze punish any typing for their whole cast, and the field's
        // own lock only reports them once it has already engaged. The cast bar is the early warning.
        var halted = CastBar.Blocked(GameGui, now, out var holdReason);
        var prompts = PromptMap.WithCasts(
            GameState.Prompts(GameGui, Objects, player?.Position ?? default, player != null), CastBar.Casts, cactuarWords);
        wordsDirty |= Stats.Observe(configuration.WordsSeen, shownWords, prompts, now);
        var context = new FrameContext(halted, holdReason,
            Conditions[ConditionFlag.BetweenAreas] || Conditions[ConditionFlag.OccupiedInCutSceneEvent],
            read.Value.Field, read.Value.Allowed, read.Value.Reason, prompts, GameState.ChatFocused(GameGui));

        var settings = new FrameSettings(configuration.Priority, configuration.CharDelayMinMs, configuration.CharDelayMaxMs,
            configuration.AutoSubmit, configuration.SubmitDelayMinMs, configuration.SubmitDelayMaxMs);

        // One frame, in one place: the gates, the decision (permission, ownership, backspace, barrier
        // takeover, skip) and the write itself. tests/ drives this same entry point against a recording field.
        var result = Frame.Run(context, settings, state, new NativeInputField(addon, input), now,
            ref nextWriteAt, ref nextFocusAt, Random.Shared);
        state = result.State;
        status = result.Status;
    }
}
