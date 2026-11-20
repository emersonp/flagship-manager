using System.Globalization;
using Sim.Core;
using Sim.Core.Content;
using Sim.Core.Narrative;

namespace Sim.Chronicle;

// Owns the chronicle (slice 1e part five): the legacy's permanent history, one entry for each thing worth remembering, written
// once when it happens. Each entry's text is the narrator's line for its situation, rendered with the pack live at that moment and
// stored; nothing renders it again. It draws no randomness and changes no other module's state. Other modules and the page read it
// through IChronicleView.
public static class ChronicleModule
{
    public const string Name = "chronicle";

    // After every other module (crew 100, missions 200, traits 250, storylets 300), so it hears each event after the module that
    // owns it has acted; before the cycle's next day (1000).
    private const int Order = 400;

    // The words of an entry when no line holds, so an entry is never lost. The game's pack never needs them.
    public const string FoundingFallback = "The ship took on her first crew.";

    public static void Register(EventBus bus, ContentPack pack, INarrator narrator)
    {
        bus.Module<ChronicleState>(Name);
        // The factory reads the state on every call: it may not exist until the first entry is written.
        bus.Provide<IChronicleView>(state => new ChronicleView(state.Module<ChronicleState>(Name), state.Day));
        bus.Subscribe<LegacyFounded>(Order, Name, (_, context) => OnFounded(pack, narrator, context));
    }

    private static void OnFounded(ContentPack pack, INarrator narrator, IBusContext context)
    {
        int crew = context.View<IRosterView>().Roster.Count;
        var situation = new Situation(
            NarrativeFacts.Chronicle.Founding,
            new Dictionary<string, FactValue> { ["crew"] = FactValue.Of(crew) },
            new Dictionary<string, Person>(),
            new Dictionary<string, string> { ["crew"] = crew.ToString(CultureInfo.InvariantCulture) });
        Write(narrator, context, ChronicleKinds.Founding, pack.Chronicle.Weights[ChronicleTuning.Founding], [], situation, FoundingFallback);
    }

    // Writes one entry for the event being handled, or none at weight 0. The line's ties break by the legacy's seed, the kind, and
    // the event's sequence number, so the same legacy always gets the same words.
    private static void Write(INarrator narrator, IBusContext context, string kind, int weight, IReadOnlyList<ChronicleSubject> subjects, Situation situation, string fallback)
    {
        if (weight <= 0)
        {
            return;
        }

        LegacyState legacy = context.State;
        string key = string.Create(CultureInfo.InvariantCulture, $"{legacy.Seed}|chronicle|{kind}|{context.Sequence}");
        string text = narrator.Line(situation, key) ?? fallback;
        ChronicleState state = context.Module<ChronicleState>();
        state.Entries.Add(new ChronicleEntry(state.NextId(), legacy.Day, legacy.Cycle, kind, weight, subjects, text, [context.Sequence]));
    }
}
