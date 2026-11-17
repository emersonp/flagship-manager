using System.Globalization;
using Sim.Core.Content;
using Sim.Core.Narrative;

namespace Sim.Content;

// Storylets (slice 1e): what arrives in the aftermath when flags hold, and the answers with their fixed effects.
public static partial class ContentLoader
{
    private const int MaxRequirements = 4;
    private const int MinOptions = 2;
    private const int MaxOptions = 4;
    private const int MaxVignetteEntries = 3;

    private static List<StoryletDef> BuildStorylets(List<(string File, StoryletDto Dto)> items, List<ContentError> errors)
    {
        var defs = new List<StoryletDef>();
        foreach ((string file, StoryletDto dto) in items)
        {
            int before = errors.Count;
            void Error(string message)
            {
                errors.Add(new ContentError(file, $"{dto.Id}: {message}"));
            }

            if (dto.Stakes is < 1 or > 5)
            {
                Error("A storylet's stakes must be 1 to 5.");
            }

            string? scope = dto.Scope is StoryletScopes.Ship or StoryletScopes.Crew ? dto.Scope : null;
            if (scope is null)
            {
                Error("A storylet needs scope: ship or crew.");
            }

            List<Requirement> requires = Requirements(dto.Requires, scope, Error);
            List<StoryletOption> options = Options(dto.Options, scope, Error);
            if (dto.Effects is not null || dto.Default is not null)
            {
                Error("A storylet's effects and default belong to a vignette; a storylet's effects are in its options.");
            }

            if (requires.Count > 0 && !requires.OfType<FlagRange>().Any())
            {
                Error("A storylet needs at least one flag among its requirements, so its answers can stop it returning.");
            }

            string? name = CheckName(file, dto.Id!, dto.Name, errors);

            // Only a storylet that is otherwise sound can be judged on whether its answers end it.
            if (errors.Count == before)
            {
                foreach (StoryletOption option in options.Where(o => LeavesEveryRequirementTrue(requires, o, scope!)))
                {
                    Error($"Option '{option.Id}' leaves every requirement true, so this storylet would come back every cycle.");
                }
            }

            if (errors.Count == before)
            {
                defs.Add(new StoryletDef(dto.Id!, dto.Stakes, scope!, requires, options, name));
            }
        }

        return defs;
    }

    // Vignettes (1e part two, design 4.3): a storylet's shape with no stakes, and either effects that apply when it arrives or one
    // small choice of two options with a default. Its requirements need no flag: a cooldown, not its answers, stops it returning.
    private static List<VignetteDef> BuildVignettes(List<(string File, StoryletDto Dto)> items, List<ContentError> errors)
    {
        var defs = new List<VignetteDef>();
        foreach ((string file, StoryletDto dto) in items)
        {
            int before = errors.Count;
            void Error(string message)
            {
                errors.Add(new ContentError(file, $"{dto.Id}: {message}"));
            }

            if (dto.Stakes != 0)
            {
                Error("A vignette has no stakes.");
            }

            string? scope = dto.Scope is StoryletScopes.Ship or StoryletScopes.Crew ? dto.Scope : null;
            if (scope is null)
            {
                Error("A vignette needs scope: ship or crew.");
            }

            List<Requirement> requires = Requirements(dto.Requires, scope, Error, "vignette");
            bool effectsWritten = dto.Effects is { Count: > 0 };
            bool optionsWritten = dto.Options is { Count: > 0 };
            var effects = new List<StoryletEffect>();
            var options = new List<StoryletOption>();
            if (effectsWritten && optionsWritten)
            {
                Error("A vignette has effects or two options, not both.");
            }
            else if (optionsWritten)
            {
                options = Options(dto.Options, scope, Error, vignette: true);
                if (dto.Default is null || options.All(o => o.Id != dto.Default))
                {
                    Error("A vignette with options needs a default, and it must be one of them.");
                }
            }
            else
            {
                if (!effectsWritten)
                {
                    Error("A vignette with no choice needs at least one effect.");
                }

                if (dto.Default is not null)
                {
                    Error("A vignette with no choice has no default.");
                }

                effects = (dto.Effects ?? []).Select(e => StoryletEffectOf(e ?? [], scope, Error)).OfType<StoryletEffect>().ToList();
            }

            if (dto.Text is { } text && text.Split('\n').Count(l => l.Trim().Length > 0) > MaxVignetteEntries)
            {
                Error("A vignette's text is one to three entries.");
            }

            string? name = CheckName(file, dto.Id!, dto.Name, errors);
            if (errors.Count == before)
            {
                defs.Add(new VignetteDef(dto.Id!, scope!, requires, effects, options, dto.Default, name));
            }
        }

        return defs;
    }

