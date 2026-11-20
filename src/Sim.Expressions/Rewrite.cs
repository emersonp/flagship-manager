using System.Globalization;
using System.Text;
using Sim.Core;
using Sim.Core.Content;

namespace Sim.Expressions;

// The rewrite in front of the parser (design section 9, Task 1): it reads the source as tokens, refuses anything outside the
// language with its position, and turns what DynamicExpresso cannot check into what it can:
//   x.trait.t            -> x.trait["t"]           (t checked against the book, and where it may sit)
//   x.trait.t.toward(y)  -> x.trait.toward("t", y) (only a trait on a pair, and y a slot)
//   x.skills.S           -> x.skills["S"]          (S a skill id)
//   x.history.count('k', n), x.history.last('k')   (k a history kind and n a constant from 1 to history.cycles)
//   x.trait.t == 'level' -> x.trait["t"] == 2      (the level resolved to its number)
//   x.trait.t.daysSince  -> x.trait.daysSince("t") (and cyclesSince; ship.trait.t too; not on a pair trait yet)
//   storylet.s.daysSince -> storylet.daysSince("s") (and cyclesSince; s a storylet, never a vignette)
//   chronicle.count('k', n), x.chronicle.count('k', n) (k a chronicle kind and n a constant from 1, in days)
// and single quotes to double. A map from each character it writes back to the source keeps the parser's positions true.
internal sealed class Rewrite
{
    private const string Ship = ExpressionBook.Ship;
    private const string Cycle = ExpressionBook.Cycle;
    private const string Storylet = ExpressionBook.Storylet;
    private const string Chronicle = ExpressionBook.Chronicle;

    private static readonly Dictionary<string, string> Waiting = new(StringComparer.Ordinal)
    {
        ["rel"] = "rel() waits for relationships (Phase 4).",
        ["grade"] = "grade waits for 1h.",
        ["dept"] = "dept waits for departments (Phase 5).",
    };

    private static readonly Dictionary<string, string> Spelled = new(StringComparer.Ordinal)
    {
        ["and"] = "and is written &&.",
        ["or"] = "or is written ||.",
        ["not"] = "not is written !.",
    };

    private static readonly string[] Pairs = ["&&", "||", "==", "!=", "<=", ">="];

    private const string Singles = "(),.!<>+-*";

    private readonly string source;
    private readonly IReadOnlyList<string> slots;
    private readonly ExpressionBook book;
    private readonly List<Token> tokens;
    private readonly StringBuilder text = new();
    private readonly List<int> map = [];
    private readonly List<TraitRead> traits = [];
    private readonly List<LastRead> lasts = [];

    // The trait whose read ends just before this token index, for a level compared with it.
    private (TraitDef Def, int End)? lastTrait;

    private Rewrite(string source, IReadOnlyList<string> slots, ExpressionBook book)
    {
        this.source = source;
        this.slots = slots;
        this.book = book;
        tokens = Tokenize(source);
    }

    public string Text => text.ToString();

    public IReadOnlyList<TraitRead> Traits => traits;

    public IReadOnlyList<LastRead> Lasts => lasts;

    // Where a position in the rewritten text came from in the source; past its end is the source's end.
    public int SourcePosition(int position)
    {
        return position >= 0 && position < map.Count ? map[position] : source.Length;
    }

    public static Rewrite Run(string source, IReadOnlyList<string> slots, ExpressionBook book)
    {
        var rewrite = new Rewrite(source, slots, book);
        rewrite.Write();
        return rewrite;
    }

    private void Write()
    {
        if (tokens.Count == 0)
        {
            throw new RewriteException("An expression is empty.", 0);
        }

        int i = 0;
        while (i < tokens.Count)
        {
            Token token = tokens[i];
            switch (token.Kind)
            {
                case TokenKind.Name:
                    i = Primary(i);
                    break;
                case TokenKind.Quoted:
                    i = QuotedFirst(i);
                    break;
                case TokenKind.Symbol when token.Text == ".":
                    throw new RewriteException("'.' reads from a slot, ship, or cycle, as subject.morale.", token.Position);
                case TokenKind.Symbol when token.Text is "==" or "!=" && lastTrait?.End == i && At(i + 1) is { Kind: TokenKind.Quoted } level:
                    Put(token.Text, token.Position);
                    Put(Level(lastTrait.Value.Def, level), level.Position);
                    i += 2;
                    break;
                default:
                    Put(token.Text, token.Position);
                    i++;
                    break;
            }
        }
    }

