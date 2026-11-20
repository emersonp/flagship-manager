namespace Sim.Core.Narrative;

// Where a fact's values come from, so the loader can refuse a value that can never occur.
public enum FactDomain
{
    Free,
    Words,
    Objective,
    Encounter,
    Skill,
    Decision,

    // The ids of the approaches of the encounter that owns the line.
    Approach,

    Storylet,
}

// Words is the closed list of values when Domain is Words.
public sealed record FactSpec(string Name, FactType Type, FactDomain Domain = FactDomain.Free, IReadOnlyList<string>? Words = null);

// What a line for one beat kind may test and mention: its facts, the people in it (by role), and the things it names (by role).
public sealed record BeatSpec(string Kind, IReadOnlyList<FactSpec> Facts, IReadOnlyList<string> People, IReadOnlyList<string> Things);

// The catalogue of every beat kind's facts. Sim.Missions builds situations that hold exactly these; the loader and the editor read it.
public static class NarrativeFacts
{
    public static readonly IReadOnlyList<string> Tiers = ["critical", "success", "partial", "failure", "disaster"];
    public static readonly IReadOnlyList<string> Severities = ["light", "severe"];
    public static readonly IReadOnlyList<string> InjuryOutcomes = ["unharmed", "lightInjury", "incapacitated", "dead"];
    public static readonly IReadOnlyList<string> Answers = ["bold", "safe", "withdraw"];
    public static readonly IReadOnlyList<string> Standings = ["backed", "overruled", "neutral"];
    public static readonly IReadOnlyList<string> Outcomes = ["fullSuccess", "partialSuccess", "withdrawal", "loss", "incomplete"];
    public static readonly IReadOnlyList<string> CheckOutcomes = ["success", "failure"];

    // How a cycle's rolled length compares with the days planned (slice 1e-calendar, design 4.4).
    public static readonly IReadOnlyList<string> Schedules = ["ahead", "on", "delayed"];

    // The text an encounter, an approach, a decision, and an objective may carry for their own beats, by key. An encounter's
    // "arrive" is its stage beat; an approach has a text per tier; a decision's "call" and "initiative" are those beats and its "bold"
    // and "safe" the effects beat that follows the choice; an objective has a text per outcome.
    public static readonly IReadOnlyList<string> EncounterTexts = ["arrive"];

    // An approach's "attempt" is said before the check's result is known, and its tier texts after.
    public static readonly IReadOnlyList<string> ApproachTexts = ["attempt", .. Tiers];
    public static readonly IReadOnlyList<string> DecisionTexts = ["call", "initiative", "bold", "safe"];

    // Every person in a situation also has these facts, as "role.stat".
    public static readonly IReadOnlyList<FactSpec> PersonStats =
    [
        new("id", FactType.Text),
        new("caution", FactType.Number),
        new("discipline", FactType.Number),
        new("seniority", FactType.Number),
        new("isLead", FactType.Flag),
    ];

    // The pronoun forms a person token can ask for.
    public static readonly IReadOnlyList<string> PronounForms = ["they", "them", "their", "theirs", "themself"];

    // The facts every beat has.
    public static readonly IReadOnlyList<FactSpec> Common =
    [
        new("objective", FactType.Text, FactDomain.Objective),
        new("objectiveTags", FactType.Set),
        new("encounter", FactType.Text, FactDomain.Encounter),
        new("encounterTags", FactType.Set),
        new("stage", FactType.Number),
        new("momentum", FactType.Number),
        new("strain", FactType.Number),
        new("commsBlocked", FactType.Flag),
        // How many of the team can still act; on an injury beat, counted after the injury.
        new("teamStanding", FactType.Number),
    ];

