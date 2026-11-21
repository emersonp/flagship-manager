using System.Globalization;
using System.Text.RegularExpressions;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Core.Narrative;

namespace Web.Forms;

// The parts of a storylet's form that a vignette's will share (1e part six, design 4.3, 4.4, and 4.8): the cast, effect rows, options
// as tabs, the words with their chips, and the notes. Every list nested in a row (a slot's clauses, an option's requires and effects)
// is posted under that row and carried with it (RowSet.Carry), so adding, removing, or moving a slot or an option keeps what is in it.
internal static partial class StoryletParts
{
    // A slot's name; its clauses post under it, as cast[0].when.
    public static readonly RowSet Cast = new("cast", ["name"], Qualified: true, Noun: "slot", Carry: true);

    public const string ByEffects = "effects";

    public const string ByCheck = "check";

    // An option's own fields. saved is "true" for an option the file already holds, whose id is fixed. settled is ByEffects or ByCheck.
    // Its requires (options[0].requires), its effects (options[0].effects[0].what), and a check's outcomes' effects
    // (options[0].success.effects[0].what) post under its row and are carried with it.
    public static readonly RowSet Options = new(
        "options",
        ["id", "saved", "label", "settled", "skill", "difficulty", "text", "success.text", "failure.text", "chronicleWeight", "chronicleText"],
        Qualified: true,
        Noun: "option",
        Defaults: new Dictionary<string, string> { ["settled"] = ByEffects },
        Carry: true);

    public static readonly IReadOnlyList<string> Outcomes = ["success", "failure"];

    // One effect (design 4.4): what it changes (loyalty, morale, or trait), the trait, add or set, the amount (a number, or a level's
    // name for a set), on, and toward. Blank on and toward are absent, as the loader reads them.
    public static readonly IReadOnlyList<string> EffectFields = ["what", "trait", "mode", "amount", "on", "toward"];

    public static readonly IReadOnlyList<Choice> Whats = [new("loyalty", "Loyalty"), new("morale", "Morale"), new("trait", "Trait")];

    // The posted field that says which option's tab the page shows, as on the encounter form; only the page reads it.
    public const string TabField = EncounterKind.TabField;

    public const string TextRule = "Write - alone for an answer that passes in silence.";

    public static RowSet EffectSet(string prefix)
    {
        return new RowSet(prefix, EffectFields, Qualified: true, Noun: "effect");
    }

    public static string CastPrefix(int slot)
    {
        return Cast.Name(slot, "when");
    }

    public static string OptionRequires(int option)
    {
        return Options.Name(option, "requires");
    }

    public static RowSet OptionEffects(int option)
    {
        return EffectSet(Options.Name(option, "effects"));
    }

    public static RowSet OutcomeEffects(int option, string outcome)
    {
        return EffectSet(Options.Name(option, $"{outcome}.effects"));
    }

    // The slot names as posted, in order, blank and repeated names left out: the slots an expression may read, as the loader has them.
    public static List<string> SlotNames(FormValues values)
    {
        return FormRows.Read(values, Cast).Select(r => r["name"].Trim()).Where(n => n.Length > 0).Distinct().ToList();
    }

    // Each slot's widget, which reads the slots before it and itself.
    public static List<ExpressionScope> CastScopes(FormValues values)
    {
        List<Dictionary<string, string>> rows = FormRows.Read(values, Cast);
        var scopes = new List<ExpressionScope>();
        var names = new List<string>();
        for (int k = 0; k < rows.Count; k++)
        {
            string name = rows[k]["name"].Trim();
            if (name.Length > 0 && !names.Contains(name))
            {
                names.Add(name);
            }

            scopes.Add(new ExpressionScope(CastPrefix(k), name.Length > 0 ? $"Who can be {name}" : $"Who can fill slot {k + 1}", names.ToList()));
        }

        return scopes;
    }

    // Each option's requires, which reads every slot.
    public static List<ExpressionScope> OptionScopes(FormValues values, IReadOnlyList<string> slots)
    {
        return FormRows.Read(values, Options).Select((row, i) => new ExpressionScope(OptionRequires(i), $"Requires, {OptionTitle(row, i)}", slots)).ToList();
    }

    // The option as the author knows it: its label, its id, or its place.
    public static string OptionTitle(IReadOnlyDictionary<string, string> row, int index)
    {
        string label = row.GetValueOrDefault("label", "").Trim();
        string id = row.GetValueOrDefault("id", "").Trim();
        return label.Length > 0 ? label : id.Length > 0 ? id : $"Option {index + 1}";
    }

    // Every option's effect rows, and its check's outcomes', for the fields as posted.
    public static List<RowSet> EffectSets(FormValues values)
    {
        int count = FormRows.Read(values, Options).Count;
        return Enumerable.Range(0, count).SelectMany(i => new[] { OptionEffects(i), OutcomeEffects(i, "success"), OutcomeEffects(i, "failure") }).ToList();
    }

    public static void AddCast(List<KeyValuePair<string, string>> fields, IReadOnlyList<CastSlotModel> cast, ExpressionChoices choices)
    {
        var names = new List<string>();
        for (int k = 0; k < cast.Count; k++)
        {
            fields.Add(new(Cast.Name(k, "name"), cast[k].Name));
            names.Add(cast[k].Name);
            fields.AddRange(ExpressionField.Load(new ExpressionScope(CastPrefix(k), cast[k].Name, names.ToList()), cast[k].Condition, choices));
        }
    }

