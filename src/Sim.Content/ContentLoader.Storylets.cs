using System.Globalization;
using Sim.Core.Content;
using Sim.Core.Narrative;

namespace Sim.Content;

// Storylets (slice 1e): what arrives on the itinerary when traits hold, and the answers with their fixed effects.
public static partial class ContentLoader
{
    private const int MaxRequirements = 4;
    private const int MinOptions = 2;
    private const int MaxOptions = 4;
    private const int MaxVignetteEntries = 3;

    private static List<StoryletDef> BuildStorylets(List<(string File, StoryletDto Dto)> items, TraitBook book, List<ContentError> errors)
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

            string? scope = dto.Scope is StoryletScopes.Ship or StoryletScopes.Crew or StoryletScopes.Pair ? dto.Scope : null;
            if (scope is null)
            {
                Error("A storylet needs scope: ship, crew, or pair.");
            }

            List<Requirement> requires = Requirements(dto.Requires, scope, book, Error);
            List<StoryletOption> options = Options(dto.Options, scope, book, Error);
            if (dto.Effects is not null || dto.Default is not null)
            {
                Error("A storylet's effects and default belong to a vignette; a storylet's effects are in its options.");
            }

            if (requires.Count > 0 && !requires.OfType<TraitRange>().Any())
            {
                Error("A storylet needs at least one trait among its requirements, so its answers can stop it returning.");
            }

            string? name = CheckName(file, dto.Id!, dto.Name, errors);

