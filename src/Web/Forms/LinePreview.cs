using System.Globalization;
using System.Runtime.CompilerServices;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Core.Narrative;
using Sim.Missions;
using Sim.Narrative;
using Web.Bridge;

namespace Web.Forms;

// A beat from a sample mission, with the key its text is picked by.
public sealed record SampleBeat(Situation Situation, string Key, string Where);

// The same fixed missions every time, so the preview and the lint see the same beats: each objective template on four seeds,
// sent with the starting crew from the senior end and from the junior end, every call answered as the lead prefers.
public static class NarrativeSamples
{
    private static readonly ConditionalWeakTable<ContentPack, IReadOnlyList<SampleBeat>> Cache = new();

    public static IReadOnlyList<SampleBeat> Of(ContentPack pack) => Cache.GetValue(pack, Build);

    private static IReadOnlyList<SampleBeat> Build(ContentPack pack)
    {
        var names = new Names(pack);
        Dictionary<string, CrewDef> crew = pack.Crew.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        List<TeamMember> members = pack.Crew.Select(c => new TeamMember(c.Id, c.Seniority, c.Skills, c.Caution, c.Discipline)).ToList();
        var samples = new List<SampleBeat>();
        foreach (ObjectiveDef objective in pack.Objectives)
        {
            int size = Math.Clamp(objective.TeamSizeMin, 1, Math.Max(1, members.Count));
            var teams = new (string Label, List<TeamMember> Members)[]
            {
                ("senior", members.OrderByDescending(m => m.Seniority).ThenBy(m => m.Id, StringComparer.Ordinal).Take(size).ToList()),
                ("junior", members.OrderBy(m => m.Seniority).ThenBy(m => m.Id, StringComparer.Ordinal).Take(size).ToList()),
            };
            foreach ((string label, List<TeamMember> team) in teams.Where(t => t.Members.Count > 0))
            {
                for (ulong seed = 1; seed <= 4; seed++)
                {
                    MissionState start = MissionResolver.Start(objective, pack.Encounters, new Team(team, team[0].Id), [], new Parameters(), pack.Tuning, seed);
                    MissionStep step = MissionResolver.Advance(start);
                    var beats = new List<Beat>(step.Beats);
                    while (step.Pending is { } call)
                    {
                        step = MissionResolver.Advance(step.State, new CallReply(call.CallId, call.LeadPrefers));
                        beats.AddRange(step.Beats);
                    }

                    var frame = new BeatFrame(
                        start,
                        team[0].Id,
                        objective.Tags,
                        id => new Person(id, names.Crew(id), pack.PronounsOf(crew.GetValueOrDefault(id))),
                        (kind, id) => kind switch
                        {
                            "encounter" => names.Encounter(id),
                            "skill" => names.Skill(id),
                            "objective" => names.Objective(id),
                            "decision" => names.Decision(id),
                            _ => id,
                        });
                    IReadOnlyList<Situation> situations = BeatSituations.Of(beats, frame);
                    for (int i = 0; i < situations.Count; i++)
                    {
                        string where = string.Create(CultureInfo.InvariantCulture, $"{names.Objective(objective.Id)}, {label} team, seed {seed}, beat {i + 1}");
                        samples.Add(new SampleBeat(situations[i], string.Create(CultureInfo.InvariantCulture, $"sample|{objective.Id}|{label}|{seed}|{i}"), where));
                    }
                }
            }
        }

        return samples;
    }
}

public sealed record PreviewItem(string Where, string Text, bool Wins, string? BeatenBy);

// The line form's preview: the line as posted, expanded against up to five sample beats it holds for, and whether it wins each.
public sealed record LinePreview(string? Message, IReadOnlyList<PreviewItem> Items, int Matches)
{
    public const int Shown = 5;

    public static LinePreview Of(ContentFileSet files, string id, FormValues values)
    {
        if (files.Validate().Pack is not { } pack)
        {
            return new LinePreview("The pack on disk does not load, so there is nothing to preview against.", [], 0);
        }

        string beat = values.Get("beat");
        string use = values.Get("use") is { Length: > 0 } u ? u : "log";
        string text = values.Get("text").Trim();
        if (!NarrativeFacts.Beats.ContainsKey(beat) || text.Length == 0)
        {
            return new LinePreview("Choose a beat and write the text to see a preview.", [], 0);
        }

        var criteria = new List<CriterionDef>();
        foreach (Dictionary<string, string> row in FormRows.Read(values, LineKind.When))
        {
            if (LineKind.ParseValue(row["fact"].Trim(), row["value"], out _) is not { } model)
            {
                return new LinePreview("Fix the criteria to see a preview.", [], 0);
            }

            criteria.Add(new CriterionDef(model.Fact, model.Values, model.Min, model.Max));
        }

        string lineId = id.Length > 0 ? id : "NEW_LINE";
        var line = new LineDef(lineId, use, beat, criteria, text);
        List<LineDef> lines = [.. pack.Lines.Where(l => l.Id != lineId), line];
        Dictionary<string, SymbolDef> symbols = pack.Symbols.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        var items = new List<PreviewItem>();
        int matches = 0;
        foreach (SampleBeat sample in NarrativeSamples.Of(pack).Where(s => s.Situation.Beat == beat && line.When.All(c => LineSelector.Holds(c, s.Situation))))
        {
            matches++;
            if (items.Count >= Shown)
            {
                continue;
            }

            string expanded;
            try
            {
                expanded = string.Concat(Expander.Expand(text, sample.Situation, symbols, sample.Key).Select(p => p.Text));
            }
            catch (ExpansionException e)
            {
                expanded = e.Message;
            }

            LineDef? winner = LineSelector.Select(lines, sample.Situation, use, new HashSet<string>(), sample.Key);
            items.Add(new PreviewItem(sample.Where, expanded, winner?.Id == lineId, winner?.Id == lineId ? null : winner?.Id));
        }

        return matches == 0
            ? new LinePreview("No sample beat matches this line's beat and criteria.", [], 0)
            : new LinePreview(null, items, matches);
    }
}