    // A name where a value starts: true or false, a slot, ship, or cycle, and what is read from it.
    private int Primary(int i)
    {
        Token name = tokens[i];
        if (name.Text is "true" or "false")
        {
            Put(name.Text, name.Position);
            return i + 1;
        }

        if (Waiting.TryGetValue(name.Text, out string? waiting))
        {
            throw new RewriteException(waiting, name.Position);
        }

        if (Spelled.TryGetValue(name.Text, out string? spelled))
        {
            throw new RewriteException(spelled, name.Position);
        }

        int end = name.Text switch
        {
            _ when slots.Contains(name.Text) => Crew(i),
            Ship => ShipRead(i),
            Cycle => CycleRead(i),
            Storylet => StoryletRead(i),
            Chronicle => ChronicleRead(i, name, null),
            _ => throw NotReadable(name),
        };

        if (At(end) is { Text: "." } dot)
        {
            throw new RewriteException($"Nothing is read from {source[name.Position..(tokens[end - 1].Position + tokens[end - 1].Length)]}.", dot.Position);
        }

        return end;
    }

    private int Crew(int i)
    {
        Token slot = tokens[i];
        if (Member(i + 1) is not { } member)
        {
            Put(slot.Text, slot.Position);
            return i + 1;
        }

        switch (member.Text)
        {
            case "morale" or "loyalty" or "fit" or "aboard":
                Put(slot.Text, slot.Position);
                Put("." + member.Text, member.Position, glued: true);
                return i + 3;
            case "skills":
                return Skill(i, slot, member);
            case "trait":
                return CrewTrait(i, slot, member);
            case "history":
                return History(i, slot, member);
            case "chronicle":
                return ChronicleRead(i + 2, member, slot);
            default:
                if (Waiting.TryGetValue(member.Text, out string? waiting))
                {
                    throw new RewriteException(waiting, member.Position);
                }

                throw new RewriteException($"A crew member has nothing named {member.Text}; it reads morale, loyalty, skills, fit, aboard, trait, history, and chronicle.", member.Position);
        }
    }

    private int Skill(int i, Token slot, Token member)
    {
        Token id = Member(i + 3) ?? throw new RewriteException($"A skill is read as {slot.Text}.skills.SKILL_ID.", member.Position);
        if (!book.Skills.Contains(id.Text))
        {
            throw new RewriteException($"No skill named {id.Text}.", id.Position);
        }

        Put(slot.Text, slot.Position);
        Put(".skills", member.Position, glued: true);
        Put($"[\"{id.Text}\"]", id.Position, glued: true);
        return i + 5;
    }

