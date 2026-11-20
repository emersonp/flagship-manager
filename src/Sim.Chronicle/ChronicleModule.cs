using System.Globalization;
using Sim.Core;
using Sim.Core.Content;
using Sim.Core.Narrative;

namespace Sim.Chronicle;

// Owns the chronicle (slice 1e part five): the legacy's permanent history, one entry for each thing worth remembering, written
// once when it happens. Each entry's text is the narrator's line for its situation, rendered with the pack live at that moment and
// stored; nothing renders it again. It draws no randomness and changes no other module's state. Other modules and the page read it
// through IChronicleView.
public static class ChronicleModule
{
    public const string Name = "chronicle";

    // After every other module (crew 100, missions 200, traits 250, storylets 300), so it hears each event after the module that
    // owns it has acted; before the cycle's next day (1000).
    private const int Order = 400;

    // The words of an entry when no line holds, so an entry is never lost. The game's pack never needs them.
    public const string FoundingFallback = "The ship took on her first crew.";
    public const string DeathFallback = "A crew member was lost.";
    public const string MissionFallback = "A mission ended.";
    public const string CallFallback = "The captain answered a call.";

    public static void Register(EventBus bus, ContentPack pack, INarrator narrator)
    {
        var situations = new Situations(pack);
        bus.Module<ChronicleState>(Name);
        // The factory reads the state on every call: it may not exist until the first entry is written.
        bus.Provide<IChronicleView>(state => new ChronicleView(state.Module<ChronicleState>(Name), state.Day));
        bus.Subscribe<LegacyFounded>(Order, Name, (_, context) => OnFounded(pack, narrator, context));
        bus.Subscribe<SiteArrived>(Order, Name, (e, context) => context.Module<ChronicleState>().Site = e.AssignmentId);
        bus.Subscribe<MissionStarted>(Order, Name, OnStarted);
        bus.Subscribe<CallAnswered>(Order, Name, (e, context) => OnCall(pack, situations, narrator, e, context));
        bus.Subscribe<CrewDied>(Order, Name, (e, context) => OnDied(pack, situations, narrator, e, context));
        bus.Subscribe<MissionEnded>(Order, Name, (e, context) => OnEnded(pack, situations, narrator, e, context));
    }

    private static void OnFounded(ContentPack pack, INarrator narrator, IBusContext context)
    {
        Situation situation = Situations.Founding(context.View<IRosterView>().Roster.Count);
        Write(narrator, context, ChronicleKinds.Founding, pack.Chronicle.Weights[ChronicleTuning.Founding], [], situation, FoundingFallback, [context.Sequence]);
    }

    private static void OnStarted(MissionStarted started, IBusContext context)
    {
        ChronicleState state = context.Module<ChronicleState>();
        state.Missions.Add(new OpenMission
        {
            Id = started.MissionId,
            ObjectiveId = started.TemplateId,
            SiteId = state.Site,
            Team = started.CrewIds.ToList(),
            LeadId = started.LeadId,
        });
    }

    // The lead who made the call is the subject. A decision's own weight wins over the tuning file's.
    private static void OnCall(ContentPack pack, Situations situations, INarrator narrator, CallAnswered call, IBusContext context)
    {
        int weight = situations.CallWeight(call.DecisionId)
            ?? pack.Chronicle.Weights[call.Standing == "overruled" ? ChronicleTuning.CallOverruled : ChronicleTuning.CallBacked];
        Situation situation = situations.Call(call, Find(context, call.MissionId));
        Write(narrator, context, ChronicleKinds.Call, weight, [situations.Subject(call.LeadId)], situation, CallFallback, [context.Sequence]);
    }

    // Cycles served counts from the founding: every crew member has been aboard since cycle 1, until crew have a join date.
    private static void OnDied(ContentPack pack, Situations situations, INarrator narrator, CrewDied died, IBusContext context)
    {
        OpenMission? mission = Find(context, died.MissionId);
        Situation situation = situations.Death(died, mission, context.State.Cycle);
        Write(narrator, context, ChronicleKinds.Death, pack.Chronicle.Weights[ChronicleTuning.Death], [situations.Subject(died.CrewId)], situation, DeathFallback, [context.Sequence]);
        if (mission is not null)
        {
            mission.DeathSeqs.Add(context.Sequence);
            Close(pack, situations, narrator, mission, context);
        }
    }

    private static void OnEnded(ContentPack pack, Situations situations, INarrator narrator, MissionEnded ended, IBusContext context)
    {
        if (Find(context, ended.MissionId) is not { } mission)
        {
            return;
        }

        mission.Ended = Word(ended.Result.Outcome);
        mission.EndedSeq = context.Sequence;
        mission.Deaths = ended.Result.Crew.Count(c => c.Status == MemberStatus.Dead);
        Close(pack, situations, narrator, mission, context);
    }

    // Once a mission has ended and each of its deaths has been written, its own entry follows them (design 4.3), and the chronicle
    // lets the mission go. The entry records the end and the deaths; its line's ties break by the end's sequence number.
    private static void Close(ContentPack pack, Situations situations, INarrator narrator, OpenMission mission, IBusContext context)
    {
        if (mission.Ended is not { } outcome || mission.DeathSeqs.Count < mission.Deaths)
        {
            return;
        }

        int weight = pack.Chronicle.Weights[outcome is "fullSuccess" or "partialSuccess" ? ChronicleTuning.MissionWon : ChronicleTuning.MissionLost];
        Situation situation = situations.Mission(mission, outcome, mission.Deaths);
        Write(narrator, context, ChronicleKinds.Mission, weight, [situations.Subject(mission.LeadId)], situation, MissionFallback, [mission.EndedSeq, .. mission.DeathSeqs]);
        context.Module<ChronicleState>().Missions.Remove(mission);
    }

    private static OpenMission? Find(IBusContext context, string missionId)
    {
        return context.Module<ChronicleState>().Missions.FirstOrDefault(m => m.Id == missionId);
    }

    // Writes one entry, or none at weight 0. The line's ties break by the legacy's seed, the kind, and the first source's sequence
    // number, so the same legacy always gets the same words.
    private static void Write(INarrator narrator, IBusContext context, string kind, int weight, IReadOnlyList<ChronicleSubject> subjects, Situation situation, string fallback, IReadOnlyList<long> sources)
    {
        if (weight <= 0)
        {
            return;
        }

        LegacyState legacy = context.State;
        string key = string.Create(CultureInfo.InvariantCulture, $"{legacy.Seed}|chronicle|{kind}|{sources[0]}");
        string text = narrator.Line(situation, key) ?? fallback;
        ChronicleState state = context.Module<ChronicleState>();
        state.Entries.Add(new ChronicleEntry(state.NextId(), legacy.Day, legacy.Cycle, kind, weight, subjects, text, sources));
    }

    // Enum names as the catalogue writes them: FullSuccess is fullSuccess.
    private static string Word<T>(T value)
        where T : struct, Enum
    {
        string name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
