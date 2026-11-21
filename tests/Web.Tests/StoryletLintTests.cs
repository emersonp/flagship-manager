using Sim.Content;
using Web.Forms;
using static Web.Tests.ContentFormsTests;

namespace Web.Tests;

// The lint above the storylets list (1e part six, Task 10; design 4.10): the game's pack has no faults, and a fixture item made for
// each check gives its finding.
public sealed class StoryletLintTests(RepoContentGuard guard) : IClassFixture<RepoContentGuard>
{
    private const string Storylets = "storylets/base.yaml";

    private static ContentFileSet Game() => ContentFileSet.ReadDirectory(Path.Combine(TestApp.RepositoryRoot(), "content"));

    private static ContentFileSet Fixture() => ContentFileSet.ReadDirectory(Path.Combine(TestApp.RepositoryRoot(), "tests", "fixtures", "content"));

    // The fixture with more storylets appended to its file.
    private static ContentFileSet With(string yaml, string? traits = null)
    {
        ContentFileSet files = Fixture();
        files = ContentEdits.ReplaceText(files, Storylets, files.Files[Storylets] + yaml);
        files = traits is null ? files : ContentEdits.ReplaceText(files, "traits/base.yaml", files.Files["traits/base.yaml"] + traits);
        return files;
    }

    private static List<string> Of(StoryletLint lint, string id, bool fault)
    {
        return lint.Findings.Where(f => f.Item.Id == id && f.Fault == fault).Select(f => f.Message).ToList();
    }

    [Fact]
    public void The_games_pack_has_no_faults()
    {
        StoryletLint lint = StoryletLint.Of(Game());

        Assert.True(lint.Loaded);
        Assert.Equal(2, lint.Ships);
        Assert.Empty(lint.Findings.Where(f => f.Fault).Select(f => $"{f.Item.Id} {f.Message}"));
    }

    [Fact]
    public void A_storylet_that_casts_on_no_test_ship_is_a_fault()
    {
        // The fixture's STORYLET_05 asks for loyalty above 100.
        StoryletLint lint = StoryletLint.Of(Fixture());

        Assert.Equal(["cannot be cast on the test ship."], Of(lint, "STORYLET_05", true));
        Assert.DoesNotContain(lint.Findings, f => f.Fault && f.Item.Id != "STORYLET_05");
    }

    [Fact]
    public void A_trait_required_that_nothing_sets_is_a_fault()
    {
        StoryletLint lint = StoryletLint.Of(With(
            """

            - id: STORYLET_LINT
              stakes: 1
              cast:
                subject: "subject.trait.trait_lint >= 1"
              text: |-
                subject: STORYLET_LINT_LINE
              options:
                - { id: option_a, label: A, effects: [{ trait: trait_lint, set: 0 }], text: A }
                - { id: option_b, label: B, effects: [{ trait: trait_lint, set: 0 }], text: B }
            """,
            "- id: trait_lint\n  on: crew\n"));

        Assert.Contains("requires trait_lint on a crew member, and nothing in the pack sets it there.", Of(lint, "STORYLET_LINT", true));
    }

    [Fact]
    public void An_option_no_cast_is_offered_is_a_fault_and_says_the_bound()
    {
        // STORYLET_02's cast fits CREW_02 on the fixture's test ship.
        StoryletLint lint = StoryletLint.Of(With(
            """

            - id: STORYLET_LINT
              stakes: 1
              cast:
                subject: "subject.trait.trait_b >= 1"
              text: |-
                subject: STORYLET_LINT_LINE
              options:
                - { id: option_a, label: A, effects: [{ trait: trait_b, set: 0 }], text: A }
                - { id: option_b, label: B, requires: "subject.loyalty > 100", effects: [{ trait: trait_b, set: 0 }], text: B }
            """));

        Assert.Equal([$"offers option_b to no cast: its requires held for none of up to {StoryletLint.Samples} casts on each test ship."], Of(lint, "STORYLET_LINT", true));
    }

    [Fact]
    public void A_slot_cast_and_never_named_is_advice()
    {
        StoryletLint lint = StoryletLint.Of(With(
            """

            - id: STORYLET_LINT
              stakes: 1
              cast:
                subject: "subject.trait.trait_b >= 1"
                target: "target.aboard"
              text: |-
                subject: STORYLET_LINT_LINE
              options:
                - { id: option_a, label: A, effects: [{ trait: trait_b, set: 0 }, { morale: 1, on: target }], text: A }
                - { id: option_b, label: B, effects: [{ trait: trait_b, set: 0 }], text: B }
            """));

        Assert.True(lint.Loaded);
        Assert.Empty(Of(lint, "STORYLET_LINT", true));
        Assert.Equal(["casts target, and its words never name them."], Of(lint, "STORYLET_LINT", false));
    }

