using System.Globalization;
using Sim.Core;
using Sim.Core.Content;
using Sim.Expressions;

namespace Web.Forms;

// One expression widget on a form (1e part six, design 4.5): the prefix its fields are posted under, its label, and the slots it may
// read (for a cast slot, the slots before it and itself; for requires, every slot).
public sealed record ExpressionScope(string Prefix, string Label, IReadOnlyList<string> Slots);

// A defined trait as the widget offers it: where it may sit, and its levels by value.
public sealed record TraitChoice(string Id, string? On, IReadOnlyDictionary<int, string> Levels, int Cap)
{
    public bool OnShip => On is null || On == TraitPlaces.Ship;

    public bool OnCrew => On is null || On == TraitPlaces.Crew;

    public bool OnPair => On == TraitPlaces.Pair;
}

// What the widget picks from, read from the pack: the book the loader checks with (built the first time it is used), the traits,
// skills, and storylets. With no pack (it does not load), there is no book, so an expression is shown as text and not checked here.
public sealed class ExpressionChoices
{
    private readonly ContentPack? pack;
    private ExpressionBook? book;

    private ExpressionChoices(ContentPack? pack)
    {
        this.pack = pack;
        Traits = pack?.Traits.Select(t => new TraitChoice(t.Id, t.On, t.Levels, t.Cap)).ToList() ?? [];
        Skills = pack?.Skills.Select(s => s.Id).ToList() ?? [];
        Storylets = pack?.Storylets.Select(s => s.Id).ToList() ?? [];
    }

    public static ExpressionChoices None { get; } = new(null);

    public static ExpressionChoices Of(ContentPack? pack)
    {
        return pack is null ? None : new ExpressionChoices(pack);
    }

    // The loader's own book for this pack, as Casting builds it; null with no pack.
    public ExpressionBook? Book => pack is null ? null : book ??= new ExpressionBook(pack.Traits, pack.Skills.Select(s => s.Id), HistoryKinds.All, pack.HistoryCycles, pack.Storylets.Select(s => s.Id), pack.Vignettes.Select(v => v.Id));

    public IReadOnlyList<TraitChoice> Traits { get; }

    public IReadOnlyList<string> Skills { get; }

    public IReadOnlyList<string> Storylets { get; }

    public TraitChoice? Trait(string id)
    {
        return Traits.FirstOrDefault(t => t.Id == id);
    }
}

// One clause row as drawn: its posted parts, the row it makes when complete, and the choices for each of its selects.
public sealed record ClauseView(int Index, IReadOnlyDictionary<string, string> Parts, ShapeKind? Kind, string? Text, IReadOnlyList<Choice> Whos, IReadOnlyList<Choice> Names, IReadOnlyList<Choice>? Levels);

// A select's option: its value and what it is called.
public sealed record Choice(string Value, string Label);

// The text's check, under the box: why it stays text, or the loader's error and where it is in the box, or nothing.
public sealed record ExpressionCheck(string? Reason, string? Error, TextSpot? Spot, bool ReadsAsRows);

// The widget as drawn: rows or text, and each mode's parts.
public sealed record ExpressionView(ExpressionScope Scope, RowSet Set, bool IsText, IReadOnlyList<ClauseView> Clauses, string Text, ExpressionCheck Check, IReadOnlyList<string> Chips);

// The widget's fields, and the string made from them. A widget with prefix p posts p.mode (rows or text), then either p.text or its
// clauses as rows through FormRows (p[N].kind, p[N].who, and the rest of Parts). The YAML holds the string: the server writes it from
// the rows with Shapes.Write, and nothing stores the rows. A stored expression opens as rows when Shapes.Read reads it as rows.
public static class ExpressionField
{
    public const string RowsMode = "rows";

    public const string TextMode = "text";

    // The action the switch posts to the rows route: mode:{prefix}:rows or mode:{prefix}:text.
    public const string SwitchPrefix = "mode:";

    public static readonly IReadOnlyList<string> Parts = ["kind", "who", "name", "toward", "op", "value", "window", "unit", "not"];

    // The operators, as a row's select says them.
    public static readonly IReadOnlyList<Choice> Ops = [new("==", "is"), new("!=", "is not"), new(">=", "is at least"), new("<=", "is at most"), new(">", "is more than"), new("<", "is less than")];

