using Sim.Core;
using Sim.Core.Content;
using Sim.Core.Narrative;

namespace Sim.Missions;

// What a mission's beats are read against: the mission (its drawn encounters and team), the lead it started with, the objective
// template's tags, and how to name people (with pronouns) and things ("encounter", "skill", "objective", "decision") by id.
public sealed record BeatFrame(
    MissionState Mission,
    string StartLeadId,
    IReadOnlyList<string> ObjectiveTags,
    Func<string, Person> Person,
    Func<string, string, string> Name);

// Maps each beat of a mission to the situation a line is chosen for, holding exactly the facts NarrativeFacts lists for its kind.
// Facts that depend on what came before (the encounter in play, the lead, momentum, and strain) are carried through the beats.
public static class BeatSituations
{
    public static string Kind(Beat beat) => beat switch
    {
        StageBeat => "stage",
        SkipBeat => "skip",
        ChainBeat => "chain",
        CheckBeat => "check",
        EffectsBeat => "effects",
        InjuryBeat => "injury",
        LeadChangeBeat => "lead",
        CallBeat => "call",
        NoticeBeat => "notice",
        AnswerBeat => "answer",
        CollapseBeat => "collapse",
        ResultBeat => "result",
        _ => throw new ArgumentOutOfRangeException(nameof(beat), beat.GetType().Name, "No beat kind for this beat."),
    };

