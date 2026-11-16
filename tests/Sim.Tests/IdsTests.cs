using Sim.Core.Content;

namespace Sim.Tests;

public class IdsTests
{
    [Theory]
    [InlineData("SKILL_A", true)]
    [InlineData("ENCOUNTER_01", true)]
    [InlineData("A", true)]
    [InlineData("1SKILL", false)]
    [InlineData("_SKILL", false)]
    [InlineData("SKILL-A", false)]
    [InlineData("SKILL.A", false)]
    [InlineData("Skill_a", false)]
    [InlineData("skill_a", false)]
    [InlineData("", false)]
    public void An_item_id_is_an_upper_case_letter_then_upper_case_letters_digits_and_underscores(string id, bool valid)
    {
        Assert.Equal(valid, Ids.IsItemId(id));
    }

    [Theory]
    [InlineData("careful", true)]
    [InlineData("captain_ask", true)]
    [InlineData("a", true)]
    [InlineData("step2", true)]
    [InlineData("1st", false)]
    [InlineData("_flag", false)]
    [InlineData("flag-a", false)]
    [InlineData("flag.a", false)]
    [InlineData("captainAsk", false)]
    [InlineData("Flag", false)]
    [InlineData("flag a", false)]
    [InlineData("", false)]
    public void An_inner_id_is_a_lower_case_letter_then_lower_case_letters_digits_and_underscores(string id, bool valid)
    {
        Assert.Equal(valid, Ids.IsInnerId(id));
    }

    [Fact]
    public void An_id_may_be_40_characters_and_not_41()
    {
        Assert.Equal(40, Ids.MaxLength);
        Assert.True(Ids.IsItemId(new string('A', 40)));
        Assert.False(Ids.IsItemId(new string('A', 41)));
        Assert.True(Ids.IsInnerId(new string('a', 40)));
        Assert.False(Ids.IsInnerId(new string('a', 41)));
    }

    [Fact]
    public void A_trailing_line_break_is_not_let_through()
    {
        Assert.False(Ids.IsItemId("SKILL_A\n"));
        Assert.False(Ids.IsInnerId("flag_a\n"));
    }
}
