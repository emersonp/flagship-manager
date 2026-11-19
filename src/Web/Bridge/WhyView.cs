using GameHost;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;

namespace Web.Bridge;

// Dev Play's "Why these storylets" (1e part four, Task 7, design 4.10). Heading says which itinerary; Count is the count line; the
// two lists are the contenders in salience order. Found: what a search said, one line for each storylet or vignette it found; null
// before any search.
public sealed record WhyView(string Heading, string Count, IReadOnlyList<WhyRow> Storylets, IReadOnlyList<WhyRow> Vignettes)
{
    public string Query { get; init; } = "";

    public IReadOnlyList<string>? Found { get; init; }
}

// One contender: its parts as weighted (freshness is taken off), the tie draw's place, the total, and its fate. Edit: its file in
// the editor.
public sealed record WhyRow(string Id, string Shown, string Cast, int Stakes, int Specificity, int Freshness, int Tie, int Salience, string Fate, string Edit);

public static class WhyReader
{
    // A search lists at most this many, and says how many more it found.
    public const int MostFound = 20;

    // cycleEvents: this cycle's, which the dry run reads to put the open itinerary back as it opened.
    public static WhyView Read(LegacyState state, Game game, ContentPack pack, Names names, IReadOnlyList<IEvent> cycleEvents, string? query = null)
    {
        IStoryletDryRun dry = game.Bus.View<IStoryletDryRun>(state);
        Contest contest = dry.Contest(cycleEvents);
        List<Contender> all = [.. contest.Storylets, .. contest.Vignettes];
        string heading = contest.Open ? $"The itinerary now open, cycle {contest.Cycle}, as it opened" : $"The next itinerary, cycle {contest.Cycle}, as things stand now";
        string count = $"{Plural(pack.Storylets.Count, "storylet")}, {Plural(pack.Vignettes.Count, "vignette")}: "
            + $"{all.Count(c => c.Eligible)} eligible, {all.Count(c => c.Score is not null)} cast, {all.Count(c => c.Fate == ContestFate.Arrived)} arrived";
        var view = new WhyView(heading, count, Rows(contest.Storylets, pack, names), Rows(contest.Vignettes, pack, names));
        return query is null ? view : view with { Query = query, Found = Find(query.Trim(), contest, dry, cycleEvents, pack, names) };
    }

    private static List<WhyRow> Rows(IReadOnlyList<Contender> contenders, ContentPack pack, Names names)
    {
        return contenders.Where(c => c.Score is not null).Select(c =>
        {
            SalienceScore score = c.Score!;
            return new WhyRow(c.Id, Shown(c.Id, names), CastText(c.Id, c.Cast!, pack, names), score.Stakes, score.Specificity, score.Freshness, score.Tie, score.Total,
                c.Fate == ContestFate.Arrived ? "Arrived" : "Lost on the budget", Edit(c.Id, pack));
        }).ToList();
    }

    // Every storylet and vignette whose id or name holds the words, in the pack's order, each with why it is or is not here.
    private static List<string> Find(string query, Contest contest, IStoryletDryRun dry, IReadOnlyList<IEvent> cycleEvents, ContentPack pack, Names names)
    {
        if (query.Length == 0)
        {
            return ["Type an id or a name, or part of one."];
        }

        List<Contender> matches = contest.Storylets.Concat(contest.Vignettes)
            .Where(c => c.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || names.Storylet(c.Id).Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => Order(c.Id, pack))
            .ToList();
        if (matches.Count == 0)
        {
            return [$"No storylet or vignette has an id or name with {query} in it."];
        }

        List<string> found = matches.Take(MostFound).Select(c => $"{Shown(c.Id, names)}: {Reason(c, contest.Open, dry, cycleEvents, pack, names)}").ToList();
        if (matches.Count > MostFound)
        {
            found.Add($"And {matches.Count - MostFound} more. Type more of the id or name.");
        }

        return found;
    }

    private static string Reason(Contender c, bool open, IStoryletDryRun dry, IReadOnlyList<IEvent> cycleEvents, ContentPack pack, Names names)
    {
        Cooldown cooldown = pack.Storylets.FirstOrDefault(s => s.Id == c.Id)?.Cooldown ?? pack.CooldownOf(pack.Vignettes.First(v => v.Id == c.Id));
        return c.Fate switch
        {
            ContestFate.Arrived => $"{(open ? "arrived" : "arrives")}, with salience {c.Score!.Total}.",
            ContestFate.LostOnBudget => $"{(open ? "cast, and lost" : "casts, and loses")} on the budget with salience {c.Score!.Total}.",
            ContestFate.Waiting => "waiting for an answer, so it is not cast again.",
            ContestFate.Cooling => $"in its global cooldown of {Plural(cooldown.Global, "cycle")}; it may arrive again from cycle {c.ReturnsAt}.",
            ContestFate.CastCooling when c.Cast is { Count: > 0 } cast => $"every cast that held is in its cast cooldown of {Plural(cooldown.Cast, "cycle")}; {CastText(c.Id, cast, pack, names)} may have it again from cycle {c.ReturnsAt}.",
            ContestFate.CastCooling => $"in its cast cooldown of {Plural(cooldown.Cast, "cycle")}; it may arrive again from cycle {c.ReturnsAt}.",
            ContestFate.OutOfTries => $"out of tries after {c.Tries}.",
            _ => Failed(dry.Failed(c.Id, cycleEvents), Slots(c.Id, pack).Count == 0),
        };
    }

    // "subject: subject.history.count('lost_mission', 3) > 0 held for no one" (design 4.10).
    private static string Failed(FailedClause? failed, bool ship)
    {
        if (failed is null)
        {
            return "no cast held, though each clause held for someone.";
        }

        if (failed.Clause is not { } clause)
        {
            return $"{failed.Slot}: too many casts to search.";
        }

        if (ship)
        {
            return $"{failed.Slot}: {clause} is false.";
        }

        string whom = failed.Slot == FailedClause.Requires ? "no cast" : "no one";
        return failed.Before is { } before
            ? $"{failed.Slot}: {clause} held for {whom}; the clauses before it held for {before}."
            : $"{failed.Slot}: {clause} held for {whom}.";
    }

    private static IReadOnlyList<CastSlot> Slots(string id, ContentPack pack)
    {
        return pack.Storylets.FirstOrDefault(s => s.Id == id)?.Cast ?? pack.Vignettes.FirstOrDefault(v => v.Id == id)?.Cast ?? [];
    }

    private static string CastText(string id, IReadOnlyDictionary<string, string> cast, ContentPack pack, Names names)
    {
        return BridgeReader.Cast(Slots(id, pack), cast, names.Crew) ?? "The ship";
    }

    private static string Shown(string id, Names names)
    {
        string name = names.Storylet(id);
        return name == id ? id : $"{name} ({id})";
    }

    private static string Edit(string id, ContentPack pack)
    {
        return EditorLinks.File(pack.Storylets.Any(s => s.Id == id) ? "storylets" : "vignettes");
    }

    // The pack's order: storylets, then vignettes.
    private static int Order(string id, ContentPack pack)
    {
        int storylet = pack.Storylets.ToList().FindIndex(s => s.Id == id);
        return storylet >= 0 ? storylet : pack.Storylets.Count + pack.Vignettes.ToList().FindIndex(v => v.Id == id);
    }

    private static string Plural(int count, string word)
    {
        return count == 1 ? $"1 {word}" : $"{count} {word}s";
    }
}
