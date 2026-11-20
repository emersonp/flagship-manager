using System.Globalization;
using Sim.Core;
using Sim.Core.Content;
using Sim.Core.Narrative;

namespace Sim.Chronicle;

// The situation each kind of entry is written against (design 4.3), holding exactly the facts NarrativeFacts lists for its beat. Names
// and pronouns come from the pack, as it is at the moment of writing.
internal sealed class Situations(ContentPack pack)
{
    private readonly Dictionary<string, CrewDef> crew = pack.Crew.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
    private readonly Dictionary<string, string> objectives = Names(pack.Objectives.Select(o => (o.Id, o.Name)));
    private readonly Dictionary<string, string> encounters = Names(pack.Encounters.Select(e => (e.Id, e.Name)));
    private readonly Dictionary<string, string> sites = Names(pack.Assignments.Select(a => (a.Id, a.Name)));
    private readonly Dictionary<string, DecisionDef> decisions = pack.Encounters.Where(e => e.Decision is not null).Select(e => e.Decision!)
        .GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());

    public static Situation Founding(int crew)
    {
        return new Situation(
            NarrativeFacts.Chronicle.Founding,
            new Dictionary<string, FactValue> { ["crew"] = FactValue.Of(crew) },
            new Dictionary<string, Person>(),
            new Dictionary<string, string> { ["crew"] = Number(crew) });
    }

    // Mission: null when the chronicle knows no mission by that id, and then only the dead and the encounter are in it.
    public Situation Death(CrewDied died, OpenMission? mission, int cycles)
    {
        var builder = new Builder(this, NarrativeFacts.Chronicle.Death, mission?.LeadId);
        builder.Mission(mission);
        if (died.EncounterId is { } encounter)
        {
            builder.Encounter(encounter);
        }

        if (died.ApproachId is { } approach)
        {
            builder.Facts["approach"] = FactValue.Of(approach);
        }

        builder.Facts["cycles"] = FactValue.Of(cycles);
        builder.Words["cycles"] = Count(cycles, "cycle");
        builder.Add("dead", died.CrewId);
        return builder.Build();
    }

    public Situation Mission(OpenMission mission, string outcome, int deaths)
    {
        var builder = new Builder(this, NarrativeFacts.Chronicle.Mission, mission.LeadId);
        builder.Mission(mission);
        builder.Facts["outcome"] = FactValue.Of(outcome);
        builder.Facts["team"] = FactValue.Of(mission.Team.Count);
        builder.Facts["deaths"] = FactValue.Of(deaths);
        builder.Words["team"] = Number(mission.Team.Count);
        builder.Words["deaths"] = Number(deaths);
        return builder.Build();
    }

    public Situation Call(CallAnswered call, OpenMission? mission)
    {
        var builder = new Builder(this, NarrativeFacts.Chronicle.Call, call.LeadId);
        builder.Mission(mission);
        builder.Encounter(call.EncounterId);
        builder.Facts["decision"] = FactValue.Of(call.DecisionId);
        builder.Words["decision"] = decisions.TryGetValue(call.DecisionId, out DecisionDef? decision) && decision.Name is { } name ? name : call.DecisionId;
        builder.Facts["answer"] = FactValue.Of(call.Answer);
        builder.Facts["prefers"] = FactValue.Of(call.LeadPrefers);
        builder.Facts["standing"] = FactValue.Of(call.Standing);
        return builder.Build();
    }

    // The decision's own chronicle weight, if it sets one.
    public int? CallWeight(string decisionId) => decisions.GetValueOrDefault(decisionId)?.ChronicleWeight;

    // The crew member as they are named now, frozen with the entry.
    public ChronicleSubject Subject(string crewId) => new(crewId, Name(crewId));

    private string Name(string crewId) => crew.GetValueOrDefault(crewId)?.Name ?? crewId;

    private Person Person(string crewId) => new(crewId, Name(crewId), pack.PronounsOf(crew.GetValueOrDefault(crewId)));

    private static string Number(int n) => n.ToString(CultureInfo.InvariantCulture);

    // "1 cycle", "8 cycles".
    private static string Count(int n, string unit) => $"{Number(n)} {unit}{(n == 1 ? "" : "s")}";

    private static Dictionary<string, string> Names(IEnumerable<(string Id, string? Name)> items)
    {
        var names = new Dictionary<string, string>();
        foreach ((string id, string? name) in items)
        {
            names.TryAdd(id, name ?? id);
        }

        return names;
    }

    private sealed class Builder(Situations names, string beat, string? leadId)
    {
        public Dictionary<string, FactValue> Facts { get; } = new();

        public Dictionary<string, string> Words { get; } = new();

        private readonly Dictionary<string, Person> people = new();

        public void Mission(OpenMission? mission)
        {
            if (mission is null)
            {
                return;
            }

            Facts["objective"] = FactValue.Of(mission.ObjectiveId);
            Words["objective"] = names.objectives.GetValueOrDefault(mission.ObjectiveId, mission.ObjectiveId);
            if (mission.SiteId is { } site)
            {
                Words["site"] = names.sites.GetValueOrDefault(site, site);
            }
        }

        public void Encounter(string encounterId)
        {
            Facts["encounter"] = FactValue.Of(encounterId);
            Words["encounter"] = names.encounters.GetValueOrDefault(encounterId, encounterId);
        }

        // A person by role, with the stats every person has that the chronicle knows: their id, and whether they led.
        public void Add(string role, string crewId)
        {
            people[role] = names.Person(crewId);
            Facts[$"{role}.id"] = FactValue.Of(crewId);
            Facts[$"{role}.isLead"] = FactValue.Of(crewId == leadId);
        }

        public Situation Build()
        {
            if (leadId is not null)
            {
                Add("lead", leadId);
            }

            return new Situation(beat, Facts, people, Words);
        }
    }
}
