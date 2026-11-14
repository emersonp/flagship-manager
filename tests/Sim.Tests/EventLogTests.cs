using Sim.Core;
using Sim.Cycle;

namespace Sim.Tests;

public class EventLogTests
{
    private readonly EventTypeRegistry registry = EventTypeRegistry.FromAssemblies(typeof(CycleModule).Assembly);

    [Fact]
    public void An_event_round_trips_through_its_envelope()
    {
        var published = new PublishedEvent(7, 3, new PhaseEntered(3, CyclePhase.Resolution));

        EventEnvelope envelope = registry.ToEnvelope(published);

        Assert.Equal(new EventEnvelope(7, 3, "PhaseEntered", 1, """{"Cycle":3,"Phase":2}"""), envelope);
        Assert.Equal(published.Event, registry.FromEnvelope(envelope));
    }

    [Fact]
    public void An_unknown_event_type_throws()
    {
        var envelope = new EventEnvelope(1, 1, "Nope", 1, "{}");

        Assert.Throws<InvalidOperationException>(() => registry.FromEnvelope(envelope));
    }

    // Every event must carry a stable name and version, because class names will change over the years.
    [Fact]
    public void An_event_without_the_attribute_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => EventTypeRegistry.FromAssemblies(typeof(EventLogTests).Assembly));
    }

    private sealed record Unlabelled : IEvent;
}
