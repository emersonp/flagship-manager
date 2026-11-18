using System.Globalization;
using Sim.Core.Content;

namespace Sim.Content;

// Trait definitions (slice 1e, part three, design 4.3), read before encounters, storylets, and vignettes, which are checked against them.
public static partial class ContentLoader
{
    private const int MaxBriefing = 40;

    private static List<TraitDef> BuildTraits(List<(string File, TraitDto Dto)> items, List<ContentError> errors)
    {
        // First the shape of each, then its rules' effects, which may name another trait's levels.
        var shaped = new List<(string File, TraitDto Dto, TraitDef Def)>();
        foreach ((string file, TraitDto dto) in items)
        {
            int before = errors.Count;
            string id = dto.Id!;
            void Error(string message)
            {
                errors.Add(new ContentError(file, $"{id}: {message}"));
            }

            if (dto.On is not null && !TraitPlaces.All.Contains(dto.On))
            {
                Error($"on must be {Either(TraitPlaces.All)}, or left out for the ship or a crew member.");
            }

            bool pair = dto.On == TraitPlaces.Pair;
            var levels = new Dictionary<int, string>();
            foreach ((int value, string? level) in dto.Levels ?? [])
            {
                if (value < 1)
                {
                    Error($"A level's number is 1 or more; {value} is not.");
                }
                else if (level is null || !Ids.IsInnerId(level))
                {
                    Error($"Level name '{level}' must be {Said(Ids.InnerRule)}");
                }
                else if (levels.ContainsValue(level))
                {
                    Error($"Level name '{level}' is used twice.");
                }
                else
                {
                    levels[value] = level;
                }
            }

            if (dto.Max is { } max && max < 1)
            {
                Error("max must be 1 or more.");
            }
            else if (dto.Max is { } cap && levels.Keys.Any(l => l > cap))
            {
                Error($"max {cap} is below its highest level, {levels.Keys.Max()}.");
            }

            string? briefing = dto.Briefing;
            if (briefing is not null && (briefing.Length is < 1 or > MaxBriefing || briefing.Trim() != briefing || briefing.IndexOfAny(['\r', '\n']) >= 0))
            {
                Error($"briefing must be 1 to {MaxBriefing} characters on one line, with no spaces at either end.");
            }

            TraitStep? every = null;
            if (dto.Every is { } step)
            {
                if (step.Weeks < 1 || step.Add == 0)
                {
                    Error("every needs weeks of 1 or more and an add that is not 0.");
                }
                else if (levels.Count > 0)
                {
                    Error("A trait with named levels is set, not added to, so it cannot have every.");
                }
                else if (step.Add > 0 && dto.Max is null)
                {
                    Error("A trait that rises every week needs a max.");
                }
                else
                {
                    every = new TraitStep(step.Weeks, step.Add);
                }
            }

            if (dto.Expires is { Weeks: < 1 })
            {
                Error("expires needs weeks of 1 or more.");
            }

            TraitMission? mission = null;
            if (dto.Mission is { } bonus)
            {
                if (bonus.CheckBonus == 0)
                {
                    Error("mission needs a checkBonus that is not 0.");
                }
                else if (bonus.WithTarget && !pair)
                {
                    Error("Only a trait on a pair can have withTarget.");
                }
                else
                {
                    mission = new TraitMission(bonus.CheckBonus, bonus.WithTarget);
                }
            }

            if (dto.Launch?.WithTarget is { Count: > 0 } && !pair)
            {
                Error("Only a trait on a pair can have withTarget.");
            }

            if (dto.OnTargetDied is { Count: > 0 } && !pair)
            {
                Error("Only a trait on a pair can have onTargetDied.");
            }

            string? name = CheckName(file, id, dto.Name, errors);
            if (errors.Count == before)
            {
                var def = new TraitDef(id, name, dto.Shown ?? true, dto.On, levels, dto.Max, briefing, null, mission, every, dto.Expires?.Weeks, []);
                shaped.Add((file, dto, def));
            }
        }

        var book = new TraitBook(shaped.Select(s => s.Def));
        var defs = new List<TraitDef>();
        foreach ((string file, TraitDto dto, TraitDef def) in shaped)
        {
            int before = errors.Count;
            List<TraitRuleEffect> Rule(List<Dictionary<string, string>?>? written, string key)
            {
                return (written ?? []).Select(e => RuleEffect(e ?? [], def, book, m => errors.Add(new ContentError(file, $"{def.Id}: {key}: {m}")))).OfType<TraitRuleEffect>().ToList();
            }

            TraitLaunch? launch = dto.Launch is { } written
                ? new TraitLaunch(Rule(written.Sent, "launch.sent"), Rule(written.NotSent, "launch.notSent"), Rule(written.WithTarget, "launch.withTarget"))
                : null;
            List<TraitRuleEffect> died = Rule(dto.OnTargetDied, "onTargetDied");
            if (errors.Count == before)
            {
                defs.Add(def with { Launch = launch, OnTargetDied = died });
            }
        }

        return defs;
    }

