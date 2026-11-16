using System.Globalization;
using Sim.Content;
using Sim.Core.Content;
using Sim.Core.Narrative;
using Sim.Narrative;
using Web.Bridge;

namespace Web.Forms;

// One preview in the panel: what the log would say (Entries), a sentence on which line that is (Note), and what is wrong with
// the author's own text, if anything (Problem). Id is the element the preview route patches.
public sealed record PlaysPreview(string Id, IReadOnlyList<ProseEntry>? Entries, string Note, string? Problem);

// A fixed text box of the item: the field it posts, its label, and, while it is blank, the text of the line that plays there now.
public sealed record PlaysSlot(string Field, string Label, string Placeholder, PlaysPreview Preview);

// A beat the item has no box for: what plays there now, and the place to start a line of the item's own from it.
public sealed record PlaysFallback(string Beat, PlaysPreview Preview);

// A line written inside the item: its row in the form, its beat, the criterion its item implies, and the facts its beat has.
public sealed record PlaysLine(int Index, string Beat, string Locked, PlaysPreview Preview);

public sealed record PlaysGroup(
    string Key,
    string Title,
    string? Hint,
    bool Primary,
    IReadOnlyList<PlaysSlot> Slots,
    IReadOnlyList<PlaysFallback> Fallbacks,
    IReadOnlyList<PlaysLine> Lines,
    IReadOnlyList<string> Chips,
    bool Labels);

// The "What plays here" panel of an encounter or an objective (slice 1d authoring design): every beat the item can produce, in
// mission order, with what the log says there now. Built from the form as it stands, so it previews words that are not saved.
// Cast names who the sample has in each role, so the author knows whose pronouns they are reading.
public sealed record PlaysView(int Sample, string Cast, IReadOnlyList<PlaysGroup> Groups, string? Message)
{
    public IEnumerable<PlaysPreview> Previews => Groups.SelectMany(g => g.Slots.Select(s => s.Preview).Concat(g.Fallbacks.Select(f => f.Preview)).Concat(g.Lines.Select(l => l.Preview)));

    // An approach's own words, drawn in its tab; null for a row the form does not have.
    public PlaysGroup? Approach(int row) => Groups.FirstOrDefault(g => g.Key == Plays.ApproachKey(row));

    // Everything that is not an approach's: what the panel draws.
    public IEnumerable<PlaysGroup> Shared => Groups.Where(g => !g.Key.StartsWith(Plays.ApproachPrefix, StringComparison.Ordinal));
}

internal static class Plays
{
    // A fixed box: the field, its label, the id its line takes (as the loader gives it), its beat, and the criteria its place implies.
    private sealed record Slot(string Field, string Label, string LineId, string Beat, IReadOnlyList<CriterionDef> When);

    private sealed record Group(string Key, string Title, string? Hint, bool Primary, IReadOnlyList<Slot> Slots, IReadOnlyList<string> Beats, bool Labels = false);

    // What the item is, read from the form: enough to say which beats can happen at it and to build a sample of each.
    // Approaches are the encounter's rows as the form holds them: the id, the title shown, and the skill; an objective has none.
    private sealed record Item(string Id, string Fact, string Name, IReadOnlyList<string> Tags, bool CommsBlocked, IReadOnlyList<(string Id, string Title, string Skill)> Approaches, string? DecisionId, int Stakes);

    private static readonly IReadOnlyDictionary<string, string> BeatTitles = new Dictionary<string, string>
    {
        ["stage"] = "Arrival",
        ["skip"] = "Skipped",
        ["chain"] = "Arrival by chain",
        ["attempt"] = "Before a check",
        ["check"] = "A check",
        ["effects"] = "Effects",
        ["injury"] = "Injury",
        ["lead"] = "Lead change",
        ["call"] = "The lead calls in",
        ["initiative"] = "The lead decides alone",
        ["answer"] = "The captain answers",
        ["collapse"] = "Team collapse",
        ["result"] = "Result",
    };

    public static string Title(string beat) => BeatTitles.GetValueOrDefault(beat, beat);

    // An effect as the encounter form says it: "+1 progress, -1 strain, +1 momentum", "light injury roll", or "Nothing".
    public static string EffectWords(EffectDef effect)
    {
        var parts = new List<string>();
        void Add(int value, string what)
        {
            if (value != 0)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{(value > 0 ? "+" : "")}{value} {what}"));
            }
        }