    // Each entry is one of three kinds (design 4.2): { flag, min, max }, { stat, min, max }, or { last, not }. A range's min is 1
    // when left out, or 0 when only max is given; max has no limit when left out. Stats and facts are read on the subject, so
    // a storylet for the ship cannot have them.
    private static List<Requirement> Requirements(List<Dictionary<string, string>?>? written, string? scope, Action<string> error, string noun = "storylet")
    {
        var requires = new List<Requirement>();
        if (written is not { Count: >= 1 and <= MaxRequirements })
        {
            error($"A {noun} needs one to four requirements.");
        }

        foreach (Dictionary<string, string> entry in (written ?? []).Select(e => e ?? []))
        {
            string kind = entry.ContainsKey("stat") ? "stat" : entry.ContainsKey("last") ? "last" : "flag";
            string[] keys = kind == "last" ? ["last", "not"] : [kind, "min", "max"];
            bool ok = true;
            foreach (string key in entry.Keys.Where(k => !keys.Contains(k)))
            {
                error(kind == "flag"
                    ? $"A requirement takes flag, stat, or last; '{key}' is not one of them."
                    : $"A requirement on {kind} takes {string.Join(", ", keys[1..])}; '{key}' is not one of them.");
                ok = false;
            }

            string? name = entry.GetValueOrDefault(kind);
            if (kind != "flag" && scope == StoryletScopes.Ship)
            {
                error($"A {noun} for the ship has no subject, so it cannot read {name}.");
                ok = false;
            }

            if (kind == "last")
            {
                if (name is null || !LastCycleFacts.All.Contains(name))
                {
                    error($"'{name}' is not a fact about the last cycle; they are {string.Join(", ", LastCycleFacts.All)}.");
                    ok = false;
                }

                bool not = false;
                if (entry.TryGetValue("not", out string? notText) && !bool.TryParse(notText, out not))
                {
                    error("A requirement's not must be true or false.");
                    ok = false;
                }

                if (ok)
                {
                    requires.Add(new LastCycle(name!, not));
                }

                continue;
            }

            if (kind == "stat" && (name is null || !StoryletStats.All.Contains(name)))
            {
                error($"'{name}' is not a stat a storylet can read; they are {string.Join(", ", StoryletStats.All)}.");
                ok = false;
            }

            if (kind == "flag" && (name is null || !Ids.IsInnerId(name)))
            {
                error($"Flag id '{name}' must be {Said(Ids.InnerRule)}");
                ok = false;
            }

            bool unreadable = false;
            int? Bound(string key)
            {
                if (!entry.TryGetValue(key, out string? text))
                {
                    return null;
                }

                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= 0)
                {
                    return value;
                }

                unreadable = true;
                return null;
            }

            int? min = Bound("min");
            int? max = Bound("max");
            if (unreadable)
            {
                error("A requirement's min and max must be whole numbers of 0 or more.");
                continue;
            }

            int low = min ?? (entry.ContainsKey("max") ? 0 : 1);
            if (max is { } high && high < low)
            {
                error($"The requirement on {name} has max {high} below min {low}.");
                ok = false;
            }

            if (ok)
            {
                requires.Add(kind == "stat" ? new StatRange(name!, low, max) : new FlagRange(name!, low, max));
            }
        }

        // Ranges on one flag or stat that do not overlap would make the storylet one that can never arrive.
        IEnumerable<(string Name, int Min, int? Max)> ranges = requires.OfType<FlagRange>().Select(r => (r.Flag, r.Min, r.Max))
            .Concat(requires.OfType<StatRange>().Select(r => (r.Stat, r.Min, r.Max)));
        foreach (IGrouping<string, (string Name, int Min, int? Max)> same in ranges.GroupBy(r => r.Name))
        {
            int highestMin = same.Max(r => r.Min);
            if (same.Any(r => r.Max < highestMin))
            {
                error($"No value of {same.Key} satisfies all of this {noun}'s requirements on it.");
            }
        }

