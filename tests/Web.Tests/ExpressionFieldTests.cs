using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Expressions;
using Web.Components.Content;
using Web.Forms;
using static Web.Tests.ContentFormsTests;
using Widget = Web.Forms.ExpressionField;

namespace Web.Tests;

// The expression widget (1e part six, Task 7): rows from posted fields and the string from rows, the text with its reason or its error at
// its character, and the Rows / Text switch, on the game's pack. No form holds a widget until the storylet form (Task 8), so a form made
// here holds one, on requires with two slots.
public sealed class ExpressionFieldTests(RepoContentGuard guard) : IClassFixture<RepoContentGuard>
{
    private static readonly ExpressionScope Requires = new("requires", "Requires", ["subject", "target"]);

    private static readonly Lazy<ContentFileSet> GameFiles = new(() => ContentFileSet.ReadDirectory(Path.Combine(TestApp.RepositoryRoot(), "content")));

    private static ContentPack GamePack => GameFiles.Value.Validate().Pack!;

    private static readonly Lazy<ExpressionChoices> LazyChoices = new(() => FormChoicesReader.Read(GameFiles.Value).Expressions);

    private static ExpressionChoices Choices => LazyChoices.Value;

    private static Row Row(ShapeKind kind, string who, string name, string? op = null, string? value = null)
    {
        return new Row(kind, who, name, null, op, value, null, null, false);
    }

