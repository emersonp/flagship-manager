using System.Globalization;
using Sim.Content;
using Sim.Core.Content;

namespace Web.Forms;

// The trait form (1e part six, design 4.9): every key the loader reads. Where it sits, whether it is shown, named levels or a max (one or
// the other), a weekly step, an expiry, the briefing chip, the launch rules, a check bonus on a mission, what happens when a pair's target
// dies, and the notes. In place of "What plays here", "Where it's used" (TraitUses).
internal sealed class TraitKind : KindForm
{
    public const string ModeField = "values";

    public const string LevelsMode = "levels";

    public const string MaxMode = "max";

    public const string PairRule = "Only a trait on a pair has a target. Remove these, or set On to a pair.";

    // A named level: its number and its name.
    public static readonly RowSet Levels = new("levels", ["number", "name"], Qualified: true, Noun: "level");

    // One rule (design 4.9): what it changes (loyalty, morale, or a trait), the trait (blank for this one), add or set, the amount (a
    // number, or a level's name for a set), and on (blank for the holder; target on a pair; crew for loyalty and morale).
    public static readonly IReadOnlyList<string> RuleFields = ["what", "trait", "mode", "amount", "on"];

    public static readonly RowSet Sent = Rules("sent");

    public static readonly RowSet NotSent = Rules("notSent");

    public static readonly RowSet WithTarget = Rules("withTarget");

    public static readonly RowSet OnTargetDied = Rules("onTargetDied");

    public static readonly IReadOnlyList<Choice> Places = [new(TraitPlaces.Ship, "the ship"), new(TraitPlaces.Crew, "a crew member"), new(TraitPlaces.Pair, "a pair: one crew member toward another")];

    public override ContentKind Kind => ContentKind.Trait;

    public override IReadOnlyList<RowSet> RowSets => [Levels, Sent, NotSent, WithTarget, OnTargetDied];

    private static RowSet Rules(string prefix)
    {
        // A new rule clears this trait, which any trait can be set to.
        return new RowSet(prefix, RuleFields, Qualified: true, Noun: "rule", Defaults: new Dictionary<string, string> { ["what"] = "trait", ["mode"] = "set", ["amount"] = "0" });
    }

    public static bool Pair(FormValues values)
    {
        return values.Get("on") == TraitPlaces.Pair;
    }