    // The slots, each a name the loader takes and at least one clause.
    public static List<CastSlotModel> BindCast(FormValues values, ExpressionChoices choices, Dictionary<string, string> errors)
    {
        List<Dictionary<string, string>> rows = FormRows.Read(values, Cast);
        if (rows.Count > StoryletScopes.MaxSlots)
        {
            errors[Cast.Prefix] = "A cast is at most three slots.";
        }

        List<ExpressionScope> scopes = CastScopes(values);
        var cast = new List<CastSlotModel>();
        var seen = new HashSet<string>();
        for (int k = 0; k < rows.Count; k++)
        {
            string field = Cast.Name(k, "name");
            string name = rows[k]["name"].Trim();
            if (name.Length == 0)
            {
                errors[field] = "Enter the slot's name.";
            }
            else if (!Ids.IsInnerId(name))
            {
                errors[field] = Ids.InnerRule;
            }
            else if (StoryletScopes.Reserved.Contains(name))
            {
                errors[field] = $"{name} means something already; a slot cannot be called {string.Join(", ", StoryletScopes.Reserved.SkipLast(1))}, or {StoryletScopes.Reserved[^1]}.";
            }
            else if (!seen.Add(name))
            {
                errors[field] = "Each slot has its own name.";
            }

            int before = errors.Count;
            string? condition = ExpressionField.Bind(values, scopes[k], choices, errors);
            if (condition is null && errors.Count == before)
            {
                errors[scopes[k].Prefix] = "A slot needs a clause: who can fill it. “Aboard or fit”, is aboard, holds for anyone aboard.";
            }

            cast.Add(new CastSlotModel(name, condition ?? ""));
        }

        return cast;
    }

    public static void AddEffects(List<KeyValuePair<string, string>> fields, RowSet set, IReadOnlyList<StoryletEffectModel> effects)
    {
        for (int i = 0; i < effects.Count; i++)
        {
            StoryletEffectModel effect = effects[i];
            fields.Add(new(set.Name(i, "what"), effect.What));
            fields.Add(new(set.Name(i, "trait"), effect.Trait ?? ""));
            fields.Add(new(set.Name(i, "mode"), effect.What == "trait" ? effect.Set ? "set" : "add" : ""));
            fields.Add(new(set.Name(i, "amount"), effect.Amount is { } amount ? Checks.Text(amount) : effect.Level ?? ""));
            fields.Add(new(set.Name(i, "on"), effect.On ?? ""));
            fields.Add(new(set.Name(i, "toward"), effect.Toward ?? ""));
        }
    }

    // Whom an effect may land on (design 4.4): loyalty and morale on crew or a slot; a trait on a slot or the ship, as its on allows,
    // or blank for the first slot (the ship when there is no cast). handler is offered only in a check's success and failure.
    public static List<Choice> OnChoices(string what, TraitChoice? trait, IReadOnlyList<string> slots, bool handler)
    {
        List<Choice> people = [.. slots.Select(s => new Choice(s, s)), .. handler ? [new Choice(StoryletScopes.Handler, StoryletScopes.Handler)] : Array.Empty<Choice>()];
        if (what is "loyalty" or "morale")
        {
            return [new Choice(StoryletScopes.Crew, "the whole crew"), .. people];
        }

        bool onCrew = trait is null || trait.OnCrew || trait.OnPair;
        bool onShip = trait is null || trait.OnShip;
        return [.. onCrew ? people : [], .. onShip ? [new Choice(StoryletScopes.Ship, "the ship")] : Array.Empty<Choice>()];
    }

    // What a blank on means for a trait: the first slot, or the ship when there is no cast.
    public static string DefaultOn(IReadOnlyList<string> slots)
    {
        return slots.Count > 0 ? $"{slots[0]}, the first slot" : "the ship";
    }

    public static List<Choice> TowardChoices(IReadOnlyList<string> slots, bool handler)
    {
        return [.. slots.Select(s => new Choice(s, s)), .. handler ? [new Choice(StoryletScopes.Handler, StoryletScopes.Handler)] : Array.Empty<Choice>()];
    }

    // A trait with named levels is set, by name or to 0; any other is added to or set.
    public static List<Choice> ModeChoices(TraitChoice? trait)
    {
        return trait is { Levels.Count: > 0 } ? [new("set", "set to")] : [new("add", "add"), new("set", "set to")];
    }

    // The values a trait with named levels is set to: 0 (not held) and each level by name.
    public static List<Choice>? LevelChoices(TraitChoice? trait, string mode)
    {
        return trait is { Levels.Count: > 0 } && mode == "set" ? [new("0", "0, not held"), .. trait.Levels.OrderBy(l => l.Key).Select(l => new Choice(l.Value, $"{l.Value} ({l.Key})"))] : null;
    }

    // The effects the rows hold, each as the loader reads it; a problem is put beside its part.
    public static List<StoryletEffectModel> BindEffects(FormValues values, RowSet set, IReadOnlyList<string> slots, bool handler, ExpressionChoices choices, Dictionary<string, string> errors)
    {
        var effects = new List<StoryletEffectModel>();
        List<Dictionary<string, string>> rows = FormRows.Read(values, set);
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i].ToDictionary(p => p.Key, p => p.Value.Trim());
            string Field(string part) => set.Name(i, part);
            string what = row["what"];
            string on = row["on"];
            if (what is "loyalty" or "morale")
            {
                int amount = Checks.Whole(values, Field("amount"), null, null, errors);
                if (on.Length == 0)
                {
                    errors[Field("on")] = "Choose who it changes.";
                }
                else if (OnChoices(what, null, slots, handler).All(c => c.Value != on))
                {
                    errors[Field("on")] = "Choose the whole crew or a slot.";
                }

                effects.Add(new StoryletEffectModel(what, amount, On: on.Length == 0 ? null : on));
                continue;
            }

