namespace Sim.Core;

public sealed class EventBus
{
    private readonly Dictionary<Type, Action<ICommand, IBusContext>> handlers = new();
    private readonly List<Subscription> subscriptions = new();

    public void Handle<TCommand>(Action<TCommand, IBusContext> handler)
        where TCommand : ICommand
    {
        if (!handlers.TryAdd(typeof(TCommand), (command, context) => handler((TCommand)command, context)))
        {
            throw new InvalidOperationException($"Command {typeof(TCommand).Name} already has a handler.");
        }
    }

    public void Subscribe<TEvent>(int order, string name, Action<TEvent, IBusContext> subscriber)
        where TEvent : IEvent
    {
        subscriptions.Add(new Subscription(typeof(TEvent), order, name, (e, context) => subscriber((TEvent)e, context)));
        subscriptions.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Name, b.Name));
    }

    public IReadOnlyList<PublishedEvent> Send(LegacyState state, ICommand command)
    {
        var context = new Context(this, state);
        context.Send(command);
        while (context.Pending.Count > 0)
        {
            PublishedEvent published = context.Pending.Dequeue();
            foreach (Subscription subscription in subscriptions)
            {
                if (subscription.EventType == published.Event.GetType())
                {
                    subscription.Invoke(published.Event, context);
                }
            }
        }

        context.SaveStreams();
        return context.Published;
    }

    private sealed record Subscription(Type EventType, int Order, string Name, Action<IEvent, IBusContext> Invoke);

    private sealed class Context(EventBus bus, LegacyState state) : IBusContext
    {
        private readonly Dictionary<string, RngStream> streams = new();

        public LegacyState State { get; } = state;

        public Queue<PublishedEvent> Pending { get; } = new();

        public List<PublishedEvent> Published { get; } = new();

        public void Publish(IEvent @event)
        {
            var published = new PublishedEvent(State.NextSequence++, State.Cycle, @event);
            Published.Add(published);
            Pending.Enqueue(published);
        }

        public void Send(ICommand command)
        {
            if (!bus.handlers.TryGetValue(command.GetType(), out Action<ICommand, IBusContext>? handler))
            {
                throw new InvalidOperationException($"Command {command.GetType().Name} has no handler.");
            }

            handler(command, this);
        }

        public RngStream Rng(string streamName)
        {
            string key = $"{State.Cycle}:{streamName}";
            if (!streams.TryGetValue(key, out RngStream? stream))
            {
                stream = State.RngStates.TryGetValue(key, out ulong saved)
                    ? new RngStream(saved)
                    : RngStream.For(State.Seed, State.Cycle, streamName);
                streams[key] = stream;
            }

            return stream;
        }

        public void SaveStreams()
        {
            string prefix = $"{State.Cycle}:";
            foreach (string stale in State.RngStates.Keys.Where(k => !k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                State.RngStates.Remove(stale);
            }

            foreach ((string key, RngStream stream) in streams)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    State.RngStates[key] = stream.State;
                }
            }
        }
    }
}
