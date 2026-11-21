using Sim.Content;
using Sim.Core.Content;
using Sim.Core.Narrative;
using Sim.Storylets;

namespace Web.Forms;

// One finding of the lint on the storylets list: the item, whether it is a fault or advice, and what is wrong, in a sentence that
// follows the item's name.
public sealed record LintFinding(ContentItemRef Item, bool Fault, string Message);

// The lint above the storylets list (1e part six, design 4.10), as the Lines page has its own. From the words as written: a slot
// the words name that the storylet does not cast, #handler# outside a check's outcome (both faults), and a slot cast that the words
// never name (advice). From the pack, when it loads: a storylet that casts on no test ship, a trait it requires that nothing sets
// (TextFileFlow's reckoning), and an option offered to no cast tried on any test ship (faults). The words are read from the files,
// not the pack, so a word fault is listed while the loader refuses it, which is when it matters.
public sealed record StoryletLint(IReadOnlyList<LintFinding> Findings, bool Loaded, int Ships)
{
    // How many casts an option is tried with on each test ship before the lint says no cast is offered it.
    public const int Samples = 12;

    public bool Clean => Findings.Count == 0;

    public static StoryletLint Of(ContentFileSet files)
    {
        List<ContentItemRef> items = files.Index().Where(i => i.Kind == ContentKind.Storylet).ToList();
        Dictionary<string, SymbolModel> symbols = files.Index().Where(i => i.Kind == ContentKind.Symbol)
            .Select(i => ContentEdits.Get<SymbolModel>(files, i.Kind, i.Id)).OfType<SymbolModel>()
            .GroupBy(s => s.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        ContentPack? pack = files.Validate().Pack;
        IReadOnlyList<TestShipDef> ships = pack is null ? [] : files.TestShips().Ships;
        IReadOnlyList<UnsetTrait> unset = pack is null ? [] : TextFileFlow.Unset(pack);

        var findings = new List<LintFinding>();
        foreach (ContentItemRef item in items)
        {
            if (ContentEdits.Get<StoryletModel>(files, item.Kind, item.Id) is not { } model)
            {
                continue;
            }

            var found = new List<LintFinding>();
            void Add(bool fault, string message)
            {
                found.Add(new LintFinding(item, fault, message));
            }

            Words(model, symbols, Add);
            if (pack?.Storylets.FirstOrDefault(s => s.Id == item.Id) is { } storylet)
            {
                foreach (UnsetTrait trait in unset.Where(u => u.Item == item.Id).DistinctBy(u => (u.Trait, u.OnShip)))
                {
                    Add(true, $"requires {trait.Trait} on {(trait.OnShip ? "the ship" : "a crew member")}, and nothing in the pack sets it there.");
                }

                if (ships.Count > 0)
                {
                    Casts(pack, storylet, ships, Add);
                }
            }

            // Faults first, then advice, each in the order found.
            findings.AddRange(found.Where(f => f.Fault).Concat(found.Where(f => !f.Fault)));
        }

        return new StoryletLint(findings, pack is not null, ships.Count);
    }

    // Each test ship as the preview casts on it: sample 0 says whether it casts at all, and each option is tried with up to Samples
    // casts on each ship, stopping once every option has been offered.
    private static void Casts(ContentPack pack, StoryletDef storylet, IReadOnlyList<TestShipDef> ships, Action<bool, string> add)
    {
        var offered = new HashSet<string>(StringComparer.Ordinal);
        bool casts = false;
        foreach (TestShipDef ship in ships)
        {
            var source = new TestShipSource(pack, ship);
            for (int sample = 0; sample < Samples && offered.Count < storylet.Options.Count; sample++)
            {
                PreviewCast cast = Preview.Storylet(pack, storylet.Id, source, source.Roster, sample, false);
                if (cast.Cast is null)
                {
                    break;
                }

                casts = true;
                offered.UnionWith(cast.OptionOffered.Where(o => o.Value).Select(o => o.Key));
            }
        }

        if (!casts)
        {
            add(true, ships.Count == 1 ? "cannot be cast on the test ship." : $"cannot be cast on any of the {ships.Count} test ships.");
            return;
        }

        foreach (StoryletOption option in storylet.Options.Where(o => !offered.Contains(o.Id)))
        {
            add(true, $"offers {option.Id} to no cast: its requires held for none of up to {Samples} casts on each test ship.");
        }
    }

    // Who the words name: each slot as a speaker or a #slot# token, read through the symbols they use. A storylet's script and an
    // option's words are the storylet beats', which have the cast; a check's outcomes, and a check option's chronicle text, have the
    // handler too.
    private static void Words(StoryletModel model, IReadOnlyDictionary<string, SymbolModel> symbols, Action<bool, string> add)
    {
        var slots = model.Cast.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var named = new HashSet<string>(StringComparer.Ordinal);
        var texts = new List<(string Where, string? Text, string Beat, bool Handler)> { ("its script", model.Script, "storylet", false) };
        foreach (StoryletOptionModel option in model.Options)
        {
            texts.Add(($"{option.Id}'s words", option.Text, "storyletAnswer", false));
            if (option.Check is { } check)
            {
                texts.Add(($"{option.Id}'s success", check.Success?.Text, "storyletAnswer", true));
                texts.Add(($"{option.Id}'s failure", check.Failure?.Text, "storyletAnswer", true));
            }

            texts.Add(($"{option.Id}'s chronicle text", option.Chronicle?.Text, NarrativeFacts.Chronicle.Storylet, option.Check is not null));
        }

        foreach ((string where, string? text, string beat, bool handler) in texts)
        {
            if (text is not { Length: > 0 } || Names(text, slots, symbols) is not { } names)
            {
                continue;
            }

            IReadOnlyList<string> things = NarrativeFacts.Beats[beat].Things;
            foreach (string name in names.Distinct(StringComparer.Ordinal))
            {
                if (slots.Contains(name))
                {
                    named.Add(name);
                }
                else if (name == StoryletScopes.Handler)
                {
                    if (!handler)
                    {
                        add(true, $"names #handler# in {where}, outside a check's success and failure, where no one has been sent.");
                    }
                }
                else if (!things.Contains(name))
                {
                    add(true, $"names {name} in {where}, and casts no slot called {name}.");
                }
            }
        }

        foreach (string slot in model.Cast.Select(c => c.Name).Where(s => !named.Contains(s)))
        {
            add(false, $"casts {slot}, and its words never name them.");
        }
    }

    // The people and things a text names: its speakers (not the captain or a best SKILL_ID speaker), and its tokens' heads, with a
    // symbol's own options read in its place. Null when it cannot be read, which the loader says.
    private static List<string>? Names(string text, HashSet<string> slots, IReadOnlyDictionary<string, SymbolModel> symbols)
    {
        try
        {
            Script script = Scripts.Parse(text.ReplaceLineEndings("\n"));
            var names = new List<string>();
            var walked = new HashSet<string>(StringComparer.Ordinal);
            foreach (ScriptEntry entry in script.Entries)
            {
                if (entry.Speaker is { } speaker && speaker != Scripts.Captain && Scripts.BestSkill(speaker) is null)
                {
                    names.Add(speaker);
                }

                Heads(entry.Text, slots, symbols, walked, names);
            }

            if (script.Summary is { } summary)
            {
                Heads(summary, slots, symbols, walked, names);
            }

            return names;
        }
        catch (NarrativeFormatException)
        {
            return null;
        }
    }

    // A person is read before a symbol of the same name, as the loader reads them.
    private static void Heads(string text, HashSet<string> slots, IReadOnlyDictionary<string, SymbolModel> symbols, HashSet<string> walked, List<string> names)
    {
        foreach (Token token in NarrativeText.Parse(text).Select(p => p.Token).OfType<Token>())
        {
            if (!slots.Contains(token.Head) && token.Head != StoryletScopes.Handler && symbols.TryGetValue(token.Head, out SymbolModel? symbol))
            {
                if (walked.Add(symbol.Id))
                {
                    foreach (string option in symbol.Options)
                    {
                        Heads(option, slots, symbols, walked, names);
                    }
                }
            }
            else
            {
                names.Add(token.Head);
            }
        }
    }

}
