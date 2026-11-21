using Sim.Content;
using Sim.Core.Content;
using Sim.Core.Narrative;

namespace Web.Forms;

internal sealed class ObjectiveKind : KindForm
{
    public static readonly RowSet Stages = new("stages", ["slot", "draw", "order", "countMin", "countMax", "skippable"], MultiFields: ["draw", "order"], Noun: "stage", Defaults: new Dictionary<string, string> { ["countMin"] = "1", ["countMax"] = "1" });

    public override ContentKind Kind => ContentKind.Objective;

    public override IReadOnlyList<RowSet> RowSets => [Stages, OwnedText.Lines];

    public override string? NameOf(ContentFileSet files, string id)
    {
        return ContentEdits.Get<ObjectiveModel>(files, Kind, id)?.Name;
    }

    // A stage that draws by a tag no encounter uses (a pack the loader refuses) still shows its tag, so the stage can be seen and fixed.
    public override FormChoices Widen(FormChoices choices, ContentFileSet files, string id)
    {
        if (ContentEdits.Get<ObjectiveModel>(files, Kind, id) is not { } objective)
        {
            return choices;
        }

        return choices with { EncounterTags = [.. choices.EncounterTags, .. objective.Stages.SelectMany(s => s.DrawTags).Distinct().Where(t => !choices.EncounterTags.Contains(t))] };
    }

    public override FormValues? Load(ContentFileSet files, string id, FormChoices choices)
    {
        if (ContentEdits.Get<ObjectiveModel>(files, Kind, id) is not { } objective)
        {
            return null;
        }

        var fields = new List<KeyValuePair<string, string>>
        {
            new("name", Name(objective.Name)),
            new("tags", string.Join(", ", objective.Tags)),
            new("teamSizeMin", Checks.Text(objective.TeamSizeMin)),
            new("teamSizeMax", Checks.Text(objective.TeamSizeMax)),
            new("need", Checks.Text(objective.Need)),
            new("unattended", objective.UnattendedDifficulty is { } d ? Checks.Text(d) : ""),
        };
        for (int i = 0; i < objective.Stages.Count; i++)
        {
            StageModel stage = objective.Stages[i];
            fields.Add(new(Stages.Name(i, "slot"), stage.Slot));
            fields.AddRange(stage.DrawTags.Select(t => new KeyValuePair<string, string>(Stages.Name(i, "draw"), t)));
            // The tags in the order the file has them: the boxes post in the page's order, and a save keeps the file's.
            fields.AddRange(stage.DrawTags.Select(t => new KeyValuePair<string, string>(Stages.Name(i, "order"), t)));
            fields.Add(new(Stages.Name(i, "countMin"), Checks.Text(stage.CountMin)));
            fields.Add(new(Stages.Name(i, "countMax"), Checks.Text(stage.CountMax)));
            if (stage.Skippable)
            {
                fields.Add(new(Stages.Name(i, "skippable"), "true"));
            }
        }

        OwnedText.Add(fields, "", NarrativeFacts.Outcomes, objective.Text);
        OwnedText.AddLines(fields, objective.Lines);
        return new FormValues(fields);
    }

    public override FormValues Blank(FormChoices choices)
    {
        return new FormValues([new("name", ""), new("tags", ""), new("teamSizeMin", "1"), new("teamSizeMax", "1"), new("need", "1"), new("unattended", ""), new(Stages.Name(0, "slot"), ""), new(Stages.Name(0, "countMin"), "1"), new(Stages.Name(0, "countMax"), "1")]);
    }

