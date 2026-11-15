using System.Globalization;
using System.Text.RegularExpressions;
using Sim.Core.Content;
using Sim.Core.Narrative;

namespace Sim.Content;

// Lines, symbols, and pronoun sets (slice 1d), and the text an encounter or an objective carries for its own beats, which becomes
// lines too. All of it is optional: with no lines the game shows its systems lines.
public static partial class ContentLoader
{
    // Roles a token can name; a symbol by one of these names would never be reached.
    private static readonly HashSet<string> Roles = NarrativeFacts.Beats.Values.SelectMany(b => b.People.Concat(b.Things)).ToHashSet(StringComparer.Ordinal);

    private static List<PronounSet> BuildPronouns(List<(string File, PronounDto Dto)> items, List<ContentError> errors)
    {
        var sets = new List<PronounSet>();
        foreach ((string file, PronounDto dto) in items)
        {
            int before = errors.Count;
            string Form(string key, string? value)
            {
                if (string.IsNullOrWhiteSpace(value) || value.Trim() != value || value.IndexOfAny(['\r', '\n']) >= 0)
                {
                    errors.Add(new ContentError(file, $"{dto.Id}: {key} must be one word or phrase on one line, with no spaces at either end."));
                    return "";
                }

                return value;
            }

            string subject = Form("subject", dto.Subject);
            string obj = Form("object", dto.Object);
            string possessive = Form("possessive", dto.Possessive);
            string possessivePronoun = Form("possessivePronoun", dto.PossessivePronoun);
            string reflexive = Form("reflexive", dto.Reflexive);
            string? name = CheckName(file, dto.Id!, dto.Name, errors);
            if (errors.Count == before)
            {
                sets.Add(new PronounSet(dto.Id!, subject, obj, possessive, possessivePronoun, reflexive, dto.Plural, name));
            }
        }

        return sets;
    }

    private static List<SymbolDef> BuildSymbols(List<(string File, SymbolDto Dto)> items, List<ContentError> errors)
    {
        var symbols = new List<SymbolDef>();
        foreach ((string file, SymbolDto dto) in items)
        {
            int before = errors.Count;
            if (Roles.Contains(dto.Id!))
            {
                errors.Add(new ContentError(file, $"{dto.Id}: a symbol cannot be called '{dto.Id}', which names a person or thing in lines."));
            }

            List<string?> options = dto.Options ?? [];
            if (options.Count == 0)
            {
                errors.Add(new ContentError(file, $"{dto.Id}: options must list at least one option."));
            }

            for (int i = 0; i < options.Count; i++)
            {
                if (string.IsNullOrEmpty(options[i]))
                {
                    errors.Add(new ContentError(file, $"{dto.Id}: option {i + 1} is blank."));
                }
                else if (Tokens(options[i]!, out string? problem) is null)
                {
                    errors.Add(new ContentError(file, $"{dto.Id}: option {i + 1}: {problem}"));
                }
            }

            string? name = CheckName(file, dto.Id!, dto.Name, errors);
            if (errors.Count == before)
            {
                symbols.Add(new SymbolDef(dto.Id!, options.Select(o => o!).ToList(), name));
            }
        }

        // A symbol that reaches itself would expand forever.
        Dictionary<string, SymbolDef> byId = symbols.ToDictionary(s => s.Id);
        foreach (SymbolDef symbol in symbols)
        {
            if (Reaches(symbol.Id, symbol.Id, byId, []))
            {
                string file = items.First(i => i.Dto.Id == symbol.Id).File;
                errors.Add(new ContentError(file, $"{symbol.Id}: the symbol uses itself, directly or through other symbols."));
            }
        }

        return symbols;
    }

    private static bool Reaches(string from, string target, Dictionary<string, SymbolDef> symbols, HashSet<string> seen)
    {
        if (!symbols.TryGetValue(from, out SymbolDef? symbol) || !seen.Add(from))
        {
            return false;
        }

        foreach (Token token in symbol.Options.SelectMany(o => Tokens(o, out _) ?? []))
        {
            if (token.Head == target || Reaches(token.Head, target, symbols, seen))
            {
                return true;
            }
        }

        return false;
    }