    [Fact]
    public void A_slot_named_and_not_cast_and_a_handler_outside_a_check_are_faults_read_from_the_words()
    {
        // The loader refuses both, so the pack does not load; the lint reads the words from the file and says so.
        StoryletLint lint = StoryletLint.Of(With(
            """

            - id: STORYLET_LINT
              stakes: 1
              cast:
                subject: "subject.aboard"
              text: |-
                subject: STORYLET_LINT_LINE
                target: STORYLET_LINT_LINE
                = #subject# and #target#
              options:
                - { id: option_a, label: A, effects: [], text: "#handler# A" }
                - { id: option_b, label: B, check: { skill: SKILL_A, difficulty: 1 },
                    success: { effects: [], text: "#handler# B" }, failure: { effects: [], text: "#handler# C" } }
            """));

        Assert.False(lint.Loaded);
        Assert.Equal(
            [
                "names target in its script, and casts no slot called target.",
                "names #handler# in option_a's words, outside a check's success and failure, where no one has been sent.",
            ],
            Of(lint, "STORYLET_LINT", true));
        Assert.Empty(Of(lint, "STORYLET_LINT", false));
    }

    [Fact]
    public async Task The_storylets_list_shows_the_lint_above_the_items_and_links_each_finding()
    {
        using TestApp app = NewApp();
        using HttpClient client = app.CreateClient();

        (_, string page) = await Get(client, "/dev/content/storylets");
        (_, string skills) = await Get(client, "/dev/content/skills");

        int lint = page.IndexOf("id=\"lint\"", StringComparison.Ordinal);
        Assert.True(lint >= 0 && lint < page.IndexOf("id=\"h-items\"", StringComparison.Ordinal));
        Assert.Contains("<li class=\"lint--fault\"><span class=\"lint__kind\">Fault</span> <a href=\"/dev/content/storylets/STORYLET_05\">", page, StringComparison.Ordinal);
        Assert.Contains($"up to {StoryletLint.Samples} casts on each ship", page, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"lint\"", skills, StringComparison.Ordinal);
        guard.Verify();
    }

    // --- The vignettes list (Task 11) ---

    [Fact]
    public void The_games_vignettes_have_no_findings()
    {
        StoryletLint lint = StoryletLint.Of(Game(), ContentKind.Vignette);

        Assert.True(lint.Loaded);
        Assert.Equal(ContentKind.Vignette, lint.Kind);
        Assert.Empty(lint.Findings.Select(f => $"{f.Item.Id} {f.Message}"));
    }

    [Fact]
    public void The_vignettes_lint_casts_each_on_the_test_ships_and_reads_its_words()
    {
        // The fixture's VIGNETTE_03 needs trait_a at 2 on the ship, which its test ship does not have. VIGNETTE_LINT casts a target its
        // words never name, and names #handler#, which no vignette has.
        ContentFileSet files = Fixture();
        files = ContentEdits.ReplaceText(files, "vignettes/base.yaml", files.Files["vignettes/base.yaml"] + """

            - id: VIGNETTE_LINT
              cast:
                subject: "subject.aboard"
                target: "target.aboard"
              text: |-
                subject: VIGNETTE_LINT_LINE
              default: option_b
              options:
                - { id: option_a, label: A, effects: [], text: "#handler# A" }
                - { id: option_b, label: B, effects: [], text: B }
            """);

        StoryletLint lint = StoryletLint.Of(files, ContentKind.Vignette);

        Assert.False(lint.Loaded);
        Assert.Equal(["names #handler# in option_a's words, outside a check's success and failure, where no one has been sent."], Of(lint, "VIGNETTE_LINT", true));
        Assert.Equal(["casts target, and its words never name them."], Of(lint, "VIGNETTE_LINT", false));

        StoryletLint fixture = StoryletLint.Of(Fixture(), ContentKind.Vignette);
        Assert.Equal(["cannot be cast on the test ship."], Of(fixture, "VIGNETTE_03", true));
        Assert.DoesNotContain(fixture.Findings, f => f.Item.Id != "VIGNETTE_03");
    }

    [Fact]
    public async Task The_vignettes_list_shows_its_lint_and_links_each_finding_to_the_vignette_form()
    {
        using TestApp app = NewApp();
        using HttpClient client = app.CreateClient();

        (_, string page) = await Get(client, "/dev/content/vignettes");

        int lint = page.IndexOf("id=\"lint\"", StringComparison.Ordinal);
        Assert.True(lint >= 0 && lint < page.IndexOf("id=\"h-items\"", StringComparison.Ordinal));
        Assert.Contains("<li class=\"lint--fault\"><span class=\"lint__kind\">Fault</span> <a href=\"/dev/content/vignettes/VIGNETTE_03\">", page, StringComparison.Ordinal);
        Assert.Contains("casting each vignette on the test ship", page, StringComparison.Ordinal);
        Assert.Contains("href=\"/dev/content/vignettes/new\"", page, StringComparison.Ordinal);
        guard.Verify();
    }
}