            // Only a storylet that is otherwise sound can be judged on whether its answers end it.
            if (errors.Count == before)
            {
                foreach (StoryletOption option in options.Where(o => LeavesEveryRequirementTrue(requires, o, scope!, book)))
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
    // small choice of two options with a default. Its requirements need no trait: a cooldown, not its answers, stops it returning.
    private static List<VignetteDef> BuildVignettes(List<(string File, StoryletDto Dto)> items, TraitBook book, List<ContentError> errors)
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

            string? scope = dto.Scope is StoryletScopes.Ship or StoryletScopes.Crew or StoryletScopes.Pair ? dto.Scope : null;
            if (scope is null)
            {
                Error("A vignette needs scope: ship, crew, or pair.");
            }

            List<Requirement> requires = Requirements(dto.Requires, scope, book, Error, "vignette");
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
                options = Options(dto.Options, scope, book, Error, vignette: true);
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

                effects = (dto.Effects ?? []).Select(e => StoryletEffectOf(e ?? [], scope, book, Error)).OfType<StoryletEffect>().ToList();
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

    // Each entry is one of three kinds (design 4.2): { trait, min, max }, { stat, min, max }, or { last, not }. A range's min is 1
    // when left out, or 0 when only max is given; max has no limit when left out. A trait's range may instead be is: a number or
    // one of its levels (1e part three). Stats and facts are read on the subject, so a storylet for the ship cannot have them.
    private static List<Requirement> Requirements(List<Dictionary<string, string>?>? written, string? scope, TraitBook book, Action<string> error, string noun = "storylet")
    {
        var requires = new List<Requirement>();
        if (written is not { Count: >= 1 and <= MaxRequirements })
        {
            error($"A {noun} needs one to four requirements.");
        }

        foreach (Dictionary<string, string> entry in (written ?? []).Select(e => e ?? []))
        {
            string kind = entry.ContainsKey("stat") ? "stat" : entry.ContainsKey("last") ? "last" : "trait";
            string[] keys = kind == "last" ? ["last", "not", "of"] : kind == "trait" ? ["trait", "min", "max", "is"] : [kind, "min", "max", "of"];
            bool ok = true;
            foreach (string key in entry.Keys.Where(k => !keys.Contains(k)))
            {
                error(key == "flag" ? FlagIsNowTrait
                    : kind == "trait"
                    ? $"A requirement takes trait, stat, or last; '{key}' is not one of them."
                    : $"A requirement on {kind} takes {string.Join(", ", keys[1..])}; '{key}' is not one of them.");
                ok = false;
            }

            string? name = entry.GetValueOrDefault(kind);
            if (kind != "trait" && scope == StoryletScopes.Ship)
            {
                error($"A {noun} for the ship has no subject, so it cannot read {name}.");
                ok = false;
            }

            // A stat or a fact about the last cycle may be the pair's target's (1e part three): of: target.
            bool ofTarget = false;
            if (entry.TryGetValue("of", out string? of))
            {
                if (of != StoryletScopes.Target || scope != StoryletScopes.Pair)
                {
                    error($"of: target is the only of, and only a {noun} for a pair has a target.");
                    ok = false;
                }

                ofTarget = true;
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
                    requires.Add(new LastCycle(name!, not, ofTarget));
                }

                continue;
            }

            if (kind == "stat" && (name is null || !StoryletStats.All.Contains(name)))
            {
                error($"'{name}' is not a stat a storylet can read; they are {string.Join(", ", StoryletStats.All)}.");
                ok = false;
            }

            if (kind == "trait" && !entry.ContainsKey("flag") && (name is null || !Ids.IsInnerId(name)))
            {
                error($"Trait id '{name}' must be {Said(Ids.InnerRule)}");
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
            if (kind == "trait" && entry.TryGetValue("is", out string? isText))
            {
                if (entry.ContainsKey("min") || entry.ContainsKey("max"))
                {
                    error("A requirement has is, or min and max, not both.");
                    continue;
                }

                if (name is null || !Ids.IsInnerId(name) || book.Is(name, isText, error) is not { } exact)
                {
                    continue;
                }

                (min, max) = (exact, exact);
            }

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

            // In a storylet for a pair, a trait defined on a pair is the pair's; any other is the holder's own.
            if (ok && kind == "trait" && scope is not null)
            {
                string place = scope == StoryletScopes.Ship ? TraitPlaces.Ship
                    : scope == StoryletScopes.Pair && book.Def(name!) is { OnPair: true } ? TraitPlaces.Pair
                    : TraitPlaces.Crew;
                ok = book.CheckPlace(name!, place, error);
            }

            if (ok)
            {
                requires.Add(kind == "stat" ? new StatRange(name!, low, max, ofTarget) : new TraitRange(name!, low, max));
            }
        }

        // Ranges on one trait or stat that do not overlap would make the storylet one that can never arrive.
        IEnumerable<(string Name, int Min, int? Max)> ranges = requires.OfType<TraitRange>().Select(r => (r.Trait, r.Min, r.Max))
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

    private static List<StoryletOption> Options(List<StoryletOptionDto?>? written, string? scope, TraitBook book, Action<string> error, bool vignette = false)
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

            List<StoryletEffect> effects = (dto.Effects ?? []).Select(e => StoryletEffectOf(e ?? [], scope, book, error)).OfType<StoryletEffect>().ToList();
            options.Add(new StoryletOption(dto.Id ?? "", label ?? "", effects));
        }

        return options;
    }