            if (what != "trait")
            {
                errors[Field("what")] = "Choose what it changes.";
                continue;
            }

            string traitId = row["trait"];
            TraitChoice? trait = choices.Trait(traitId);
            if (traitId.Length == 0)
            {
                errors[Field("trait")] = "Choose a trait.";
            }
            else if (!Ids.IsInnerId(traitId))
            {
                errors[Field("trait")] = Ids.InnerRule;
            }

            string mode = row["mode"];
            if (mode is not ("add" or "set"))
            {
                errors[Field("mode")] = "Choose add or set.";
            }
            else if (mode == "add" && trait is { Levels.Count: > 0 })
            {
                errors[Field("mode")] = $"{traitId} has named levels, so it is set, not added to.";
            }

            string text = row["amount"];
            int? number = null;
            string? level = null;
            if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n))
            {
                number = n;
                if (mode == "set" && n < 0)
                {
                    errors[Field("amount")] = "A trait is set to 0 or more.";
                }
            }
            else if (mode == "set" && text.Length > 0 && (trait is null ? Ids.IsInnerId(text) : trait.Levels.Values.Contains(text)))
            {
                level = text;
            }
            else
            {
                errors[Field("amount")] = trait is { Levels.Count: > 0 } ? "Choose a level." : "Enter a whole number.";
            }

            if (on.Length > 0 && OnChoices(what, trait, slots, handler).All(c => c.Value != on))
            {
                errors[Field("on")] = trait?.OnShip == true && !trait.OnCrew ? "This trait is held by the ship." : "Choose a slot, or leave it blank for the first.";
            }

            string toward = row["toward"];
            if (toward.Length > 0 && TowardChoices(slots, handler).All(c => c.Value != toward))
            {
                errors[Field("toward")] = "Choose a slot.";
            }
            else if (toward.Length == 0 && trait is { OnPair: true })
            {
                errors[Field("toward")] = $"{traitId} is held toward someone: choose whom.";
            }

            effects.Add(new StoryletEffectModel("trait", number, traitId, mode == "set", level, on.Length == 0 ? null : on, toward.Length == 0 ? null : toward));
        }

        return effects;
    }

    // A row whose what, trait, or add-or-set was just changed, made to fit: what a loyalty or morale change does not have is cleared, and
    // a trait's add or set, amount, and toward follow from it. Null when the focus is not in an effect row.
    public static FormValues Settle(FormValues values, IReadOnlyList<RowSet> sets, IReadOnlyList<string> slots, ExpressionChoices choices, string? focus)
    {
        if (focus is null)
        {
            return values;
        }

        foreach (RowSet set in sets)
        {
            List<Dictionary<string, string>> rows = FormRows.Read(values, set);
            for (int i = 0; i < rows.Count; i++)
            {
                if (!new[] { "what", "trait", "mode" }.Any(p => FormValues.Slug(set.Name(i, p)) == focus))
                {
                    continue;
                }

                Dictionary<string, string> row = rows[i];
                bool whole = int.TryParse(row["amount"].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
                if (row["what"] is "loyalty" or "morale")
                {
                    row["trait"] = "";
                    row["mode"] = "";
                    row["toward"] = "";
                    row["amount"] = whole ? row["amount"].Trim() : "1";
                    if (row["on"].Length == 0)
                    {
                        row["on"] = StoryletScopes.Crew;
                    }
                }
                else if (row["what"] == "trait" && choices.Trait(row["trait"]) is { } trait)
                {
                    List<Choice> modes = ModeChoices(trait);
                    if (modes.All(m => m.Value != row["mode"]))
                    {
                        row["mode"] = modes[0].Value;
                    }

                    if (LevelChoices(trait, row["mode"]) is { } levels)
                    {
                        row["amount"] = levels.Any(l => l.Value == row["amount"].Trim()) ? row["amount"].Trim() : levels[^1].Value;
                    }
                    else if (!whole)
                    {
                        row["amount"] = "1";
                    }

                    if (row["on"] == StoryletScopes.Crew || (row["on"].Length > 0 && OnChoices("trait", trait, slots, handler: true).All(c => c.Value != row["on"])))
                    {
                        row["on"] = "";
                    }

                    row["toward"] = trait.OnPair ? slots.Contains(row["toward"]) || row["toward"] == StoryletScopes.Handler ? row["toward"] : slots.Skip(1).FirstOrDefault() ?? "" : "";
                }
                else if (row["what"] == "trait" && row["on"] == StoryletScopes.Crew)
                {
                    row["on"] = "";
                }

                return FormRows.Write(values, set, rows);
            }
        }

        return values;
    }

    // vignette: a vignette's options, which have no requires, no check, and no chronicle (design 4.8).
    public static void AddOptions(List<KeyValuePair<string, string>> fields, IReadOnlyList<StoryletOptionModel> options, IReadOnlyList<string> slots, ExpressionChoices choices, bool vignette = false)
    {
        for (int i = 0; i < options.Count; i++)
        {
            StoryletOptionModel option = options[i];
            fields.Add(new(Options.Name(i, "id"), option.Id));
            fields.Add(new(Options.Name(i, "saved"), "true"));
            fields.Add(new(Options.Name(i, "label"), option.Label));
            if (!vignette)
            {
                fields.AddRange(ExpressionField.Load(new ExpressionScope(OptionRequires(i), "", slots), option.Requires, choices));
            }

            if (option.Check is { } check)
            {
                fields.Add(new(Options.Name(i, "settled"), ByCheck));
                fields.Add(new(Options.Name(i, "skill"), check.Skill));
                fields.Add(new(Options.Name(i, "difficulty"), Checks.Text(check.Difficulty)));
                foreach ((string outcome, StoryletOutcomeModel? written) in new[] { ("success", check.Success), ("failure", check.Failure) })
                {
                    fields.Add(new(Options.Name(i, $"{outcome}.text"), written?.Text ?? ""));
                    AddEffects(fields, OutcomeEffects(i, outcome), written?.Effects ?? []);
                }
            }
            else
            {
                fields.Add(new(Options.Name(i, "settled"), ByEffects));
                fields.Add(new(Options.Name(i, "text"), option.Text ?? ""));
                AddEffects(fields, OptionEffects(i), option.Effects);
            }

            if (vignette)
            {
                continue;
            }

            fields.Add(new(Options.Name(i, "chronicleWeight"), option.Chronicle?.Weight is { } weight ? Checks.Text(weight) : ""));
            fields.Add(new(Options.Name(i, "chronicleText"), option.Chronicle?.Text ?? ""));
        }
    }

    // A new option, blank but for how it is settled.
    public static void AddBlankOption(List<KeyValuePair<string, string>> fields, int index, bool vignette = false)
    {
        foreach ((string field, string value) in Options.NewRow())
        {
            fields.Add(new(Options.Name(index, field), value));
        }

        if (vignette)
        {
            return;
        }

        fields.Add(new(ExpressionField.ModeField(OptionRequires(index)), ExpressionField.RowsMode));
    }

    // The options, each as the loader reads it. A saved option's id is fixed (review focus 5); a new one left blank takes its id from its
    // label, passing over any id another option already has. countRule is refused when the count is outside min to max. A vignette's
    // options (vignette) are settled by their effects, with no requires and no chronicle, whatever is posted.
    public static List<StoryletOptionModel> BindOptions(FormValues values, FormChoices choices, IReadOnlyList<string> slots, Dictionary<string, string> errors, int min, int max, string countRule, bool vignette = false)
    {
        List<Dictionary<string, string>> rows = FormRows.Read(values, Options);
        if (rows.Count < min || rows.Count > max)
        {
            errors[Options.Prefix] = countRule;
        }

        HashSet<string> given = rows.Select(r => r["id"].Trim()).Where(i => i.Length > 0).ToHashSet();
        var ids = new HashSet<string>();
        var options = new List<StoryletOptionModel>();
        List<ExpressionScope> scopes = OptionScopes(values, slots);
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            string Field(string part) => Options.Name(i, part);
            string label = row["label"].Trim();
            if (label.Length == 0)
            {
                errors[Field("label")] = "Enter a label: the words on the answer's button.";
            }
            else if (label.Length > 40 || label.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029']) >= 0)
            {
                errors[Field("label")] = "A label is 1 to 40 characters on one line.";
            }

            string id = row["id"].Trim();
            if (row["saved"] == "true")
            {
                if (!choices.FixedOptionIds.Contains(id))
                {
                    errors[Field("id")] = "An option's id is fixed once it is saved.";
                }
            }
            else if (id.Length == 0)
            {
                id = Free(NewIds.FromName(label), given);
                if (id.Length == 0)
                {
                    errors[Field("id")] = "Enter an id, or a label to make one from.";
                }

                given.Add(id);
            }

            if (id.Length > 0 && !Ids.IsInnerId(id))
            {
                errors[Field("id")] = Ids.InnerRule;
            }
            else if (id.Length > 0 && !ids.Add(id))
            {
                errors[Field("id")] = "Each option has its own id.";
            }

            if (vignette)
            {
                List<StoryletEffectModel> own = BindEffects(values, OptionEffects(i), slots, handler: false, choices.Expressions, errors);
                options.Add(new StoryletOptionModel(id, label, null, own, null, BindText(values, Field("text"), errors)));
                continue;
            }

            string? requires = ExpressionField.Bind(values, scopes[i], choices.Expressions, errors);
            int? weight = Checks.OptionalWhole(values, Field("chronicleWeight"), 0, ChronicleTuning.Most, errors);
            string? chronicleText = BindText(values, Field("chronicleText"), errors);
            ChronicleModel? chronicle = weight is null && chronicleText is null ? null : new ChronicleModel(weight, chronicleText);
            if (row["settled"] == ByCheck)
            {
                string skill = row["skill"].Trim();
                if (skill.Length == 0 || (choices.Skills.Count > 0 && choices.Skills.All(s => s.Id != skill)))
                {
                    errors[Field("skill")] = "Choose a skill.";
                }

                int difficulty = Checks.Whole(values, Field("difficulty"), 1, null, errors);
                StoryletOutcomeModel Outcome(string outcome) => new(BindEffects(values, OutcomeEffects(i, outcome), slots, handler: true, choices.Expressions, errors), BindText(values, Field($"{outcome}.text"), errors));
                options.Add(new StoryletOptionModel(id, label, requires, [], new StoryletCheckModel(skill, difficulty, Outcome("success"), Outcome("failure")), null, chronicle));
            }
            else
            {
                List<StoryletEffectModel> effects = BindEffects(values, OptionEffects(i), slots, handler: false, choices.Expressions, errors);
                options.Add(new StoryletOptionModel(id, label, requires, effects, null, BindText(values, Field("text"), errors), chronicle));
            }
        }

        return options;
    }

    // The id, or the id with _2, _3, and so on, until it is not one already given.
    private static string Free(string id, HashSet<string> given)
    {
        if (id.Length == 0 || !given.Contains(id))
        {
            return id;
        }

        for (int n = 2; ; n++)
        {
            string suffix = "_" + n.ToString(CultureInfo.InvariantCulture);
            string next = (id.Length + suffix.Length > Ids.MaxLength ? id[..(Ids.MaxLength - suffix.Length)] : id) + suffix;
            if (!given.Contains(next))
            {
                return next;
            }
        }
    }

    // A script as it is kept (each line trimmed, blank lines dropped), or null when blank; one that cannot be read is refused beside its box.
    public static string? BindText(FormValues values, string field, Dictionary<string, string> errors)
    {
        string text = OwnedText.Clean(values.Get(field));
        if (text.Length == 0)
        {
            return null;
        }

        if (OwnedText.Problem(text) is { } problem)
        {
            errors[field] = problem;
        }

        return text;
    }

    // The author's notes as typed, with the browser's line breaks made the file's; null when blank. Never shown in play.
    public static string? Notes(FormValues values)
    {
        string notes = values.Get("notes").ReplaceLineEndings("\n");
        return notes.Trim().Length == 0 ? null : notes.TrimEnd('\n');
    }

    // What the chips insert into a storylet's words (design 4.3): the speakers (the captain, each slot, the best at each skill), a note
    // and the closing sentence, each slot's token, the storylet's name, the handler inside a check's outcomes only, and the pack's symbols.
    public static List<string> Chips(IReadOnlyList<string> slots, FormChoices choices, bool handler)
    {
        List<string> people = [.. slots, .. handler ? [StoryletScopes.Handler] : Array.Empty<string>()];
        return
        [
            "captain: ",
            .. people.Select(p => $"{p}: "),
            .. choices.Skills.Select(s => $"best {s.Id}: "),
            "> ",
            "= ",
            .. people.Select(p => $"#{p}#"),
            "#storylet#",
            .. choices.Symbols.Order(StringComparer.Ordinal).Select(s => $"#{s}#"),
        ];
    }

    // The tab shown: the one posted, within the options there are.
    public static int Tab(FormValues values, int count)
    {
        int tab = int.TryParse(values.Get(TabField), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0;
        return Math.Clamp(tab, 0, Math.Max(0, count - 1));
    }

    // An option added or moved shows its own tab; one removed leaves the tab that was shown, or the nearest one left. A vignette's
    // options have no requires.
    public static FormValues AfterRows(FormRows.Applied applied, bool vignette = false)
    {
        if (applied.Set != Options || applied.Verb is not ("add" or "remove" or "up" or "down"))
        {
            return applied.Values;
        }

        int count = FormRows.Read(applied.Values, Options).Count;
        int shown = Tab(applied.Values, count + (applied.Verb == "remove" ? 1 : 0));
        int tab = applied.Verb != "remove" ? applied.Row : shown > applied.Row ? shown - 1 : Math.Max(0, Math.Min(shown, count - 1));
        FormValues values = applied.Values.With(TabField, tab.ToString(CultureInfo.InvariantCulture));
        // A new option's requires starts as rows, as a stored one with none does.
        return applied.Verb == "add" && !vignette ? values.With(ExpressionField.ModeField(OptionRequires(applied.Row)), ExpressionField.RowsMode) : values;
    }

    // The field a loader refusal of this item belongs beside (design 4.3, Step 5): an expression's at its character in its widget, a
    // text's beside its box, and an option's label, skill, difficulty, or chronicle beside that field. Null for the rest, which the
    // alert lists.
    public static (string Field, string Message)? Place(string id, ContentError error, FormValues values, FormChoices choices, IReadOnlyList<ExpressionScope> scopes)
    {
        string head = id + ": ";
        if (!error.Message.StartsWith(head, StringComparison.Ordinal))
        {
            return null;
        }

        string said = error.Message[head.Length..];
        List<Dictionary<string, string>> rows = FormRows.Read(values, Options);
        int Option(string optionId) => rows.FindIndex(r => r["id"].Trim() == optionId);

        Match expression = ExpressionError().Match(said);
        if (expression.Success)
        {
            string key = expression.Groups["key"].Value;
            string? prefix = key == "requires" ? "requires"
                : key.StartsWith("cast.", StringComparison.Ordinal) ? FormRows.Read(values, Cast).FindIndex(r => r["name"].Trim() == key[5..]) is >= 0 and int k ? CastPrefix(k) : null
                : key.StartsWith("options.", StringComparison.Ordinal) && Option(key[8..^9]) is >= 0 and int o ? OptionRequires(o) : null;
            if (prefix is not null && scopes.FirstOrDefault(s => s.Prefix == prefix) is { } scope)
            {
                int position = int.Parse(expression.Groups["at"].Value, CultureInfo.InvariantCulture) - 1;
                return ExpressionField.Place(values, scope, choices.Expressions, position, expression.Groups["message"].Value);
            }

            return null;
        }

        Match text = TextError().Match(said);
        if (text.Success)
        {
            string where = text.Groups["where"].Value;
            string message = Sentence(text.Groups["message"].Value);
            if (where == "text")
            {
                return ("script", message);
            }

            string[] parts = where.Split('.');
            if (Option(parts[0]) is >= 0 and int o)
            {
                string field = parts[1..] switch
                {
                    ["text"] => "text",
                    ["success", "text"] => "success.text",
                    ["failure", "text"] => "failure.text",
                    ["chronicle", "text"] => "chronicleText",
                    _ => "",
                };
                return field.Length > 0 ? (Options.Name(o, field), message) : null;
            }

            return null;
        }

        Match option = OptionError().Match(said);
        if (option.Success && Option(option.Groups["option"].Value) is >= 0 and int index)
        {
            string message = Sentence(option.Groups["message"].Value);
            string? field = message.StartsWith("Label", StringComparison.Ordinal) ? "label"
                : message.Contains("unknown skill", StringComparison.Ordinal) ? "skill"
                : message.Contains("difficulty", StringComparison.Ordinal) ? "difficulty"
                : message.Contains("chronicle weight", StringComparison.Ordinal) ? "chronicleWeight"
                : null;
            return field is null ? null : (Options.Name(index, field), message);
        }

        return said switch
        {
            _ when said.StartsWith("A storylet's stakes", StringComparison.Ordinal) => ("stakes", said),
            _ when said.StartsWith("A storylet needs two to four options", StringComparison.Ordinal) => (Options.Prefix, said),
            _ when said.StartsWith("A cast is at most", StringComparison.Ordinal) => (Cast.Prefix, said),
            _ when said.StartsWith("A cooldown's", StringComparison.Ordinal) => ("cooldownCast", said),
            _ when said.StartsWith("The storylet's chronicle weight", StringComparison.Ordinal) => ("chronicleWeight", said),
            _ => null,
        };
    }

    private static string Sentence(string text)
    {
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }

    // "cast.subject, at character 5: ..." and the like, as the loader says an expression is refused.
    [GeneratedRegex(@"^(?<key>requires|cast\.[a-z0-9_]+|options\.[a-z0-9_]+\.requires), at character (?<at>[0-9]+): (?<message>.*)\z", RegexOptions.Singleline)]
    private static partial Regex ExpressionError();

    // "text cannot be read: ...", "make_time.success.text cannot be used: ...".
    [GeneratedRegex(@"^(?<where>[a-z0-9_.]+) (?<message>cannot be (read|used): .*)\z", RegexOptions.Singleline)]
    private static partial Regex TextError();

    // "Option 'make_time': label must be ...".
    [GeneratedRegex(@"^Option '(?<option>[^']*)': (?<message>.*)\z", RegexOptions.Singleline)]
    private static partial Regex OptionError();
}

// The storylet form (1e part six, design 4.3): definitions, the cast, the words, the notes, and a tab for each option.
internal sealed class StoryletKind : KindForm
{
    public const string RequiresPrefix = "requires";

    public override ContentKind Kind => ContentKind.Storylet;

    public override IReadOnlyList<RowSet> RowSets => [StoryletParts.Options, StoryletParts.Cast];

    public static ExpressionScope Requires(IReadOnlyList<string> slots)
    {
        return new ExpressionScope(RequiresPrefix, "Requires", slots);
    }

    public override IReadOnlyList<RowSet> NestedRowSets(FormValues values)
    {
        return StoryletParts.EffectSets(values);
    }

    public override IReadOnlyList<ExpressionScope> Expressions(FormValues values)
    {
        List<string> slots = StoryletParts.SlotNames(values);
        return [Requires(slots), .. StoryletParts.CastScopes(values), .. StoryletParts.OptionScopes(values, slots)];
    }

    public override FormValues AfterRows(FormRows.Applied applied)
    {
        FormValues values = StoryletParts.AfterRows(applied);
        // A new slot's clauses start as rows.
        return applied.Set == StoryletParts.Cast && applied.Verb == "add" ? values.With(ExpressionField.ModeField(StoryletParts.CastPrefix(applied.Row)), ExpressionField.RowsMode) : values;
    }

    public override FormValues Settle(FormValues values, FormChoices choices, string? focus)
    {
        // Settled by a check: a difficulty to start from.
        List<Dictionary<string, string>> options = FormRows.Read(values, StoryletParts.Options);
        for (int i = 0; i < options.Count; i++)
        {
            if (FormValues.Slug(StoryletParts.Options.Name(i, "settled")) == focus && options[i]["settled"] == StoryletParts.ByCheck && options[i]["difficulty"].Trim().Length == 0)
            {
                return values.With(StoryletParts.Options.Name(i, "difficulty"), "1");
            }
        }

        return StoryletParts.Settle(values, StoryletParts.EffectSets(values), StoryletParts.SlotNames(values), choices.Expressions, focus);
    }

    public override (string Field, string Message)? Place(string id, ContentError error, FormValues values, FormChoices choices)
    {
        return StoryletParts.Place(id, error, values, choices, Expressions(values));
    }

    public override FormChoices Widen(FormChoices choices, ContentFileSet files, string id)
    {
        return ContentEdits.Get<StoryletModel>(files, Kind, id) is { } storylet ? choices with { FixedOptionIds = storylet.Options.Select(o => o.Id).ToList() } : choices;
    }

    public override string? NameOf(ContentFileSet files, string id)
    {
        return ContentEdits.Get<StoryletModel>(files, Kind, id)?.Name;
    }

    public override FormValues? Load(ContentFileSet files, string id, FormChoices choices)
    {
        if (ContentEdits.Get<StoryletModel>(files, Kind, id) is not { } storylet)
        {
            return null;
        }

        List<string> slots = storylet.Cast.Select(c => c.Name).ToList();
        var fields = new List<KeyValuePair<string, string>>
        {
            new("name", Name(storylet.Name)),
            new("stakes", Checks.Text(storylet.Stakes)),
        };
        fields.AddRange(ExpressionField.Load(Requires(slots), storylet.Requires, choices.Expressions));
        StoryletParts.AddCast(fields, storylet.Cast, choices.Expressions);
        fields.Add(new("cooldownCast", storylet.CastCooldown is { } cast ? Checks.Text(cast) : ""));
        fields.Add(new("cooldownGlobal", storylet.GlobalCooldown is { } global ? Checks.Text(global) : ""));
        fields.Add(new("chronicleWeight", storylet.Chronicle?.Weight is { } weight ? Checks.Text(weight) : ""));
        fields.Add(new("script", storylet.Script ?? ""));
        fields.Add(new("notes", storylet.Notes ?? ""));
        StoryletParts.AddOptions(fields, storylet.Options, slots, choices.Expressions);
        return new FormValues(fields);
    }

    // A new storylet: stakes 1, one slot called subject with no clauses yet, and two blank options settled by their effects.
    public override FormValues Blank(FormChoices choices)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("name", ""),
            new("stakes", "1"),
            new(ExpressionField.ModeField(RequiresPrefix), ExpressionField.RowsMode),
            new(StoryletParts.Cast.Name(0, "name"), StoryletScopes.Subject),
            new(ExpressionField.ModeField(StoryletParts.CastPrefix(0)), ExpressionField.RowsMode),
            new("cooldownCast", ""),
            new("cooldownGlobal", ""),
            new("chronicleWeight", ""),
            new("script", ""),
            new("notes", ""),
        };
        StoryletParts.AddBlankOption(fields, 0);
        StoryletParts.AddBlankOption(fields, 1);
        return new FormValues(fields);
    }

    public override Bound Bind(string id, FormValues values, FormChoices choices)
    {
        var errors = new Dictionary<string, string>();
        string? name = Checks.Name(values, errors);
        int stakes = Checks.Whole(values, "stakes", 1, 5, errors);
        List<string> slots = StoryletParts.SlotNames(values);
        string? requires = ExpressionField.Bind(values, Requires(slots), choices.Expressions, errors);
        List<CastSlotModel> cast = StoryletParts.BindCast(values, choices.Expressions, errors);
        int? castCooldown = Checks.OptionalWhole(values, "cooldownCast", 0, null, errors);
        int? globalCooldown = Checks.OptionalWhole(values, "cooldownGlobal", 0, null, errors);
        int? weight = Checks.OptionalWhole(values, "chronicleWeight", 0, ChronicleTuning.Most, errors);
        string? script = StoryletParts.BindText(values, "script", errors);
        string? notes = StoryletParts.Notes(values);
        List<StoryletOptionModel> options = StoryletParts.BindOptions(values, choices, slots, errors, 2, 4, "A storylet needs two to four options.");
        if (errors.Count > 0)
        {
            return new Bound(null, errors);
        }

        return new Bound(_ => new StoryletModel(id, name, stakes, requires, cast, options, castCooldown, globalCooldown, script, weight is { } w ? new ChronicleModel(w, null) : null, notes), errors);
    }
}

