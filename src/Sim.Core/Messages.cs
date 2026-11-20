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

    // The sequence number of the event whose subscribers are running (slice 1e part five: the chronicle records it on each entry);
    // 0 while a top-level command's handler runs, before any event is handled.
    long Sequence { get; }

    void Publish(IEvent @event);

    void Send(ICommand command);

    RngStream Rng(string streamName);

    TState Module<TState>()
        where TState : class, new();

    TView View<TView>()
        where TView : class;
}
