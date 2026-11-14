using Sim.Core.Content;
using Web.Bridge;

namespace Web.Tests;

public class NamesTests
{
    private static readonly ContentPack Plain = BridgePack.Load();

    [Fact]
    public void With_no_names_set_every_kind_reads_as_its_id()
    {
        var names = new Names(Plain);

        Assert.Equal("SKILL_A", names.Skill("SKILL_A"));
        Assert.Equal("CREW_01", names.Crew("CREW_01"));
        Assert.Equal("GEAR_01", names.Gear("GEAR_01"));
        Assert.Equal("OBJECTIVE_TEMPLATE_1", names.Objective("OBJECTIVE_TEMPLATE_1"));
        Assert.Equal("ENCOUNTER_01", names.Encounter("ENCOUNTER_01"));
        Assert.Equal("DECISION_PROMPT_1", names.Decision("DECISION_PROMPT_1"));
        Assert.Equal("ASSIGNMENT_TEMPLATE_1", names.Assignment("ASSIGNMENT_TEMPLATE_1"));
    }

    [Fact]
    public void A_set_name_replaces_the_id_for_each_of_the_seven_kinds()
    {
        var names = new Names(NamedPack.Of(Plain));

        Assert.Equal("NAME_PLACEHOLDER_SKILL_A", names.Skill("SKILL_A"));
        Assert.Equal("NAME_PLACEHOLDER_CREW_01", names.Crew("CREW_01"));
        Assert.Equal("NAME_PLACEHOLDER_GEAR_01", names.Gear("GEAR_01"));
        Assert.Equal("NAME_PLACEHOLDER_OBJECTIVE_TEMPLATE_1", names.Objective("OBJECTIVE_TEMPLATE_1"));
        Assert.Equal("NAME_PLACEHOLDER_ENCOUNTER_01", names.Encounter("ENCOUNTER_01"));
        Assert.Equal("NAME_PLACEHOLDER_DECISION_PROMPT_1", names.Decision("DECISION_PROMPT_1"));
        Assert.Equal("NAME_PLACEHOLDER_ASSIGNMENT_TEMPLATE_1", names.Assignment("ASSIGNMENT_TEMPLATE_1"));
    }

    // The loader refuses a repeated decision id, but a page must not crash if one ever got through: the first one wins.
    [Fact]
    public void A_repeated_id_does_not_throw_and_the_first_name_wins()
    {
        ContentPack pack = NamedPack.Of(Plain);
        EncounterDef first = pack.Encounters[0];
        EncounterDef second = pack.Encounters[1];
        ContentPack repeated = pack with
        {
            Skills = [.. pack.Skills, pack.Skills[0] with { Name = "NAME_PLACEHOLDER_LATER" }],
            Encounters = [first, second with { Decision = first.Decision! with { Name = "NAME_PLACEHOLDER_LATER" } }, .. pack.Encounters.Skip(2)],
        };

        var names = new Names(repeated);

        Assert.Equal("NAME_PLACEHOLDER_SKILL_A", names.Skill("SKILL_A"));
        Assert.Equal(first.Decision!.Name, names.Decision(first.Decision.Id));
    }

    // A stored legacy may name an id the loaded pack lacks.
    [Fact]
    public void An_unknown_id_comes_back_unchanged()
    {
        var names = new Names(NamedPack.Of(Plain));

        Assert.Equal("CREW_GONE", names.Crew("CREW_GONE"));
        Assert.Equal("SKILL_GONE", names.Skill("SKILL_GONE"));
        Assert.Equal("DECISION_GONE", names.Decision("DECISION_GONE"));
    }

    // A kind's names never answer for another kind's id.
    [Fact]
    public void Each_kind_looks_only_in_its_own_list()
    {
        var names = new Names(NamedPack.Of(Plain));

        Assert.Equal("CREW_01", names.Skill("CREW_01"));
        Assert.Equal("SKILL_A", names.Crew("SKILL_A"));
    }
}