    // The lines of lines/, and the lines written inside an encounter or an objective. A line written inside an item holds only
    // for that item: the item is one more criterion, implied by where the line sits, so it outranks a shared line that tests the same things.
    private static List<LineDef> BuildLines(
        List<(string File, LineDto Dto)> items,
        List<(string File, EncounterDto Dto)> encounterItems,
        List<(string File, ObjectiveDto Dto)> objectiveItems,
        List<SymbolDef> symbols,
        List<SkillDef> skills,
        List<EncounterDef> encounters,
        List<ObjectiveDef> objectives,
        List<ContentError> errors)
    {
        Dictionary<string, SymbolDef> symbolsById = symbols.ToDictionary(s => s.Id);
        var domains = new Dictionary<FactDomain, HashSet<string>>
        {
            [FactDomain.Skill] = skills.Select(s => s.Id).ToHashSet(),
            [FactDomain.Encounter] = encounters.Select(e => e.Id).ToHashSet(),
            [FactDomain.Objective] = objectives.Select(o => o.Id).ToHashSet(),
            [FactDomain.Decision] = encounters.Where(e => e.Decision is not null).Select(e => e.Decision!.Id).Append(DecisionIds.Abort).ToHashSet(),
        };

        var lines = new List<LineDef>();
        void Build(string file, string label, string id, LineDto dto, CriterionDef? implied, string? owner)
        {
            int before = errors.Count;
            void Error(string message)
            {
                errors.Add(new ContentError(file, $"{label}: {message}"));
            }

            if (dto.Use is not null)
            {
                Error("use is no longer read: one line serves both the log and the report. Remove it.");
            }

            Dictionary<string, object?> when = dto.When ?? [];
            string? beat = when.GetValueOrDefault("beat") as string;
            if (beat is null)
            {
                Error($"when must name a beat, one of {string.Join(", ", NarrativeFacts.Kinds)}.");
            }
            else if (!NarrativeFacts.Beats.ContainsKey(beat))
            {
                Error($"unknown beat '{beat}'; it must be one of {string.Join(", ", NarrativeFacts.Kinds)}.");
                beat = null;
            }

            var criteria = new List<CriterionDef>();
            if (implied is not null)
            {
                criteria.Add(implied);
            }

            foreach ((string fact, object? value) in when.Where(w => w.Key != "beat"))
            {
                if (beat is null)
                {
                    break;
                }

                if (fact == implied?.Fact)
                {
                    Error($"{fact} is already {implied.Values[0]}, because the line is written inside it. Remove it.");
                    continue;
                }

                if (NarrativeFacts.Fact(beat, fact) is not { } spec)
                {
                    Error($"a {beat} beat has no fact '{fact}'.");
                    continue;
                }

                if (Criterion(fact, spec, value, domains, Error) is { } criterion)
                {
                    criteria.Add(criterion);
                }
            }

            if (string.IsNullOrEmpty(dto.Text))
            {
                Error("text must not be empty.");
            }
            else if (Tokens(dto.Text, out string? problem) is not { } tokens)
            {
                Error(problem!);
            }
            else if (beat is not null)
            {
                CheckTokens(tokens, NarrativeFacts.Beats[beat], symbolsById, null, [], Error);
            }

            string? name = owner is null ? CheckName(file, id, dto.Name, errors) : null;
            if (owner is not null && (dto.Id is not null || dto.Name is not null))
            {
                Error("a line written inside an item takes no id or name.");
            }

            if (errors.Count == before)
            {
                lines.Add(new LineDef(id, beat!, criteria, dto.Text!, name, owner));
            }
        }

        foreach ((string file, LineDto dto) in items)
        {
            Build(file, dto.Id!, dto.Id!, dto, null, null);
        }

        void Inside(string file, string owner, string fact, List<LineDto?>? own)
        {
            for (int i = 0; i < (own?.Count ?? 0); i++)
            {
                if (own![i] is { } dto)
                {
                    Build(file, $"{owner}: lines[{i + 1}]", $"{owner}.lines[{i + 1}]", dto, new CriterionDef(fact, [owner]), owner);
                }
                else
                {
                    errors.Add(new ContentError(file, $"{owner}: lines[{i + 1}] is blank."));
                }
            }
        }

        foreach ((string file, EncounterDto dto) in encounterItems)
        {
            Inside(file, dto.Id!, "encounter", dto.Lines);
        }

        foreach ((string file, ObjectiveDto dto) in objectiveItems)
        {
            Inside(file, dto.Id!, "objective", dto.Lines);
        }

        return lines;
    }

