namespace Sim.Core;

public sealed class EventBus
{
    private readonly Dictionary<Type, Action<ICommand, IBusContext>> handlers = new();
    private readonly List<Subscription> subscriptions = new();
    private readonly List<GuardEntry> guards = new();
    private readonly Dictionary<string, Type> moduleTypes = new();
    private readonly Dictionary<Type, Func<LegacyState, object>> views = new();

    public IReadOnlyDictionary<string, Type> ModuleTypes => moduleTypes.AsReadOnly();

    // A guard only reads: it may not publish or send, and reading module state never creates it.
    public void Guard<TCommand>(int order, string name, Func<TCommand, IBusContext, string?> check)
        where TCommand : ICommand
    {
        if (guards.Any(g => g.CommandType == typeof(TCommand) && g.Order == order && g.Name == name))
        {
            throw new InvalidOperationException($"Command {typeof(TCommand).Name} already has a guard '{name}' at order {order}.");
        }

        guards.Add(new GuardEntry(typeof(TCommand), order, name, (command, context) => check((TCommand)command, context)));
        guards.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Name, b.Name));
    }

    // A state type has a public parameterless constructor, no `required` on the type, and [JsonRequired] on each stored property with an empty initialiser.
    public void Module<TState>(string name)
        where TState : class, new()
    {
        if (moduleTypes.ContainsKey(name) || moduleTypes.ContainsValue(typeof(TState)))
        {
            throw new InvalidOperationException($"Module '{name}' or state type {typeof(TState).Name} is already declared.");
        }

        moduleTypes.Add(name, typeof(TState));
    }

    public void Provide<TView>(Func<LegacyState, TView> factory)
        where TView : class
    {
        if (!views.TryAdd(typeof(TView), factory))
        {
            throw new InvalidOperationException($"View {typeof(TView).Name} is already provided.");
        }
    }

    public TView View<TView>(LegacyState state)
        where TView : class
    {
        if (!views.TryGetValue(typeof(TView), out Func<LegacyState, object>? factory))
        {
            throw new InvalidOperationException($"View {typeof(TView).Name} is not provided.");
        }

        bool previous = state.ReadOnlyModules;
        state.ReadOnlyModules = true;
        try
        {
            return (TView)factory(state);
        }
        finally
        {
            state.ReadOnlyModules = previous;
        }
    }

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
        if (subscriptions.Any(s => s.EventType == typeof(TEvent) && s.Order == order && s.Name == name))
        {
            throw new InvalidOperationException($"Event {typeof(TEvent).Name} already has a subscriber '{name}' at order {order}.");
        }

        subscriptions.Add(new Subscription(typeof(TEvent), order, name, (e, context) => subscriber((TEvent)e, context)));
        subscriptions.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Name, b.Name));
    }

    // If a handler or subscriber throws anything, the state may be half-changed: the caller must discard it and reload.
    // Only a guard's refusal of the top-level command is guaranteed to leave the state untouched.
    public IReadOnlyList<PublishedEvent> Send(LegacyState state, ICommand command)
    {
        var context = new Context(this, state);
        context.Send(command);
        while (context.Pending.Count > 0)
        {
            PublishedEvent published = context.Pending.Dequeue();
            context.Sequence = published.Sequence;
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

    private sealed record GuardEntry(Type CommandType, int Order, string Name, Func<ICommand, IBusContext, string?> Check);

    private sealed record Subscription(Type EventType, int Order, string Name, Action<IEvent, IBusContext> Invoke);

    private sealed class Context(EventBus bus, LegacyState state) : IBusContext
    {
        private readonly Dictionary<string, RngStream> streams = new();

        private bool guarding;

        public LegacyState State { get; } = state;

        public long Sequence { get; set; }

        public Queue<PublishedEvent> Pending { get; } = new();

        public List<PublishedEvent> Published { get; } = new();

        public void Publish(IEvent @event)
        {
            ThrowIfGuarding();
            var published = new PublishedEvent(State.NextSequence++, State.Cycle, @event);
            Published.Add(published);
            Pending.Enqueue(published);
        }

        public void Send(ICommand command)
        {
            ThrowIfGuarding();
            if (!bus.handlers.TryGetValue(command.GetType(), out Action<ICommand, IBusContext>? handler))
            {
                throw new InvalidOperationException($"Command {command.GetType().Name} has no handler.");
            }

            RunGuards(command);

            handler(command, this);
        }

        private void ThrowIfGuarding()
        {
            if (guarding)
            {
                throw new InvalidOperationException("A guard may not publish or send.");
            }
        }

        private void RunGuards(ICommand command)
        {
            bool previousGuarding = guarding;
            bool previousReadOnly = State.ReadOnlyModules;
            guarding = true;
            State.ReadOnlyModules = true;
            try
            {
                foreach (GuardEntry guard in bus.guards)
                {
                    if (guard.CommandType == command.GetType())
                    {
                        string? reason = guard.Check(command, this);
                        if (reason is not null)
                        {
                            throw new CommandRejectedException(reason);
                        }
                    }
                }
            }
            finally
            {
                guarding = previousGuarding;
                State.ReadOnlyModules = previousReadOnly;
            }
        }

        public TState Module<TState>()
            where TState : class, new()
        {
            foreach ((string name, Type type) in bus.moduleTypes)
            {
                if (type == typeof(TState))
                {
                    return State.Module<TState>(name);
                }
            }

            throw new InvalidOperationException($"Module state {typeof(TState).Name} is not declared.");
        }

        public TView View<TView>()
            where TView : class
        {
            return bus.View<TView>(State);
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
