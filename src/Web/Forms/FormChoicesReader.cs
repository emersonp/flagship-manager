using Sim.Content;

namespace Web.Forms;

public static class FormChoicesReader
{
    // The two tags the mission rules look for by name, besides the ones the encounters ask for.
    private static readonly string[] BuiltInGearTags = ["comms", "medical"];

    public static FormChoices Read(ContentFileSet files)
    {
        IReadOnlyList<ContentItemRef> index = files.Index();
        List<SkillChoice> skills = index.Where(i => i.Kind == ContentKind.Skill).Select(i => new SkillChoice(i.Id, i.Name)).ToList();
        List<EncounterModel> encounters = index.Where(i => i.Kind == ContentKind.Encounter).Select(i => ContentEdits.Get<EncounterModel>(files, i.Kind, i.Id)).OfType<EncounterModel>().ToList();
        IEnumerable<string> inUse = index.Where(i => i.Kind == ContentKind.Gear).Select(i => ContentEdits.Get<GearModel>(files, i.Kind, i.Id)?.Tag).OfType<string>();
        List<string> gearTags = BuiltInGearTags.Concat(encounters.SelectMany(e => new[] { e.Careful.GearTag, e.Fast.GearTag }).OfType<string>()).Concat(inUse).Distinct().Order(StringComparer.Ordinal).ToList();
        IEnumerable<ObjectiveModel> objectives = index.Where(i => i.Kind == ContentKind.Objective).Select(i => ContentEdits.Get<ObjectiveModel>(files, i.Kind, i.Id)).OfType<ObjectiveModel>();
        List<string> objectiveTags = objectives.SelectMany(o => o.Tags).Distinct().Order(StringComparer.Ordinal).ToList();
        return new FormChoices(skills, gearTags, objectiveTags, files.Validate().Pack?.CrewTuning.MaxLevel)
        {
            Encounters = encounters.Select(e => new EncounterChoice(e.Id, e.Name, e.Tags, e.Chain)).ToList(),
            EncounterTags = encounters.SelectMany(e => e.Tags).Distinct().Order(StringComparer.Ordinal).ToList(),
            DecisionOwners = DecisionOwnersOf(encounters),
        };
    }

    // Decision id to the encounter that holds it (the first, if the files hold one twice).
    public static IReadOnlyDictionary<string, string> DecisionOwnersOf(IEnumerable<EncounterModel> encounters)
    {
        var owners = new Dictionary<string, string>();
        foreach (EncounterModel encounter in encounters)
        {
            if (encounter.Decision is { } decision)
            {
                owners.TryAdd(decision.Id, encounter.Id);
            }
        }

        return owners;
    }

    // The same, read from the files as they are now: what the save's own check uses under the lock.
    public static IReadOnlyDictionary<string, string> DecisionOwnersIn(ContentFileSet files)
    {
        return DecisionOwnersOf(files.Index().Where(i => i.Kind == ContentKind.Encounter).Select(i => ContentEdits.Get<EncounterModel>(files, i.Kind, i.Id)).OfType<EncounterModel>());
    }
}
