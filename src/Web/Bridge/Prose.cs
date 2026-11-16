using System.Globalization;
using Sim.Core;
using Sim.Core.Content;
using Sim.Core.Narrative;
using Sim.Crew;
using Sim.Missions;
using Sim.Narrative;

namespace Web.Bridge;

// One entry of a beat's prose as it is shown: narration, a note, or speech with the speaker's name.
public sealed record ProseEntry(EntryKind Kind, string? Speaker, IReadOnlyList<LinePart> Parts);

// One beat's prose: its entries, whether an authored line gave them (otherwise they are the systems line), the line's id, and
// the sentence the report uses for the beat (null when it has none).
public sealed record BeatProse(IReadOnlyList<ProseEntry> Entries, bool Authored, string? LineId, IReadOnlyList<LinePart>? Report = null)
{
    // The entries as one run of text: a one-sentence line is exactly its sentence; a script's speech reads "Name: words".
    public IReadOnlyList<LinePart> Parts
    {
        get
        {
            var parts = new List<LinePart>();
            foreach (ProseEntry entry in Entries)
            {
                if (parts.Count > 0)
                {
                    parts.Add(new LinePart(false, " "));
                }

                if (entry.Speaker is { } speaker)
                {
                    parts.Add(new LinePart(true, speaker));
                    parts.Add(new LinePart(false, ": "));
                }

                parts.AddRange(entry.Parts);
            }

            return parts;
        }
    }
}

// A mission's text, regenerated on every view from its beats and the current pack (slice 1d design, decision 6). Nothing here is stored.
public sealed class Prose
{
    public const string CaptainName = "Captain";

    private readonly ContentPack pack;
    private readonly Dictionary<string, SymbolDef> symbols;
    private readonly IReadOnlyList<LineDef> reportable;
    private readonly string seed;
    private readonly string missionId;
    private readonly IReadOnlyList<MissionBeat> beats;
    private readonly IReadOnlyList<Situation> situations;

    public Prose(ContentPack pack, Names names, ulong legacySeed, MissionInFlight mission, IReadOnlyList<MissionBeat> beats, string startLeadId)
    {
        this.pack = pack;
        symbols = pack.Symbols.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        reportable = Reportable(pack.Lines);
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
            prose.Add(Render(i, used) ?? new BeatProse([new ProseEntry(EntryKind.Narration, null, systems(i))], false, null));
        }