    public static readonly IReadOnlyDictionary<string, BeatSpec> Beats = new[]
    {
        Beat("stage", [new("slot", FactType.Text)], []),
        Beat("skip", [], []),
        Beat("chain", [], []),
        // What is said before a check's result is known; it holds what the check holds, without the tier and the margin.
        Beat(
            "attempt",
            [
                new("approach", FactType.Text, FactDomain.Approach),
                new("skill", FactType.Text, FactDomain.Skill),
                new("supervised", FactType.Flag),
                new("helped", FactType.Flag),
                new("mentoring", FactType.Flag),
            ],
            ["actor", "helper"],
            ["skill"]),
        Beat(
            "check",
            [
                new("approach", FactType.Text, FactDomain.Approach),
                new("skill", FactType.Text, FactDomain.Skill),
                new("tier", FactType.Text, FactDomain.Words, Tiers),
                new("margin", FactType.Number),
                new("supervised", FactType.Flag),
                new("helped", FactType.Flag),
                new("mentoring", FactType.Flag),
            ],
            ["actor", "helper"],
            ["skill"]),
        Beat(
            "effects",
            [
                new("progress", FactType.Number),
                new("momentumChange", FactType.Number),
                new("strainChange", FactType.Number),
                new("injuryRoll", FactType.Text, FactDomain.Words, Severities),
                new("withdraw", FactType.Flag),
                new("decision", FactType.Text, FactDomain.Decision),
                new("chose", FactType.Text, FactDomain.Words, Answers),
            ],
            [],
            ["decision"]),
        Beat("injury", [new("severity", FactType.Text, FactDomain.Words, Severities), new("outcome", FactType.Text, FactDomain.Words, InjuryOutcomes)], ["crew"]),
        Beat("lead", [], ["crew"]),
        Beat(
            "call",
            [new("decision", FactType.Text, FactDomain.Decision), new("stakes", FactType.Number), new("prefers", FactType.Text, FactDomain.Words, Answers)],
            [],
            ["decision"]),
        Beat(
            "initiative",
            [new("decision", FactType.Text, FactDomain.Decision), new("stakes", FactType.Number), new("chose", FactType.Text, FactDomain.Words, Answers), new("blocked", FactType.Flag)],
            [],
            ["decision"]),
        Beat(
            "answer",
            [new("decision", FactType.Text, FactDomain.Decision), new("answer", FactType.Text, FactDomain.Words, Answers), new("standing", FactType.Text, FactDomain.Words, Standings)],
            [],
            ["decision"]),
        Beat("collapse", [], []),
        Beat("result", [new("outcome", FactType.Text, FactDomain.Words, Outcomes)], []),
        // A storylet arrives on the itinerary, apart from any mission: no objective, encounter, or lead. Its subject is the crew
        // member it arrived for; a storylet for the ship has none, and only one for a pair has a target (1e part three).
        new BeatSpec("storylet", [new("storylet", FactType.Text, FactDomain.Storylet)], ["subject", "target"], ["storylet"]),
        // Outcome: success or failure, for an option settled by a check (1e part four, Task 6), whose own text may name the handler.
        new BeatSpec("storyletAnswer", [new("storylet", FactType.Text, FactDomain.Storylet), new("option", FactType.Text), new("outcome", FactType.Text, FactDomain.Words, CheckOutcomes)], ["subject", "target"], ["storylet"]),
        // The cycle has turned and the calendar moved (slice 1e-calendar), said at the top of the itinerary, apart from any mission.
        // Days: how many days early or late, 0 on schedule; weeks: the weeks planned; travel: a journey of more than one week.
        // #days# says the days early or late ("4 days") and #planned# the weeks planned ("5 weeks"). The length itself is never said.
        new BeatSpec(
            "schedule",
            [new("schedule", FactType.Text, FactDomain.Words, Schedules), new("days", FactType.Number), new("weeks", FactType.Number), new("travel", FactType.Flag)],
            [],
            ["days", "planned"]),
    }.ToDictionary(b => b.Kind);

    public static IReadOnlyList<string> Kinds => Beats.Keys.ToList();

    // The fact a criterion names for this beat kind, a person's stat ("actor.caution") included; null when the kind has no such fact.
    public static FactSpec? Fact(string beat, string fact)
    {
        if (!Beats.TryGetValue(beat, out BeatSpec? spec))
        {
            return null;
        }

        int dot = fact.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0)
        {
            string role = fact[..dot];
            string stat = fact[(dot + 1)..];
            return spec.People.Contains(role) ? PersonStats.FirstOrDefault(s => s.Name == stat) : null;
        }

        return spec.Facts.FirstOrDefault(f => f.Name == fact);
    }

    // Every fact a line for this kind may test, people's stats included, in a stable order for pickers.
    public static IReadOnlyList<string> FactNames(string beat)
    {
        if (!Beats.TryGetValue(beat, out BeatSpec? spec))
        {
            return [];
        }

        return spec.Facts.Select(f => f.Name)
            .Concat(spec.People.SelectMany(p => PersonStats.Select(s => $"{p}.{s.Name}")))
            .ToList();
    }

    // Every beat has the common facts, the lead, and the objective and encounter as things.
    private static BeatSpec Beat(string kind, FactSpec[] facts, string[] people, string[]? things = null)
    {
        return new BeatSpec(kind, [.. Common, .. facts], ["lead", .. people], ["objective", "encounter", .. things ?? []]);
    }
}
