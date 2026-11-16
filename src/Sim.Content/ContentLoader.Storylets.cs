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

            List<FlagRange> requires = Requirements(dto.Requires, Error);
            List<StoryletOption> options = Options(dto.Options, scope, Error);
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

    // Each entry names a flag and a range: min is 1 when left out, or 0 when only max is given; max has no limit when left out.
    private static List<FlagRange> Requirements(List<Dictionary<string, string>?>? written, Action<string> error)
    {
        var requires = new List<FlagRange>();
        if (written is not { Count: >= 1 and <= MaxRequirements })
        {
            error("A storylet needs one to four requirements.");
        }

        foreach (Dictionary<string, string> entry in (written ?? []).Select(e => e ?? []))
        {
            bool ok = true;
            foreach (string key in entry.Keys.Where(k => k is not ("flag" or "min" or "max")))
            {
                error($"A requirement takes flag, min, and max; '{key}' is not one of them.");
                ok = false;
            }

            string? flag = entry.GetValueOrDefault("flag");
            if (flag is null || !Ids.IsInnerId(flag))
            {
                error($"Flag id '{flag}' must be {Said(Ids.InnerRule)}");
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
                error($"The requirement on {flag} has max {high} below min {low}.");
                ok = false;
            }

            if (ok)
            {
                requires.Add(new FlagRange(flag!, low, max));
            }
        }

        return requires;
    }

    private static List<StoryletOption> Options(List<StoryletOptionDto?>? written, string? scope, Action<string> error)
    {
        var options = new List<StoryletOption>();
        if (written is not { Count: >= MinOptions and <= MaxOptions })
        {
            error("A storylet needs two to four options.");
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

    // One loyalty change or one flag change, or null when it is refused (each problem reported).
    private static StoryletEffect? StoryletEffectOf(Dictionary<string, string> entry, string? scope, Action<string> error)
    {
        bool loyalty = entry.ContainsKey("loyalty");
        bool flagged = entry.ContainsKey("flag");
        if (loyalty && flagged)
        {
            error("An effect changes loyalty or a flag, not both.");
            return null;
        }

        string[] keys = loyalty ? ["loyalty", "on"] : flagged ? ["flag", "add", "set", "on"] : [];
        bool ok = true;
        foreach (string key in entry.Keys.Where(k => !keys.Contains(k)))
        {
            error($"A storylet's effect can change loyalty or a flag; '{key}' is not one of them.");
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

        if (loyalty)
        {
            if (!int.TryParse(entry["loyalty"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
            {
                error("A loyalty change must be a whole number.");
                ok = false;
            }

            if (on is not (StoryletScopes.Subject or StoryletScopes.Crew))
            {
                error("A loyalty change needs on: subject or crew.");
                ok = false;
            }

            return ok ? new StoryletEffect(Loyalty: amount, LoyaltyOn: on) : null;
        }

        if (!flagged)
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
            error("A flag change needs add or set, not both.");
            ok = false;
        }
        else if (!int.TryParse(set ? setText : addText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error($"Unknown effect '{(set ? "set" : "add")}: {(set ? setText : addText)}'.");
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
    private static bool LeavesEveryRequirementTrue(IReadOnlyList<FlagRange> requires, StoryletOption option, string scope)
    {
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
    }

    private sealed class StoryletOptionDto
    {
        public string? Id { get; set; }

        public string? Label { get; set; }

        public List<Dictionary<string, string>?>? Effects { get; set; }

        public string? Text { get; set; }
    }
}
