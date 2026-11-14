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
        IEnumerable<EncounterModel> encounters = index.Where(i => i.Kind == ContentKind.Encounter).Select(i => ContentEdits.Get<EncounterModel>(files, i.Kind, i.Id)).OfType<EncounterModel>();
        List<string> gearTags = BuiltInGearTags.Concat(encounters.SelectMany(e => new[] { e.Careful.GearTag, e.Fast.GearTag }).OfType<string>()).Distinct().Order(StringComparer.Ordinal).ToList();
        IEnumerable<ObjectiveModel> objectives = index.Where(i => i.Kind == ContentKind.Objective).Select(i => ContentEdits.Get<ObjectiveModel>(files, i.Kind, i.Id)).OfType<ObjectiveModel>();
        List<string> objectiveTags = objectives.SelectMany(o => o.Tags).Distinct().Order(StringComparer.Ordinal).ToList();
        return new FormChoices(skills, gearTags, objectiveTags, files.Validate().Pack?.CrewTuning.MaxLevel);
    }
}