    private int CrewTrait(int i, Token slot, Token member)
    {
        Token id = Member(i + 3) ?? throw new RewriteException($"A trait is read as {slot.Text}.trait.some_trait.", member.Position);
        TraitDef def = Def(id);
        if (def.On == TraitPlaces.Ship)
        {
            throw new RewriteException($"{id.Text} is on the ship; read it as ship.trait.{id.Text}.", id.Position);
        }

        if (Member(i + 5) is { Text: "cyclesSince" or "daysSince" } since)
        {
            if (def.OnPair)
            {
                throw PairSince(id, since);
            }

            Put(slot.Text, slot.Position);
            Put(".trait", member.Position, glued: true);
            Put("." + since.Text, since.Position, glued: true);
            Put($"(\"{id.Text}\")", id.Position, glued: true);
            return i + 7;
        }

        if (Member(i + 5) is { Text: "toward" } toward)
        {
            if (!def.OnPair)
            {
                throw new RewriteException($"{id.Text} is not on a pair; toward reads only a trait held toward someone.", toward.Position);
            }

            Token target = Expect(i + 7, "(", toward) is var open && At(i + 8) is { Kind: TokenKind.Name } name && At(i + 9) is { Text: ")" }
                ? name
                : throw new RewriteException("toward takes a slot, as toward(target).", (At(i + 8) ?? open).Position);
            if (!slots.Contains(target.Text))
            {
                throw target.Text is Ship or Cycle or Storylet or Chronicle || Spelled.ContainsKey(target.Text) || target.Text is "true" or "false"
                    ? new RewriteException("toward takes a slot, as toward(target).", target.Position)
                    : NotReadable(target);
            }

            Put(slot.Text, slot.Position);
            Put(".trait", member.Position, glued: true);
            Put($".toward(\"{id.Text}\", ", id.Position, glued: true);
            Put(target.Text + ")", target.Position, glued: true);
            if (Member(i + 10) is { Text: "cyclesSince" or "daysSince" } after)
            {
                throw PairSince(id, after);
            }

            Read(new TraitRead(id.Text, slot.Text, target.Text));
            lastTrait = (def, i + 10);
            return i + 10;
        }

        if (def.OnPair)
        {
            throw new RewriteException($"{id.Text} is held toward someone; read it as {slot.Text}.trait.{id.Text}.toward(target).", id.Position);
        }

        Put(slot.Text, slot.Position);
        Put(".trait", member.Position, glued: true);
        Put($"[\"{id.Text}\"]", id.Position, glued: true);
        Read(new TraitRead(id.Text, slot.Text, null));
        lastTrait = (def, i + 5);
        return i + 5;
    }

