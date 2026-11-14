using Sim.Core;

namespace Sim.Cycle;

public sealed class CycleState
{
    public bool Founded { get; set; }
}

public static class CycleModule
{
    public const string Name = "cycle";

    // One week per cycle for now; transit will vary it.
    private const int WeeksPerCycle = 1;

    public static void Register(EventBus bus)
    {
        bus.Module<CycleState>(Name);
        bus.Guard<FoundLegacy>(0, "not-yet-founded", (_, context) => context.Module<CycleState>().Founded ? "The legacy is already founded." : null);
        bus.Guard<AdvancePhase>(0, "founded", (_, context) => context.Module<CycleState>().Founded ? null : "The legacy has not been founded.");
        bus.Handle<FoundLegacy>(OnFoundLegacy);
        bus.Handle<AdvancePhase>(OnAdvancePhase);
    }

    private static void OnFoundLegacy(FoundLegacy command, IBusContext context)
    {
        context.Module<CycleState>().Founded = true;
        context.Publish(new LegacyFounded());
        context.Publish(new PhaseEntered(context.State.Cycle, context.State.Phase));
    }

    private static void OnAdvancePhase(AdvancePhase command, IBusContext context)
    {
        LegacyState state = context.State;
        if (state.Phase == CyclePhase.Advance)
        {
            state.Cycle++;
            state.Week += WeeksPerCycle;
            state.Phase = CyclePhase.Orders;
            context.Publish(new CycleAdvanced(state.Cycle));
            context.Publish(new WeekAdvanced(state.Week));
        }
        else
        {
            state.Phase++;
        }

        context.Publish(new PhaseEntered(state.Cycle, state.Phase));
    }
}
