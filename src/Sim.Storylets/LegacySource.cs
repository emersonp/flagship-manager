using Sim.Core;
using Sim.Expressions;

namespace Sim.Storylets;

// A legacy as the expressions read it now (1e part six, Task 14): the module's own StateSource over the bus's views, at the
// legacy's cycle and day, as a waiting storylet's answers are read. Dev Play's "Save as test ship" captures what this answers, and
// its test asks a captured ship the same questions. It reads only.
public static class LegacySource
{
    public static IStateSource Of(EventBus bus, LegacyState state)
    {
        return new StateSource(bus.View<IRosterView>(state), bus.View<ITraitView>(state), bus.View<IChronicleView>(state),
            state.Module<StoryletsState>(StoryletsModule.Name), state.Cycle, state.Day);
    }
}
