namespace Sim.Core;

public interface ICommand
{
}

public interface IEvent
{
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EventTypeAttribute(string name, int version) : Attribute
{
    public string Name { get; } = name;

    public int Version { get; } = version;
}

public sealed record PublishedEvent(long Sequence, int Cycle, IEvent Event);

public interface IBusContext
{
    LegacyState State { get; }

    void Publish(IEvent @event);

    void Send(ICommand command);

    RngStream Rng(string streamName);

    TState Module<TState>()
        where TState : class, new();

    TView View<TView>()
        where TView : class;
}