    public static IReadOnlyList<Situation> Of(IReadOnlyList<Beat> beats, BeatFrame frame)
    {
        MissionState mission = frame.Mission;
        Dictionary<string, EncounterDef> encounters = mission.Encounters.Select(d => d.Encounter).Concat(mission.Chains.Values)
            .GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        Dictionary<string, TeamMember> team = mission.Team.Select(m => m.Member).ToDictionary(m => m.Id);
        int cap = mission.Tuning.Cap;

        string? encounter = null;
        int stage = 0;
        int momentum = 0;
        int strain = 0;
        bool blocked = false;
        string lead = frame.StartLeadId;
        string? decision = null;
        string? chose = null;
        var situations = new List<Situation>();
        foreach (Beat beat in beats)
        {
            var facts = new Dictionary<string, FactValue>();
            var people = new Dictionary<string, Person>();
            var words = new Dictionary<string, string>();
            void Add(string role, string id)
            {
                people[role] = frame.Person(id);
                if (team.TryGetValue(id, out TeamMember? member))
                {
                    facts[$"{role}.caution"] = FactValue.Of(member.Caution);
                    facts[$"{role}.discipline"] = FactValue.Of(member.Discipline);
                    facts[$"{role}.seniority"] = FactValue.Of(member.Seniority);
                }

                facts[$"{role}.id"] = FactValue.Of(id);
                facts[$"{role}.isLead"] = FactValue.Of(id == lead);
            }

            switch (beat)
            {
                case StageBeat b:
                    stage++;
                    encounter = b.EncounterId;
                    (momentum, strain, blocked) = (b.Momentum, b.Strain, b.CommsBlocked);
                    facts["slot"] = FactValue.Of(b.Slot);
                    break;
                case SkipBeat b:
                    encounter = b.EncounterId;
                    break;
                case ChainBeat b:
                    encounter = b.EncounterId;
                    break;
                case InjuryBeat b:
                    encounter = b.EncounterId;
                    break;
                case LeadChangeBeat b:
                    lead = b.CrewId;
                    break;
                case AnswerBeat b:
                    lead = b.LeadId;
                    chose = Word(b.Answer);
                    break;
                case CallBeat b:
                    decision = b.Call.DecisionId;
                    break;
                case NoticeBeat b:
                    decision = b.DecisionId;
                    chose = Word(b.Chose);
                    break;
            }

            facts["objective"] = FactValue.Of(mission.ObjectiveId);
            facts["objectiveTags"] = FactValue.Of(frame.ObjectiveTags);
            words["objective"] = frame.Name("objective", mission.ObjectiveId);
            if (encounter is not null)
            {
                facts["encounter"] = FactValue.Of(encounter);
                facts["encounterTags"] = FactValue.Of(encounters.TryGetValue(encounter, out EncounterDef? def) ? def.Tags : []);
                words["encounter"] = frame.Name("encounter", encounter);
            }

            facts["stage"] = FactValue.Of(stage);
            facts["momentum"] = FactValue.Of(momentum);
            facts["strain"] = FactValue.Of(strain);
            facts["commsBlocked"] = FactValue.Of(blocked);
            Add("lead", lead);

            switch (beat)
            {
                case CheckBeat b:
                    facts["approach"] = FactValue.Of(Word(b.Approach));
                    facts["skill"] = FactValue.Of(b.Skill);
                    facts["tier"] = FactValue.Of(Word(b.Tier));
                    facts["margin"] = FactValue.Of(b.Margin);
                    facts["supervised"] = FactValue.Of(b.Supervised);
                    facts["helped"] = FactValue.Of(b.HelperId is not null);
                    facts["mentoring"] = FactValue.Of(b.Mentoring);
                    words["skill"] = frame.Name("skill", b.Skill);
                    Add("actor", b.ActorId);
                    if (b.HelperId is { } helper)
                    {
                        Add("helper", helper);
                    }

                    momentum = b.MomentumAfter ?? momentum;
                    strain = b.StrainAfter ?? strain;
                    break;
                case EffectsBeat b:
                    facts["progress"] = FactValue.Of(b.Effects.Progress);
                    facts["momentumChange"] = FactValue.Of(b.Effects.Momentum);
                    facts["strainChange"] = FactValue.Of(b.Effects.Strain);
                    if (b.Effects.InjuryRoll is { } roll)
                    {
                        facts["injuryRoll"] = FactValue.Of(Word(roll));
                    }

                    facts["withdraw"] = FactValue.Of(b.Effects.Withdraw);
                    // Effects follow a notice or an answer, so the decision and what was chosen are the ones just seen.
                    if (decision is not null && chose is not null)
                    {
                        Decision(decision);
                        facts["chose"] = FactValue.Of(chose);
                    }

                    momentum = Math.Clamp(momentum + b.Effects.Momentum, 0, cap);
                    strain = Math.Clamp(strain + b.Effects.Strain, 0, cap);
                    break;
                case InjuryBeat b:
                    facts["severity"] = FactValue.Of(Word(b.Severity));
                    facts["outcome"] = FactValue.Of(Word(b.Outcome));
                    Add("crew", b.CrewId);
                    break;
                case LeadChangeBeat b:
                    Add("crew", b.CrewId);
                    break;
                case CallBeat b:
                    Decision(b.Call.DecisionId);
                    facts["stakes"] = FactValue.Of(b.Call.Stakes);
                    facts["prefers"] = FactValue.Of(Word(b.Call.LeadPrefers));
                    break;
                case NoticeBeat b:
                    Decision(b.DecisionId);
                    facts["stakes"] = FactValue.Of(b.Stakes);
                    facts["chose"] = FactValue.Of(Word(b.Chose));
                    facts["blocked"] = FactValue.Of(b.Blocked);
                    break;
                case AnswerBeat b:
                    if (decision is not null)
                    {
                        Decision(decision);
                    }

                    facts["answer"] = FactValue.Of(Word(b.Answer));
                    facts["standing"] = FactValue.Of(Word(b.Standing));
                    break;
                case ResultBeat b:
                    facts["outcome"] = FactValue.Of(Word(b.Outcome));
                    break;
            }

            situations.Add(new Situation(Kind(beat), facts, people, words));

            void Decision(string id)
            {
                facts["decision"] = FactValue.Of(id);
                words["decision"] = frame.Name("decision", id);
            }
        }

        return situations;
    }

    // Enum names as the catalogue writes them: FullSuccess is fullSuccess.
    public static string Word<T>(T value)
        where T : struct, Enum
    {
        string name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
