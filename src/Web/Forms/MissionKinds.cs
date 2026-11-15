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
    // An approach's words post with its row, as approaches[0].text.attempt, so they move and go with it.
    public static readonly RowSet Approaches = new(
        "approaches",
        ["id", "name", "skill", "difficulty", "gearTag", .. NarrativeFacts.ApproachTexts.Select(TextField)],
        Qualified: true,
        Noun: "approach",
        Defaults: new Dictionary<string, string> { ["difficulty"] = "1" });

    public static readonly RowSet Bold = new("bold", ["progress", "momentum", "strain", "injury", "withdraw"], Qualified: true, Noun: "effect");

    public static readonly RowSet Safe = new("safe", ["progress", "momentum", "strain", "injury", "withdraw"], Qualified: true, Noun: "effect");

    public override ContentKind Kind => ContentKind.Encounter;

    public override IReadOnlyList<RowSet> RowSets => [Approaches, Bold, Safe, OwnedText.Lines];

    // The row field that holds an approach's text for this key.
    public static string TextField(string key) => OwnedText.Field("", key);

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
        };
        if (encounter.CommsBlocked)
        {
            fields.Add(new("commsBlocked", "true"));
        }

        fields.Add(new("chain", encounter.Chain ?? ""));
        OwnedText.Add(fields, "", NarrativeFacts.EncounterTexts, encounter.Text);
        for (int i = 0; i < encounter.Approaches.Count; i++)
        {
            AddApproach(fields, i, encounter.Approaches[i]);
        }

        if (encounter.Decision is { } decision)
        {
            fields.Add(new("hasDecision", "true"));
            fields.Add(new("decision.name", Name(decision.Name)));
            fields.Add(new("decision.stakes", Checks.Text(decision.Stakes)));
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
        return new FormValues([new("name", ""), new("tags", ""), new("chain", ""), new(Approaches.Name(0, "id"), ""), new(Approaches.Name(0, "name"), ""), new(Approaches.Name(0, "skill"), ""), new(Approaches.Name(0, "difficulty"), "1"), new(Approaches.Name(0, "gearTag"), "")]);
    }

    public override Bound Bind(string id, FormValues values, FormChoices choices)
    {
        var errors = new Dictionary<string, string>();
        string? name = Checks.Name(values, errors);
        List<string> tags = Checks.Tags(values, "tags", errors);
        bool commsBlocked = values.Get("commsBlocked") == "true";
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
        if (hasDecision)
        {
            if (choices.FixedDecisionId is null)
            {
                typedId = values.Get("decision.id").Trim();
                Checks.DecisionId(typedId, id, choices, "decision.id", errors);
            }

            decisionName = Checks.Name(values, errors, "decision.name");
            stakes = Checks.Whole(values, "decision.stakes", 1, 5, errors);
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

                decision = new DecisionModel(decisionId, decisionName, stakes, bold, safe, decisionText, labels);
            }

            return new EncounterModel(id, name, tags, commsBlocked, chain.Length == 0 ? null : chain, approaches, decision, text, lines);
        }, errors);
    }

    private static void AddApproach(List<KeyValuePair<string, string>> fields, int row, ApproachModel approach)
    {
        fields.Add(new(Approaches.Name(row, "id"), approach.Id));
        fields.Add(new(Approaches.Name(row, "name"), Name(approach.Name)));
        fields.Add(new(Approaches.Name(row, "skill"), approach.Skill));
        fields.Add(new(Approaches.Name(row, "difficulty"), Checks.Text(approach.Difficulty)));
        fields.Add(new(Approaches.Name(row, "gearTag"), approach.GearTag ?? ""));
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
        }
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
            else if (!ContentLoader.IsApproachId(approachId))
            {
                errors[idField] = ApproachIdRule;
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

            approaches.Add(new ApproachModel(approachId, name, skill, difficulty, gearTag.Length == 0 ? null : gearTag, OwnedText.Bind(values, Approaches.Name(i, ""), NarrativeFacts.ApproachTexts, errors)));
        }

        return approaches;
    }

    public const string ApproachIdRule = "1 to 24 lower case letters, digits, and hyphens, unique in this encounter.";

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
            if (errors.Count == before && progress is null && momentum is null && strain is null && injury.Length == 0 && !withdraw)
            {
                errors[set.Name(i, "progress")] = "Give the effect a value.";
            }

            effects.Add(new EffectModel(progress, momentum, strain, injury.Length == 0 ? null : injury, withdraw));
        }

        return effects;
    }
}