    // One loyalty, morale, or trait change, or null when it is refused (each problem reported).
    private static StoryletEffect? StoryletEffectOf(Dictionary<string, string> entry, string? scope, TraitBook book, Action<string> error)
    {
        string[] kinds = ["loyalty", "morale", "trait"];
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
            "trait" => ["trait", "add", "set", "on", "toward"],
            _ => [],
        };
        bool ok = true;
        foreach (string key in entry.Keys.Where(k => !keys.Contains(k)))
        {
            error(key == "flag" ? FlagIsNowTrait : $"A storylet's effect can change loyalty, morale, or a trait; '{key}' is not one of them.");
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

        if (on == StoryletScopes.Target && scope != StoryletScopes.Pair)
        {
            error("Only a storylet for a pair has a target, so only it can have on: target.");
            ok = false;
        }

        if (kind is "loyalty" or "morale")
        {
            if (!int.TryParse(entry[kind], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
            {
                error($"A {kind} change must be a whole number.");
                ok = false;
            }

            if (on is not (StoryletScopes.Subject or StoryletScopes.Crew or StoryletScopes.Target))
            {
                error($"A {kind} change needs on: subject or crew{(scope == StoryletScopes.Pair ? ", or target" : "")}.");
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

        string trait = entry["trait"];
        if (!Ids.IsInnerId(trait))
        {
            error($"Trait id '{trait}' must be {Said(Ids.InnerRule)}");
            ok = false;
        }

        bool set = entry.TryGetValue("set", out string? setText);
        bool add = entry.TryGetValue("add", out string? addText);
        int value = 0;
        if (set == add)
        {
            error("A trait change needs add or set, and only one of them.");
            ok = false;
        }
        else if (book.Def(trait) is { HasLevels: true } levelled)
        {
            if (!set)
            {
                error(levelled.LevelsAreSet);
                ok = false;
            }
            else if (book.SetValue(trait, setText!, error) is { } level)
            {
                value = level;
            }
            else
            {
                ok = false;
            }
        }
        else if (!int.TryParse(set ? setText : addText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error("A trait change's amount must be a whole number.");
            ok = false;
        }
        else if (set && value < 0)
        {
            error("A trait's set must be 0 or more.");
            ok = false;
        }

        string? toward = entry.GetValueOrDefault("toward");
        if (on is not (null or StoryletScopes.Ship or StoryletScopes.Subject or StoryletScopes.Target))
        {
            error("A storylet's trait change takes on: ship, subject, or target, or no on.");
            ok = false;
        }
        else if (toward is not null && (toward != StoryletScopes.Target || scope != StoryletScopes.Pair || on is not (null or StoryletScopes.Subject)))
        {
            // Nothing else in a storylet names a second crew member until casting (part four).
            error("toward: target is for a storylet for a pair, on its subject.");
            ok = false;
        }
        else if (ok && Ids.IsInnerId(trait) && (on ?? scope) is { } where)
        {
            ok &= book.CheckPlace(trait, toward is not null ? TraitPlaces.Pair : where == StoryletScopes.Ship ? TraitPlaces.Ship : TraitPlaces.Crew, error);
        }

        return ok ? new StoryletEffect(Trait: new TraitChange(trait, value, set, on ?? "", toward)) : null;
    }

    // Applies the option's trait changes on the storylet's own scope to the lowest values that satisfy its requirements, clamped to
    // each trait's cap as play clamps them. If every requirement still holds, the storylet would be offered again at once, and
    // every cycle after.
    private static bool LeavesEveryRequirementTrue(IReadOnlyList<Requirement> all, StoryletOption option, string scope, TraitBook book)
    {
        // Only traits: a stat or a fact is not the storylet's to move.
        List<TraitRange> requires = all.OfType<TraitRange>().ToList();
        // A pair's own traits are on its subject: the holder's, and the pair trait it holds toward the target.
        string own = scope == StoryletScopes.Ship ? StoryletScopes.Ship : StoryletScopes.Subject;
        Dictionary<string, int> values = requires.GroupBy(r => r.Trait).ToDictionary(g => g.Key, g => g.Max(r => r.Min));
        foreach (TraitChange change in option.Effects.Select(e => e.Trait).OfType<TraitChange>().Where(c => c.On == "" || c.On == own))
        {
            int current = values.GetValueOrDefault(change.Trait);
            values[change.Trait] = (book.Def(change.Trait) ?? TraitDef.Plain(change.Trait)).Clamp(change.Set ? change.Amount : current + change.Amount);
        }

        return requires.All(r => r.Holds(values.GetValueOrDefault(r.Trait)));
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
            bool pair = dto.Scope == StoryletScopes.Pair;
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
                // Only a storylet for a pair names its target; one for the ship names no one.
                BeatSpec scoped = ship ? spec with { People = [] } : pair ? spec : spec with { People = spec.People.Where(p => p != StoryletScopes.Target).ToList() };
                CheckScript(text, scoped, symbolsById, skillIds, m => Error($"cannot be read: {m}"), m => Error($"cannot be used: {m}"));
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