        Add(effect.Progress, "progress");
        Add(effect.Strain, "strain");
        Add(effect.Momentum, "momentum");
        if (effect.InjuryRoll is { } injury)
        {
            parts.Add($"{injury.ToString().ToLowerInvariant()} injury roll");
        }

        if (effect.Withdraw)
        {
            parts.Add("withdraw");
        }

        return parts.Count == 0 ? "Nothing" : string.Join(", ", parts);
    }

    // The group of an approach's own words, by its row: the encounter form draws it in that approach's tab, not in the panel.
    public const string ApproachPrefix = "approach-";

    public static string ApproachKey(int row) => string.Create(CultureInfo.InvariantCulture, $"{ApproachPrefix}{row}");

    // How much of an item is written, for the kind's list: its boxes that hold words, of how many, and its lines.
    public static string Written(IContentModel model)
    {
        (int written, int total, int lines) = model switch
        {
            EncounterModel e => (
                Count(e.Text) + e.Approaches.Sum(a => Count(a.Text)) + Count(e.Decision?.Text),
                NarrativeFacts.EncounterTexts.Count + (e.Approaches.Count * NarrativeFacts.ApproachTexts.Count) + (e.Decision is null ? 0 : NarrativeFacts.DecisionTexts.Count),
                e.Lines?.Count ?? 0),
            ObjectiveModel o => (Count(o.Text), NarrativeFacts.Outcomes.Count, o.Lines?.Count ?? 0),
            _ => (0, 0, 0),
        };
        string boxes = string.Create(CultureInfo.InvariantCulture, $"{written} of {total}");
        return lines == 0 ? boxes : string.Create(CultureInfo.InvariantCulture, $"{boxes}, {lines} {(lines == 1 ? "line" : "lines")}");

        static int Count(IReadOnlyDictionary<string, string>? texts) => texts?.Count ?? 0;
    }

    // Null for a kind that has no panel.
    public static PlaysView? Of(ContentKind kind, ContentFileSet files, string id, FormValues values, FormChoices choices)
    {
        if (kind is not (ContentKind.Encounter or ContentKind.Objective))
        {
            return null;
        }

        int sample = int.TryParse(values.Get("sample"), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n % 1000 : 0;
        Item item = Read(kind, id, values, choices);
        List<Group> groups = kind == ContentKind.Encounter ? EncounterGroups(item, values) : ObjectiveGroups(item);
        ContentPack? pack = files.Validate().Pack;
        var names = pack is null ? null : new Names(pack);
        Dictionary<string, SymbolDef> symbols = pack?.Symbols.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First()) ?? [];

        // The pool as it would be if the form were saved: every line but this item's stored ones, and the item's as the form has them.
        List<Dictionary<string, string>> rows = FormRows.Read(values, OwnedText.Lines);
        var implied = new CriterionDef(item.Fact, [item.Id]);
        var own = new List<LineDef>();
        foreach (Slot slot in groups.SelectMany(g => g.Slots))
        {
            string text = OwnedText.Clean(values.Get(slot.Field));
            if (text.Length > 0)
            {
                own.Add(new LineDef(slot.LineId, slot.Beat, slot.When, text, null, item.Id));
            }
        }

        var rowLines = new List<LineDef?>();
        for (int i = 0; i < rows.Count; i++)
        {
            string beat = rows[i]["beat"].Trim();
            string text = OwnedText.Clean(rows[i]["text"]);
            List<CriterionDef>? criteria = RowCriteria(rows[i], beat);
            LineDef? line = criteria is null || text.Length == 0 || !NarrativeFacts.Beats.ContainsKey(beat)
                ? null
                : new LineDef(RowId(item, i), beat, [implied, .. criteria], text, null, item.Id);
            rowLines.Add(line);
            if (line is not null)
            {
                own.Add(line);
            }
        }

        List<LineDef> lines = [.. (pack?.Lines ?? []).Where(l => l.Owner != item.Id), .. own];
        IReadOnlyList<LineDef> reportable = Prose.Reportable(lines);

        PlaysPreview Preview(string elementId, string beat, IReadOnlyList<CriterionDef> when, string? ownId, string ownText)
        {
            if (pack is null)
            {
                return new PlaysPreview(elementId, null, "", null);
            }

            if (Impossible(item, beat, when) is { } never)
            {
                return new PlaysPreview(elementId, null, "", never);
            }

            Situation situation = Sample(pack, names!, item, beat, when, sample);
            string key = string.Create(CultureInfo.InvariantCulture, $"plays|{item.Id}|{elementId}|{sample}");
            string? problem = ownText.Length > 0 ? Problem(ownText, situation, symbols, key) : null;
            BeatProse? prose = Prose.Render(lines, reportable, symbols, situation, key, []);
            string? winner = prose?.LineId;
            string note = prose is null ? "No line holds here, so the log shows the systems line."
                : winner is null ? "Only what is said before the check is written; the result shows the systems line."
                : winner == ownId ? (ownId!.Contains(".lines[", StringComparison.Ordinal) ? "This line wins in this sample." : "Its own words.")
                : lines.First(l => l.Id == winner).Owner == item.Id ? (ownText.Length > 0 ? $"{Describe(winner, item)} wins in this sample: it tests as much or more." : $"{Describe(winner, item)} plays here.")
                : ownText.Length > 0 ? $"Shared line {winner} wins in this sample: it tests as much or more."
                : $"Shared line {winner}.";
            return new PlaysPreview(elementId, prose?.Entries, note, problem);
        }

        string Placeholder(Slot slot)
        {
            if (pack is null || Impossible(item, slot.Beat, slot.When) is not null)
            {
                return "";
            }

            Situation situation = Sample(pack, names!, item, slot.Beat, slot.When, sample);
            return LineSelector.Select(lines.Where(l => l.Id != slot.LineId).ToList(), situation, new HashSet<string>(), "plays|placeholder")?.Text ?? "";
        }

        var built = new List<PlaysGroup>();
        var placed = new HashSet<int>();
        foreach (Group group in groups)
        {
            var slots = group.Slots.Select(s => new PlaysSlot(s.Field, s.Label, Placeholder(s), Preview("pv-" + FormValues.Slug(s.Field), s.Beat, s.When, s.LineId, OwnedText.Clean(values.Get(s.Field))))).ToList();
            var fallbacks = group.Beats.Where(b => group.Slots.All(s => s.Beat != b))
                .Select(b => new PlaysFallback(b, Preview($"pv-any-{group.Key}-{b}", b, [implied], null, ""))).ToList();
            var groupLines = new List<PlaysLine>();
            for (int i = 0; i < rows.Count; i++)
            {
                string beat = rows[i]["beat"].Trim();
                if (group.Beats.Contains(beat) && placed.Add(i))
                {
                    groupLines.Add(Line(i, beat));
                }
            }

            built.Add(new PlaysGroup(group.Key, group.Title, group.Hint, group.Primary, slots, fallbacks, groupLines, Chips(group, symbols.Keys), group.Labels));
        }

        // A line whose beat no group has (a beat that is not one, or one this item never produces) is still shown, so it can be fixed or removed.
        List<PlaysLine> stray = Enumerable.Range(0, rows.Count).Where(i => !placed.Contains(i)).Select(i => Line(i, rows[i]["beat"].Trim())).ToList();
        if (stray.Count > 0)
        {
            built.Add(new PlaysGroup("other", "Other lines", "These lines name a beat this page has no place for.", true, [], [], stray, [], false));
        }

        string cast = pack is { Crew.Count: > 0 } ? $"Sample {sample + 1}: {names!.Crew(Cast(pack, sample, 0).Id)} leads, {names.Crew(Cast(pack, sample, 1).Id)} acts." : "";
        return new PlaysView(sample, cast, built, pack is null ? "The pack on disk does not load, so there is nothing to preview against. The boxes still save." : null);

        PlaysLine Line(int i, string beat)
        {
            LineDef? line = rowLines[i];
            PlaysPreview preview = line is null
                ? new PlaysPreview(PreviewId(i), null, "", OwnedText.Clean(rows[i]["text"]).Length == 0 ? null : "Fix the criteria to see a preview.")
                : Preview(PreviewId(i), line.Beat, line.When, line.Id, line.Text);
            return new PlaysLine(i, beat, $"{item.Fact}: {item.Id}", preview);
        }
    }

    // The text the "Write one for here" action starts a new line with: what plays at that beat now, as it is written.
    public static string FallbackText(ContentKind kind, ContentFileSet files, string id, FormValues values, FormChoices choices, string beat)
    {
        if (files.Validate().Pack is not { } pack || !NarrativeFacts.Beats.ContainsKey(beat))
        {
            return "";
        }

        Item item = Read(kind, id, values, choices);
        Situation situation = Sample(pack, new Names(pack), item, beat, [new CriterionDef(item.Fact, [item.Id])], 0);
        return LineSelector.Select(pack.Lines.Where(l => l.Owner is null).ToList(), situation, new HashSet<string>(), "plays|write")?.Text ?? "";
    }

    private static string PreviewId(int row) => string.Create(CultureInfo.InvariantCulture, $"pv-line-{row}");

    private static string RowId(Item item, int row) => string.Create(CultureInfo.InvariantCulture, $"{item.Id}.lines[{row + 1}]");

    private static string Describe(string lineId, Item item)
    {
        int at = lineId.IndexOf(".lines[", StringComparison.Ordinal);
        return at >= 0 ? $"Line {lineId[(at + 7)..^1]} of its own" : $"Its own text ({lineId[(item.Id.Length + 1)..]})";
    }

    // The row's criteria as the loader would read them, or null when one cannot be read.
    private static List<CriterionDef>? RowCriteria(IReadOnlyDictionary<string, string> row, string beat)
    {
        var criteria = new List<CriterionDef>();
        foreach ((_, string fact, string value) in OwnedText.Criteria(row))
        {
            if (NarrativeFacts.Fact(beat, fact) is null || criteria.Any(c => c.Fact == fact) || LineKind.ParseValue(fact, value, out _) is not { } model)
            {
                return null;
            }

            criteria.Add(new CriterionDef(model.Fact, model.Values, model.Min, model.Max));
        }

        return criteria;
    }

    private static Item Read(ContentKind kind, string id, FormValues values, FormChoices choices)
    {
        string shown = id.Length > 0 ? id : kind == ContentKind.Encounter ? "NEW_ENCOUNTER" : "NEW_OBJECTIVE";
        string name = values.Get("name").Trim() is { Length: > 0 } typed ? typed : shown;
        List<string> tags = values.Get("tags").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (kind == ContentKind.Objective)
        {
            return new Item(shown, "objective", name, tags, false, [], null, 1);
        }

        bool hasDecision = values.Get("hasDecision") == "true";
        string? decision = !hasDecision ? null : choices.FixedDecisionId ?? (values.Get("decision.id").Trim() is { Length: > 0 } d ? d : "NEW_DECISION");
        int stakes = int.TryParse(values.Get("decision.stakes"), NumberStyles.None, CultureInfo.InvariantCulture, out int s) ? s : 1;
        List<(string, string, string)> approaches = FormRows.Read(values, EncounterKind.Approaches).Select((row, i) =>
        {
            string approachId = row["id"].Trim();
            string title = row["name"].Trim() is { Length: > 0 } named ? named : approachId.Length > 0 ? approachId : string.Create(CultureInfo.InvariantCulture, $"Approach {i + 1}");
            return (approachId, title, row["skill"]);
        }).ToList();
        return new Item(shown, "encounter", name, tags, values.Get("commsBlocked") == "true", approaches, decision, stakes);
    }

    private static List<Group> EncounterGroups(Item item, FormValues values)
    {
        var here = new CriterionDef("encounter", [item.Id]);
        var groups = new List<Group>
        {
            new("arrive", "Arrival", "What the log says when the team reaches this encounter.", true, [new Slot("text.arrive", "The team arrives", $"{item.Id}.arrive", "stage", [here])], ["stage"]),
        };
        for (int i = 0; i < item.Approaches.Count; i++)
        {
            (string approach, string title, _) = item.Approaches[i];
            int row = i;
            var on = new CriterionDef("approach", [approach]);
            string Field(string key) => EncounterKind.Approaches.Name(row, EncounterKind.TextField(key));
            List<Slot> slots = NarrativeFacts.ApproachTexts.Select(key => key == "attempt"
                ? new Slot(Field(key), "Before the check", $"{item.Id}.{approach}.attempt", "attempt", [here, on])
                : new Slot(Field(key), Capital(key), $"{item.Id}.{approach}.{key}", "check", [here, on, new CriterionDef("tier", [key])])).ToList();
            // Keyed by place, not id: an id is the author's to choose and could be any other group's key.
            groups.Add(new Group(ApproachKey(row), title, "What is said before the check, then what the log says by how it went. Write - alone for a result that passes in silence.", true, slots, []));
        }

        groups.Add(new Group("checks", "Checks, any approach", "Lines for a check here that the boxes above cannot say: only when someone helps, only for a cautious actor.", false, [], ["attempt", "check"]));
        groups.Add(new Group("effects", "Effects", "What the log says when a check's or a choice's effects land.", false, [], ["effects"]));
        groups.Add(new Group("injury", "Injury", "What the log says when someone is hurt here.", true, [], ["injury"]));
        groups.Add(new Group("lead", "Lead change", null, false, [], ["lead"]));
        if (item.DecisionId is { } decision)
        {
            var that = new CriterionDef("decision", [decision]);
            groups.Add(new Group(
                "decision",
                "Decision",
                "What the team says when the lead calls this in (shown on the call as well as in the log), what the log says when the lead decides alone, and when each choice takes effect.",
                true,
                [
                    new Slot("decision.text.call", "The lead calls it in", $"{item.Id}.decision.call", "call", [that]),
                    new Slot("decision.text.initiative", "The lead decides alone", $"{item.Id}.decision.initiative", "initiative", [that]),
                    new Slot("decision.text.bold", "The bold choice takes effect", $"{item.Id}.decision.bold", "effects", [that, new CriterionDef("chose", ["bold"])]),
                    new Slot("decision.text.safe", "The safe choice takes effect", $"{item.Id}.decision.safe", "effects", [that, new CriterionDef("chose", ["safe"])]),
                ],
                ["call", "initiative"],
                Labels: true));
            groups.Add(new Group("answer", "The captain answers", null, false, [], ["answer"]));
        }

        groups.Add(new Group("skip", "Skipped", "When the team passes this encounter by.", false, [], ["skip"]));
        groups.Add(new Group("chain", "Arrival by chain", "When another encounter leads on to this one.", false, [], ["chain"]));
        groups.Add(new Group("collapse", "Team collapse", null, false, [], ["collapse"]));
        return groups;
    }

    private static List<Group> ObjectiveGroups(Item item)
    {
        var here = new CriterionDef("objective", [item.Id]);
        List<Slot> results = NarrativeFacts.Outcomes.Select(o => new Slot($"text.{o}", Capital(o), $"{item.Id}.{o}", "result", [here, new CriterionDef("outcome", [o])])).ToList();
        var groups = new List<Group> { new("result", "Result", "What the log and the report say when a mission for this objective ends, by outcome.", true, results, ["result"]) };
        groups.Add(new Group("stage", "Arrival at an encounter", "Lines here hold at every encounter of a mission for this objective.", false, [], ["stage"]));
        groups.Add(new Group("checks", "Checks", null, false, [], ["attempt", "check"]));
        foreach (string beat in new[] { "effects", "injury", "lead", "call", "initiative", "answer", "skip", "chain", "collapse" })
        {
            groups.Add(new Group(beat, Title(beat), null, beat == "injury", [], [beat]));
        }

        return groups;
    }

    private static string Capital(string word)
    {
        // fullSuccess reads "Full success".
        string spaced = string.Concat(word.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    // What a text in this group can name: speakers first, then people and things, a pronoun and a verb for the first person, and the pack's symbols.
    private static List<string> Chips(Group group, IEnumerable<string> symbols)
    {
        List<BeatSpec> specs = group.Slots.Select(s => s.Beat).Concat(group.Beats).Distinct().Select(b => NarrativeFacts.Beats[b]).ToList();
        if (specs.Count == 0)
        {
            return [];
        }

        List<string> people = specs.SelectMany(s => s.People).Distinct().ToList();
        var chips = new List<string>();
        chips.AddRange(people.Select(p => $"{p}: "));
        chips.Add("captain: ");
        chips.Add("best SKILL_ID: ");
        chips.Add("> ");
        chips.Add("= ");
        chips.AddRange(people.Select(p => $"#{p}#"));
        chips.AddRange(specs.SelectMany(s => s.Things).Distinct().Select(t => $"#{t}#"));
        string person = people.Count > 1 ? people[1] : people[0];
        chips.Add($"#{person}.they#");
        chips.Add($"#{person}.their#");
        chips.Add($"#{person}:is|are#");
        chips.AddRange(symbols.Order(StringComparer.Ordinal).Select(s => $"#{s}#"));
        return chips;
    }

    // Why a line with these criteria can never be chosen at this item, or null. Advice from what the form holds, not a refusal.
    private static string? Impossible(Item item, string beat, IReadOnlyList<CriterionDef> when)
    {
        foreach (CriterionDef c in when)
        {
            switch (c.Fact)
            {
                // The save refuses such a line; until then it is not shown winning on a check that cannot come.
                case "approach" when item.Fact == "encounter" && c.Values.FirstOrDefault(v => item.Approaches.All(a => a.Id != v)) is { } missing:
                    return $"This encounter has no approach '{missing}'.";
                case "skill" when item.Fact == "encounter":
                    string? approach = when.FirstOrDefault(w => w.Fact == "approach")?.Values.FirstOrDefault();
                    List<string> checkedHere = item.Approaches.Where(a => approach is null || a.Id == approach).Select(a => a.Skill).Where(s => s.Length > 0).ToList();
                    if (checkedHere.Count > 0 && !c.Values.Any(checkedHere.Contains))
                    {
                        return $"This cannot happen here: the checks at this encounter are on {string.Join(" and ", checkedHere.Distinct())}.";
                    }

                    break;
                case "decision" when item.Fact == "encounter":
                    // The call to abandon a mission can come at any encounter.
                    bool abort = beat is "call" or "answer" or "effects" && c.Values.Contains(DecisionIds.Abort);
                    if (!abort && (item.DecisionId is null || !c.Values.Contains(item.DecisionId)))
                    {
                        return item.DecisionId is null ? "This cannot happen here: this encounter has no decision." : $"This cannot happen here: this encounter's decision is {item.DecisionId}.";
                    }

                    break;
                case "encounterTags" when item.Fact == "encounter" && !c.Values.Any(item.Tags.Contains):
                    return "This cannot happen here: this encounter has none of those tags.";
                case "objectiveTags" when item.Fact == "objective" && !c.Values.Any(item.Tags.Contains):
                    return "This cannot happen here: this objective has none of those tags.";
                case "commsBlocked" when item.Fact == "encounter" && !c.Values.Contains(item.CommsBlocked ? "true" : "false"):
                    return $"This cannot happen here: this encounter's comms are {(item.CommsBlocked ? "blocked" : "not blocked")}.";
            }
        }

        return null;
    }

    private static CrewDef Cast(ContentPack pack, int sample, int offset) => pack.Crew[(sample + offset) % pack.Crew.Count];

    // A beat of this kind at this item, made to satisfy the criteria: ordinary values for every fact the beat has, the item's
    // own where it has them, and the starting crew in the roles, turned by the sample number so each sample shows other people.
    private static Situation Sample(ContentPack pack, Names names, Item item, string beat, IReadOnlyList<CriterionDef> when, int sample)
    {
        BeatSpec spec = NarrativeFacts.Beats[beat];
        var facts = new Dictionary<string, FactValue>();
        var words = new Dictionary<string, string>();
        ObjectiveDef? objective = item.Fact == "objective" ? pack.Objectives.FirstOrDefault(o => o.Id == item.Id) : pack.Objectives.FirstOrDefault();
        EncounterDef? encounter = item.Fact == "encounter" ? null : pack.Encounters.Count > 0 ? pack.Encounters[sample % pack.Encounters.Count] : null;
        string objectiveId = item.Fact == "objective" ? item.Id : objective?.Id ?? "OBJECTIVE";
        string encounterId = item.Fact == "encounter" ? item.Id : encounter?.Id ?? "ENCOUNTER";
        string? Wanted(string fact) => when.FirstOrDefault(c => c.Fact == fact)?.Values.FirstOrDefault();
        string approach = Wanted("approach") ?? (item.Fact == "encounter" ? item.Approaches.FirstOrDefault().Id : encounter?.Approaches.FirstOrDefault()?.Id) ?? "";
        string skill = Wanted("skill")
            ?? (item.Fact == "encounter" ? item.Approaches.FirstOrDefault(a => a.Id == approach).Skill is { Length: > 0 } s ? s : null : encounter?.Approaches.FirstOrDefault(a => a.Id == approach)?.Skill)
            ?? pack.Skills.FirstOrDefault()?.Id ?? "SKILL";
        string tier = Wanted("tier") ?? "success";
        string decision = Wanted("decision") ?? item.DecisionId ?? encounter?.Decision?.Id ?? DecisionIds.Abort;

        facts["objective"] = FactValue.Of(objectiveId);
        facts["objectiveTags"] = FactValue.Of(item.Fact == "objective" ? item.Tags : objective?.Tags ?? []);
        facts["encounter"] = FactValue.Of(encounterId);
        facts["encounterTags"] = FactValue.Of(item.Fact == "encounter" ? item.Tags : encounter?.Tags ?? []);
        facts["stage"] = FactValue.Of(1);
        facts["momentum"] = FactValue.Of(0);
        facts["strain"] = FactValue.Of(0);
        facts["commsBlocked"] = FactValue.Of(item.Fact == "encounter" ? item.CommsBlocked : encounter?.CommsBlocked ?? false);
        facts["teamStanding"] = FactValue.Of(Math.Min(3, Math.Max(1, pack.Crew.Count)));
        words["objective"] = item.Fact == "objective" ? item.Name : names.Objective(objectiveId);
        words["encounter"] = item.Fact == "encounter" ? item.Name : names.Encounter(encounterId);
        switch (beat)
        {
            case "stage":
                facts["slot"] = FactValue.Of(objective?.Stages.FirstOrDefault()?.Slot ?? "main");
                break;
            case "attempt" or "check":
                facts["approach"] = FactValue.Of(approach);
                facts["skill"] = FactValue.Of(skill);
                facts["supervised"] = FactValue.Of(false);
                facts["helped"] = FactValue.Of(false);
                facts["mentoring"] = FactValue.Of(false);
                words["skill"] = names.Skill(skill);
                if (beat == "check")
                {
                    facts["tier"] = FactValue.Of(tier);
                    facts["margin"] = FactValue.Of(tier switch { "critical" => 6, "success" => 2, "partial" => -1, "failure" => -3, _ => -7 });
                }

                break;
            case "effects":
                facts["progress"] = FactValue.Of(1);
                facts["momentumChange"] = FactValue.Of(0);
                facts["strainChange"] = FactValue.Of(0);
                facts["withdraw"] = FactValue.Of(false);
                if (Wanted("decision") is not null || Wanted("chose") is not null)
                {
                    facts["decision"] = FactValue.Of(decision);
                    facts["chose"] = FactValue.Of(Wanted("chose") ?? "bold");
                    words["decision"] = item.DecisionId == decision ? DecisionName(item, names, decision) : names.Decision(decision);
                }

                break;
            case "injury":
                facts["severity"] = FactValue.Of("light");
                facts["outcome"] = FactValue.Of("lightInjury");
                break;
            case "call":
                facts["decision"] = FactValue.Of(decision);
                facts["stakes"] = FactValue.Of(item.Stakes);
                facts["prefers"] = FactValue.Of("safe");
                words["decision"] = DecisionName(item, names, decision);
                break;
            case "initiative":
                facts["decision"] = FactValue.Of(decision);
                facts["stakes"] = FactValue.Of(item.Stakes);
                facts["chose"] = FactValue.Of("safe");
                facts["blocked"] = FactValue.Of(false);
                words["decision"] = DecisionName(item, names, decision);
                break;
            case "answer":
                facts["decision"] = FactValue.Of(decision);
                facts["answer"] = FactValue.Of("safe");
                facts["standing"] = FactValue.Of("backed");
                words["decision"] = DecisionName(item, names, decision);
                break;
            case "result":
                facts["outcome"] = FactValue.Of("fullSuccess");
                break;
        }

        // The criteria decide what they test, whatever the ordinary value was. A person's stat is set on the fact; the person stays.
        var forced = new HashSet<string>(StringComparer.Ordinal);
        foreach (CriterionDef c in when)
        {
            if (NarrativeFacts.Fact(beat, c.Fact) is not { } fact)
            {
                continue;
            }

            forced.Add(c.Fact);
            string first = c.Values.FirstOrDefault() ?? "";
            facts[c.Fact] = fact.Type switch
            {
                FactType.Number => FactValue.Of(c.IsRange ? c.Min ?? c.Max ?? 0 : int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : 0),
                FactType.Flag => FactValue.Of(first == "true"),
                FactType.Set => FactValue.Of(c.Values),
                _ => FactValue.Of(first),
            };
        }

        var people = new Dictionary<string, Person>();
        if (pack.Crew.Count > 0)
        {
            Dictionary<string, CrewDef> cast = new() { ["lead"] = Cast(pack, sample, 0) };
            if (spec.People.Contains("actor"))
            {
                cast["actor"] = Cast(pack, sample, 1);
            }

            if (spec.People.Contains("crew"))
            {
                cast["crew"] = Cast(pack, sample, 1);
            }

            if (spec.People.Contains("helper") && facts.TryGetValue("helped", out FactValue? helped) && helped.Flag)
            {
                cast["helper"] = Cast(pack, sample, 2);
            }

            foreach ((string role, CrewDef crew) in cast)
            {
                people[role] = new Person(crew.Id, names.Crew(crew.Id), pack.PronounsOf(crew));
                void Stat(string stat, FactValue value)
                {
                    if (!forced.Contains($"{role}.{stat}"))
                    {
                        facts[$"{role}.{stat}"] = value;
                    }
                }

                Stat("id", FactValue.Of(crew.Id));
                Stat("caution", FactValue.Of(crew.Caution));
                Stat("discipline", FactValue.Of(crew.Discipline));
                Stat("seniority", FactValue.Of(crew.Seniority));
                Stat("isLead", FactValue.Of(role == "lead" || crew.Id == cast["lead"].Id));
            }

            // The team is the three the sample turns to; the best at a skill is one of them who is not the lead.
            List<CrewDef> team = Enumerable.Range(0, Math.Min(3, pack.Crew.Count)).Select(i => Cast(pack, sample, i)).ToList();
            foreach (SkillDef each in pack.Skills)
            {
                if (team.Skip(1).Where(c => c.Skills.GetValueOrDefault(each.Id) > 0).OrderByDescending(c => c.Skills[each.Id]).FirstOrDefault() is { } best)
                {
                    people[Scripts.BestPrefix + each.Id] = new Person(best.Id, names.Crew(best.Id), pack.PronounsOf(best));
                }
            }
        }

        return new Situation(beat, facts, people, words);
    }

    private static string DecisionName(Item item, Names names, string decision) => names.Decision(decision);

    // What is wrong with the author's own text against this sample, or null: it cannot be read, a speaker the beat does not
    // have, or a name that expands to nothing. Entries whose speaker is merely absent in this sample are not a problem.
    private static string? Problem(string text, Situation situation, IReadOnlyDictionary<string, SymbolDef> symbols, string key)
    {
        Script script;
        try
        {
            script = Scripts.Parse(text);
        }
        catch (NarrativeFormatException e)
        {
            return e.Message;
        }

        BeatSpec spec = NarrativeFacts.Beats[situation.Beat];
        foreach (ScriptEntry entry in script.Entries)
        {
            if (entry.Speaker is { } speaker && speaker != Scripts.Captain && Scripts.BestSkill(speaker) is null && !spec.People.Contains(speaker))
            {
                return $"'{speaker}' cannot speak here: a {situation.Beat} beat has {string.Join(", ", spec.People)}.";
            }
        }

        foreach (string part in script.Entries.Where(e => e.Speaker is null || e.Speaker == Scripts.Captain || situation.People.ContainsKey(e.Speaker)).Select(e => e.Text).Concat(script.Summary is null ? [] : [script.Summary]))
        {
            try
            {
                Expander.Expand(part, situation, symbols, key);
            }
            catch (ExpansionException e)
            {
                return e.Message;
            }
        }

        return null;
    }
}
