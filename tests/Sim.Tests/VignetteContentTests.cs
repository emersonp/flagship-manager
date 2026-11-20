using Sim.Content;
using Sim.Core.Content;

namespace Sim.Tests;

// Vignettes (1e part two, design 4.3): the loader's rules and messages.
public class VignetteContentTests
{
    private const string Choice = """
        - id: VIGNETTE_X
          cast:
            subject: "subject.history.last('lost_crewmate')"
          text: "subject: LINE"
          default: option_b
          options:
            - { id: option_a, label: LABEL_A, effects: [{ morale: 3, on: subject }] }
            - { id: option_b, label: LABEL_B, effects: [] }

        """;

    private const string Plain = """
        - id: VIGNETTE_Y
          cast:
            subject: "subject.history.last('rested') && subject.morale >= 60"
          effects: [{ morale: 2, on: subject }]

        """;

    private static ContentLoadResult LoadWith(string vignettes)
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        files["vignettes/base.yaml"] = vignettes;
        return ContentLoader.Load(files);
    }

    private static void AssertRefused(string vignettes, string message)
    {
        ContentLoadResult result = LoadWith(vignettes);

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.File == "vignettes/base.yaml" && e.Message == message);
    }

    [Fact]
    public void The_fixtures_vignettes_load_with_the_tuning()
    {
        ContentPack pack = TestContent.Pack;

        Assert.Equal(["VIGNETTE_01", "VIGNETTE_02", "VIGNETTE_03", "VIGNETTE_04"], pack.Vignettes.Select(v => v.Id));
        Assert.Equal((3, 6, 3), (pack.VignettesPerCycle, pack.VignetteCooldown, pack.CaptainsTimePerCycle));
        VignetteDef choice = pack.Vignettes[1];
        Assert.Equal(("option_b", 2, "lost_crewmate"), (choice.Default, choice.Options.Count, choice.Fact));
        Assert.Equal(["subject", "target"], pack.Vignettes[3].Slots);
        Assert.Equal([new StoryletEffect(Morale: -1, MoraleOn: "crew")], pack.Vignettes[2].Effects);
    }

    // 1e part four, Task 5 (design 4.6): a vignette with no cooldown takes { cast: vignettes.cooldown }; one written is its own.
    [Fact]
    public void A_vignette_with_no_cooldown_takes_the_packs()
    {
        ContentPack plain = LoadWith(Plain).Pack!;
        ContentPack own = LoadWith(Plain.Replace("  effects:", "  cooldown: { global: 2 }\n  effects:")).Pack!;

        Assert.Null(plain.Vignettes.Single().Cooldown);
        Assert.Equal(new Cooldown(6, 0), plain.CooldownOf(plain.Vignettes.Single()));
        Assert.Equal(new Cooldown(0, 2), own.CooldownOf(own.Vignettes.Single()));
    }

    [Fact]
    public void A_vignettes_words_are_lines_of_the_storylet_beats()
    {
        List<LineDef> owned = TestContent.Pack.Lines.Where(l => l.Owner == "VIGNETTE_02").ToList();

        Assert.Equal(["VIGNETTE_02.text", "VIGNETTE_02.option_a.text", "VIGNETTE_02.option_b.text"], owned.Select(l => l.Id));
        Assert.Equal(["storylet", "storyletAnswer", "storyletAnswer"], owned.Select(l => l.Beat));
    }

    [Fact]
    public void A_vignette_needs_no_trait_and_no_stakes()
    {
        ContentLoadResult result = LoadWith(Plain + Choice);

        Assert.Empty(result.Errors);
        Assert.Equal(["VIGNETTE_Y", "VIGNETTE_X"], result.Pack!.Vignettes.Select(v => v.Id));
    }

    [Theory]
    [InlineData("  default: option_b\n", "", "VIGNETTE_X: A vignette with options needs a default, and it must be one of them.")]
    [InlineData("default: option_b", "default: option_c", "VIGNETTE_X: A vignette with options needs a default, and it must be one of them.")]
    [InlineData("    - { id: option_b, label: LABEL_B, effects: [] }\n", "", "VIGNETTE_X: A vignette's choice is two options.")]
    [InlineData("  default: option_b\n", "  default: option_b\n  effects: [{ morale: 1, on: subject }]\n", "VIGNETTE_X: A vignette has effects or two options, not both.")]
    [InlineData("  cast:\n", "  stakes: 2\n  cast:\n", "VIGNETTE_X: A vignette has no stakes.")]
    [InlineData("  cast:\n", "  scope: crew\n  cast:\n", "VIGNETTE_X: scope was replaced by cast in part four; a storylet for crew casts subject.")]
    [InlineData("    - { id: option_a, label: LABEL_A,", "    - { id: option_a, label: LABEL_A, requires: \"subject.fit\",", "VIGNETTE_X: Option 'option_a': a vignette's options have no requires.")]
    [InlineData("  text: \"subject: LINE\"\n", "  text: |-\n    subject: ONE\n    subject: TWO\n    subject: THREE\n    = FOUR\n", "VIGNETTE_X: A vignette's text is one to three entries.")]
    public void A_malformed_vignette_with_a_choice_is_refused(string from, string to, string message)
    {
        Assert.Contains(from, Choice, StringComparison.Ordinal);

        AssertRefused(Choice.Replace(from, to, StringComparison.Ordinal), message);
    }

    [Theory]
    [InlineData("  effects: [{ morale: 2, on: subject }]\n", "", "VIGNETTE_Y: A vignette with no choice needs at least one effect.")]
    [InlineData("  effects: [{ morale: 2, on: subject }]\n", "  effects: [{ morale: 2, on: subject }]\n  default: option_a\n", "VIGNETTE_Y: A vignette with no choice has no default.")]
    [InlineData("  cast:\n    subject: \"subject.history.last('rested') && subject.morale >= 60\"\n", "  requires: \"subject.history.last('rested')\"\n", "VIGNETTE_Y: requires, at character 1: subject is not a slot this expression can read; it reads ship, cycle, storylet, and chronicle.")]
    [InlineData("subject.morale >= 60", "subject.morale >= 60 && subject.trait.nosuch > 0", "VIGNETTE_Y: cast.subject, at character 73: No trait named nosuch.")]
    public void A_malformed_vignette_with_no_choice_is_refused(string from, string to, string message)
    {
        Assert.Contains(from, Plain, StringComparison.Ordinal);

        AssertRefused(Plain.Replace(from, to, StringComparison.Ordinal), message);
    }

    [Fact]
    public void A_vignette_and_a_storylet_cannot_share_an_id()
    {
        ContentLoadResult result = LoadWith(Choice.Replace("VIGNETTE_X", "STORYLET_01", StringComparison.Ordinal));

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.Message.Contains("STORYLET_01", StringComparison.Ordinal));
    }

    [Fact]
    public void A_storylet_cannot_carry_a_vignettes_effects_or_default()
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        files["storylets/base.yaml"] = files["storylets/base.yaml"].Replace("  stakes: 2\n", "  stakes: 2\n  default: option_a\n", StringComparison.Ordinal);

        ContentLoadResult result = ContentLoader.Load(files);

        Assert.Contains(result.Errors, e => e.Message == "STORYLET_01: A storylet's effects and default belong to a vignette; a storylet's effects are in its options.");
    }
}