    // The words written inside an encounter or an objective, as lines whose criteria are implied by where they sit: an encounter's
    // arrival holds for its stage beat, an approach's tier text for that check, a decision's texts for its call, its notice, and
    // the effects of each choice, and an objective's texts for its result. They join the shared pool, so salience ranks them with
    // the lines of lines/, and their implied criteria count like written ones.
    private static List<LineDef> BuildOwnedLines(
        List<(string File, EncounterDto Dto)> encounters,
        List<(string File, ObjectiveDto Dto)> objectives,
        List<SymbolDef> symbols,
        List<ContentError> errors)
    {
        Dictionary<string, SymbolDef> symbolsById = symbols.ToDictionary(s => s.Id);
        var lines = new List<LineDef>();
        void Add(string file, string owner, string where, Dictionary<string, string?>? texts, IReadOnlyList<string> keys, string beat, Func<string, CriterionDef[]> when)
        {
            foreach ((string key, string? text) in texts ?? [])
            {
                string at = $"{owner}: {where}text.{key}";
                int before = errors.Count;
                void Error(string message)
                {
                    errors.Add(new ContentError(file, $"{at} {message}"));
                }

                if (!keys.Contains(key))
                {
                    Error($"is not a text this can have; it must be one of {string.Join(", ", keys)}.");
                }
                else if (string.IsNullOrEmpty(text))
                {
                    Error("must not be empty.");
                }
                else if (Tokens(text, out string? problem) is not { } tokens)
                {
                    Error($"cannot be read: {problem}");
                }
                else
                {
                    string kind = key is "call" or "notice" ? key : beat;
                    CheckTokens(tokens, NarrativeFacts.Beats[kind], symbolsById, null, [], m => Error($"cannot be used: {m}"));
                    if (errors.Count == before)
                    {
                        lines.Add(new LineDef($"{owner}.{where}{key}", kind, when(key), text, null, owner));
                    }
                }
            }
        }

        foreach ((string file, EncounterDto dto) in encounters)
        {
            string id = dto.Id!;
            var here = new CriterionDef("encounter", [id]);
            Add(file, id, "", dto.Text, NarrativeFacts.EncounterTexts, "stage", _ => [here]);
            foreach ((string approach, ApproachDto? def) in new[] { ("careful", dto.Approaches?.Careful), ("fast", dto.Approaches?.Fast) })
            {
                Add(file, id, approach + ".", def?.Text, NarrativeFacts.Tiers, "check", tier => [here, new CriterionDef("approach", [approach]), new CriterionDef("tier", [tier])]);
            }

            if (dto.Decision is { Id: { } decision })
            {
                var that = new CriterionDef("decision", [decision]);
                Add(file, id, "decision.", dto.Decision.Text, NarrativeFacts.DecisionTexts, "effects", key => key is "call" or "notice" ? [that] : [that, new CriterionDef("chose", [key])]);
            }
        }

        foreach ((string file, ObjectiveDto dto) in objectives)
        {
            string id = dto.Id!;
            Add(file, id, "", dto.Text, NarrativeFacts.Outcomes, "result", outcome => [new CriterionDef("objective", [id]), new CriterionDef("outcome", [outcome])]);
        }

        return lines;
    }

