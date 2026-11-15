using Sim.Core.Content;

namespace Web.Bridge;

// Display names for one pack: the name when set, else the id. Unknown ids come back unchanged.
public sealed class Names(ContentPack pack)
{
    private readonly Dictionary<string, string> skills = Index(pack.Skills.Select(s => (s.Id, s.Name)));
    private readonly Dictionary<string, string> crew = Index(pack.Crew.Select(c => (c.Id, c.Name)));
    private readonly Dictionary<string, string> gear = Index(pack.Gear.Select(g => (g.Id, g.Name)));
    private readonly Dictionary<string, string> objectives = Index(pack.Objectives.Select(o => (o.Id, o.Name)));
    private readonly Dictionary<string, string> encounters = Index(pack.Encounters.Select(e => (e.Id, e.Name)));
    private readonly Dictionary<string, string> decisions = Index(pack.Encounters.Where(e => e.Decision is not null).Select(e => (e.Decision!.Id, e.Decision.Name)));
    private readonly Dictionary<string, string> assignments = Index(pack.Assignments.Select(a => (a.Id, a.Name)));

    // An approach id is unique only within its encounter, so these are keyed by both.
    private readonly Dictionary<(string, string), string> approaches = pack.Encounters
        .SelectMany(e => e.Approaches.Where(a => a.Name is not null).Select(a => ((e.Id, a.Id), a.Name!)))
        .DistinctBy(p => p.Item1)
        .ToDictionary(p => p.Item1, p => p.Item2);

    public string Skill(string id)
    {
        return Of(skills, id);
    }

    public string Crew(string id)
    {
        return Of(crew, id);
    }

    public string Gear(string id)
    {
        return Of(gear, id);
    }

    public string Objective(string templateId)
    {
        return Of(objectives, templateId);
    }

    public string Encounter(string id)
    {
        return Of(encounters, id);
    }

    public string Decision(string id)
    {
        return Of(decisions, id);
    }

    public string Assignment(string id)
    {
        return Of(assignments, id);
    }

    // The approach's name, or its id when it has none.
    public string Approach(string encounterId, string approachId)
    {
        return approaches.TryGetValue((encounterId, approachId), out string? name) ? name : approachId;
    }

    // Only named items are kept, so a lookup that misses falls back to the id. The loader refuses a repeated id, but a page must
    // never crash on one, so the first name wins.
    private static Dictionary<string, string> Index(IEnumerable<(string Id, string? Name)> items)
    {
        var names = new Dictionary<string, string>();
        foreach ((string id, string? name) in items)
        {
            if (name is not null)
            {
                names.TryAdd(id, name);
            }
        }

        return names;
    }

    private static string Of(Dictionary<string, string> names, string id)
    {
        return names.TryGetValue(id, out string? name) ? name : id;
    }
}