    public override Bound Bind(string id, FormValues values, FormChoices choices)
    {
        var errors = new Dictionary<string, string>();
        string? name = Checks.Name(values, errors);
        List<string> tags = Checks.Tags(values, "tags", errors);
        int min = Checks.Whole(values, "teamSizeMin", 1, null, errors);
        int max = Checks.Whole(values, "teamSizeMax", 1, null, errors);
        if (!errors.ContainsKey("teamSizeMin") && !errors.ContainsKey("teamSizeMax") && max < min)
        {
            errors["teamSizeMax"] = "The maximum must not be below the minimum.";
        }

        int need = Checks.Whole(values, "need", 1, null, errors);
        int? unattended = Checks.OptionalWhole(values, "unattended", 1, null, errors);
        List<Dictionary<string, string>> rows = FormRows.Read(values, Stages);
        if (rows.Count == 0)
        {
            errors["stages"] = "An objective needs at least one stage.";
        }

        var stages = new List<StageModel>();
        for (int i = 0; i < rows.Count; i++)
        {
            string slot = rows[i]["slot"].Trim();
            if (slot.Length == 0)
            {
                errors[Stages.Name(i, "slot")] = "Enter a slot.";
            }
            else if (slot.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029']) >= 0)
            {
                errors[Stages.Name(i, "slot")] = "A slot is one line.";
            }

            IReadOnlyList<string> draw = RowSet.Split(rows[i]["draw"]);
            if (draw.Any(t => !choices.EncounterTags.Contains(t)))
            {
                errors[Stages.Name(i, "draw")] = "Unknown tag.";
            }

            int countMin = Checks.Whole(values, Stages.Name(i, "countMin"), 1, null, errors);
            int countMax = Checks.Whole(values, Stages.Name(i, "countMax"), 1, null, errors);
            if (!errors.ContainsKey(Stages.Name(i, "countMin")) && !errors.ContainsKey(Stages.Name(i, "countMax")) && countMax < countMin)
            {
                errors[Stages.Name(i, "countMax")] = "The maximum must not be below the minimum.";
            }

            IReadOnlyList<string> kept = RowSet.Split(rows[i]["order"]);
            stages.Add(new StageModel(slot, draw.OrderBy(t => kept.Contains(t) ? kept.ToList().IndexOf(t) : int.MaxValue).ToList(), countMin, countMax, rows[i]["skippable"] == "true"));
        }

        IReadOnlyDictionary<string, string>? text = OwnedText.Bind(values, "", NarrativeFacts.Outcomes, errors);
        IReadOnlyList<OwnLineModel>? lines = OwnedText.BindLines(values, "objective", errors);
        return new Bound(errors.Count > 0 ? null : _ => new ObjectiveModel(id, name, tags, min, max, need, unattended, stages, text, lines), errors);
    }
}

internal sealed class EncounterKind : KindForm
{
    // The form edits this many requirements of an approach. The author writes no more until it edits more, so it does nothing special for them.
    public const int RequirementRows = 3;

    // A requirement's "what" for a gear tag; any other value is a skill id.
    public const string GearPrefix = "gear:";

    // A requirement's "what" for a trait (1e part three; written by the form since part six): its trait, at least and at most, and on
    // are fields of their own. is, as the file wrote it, is kept while at least and at most both stay at the value it names, so a
    // condition opened and saved unchanged is written as it was.
    public const string TraitWhat = "trait";

    // A requirement row's fields: what and level for a skill or gear; trait, min, max, on, and is for a trait.
    public static readonly IReadOnlyList<string> RequirementParts = ["what", "level", "trait", "min", "max", "on", "is"];

    // The traits a condition may read: every defined one but a pair's, which the loader refuses here.
    public static List<Choice> ConditionTraits(FormChoices choices)
    {
        return choices.Expressions.Traits.Where(t => !t.OnPair).Select(t => new Choice(t.Id, t.Id)).ToList();
    }

    // Whom a trait condition looks at, as the trait's place allows: the ship, the lead, or anyone standing. An unknown trait is offered all three.
    public static List<Choice> ConditionOns(TraitChoice? trait)
    {
        return
        [
            .. trait is null || trait.OnShip ? [new Choice(TraitOn.Ship, "the ship")] : Array.Empty<Choice>(),
            .. trait is null || trait.OnCrew ? [new Choice(TraitOn.Lead, "the lead"), new Choice(TraitOn.Anyone, "anyone")] : Array.Empty<Choice>(),
        ];
    }

    // At least and at most by a level's name, for a trait with named levels (they are written as numbers, which is what the loader
    // reads); null for a counting trait, which takes a number. At most also offers 0, not held.
    public static List<Choice>? ConditionLevels(TraitChoice? trait, bool most)
    {
        return trait is { Levels.Count: > 0 }
            ? [.. most ? [new Choice("0", "0, not held")] : Array.Empty<Choice>(), .. trait.Levels.OrderBy(l => l.Key).Select(l => new Choice(Checks.Text(l.Key), $"{l.Value} ({l.Key})"))]
            : null;
    }

    // What an is names, as a number: a whole number of 0 or more, or one of the trait's level names; null when it names neither.
    public static int? IsValue(string text, TraitChoice? trait)
    {
        if (int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }

        return trait?.Levels.FirstOrDefault(l => l.Value == text) is { Value: not null } level ? level.Key : null;
    }

    // "Trait: sensors_quirk at most 1, on the ship".
    public static string TraitLabel(RequirementModel r)
    {
        string range = r.Is is { } exact ? $"is {exact}" : r.Max is { } max && (r.Min ?? 0) == 0 ? $"at most {max}" : r.Max is { } top ? $"{r.Min} to {top}" : $"at least {r.Min ?? 1}";
        string on = r.On switch { "ship" => "on the ship", "lead" => "on the lead", _ => "on anyone" };
        return $"Trait: {r.Trait} {range}, {on}";
    }