    // One effect of a rule: { set | add } on this trait, { trait, set | add } on another, { loyalty } or { morale }; on is holder
    // (the default), target (a pair's only), or crew (loyalty and morale only).
    private static TraitRuleEffect? RuleEffect(Dictionary<string, string> entry, TraitDef owner, TraitBook book, Action<string> error)
    {
        string[] stats = ["loyalty", "morale"];
        string? stat = stats.FirstOrDefault(entry.ContainsKey);
        bool change = entry.ContainsKey("set") || entry.ContainsKey("add");
        if (entry.Count == 0 || (stat is null) == !change || (stat is not null && stats.Count(entry.ContainsKey) > 1))
        {
            error("A rule's effect changes one thing: a trait (set or add), loyalty, or morale.");
            return null;
        }

        string[] keys = stat is not null ? [stat, "on"] : ["trait", "set", "add", "on"];
        bool ok = true;
        foreach (string key in entry.Keys.Where(k => !keys.Contains(k)))
        {
            error($"'{key}' does not belong in this effect.");
            ok = false;
        }

        string on = entry.GetValueOrDefault("on") ?? TraitRuleOn.Holder;
        if (on is not (TraitRuleOn.Holder or TraitRuleOn.Target or TraitRuleOn.Crew))
        {
            error("A rule's effect takes on: holder, target, or crew.");
            ok = false;
        }
        else if (on == TraitRuleOn.Target && !owner.OnPair)
        {
            error("Only a trait on a pair has a target.");
            ok = false;
        }
        else if (on == TraitRuleOn.Crew && stat is null)
        {
            error("on: crew is for loyalty and morale; a trait is changed on the holder or the target.");
            ok = false;
        }

        if (stat is not null)
        {
            if (!int.TryParse(entry[stat], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount == 0)
            {
                error($"A {stat} change must be a whole number that is not 0.");
                return null;
            }

            return ok ? new TraitRuleEffect(null, 0, false, stat == "loyalty" ? amount : 0, stat == "morale" ? amount : 0, on) : null;
        }

        string trait = entry.GetValueOrDefault("trait") ?? owner.Id;
        if (!Ids.IsInnerId(trait))
        {
            error($"Trait id '{trait}' must be {Said(Ids.InnerRule)}");
            return null;
        }

        if (entry.ContainsKey("set") && entry.ContainsKey("add"))
        {
            error("A trait change needs add or set, and only one of them.");
            return null;
        }

        bool set = entry.ContainsKey("set");
        int? value = set ? book.SetValue(trait, entry["set"], error) : book.AddAmount(trait, entry["add"], error);
        string place = on == TraitRuleOn.Target ? TraitPlaces.Crew : owner.OnPair && trait == owner.Id ? TraitPlaces.Pair : TraitPlaces.Crew;
        ok &= book.CheckPlace(trait, place, error);
        return ok && value is { } v ? new TraitRuleEffect(entry.ContainsKey("trait") ? trait : null, v, set, 0, 0, on) : null;
    }

    private static string Either(IReadOnlyList<string> words)
    {
        return words.Count switch
        {
            0 => "",
            1 => words[0],
            2 => $"{words[0]} or {words[1]}",
            _ => $"{string.Join(", ", words.Take(words.Count - 1))}, or {words[^1]}",
        };
    }

    private sealed class TraitDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public bool? Shown { get; set; }

        public string? On { get; set; }

        public Dictionary<int, string?>? Levels { get; set; }

        public int? Max { get; set; }

        public string? Briefing { get; set; }

        public TraitLaunchDto? Launch { get; set; }

        public TraitMissionDto? Mission { get; set; }

        public TraitStepDto? Every { get; set; }

        public TraitExpiresDto? Expires { get; set; }

        public List<Dictionary<string, string>?>? OnTargetDied { get; set; }
    }