// The vignette form (1e part six, design 4.8): the storylet form's parts with no stakes and no chronicle. A vignette with no options has
// effect rows on itself; one with options has two, each settled by its effects with no requires, and a default, taken when it is left.
internal sealed class VignetteKind : KindForm
{
    public const string DefaultField = "default";

    public const string ChoiceRule = "A vignette's choice is two options. Add the other, or remove this one to give the vignette effects of its own.";

    // The vignette's own effects, applied when it arrives: only when it has no options.
    public static readonly RowSet Effects = StoryletParts.EffectSet("effects");

    public override ContentKind Kind => ContentKind.Vignette;

    public override IReadOnlyList<RowSet> RowSets => [StoryletParts.Options, StoryletParts.Cast, Effects];

    public override IReadOnlyList<RowSet> NestedRowSets(FormValues values)
    {
        return Enumerable.Range(0, FormRows.Read(values, StoryletParts.Options).Count).Select(StoryletParts.OptionEffects).ToList();
    }

    public override IReadOnlyList<ExpressionScope> Expressions(FormValues values)
    {
        return [StoryletKind.Requires(StoryletParts.SlotNames(values)), .. StoryletParts.CastScopes(values)];
    }

    public override FormValues AfterRows(FormRows.Applied applied)
    {
        FormValues values = StoryletParts.AfterRows(applied, vignette: true);
        // A new slot's clauses start as rows.
        return applied.Set == StoryletParts.Cast && applied.Verb == "add" ? values.With(ExpressionField.ModeField(StoryletParts.CastPrefix(applied.Row)), ExpressionField.RowsMode) : values;
    }

