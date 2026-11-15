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
}

// Words is the closed list of values when Domain is Words.
public sealed record FactSpec(string Name, FactType Type, FactDomain Domain = FactDomain.Free, IReadOnlyList<string>? Words = null);

// What a line for one beat kind may test and mention: its facts, the people in it (by role), and the things it names (by role).
public sealed record BeatSpec(string Kind, IReadOnlyList<FactSpec> Facts, IReadOnlyList<string> People, IReadOnlyList<string> Things);

// The catalogue of every beat kind's facts. Sim.Missions builds situations that hold exactly these; the loader and the editor read it.
public static class NarrativeFacts
{
    public static readonly IReadOnlyList<string> Uses = ["log", "report"];

    public static readonly IReadOnlyList<string> Tiers = ["critical", "success", "partial", "failure", "disaster"];
    public static readonly IReadOnlyList<string> Approaches = ["careful", "fast"];
    public static readonly IReadOnlyList<string> Severities = ["light", "severe"];
    public static readonly IReadOnlyList<string> InjuryOutcomes = ["unharmed", "lightInjury", "incapacitated", "dead"];
    public static readonly IReadOnlyList<string> Answers = ["bold", "safe", "withdraw"];
    public static readonly IReadOnlyList<string> Standings = ["backed", "overruled", "neutral"];
    public static readonly IReadOnlyList<string> Outcomes = ["fullSuccess", "partialSuccess", "withdrawal", "loss", "incomplete"];

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

    private static readonly FactSpec[] Common =
    [
        new("objective", FactType.Text, FactDomain.Objective),
        new("objectiveTags", FactType.Set),
        new("encounter", FactType.Text, FactDomain.Encounter),
        new("encounterTags", FactType.Set),
        new("stage", FactType.Number),
        new("momentum", FactType.Number),
        new("strain", FactType.Number),
        new("commsBlocked", FactType.Flag),
    ];

    public static readonly IReadOnlyDictionary<string, BeatSpec> Beats = new[]
    {
        Beat("stage", [new("slot", FactType.Text)], []),
        Beat("skip", [], []),
        Beat("chain", [], []),
        Beat(
            "check",
            [
                new("approach", FactType.Text, FactDomain.Words, Approaches),
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
            ],
            []),
        Beat("injury", [new("severity", FactType.Text, FactDomain.Words, Severities), new("outcome", FactType.Text, FactDomain.Words, InjuryOutcomes)], ["crew"]),
        Beat("lead", [], ["crew"]),
        Beat(
            "call",
            [new("decision", FactType.Text, FactDomain.Decision), new("stakes", FactType.Number), new("prefers", FactType.Text, FactDomain.Words, Answers)],
            [],
            ["decision"]),
        Beat(
            "notice",
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
