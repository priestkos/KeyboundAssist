using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace KeyboundAssist;

// The duty expels you for inactivity even while the plugin is filling its field, because a direct
// write into the text component is not input. A physical player clears these counters by pressing
// keys; this clears the same counters directly, so the plugin still never sends a keystroke.
// Layout and the pointer-identity checks are taken from Keybound Pilot's KtgActivity/ActivityTimers,
// which ships this against the same client (Apache-2.0).
internal static unsafe class ActivityKeepAlive
{
    private const long IntervalMs = 3000;
    private const int MaxFailures = 3;

    private static long next;
    private static int failures;

    internal static int Resets { get; private set; }
    internal static int Failures => failures;
    internal static bool Stopped => failures >= MaxFailures;
    internal static float LastIdleSeconds { get; private set; } = float.NaN;

    internal static void Tick(long now, bool active)
    {
        if (!active)
        {
            next = 0;
            return;
        }
        // Three failures in a row means the layout moved; stop poking memory and say so.
        if (Stopped || now < next) return;
        next = now + IntervalMs;
        if (Touch()) { failures = 0; Resets++; }
        else failures++;
    }

    private static bool Touch()
    {
        var generic = TouchGenericTimers();
        var dedicated = TouchDedicatedTimers();
        return generic && dedicated;
    }

    // InputTimerModule is the module the game itself uses for AFK and duty withdrawal.
    private static bool TouchGenericTimers()
    {
        var timers = InputTimerModule.Instance();
        if (timers == null) return false;
        if (float.IsFinite(timers->ContentInputTimer) && timers->ContentInputTimer >= 0)
            LastIdleSeconds = timers->ContentInputTimer;

        timers->ContentInputTimer = 0;
        timers->InputTimer = 0;
        if (timers->AfkTimer > 0) timers->AfkTimer = 0;
        foreach (ref var device in timers->ControllerInputTimers) device.Timer = 0;
        foreach (ref var mouse in timers->MouseInputTimers) mouse.Timer = 0;

        return timers->ContentInputTimer == 0 && timers->InputTimer == 0 && timers->AfkTimer <= 0;
    }

    // This duty keeps its own idle pair on the KTG input agent (397 is the HUD, the input agent sits
    // next to it). Writing without proving all three pointers belong together would corrupt whatever
    // the offset actually points at, so nothing is written until the identity checks pass.
    [StructLayout(LayoutKind.Explicit, Size = 0x60)]
    private struct KtgInputAgent
    {
        [FieldOffset(0)] public AgentInterface Agent;
        [FieldOffset(0x28)] public AgentInterface* Hud;
        [FieldOffset(0x50)] public float IdleSeconds;
        [FieldOffset(0x54)] public float SameKeySeconds;
    }

    private static bool TouchDedicatedTimers()
    {
        var module = AgentModule.Instance();
        if (module == null) return false;
        var hud = module->GetAgentByInternalId(AgentId.KTGHud);
        var input = (KtgInputAgent*)module->GetAgentByInternalId((AgentId)398);
        if (input == null || hud == null || (AgentInterface*)input == hud) return false;
        if (input->Hud != hud) return false;
        if (hud->UIModuleInterface == null || input->Agent.UIModuleInterface != hud->UIModuleInterface) return false;
        if (!float.IsFinite(input->IdleSeconds) || input->IdleSeconds < 0) return false;
        if (!float.IsFinite(input->SameKeySeconds) || input->SameKeySeconds < 0) return false;

        input->IdleSeconds = 0;
        input->SameKeySeconds = 0;
        return input->IdleSeconds == 0 && input->SameKeySeconds == 0;
    }
}
