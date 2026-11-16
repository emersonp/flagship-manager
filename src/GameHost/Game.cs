using Sim.Core;
using Sim.Core.Content;
using Sim.Crew;
using Sim.Cycle;
using Sim.Missions;
using Sim.Storylets;

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
        CycleModule.Register(bus);
        CrewModule.Register(bus, pack);
        MissionsModule.Register(bus, pack);
        StoryletsModule.Register(bus, pack);
        EventTypeRegistry events = EventTypeRegistry.FromAssemblies(typeof(LegacyState).Assembly, typeof(CrewModule).Assembly, typeof(MissionsModule).Assembly, typeof(StoryletsModule).Assembly);
        return new Game(bus, events);
    }
}