    public override FormValues Settle(FormValues values, FormChoices choices, string? focus)
    {
        return StoryletParts.Settle(values, [Effects, .. NestedRowSets(values)], StoryletParts.SlotNames(values), choices.Expressions, focus);
    }

    // What the default select says when the option it names has been removed or renamed.
    public static string DefaultGone(string id)
    {
        return $"If left, it takes {id}, which is not one of its options now. Choose one that is.";
    }

    // The default's choices: each option by its id as it would be saved (one left blank takes it from its label), shown by its label.
    public static List<Choice> Defaults(FormValues values)
    {
        return FormRows.Read(values, StoryletParts.Options)
            .Select((row, i) => (Id: row["id"].Trim().Length > 0 ? row["id"].Trim() : NewIds.FromName(row["label"]), Title: StoryletParts.OptionTitle(row, i)))
            .Where(o => o.Id.Length > 0)
            .Select(o => new Choice(o.Id, o.Title == o.Id ? o.Id : $"{o.Title} ({o.Id})"))
            .ToList();
    }

    public override (string Field, string Message)? Place(string id, ContentError error, FormValues values, FormChoices choices)
    {
        string head = id + ": ";
        if (!error.Message.StartsWith(head, StringComparison.Ordinal))
        {
            return null;
        }

        string said = error.Message[head.Length..];
        return said switch
        {
            _ when said.StartsWith("A vignette's choice is", StringComparison.Ordinal) || said.StartsWith("A vignette has effects or", StringComparison.Ordinal) => (StoryletParts.Options.Prefix, said),
            _ when said.StartsWith("A vignette with options needs a default", StringComparison.Ordinal) || said.StartsWith("A vignette with no choice has no default", StringComparison.Ordinal) => (DefaultField, said),
            _ when said.StartsWith("A vignette with no choice needs", StringComparison.Ordinal) => (Effects.Prefix, said),
            _ when said.StartsWith("A vignette's text", StringComparison.Ordinal) => ("script", said),
            _ => StoryletParts.Place(id, error, values, choices, Expressions(values)),
        };
    }

