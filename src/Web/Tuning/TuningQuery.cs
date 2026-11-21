using System.Globalization;
using System.Text;
using MissionBench;
using Microsoft.AspNetCore.Http;
using Sim.Core.Content;
using Sim.Missions;

namespace Web.Tuning;

// The Tuning page's inputs and view, kept in the address's query (1f part one, Task 9; design 4.1), so a reload, a "Bench this" link, and
// a link copied from the address bar all land on the same inputs. Each is a word or a number as the command line's bench takes it
// (SimRunner/Cli.cs): a value the page does not know falls back to its default here, and Runs and Seed are kept as typed, as text (a
// 64-bit seed does not survive JavaScript), for the inputs to judge (Task 10). A default is left out of the written query.
public sealed record TuningQuery
{
    public const string Path = "/dev/tuning";

    public static readonly IReadOnlyList<string> Draws = ["redrawn", "fixed"];
    public static readonly IReadOnlyList<string> Teams = ["generated", BenchTeams.LegacyKind, BenchTeams.TestShipKind];
    public static readonly IReadOnlyList<string> Strengths = ["rookie", "mixed", "veteran", "sweep"];
    public static readonly IReadOnlyList<string> LeadStyles = ["cautious", "average", "reckless"];
    public static readonly IReadOnlyList<string> Levels = ["-1", "0", "1"];
    public static readonly IReadOnlyList<string> Policies = ["back-lead", "bold", "safe", "withdraw"];
    public static readonly IReadOnlyList<string> Views = ["one", "every"];

    public static TuningQuery Default { get; } = new();

    // The objective's id; empty until one is chosen.
    public string Objective { get; init; } = "";

    public string Draw { get; init; } = "redrawn";

    // As typed; empty for the view's default (10,000 runs in One mission, 1,000 in Every objective).
    public string Runs { get; init; } = "";

    // As typed, as text.
    public string Seed { get; init; } = "1";

    // Generated, a legacy, or a test ship.
    public string Team { get; init; } = "generated";

    // A generated team's strength, or a sweep of all three.
    public string Strength { get; init; } = "mixed";

    // A real team's source: the legacy's id or the test ship's id.
    public string Source { get; init; } = "";

    // A real team's picks, in the order given, and its lead.
    public IReadOnlyList<string> Crew { get; init; } = [];

    public string Lead { get; init; } = "";

    // A generated team's lead.
    public string Style { get; init; } = "average";

    // The operational parameters, as the command line's -1, 0, and 1.
    public string Priority { get; init; } = "0";

    public string Autonomy { get; init; } = "0";

    public bool Develop { get; init; }

    public IReadOnlyList<string> Gear { get; init; } = [];

    public string Policy { get; init; } = "back-lead";

    // One mission, or every objective.
    public string View { get; init; } = "one";

    // The bench's inputs as its form posts them, under the same names as the query. The runs box shows the view's default when none
    // were typed, so that default is read back as none, and a link to the other view takes that view's own.
    public static TuningQuery From(IFormCollection form)
    {
        TuningQuery query = From(new QueryCollection(form.ToDictionary(f => f.Key, f => f.Value)));
        return query.Runs == (query with { Runs = "" }).RunsOrDefault() ? query with { Runs = "" } : query;
    }

