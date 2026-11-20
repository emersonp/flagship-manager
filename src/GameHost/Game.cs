using Sim.Chronicle;
using Sim.Core;
using Sim.Core.Content;
using Sim.Crew;
using Sim.Cycle;
using Sim.Missions;
using Sim.Narrative;
using Sim.Storylets;
using Sim.Traits;

namespace GameHost;

// The bus a content pack builds, with the registry and module types a store needs to read its events and state back.
public sealed class Game
{
    private Game(EventBus bus, EventTypeRegistry events)
    {
        Bus = bus;
        Events = events;
    }

    public EventBus Bus { get; }

    public EventTypeRegistry Events { get; }

    public IReadOnlyDictionary<string, Type> ModuleTypes => Bus.ModuleTypes;

    public static Game Build(ContentPack pack)
    {
        var bus = new EventBus();
        CycleModule.Register(bus, pack);
        CrewModule.Register(bus, pack);
        MissionsModule.Register(bus, pack);
        TraitsModule.Register(bus, pack);
        StoryletsModule.Register(bus, pack);
        // Last, so it hears each event after the module that owns it (slice 1e part five). Its words come through the narrator.
        ChronicleModule.Register(bus, pack, new Narrator(pack));
        EventTypeRegistry events = EventTypeRegistry.FromAssemblies(typeof(LegacyState).Assembly, typeof(CrewModule).Assembly, typeof(MissionsModule).Assembly, typeof(TraitsModule).Assembly, typeof(StoryletsModule).Assembly, typeof(ChronicleModule).Assembly);
        return new Game(bus, events);
    }
}
