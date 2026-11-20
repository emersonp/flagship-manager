using Sim.Core.Narrative;

namespace Web.Forms;

// One beat kind as the Lines page explains it: its id, its name in the editor, when the mission produces it, and what a line for
// it may test and mention beyond what every beat has.
public sealed record BeatEntry(string Kind, string Title, string When, IReadOnlyList<string> Facts, IReadOnlyList<string> People, IReadOnlyList<string> Things);

internal static class BeatGlossary
{
    private static readonly IReadOnlyDictionary<string, string> Whens = new Dictionary<string, string>
    {
        ["stage"] = "The team arrives at an encounter, before anything is tried there.",
        ["skip"] = "The team passes an encounter by: its stage is skippable and momentum is high enough. Costs momentum.",
        ["chain"] = "An encounter leads on to the one it chains to, which comes next.",
        ["attempt"] = "Someone starts a check, before its result is known.",
        ["check"] = "A check's result, by tier and margin.",
        ["effects"] = "The effects of a check or of a choice land: progress, momentum, strain, an injury roll, a withdrawal.",
        ["injury"] = "A crew member is hurt, and how badly.",
        ["lead"] = "The lead can no longer act, and the most senior crew member still standing takes over.",
        ["call"] = "The lead calls the captain about a decision, and the mission waits for the answer.",
        ["initiative"] ="The lead decides alone, because the stakes are under their threshold or comms are blocked.",
        ["answer"] = "The captain answers a call, backing or overruling the lead.",
        ["collapse"] = "Strain reaches its cap and the team collapses. The mission is lost.",
        ["result"] = "The mission ends, with its outcome.",
        ["storylet"] = "A storylet arrives in the aftermath, apart from any mission, for a crew member (its subject) or for the ship.",
        ["storyletAnswer"] = "The captain picks one of a storylet's options.",
        ["schedule"] = "The cycle has turned and the ship has arrived: ahead of schedule, on schedule, or delayed. Shown at the top of the itinerary.",
    };

    // Names for the beats the "What plays here" panel never shows.
    private static readonly IReadOnlyDictionary<string, string> Titles = new Dictionary<string, string>
    {
        ["storylet"] = "A storylet",
        ["storyletAnswer"] = "A storylet answered",
        ["schedule"] = "The cycle's length",
    };
    private static readonly HashSet<string> CommonFacts = NarrativeFacts.Common.Select(f => f.Name).ToHashSet();

    public static IReadOnlyList<string> SharedFacts => NarrativeFacts.Common.Select(f => f.Name).ToList();

    public static IReadOnlyList<string> PersonStats => NarrativeFacts.PersonStats.Select(s => s.Name).ToList();

    public static IReadOnlyList<BeatEntry> Entries => NarrativeFacts.Kinds
        .Select(kind =>
        {
            BeatSpec spec = NarrativeFacts.Beats[kind];
            return new BeatEntry(
                kind,
                Titles.GetValueOrDefault(kind) ?? Plays.Title(kind),
                Whens.GetValueOrDefault(kind, ""),
                spec.Facts.Select(f => f.Name).Where(f => !CommonFacts.Contains(f)).ToList(),
                spec.People.Where(p => p != "lead").ToList(),
                spec.Things.Where(t => t is not "objective" and not "encounter").ToList());
        })
        .ToList();
}
