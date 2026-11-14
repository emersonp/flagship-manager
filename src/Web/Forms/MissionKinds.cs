using Sim.Content;

namespace Web.Forms;

internal sealed class ObjectiveKind : KindForm
{
    public static readonly RowSet Stages = new("stages", ["slot", "draw", "order", "countMin", "countMax", "skippable"], MultiFields: ["draw", "order"], Noun: "stage", Defaults: new Dictionary<string, string> { ["countMin"] = "1", ["countMax"] = "1" });

    public override ContentKind Kind => ContentKind.Objective;

    public override IReadOnlyList<RowSet> RowSets => [Stages];

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

        return new Bound(errors.Count > 0 ? null : _ => new ObjectiveModel(id, name, tags, min, max, need, unattended, stages), errors);
    }
}

internal sealed class EncounterKind : KindForm
{
    public static readonly RowSet Bold = new("bold", ["progress", "momentum", "strain", "injury", "withdraw"], Qualified: true, Noun: "effect");

    public static readonly RowSet Safe = new("safe", ["progress", "momentum", "strain", "injury", "withdraw"], Qualified: true, Noun: "effect");

    public override ContentKind Kind => ContentKind.Encounter;

    public override IReadOnlyList<RowSet> RowSets => [Bold, Safe];

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
        AddApproach(fields, "careful", encounter.Careful);
        AddApproach(fields, "fast", encounter.Fast);
        if (encounter.Decision is { } decision)
        {
            fields.Add(new("hasDecision", "true"));
            fields.Add(new("decision.name", Name(decision.Name)));
            fields.Add(new("decision.stakes", Checks.Text(decision.Stakes)));
            AddEffects(fields, Bold, decision.Bold);
            AddEffects(fields, Safe, decision.Safe);
        }

        return new FormValues(fields);
    }

    public override FormValues Blank(FormChoices choices)
    {
        return new FormValues([new("name", ""), new("tags", ""), new("chain", ""), new("careful.skill", ""), new("careful.difficulty", "1"), new("careful.gearTag", ""), new("fast.skill", ""), new("fast.difficulty", "1"), new("fast.gearTag", "")]);
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

        ApproachModel? careful = BindApproach(values, "careful", choices, errors);
        ApproachModel? fast = BindApproach(values, "fast", choices, errors);
        bool hasDecision = values.Get("hasDecision") == "true";
        string? typedId = null;
        string? decisionName = null;
        int stakes = 0;
        List<EffectModel> bold = [];
        List<EffectModel> safe = [];
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

                decision = new DecisionModel(decisionId, decisionName, stakes, bold, safe);
            }

            return new EncounterModel(id, name, tags, commsBlocked, chain.Length == 0 ? null : chain, careful!, fast!, decision);
        }, errors);
    }

    private static void AddApproach(List<KeyValuePair<string, string>> fields, string key, ApproachModel approach)
    {
        fields.Add(new($"{key}.skill", approach.Skill));
        fields.Add(new($"{key}.difficulty", Checks.Text(approach.Difficulty)));
        fields.Add(new($"{key}.gearTag", approach.GearTag ?? ""));
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

    private static ApproachModel? BindApproach(FormValues values, string key, FormChoices choices, Dictionary<string, string> errors)
    {
        string skill = values.Get($"{key}.skill");
        if (skill.Length == 0)
        {
            errors[$"{key}.skill"] = "Choose a skill.";
        }
        else if (choices.Skills.All(s => s.Id != skill))
        {
            errors[$"{key}.skill"] = "Unknown skill.";
        }

        int difficulty = Checks.Whole(values, $"{key}.difficulty", 1, null, errors);
        string gearTag = values.Get($"{key}.gearTag");
        if (gearTag.Length > 0 && !choices.GearTags.Contains(gearTag))
        {
            errors[$"{key}.gearTag"] = "Unknown gear tag.";
        }

        return new ApproachModel(skill, difficulty, gearTag.Length == 0 ? null : gearTag);
    }

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