    // A value as written: one value, a list of values (any of), or a range { min, max } on a number.
    private static CriterionDef? Criterion(string fact, FactSpec spec, object? value, Dictionary<FactDomain, HashSet<string>> domains, Action<string> error)
    {
        if (value is Dictionary<object, object> map)
        {
            if (spec.Type != FactType.Number)
            {
                error($"{fact} is not a number, so it cannot take a range.");
                return null;
            }

            int? Bound(string key)
            {
                if (!map.TryGetValue(key, out object? raw))
                {
                    return null;
                }

                if (raw is string text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                {
                    return n;
                }

                error($"{fact}.{key} must be a whole number.");
                return null;
            }

            if (map.Keys.Any(k => k is not ("min" or "max")) || map.Count == 0)
            {
                error($"{fact} as a range takes min, max, or both, and nothing else.");
                return null;
            }

            int? min = Bound("min");
            int? max = Bound("max");
            if (min is { } lo && max is { } hi && lo > hi)
            {
                error($"{fact}: min {lo} is above max {hi}.");
                return null;
            }

            return new CriterionDef(fact, [], min, max);
        }

        List<object?> raws = value is List<object> list ? list.Cast<object?>().ToList() : [value];
        var values = new List<string>();
        foreach (object? raw in raws)
        {
            if (raw is not string text || text.Length == 0)
            {
                error($"{fact} takes a value, a list of values, or (for a number) a range.");
                return null;
            }

            switch (spec.Type)
            {
                case FactType.Number when !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _):
                    error($"{fact} is a number, not '{text}'.");
                    return null;
                case FactType.Number:
                    values.Add(int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                    break;
                case FactType.Flag when ContentReader.IsBool(text) is not { } flag:
                    error($"{fact} is true or false, not '{text}'.");
                    return null;
                case FactType.Flag:
                    values.Add(ContentReader.IsBool(text)!.Value ? "true" : "false");
                    break;
                default:
                    if (spec.Domain == FactDomain.Words && !spec.Words!.Contains(text))
                    {
                        error($"{fact} is one of {string.Join(", ", spec.Words!)}, not '{text}'.");
                        return null;
                    }

                    if (domains.TryGetValue(spec.Domain, out HashSet<string>? known) && !known.Contains(text))
                    {
                        error($"{fact} names '{text}', which the pack does not have.");
                        return null;
                    }

                    values.Add(text);
                    break;
            }
        }

        return new CriterionDef(fact, values);
    }

    // Every token must name a person or thing the beat has, or a symbol; pronouns and verbs need a person. Symbols are walked
    // too, against the same beat, so a symbol used by a stage line cannot mention a helper.
    private static void CheckTokens(IEnumerable<Token> tokens, BeatSpec beat, Dictionary<string, SymbolDef> symbols, string? via, HashSet<string> walked, Action<string> error)
    {
        string where = via is null ? "" : $"symbol '{via}' ";
        foreach (Token token in tokens)
        {
            bool person = beat.People.Contains(token.Head);
            if ((token.Form is not null || token.Singular is not null) && !person)
            {
                error($"{where}#{token.Raw}# needs a person, and a {beat.Kind} beat has no '{token.Head}' among {string.Join(", ", beat.People)}.");
            }
            else if (person || beat.Things.Contains(token.Head))
            {
                continue;
            }
            else if (symbols.TryGetValue(token.Head, out SymbolDef? symbol))
            {
                if (walked.Add(symbol.Id))
                {
                    CheckTokens(symbol.Options.SelectMany(o => Tokens(o, out _) ?? []), beat, symbols, symbol.Id, walked, error);
                }
            }
            else if (Roles.Contains(token.Head))
            {
                error($"{where}#{token.Raw}# names '{token.Head}', which a {beat.Kind} beat does not have.");
            }
            else
            {
                error($"{where}#{token.Raw}# names no person, thing, or symbol.");
            }
        }
    }

    private static List<Token>? Tokens(string text, out string? problem)
    {
        try
        {
            problem = null;
            return NarrativeText.Parse(text).Where(p => p.Token is not null).Select(p => p.Token!).ToList();
        }
        catch (NarrativeFormatException e)
        {
            problem = e.Message;
            return null;
        }
    }

    // Symbols and pronoun sets are named in text, so their ids are lower case: #hesitates#, pronouns: they.
    private static bool CheckLowerId(string file, string? id, List<ContentError> errors)
    {
        if (id is not null && LowerIdPattern().IsMatch(id))
        {
            return true;
        }

        errors.Add(new ContentError(file, $"Id '{id}' must start with a lower case letter and hold only letters, digits, and underscores."));
        return false;
    }

    [GeneratedRegex("^[a-z][A-Za-z0-9_]*$")]
    private static partial Regex LowerIdPattern();

    internal sealed class LineDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Use { get; set; }

        public Dictionary<string, object?>? When { get; set; }

        public string? Text { get; set; }
    }

    private sealed class SymbolDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public List<string?>? Options { get; set; }
    }

    private sealed class PronounDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Subject { get; set; }

        public string? Object { get; set; }

        public string? Possessive { get; set; }

        public string? PossessivePronoun { get; set; }

        public string? Reflexive { get; set; }

        public bool Plural { get; set; }
    }
}