    public override FormChoices Widen(FormChoices choices, ContentFileSet files, string id)
    {
        return ContentEdits.Get<VignetteModel>(files, Kind, id) is { } vignette ? choices with { FixedOptionIds = vignette.Options.Select(o => o.Id).ToList() } : choices;
    }

    public override string? NameOf(ContentFileSet files, string id)
    {
        return ContentEdits.Get<VignetteModel>(files, Kind, id)?.Name;
    }

    public override FormValues? Load(ContentFileSet files, string id, FormChoices choices)
    {
        if (ContentEdits.Get<VignetteModel>(files, Kind, id) is not { } vignette)
        {
            return null;
        }

        List<string> slots = vignette.Cast.Select(c => c.Name).ToList();
        var fields = new List<KeyValuePair<string, string>> { new("name", Name(vignette.Name)) };
        fields.AddRange(ExpressionField.Load(StoryletKind.Requires(slots), vignette.Requires, choices.Expressions));
        StoryletParts.AddCast(fields, vignette.Cast, choices.Expressions);
        fields.Add(new("cooldownCast", vignette.CastCooldown is { } cast ? Checks.Text(cast) : ""));
        fields.Add(new("cooldownGlobal", vignette.GlobalCooldown is { } global ? Checks.Text(global) : ""));
        fields.Add(new("script", vignette.Script ?? ""));
        fields.Add(new("notes", vignette.Notes ?? ""));
        StoryletParts.AddEffects(fields, Effects, vignette.Effects);
        fields.Add(new(DefaultField, vignette.Default ?? ""));
        StoryletParts.AddOptions(fields, vignette.Options, slots, choices.Expressions, vignette: true);
        return new FormValues(fields);
    }

