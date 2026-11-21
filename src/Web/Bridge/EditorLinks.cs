using Sim.Content;
using Sim.Core.Content;
using Web.Tuning;

namespace Web.Bridge;

// Where Dev Play links into the content editor. The links are drawn only for Dev Play, which exists only in developer mode.
public static class EditorLinks
{
    public static string Item(ContentKind kind, string id)
    {
        return $"/dev/content/{ContentKinds.Segment(kind)}/{Uri.EscapeDataString(id)}";
    }

    // A file edited as text (storylets and vignettes until their forms exist), by its name in the editor.
    public static string File(string name)
    {
        return $"/dev/content/files/{name}";
    }

    // The Tuning page with this legacy as the team source and nobody chosen (Dev Play's panel; 1f part one, Task 13).
    public static string Tuning(Guid legacyId)
    {
        return Bench(legacyId, objective: "", [], "");
    }

    // "Bench this" (1f part one, Task 13; design 4.6): the Tuning page with this objective (its template's id, as the bench names it),
    // this legacy as the team source, and the team as picked so far, its lead among them, or nobody. The page judges the picks as the
    // launch would, so a member the launch would now refuse is shown so there.
    public static string Bench(Guid legacyId, string objective, IReadOnlyList<string> crew, string lead)
    {
        var query = new TuningQuery
        {
            Objective = objective,
            Team = BenchTeams.LegacyKind,
            Source = legacyId.ToString(),
            Crew = crew,
            Lead = crew.Contains(lead) ? lead : "",
        };
        return query.Href();
    }

    // The page a line is written on: its own page for a shared line, else the encounter's or objective's whose own words it is.
    public static string? Line(ContentPack pack, string? lineId)
    {
        if (lineId is null || pack.Lines.FirstOrDefault(l => l.Id == lineId) is not { } line)
        {
            return null;
        }

        return line.Owner switch
        {
            null => Item(ContentKind.Line, line.Id),
            string owner when pack.Encounters.Any(e => e.Id == owner) => Item(ContentKind.Encounter, owner),
            string owner when pack.Objectives.Any(o => o.Id == owner) => Item(ContentKind.Objective, owner),
            _ => null,
        };
    }
}