    public static TuningQuery From(IQueryCollection query)
    {
        string One(string name) => query[name].ToString();
        IReadOnlyList<string> Many(string name) => query[name].Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).Distinct(StringComparer.Ordinal).ToList();
        string Known(string name, IReadOnlyList<string> known, string fallback) => known.Contains(One(name)) ? One(name) : fallback;
        TuningQuery d = Default;
        return new TuningQuery
        {
            Objective = One("objective"),
            Draw = Known("draw", Draws, d.Draw),
            Runs = One("runs"),
            Seed = query.ContainsKey("seed") ? One("seed") : d.Seed,
            Team = Known("team", Teams, d.Team),
            Strength = Known("strength", Strengths, d.Strength),
            Source = One("source"),
            Crew = Many("crew"),
            Lead = One("lead"),
            Style = Known("style", LeadStyles, d.Style),
            Priority = Known("priority", Levels, d.Priority),
            Autonomy = Known("autonomy", Levels, d.Autonomy),
            Develop = One("develop") == "true",
            Gear = Many("gear"),
            Policy = Known("policy", Policies, d.Policy),
            View = Known("view", Views, d.View),
        };
    }

    // The query as it is written into a link: each input that is not its default, in a fixed order, escaped.
    public string Query()
    {
        var parts = new List<(string Name, string Value)>();
        void Add(string name, string value, string fallback)
        {
            if (value != fallback)
            {
                parts.Add((name, value));
            }
        }

        TuningQuery d = Default;
        Add("view", View, d.View);
        Add("objective", Objective, d.Objective);
        Add("draw", Draw, d.Draw);
        Add("runs", Runs, d.Runs);
        Add("seed", Seed, d.Seed);
        Add("team", Team, d.Team);
        Add("strength", Strength, d.Strength);
        Add("source", Source, d.Source);
        parts.AddRange(Crew.Select(c => ("crew", c)));
        Add("lead", Lead, d.Lead);
        Add("style", Style, d.Style);
        Add("priority", Priority, d.Priority);
        Add("autonomy", Autonomy, d.Autonomy);
        Add("develop", Develop ? "true" : "false", "false");
        parts.AddRange(Gear.Select(g => ("gear", g)));
        Add("policy", Policy, d.Policy);
        var text = new StringBuilder();
        foreach ((string name, string value) in parts)
        {
            text.Append(text.Length == 0 ? '?' : '&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
        }

        return text.ToString();
    }

    // The page's address with these inputs.
    public string Href()
    {
        return Path + Query();
    }

    // One mission for this objective, from a row of Every objective (design 4.8): the same inputs, with the objective named. A generated
    // team arrives as a sweep, since the row it came from showed all three strengths (1f part one, Task 11; my call).
    public string OneMission(string objectiveId)
    {
        return (this with { View = "one", Objective = objectiveId, Strength = RealTeam ? Strength : "sweep" }).Href();
    }

    // The runs a view takes when none are typed (design 4.4).
    public string RunsOrDefault()
    {
        return Runs.Length > 0 ? Runs : (View == "every" ? 1000 : 10000).ToString(CultureInfo.InvariantCulture);
    }

    // The objective benched: the one named, or with none named the pack's first (1f part one, Task 10; my call, so a fresh page can run).
    public string ObjectiveOrFirst(ContentPack pack)
    {
        return Objective.Length > 0 ? Objective : pack.Objectives.FirstOrDefault()?.Id ?? "";
    }

    public bool RealTeam => Team != "generated";

    // A real team's lead: the one named when they are picked, or else the first picked (the lead select shows the same).
    public string LeadOrFirst()
    {
        return Crew.Contains(Lead) ? Lead : Crew.FirstOrDefault() ?? "";
    }

    // The gear tags the pack knows, as the command line lists them: comms and medical, and every approach's gear tag.
    public static IReadOnlyList<string> GearTags(ContentPack pack)
    {
        string[] tags = ["comms", "medical", .. pack.Encounters.SelectMany(e => e.Approaches.Select(a => a.GearTag)).OfType<string>()];
        return tags.Distinct().Order(StringComparer.Ordinal).ToList();
    }

    // The inputs as the command line's bench takes them (SimRunner/Cli.cs), each judged in its words: the options for Bench.Run, the
    // strengths a generated team runs under (three for a sweep; none for a real team, which the run adds as BenchOptions.Team), and an
    // error by input for each the bench cannot take. Options is null when there is any error.
    public TuningInputs Read(ContentPack pack)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        // Every objective benches each objective in turn, so the one named is not judged there (1f part one, Task 11).
        bool every = View == "every";
        string objective = every ? pack.Objectives.FirstOrDefault()?.Id ?? "" : ObjectiveOrFirst(pack);
        if (objective.Length == 0)
        {
            errors["objective"] = "The pack has no objective to bench.";
        }
        else if (pack.Objectives.All(o => o.Id != objective))
        {
            errors["objective"] = $"Unknown objective '{objective}'. The pack has: {string.Join(", ", pack.Objectives.Select(o => o.Id))}.";
        }

        string runsText = RunsOrDefault();
        if (!int.TryParse(runsText, NumberStyles.None, CultureInfo.InvariantCulture, out int runs) || runs < 1 || runs > MaxRuns)
        {
            errors["runs"] = $"Runs must be a whole number from 1 to {MaxRuns.ToString("N0", CultureInfo.InvariantCulture)}, not '{runsText}'.";
        }

        if (!ulong.TryParse(Seed, NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
        {
            errors["seed"] = $"Seed must be a whole number of 0 or more, not '{Seed}'.";
        }

        IReadOnlyList<string> known = GearTags(pack);
        if (Gear.FirstOrDefault(t => !known.Contains(t)) is { } unknown)
        {
            errors["gear"] = $"Unknown gear tag '{unknown}'. The pack has: {string.Join(", ", known)}.";
        }

        // A generated team in Every objective is benched at all three strengths (design 4.8).
        IReadOnlyList<TeamStrength> strengths = RealTeam
            ? []
            : Strength == "sweep" || every ? [TeamStrength.Rookie, TeamStrength.Mixed, TeamStrength.Veteran] : [Word<TeamStrength>(Strength)];
        if (errors.Count > 0)
        {
            return new TuningInputs(null, strengths, errors);
        }

        var options = new BenchOptions(objective)
        {
            Runs = runs,
            Seed = seed,
            Strength = strengths.FirstOrDefault(TeamStrength.Mixed),
            Lead = Word<LeadStyle>(Style),
            Parameters = new Parameters(int.Parse(Priority, CultureInfo.InvariantCulture), int.Parse(Autonomy, CultureInfo.InvariantCulture), Develop),
            GearTags = Gear,
            Policy = Word<CallPolicy>(Policy),
            FixedDraw = Draw == "fixed",
        };
        return new TuningInputs(options, strengths, errors);
    }

    // The command line's most runs.
    public const int MaxRuns = 10_000_000;

    // The value whose kebab-case name is this word ("back-lead" is BackLead), as the command line's Choice reads it.
    private static T Word<T>(string word)
        where T : struct, Enum
    {
        return Enum.GetValues<T>().First(v => string.Concat(v.ToString().Select((c, i) => char.IsUpper(c) ? (i > 0 ? "-" : "") + char.ToLowerInvariant(c) : c.ToString())) == word);
    }
}

// The page's inputs read for the bench (TuningQuery.Read).
public sealed record TuningInputs(BenchOptions? Options, IReadOnlyList<TeamStrength> Strengths, IReadOnlyDictionary<string, string> Errors);