    // What a clause reads, as the first select says it.
    public static readonly IReadOnlyList<Choice> Kinds =
    [
        new(nameof(ShapeKind.Presence), "Aboard or fit"),
        new(nameof(ShapeKind.Trait), "Trait"),
        new(nameof(ShapeKind.PairTrait), "Trait toward"),
        new(nameof(ShapeKind.Stat), "Morale or loyalty"),
        new(nameof(ShapeKind.Skill), "Skill"),
        new(nameof(ShapeKind.HistoryLast), "History last cycle"),
        new(nameof(ShapeKind.HistoryCount), "History count"),
        new(nameof(ShapeKind.ChronicleCount), "Chronicle count"),
        new(nameof(ShapeKind.StoryletSince), "Storylet since"),
        new(nameof(ShapeKind.TraitSince), "Trait since"),
    ];

    public static readonly IReadOnlyList<Choice> Units = [new("cycles", "cycles"), new("days", "days")];

    private const string Ship = ExpressionBook.Ship;

    public static string ModeField(string prefix)
    {
        return prefix + ".mode";
    }

    public static string TextField(string prefix)
    {
        return prefix + ".text";
    }

    public static RowSet Clauses(ExpressionScope scope)
    {
        return new RowSet(scope.Prefix, Parts, Qualified: true, Noun: "clause");
    }

    public static string SwitchAction(string prefix, string mode)
    {
        return $"{SwitchPrefix}{prefix}:{mode}";
    }

