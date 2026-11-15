using System.Globalization;
using Sim.Core;
using Sim.Core.Content;
using Sim.Core.Narrative;
using Sim.Missions;
using Sim.Narrative;

namespace Web.Bridge;

// One beat's prose: the parts, whether an authored line gave them (otherwise they are the systems line), and the line's id.
public sealed record BeatProse(IReadOnlyList<LinePart> Parts, bool Authored, string? LineId);

// A mission's text, regenerated on every view from its beats and the current pack (slice 1d design, decision 6). Nothing here is stored.
public sealed class Prose
{
    private readonly ContentPack pack;
    private readonly Dictionary<string, SymbolDef> symbols;
    private readonly string seed;
    private readonly string missionId;
    private readonly IReadOnlyList<MissionBeat> beats;
    private readonly IReadOnlyList<Situation> situations;

    public Prose(ContentPack pack, Names names, ulong legacySeed, MissionInFlight mission, IReadOnlyList<MissionBeat> beats, string startLeadId)
    {
        this.pack = pack;
        symbols = pack.Symbols.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        seed = legacySeed.ToString(CultureInfo.InvariantCulture);
        missionId = mission.Id;
        this.beats = beats;
        Dictionary<string, CrewDef> crew = pack.Crew.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        IReadOnlyList<string> tags = pack.Objectives.FirstOrDefault(o => o.Id == mission.State.ObjectiveId)?.Tags ?? [];
        var frame = new BeatFrame(
            mission.State,
            startLeadId,
            tags,
            id => new Person(id, names.Crew(id), pack.PronounsOf(crew.GetValueOrDefault(id))),
            (kind, id) => kind switch
            {
                "encounter" => names.Encounter(id),
                "skill" => names.Skill(id),
                "objective" => names.Objective(id),
                "decision" => names.Decision(id),
                _ => id,
            });
        situations = BeatSituations.Of(beats.Select(b => b.Beat).ToList(), frame);
    }

    // The mission's prose, one per beat in order; the live log shows all of it and the report a chosen part. A line used earlier in the mission loses to an equally specific fresh one.
    public IReadOnlyList<BeatProse> Log(Func<int, IReadOnlyList<LinePart>> systems)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var prose = new List<BeatProse>();
        for (int i = 0; i < beats.Count; i++)
        {
            prose.Add(Render(i, used) ?? new BeatProse(systems(i), false, null));
        }

        return prose;
    }

    // The prose for one beat, or null when no authored line holds or its text cannot be expanded.
    public BeatProse? Render(int position, HashSet<string> used)
    {
        Situation situation = situations[position];
        string key = string.Create(CultureInfo.InvariantCulture, $"{seed}|{missionId}|{beats[position].Index}");
        LineDef? line = LineSelector.Select(pack.Lines, situation, used, key);
        if (line is null)
        {
            return null;
        }

        try
        {
            IReadOnlyList<TextPart> parts = Expander.Expand(line.Text, situation, symbols, key);
            used.Add(line.Id);
            return new BeatProse(parts.Select(p => new LinePart(p.IsName, p.Text)).ToList(), true, line.Id);
        }
        catch (ExpansionException)
        {
            return null;
        }
    }

    public IReadOnlyList<Situation> Situations => situations;
}
