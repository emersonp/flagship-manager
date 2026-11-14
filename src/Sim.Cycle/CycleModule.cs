using Sim.Core;

namespace Sim.Cycle;

public sealed record AdvancePhase : ICommand;

[EventType("PhaseEntered", 1)]
public sealed record PhaseEntered(int Cycle, CyclePhase Phase) : IEvent;

[EventType("CycleAdvanced", 1)]
public sealed record CycleAdvanced(int Cycle) : IEvent;

public static class CycleModule
{
    public static void Register(EventBus bus)
    {
        bus.Handle<AdvancePhase>(OnAdvancePhase);
    }

    private static void OnAdvancePhase(AdvancePhase command, IBusContext context)
    {
        LegacyState state = context.State;
        if (state.Phase == CyclePhase.Advance)
        {
            state.Cycle++;
            state.Phase = CyclePhase.Orders;
            context.Publish(new CycleAdvanced(state.Cycle));
        }
        else
        {
            state.Phase++;
        }

        context.Publish(new PhaseEntered(state.Cycle, state.Phase));
    }
}