    private sealed class TraitLaunchDto
    {
        public List<Dictionary<string, string>?>? Sent { get; set; }

        public List<Dictionary<string, string>?>? NotSent { get; set; }

        public List<Dictionary<string, string>?>? WithTarget { get; set; }
    }

    private sealed class TraitMissionDto
    {
        public int CheckBonus { get; set; }

        public bool WithTarget { get; set; }
    }

    private sealed class TraitStepDto
    {
        public int Weeks { get; set; }

        public int Add { get; set; }
    }

    private sealed class TraitExpiresDto
    {
        public int Weeks { get; set; }
    }
}

// The pack's trait definitions while it loads: every use of a trait in an effect or a requirement is checked here (design 4.3).
internal sealed class TraitBook(IEnumerable<TraitDef> defs)
{
    private readonly Dictionary<string, TraitDef> byId = defs.ToDictionary(d => d.Id);

    public TraitDef? Def(string trait)
    {
        return byId.GetValueOrDefault(trait);
    }

    // A set's value: a whole number of 0 or more, or one of the trait's level names. A trait with levels is set only to 0 or a level.
    public int? SetValue(string trait, string text, Action<string> error)
    {
        TraitDef? def = Def(trait);
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            if (value < 0)
            {
                error("A trait's set must be 0 or more.");
                return null;
            }

            if (def is { HasLevels: true } && value != 0 && def.LevelName(value) is null)
            {
                error($"{trait} has no level {value}; its levels are {Levels(def)}.");
                return null;
            }

            return value;
        }

        if (def is { HasLevels: true })
        {
            if (def.Levels.FirstOrDefault(l => l.Value == text) is { Value: not null } level)
            {
                return level.Key;
            }

            error($"{trait} has no level named '{text}'; its levels are {Levels(def)}.");
            return null;
        }

        error($"A trait change's amount must be a whole number{(Ids.IsInnerId(text) ? $", or a level's name, and {trait} names no levels" : "")}.");
        return null;
    }

    // An add's amount. A trait with levels is set, never added to.
    public int? AddAmount(string trait, string text, Action<string> error)
    {
        if (Def(trait) is { HasLevels: true })
        {
            error(Def(trait)!.LevelsAreSet);
            return null;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
        {
            error("A trait change's amount must be a whole number.");
            return null;
        }

        return amount;
    }

    // A use of the trait on the ship, a crew member, or a pair, checked against where its definition says it sits.
    public bool CheckPlace(string trait, string place, Action<string> error)
    {
        string? on = Def(trait)?.On;
        if (on == place || (on is null && place != TraitPlaces.Pair))
        {
            return true;
        }

        error(on is null || place == TraitPlaces.Pair
            ? "A trait on a pair needs a definition with on: pair."
            : on == TraitPlaces.Pair
            ? $"{trait} sits on a pair, so it is used with toward."
            : $"{trait} sits on {(on == TraitPlaces.Ship ? "the ship" : "a crew member")}, not on {(place == TraitPlaces.Ship ? "the ship" : "a crew member")}.");
        return false;
    }

    // A requirement's is: a level's name or a number, read as min and max both at that value.
    public int? Is(string trait, string text, Action<string> error)
    {
        TraitDef? def = Def(trait);
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= 0)
        {
            return value;
        }

        if (def is { HasLevels: true } && def.Levels.FirstOrDefault(l => l.Value == text) is { Value: not null } level)
        {
            return level.Key;
        }

        error(def is { HasLevels: true }
            ? $"{trait} has no level named '{text}'; its levels are {Levels(def)}."
            : $"A requirement's is must be a whole number of 0 or more{(Ids.IsInnerId(text) ? $", or a level's name, and {trait} names no levels" : "")}.");
        return null;
    }

    private static string Levels(TraitDef def)
    {
        List<string> names = def.Levels.OrderBy(l => l.Key).Select(l => l.Value).ToList();
        return names.Count switch
        {
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{string.Join(", ", names.Take(names.Count - 1))}, and {names[^1]}",
        };
    }
}
