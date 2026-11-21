using System.Globalization;
using Sim.Core;
using Sim.Core.Content;
using Sim.Core.Narrative;

namespace Sim.Chronicle;

// A chronicle beat to preview a line against: the situation, the key its line is picked by, and where it comes from.
public sealed record ChronicleSample(Situation Situation, string Key, string Where);

// Fixed sample situations for the chronicle's beats (slice 1e part five, design 4.9), built by the same code that writes entries, so
// the editor previews and lints a chronicle line against what the module would hand it. Each beat is sampled over the words its lines
// may test: every outcome with no, one, and two deaths; every answer against every preference; a death on a check, of the lead, and
// in a collapse; a storylet answered plainly and by a check that succeeds and fails. The people, objective, encounter, site,
// decision, and storylet are the pack's first, or ids when it has none.
public static class ChronicleSamples
{
    public static IReadOnlyList<ChronicleSample> Of(ContentPack pack)
    {
        var situations = new Situations(pack);
        List<string> crew = pack.Crew.Select(c => c.Id).Distinct().ToList();
        string Crew(int i) => i < crew.Count ? crew[i] : $"crew_{i + 1}";
        string lead = Crew(0), other = Crew(1), handler = Crew(2);
        string objective = pack.Objectives.FirstOrDefault()?.Id ?? "sample_objective";
        string encounter = pack.Encounters.FirstOrDefault()?.Id ?? "sample_encounter";
        EncounterDef? decided = pack.Encounters.FirstOrDefault(e => e.Decision is not null);
        string callAt = decided?.Id ?? encounter;
        string decision = decided?.Decision!.Id ?? "sample_decision";
        string? site = pack.Assignments.FirstOrDefault()?.Id;
        var mission = new OpenMission { Id = "sample_mission", ObjectiveId = objective, SiteId = site, Team = [lead, other, handler], LeadId = lead };
        var samples = new List<ChronicleSample>();
        void Add(Situation situation, string where) =>
            samples.Add(new ChronicleSample(situation, string.Create(CultureInfo.InvariantCulture, $"sample|chronicle|{samples.Count}"), where));

        Add(Situations.Founding(crew.Count), "The founding");

        Add(situations.Death(new CrewDied(other, mission.Id, encounter, "sample_approach"), mission, 8), "A death on a check");
        Add(situations.Death(new CrewDied(lead, mission.Id, encounter, "sample_approach"), mission, 8), "The lead's death on a check");
        Add(situations.Death(new CrewDied(other, mission.Id, encounter, null), mission, 1), "A death in a collapse");

        foreach (string outcome in NarrativeFacts.Outcomes)
        {
            for (int deaths = 0; deaths <= 2; deaths++)
            {
                Add(situations.Mission(mission, outcome, deaths), string.Create(CultureInfo.InvariantCulture, $"A mission, {outcome}, {deaths} dead"));
            }
        }

        foreach (string answer in NarrativeFacts.Answers)
        {
            foreach (string prefers in NarrativeFacts.Answers)
            {
                string standing = Standing(answer, prefers);
                Add(situations.Call(new CallAnswered(mission.Id, decision, callAt, answer, prefers, standing, lead), mission), $"A call, {answer} where the lead preferred {prefers}");
            }
        }

        StoryletDef? storylet = pack.Storylets.FirstOrDefault(s => s.Options.Count > 0);
        IReadOnlyList<string> slots = storylet?.Slots.ToList() ?? [];
        var cast = slots.Select((slot, i) => (slot, Crew(i))).ToDictionary(p => p.slot, p => p.Item2);
        string storyletId = storylet?.Id ?? "sample_storylet";
        string plain = storylet?.Options[0].Id ?? "sample_option";
        string check = storylet?.Options.FirstOrDefault(o => o.Check is not null)?.Id ?? plain;
        Add(situations.Storylet(new StoryletAnswered("sample", storyletId, cast, plain), storylet, slots), "A storylet answered");
        foreach (bool success in new[] { true, false })
        {
            var roll = new StoryletRoll("sample_skill", 0, 0, [], 0, 0, success);
            Add(situations.Storylet(new StoryletAnswered("sample", storyletId, cast, check, handler, roll), storylet, slots), $"A storylet's check, {(success ? "success" : "failure")}");
        }

        return samples;
    }

    // The situation the module writes a storylet's entry against, for this cast, option, handler, and check outcome (null for an
    // option settled by its effects): the editor's "What plays here" (1e part six, design 4.3) previews the chronicle line with it.
    public static Situation Storylet(ContentPack pack, string storyletId, IReadOnlyDictionary<string, string> cast, string optionId, string? handlerId, bool? success)
    {
        var situations = new Situations(pack);
        StoryletDef? storylet = situations.StoryletOf(storyletId);
        List<string> slots = storylet?.Slots.Where(cast.ContainsKey).ToList() ?? cast.Keys.ToList();
        StoryletRoll? roll = success is { } s ? new StoryletRoll("preview", 0, 0, [], 0, 0, s) : null;
        return situations.Storylet(new StoryletAnswered("preview", storyletId, cast, optionId, handlerId, roll), storylet, slots);
    }

    // As the missions module stands an answer: withdrawing where the lead preferred safe is neutral, their own choice backs them.
    private static string Standing(string answer, string prefers)
    {
        return answer == "withdraw" && prefers == "safe" ? "neutral" : answer == prefers ? "backed" : "overruled";
    }
}