    // The trait as the form now has it, for its own rules: where it sits and its levels as posted.
    public static TraitChoice Own(FormValues values)
    {
        var levels = new Dictionary<int, string>();
        if (values.Get(ModeField) == LevelsMode)
        {
            foreach (Dictionary<string, string> row in FormRows.Read(values, Levels))
            {
                if (int.TryParse(row["number"].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 1 && Ids.IsInnerId(row["name"].Trim()))
                {
                    levels.TryAdd(n, row["name"].Trim());
                }
            }
        }

        string on = values.Get("on");
        return new TraitChoice(values.Get("id"), on.Length == 0 ? null : on, levels, levels.Count > 0 ? levels.Keys.Max() : int.MaxValue);
    }

    // The trait a rule row changes: this one (blank), or another the pack defines.
    public static TraitChoice? RuleTrait(FormValues values, string trait, FormChoices choices)
    {
        return trait.Length == 0 ? Own(values) : choices.Expressions.Trait(trait);
    }

    // Whom a rule lands on: the holder (blank), the target on a pair, and the whole crew for loyalty and morale.
    public static List<Choice> OnChoices(string what, bool pair)
    {
        return [.. pair ? [new Choice("target", "the target")] : Array.Empty<Choice>(), .. what is "loyalty" or "morale" ? [new Choice("crew", "the whole crew")] : Array.Empty<Choice>()];
    }

    public override string? NameOf(ContentFileSet files, string id)
    {
        return ContentEdits.Get<TraitModel>(files, Kind, id)?.Name;
    }

    public override FormValues? Load(ContentFileSet files, string id, FormChoices choices)
    {
        if (ContentEdits.Get<TraitModel>(files, Kind, id) is not { } trait)
        {
            return null;
        }

        var fields = new List<KeyValuePair<string, string>>
        {
            new("name", Name(trait.Name)),
            new("on", trait.On ?? ""),
            new("shown", trait.Shown == false ? "" : "true"),
            new(ModeField, trait.Levels is { Count: > 0 } ? LevelsMode : MaxMode),
            new("max", trait.Max is { } max ? Checks.Text(max) : ""),
        };
        int row = 0;
        foreach ((int number, string name) in trait.Levels ?? new Dictionary<int, string>())
        {
            fields.Add(new(Levels.Name(row, "number"), Checks.Text(number)));
            fields.Add(new(Levels.Name(row, "name"), name));
            row++;
        }

        fields.Add(new("everyWeeks", trait.Every is { } every ? Checks.Text(every.Weeks) : ""));
        fields.Add(new("everyAdd", trait.Every is { } step ? Checks.Text(step.Add) : ""));
        fields.Add(new("expires", trait.ExpiresWeeks is { } weeks ? Checks.Text(weeks) : ""));
        fields.Add(new("briefing", trait.Briefing ?? ""));
        AddRules(fields, Sent, trait.Launch?.Sent ?? []);
        AddRules(fields, NotSent, trait.Launch?.NotSent ?? []);
        AddRules(fields, WithTarget, trait.Launch?.WithTarget ?? []);
        fields.Add(new("missionBonus", trait.Mission is { } mission ? Checks.Text(mission.CheckBonus) : ""));
        fields.Add(new("missionWithTarget", trait.Mission?.WithTarget == true ? "true" : ""));
        AddRules(fields, OnTargetDied, trait.OnTargetDied);
        fields.Add(new("notes", trait.Notes ?? ""));
        return new FormValues(fields);
    }

    private static void AddRules(List<KeyValuePair<string, string>> fields, RowSet set, IReadOnlyList<StoryletEffectModel> rules)
    {
        for (int i = 0; i < rules.Count; i++)
        {
            StoryletEffectModel rule = rules[i];
            fields.Add(new(set.Name(i, "what"), rule.What));
            fields.Add(new(set.Name(i, "trait"), rule.Trait ?? ""));
            fields.Add(new(set.Name(i, "mode"), rule.What == "trait" ? rule.Set ? "set" : "add" : ""));
            fields.Add(new(set.Name(i, "amount"), rule.Amount is { } amount ? Checks.Text(amount) : rule.Level ?? ""));
            fields.Add(new(set.Name(i, "on"), rule.On ?? ""));
        }
    }

    // A new trait: on a crew member, shown, with no levels or max yet, and no rules.
    public override FormValues Blank(FormChoices choices)
    {
        return new FormValues(
        [
            new("name", ""),
            new("on", TraitPlaces.Crew),
            new("shown", "true"),
            new(ModeField, MaxMode),
            new("max", ""),
            new("everyWeeks", ""),
            new("everyAdd", ""),
            new("expires", ""),
            new("briefing", ""),
            new("missionBonus", ""),
            new("missionWithTarget", ""),
            new("notes", ""),
        ]);
    }

    // After On or the levels-or-max select posts: named levels start with one row. After a rule's what, trait, or add-or-set: the row fits.
    public override FormValues Settle(FormValues values, FormChoices choices, string? focus)
    {
        if (focus == FormValues.Slug(ModeField) && values.Get(ModeField) == LevelsMode && FormRows.Read(values, Levels).Count == 0)
        {
            Dictionary<string, string> first = Levels.NewRow();
            first["number"] = "1";
            return FormRows.Write(values, Levels, [first]);
        }

        bool pair = Pair(values);
        foreach (RowSet set in new[] { Sent, NotSent, WithTarget, OnTargetDied })
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
                    row["amount"] = whole ? row["amount"].Trim() : "-1";
                }
                else if (row["what"] == "trait" && RuleTrait(values, row["trait"], choices) is { } trait)
                {
                    List<Choice> modes = StoryletParts.ModeChoices(trait);
                    if (modes.All(m => m.Value != row["mode"]))
                    {
                        row["mode"] = modes[0].Value;
                    }

                    if (StoryletParts.LevelChoices(trait, row["mode"]) is { } levels)
                    {
                        row["amount"] = levels.Any(l => l.Value == row["amount"].Trim()) ? row["amount"].Trim() : levels[0].Value;
                    }
                    else if (!whole)
                    {
                        row["amount"] = "0";
                    }

                    if (row["on"] == "crew")
                    {
                        row["on"] = "";
                    }
                }

                if (row["on"] == "target" && !pair)
                {
                    row["on"] = "";
                }

                return FormRows.Write(values, set, rows);
            }
        }

        return values;
    }

    public override Bound Bind(string id, FormValues values, FormChoices choices)
    {
        var errors = new Dictionary<string, string>();
        string? name = Checks.Name(values, errors);
        string on = values.Get("on").Trim();
        if (on.Length > 0 && !TraitPlaces.All.Contains(on))
        {
            errors["on"] = "Choose the ship, a crew member, or a pair.";
        }

        bool pair = on == TraitPlaces.Pair;
        bool shown = values.Get("shown") == "true";
        Dictionary<int, string>? levels = null;
        int? max = null;
        if (values.Get(ModeField) == LevelsMode)
        {
            levels = BindLevels(values, errors);
        }
        else
        {
            max = Checks.OptionalWhole(values, "max", 1, null, errors);
        }

        TraitStepModel? every = null;
        if (values.Get("everyWeeks").Trim().Length > 0 || values.Get("everyAdd").Trim().Length > 0)
        {
            int weeks = Checks.Whole(values, "everyWeeks", 1, null, errors);
            int add = Checks.Whole(values, "everyAdd", null, null, errors);
            if (!errors.ContainsKey("everyAdd") && add == 0)
            {
                errors["everyAdd"] = "Enter a step that is not 0: below 0 to ease, above 0 to grow.";
            }

            every = new TraitStepModel(weeks, add);
        }

        int? expires = Checks.OptionalWhole(values, "expires", 1, null, errors);
        string briefing = values.Get("briefing").Trim();
        if (briefing.Length > 40 || briefing.IndexOfAny(['\r', '\n', (char)0x85, (char)0x2028, (char)0x2029]) >= 0)
        {
            errors["briefing"] = "A briefing is 1 to 40 characters on one line.";
        }

        TraitChoice own = Own(values);
        List<StoryletEffectModel> sent = BindRules(values, Sent, own, pair, choices, errors);
        List<StoryletEffectModel> notSent = BindRules(values, NotSent, own, pair, choices, errors);
        List<StoryletEffectModel> withTarget = BindRules(values, WithTarget, own, pair, choices, errors);
        List<StoryletEffectModel> died = BindRules(values, OnTargetDied, own, pair, choices, errors);
        foreach ((RowSet set, List<StoryletEffectModel> rules) in new[] { (WithTarget, withTarget), (OnTargetDied, died) })
        {
            if (!pair && rules.Count > 0)
            {
                errors[set.Prefix] = PairRule;
            }
        }

        int? bonus = Checks.OptionalWhole(values, "missionBonus", null, null, errors);
        bool bonusWithTarget = values.Get("missionWithTarget") == "true";
        if (bonus == 0)
        {
            errors["missionBonus"] = "Enter a bonus that is not 0, or leave it blank for none.";
        }
        else if (bonus is null && bonusWithTarget && !errors.ContainsKey("missionBonus"))
        {
            errors["missionBonus"] = "Enter the bonus it gives with the target.";
        }

        if (bonusWithTarget && !pair)
        {
            errors["missionWithTarget"] = PairRule;
        }

        string? notes = StoryletParts.Notes(values);
        if (errors.Count > 0)
        {
            return new Bound(null, errors);
        }

        return new Bound(files =>
        {
            TraitModel? stored = ContentEdits.Get<TraitModel>(files, Kind, id);
            // Shown is absent when ticked, as the files write it, unless the file says so already.
            bool? keptShown = shown ? stored?.Shown == true ? true : null : false;
            // A launch with no rules is no launch, unless the file already writes launch: {}.
            TraitLaunchModel? launch = sent.Count + notSent.Count + withTarget.Count > 0 || stored?.Launch is { Sent.Count: 0, NotSent.Count: 0, WithTarget.Count: 0 } ? new TraitLaunchModel(sent, notSent, withTarget) : null;
            TraitMissionModel? mission = bonus is { } b ? new TraitMissionModel(b, bonusWithTarget) : null;
            return new TraitModel(id, name, keptShown, on.Length == 0 ? null : on, levels is { Count: > 0 } ? levels : null, max, briefing.Length == 0 ? null : briefing, launch, mission, every, expires, died, notes);
        }, errors);
    }

    // The named levels, in the order written: each a number of 1 or more and a lower-case name, neither used twice.
    private static Dictionary<int, string> BindLevels(FormValues values, Dictionary<string, string> errors)
    {
        var levels = new Dictionary<int, string>();
        List<Dictionary<string, string>> rows = FormRows.Read(values, Levels);
        for (int i = 0; i < rows.Count; i++)
        {
            int number = Checks.Whole(values, Levels.Name(i, "number"), 1, null, errors);
            string field = Levels.Name(i, "name");
            string level = rows[i]["name"].Trim();
            if (level.Length == 0)
            {
                errors[field] = "Enter the level's name.";
            }
            else if (!Ids.IsInnerId(level))
            {
                errors[field] = Ids.InnerRule;
            }
            else if (levels.ContainsValue(level))
            {
                errors[field] = "Each level has its own name.";
            }

            if (errors.ContainsKey(Levels.Name(i, "number")) || errors.ContainsKey(field))
            {
                continue;
            }

            if (!levels.TryAdd(number, level))
            {
                errors[Levels.Name(i, "number")] = "Each level has its own number.";
            }
        }

        if (rows.Count == 0)
        {
            errors[Levels.Prefix] = "Add a level, or choose a max instead.";
        }

        return levels;
    }

    // A list of rules, each as the loader reads it; a problem is put beside its part.
    private static List<StoryletEffectModel> BindRules(FormValues values, RowSet set, TraitChoice own, bool pair, FormChoices choices, Dictionary<string, string> errors)
    {
        var rules = new List<StoryletEffectModel>();
        List<Dictionary<string, string>> rows = FormRows.Read(values, set);
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i].ToDictionary(p => p.Key, p => p.Value.Trim());
            string Field(string part) => set.Name(i, part);
            string what = row["what"];
            string on = row["on"];
            // holder is the default, kept when the file writes it.
            if (on.Length > 0 && on != "holder" && OnChoices(what, pair).All(c => c.Value != on))
            {
                errors[Field("on")] = on == "target" ? "Only a trait on a pair has a target." : "The whole crew is for loyalty and morale; a trait is changed on the holder or the target.";
            }

            string? written = on.Length == 0 ? null : on;
            if (what is "loyalty" or "morale")
            {
                int amount = Checks.Whole(values, Field("amount"), null, null, errors);
                if (!errors.ContainsKey(Field("amount")) && amount == 0)
                {
                    errors[Field("amount")] = "Enter a change that is not 0.";
                }

                rules.Add(new StoryletEffectModel(what, amount, On: written));
                continue;
            }

            if (what != "trait")
            {
                errors[Field("what")] = "Choose what it changes.";
                continue;
            }

            string traitId = row["trait"];
            TraitChoice? trait = traitId.Length == 0 ? own : choices.Expressions.Trait(traitId);
            if (traitId.Length > 0 && !Ids.IsInnerId(traitId))
            {
                errors[Field("trait")] = Ids.InnerRule;
            }
            else if (traitId.Length > 0 && trait is null && choices.Expressions.Traits.Count > 0)
            {
                errors[Field("trait")] = "Choose a trait the pack defines.";
            }

            string mode = row["mode"];
            if (mode is not ("add" or "set"))
            {
                errors[Field("mode")] = "Choose add or set.";
            }
            else if (mode == "add" && trait is { Levels.Count: > 0 })
            {
                errors[Field("mode")] = $"{(traitId.Length == 0 ? "This trait" : traitId)} has named levels, so it is set, not added to.";
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

            rules.Add(new StoryletEffectModel("trait", number, traitId.Length == 0 ? null : traitId, mode == "set", level, written));
        }

        return rules;
    }

    // A loader refusal beside its field: "shaken: max must be 1 or more." and the like. Null for the rest, which the alert lists.
    public override (string Field, string Message)? Place(string id, ContentError error, FormValues values, FormChoices choices)
    {
        string head = id + ": ";
        if (!error.Message.StartsWith(head, StringComparison.Ordinal))
        {
            return null;
        }

        string said = error.Message[head.Length..];
        foreach ((string key, RowSet set) in new[] { ("launch.sent: ", Sent), ("launch.notSent: ", NotSent), ("launch.withTarget: ", WithTarget), ("onTargetDied: ", OnTargetDied) })
        {
            if (said.StartsWith(key, StringComparison.Ordinal))
            {
                return (set.Prefix, said[key.Length..]);
            }
        }

        string? field = said switch
        {
            _ when said.StartsWith("on must be", StringComparison.Ordinal) || said.StartsWith("Only a trait on a pair", StringComparison.Ordinal) => "on",
            _ when said.StartsWith("A level's", StringComparison.Ordinal) || said.StartsWith("Level name", StringComparison.Ordinal) => Levels.Prefix,
            _ when said.StartsWith("max ", StringComparison.Ordinal) => values.Get(ModeField) == LevelsMode ? Levels.Prefix : "max",
            _ when said.StartsWith("briefing", StringComparison.Ordinal) => "briefing",
            _ when said.StartsWith("every", StringComparison.Ordinal) || said.Contains("every", StringComparison.Ordinal) => "everyWeeks",
            _ when said.StartsWith("expires", StringComparison.Ordinal) => "expires",
            _ when said.StartsWith("mission", StringComparison.Ordinal) => "missionBonus",
            _ => null,
        };
        return field is null ? null : (field, said);
    }
}