    private int History(int i, Token slot, Token member)
    {
        Token method = Member(i + 3) ?? throw new RewriteException($"History is read as {slot.Text}.history.count('hurt', 5) or {slot.Text}.history.last('hurt').", member.Position);
        switch (method.Text)
        {
            case "count":
            {
                const string usage = "history.count takes a kind in quotes and a number of cycles, as history.count('hurt', 5).";
                Expect(i + 5, "(", method, usage);
                Token kind = Kind(i + 6, usage);
                Expect(i + 7, ",", kind, usage);
                Token cycles = At(i + 8) is { Kind: TokenKind.Number } number ? number : throw Usage(i + 8, kind, usage);
                Expect(i + 9, ")", cycles, usage);
                if (!int.TryParse(cycles.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1 || n > book.HistoryCycles)
                {
                    throw new RewriteException($"history.count reads 1 to {book.HistoryCycles} cycles.", cycles.Position);
                }

                Put(slot.Text, slot.Position);
                Put(".history.count", member.Position, glued: true);
                Put($"(\"{kind.Text}\", ", kind.Position, glued: true);
                Put(n.ToString(CultureInfo.InvariantCulture) + ")", cycles.Position, glued: true);
                return i + 10;
            }

            case "last":
            {
                const string usage = "history.last takes a kind in quotes, as history.last('hurt').";
                Expect(i + 5, "(", method, usage);
                Token kind = Kind(i + 6, usage);
                Expect(i + 7, ")", kind, usage);
                Put(slot.Text, slot.Position);
                Put(".history.last", member.Position, glued: true);
                Put($"(\"{kind.Text}\")", kind.Position, glued: true);
                if ((i == 0 || tokens[i - 1].Text != "!") && !lasts.Contains(new LastRead(kind.Text, slot.Text)))
                {
                    lasts.Add(new LastRead(kind.Text, slot.Text));
                }

                return i + 8;
            }

            default:
                throw new RewriteException("History reads count and last.", method.Position);
        }
    }

    private Token Kind(int at, string usage)
    {
        Token kind = At(at) is { Kind: TokenKind.Quoted } quoted ? quoted : throw Usage(at, tokens[at - 1], usage);
        if (!book.HistoryKinds.Contains(kind.Text))
        {
            throw new RewriteException($"No history kind named {kind.Text}; the kinds are {string.Join(", ", book.HistoryKinds)}.", kind.Position);
        }

        return kind;
    }

    private int ShipRead(int i)
    {
        Token ship = tokens[i];
        if (Member(i + 1) is not { } member)
        {
            Put(ship.Text, ship.Position);
            return i + 1;
        }

        if (member.Text != "trait")
        {
            throw new RewriteException("ship reads only trait, as ship.trait.some_trait.", member.Position);
        }

        Token id = Member(i + 3) ?? throw new RewriteException("A trait is read as ship.trait.some_trait.", member.Position);
        TraitDef def = Def(id);
        if (def.On is TraitPlaces.Crew or TraitPlaces.Pair)
        {
            throw new RewriteException($"{id.Text} is on {(def.OnPair ? "a pair" : "crew")}, not the ship.", id.Position);
        }

        Put(ship.Text, ship.Position);
        Put(".trait", member.Position, glued: true);
        if (Member(i + 5) is { Text: "cyclesSince" or "daysSince" } since)
        {
            Put("." + since.Text, since.Position, glued: true);
            Put($"(\"{id.Text}\")", id.Position, glued: true);
            return i + 7;
        }

        Put($"[\"{id.Text}\"]", id.Position, glued: true);
        Read(new TraitRead(id.Text, Ship, null));
        lastTrait = (def, i + 5);
        return i + 5;
    }

    private int CycleRead(int i)
    {
        Token cycle = tokens[i];
        if (Member(i + 1) is not { } member)
        {
            Put(cycle.Text, cycle.Position);
            return i + 1;
        }

        if (member.Text != "number")
        {
            throw new RewriteException("cycle reads only number, as cycle.number.", member.Position);
        }

        Put(cycle.Text, cycle.Position);
        Put(".number", member.Position, glued: true);
        return i + 3;
    }

    // storylet.s.cyclesSince and storylet.s.daysSince (slice 1e-calendar, design 4.6). Not a trait read: they turn on time, so the
    // rule against hidden memory reads them as unknown.
    private int StoryletRead(int i)
    {
        Token root = tokens[i];
        if (Member(i + 1) is not { } id)
        {
            Put(root.Text, root.Position);
            return i + 1;
        }

        if (!book.Storylets.Contains(id.Text))
        {
            throw new RewriteException(book.Vignettes.Contains(id.Text) ? "storylet reads storylets; vignettes are not counted." : $"No storylet named {id.Text}.", id.Position);
        }

        const string usage = "A storylet is read as storylet.some_storylet.cyclesSince or storylet.some_storylet.daysSince.";
        Token reading = Member(i + 3) ?? throw new RewriteException(usage, id.Position + id.Length);
        if (reading.Text is not ("cyclesSince" or "daysSince"))
        {
            throw new RewriteException(usage, reading.Position);
        }

        Put(root.Text, root.Position);
        Put("." + reading.Text, reading.Position, glued: true);
        Put($"(\"{id.Text}\")", id.Position, glued: true);
        return i + 5;
    }

    // chronicle.count('k', n) at the root, or on a slot (design 4.6), with `at` the index of the word chronicle. Not a trait read: it
    // turns on what has happened, so the rule against hidden memory reads it as unknown.
    private int ChronicleRead(int at, Token chronicle, Token? slot)
    {
        string usage = $"The chronicle is read as {(slot is null ? "" : slot.Value.Text + ".")}chronicle.count('death', 120): a kind in quotes and a number of days.";
        Token method = Member(at + 1) ?? throw new RewriteException(usage, chronicle.Position + chronicle.Length);
        if (method.Text != "count")
        {
            throw new RewriteException(usage, method.Position);
        }

        Expect(at + 3, "(", method, usage);
        Token kind = At(at + 4) is { Kind: TokenKind.Quoted } quoted ? quoted : throw Usage(at + 4, tokens[at + 3], usage);
        if (!ChronicleKinds.All.Contains(kind.Text))
        {
            throw new RewriteException($"No chronicle kind named {kind.Text}; the kinds are {string.Join(", ", ChronicleKinds.All)}.", kind.Position);
        }

        Expect(at + 5, ",", kind, usage);
        Token days = At(at + 6) is { Kind: TokenKind.Number } number ? number : throw Usage(at + 6, kind, usage);
        Expect(at + 7, ")", days, usage);
        if (!int.TryParse(days.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1)
        {
            throw new RewriteException($"chronicle.count reads a whole number of days from 1, not {days.Text}.", days.Position);
        }

        if (slot is { } crew)
        {
            Put(crew.Text, crew.Position);
            Put(".chronicle", chronicle.Position, glued: true);
        }
        else
        {
            Put(chronicle.Text, chronicle.Position);
        }

        Put(".count", method.Position, glued: true);
        Put($"(\"{kind.Text}\", ", kind.Position, glued: true);
        Put(n.ToString(CultureInfo.InvariantCulture) + ")", days.Position, glued: true);
        return at + 8;
    }

    // How long since a pair trait changed waits: the slice reads it only on one crew member or the ship.
    private static RewriteException PairSince(Token id, Token since)
    {
        return new RewriteException($"{id.Text} is held toward someone; {since.Text} is read only on a trait on one crew member or the ship, for now.", since.Position);
    }

    // A quoted name before the trait it is compared with: 'withdrawn' == subject.trait.came_back.
    private int QuotedFirst(int i)
    {
        Token quoted = tokens[i];
        if (At(i + 1) is { Text: "==" or "!=" } && At(i + 2) is { Kind: TokenKind.Name } holder && (slots.Contains(holder.Text) || holder.Text == Ship)
            && At(i + 3) is { Text: "." } && At(i + 4) is { Text: "trait" } && At(i + 5) is { Text: "." } && At(i + 6) is { Kind: TokenKind.Name } id)
        {
            Put(Level(Def(id), quoted), quoted.Position);
            return i + 1;
        }

        throw new RewriteException("A quoted name is a level of the trait it is compared with, or a history or chronicle kind.", quoted.Position);
    }

    private string Level(TraitDef def, Token level)
    {
        if (!def.HasLevels)
        {
            throw new RewriteException($"{def.Id} has no named levels; compare it with a number.", level.Position);
        }

        foreach (KeyValuePair<int, string> named in def.Levels)
        {
            if (named.Value == level.Text)
            {
                return named.Key.ToString(CultureInfo.InvariantCulture);
            }
        }

        throw new RewriteException($"{def.Id} has no level named {level.Text}; its levels are {string.Join(", ", def.Levels.OrderBy(l => l.Key).Select(l => l.Value))}.", level.Position);
    }

    // Decision 8.4: an expression reads only a defined trait.
    private TraitDef Def(Token id)
    {
        return book.Trait(id.Text) ?? throw new RewriteException($"No trait named {id.Text}.", id.Position);
    }

    private void Read(TraitRead read)
    {
        if (!traits.Contains(read))
        {
            traits.Add(read);
        }
    }

    private RewriteException NotReadable(Token name)
    {
        List<string> readable = [.. slots, Ship, Cycle, Storylet, Chronicle];
        string list = string.Join(", ", readable.Take(readable.Count - 1)) + ", and " + Chronicle;
        return new RewriteException($"{name.Text} is not a slot this expression can read; it reads {list}.", name.Position);
    }

    // The name after a '.' at this index, or null when no '.' is there.
    private Token? Member(int dot)
    {
        if (At(dot) is not { Text: "." } found)
        {
            return null;
        }

        return At(dot + 1) is { Kind: TokenKind.Name } name ? name : throw new RewriteException("A name follows '.'.", (At(dot + 1) ?? found).Position);
    }

    private Token Expect(int at, string symbol, Token after, string? usage = null)
    {
        if (At(at) is { Kind: TokenKind.Symbol } token && token.Text == symbol)
        {
            return token;
        }

        throw usage is null ? new RewriteException($"'{symbol}' was expected.", (At(at) ?? after).Position) : Usage(at, after, usage);
    }

    private RewriteException Usage(int at, Token after, string usage)
    {
        return new RewriteException(usage, At(at)?.Position ?? after.Position + after.Length);
    }

    private Token? At(int index)
    {
        return index < tokens.Count ? tokens[index] : null;
    }

    // Writes text taken from this source position; glued: no space before it.
    private void Put(string piece, int position, bool glued = false)
    {
        if (!glued && text.Length > 0)
        {
            text.Append(' ');
            map.Add(position);
        }

        text.Append(piece);
        for (int k = 0; k < piece.Length; k++)
        {
            map.Add(position);
        }
    }

    private static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < source.Length)
        {
            char c = source[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (char.IsAsciiLetter(c) || c == '_')
            {
                int start = i;
                while (i < source.Length && (char.IsAsciiLetterOrDigit(source[i]) || source[i] == '_'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Name, source[start..i], start, i - start));
            }
            else if (char.IsAsciiDigit(c))
            {
                int start = i;
                while (i < source.Length && char.IsAsciiDigit(source[i]))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Number, source[start..i], start, i - start));
            }
            else if (c is '\'' or '"')
            {
                int close = source.IndexOf(c, i + 1);
                if (close < 0)
                {
                    throw new RewriteException("A quoted name is never closed.", i);
                }

                string name = source[(i + 1)..close];
                if (name.Length == 0 || !name.All(n => char.IsAsciiLetterOrDigit(n) || n == '_'))
                {
                    throw new RewriteException("A quoted name holds only letters, digits, and _.", i);
                }

                tokens.Add(new Token(TokenKind.Quoted, name, i, close + 1 - i));
                i = close + 1;
            }
            else if (i + 1 < source.Length && Pairs.Contains(source.Substring(i, 2)))
            {
                tokens.Add(new Token(TokenKind.Symbol, source.Substring(i, 2), i, 2));
                i += 2;
            }
            else if (Singles.Contains(c, StringComparison.Ordinal))
            {
                tokens.Add(new Token(TokenKind.Symbol, c.ToString(), i, 1));
                i++;
            }
            else
            {
                throw new RewriteException($"'{c}' is not part of the expression language.", i);
            }
        }

