using System.Reflection;
using System.Text.Json;

namespace Sim.Core;

public sealed record EventEnvelope(long Sequence, int Cycle, string Type, int Version, string Payload);

public sealed class EventTypeRegistry
{
    private readonly Dictionary<(string Name, int Version), Type> byName = new();
    private readonly Dictionary<Type, (string Name, int Version)> byType = new();

    public static EventTypeRegistry FromAssemblies(params Assembly[] assemblies)
    {
        var registry = new EventTypeRegistry();
        foreach (Type type in assemblies.SelectMany(a => a.GetTypes()).Where(t => typeof(IEvent).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract))
        {
            EventTypeAttribute attribute = type.GetCustomAttribute<EventTypeAttribute>()
                ?? throw new InvalidOperationException($"Event {type.Name} has no [EventType] attribute.");
            if (!registry.byName.TryAdd((attribute.Name, attribute.Version), type))
            {
                throw new InvalidOperationException($"Event type {attribute.Name} v{attribute.Version} is declared twice.");
            }

            registry.byType[type] = (attribute.Name, attribute.Version);
        }

        return registry;
    }

    public EventEnvelope ToEnvelope(PublishedEvent published)
    {
        Type type = published.Event.GetType();
        if (!byType.TryGetValue(type, out (string Name, int Version) key))
        {
            throw new InvalidOperationException($"Event {type.Name} is not registered.");
        }

        return new EventEnvelope(published.Sequence, published.Cycle, key.Name, key.Version, JsonSerializer.Serialize(published.Event, type, SimJson.Options));
    }

    public IEvent FromEnvelope(EventEnvelope envelope)
    {
        if (!byName.TryGetValue((envelope.Type, envelope.Version), out Type? type))
        {
            throw new InvalidOperationException($"Unknown event type {envelope.Type} v{envelope.Version}.");
        }

        return (IEvent)JsonSerializer.Deserialize(envelope.Payload, type, SimJson.Options)!;
    }
}
