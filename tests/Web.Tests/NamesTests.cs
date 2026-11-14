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