    // A new vignette: one slot called subject with no clauses yet, no options, and no effects yet.
    public override FormValues Blank(FormChoices choices)
    {
        return new FormValues(
        [
            new("name", ""),
            new(ExpressionField.ModeField(StoryletKind.RequiresPrefix), ExpressionField.RowsMode),
            new(StoryletParts.Cast.Name(0, "name"), StoryletScopes.Subject),
            new(ExpressionField.ModeField(StoryletParts.CastPrefix(0)), ExpressionField.RowsMode),
            new("cooldownCast", ""),
            new("cooldownGlobal", ""),
            new("script", ""),
            new("notes", ""),
            new(DefaultField, ""),
        ]);
    }

    public override Bound Bind(string id, FormValues values, FormChoices choices)
    {
        var errors = new Dictionary<string, string>();
        string? name = Checks.Name(values, errors);
        List<string> slots = StoryletParts.SlotNames(values);
        string? requires = ExpressionField.Bind(values, StoryletKind.Requires(slots), choices.Expressions, errors);
        List<CastSlotModel> cast = StoryletParts.BindCast(values, choices.Expressions, errors);
        int? castCooldown = Checks.OptionalWhole(values, "cooldownCast", 0, null, errors);
        int? globalCooldown = Checks.OptionalWhole(values, "cooldownGlobal", 0, null, errors);
        string? script = StoryletParts.BindText(values, "script", errors);
        string? notes = StoryletParts.Notes(values);
        List<StoryletOptionModel> options = StoryletParts.BindOptions(values, choices, slots, errors, 0, 2, ChoiceRule, vignette: true);
        if (options.Count == 1)
        {
            errors[StoryletParts.Options.Prefix] = ChoiceRule;
        }

        // With options, the vignette's own effects wait unsaved in hidden fields, kept for switching back; without, the default does.
        List<StoryletEffectModel> effects = [];
        string? chosen = null;
        if (options.Count == 0)
        {
            effects = StoryletParts.BindEffects(values, Effects, slots, handler: false, choices.Expressions, errors);
            if (FormRows.Read(values, Effects).Count == 0)
            {
                errors[Effects.Prefix] = "A vignette with no choice needs at least one effect: what changes when it arrives.";
            }
        }
        else
        {
            chosen = values.Get(DefaultField).Trim();
            if (chosen.Length == 0)
            {
                errors[DefaultField] = "Choose the option taken if the captain leaves it.";
            }
            else if (options.All(o => o.Id != chosen))
            {
                errors[DefaultField] = DefaultGone(chosen);
            }
        }

        if (errors.Count > 0)
        {
            return new Bound(null, errors);
        }

        return new Bound(_ => new VignetteModel(id, name, requires, cast, effects, options, chosen, castCooldown, globalCooldown, script, notes), errors);
    }
}