    // A trait change's fields, in an outcome and in a decision's effect row: traitMode is "add" or "set", and traitOn what it lands on.
    public static readonly IReadOnlyList<string> TraitFields = ["trait", "traitMode", "traitAmount", "traitOn"];

    // Declared after TraitFields, which it reads as it starts.
    // own is "true" when the tier is written; the next four are then its one effect, a blank meaning 0 or no roll, and the next four
    // its one trait change, a blank trait meaning none; traitToward makes it a pair's (1e part three), blank for none. A decision's
    // rows have no toward: a pair needs an actor.
    public static readonly IReadOnlyList<string> OutcomeFields = ["own", "progress", "momentum", "strain", "injury", .. TraitFields, "traitToward"];

    // What a trait change may land on in an approach's outcome; a decision's answer has no actor.
    public static readonly IReadOnlyList<string> OutcomeTargets = [TraitOn.Ship, TraitOn.Actor, TraitOn.Lead, TraitOn.Team];

    public static readonly IReadOnlyList<string> AnswerTargets = [TraitOn.Ship, TraitOn.Lead, TraitOn.Team];

    public static readonly IReadOnlyList<string> TraitModes = ["add", "set"];

    // Whom a pair trait points at, in an approach's outcome.
    public static readonly IReadOnlyList<string> Towards = [TraitOn.Lead, TraitOn.Actor];

    // An approach's requirements, outcomes, and words post with its row, as approaches[0].r0.what, approaches[0].out.success.own,
    // and approaches[0].text.attempt, so they move and go with it.
    public static readonly RowSet Approaches = new(
        "approaches",
        [
            "id", "name", "skill", "difficulty", "gearTag",
            .. Enumerable.Range(0, RequirementRows).SelectMany(r => RequirementParts.Select(p => RequirementField(r, p))),
            .. NarrativeFacts.Tiers.SelectMany(t => OutcomeFields.Select(f => OutcomeField(t, f))),
            .. NarrativeFacts.ApproachTexts.Select(TextField),
        ],
        Qualified: true,
        Noun: "approach",
        Defaults: new Dictionary<string, string> { ["difficulty"] = "1" });

    public static readonly RowSet Bold = new("bold", ["progress", "momentum", "strain", "injury", "withdraw", .. TraitFields], Qualified: true, Noun: "effect");

    public static readonly RowSet Safe = new("safe", ["progress", "momentum", "strain", "injury", "withdraw", .. TraitFields], Qualified: true, Noun: "effect");

    public override ContentKind Kind => ContentKind.Encounter;

    public override IReadOnlyList<RowSet> RowSets => [Approaches, Bold, Safe, OwnedText.Lines];

    // The row field that holds an approach's text for this key.
    public static string TextField(string key) => OwnedText.Field("", key);

    // what is a skill id, GearPrefix and a tag, TraitWhat, or empty; level is used only with a skill, and the rest of RequirementParts
    // only with a trait.
    public static string RequirementField(int row, string field) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"r{row}.{field}");

    // A field of a tier's outcome: one of OutcomeFields.
    public static string OutcomeField(string tier, string field) => $"out.{tier}.{field}";

    // The posted field that says which approach's tab the page shows. It is not content: only the page reads it.
    public const string TabField = "tab";