        return tokens;
    }

    // The text of each clause joined by && at the top of the expression, as the parsed tree joins them (ExpressionBook.Split): an
    // expression wrapped whole in parentheses is read inside them, and one with || at its top is one clause, since && binds tighter.
    // The source has been checked, so it tokenizes.
    internal static List<string> TopLevelClauses(string source)
    {
        List<Token> tokens = Tokenize(source);
        var clauses = new List<string>();

        // The matching ) of the ( at open, or -1.
        int Close(int open, int end)
        {
            int depth = 0;
            for (int i = open; i < end; i++)
            {
                depth += tokens[i].Text switch { "(" => 1, ")" => -1, _ => 0 };
                if (depth == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        void Split(int from, int to)
        {
            while (to - from > 1 && tokens[from].Text == "(" && Close(from, to) == to - 1)
            {
                from++;
                to--;
            }

            var ands = new List<int>();
            bool or = false;
            int depth = 0;
            for (int i = from; i < to; i++)
            {
                depth += tokens[i].Text switch { "(" => 1, ")" => -1, _ => 0 };
                if (depth == 0 && tokens[i].Text == "&&")
                {
                    ands.Add(i);
                }

                or |= depth == 0 && tokens[i].Text == "||";
            }

            if (or || ands.Count == 0)
            {
                clauses.Add(source[tokens[from].Position..(tokens[to - 1].Position + tokens[to - 1].Length)]);
                return;
            }

            int start = from;
            foreach (int and in ands.Append(to))
            {
                Split(start, and);
                start = and + 1;
            }
        }

        Split(0, tokens.Count);
        return clauses;
    }

    private enum TokenKind
    {
        Name,
        Number,
        Quoted,
        Symbol,
    }

    private readonly record struct Token(TokenKind Kind, string Text, int Position, int Length);
}

internal sealed class RewriteException(string message, int position) : Exception(message)
{
    public int Position { get; } = position;
}
