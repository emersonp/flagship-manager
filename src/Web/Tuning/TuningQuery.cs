using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;

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

    // The runs a view takes when none are typed (design 4.4).
    public string RunsOrDefault()
    {
        return Runs.Length > 0 ? Runs : (View == "every" ? 1000 : 10000).ToString(CultureInfo.InvariantCulture);
    }
}