        return requires;
    }

    private static List<StoryletOption> Options(List<StoryletOptionDto?>? written, string? scope, Action<string> error, bool vignette = false)
    {
        var options = new List<StoryletOption>();
        if (vignette ? written is not { Count: 2 } : written is not { Count: >= MinOptions and <= MaxOptions })
        {
            error(vignette ? "A vignette's choice is two options." : "A storylet needs two to four options.");
        }

        var ids = new HashSet<string>();
        for (int i = 0; i < (written?.Count ?? 0); i++)
        {
            if (written![i] is not { } dto)
            {
                error($"options[{i + 1}] is blank.");
                continue;
            }

            if (dto.Id is null || !Ids.IsInnerId(dto.Id))
            {
                error($"Option id '{dto.Id}' must be {Said(Ids.InnerRule)}");
            }
            else if (!ids.Add(dto.Id))
            {
                error($"Option id '{dto.Id}' is used twice.");
            }

            string? label = dto.Label;
            if (string.IsNullOrWhiteSpace(label) || label.Trim() != label || label.Length > 40 || label.IndexOfAny(['\r', '\n']) >= 0)
            {
                error($"Option '{dto.Id}': label must be 1 to 40 characters on one line, with no spaces at either end.");
            }

            List<StoryletEffect> effects = (dto.Effects ?? []).Select(e => StoryletEffectOf(e ?? [], scope, error)).OfType<StoryletEffect>().ToList();
            options.Add(new StoryletOption(dto.Id ?? "", label ?? "", effects));
        }

        return options;
    }

    // One loyalty, morale, or flag change, or null when it is refused (each problem reported).
    private static StoryletEffect? StoryletEffectOf(Dictionary<string, string> entry, string? scope, Action<string> error)
    {
        string[] kinds = ["loyalty", "morale", "flag"];
        string[] present = kinds.Where(entry.ContainsKey).ToArray();
        if (present.Length > 1)
        {
            error($"An effect changes one thing; this one changes {string.Join(" and ", present)}.");
            return null;
        }

        string? kind = present.FirstOrDefault();
        string[] keys = kind switch
        {
            "loyalty" or "morale" => [kind, "on"],
            "flag" => ["flag", "add", "set", "on"],
            _ => [],
        };
        bool ok = true;
        foreach (string key in entry.Keys.Where(k => !keys.Contains(k)))
        {
            error($"A storylet's effect can change loyalty, morale, or a flag; '{key}' is not one of them.");
            ok = false;
        }

        if (entry.Count == 0)
        {
            error("A storylet's effect is blank.");
            return null;
        }

        string? on = entry.GetValueOrDefault("on");
        if (on == StoryletScopes.Subject && scope == StoryletScopes.Ship)
        {
            error("A storylet for the ship has no subject, so nothing can be on: subject.");
            ok = false;
        }

        if (kind is "loyalty" or "morale")
        {
            if (!int.TryParse(entry[kind], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
            {
                error($"A {kind} change must be a whole number.");
                ok = false;
            }

            if (on is not (StoryletScopes.Subject or StoryletScopes.Crew))
            {
                error($"A {kind} change needs on: subject or crew.");
                ok = false;
            }

            return !ok ? null
                : kind == "loyalty" ? new StoryletEffect(Loyalty: amount, LoyaltyOn: on)
                : new StoryletEffect(Morale: amount, MoraleOn: on);
        }

        if (kind is null)
        {
            return null;
        }

        string flag = entry["flag"];
        if (!Ids.IsInnerId(flag))
        {
            error($"Flag id '{flag}' must be {Said(Ids.InnerRule)}");
            ok = false;
        }

        bool set = entry.TryGetValue("set", out string? setText);
        bool add = entry.TryGetValue("add", out string? addText);
        int value = 0;
        if (set == add)
        {
            error("A flag change needs add or set, and only one of them.");
            ok = false;
        }
        else if (!int.TryParse(set ? setText : addText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error("A flag change's amount must be a whole number.");
            ok = false;
        }
        else if (set && value < 0)
        {
            error("A flag's set must be 0 or more.");
            ok = false;
        }

        if (on is not (null or StoryletScopes.Ship or StoryletScopes.Subject))
        {
            error("A storylet's flag change takes on: ship or subject, or no on.");
            ok = false;
        }

        return ok ? new StoryletEffect(Flag: new FlagChange(flag, value, set, on ?? "")) : null;
    }

    // Applies the option's flag changes on the storylet's own scope to the lowest values that satisfy its requirements. If every
    // requirement still holds, the storylet would be offered again at once, and every cycle after.
    private static bool LeavesEveryRequirementTrue(IReadOnlyList<Requirement> all, StoryletOption option, string scope)
    {
        // Only flags: a stat or a fact is not the storylet's to move.
        List<FlagRange> requires = all.OfType<FlagRange>().ToList();
        string own = scope == StoryletScopes.Ship ? StoryletScopes.Ship : StoryletScopes.Subject;
        Dictionary<string, int> values = requires.GroupBy(r => r.Flag).ToDictionary(g => g.Key, g => g.Max(r => r.Min));
        foreach (FlagChange change in option.Effects.Select(e => e.Flag).OfType<FlagChange>().Where(c => c.On == "" || c.On == own))
        {
            int current = values.GetValueOrDefault(change.Flag);
            values[change.Flag] = Math.Max(0, change.Set ? change.Amount : current + change.Amount);
        }

        return requires.All(r => r.Holds(values.GetValueOrDefault(r.Flag)));
    }

    // A storylet's text and each option's text, as lines whose criteria are where they sit, the way an encounter's own text is.
    // A storylet for the ship has no subject, so its text may not name one.
    private static List<LineDef> BuildStoryletLines(List<(string File, StoryletDto Dto)> items, List<SymbolDef> symbols, List<SkillDef> skills, List<ContentError> errors)
    {
        Dictionary<string, SymbolDef> symbolsById = symbols.ToDictionary(s => s.Id);
        HashSet<string> skillIds = skills.Select(s => s.Id).ToHashSet();
        var lines = new List<LineDef>();
        foreach ((string file, StoryletDto dto) in items)
        {
            string id = dto.Id!;
            bool ship = dto.Scope == StoryletScopes.Ship;
            void Add(string where, string? text, string kind, CriterionDef[] when)
            {
                if (text is null)
                {
                    return;
                }

                int before = errors.Count;
                void Error(string message)
                {
                    errors.Add(new ContentError(file, $"{id}: {where} {message}"));
                }

                if (text.Length == 0)
                {
                    Error("must not be empty.");
                    return;
                }

                BeatSpec spec = NarrativeFacts.Beats[kind];
                CheckScript(text, ship ? spec with { People = [] } : spec, symbolsById, skillIds, m => Error($"cannot be read: {m}"), m => Error($"cannot be used: {m}"));
                if (errors.Count == before)
                {
                    lines.Add(new LineDef($"{id}.{where}", kind, when, text.ReplaceLineEndings("\n"), null, id));
                }
            }

            var here = new CriterionDef("storylet", [id]);
            Add("text", dto.Text, "storylet", [here]);
            foreach (StoryletOptionDto option in (dto.Options ?? []).OfType<StoryletOptionDto>().Where(o => o.Id is not null))
            {
                Add($"{option.Id}.text", option.Text, "storyletAnswer", [here, new CriterionDef("option", [option.Id!])]);
            }
        }

        return lines;
    }

    private sealed class StoryletDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public int Stakes { get; set; }

        public string? Scope { get; set; }

        public List<Dictionary<string, string>?>? Requires { get; set; }

        public string? Text { get; set; }

        public List<StoryletOptionDto?>? Options { get; set; }

        // A vignette's only (1e part two).
        public List<Dictionary<string, string>?>? Effects { get; set; }

        public string? Default { get; set; }
    }

    private sealed class StoryletOptionDto
    {
        public string? Id { get; set; }

        public string? Label { get; set; }

        public List<Dictionary<string, string>?>? Effects { get; set; }

        public string? Text { get; set; }
    }
}