        return prose;
    }

    // The prose for one beat, or null when no authored line holds or its text cannot be expanded.
    public BeatProse? Render(int position, HashSet<string> used)
    {
        string key = string.Create(CultureInfo.InvariantCulture, $"{seed}|{missionId}|{beats[position].Index}");
        return Render(pack.Lines, reportable, symbols, situations[position], key, used);
    }

    public IReadOnlyList<Situation> Situations => situations;

    // What the answer buttons of a call about this decision say, by answer word; null when the decision has no labels.
    public IReadOnlyDictionary<string, string>? ChoiceLabels(string decisionId) => pack.ChoiceLabels.GetValueOrDefault(decisionId);

    // The shared lines the report can fall back on when a beat's own line is a script with no sentence for the report.
    public static IReadOnlyList<LineDef> Reportable(IReadOnlyList<LineDef> lines)
    {
        return lines.Where(l => l.Owner is null && Read(l.Text)?.ReportText is not null).ToList();
    }

    // One beat's prose from these lines. A check is said in two parts: what an "attempt" line says before the result, then the
    // check's own line. Either alone is enough for the beat to count as authored.
    public static BeatProse? Render(IReadOnlyList<LineDef> lines, IReadOnlyList<LineDef> reportable, IReadOnlyDictionary<string, SymbolDef> symbols, Situation situation, string key, HashSet<string> used)
    {
        var entries = new List<ProseEntry>();
        bool any = false;
        if (situation.Beat == "check" && Say(lines, symbols, BeatSituations.Attempt(situation), key + "|attempt", used) is { } before)
        {
            entries.AddRange(before.Entries);
            any = true;
        }

        (LineDef Line, List<ProseEntry> Entries, IReadOnlyList<LinePart>? Report)? said = Say(lines, symbols, situation, key, used);
        if (said is null && !any)
        {
            return null;
        }

        entries.AddRange(said?.Entries ?? []);
        IReadOnlyList<LinePart>? report = said?.Report;
        if (report is null && LineSelector.Select(reportable, situation, used, key) is { } shared && Read(shared.Text)?.ReportText is { } sentence)
        {
            report = Expand(sentence, situation, symbols, key);
        }

        return new BeatProse(entries, true, said?.Line.Id, report);
    }

    // The winning line's script expanded, or null when no line holds or its text cannot be expanded. An entry whose speaker is
    // not there (no helper, nobody left who has the skill) is left out; the rest of the script still plays.
    private static (LineDef Line, List<ProseEntry> Entries, IReadOnlyList<LinePart>? Report)? Say(IReadOnlyList<LineDef> lines, IReadOnlyDictionary<string, SymbolDef> symbols, Situation situation, string key, HashSet<string> used)
    {
        if (LineSelector.Select(lines, situation, used, key) is not { } line || Read(line.Text) is not { } script)
        {
            return null;
        }

        var entries = new List<ProseEntry>();
        for (int i = 0; i < script.Entries.Count; i++)
        {
            ScriptEntry entry = script.Entries[i];
            string? speaker = null;
            if (entry.Speaker is { } role)
            {
                speaker = role == Scripts.Captain ? CaptainName : situation.People.GetValueOrDefault(role)?.Name;
                if (speaker is null)
                {
                    continue;
                }
            }

            // The first entry is picked by the beat's own key, so a one-sentence line reads as it always has.
            if (Expand(entry.Text, situation, symbols, i == 0 ? key : string.Create(CultureInfo.InvariantCulture, $"{key}|{i}")) is not { } parts)
            {
                return null;
            }

            entries.Add(new ProseEntry(entry.Kind, speaker, parts));
        }

        IReadOnlyList<LinePart>? report = script.ReportText is { } sentence ? Expand(sentence, situation, symbols, key) : null;
        used.Add(line.Id);
        return (line, entries, report);
    }

    // The situation a storylet's lines are read against, apart from any mission: the storylet (and the option, for an answer's
    // line), its subject when it has one, and for each skill a "best" speaker, the fit crew member with the highest level in it.
    public static Situation StoryletSituation(ContentPack pack, Names names, IReadOnlyList<CrewMember> crew, string storyletId, string? subjectId, string? optionId = null)
    {
        Dictionary<string, CrewDef> defs = pack.Crew.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        Person PersonOf(string id) => new(id, names.Crew(id), pack.PronounsOf(defs.GetValueOrDefault(id)));
        var facts = new Dictionary<string, FactValue> { ["storylet"] = FactValue.Of(storyletId) };
        if (optionId is not null)
        {
            facts["option"] = FactValue.Of(optionId);
        }

        var people = new Dictionary<string, Person>();
        if (subjectId is not null && crew.FirstOrDefault(m => m.Id == subjectId) is { } subject)
        {
            people["subject"] = PersonOf(subject.Id);
            facts["subject.id"] = FactValue.Of(subject.Id);
            facts["subject.caution"] = FactValue.Of(subject.Caution);
            facts["subject.discipline"] = FactValue.Of(subject.Discipline);
            facts["subject.seniority"] = FactValue.Of(subject.Seniority);
        }

        // The sort is stable, so a tie goes to the crew member earlier on the roster.
        List<CrewMember> fit = crew.Where(m => m.Status == CrewStatus.Active).ToList();
        foreach (string skill in fit.SelectMany(m => m.Skills.Keys).Distinct().Order(StringComparer.Ordinal))
        {
            if (fit.Where(m => m.Skills.GetValueOrDefault(skill)?.Level > 0).OrderByDescending(m => m.Skills[skill].Level).FirstOrDefault() is { } best)
            {
                people[Scripts.BestPrefix + skill] = PersonOf(best.Id);
            }
        }

        string beat = optionId is null ? "storylet" : "storyletAnswer";
        return new Situation(beat, facts, people, new Dictionary<string, string> { ["storylet"] = names.Storylet(storyletId) });
    }

    // A storylet's prose: the winning line, or, when its text cannot be expanded here (a shared line naming #subject# that met a storylet
    // for the ship), the next line that holds. Null when none does.
    public static BeatProse? Storylet(ContentPack pack, Situation situation, string key)
    {
        Dictionary<string, SymbolDef> symbols = pack.Symbols.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        var used = new HashSet<string>(StringComparer.Ordinal);
        List<LineDef> pool = pack.Lines.Where(l => l.Beat == situation.Beat).ToList();
        while (LineSelector.Select(pool, situation, used, key) is { } line)
        {
            if (Render([line], [], symbols, situation, key, used) is { } prose)
            {
                return prose;
            }

            pool.Remove(line);
        }

        return null;
    }

    private static Script? Read(string text)
    {
        try
        {
            return Scripts.Parse(text);
        }
        catch (NarrativeFormatException)
        {
            return null;
        }
    }

    private static IReadOnlyList<LinePart>? Expand(string text, Situation situation, IReadOnlyDictionary<string, SymbolDef> symbols, string key)
    {
        try
        {
            return Expander.Expand(text, situation, symbols, key).Select(p => new LinePart(p.IsName, p.Text)).ToList();
        }
        catch (ExpansionException)
        {
            return null;
        }
    }
}