    // The fields of a stored expression: rows when it reads as rows, text otherwise. Blank is no clauses.
    public static IReadOnlyList<KeyValuePair<string, string>> Load(ExpressionScope scope, string? expression, ExpressionChoices choices)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return [new(ModeField(scope.Prefix), RowsMode)];
        }

        if (choices.Book is { } book && Shapes.Read(expression, scope.Slots, book).Rows is { } rows)
        {
            return RowFields(scope, rows).Prepend(new(ModeField(scope.Prefix), RowsMode)).ToList();
        }

        return [new(ModeField(scope.Prefix), TextMode), new(TextField(scope.Prefix), expression)];
    }

    public static bool IsText(FormValues values, ExpressionScope scope)
    {
        return values.Get(ModeField(scope.Prefix)) == TextMode;
    }

    // The string the posted fields make, or null for none (no clauses, or a blank text). A part missing, a value that is not a whole
    // number, or a clause or text the loader would refuse adds an error beside its field and gives null.
    public static string? Bind(FormValues values, ExpressionScope scope, ExpressionChoices choices, IDictionary<string, string> errors)
    {
        if (IsText(values, scope))
        {
            string text = values.Get(TextField(scope.Prefix)).Trim();
            if (text.Length == 0)
            {
                return null;
            }

            if (choices.Book is { } book && book.Check(text, scope.Slots, out ExpressionError? error) is null)
            {
                errors[TextField(scope.Prefix)] = $"{error!.Message} (character {error.Position + 1})";
                return null;
            }

            return text;
        }

        RowSet set = Clauses(scope);
        List<Dictionary<string, string>> posted = FormRows.Read(values, set);
        var rows = new List<Row>();
        bool failed = false;
        for (int i = 0; i < posted.Count; i++)
        {
            if (RowOf(posted[i], choices, out (string Part, string Message)? missing) is not { } row)
            {
                errors[set.Name(i, missing!.Value.Part)] = missing.Value.Message;
                failed = true;
                continue;
            }

            if (choices.Book is { } book && book.Check(Shapes.Write([row]), scope.Slots, out ExpressionError? error) is null)
            {
                errors[set.Name(i, "kind")] = error!.Message;
                failed = true;
                continue;
            }

            rows.Add(row);
        }

        return failed || rows.Count == 0 ? null : Shapes.Write(rows);
    }

    // Where the loader's refusal of the string the fields made belongs, for its character (counted from 0): beside the text, with the
    // character; or beside the first select of the clause that holds it, since the rows were written joined by " && ". A widget with no
    // clauses takes it beside itself.
    public static (string Field, string Message) Place(FormValues values, ExpressionScope scope, ExpressionChoices choices, int position, string message)
    {
        if (IsText(values, scope))
        {
            return (TextField(scope.Prefix), $"{message} (character {position + 1})");
        }

        RowSet set = Clauses(scope);
        List<Dictionary<string, string>> posted = FormRows.Read(values, set);
        int at = 0;
        for (int i = 0; i < posted.Count; i++)
        {
            if (RowOf(posted[i], choices, out _) is not { } row)
            {
                continue;
            }

            at += Shapes.Write([row]).Length + 4;
            if (position < at || i == posted.Count - 1)
            {
                return (set.Name(i, "kind"), message);
            }
        }

        return (scope.Prefix, message);
    }

    // The widget as drawn from the posted fields.
    public static ExpressionView View(FormValues values, ExpressionScope scope, ExpressionChoices choices)
    {
        RowSet set = Clauses(scope);
        string text = values.Get(TextField(scope.Prefix));
        List<ClauseView> clauses = FormRows.Read(values, set).Select((row, i) => Clause(i, row, scope, choices)).ToList();
        bool isText = IsText(values, scope);
        return new ExpressionView(scope, set, isText, clauses, text, isText ? Check(text, scope, choices) : new ExpressionCheck(null, null, null, false), Chips(scope, choices));
    }

    // The text's check as typed: the loader's error at its character, or why it stays text.
    public static ExpressionCheck Check(string text, ExpressionScope scope, ExpressionChoices choices)
    {
        if (text.Trim().Length == 0)
        {
            return new ExpressionCheck(null, null, null, false);
        }

        if (choices.Book is not { } book)
        {
            return new ExpressionCheck("The pack does not load, so this is not checked here. The save is judged by the loader.", null, null, false);
        }

        RowsRead read = Shapes.Read(text, scope.Slots, book);
        if (read.Rows is not null)
        {
            return new ExpressionCheck(null, null, null, true);
        }

        if (book.Check(text, scope.Slots, out ExpressionError? error) is null)
        {
            return new ExpressionCheck(null, error!.Message, ExpressionSpots.InBox(text, error.Position), false);
        }

        return new ExpressionCheck($"Stays as text: {read.Reason}.", null, null, false);
    }

    // The switch, when it is the posted action: to Text, the box holds the string the rows make (the rows as they are, when one is not
    // complete); to Rows, the text read as rows, or, when it does not read as rows, the text kept and the reason shown. Null for any
    // other action. Focus goes to the box, or to the first clause's first select.
    public static (FormValues Values, string? Focus)? Switch(FormValues values, IReadOnlyList<ExpressionScope> scopes, ExpressionChoices choices)
    {
        string action = values.Get(RowSet.ActionField);
        foreach (ExpressionScope scope in scopes)
        {
            RowSet set = Clauses(scope);
            FormValues rest = values.Without(n => n == RowSet.ActionField);
            if (action == SwitchAction(scope.Prefix, TextMode))
            {
                string written = Written(FormRows.Read(rest, set), choices);
                FormValues text = FormRows.Write(rest, set, []).With(ModeField(scope.Prefix), TextMode).With(TextField(scope.Prefix), written);
                return (text, FormValues.Slug(TextField(scope.Prefix)));
            }

            if (action == SwitchAction(scope.Prefix, RowsMode))
            {
                string typed = rest.Get(TextField(scope.Prefix)).Trim();
                IReadOnlyList<Row>? rows = typed.Length == 0 ? [] : choices.Book is { } book ? Shapes.Read(typed, scope.Slots, book).Rows : null;
                if (rows is null)
                {
                    // It stays text; the check under the box says why.
                    return (rest.With(ModeField(scope.Prefix), TextMode), FormValues.Slug(TextField(scope.Prefix)));
                }

                FormValues back = FormRows.Write(rest.Without(n => n == TextField(scope.Prefix)), set, rows.Select(r => Fields(r)).ToList()).With(ModeField(scope.Prefix), RowsMode);
                return (back, rows.Count > 0 ? FormValues.Slug(set.Name(0, "kind")) : set.AddId);
            }
        }

        return null;
    }

    // After a select that changes what a row shows (what it reads, whose, which trait, the operator) posts the form, that row's other
    // parts are made to fit: a who, name, or toward the new choice does not offer is cleared or set to the first that fits, a level's
    // name compared in order becomes its number, and a blank operator, window, or unit takes a starting value.
    public static FormValues Settle(FormValues values, IReadOnlyList<ExpressionScope> scopes, ExpressionChoices choices, string? focus)
    {
        if (focus is null)
        {
            return values;
        }

        foreach (ExpressionScope scope in scopes)
        {
            RowSet set = Clauses(scope);
            List<Dictionary<string, string>> rows = FormRows.Read(values, set);
            for (int i = 0; i < rows.Count; i++)
            {
                if (Parts.Any(p => FormValues.Slug(set.Name(i, p)) == focus))
                {
                    rows[i] = Settled(rows[i], scope, choices);
                    return FormRows.Write(values, set, rows);
                }
            }
        }

        return values;
    }

    private static Dictionary<string, string> Settled(Dictionary<string, string> row, ExpressionScope scope, ExpressionChoices choices)
    {
        var parts = new Dictionary<string, string>(row);
        if (KindOf(parts["kind"]) is not { } kind)
        {
            return parts;
        }

        List<Choice> whos = Whos(kind, scope, choices);
        if (kind == ShapeKind.StoryletSince)
        {
            parts["who"] = Ship;
        }
        else if (!whos.Any(w => w.Value == parts["who"]))
        {
            parts["who"] = kind == ShapeKind.ChronicleCount ? Ship : whos.FirstOrDefault()?.Value ?? "";
        }

        if (!Names(kind, parts["who"], choices).Any(n => n.Value == parts["name"]))
        {
            parts["name"] = "";
        }

        if (kind == ShapeKind.PairTrait)
        {
            if (!scope.Slots.Contains(parts["toward"]))
            {
                parts["toward"] = scope.Slots.FirstOrDefault(s => s != parts["who"]) ?? "";
            }
        }
        else
        {
            parts["toward"] = "";
        }

        if (Compares(kind))
        {
            if (!Ops.Any(o => o.Value == parts["op"]))
            {
                parts["op"] = ">=";
            }

            TraitChoice? trait = kind is ShapeKind.Trait or ShapeKind.PairTrait ? choices.Trait(parts["name"]) : null;
            string value = parts["value"].Trim();
            if (IsWhole(value))
            {
                parts["value"] = value;
            }
            else if (trait?.Levels.FirstOrDefault(l => l.Value == value) is { Value: not null } level)
            {
                // The loader takes a level's name only with == or !=.
                parts["value"] = parts["op"] is "==" or "!=" ? value : level.Key.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                parts["value"] = trait is { Levels.Count: > 0 } && parts["op"] is "==" or "!=" ? trait.Levels.OrderBy(l => l.Key).First().Value : "1";
            }
        }
        else
        {
            parts["op"] = "";
            parts["value"] = "";
        }

        parts["window"] = kind switch
        {
            ShapeKind.HistoryCount => IsWhole(parts["window"]) ? parts["window"] : "1",
            ShapeKind.ChronicleCount => IsWhole(parts["window"]) ? parts["window"] : "30",
            _ => "",
        };
        parts["unit"] = kind is ShapeKind.StoryletSince or ShapeKind.TraitSince ? parts["unit"] is "cycles" or "days" ? parts["unit"] : "cycles" : "";
        parts["not"] = kind is ShapeKind.Presence or ShapeKind.HistoryLast && parts["not"] == "true" ? "true" : "";
        return parts;
    }

    // The string a switch to Text shows: the rows written, or, where a row is not complete, the complete ones.
    private static string Written(IReadOnlyList<Dictionary<string, string>> posted, ExpressionChoices choices)
    {
        return Shapes.Write(posted.Select(p => RowOf(p, choices, out _)).OfType<Row>().ToList());
    }

    private static IEnumerable<KeyValuePair<string, string>> RowFields(ExpressionScope scope, IReadOnlyList<Row> rows)
    {
        RowSet set = Clauses(scope);
        for (int i = 0; i < rows.Count; i++)
        {
            foreach ((string part, string value) in Fields(rows[i]))
            {
                yield return new(set.Name(i, part), value);
            }
        }
    }

    private static Dictionary<string, string> Fields(Row row)
    {
        return new Dictionary<string, string>
        {
            ["kind"] = row.Kind.ToString(),
            ["who"] = row.Who,
            ["name"] = row.Name ?? "",
            ["toward"] = row.Toward ?? "",
            ["op"] = row.Op ?? "",
            ["value"] = row.Value ?? "",
            ["window"] = row.Window?.ToString(CultureInfo.InvariantCulture) ?? "",
            ["unit"] = row.Unit ?? "",
            ["not"] = row.Negated ? "true" : "",
        };
    }

    // The row the parts make, or null with the first part that is missing or wrong and what to do.
    private static Row? RowOf(IReadOnlyDictionary<string, string> parts, ExpressionChoices choices, out (string Part, string Message)? missing)
    {
        missing = null;
        string Part(string name) => parts.GetValueOrDefault(name, "").Trim();

        if (KindOf(Part("kind")) is not { } kind)
        {
            missing = ("kind", "Choose what this clause reads.");
            return null;
        }

        string who = kind == ShapeKind.StoryletSince ? Ship : Part("who");
        if (who.Length == 0)
        {
            missing = ("who", "Choose whose.");
            return null;
        }

        string name = Part("name");
        if (name.Length == 0)
        {
            missing = ("name", kind switch
            {
                ShapeKind.Presence => "Choose aboard or fit.",
                ShapeKind.Trait or ShapeKind.PairTrait or ShapeKind.TraitSince => "Choose a trait.",
                ShapeKind.Stat => "Choose morale or loyalty.",
                ShapeKind.Skill => "Choose a skill.",
                ShapeKind.StoryletSince => "Choose a storylet.",
                _ => "Choose a kind.",
            });
            return null;
        }

        string? toward = null;
        if (kind == ShapeKind.PairTrait)
        {
            toward = Part("toward");
            if (toward.Length == 0)
            {
                missing = ("toward", "Choose whom it points at.");
                return null;
            }
        }

        string? op = null;
        string? value = null;
        if (Compares(kind))
        {
            op = Part("op");
            if (!Ops.Any(o => o.Value == op))
            {
                missing = ("op", "Choose how it compares.");
                return null;
            }

            value = Part("value");
            TraitChoice? trait = kind is ShapeKind.Trait or ShapeKind.PairTrait ? choices.Trait(name) : null;
            if (!IsWhole(value))
            {
                if (trait?.Levels.FirstOrDefault(l => l.Value == value) is not { Value: not null } level)
                {
                    missing = ("value", "Enter a whole number.");
                    return null;
                }

                // A level's name stands only beside == or !=; compared in order, it is its number.
                value = op is "==" or "!=" ? value : level.Key.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                value = int.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            }
        }

        int? window = null;
        if (kind is ShapeKind.HistoryCount or ShapeKind.ChronicleCount)
        {
            if (!int.TryParse(Part("window"), NumberStyles.None, CultureInfo.InvariantCulture, out int w) || w < 1)
            {
                missing = ("window", kind == ShapeKind.HistoryCount ? "Enter the cycles, a whole number from 1." : "Enter the days, a whole number from 1.");
                return null;
            }

            window = w;
        }

        string? unit = kind is ShapeKind.StoryletSince or ShapeKind.TraitSince ? Part("unit") is "days" ? "days" : "cycles" : null;
        bool negated = kind is ShapeKind.Presence or ShapeKind.HistoryLast && Part("not") == "true";
        return new Row(kind, who, name, toward, op, value, window, unit, negated);
    }

    private static ClauseView Clause(int index, Dictionary<string, string> parts, ExpressionScope scope, ExpressionChoices choices)
    {
        if (KindOf(parts["kind"]) is not { } kind)
        {
            return new ClauseView(index, parts, null, null, [], [], null);
        }

        Row? row = RowOf(parts, choices, out _);
        List<Choice> whos = Keep(Whos(kind, scope, choices), parts["who"], Who);
        List<Choice> names = Keep(Names(kind, parts["who"], choices), parts["name"], n => n);
        IReadOnlyList<Choice>? levels = null;
        if (kind is ShapeKind.Trait or ShapeKind.PairTrait && choices.Trait(parts["name"]) is { Levels.Count: > 0 } trait)
        {
            levels = parts["op"] is "==" or "!="
                ? Keep([new("0", "0, not held"), .. trait.Levels.OrderBy(l => l.Key).Select(l => new Choice(l.Value, $"{l.Value} ({l.Key})"))], parts["value"], v => v)
                : Keep(Enumerable.Range(0, trait.Cap + 1).Select(n => new Choice(n.ToString(CultureInfo.InvariantCulture), trait.Levels.TryGetValue(n, out string? level) ? $"{n} ({level})" : n == 0 ? "0, not held" : n.ToString(CultureInfo.InvariantCulture))).ToList(), parts["value"], v => v);
        }

        return new ClauseView(index, parts, kind, row is null ? null : Shapes.Write([row]), whos, names, levels);
    }

    // A choice list with the posted value added at its end when the list lacks it, so a value the pack no longer offers is shown as it is
    // (and refused by the check) rather than silently changed.
    private static List<Choice> Keep(IReadOnlyList<Choice> choices, string value, Func<string, string> label)
    {
        return value.Length == 0 || choices.Any(c => c.Value == value) ? choices.ToList() : [.. choices, new Choice(value, label(value))];
    }

    // A shape by its name as posted; null for anything else, a number included.
    private static ShapeKind? KindOf(string posted)
    {
        return Kinds.Any(k => k.Value == posted) ? Enum.Parse<ShapeKind>(posted) : null;
    }

    private static string Who(string who)
    {
        return who == Ship ? "the ship" : who;
    }

    private static bool Compares(ShapeKind kind)
    {
        return kind is not (ShapeKind.Presence or ShapeKind.HistoryLast);
    }

    private static bool IsWhole(string value)
    {
        return int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
    }

    // Whose: the slots in scope, and the ship where the shape reads it.
    private static List<Choice> Whos(ShapeKind kind, ExpressionScope scope, ExpressionChoices choices)
    {
        List<Choice> slots = scope.Slots.Select(s => new Choice(s, s)).ToList();
        var ship = new Choice(Ship, "the ship");
        return kind switch
        {
            ShapeKind.Trait or ShapeKind.TraitSince => choices.Traits.Any(t => t.OnShip) ? [.. slots, ship] : slots,
            ShapeKind.ChronicleCount => [ship, .. slots],
            ShapeKind.StoryletSince => [ship],
            _ => slots,
        };
    }

    // The names a shape reads, for this who.
    private static List<Choice> Names(ShapeKind kind, string who, ExpressionChoices choices)
    {
        IEnumerable<string> names = kind switch
        {
            ShapeKind.Presence => ["aboard", "fit"],
            ShapeKind.Trait or ShapeKind.TraitSince => choices.Traits.Where(t => who == Ship ? t.OnShip : t.OnCrew).Select(t => t.Id),
            ShapeKind.PairTrait => choices.Traits.Where(t => t.OnPair).Select(t => t.Id),
            ShapeKind.Stat => ["morale", "loyalty"],
            ShapeKind.Skill => choices.Skills,
            ShapeKind.HistoryLast or ShapeKind.HistoryCount => HistoryKinds.All,
            ShapeKind.ChronicleCount => ChronicleKinds.All,
            ShapeKind.StoryletSince => choices.Storylets,
            _ => [],
        };
        return names.Select(n => new Choice(n, n)).ToList();
    }

    // What the text's chips insert: the slots and the ship, each trait, each level's name, the history and chronicle kinds, and each
    // storylet, as the language spells them.
    private static List<string> Chips(ExpressionScope scope, ExpressionChoices choices)
    {
        return
        [
            .. scope.Slots,
            Ship,
            .. choices.Traits.Select(t => $"trait.{t.Id}"),
            .. choices.Traits.SelectMany(t => t.Levels.OrderBy(l => l.Key).Select(l => $"'{l.Value}'")).Distinct(),
            .. HistoryKinds.All.Concat(ChronicleKinds.All).Distinct().Select(k => $"'{k}'"),
            .. choices.Storylets.Select(s => $"storylet.{s}"),
        ];
    }
}
