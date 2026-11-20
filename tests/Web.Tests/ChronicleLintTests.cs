using Sim.Chronicle;
using Sim.Content;
using Sim.Core.Content;
using Sim.Core.Narrative;
using Web.Forms;

namespace Web.Tests;

// The chronicle's lines in the editor (slice 1e part five, Task 7): fixed samples per beat to preview and lint against, and a lint
// that fails a pack with no line for a chronicle beat.
public sealed class ChronicleLintTests
{
    private static readonly ContentPack Fixture = BridgePack.Load();

    private static ContentPack Game()
    {
        ContentLoadResult result = ContentLoader.LoadDirectory(Path.Combine(TestApp.RepositoryRoot(), "content"));
        return result.Pack ?? throw new InvalidOperationException("The game's content did not load.");
    }

    [Fact]
    public void Every_chronicle_beat_has_samples_holding_only_its_own_facts()
    {
        foreach (ContentPack pack in new[] { Fixture, Game() })
        {
            IReadOnlyList<ChronicleSample> samples = ChronicleSamples.Of(pack);
            foreach (string beat in NarrativeFacts.Chronicle.All)
            {
                Assert.Contains(samples, s => s.Situation.Beat == beat);
            }

            foreach (ChronicleSample sample in samples)
            {
                BeatSpec spec = NarrativeFacts.Beats[sample.Situation.Beat];
                foreach (string fact in sample.Situation.Facts.Keys.Where(f => !f.Contains('.', StringComparison.Ordinal)))
                {
                    Assert.Contains(spec.Facts, f => f.Name == fact);
                }

                foreach (string person in sample.Situation.People.Keys)
                {
                    Assert.True(spec.People.Contains(person) || person == "lead" || sample.Situation.Beat == NarrativeFacts.Chronicle.Storylet, $"{sample.Where}: {person}");
                }
            }

            Assert.Equal(samples.Select(s => s.Key).Distinct().Count(), samples.Count);
            Assert.Equal(samples.Count, NarrativeSamples.Of(pack).Count(s => NarrativeFacts.Chronicle.Is(s.Situation.Beat)));
        }
    }

    [Fact]
    public void Neither_pack_fails_the_lint()
    {
        LineLint fixture = LineLint.Of(Fixture);
        LineLint game = LineLint.Of(Game());

        Assert.False(fixture.Fails, string.Join(", ", fixture.ChronicleWithout));
        Assert.False(game.Fails, string.Join(", ", game.ChronicleWithout));
        Assert.DoesNotContain(game.Without, NarrativeFacts.Chronicle.Is);
    }

    [Fact]
    public void A_pack_with_no_line_for_a_chronicle_beat_fails_naming_it()
    {
        LineLint lint = LineLint.Of(Fixture with { Lines = Fixture.Lines.Where(l => l.Beat != NarrativeFacts.Chronicle.Death).ToList() });

        Assert.True(lint.Fails);
        Assert.False(lint.Clean);
        Assert.Equal([NarrativeFacts.Chronicle.Death], lint.ChronicleWithout);
        Assert.DoesNotContain(NarrativeFacts.Chronicle.Death, lint.Without);
    }

    [Fact]
    public void A_chronicle_beat_with_lines_that_miss_a_sample_fails_too()
    {
        // Only backed calls have a line: an overruled or neutral call would be written in fixed words.
        var backed = new LineDef("LINE_CALL_BACKED", NarrativeFacts.Chronicle.Call, [new CriterionDef("standing", ["backed"])], "Backed.");
        LineLint lint = LineLint.Of(Fixture with { Lines = [.. Fixture.Lines.Where(l => l.Beat != NarrativeFacts.Chronicle.Call), backed] });

        Assert.Equal([NarrativeFacts.Chronicle.Call], lint.ChronicleWithout);
        Assert.DoesNotContain("LINE_CALL_BACKED", lint.NeverChosen);
    }

    [Fact]
    public void The_games_chronicle_lines_are_chosen_by_their_samples()
    {
        LineLint lint = LineLint.Of(Game());

        foreach (string id in new[] { "LINE_CHRONICLE_DEATH", "LINE_CHRONICLE_DEATH_LEAD", "LINE_CHRONICLE_MISSION_LOSS", "LINE_CHRONICLE_MISSION_WON_DEAD", "LINE_CHRONICLE_CALL_NEUTRAL", "LINE_CHRONICLE_CALL_OVERRULED_SAFE", "LINE_CHRONICLE_STORYLET_FAILURE" })
        {
            Assert.DoesNotContain(id, lint.NeverChosen);
        }
    }
}