    private static FormValues Posted(params (string Name, string Value)[] fields)
    {
        return new FormValues(fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value)));
    }

    private static FormValues Stored(string expression)
    {
        return new FormValues(Widget.Load(Requires, expression, Choices));
    }

    [Fact]
    public void Posting_rows_gives_the_string_Shapes_Write_gives()
    {
        FormValues posted = Posted(
            ("requires.mode", "rows"),
            ("requires[0].kind", "Trait"), ("requires[0].who", "subject"), ("requires[0].name", "grieving"), ("requires[0].op", ">="), ("requires[0].value", "1"),
            ("requires[1].kind", "HistoryCount"), ("requires[1].who", "target"), ("requires[1].name", "lost_mission"), ("requires[1].window", "2"), ("requires[1].op", ">"), ("requires[1].value", "0"),
            ("requires[2].kind", "Trait"), ("requires[2].who", "ship"), ("requires[2].name", "sensors_quirk"), ("requires[2].op", "=="), ("requires[2].value", "let_ride"),
            ("requires[3].kind", "Presence"), ("requires[3].who", "target"), ("requires[3].name", "fit"), ("requires[3].not", "true"),
            // A part the shape does not read is ignored.
            ("requires[3].value", "9"));
        var errors = new Dictionary<string, string>();

        string? written = Widget.Bind(posted, Requires, Choices, errors);

        Assert.Empty(errors);
        string expected = Shapes.Write(
        [
            Row(ShapeKind.Trait, "subject", "grieving", ">=", "1"),
            Row(ShapeKind.HistoryCount, "target", "lost_mission", ">", "0") with { Window = 2 },
            Row(ShapeKind.Trait, "ship", "sensors_quirk", "==", "let_ride"),
            Row(ShapeKind.Presence, "target", "fit") with { Negated = true },
        ]);
        Assert.Equal(expected, written);
        Assert.Equal("subject.trait.grieving >= 1 && target.history.count('lost_mission', 2) > 0 && ship.trait.sensors_quirk == 'let_ride' && !target.fit", written);
    }

    // Every expression in the game's pack opens as rows, and its fields bind back to it character for character.
    [Fact]
    public void Every_expression_in_the_pack_opens_as_rows_and_binds_back_unchanged()
    {
        var seen = 0;
        void Check(string where, string source, IReadOnlyList<string> slots)
        {
            var scope = new ExpressionScope("x", where, slots);
            FormValues values = new(Widget.Load(scope, source, Choices));
            Assert.True(values.Get("x.mode") == "rows", $"{where} opens as text: {source}");
            var errors = new Dictionary<string, string>();
            Assert.Equal(source, Widget.Bind(values, scope, Choices, errors));
            Assert.Empty(errors);
            seen++;
        }

        foreach (StoryletDef storylet in GamePack.Storylets)
        {
            List<string> slots = storylet.Slots.ToList();
            for (int k = 0; k < storylet.Cast.Count; k++)
            {
                Check($"{storylet.Id} cast.{storylet.Cast[k].Name}", storylet.Cast[k].Condition.Source, slots[..(k + 1)]);
            }

            if (storylet.Requires is { } requires)
            {
                Check($"{storylet.Id} requires", requires.Source, slots);
            }

            foreach (StoryletOption option in storylet.Options.Where(o => o.Requires is not null))
            {
                Check($"{storylet.Id} options.{option.Id}", option.Requires!.Source, slots);
            }
        }

        Assert.True(seen > 8);
    }

    [Fact]
    public void A_row_missing_a_part_or_with_a_bad_number_is_refused_beside_that_part()
    {
        FormValues posted = Posted(
            ("requires.mode", "rows"),
            ("requires[0].kind", "Trait"), ("requires[0].who", "subject"), ("requires[0].name", ""), ("requires[0].op", ">="), ("requires[0].value", "1"),
            ("requires[1].kind", "Stat"), ("requires[1].who", "subject"), ("requires[1].name", "morale"), ("requires[1].op", "<"), ("requires[1].value", "lots"),
            ("requires[2].kind", ""),
            ("requires[3].kind", "Trait"), ("requires[3].who", "subject"), ("requires[3].name", "nosuch"), ("requires[3].op", ">="), ("requires[3].value", "1"));
        var errors = new Dictionary<string, string>();

        Assert.Null(Widget.Bind(posted, Requires, Choices, errors));
        Assert.Equal("Choose a trait.", errors["requires[0].name"]);
        Assert.Equal("Enter a whole number.", errors["requires[1].value"]);
        Assert.Equal("Choose what this clause reads.", errors["requires[2].kind"]);
        Assert.Contains("nosuch", errors["requires[3].kind"], StringComparison.Ordinal);
    }

    // The loader takes a level's name only beside == or !=, so a name compared in order is written as its number.
    [Fact]
    public void A_level_named_with_an_ordering_operator_is_written_as_its_number()
    {
        TraitChoice quirk = Choices.Trait("sensors_quirk")!;
        (int number, string name) = quirk.Levels.OrderBy(l => l.Key).Select(l => (l.Key, l.Value)).Last();
        FormValues posted = Posted(("requires.mode", "rows"), ("requires[0].kind", "Trait"), ("requires[0].who", "ship"), ("requires[0].name", "sensors_quirk"), ("requires[0].op", ">="), ("requires[0].value", name));

        Assert.Equal($"ship.trait.sensors_quirk >= {number}", Widget.Bind(posted, Requires, Choices, new Dictionary<string, string>()));
    }

    [Fact]
    public async Task A_stored_or_expression_opens_as_text_with_its_reason()
    {
        const string Source = "subject.trait.shaken >= 1 || subject.morale < 30";
        FormValues values = Stored(Source);

        Assert.Equal("text", values.Get("requires.mode"));
        ExpressionView view = Widget.View(values, Requires, Choices);
        Assert.True(view.IsText);
        Assert.Equal($"Stays as text: {Shapes.UsesOr}.", view.Check.Reason);
        Assert.Equal(Source, Widget.Bind(values, Requires, Choices, new Dictionary<string, string>()));

        using var app = new TestApp();
        string html = await Draw(app, values);
        Assert.Contains($"Stays as text: {Shapes.UsesOr}.", html, StringComparison.Ordinal);
        Assert.Contains("name=\"requires.text\"", html, StringComparison.Ordinal);
        Assert.Matches("id=\"f-requires-as-text\"[^>]*aria-pressed=\"true\"", html);
        Assert.Contains("data-insert=\"trait.grieving\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rows_are_drawn_as_sentences_with_their_text_under_them()
    {
        FormValues values = Stored("subject.trait.grieving >= 1 && ship.trait.sensors_quirk == 'let_ride'");

        using var app = new TestApp();
        string html = System.Net.WebUtility.HtmlDecode(await Draw(app, values));

        Assert.Contains("<button class=\"tab\" type=\"button\" id=\"f-requires-as-rows\"", html, StringComparison.Ordinal);
        Assert.Matches("id=\"f-requires-as-rows\"[^>]*aria-pressed=\"true\"", html);
        Assert.Contains("<code class=\"expr__clause\">subject.trait.grieving >= 1</code>", html, StringComparison.Ordinal);
        Assert.Contains("<code class=\"expr__clause\">ship.trait.sensors_quirk == 'let_ride'</code>", html, StringComparison.Ordinal);
        Assert.Contains("name=\"requires[0].kind\"", html, StringComparison.Ordinal);
        // A trait with named levels compared with "is" picks its level by name; one without has a stepper.
        Assert.Matches("<select id=\"f-requires-1-value\"[^>]*>(.|\n)*<option value=\"let_ride\" selected", html);
        Assert.Contains("id=\"f-requires-0-value\" name=\"requires[0].value\" type=\"text\" inputmode=\"numeric\" value=\"1\"", html, StringComparison.Ordinal);
        Assert.Contains("data-step=\"1\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"requires.text\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Switching_to_text_and_back_with_no_edit_gives_the_same_rows()
    {
        const string Source = "subject.trait.grieving >= 1 && subject.history.count('lost_mission', 2) > 0 && !target.fit";
        FormValues rows = Stored(Source);
        ContentFileSet files = GameFiles.Value;
        var form = new HoldsRequires(Source);

        ItemFormView text = FormFlow.Rows(form, files, null, rows.With(RowSet.ActionField, "mode:requires:text"))!;
        Assert.Equal("text", text.Values.Get("requires.mode"));
        Assert.Equal(Source, text.Values.Get("requires.text"));
        Assert.Empty(FormRows.Read(text.Values, Widget.Clauses(Requires)));
        Assert.Equal("f-requires-text", text.FocusId);

        ItemFormView back = FormFlow.Rows(form, files, null, text.Values.With(RowSet.ActionField, "mode:requires:rows"))!;
        Assert.Equal("rows", back.Values.Get("requires.mode"));
        Assert.Equal(FormRows.Read(rows, Widget.Clauses(Requires)), FormRows.Read(back.Values, Widget.Clauses(Requires)));
        Assert.False(back.Values.Has("requires.text"));
    }

    [Fact]
    public void Back_to_rows_from_text_that_is_not_rows_stays_in_text_and_says_why()
    {
        ContentFileSet files = GameFiles.Value;
        FormValues posted = Posted(("requires.mode", "text"), ("requires.text", "subject.morale + 1 > 30"), (RowSet.ActionField, "mode:requires:rows"));

        ItemFormView view = FormFlow.Rows(new HoldsRequires(null), files, null, posted)!;

        Assert.Equal("text", view.Values.Get("requires.mode"));
        Assert.Equal("subject.morale + 1 > 30", view.Values.Get("requires.text"));
        Assert.Equal($"Stays as text: {Shapes.UsesArithmetic}.", Widget.View(view.Values, Requires, view.Choices.Expressions).Check.Reason);
    }

    // Choosing what a clause reads posts the form with the select as the focus; the row's other parts are filled in to fit.
    [Fact]
    public void A_clause_whose_shape_changes_is_filled_in_to_fit_it()
    {
        ContentFileSet files = GameFiles.Value;
        FormValues added = FormFlow.Rows(new HoldsRequires(null), files, null, Posted(("requires.mode", "rows"), (RowSet.ActionField, "add:requires")))!.Values;
        Assert.Single(FormRows.Read(added, Widget.Clauses(Requires)));

        FormValues chosen = added.With("requires[0].kind", "ChronicleCount");
        ItemFormView view = FormFlow.Rows(new HoldsRequires(null), files, null, chosen, "f-requires-0-kind")!;
        Dictionary<string, string> row = FormRows.Read(view.Values, Widget.Clauses(Requires)).Single();

        Assert.Equal("ship", row["who"]);
        Assert.Equal("30", row["window"]);
        Assert.Equal(">=", row["op"]);
        Assert.Equal("1", row["value"]);
        Assert.Equal("", row["name"]);
        Assert.Equal("f-requires-0-kind", view.FocusId);

        // A trait with named levels, compared with "is", takes its first level's name.
        FormValues trait = view.Values.With("requires[0].kind", "Trait").With("requires[0].name", "sensors_quirk").With("requires[0].op", "==").With("requires[0].value", "");
        Dictionary<string, string> leveled = FormRows.Read(FormFlow.Rows(new HoldsRequires(null), files, null, trait, "f-requires-0-name")!.Values, Widget.Clauses(Requires)).Single();
        Assert.Equal("ship", leveled["who"]);
        Assert.Equal(Choices.Trait("sensors_quirk")!.Levels.OrderBy(l => l.Key).First().Value, leveled["value"]);
        Assert.Equal("", leveled["window"]);
    }

    [Fact]
    public async Task A_bad_text_is_shown_with_its_error_at_its_character_and_is_not_saved()
    {
        const string Bad = "subject.aboard && subject.trait.nosuch >= 1";
        ExpressionCheck check = Widget.Check(Bad, Requires, Choices);
        Choices.Book!.Check(Bad, Requires.Slots, out ExpressionError? error);

        Assert.Equal(error!.Message, check.Error);
        Assert.Equal(new TextSpot(1, error.Position + 1, Bad), check.Spot);
        Assert.Equal(Bad.IndexOf("nosuch", StringComparison.Ordinal), error.Position);

        using TestApp app = NewApp();
        string before = Everything(app);
        FormValues posted = Posted(("id", "STORYLET_99"), ("requires.mode", "text"), ("requires.text", Bad));
        (ItemFormView view, bool saved) = await FormFlow.Create(new HoldsRequires(null), app.Editor, posted, CancellationToken.None);

        Assert.False(saved);
        Assert.StartsWith(error.Message, view.Errors["requires.text"], StringComparison.Ordinal);
        Assert.Equal(before, Everything(app));
        string html = await Draw(app, view.Values, view);
        Assert.Contains($"{Bad}\n{new string(' ', error.Position)}^</pre>", System.Net.WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
        guard.Verify();
    }

    // The check under the text, as the author types, for the form as posted.
    [Fact]
    public void The_text_is_checked_as_typed()
    {
        ContentFileSet files = GameFiles.Value;
        var form = new HoldsRequires(null);

        Assert.True(FormFlow.CheckExpression(form, files, null, Posted(("requires.text", "subject.trait.grieving >= 1")), "requires")!.Value.Check.ReadsAsRows);
        Assert.NotNull(FormFlow.CheckExpression(form, files, null, Posted(("requires.text", "subject.trait.nosuch >= 1")), "requires")!.Value.Check.Error);
        Assert.Null(FormFlow.CheckExpression(form, files, null, Posted(("requires.text", "x")), "nothing"));
    }

    [Fact]
    public void A_blank_widget_is_no_expression()
    {
        var errors = new Dictionary<string, string>();
        Assert.Null(Widget.Bind(Stored(""), Requires, Choices, errors));
        Assert.Null(Widget.Bind(Posted(("requires.mode", "text"), ("requires.text", "  ")), Requires, Choices, errors));
        Assert.Empty(errors);
        Assert.Equal("rows", Stored("").Get("requires.mode"));
    }

    private static async Task<string> Draw(TestApp app, FormValues values, ItemFormView? view = null)
    {
        ItemFormView drawn = (view ?? new ItemFormView(ContentKind.Storylet, true, "STORYLET_99", null, values, new Dictionary<string, string>(), [], null, null, [], FormChoicesReader.Read(GameFiles.Value))) with { Values = values };
        RenderFragment widget = b =>
        {
            b.OpenComponent<Web.Components.Content.ExpressionField>(0);
            b.AddAttribute(1, nameof(Web.Components.Content.ExpressionField.Scope), Requires);
            b.CloseComponent();
        };
        return await app.Services.GetRequiredService<Web.Render>().Html<CascadingValue<ItemFormView>>(new Dictionary<string, object?> { ["Value"] = drawn, ["ChildContent"] = widget });
    }

    // A form that holds one expression widget, on requires; it saves nothing, since its kind's real form comes in Task 8.
    private sealed class HoldsRequires(string? stored) : KindForm
    {
        public override ContentKind Kind => ContentKind.Storylet;

        public override IReadOnlyList<ExpressionScope> Expressions(FormValues values)
        {
            return [Requires];
        }

        public override string? NameOf(ContentFileSet files, string id)
        {
            return null;
        }

        public override FormValues? Load(ContentFileSet files, string id, FormChoices choices)
        {
            return new FormValues(Widget.Load(Requires, stored, choices.Expressions));
        }

        public override FormValues Blank(FormChoices choices)
        {
            return new FormValues(Widget.Load(Requires, null, choices.Expressions));
        }

        public override Bound Bind(string id, FormValues values, FormChoices choices)
        {
            var errors = new Dictionary<string, string>();
            Widget.Bind(values, Requires, choices.Expressions, errors);
            return new Bound(errors.Count > 0 ? null : _ => throw new InvalidOperationException("This form saves nothing."), errors);
        }
    }
}