    // The tab shown: the one posted, within the rows there are.
    public static int Tab(FormValues values, int count)
    {
        int tab = int.TryParse(values.Get(TabField), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : 0;
        return Math.Clamp(tab, 0, Math.Max(0, count - 1));
    }

    // An approach added or moved shows its own tab; one removed leaves the tab that was shown, or the nearest one left when it was that one.
    public override FormValues AfterRows(FormRows.Applied applied)
    {
        if (applied.Set != Approaches || applied.Verb is not ("add" or "remove" or "up" or "down"))
        {
            return applied.Values;
        }

        int count = FormRows.Read(applied.Values, Approaches).Count;
        int shown = Tab(applied.Values, count + (applied.Verb == "remove" ? 1 : 0));
        int tab = applied.Verb != "remove" ? applied.Row : shown > applied.Row ? shown - 1 : Math.Max(0, Math.Min(shown, count - 1));
        return applied.Values.With(TabField, tab.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // A trait condition's trait chosen (it posts as it changes): on made to fit where the trait sits, the only choice taken when there is
    // one, and at least, at most, and is cleared where the trait has no such level.
    public override FormValues Settle(FormValues values, FormChoices choices, string? focus)
    {
        int count = FormRows.Read(values, Approaches).Count;
        for (int i = 0; i < count; i++)
        {
            for (int r = 0; r < RequirementRows; r++)
            {
                string Field(string part) => Approaches.Name(i, RequirementField(r, part));
                if (FormValues.Slug(Field("trait")) != focus)
                {
                    continue;
                }

                TraitChoice? trait = choices.Expressions.Trait(values.Get(Field("trait")));
                List<Choice> ons = ConditionOns(trait);
                string on = values.Get(Field("on"));
                FormValues settled = values.With(Field("on"), ons.Count == 1 ? ons[0].Value : ons.Any(c => c.Value == on) ? on : "");
                foreach ((string part, bool most) in new[] { ("min", false), ("max", true) })
                {
                    string value = settled.Get(Field(part)).Trim();
                    if (value.Length > 0 && ConditionLevels(trait, most) is { } levels && levels.All(l => l.Value != value))
                    {
                        settled = settled.With(Field(part), "");
                    }
                }

                return IsValue(settled.Get(Field("is")), trait) is null ? settled.With(Field("is"), "") : settled;
            }
        }

        return values;
    }

    // For each approach, the earlier one that always takes its place, or null. The lead takes the available approach with the
    // highest skill and a tie goes to the first, so an earlier one on the same skill wins whenever it is available, and it is
    // whenever this one is if this one needs everything it needs. Advice for the author, not a refusal.
    public static IReadOnlyList<int?> ShadowedBy(IReadOnlyList<ApproachModel> approaches)
    {
        static bool Covers(RequirementModel later, RequirementModel earlier) =>
            earlier.Trait is not null ? later == earlier
            : earlier.Gear is { } gear ? later.Gear == gear
            : later.Skill == earlier.Skill && (later.Level ?? 0) >= (earlier.Level ?? 0);

        var shadowed = new List<int?>();
        for (int i = 0; i < approaches.Count; i++)
        {
            ApproachModel later = approaches[i];
            int? by = null;
            for (int e = 0; e < i && by is null; e++)
            {
                ApproachModel earlier = approaches[e];
                if (later.Skill.Length > 0 && earlier.Skill == later.Skill && earlier.Requires.All(r => later.Requires.Any(l => Covers(l, r))))
                {
                    by = e;
                }
            }

            shadowed.Add(by);
        }

        return shadowed;
    }

    // The approaches as the form holds them, for advice drawn on the page; a field that fails its check is read as best it can be.
    public static List<ApproachModel> Read(FormValues values, FormChoices choices)
    {
        return BindApproaches(values, choices, []);
    }

    public override string? NameOf(ContentFileSet files, string id)
    {
        return ContentEdits.Get<EncounterModel>(files, Kind, id)?.Name;
    }

    public override FormChoices Widen(FormChoices choices, ContentFileSet files, string id)
    {
        return choices with { FixedDecisionId = ContentEdits.Get<EncounterModel>(files, Kind, id)?.Decision?.Id };
    }

    public override FormValues? Load(ContentFileSet files, string id, FormChoices choices)
    {
        if (ContentEdits.Get<EncounterModel>(files, Kind, id) is not { } encounter)
        {
            return null;
        }

        var fields = new List<KeyValuePair<string, string>>
        {
            new("name", Name(encounter.Name)),
            new("tags", string.Join(", ", encounter.Tags)),
            new("notes", encounter.Notes ?? ""),
        };
        if (encounter.CommsBlocked)
        {
            fields.Add(new("commsBlocked", "true"));
        }

        fields.Add(new("chain", encounter.Chain ?? ""));
        OwnedText.Add(fields, "", NarrativeFacts.EncounterTexts, encounter.Text);
        for (int i = 0; i < encounter.Approaches.Count; i++)
        {
            AddApproach(fields, i, encounter.Approaches[i], choices);
        }

        if (encounter.Decision is { } decision)
        {
            fields.Add(new("hasDecision", "true"));
            fields.Add(new("decision.name", Name(decision.Name)));
            fields.Add(new("decision.stakes", Checks.Text(decision.Stakes)));
            fields.Add(new("decision.chronicleWeight", decision.ChronicleWeight is { } weight ? Checks.Text(weight) : ""));
            AddEffects(fields, Bold, decision.Bold);
            AddEffects(fields, Safe, decision.Safe);
            OwnedText.Add(fields, "decision.", NarrativeFacts.DecisionTexts, decision.Text);
            OwnedText.AddLabels(fields, decision.Labels);
        }

        OwnedText.AddLines(fields, encounter.Lines);

        return new FormValues(fields);
    }

    public override FormValues Blank(FormChoices choices)
    {
        return new FormValues([new("name", ""), new("tags", ""), new("notes", ""), new("chain", ""), new(Approaches.Name(0, "id"), ""), new(Approaches.Name(0, "name"), ""), new(Approaches.Name(0, "skill"), ""), new(Approaches.Name(0, "difficulty"), "1"), new(Approaches.Name(0, "gearTag"), "")]);
    }

    public override Bound Bind(string id, FormValues values, FormChoices choices)
    {
        var errors = new Dictionary<string, string>();
        string? name = Checks.Name(values, errors);
        List<string> tags = Checks.Tags(values, "tags", errors);
        bool commsBlocked = values.Get("commsBlocked") == "true";
        string? notes = StoryletParts.Notes(values);
        string chain = values.Get("chain");
        if (chain.Length > 0 && choices.Encounters.All(e => e.Id != chain))
        {
            errors["chain"] = "Unknown encounter.";
        }

        if (chain == id)
        {
            errors["chain"] = "Chains to itself.";
        }

        IReadOnlyDictionary<string, string>? text = OwnedText.Bind(values, "", NarrativeFacts.EncounterTexts, errors);
        List<ApproachModel> approaches = BindApproaches(values, choices, errors);
        IReadOnlyList<OwnLineModel>? lines = OwnedText.BindLines(values, "encounter", errors, approaches.Select(a => a.Id).ToList());
        bool hasDecision = values.Get("hasDecision") == "true";
        string? typedId = null;
        string? decisionName = null;
        int stakes = 0;
        List<EffectModel> bold = [];
        List<EffectModel> safe = [];
        IReadOnlyDictionary<string, string>? decisionText = null;
        IReadOnlyDictionary<string, string>? labels = null;
        int? chronicleWeight = null;
        if (hasDecision)
        {
            if (choices.FixedDecisionId is null)
            {
                typedId = values.Get("decision.id").Trim();
                Checks.DecisionId(typedId, id, choices, "decision.id", errors);
            }

            decisionName = Checks.Name(values, errors, "decision.name");
            stakes = Checks.Whole(values, "decision.stakes", 1, 5, errors);
            chronicleWeight = Checks.OptionalWhole(values, "decision.chronicleWeight", 0, Sim.Core.ChronicleTuning.Most, errors);
            bold = BindEffects(values, Bold, errors);
            safe = BindEffects(values, Safe, errors);
            decisionText = OwnedText.Bind(values, "decision.", NarrativeFacts.DecisionTexts, errors);
            labels = OwnedText.BindLabels(values, errors);
        }

        if (errors.Count > 0)
        {
            return new Bound(null, errors);
        }

        return new Bound(files =>
        {
            DecisionModel? decision = null;
            if (hasDecision)
            {
                string decisionId = typedId ?? choices.FixedDecisionId!;
                // The quick check above reads the files as they were; this one decides, under the save's lock.
                if (typedId is not null && FormChoicesReader.DecisionOwnersIn(files).TryGetValue(typedId, out string? owner) && owner != id)
                {
                    throw new ChangeRefusedException($"That id is already used by {owner}.");
                }

                decision = new DecisionModel(decisionId, decisionName, stakes, bold, safe, decisionText, labels, chronicleWeight);
            }

            return new EncounterModel(id, name, tags, commsBlocked, chain.Length == 0 ? null : chain, approaches, decision, text, lines, notes);
        }, errors);
    }

    private static void AddApproach(List<KeyValuePair<string, string>> fields, int row, ApproachModel approach, FormChoices choices)
    {
        fields.Add(new(Approaches.Name(row, "id"), approach.Id));
        fields.Add(new(Approaches.Name(row, "name"), Name(approach.Name)));
        fields.Add(new(Approaches.Name(row, "skill"), approach.Skill));
        fields.Add(new(Approaches.Name(row, "difficulty"), Checks.Text(approach.Difficulty)));
        fields.Add(new(Approaches.Name(row, "gearTag"), approach.GearTag ?? ""));
        for (int r = 0; r < Math.Min(RequirementRows, approach.Requires.Count); r++)
        {
            RequirementModel requirement = approach.Requires[r];
            string Field(string part) => Approaches.Name(row, RequirementField(r, part));
            fields.Add(new(Field("what"), requirement.Trait is not null ? TraitWhat : requirement.Skill ?? (requirement.Gear is { } gear ? GearPrefix + gear : "")));
            fields.Add(new(Field("level"), requirement.Skill is not null && requirement.Level is { } level ? Checks.Text(level) : ""));
            if (requirement.Trait is { } trait)
            {
                // An is shows as at least and at most both at its value, when the form can tell what that is.
                int? exact = requirement.Is is { } named ? IsValue(named, choices.Expressions.Trait(trait)) : null;
                fields.Add(new(Field("trait"), trait));
                fields.Add(new(Field("min"), (exact ?? requirement.Min) is { } min ? Checks.Text(min) : ""));
                fields.Add(new(Field("max"), (exact ?? requirement.Max) is { } max ? Checks.Text(max) : ""));
                fields.Add(new(Field("on"), requirement.On ?? ""));
                fields.Add(new(Field("is"), requirement.Is ?? ""));
            }
        }

        // The form edits one effect a tier, so a tier written as several entries shows as their sum, as the loader adds them.
        foreach ((string tier, IReadOnlyList<EffectModel> effects) in approach.Outcomes)
        {
            static string Sum(IEnumerable<int?> values)
            {
                List<int> given = values.OfType<int>().ToList();
                return given.Count == 0 ? "" : Checks.Text(given.Sum());
            }

            fields.Add(new(Approaches.Name(row, OutcomeField(tier, "own")), "true"));
            fields.Add(new(Approaches.Name(row, OutcomeField(tier, "progress")), Sum(effects.Select(e => e.Progress))));
            fields.Add(new(Approaches.Name(row, OutcomeField(tier, "momentum")), Sum(effects.Select(e => e.Momentum))));
            fields.Add(new(Approaches.Name(row, OutcomeField(tier, "strain")), Sum(effects.Select(e => e.Strain))));
            fields.Add(new(Approaches.Name(row, OutcomeField(tier, "injury")), effects.LastOrDefault(e => e.InjuryRoll is not null)?.InjuryRoll ?? ""));
            // The form holds one trait change a tier; an outcome with more is marked as content the editor cannot keep.
            AddTrait(fields, f => Approaches.Name(row, OutcomeField(tier, f)), effects.FirstOrDefault(e => e.Trait is not null), toward: true);
        }

        OwnedText.Add(fields, Approaches.Name(row, ""), NarrativeFacts.ApproachTexts, approach.Text);
    }

    private static void AddEffects(List<KeyValuePair<string, string>> fields, RowSet set, IReadOnlyList<EffectModel> effects)
    {
        for (int i = 0; i < effects.Count; i++)
        {
            EffectModel effect = effects[i];
            fields.Add(new(set.Name(i, "progress"), effect.Progress is { } p ? Checks.Text(p) : ""));
            fields.Add(new(set.Name(i, "momentum"), effect.Momentum is { } m ? Checks.Text(m) : ""));
            fields.Add(new(set.Name(i, "strain"), effect.Strain is { } s ? Checks.Text(s) : ""));
            fields.Add(new(set.Name(i, "injury"), effect.InjuryRoll ?? ""));
            if (effect.Withdraw)
            {
                fields.Add(new(set.Name(i, "withdraw"), "true"));
            }

            int row = i;
            AddTrait(fields, f => set.Name(row, f), effect);
        }
    }

    private static void AddTrait(List<KeyValuePair<string, string>> fields, Func<string, string> name, EffectModel? effect, bool toward = false)
    {
        fields.Add(new(name("trait"), effect?.Trait ?? ""));
        fields.Add(new(name("traitMode"), effect?.Trait is null ? "" : effect.TraitSet ? "set" : "add"));
        fields.Add(new(name("traitAmount"), effect?.TraitAmount is { } amount ? Checks.Text(amount) : ""));
        fields.Add(new(name("traitOn"), effect?.TraitOn ?? ""));
        if (toward)
        {
            fields.Add(new(name("traitToward"), effect?.TraitToward ?? ""));
        }
    }

    // The trait change a form's fields hold, or null when the trait is blank (an amount without one is refused). Each problem is put beside its field.
    private static EffectModel? BindTrait(FormValues values, Func<string, string> name, IReadOnlyList<string> targets, Dictionary<string, string> errors, bool toward = false)
    {
        string trait = values.Get(name("trait")).Trim();
        string mode = values.Get(name("traitMode"));
        string on = values.Get(name("traitOn"));
        if (trait.Length == 0)
        {
            if (values.Get(name("traitAmount")).Trim().Length > 0)
            {
                errors[name("trait")] = "Enter a trait id.";
            }

            return null;
        }

        if (!Ids.IsInnerId(trait))
        {
            errors[name("trait")] = Ids.InnerRule;
        }

        if (mode is not ("add" or "set"))
        {
            errors[name("traitMode")] = "Choose add or set.";
        }

        int amount = Checks.Whole(values, name("traitAmount"), mode == "set" ? 0 : null, null, errors);
        if (!targets.Contains(on))
        {
            errors[name("traitOn")] = on.Length == 0 ? "Choose who it is on."
                : $"Choose {string.Join(", ", targets.Take(targets.Count - 1))}, or {targets[^1]}.";
        }

        string? towardWho = toward ? values.Get(name("traitToward")) : "";
        if (towardWho.Length > 0 && !Towards.Contains(towardWho))
        {
            errors[name("traitToward")] = "Choose lead or actor, or leave it blank.";
        }

        return new EffectModel(null, null, null, null, false, trait, amount, mode == "set", on, towardWho.Length == 0 ? null : towardWho);
    }

    // One row is one approach, in the order shown. The count and the ids are checked here as the loader checks them.
    private static List<ApproachModel> BindApproaches(FormValues values, FormChoices choices, Dictionary<string, string> errors)
    {
        int rows = FormRows.Read(values, Approaches).Count;
        if (rows == 0)
        {
            errors[Approaches.Prefix] = "An encounter needs at least one approach.";
        }
        else if (rows > EncounterDef.MaxApproaches)
        {
            errors[Approaches.Prefix] = $"An encounter may have at most {EncounterDef.MaxApproaches} approaches.";
        }

        var approaches = new List<ApproachModel>();
        var ids = new HashSet<string>();
        for (int i = 0; i < rows; i++)
        {
            string idField = Approaches.Name(i, "id");
            string approachId = values.Get(idField).Trim();
            if (approachId.Length == 0)
            {
                errors[idField] = "Enter an id.";
            }
            else if (!Ids.IsInnerId(approachId))
            {
                errors[idField] = Ids.InnerRule;
            }
            else if (!ids.Add(approachId))
            {
                errors[idField] = $"Approach id '{approachId}' is used twice.";
            }

            string? name = Checks.Name(values, errors, Approaches.Name(i, "name"));
            string skillField = Approaches.Name(i, "skill");
            string skill = values.Get(skillField);
            if (skill.Length == 0)
            {
                errors[skillField] = "Choose a skill.";
            }
            else if (choices.Skills.All(s => s.Id != skill))
            {
                errors[skillField] = "Unknown skill.";
            }

            int difficulty = Checks.Whole(values, Approaches.Name(i, "difficulty"), 1, null, errors);
            string gearField = Approaches.Name(i, "gearTag");
            string gearTag = values.Get(gearField);
            if (gearTag.Length > 0 && !choices.GearTags.Contains(gearTag))
            {
                errors[gearField] = "Unknown gear tag.";
            }

            List<RequirementModel> requires = BindRequirements(values, i, choices, errors);
            Dictionary<string, IReadOnlyList<EffectModel>> outcomes = BindOutcomes(values, i, errors);
            approaches.Add(new ApproachModel(approachId, name, skill, difficulty, gearTag.Length == 0 ? null : gearTag, requires, outcomes, OwnedText.Bind(values, Approaches.Name(i, ""), NarrativeFacts.ApproachTexts, errors)));
        }

        if (!errors.ContainsKey(Approaches.Prefix) && approaches.Count > 0 && approaches.All(a => a.Requires.Count > 0))
        {
            errors[Approaches.Prefix] = "At least one approach must have no requirements.";
        }

        return approaches;
    }

    // The requirements in the order of their boxes; a box left empty is skipped.
    private static List<RequirementModel> BindRequirements(FormValues values, int row, FormChoices choices, Dictionary<string, string> errors)
    {
        var requires = new List<RequirementModel>();
        for (int r = 0; r < RequirementRows; r++)
        {
            string whatField = Approaches.Name(row, RequirementField(r, "what"));
            string what = values.Get(whatField);
            if (what.Length == 0)
            {
                continue;
            }

            if (what == TraitWhat)
            {
                requires.Add(BindTraitCondition(values, f => Approaches.Name(row, RequirementField(r, f)), choices, errors));
                continue;
            }

            if (what.StartsWith(GearPrefix, StringComparison.Ordinal))
            {
                string tag = what[GearPrefix.Length..];
                if (!choices.GearTags.Contains(tag))
                {
                    errors[whatField] = "Unknown gear tag.";
                }

                requires.Add(new RequirementModel(null, null, tag));
                continue;
            }

            if (choices.Skills.All(s => s.Id != what))
            {
                errors[whatField] = "Unknown skill.";
            }

            requires.Add(new RequirementModel(what, Checks.Whole(values, Approaches.Name(row, RequirementField(r, "level")), 1, null, errors), null));
        }

        return requires;
    }

    // A trait condition (1e part six): a trait the pack defines, at least and at most (blank for the loader's default: at least 1, or 0
    // when at most is given), and on. The is it was written with is kept while both stay at its value, or while both are blank.
    private static RequirementModel BindTraitCondition(FormValues values, Func<string, string> field, FormChoices choices, Dictionary<string, string> errors)
    {
        string trait = values.Get(field("trait")).Trim();
        TraitChoice? def = choices.Expressions.Trait(trait);
        if (trait.Length == 0)
        {
            errors[field("trait")] = "Choose a trait.";
        }
        else if (choices.Expressions.Traits.Count > 0 && def is null)
        {
            errors[field("trait")] = "Unknown trait.";
        }
        else if (def is { OnPair: true })
        {
            errors[field("trait")] = $"{trait} sits on a pair, so it cannot be a condition.";
        }

        string on = values.Get(field("on"));
        if (def is not { OnPair: true } && ConditionOns(def).All(c => c.Value != on))
        {
            errors[field("on")] = on.Length == 0 ? "Choose who it is on." : $"{trait} cannot be on {on}.";
        }

        int? min = Checks.OptionalWhole(values, field("min"), 0, null, errors);
        int? max = Checks.OptionalWhole(values, field("max"), 0, null, errors);
        if (max < (min ?? (max is null ? 1 : 0)))
        {
            errors[field("max")] = "At most is below at least.";
        }

        string written = values.Get(field("is"));
        string? exact = null;
        if (written.Length > 0 && ((min is null && max is null) || (IsValue(written, def) is { } value && min == value && max == value)))
        {
            (exact, min, max) = (written, null, null);
        }

        return new RequirementModel(null, null, null, trait, min, max, exact, on.Length == 0 ? null : on);
    }

    // Each written tier as one entry per value given, in the default's order (progress, strain, momentum, injury), then its trait change;
    // none given is an empty list.
    private static Dictionary<string, IReadOnlyList<EffectModel>> BindOutcomes(FormValues values, int row, Dictionary<string, string> errors)
    {
        var outcomes = new Dictionary<string, IReadOnlyList<EffectModel>>();
        foreach (string tier in NarrativeFacts.Tiers)
        {
            string Field(string field) => Approaches.Name(row, OutcomeField(tier, field));
            if (values.Get(Field("own")) != "true")
            {
                continue;
            }

            int? progress = Checks.OptionalWhole(values, Field("progress"), null, null, errors);
            int? momentum = Checks.OptionalWhole(values, Field("momentum"), null, null, errors);
            int? strain = Checks.OptionalWhole(values, Field("strain"), null, null, errors);
            string injury = values.Get(Field("injury"));
            if (injury.Length > 0 && injury is not ("light" or "severe"))
            {
                errors[Field("injury")] = "Choose light or severe.";
            }

            var effects = new List<EffectModel>();
            if (progress is { } p)
            {
                effects.Add(new EffectModel(p, null, null, null));
            }

            if (strain is { } s)
            {
                effects.Add(new EffectModel(null, null, s, null));
            }

            if (momentum is { } m)
            {
                effects.Add(new EffectModel(null, m, null, null));
            }

            if (injury.Length > 0)
            {
                effects.Add(new EffectModel(null, null, null, injury));
            }

            if (BindTrait(values, Field, OutcomeTargets, errors, toward: true) is { } trait)
            {
                effects.Add(trait);
            }

            outcomes[tier] = effects;
        }

        return outcomes;
    }

    public const string ApproachIdRule = Ids.InnerRule + " Unique in this encounter.";

    // One row is one entry: a field per key, blank meaning the key is absent.
    private static List<EffectModel> BindEffects(FormValues values, RowSet set, Dictionary<string, string> errors)
    {
        var effects = new List<EffectModel>();
        int rows = FormRows.Read(values, set).Count;
        for (int i = 0; i < rows; i++)
        {
            int before = errors.Count;
            int? progress = Checks.OptionalWhole(values, set.Name(i, "progress"), null, null, errors);
            int? momentum = Checks.OptionalWhole(values, set.Name(i, "momentum"), null, null, errors);
            int? strain = Checks.OptionalWhole(values, set.Name(i, "strain"), null, null, errors);
            string injury = values.Get(set.Name(i, "injury"));
            if (injury.Length > 0 && injury is not ("light" or "severe"))
            {
                errors[set.Name(i, "injury")] = "Choose light or severe.";
            }

            bool withdraw = values.Get(set.Name(i, "withdraw")) == "true";
            int row = i;
            EffectModel? trait = BindTrait(values, f => set.Name(row, f), AnswerTargets, errors);
            if (errors.Count == before && progress is null && momentum is null && strain is null && injury.Length == 0 && !withdraw && trait is null)
            {
                errors[set.Name(i, "progress")] = "Give the effect a value.";
            }

            effects.Add(new EffectModel(progress, momentum, strain, injury.Length == 0 ? null : injury, withdraw, trait?.Trait, trait?.TraitAmount, trait?.TraitSet ?? false, trait?.TraitOn));
        }

        return effects;
    }
}
