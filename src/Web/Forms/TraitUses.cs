using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Sim.Content;
using Sim.Core.Content;
using Sim.Expressions;

namespace Web.Forms;

// One item that reads, changes, or holds a trait, and what it does with it, each said once in the item's order.
public sealed record TraitUse(ContentItemRef Item, IReadOnlyList<string> Says);

public sealed record TraitUseGroup(ContentKind Kind, string Title, IReadOnlyList<TraitUse> Uses);

// Where a trait is used (1e part six, design 4.9): every storylet, vignette, encounter, other trait, and test ship that reads, changes, or
// holds it, by kind, and the warnings that follow from them. Loaded is false while the pack does not load: the reads are the loader's
// own (each clause of an expression, checked with the pack's book, and its trait reads), so nothing is listed until it does.
public sealed partial record TraitUses(string Trait, bool Loaded, IReadOnlyList<TraitUseGroup> Groups, IReadOnlyList<string> Warnings)
{
    private static readonly ConditionalWeakTable<ContentFileSet, IReadOnlyDictionary<string, TraitUses>> Cache = new();

    // The groups in the order the panel shows them.
    private static readonly (ContentKind Kind, string Title)[] Kinds =
    [
        (ContentKind.Storylet, "Storylets"),
        (ContentKind.Vignette, "Vignettes"),
        (ContentKind.Encounter, "Encounters"),
        (ContentKind.Trait, "Other traits' rules"),
        (ContentKind.TestShip, "Test ships"),
    ];

    // How many items use it, for the traits list's "Used by".
    public int Count => Groups.Sum(g => g.Uses.Count);

    // Where this trait is used; nothing for an id no trait has, or while the pack does not load.
    public static TraitUses Of(ContentFileSet files, string trait)
    {
        return All(files).GetValueOrDefault(trait) ?? new TraitUses(trait, files.Validate().Pack is not null, [], []);
    }

    // Every trait's uses, read once for a set of files: the list asks for each trait's count, and the expressions are checked once each.
    public static IReadOnlyDictionary<string, TraitUses> All(ContentFileSet files)
    {
        return Cache.GetValue(files, Read);
    }

    private static IReadOnlyDictionary<string, TraitUses> Read(ContentFileSet files)
    {
        List<TraitModel> traits = files.Index().Where(i => i.Kind == ContentKind.Trait).Select(i => ContentEdits.Get<TraitModel>(files, i.Kind, i.Id)).OfType<TraitModel>().ToList();
        if (files.Validate().Pack is not { } pack)
        {
            return traits.ToDictionary(t => t.Id, t => new TraitUses(t.Id, false, [], []));
        }

        ExpressionBook book = ExpressionChoices.Of(pack).Book!;
        var reckon = new Reckoning(traits.Select(t => t.Id));
        foreach (ContentItemRef item in files.Index())
        {
            switch (item.Kind)
            {
                case ContentKind.Storylet when ContentEdits.Get<StoryletModel>(files, item.Kind, item.Id) is { } storylet:
                    Storylet(reckon, book, item, storylet.Cast, storylet.Requires, storylet.Options, []);
                    break;
                case ContentKind.Vignette when ContentEdits.Get<VignetteModel>(files, item.Kind, item.Id) is { } vignette:
                    Storylet(reckon, book, item, vignette.Cast, vignette.Requires, vignette.Options, vignette.Effects);
                    break;
                case ContentKind.Encounter when ContentEdits.Get<EncounterModel>(files, item.Kind, item.Id) is { } encounter:
                    Encounter(reckon, item, encounter);
                    break;
                case ContentKind.Trait when ContentEdits.Get<TraitModel>(files, item.Kind, item.Id) is { } other:
                    Rules(reckon, item, other);
                    break;
                case ContentKind.TestShip when ContentEdits.Get<TestShipModel>(files, item.Kind, item.Id) is { } ship:
                    foreach (TestShipTraitModel held in ship.Traits)
                    {
                        string toward = held.Toward is { } t ? $" toward {t}" : "";
                        reckon.Add(held.Trait, item, $"holds {held.Value ?? "?"} on {held.On ?? "the ship"}{toward}");
                    }

                    break;
            }
        }

        return traits.ToDictionary(t => t.Id, t => reckon.Uses(t));
    }

    // A storylet's or a vignette's reads, clause by clause, then its options' effects (and a check's outcomes'), then its own effects.
    private static void Storylet(Reckoning reckon, ExpressionBook book, ContentItemRef item, IReadOnlyList<CastSlotModel> cast, string? requires, IReadOnlyList<StoryletOptionModel> options, IReadOnlyList<StoryletEffectModel> own)
    {
        List<string> slots = cast.Select(c => c.Name).ToList();
        var expressions = new List<(string Source, List<string> Slots)>();
        for (int k = 0; k < cast.Count; k++)
        {
            expressions.Add((cast[k].Condition, slots.Take(k + 1).ToList()));
        }

        if (requires is not null)
        {
            expressions.Add((requires, slots));
        }

        expressions.AddRange(options.Where(o => o.Requires is not null).Select(o => (o.Requires!, slots)));
        foreach ((string source, List<string> scope) in expressions)
        {
            if (book.Check(source, scope, out _) is not { } whole)
            {
                continue;
            }

            foreach (CheckedExpression clause in book.Split(whole))
            {
                foreach (string trait in clause.Traits.Select(r => r.Trait).Concat(Since().Matches(clause.Source).Select(m => m.Groups["trait"].Value)).Distinct())
                {
                    reckon.Read(trait, item, $"reads: {clause.Source.Trim()}");
                }
            }
        }

        string first = slots.Count > 0 ? slots[0] : "the ship";
        foreach (StoryletOptionModel option in options)
        {
            Effects(reckon, item, option.Label, option.Effects, first);
            if (option.Check is { } check)
            {
                Effects(reckon, item, $"{option.Label}, success", check.Success?.Effects ?? [], first);
                Effects(reckon, item, $"{option.Label}, failure", check.Failure?.Effects ?? [], first);
            }
        }

        Effects(reckon, item, "When it arrives", own, first);
    }

    // "Not now: sets 1 on voice". A blank on is the first slot, or the ship with no cast, as the loader reads it.
    private static void Effects(Reckoning reckon, ContentItemRef item, string where, IReadOnlyList<StoryletEffectModel> effects, string first)
    {
        foreach (StoryletEffectModel effect in effects.Where(e => e.What == "trait" && e.Trait is not null))
        {
            string on = effect.On switch { null => first, "ship" => "the ship", { } slot => slot };
            string toward = effect.Toward is { } t ? $" toward {t}" : "";
            reckon.Change(effect.Trait!, item, $"{where}: {Change(effect)} on {on}{toward}", Raises(effect));
        }
    }

    // An encounter's approaches' requirements, then their outcomes' trait changes, then its decision's.
    private static void Encounter(Reckoning reckon, ContentItemRef item, EncounterModel encounter)
    {
        foreach (ApproachModel approach in encounter.Approaches)
        {
            string title = approach.Name ?? approach.Id;
            foreach (RequirementModel requirement in approach.Requires.Where(r => r.Trait is not null))
            {
                reckon.Read(requirement.Trait!, item, $"reads: {EncounterKind.TraitLabel(requirement)["Trait: ".Length..]}, for {title}");
            }
        }

        foreach (ApproachModel approach in encounter.Approaches)
        {
            foreach ((string tier, IReadOnlyList<EffectModel> effects) in approach.Outcomes)
            {
                EncounterEffects(reckon, item, $"{approach.Name ?? approach.Id}, {tier}", effects);
            }
        }

        if (encounter.Decision is { } decision)
        {
            EncounterEffects(reckon, item, $"{decision.Id}, bold", decision.Bold);
            EncounterEffects(reckon, item, $"{decision.Id}, safe", decision.Safe);
        }
    }

    private static void EncounterEffects(Reckoning reckon, ContentItemRef item, string where, IReadOnlyList<EffectModel> effects)
    {
        foreach (EffectModel effect in effects.Where(e => e.Trait is not null && e.TraitAmount is not null))
        {
            int amount = effect.TraitAmount!.Value;
            string on = effect.TraitOn switch { null or "ship" => "the ship", { } who => $"the {who}" };
            string toward = effect.TraitToward is { } t ? $" toward the {t}" : "";
            reckon.Change(effect.Trait!, item, $"{where}: {(effect.TraitSet ? "sets" : "adds")} {Number(amount)} on {on}{toward}", amount > 0);
        }
    }

    // Another trait's launch rules and onTargetDied that change this one. A rule on its own trait is the trait's own, not a use.
    private static void Rules(Reckoning reckon, ContentItemRef item, TraitModel other)
    {
        var lists = new List<(string Where, IReadOnlyList<StoryletEffectModel> Rules)>();
        if (other.Launch is { } launch)
        {
            lists.Add(("Sent", launch.Sent));
            lists.Add(("Not sent", launch.NotSent));
            lists.Add(("With the target", launch.WithTarget));
        }

        lists.Add(("When the target dies", other.OnTargetDied));
        foreach ((string where, IReadOnlyList<StoryletEffectModel> rules) in lists)
        {
            foreach (StoryletEffectModel rule in rules.Where(r => r.What == "trait" && r.Trait is not null && r.Trait != other.Id))
            {
                string on = rule.On == "target" ? "the target" : "the holder";
                reckon.Change(rule.Trait!, item, $"{where}: {Change(rule)} on {on}", Raises(rule));
            }
        }
    }

    // "sets 1", "adds -1", "sets let_ride".
    private static string Change(StoryletEffectModel effect)
    {
        string amount = effect.Level ?? (effect.Amount is { } n ? Number(n) : "");
        return $"{(effect.Set ? "sets" : "adds")} {amount}";
    }

    // A change that can only lower a trait (add below 0, set 0) does not set it, as the text view's warnings count (TextFileFlow).
    private static bool Raises(StoryletEffectModel effect)
    {
        return effect.Level is not null || effect.Amount > 0;
    }

    private static string Number(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    // A trait's clock, which the rewrite reads apart from its value: x.trait.t.cyclesSince, or x.trait.cyclesSince('t') as the house writes it.
    [GeneratedRegex(@"trait\.(?<trait>[a-z0-9_]+)\.(cycles|days)Since|trait\.(cycles|days)Since\(\s*['""](?<trait>[a-z0-9_]+)['""]\s*\)")]
    private static partial Regex Since();

    private sealed class Reckoning(IEnumerable<string> defined)
    {
        private readonly HashSet<string> traits = defined.ToHashSet(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<(ContentKind, string), (ContentItemRef Item, List<string> Says)>> uses = new(StringComparer.Ordinal);
        private readonly HashSet<string> read = new(StringComparer.Ordinal);
        private readonly HashSet<string> raised = new(StringComparer.Ordinal);
        private readonly HashSet<string> changed = new(StringComparer.Ordinal);

        public void Read(string trait, ContentItemRef item, string says)
        {
            if (Add(trait, item, says))
            {
                read.Add(trait);
            }
        }

        public void Change(string trait, ContentItemRef item, string says, bool raises)
        {
            if (Add(trait, item, says))
            {
                changed.Add(trait);
                if (raises)
                {
                    raised.Add(trait);
                }
            }
        }

        public bool Add(string trait, ContentItemRef item, string says)
        {
            if (!traits.Contains(trait))
            {
                return false;
            }

            if (!uses.TryGetValue(trait, out Dictionary<(ContentKind, string), (ContentItemRef, List<string>)>? byItem))
            {
                byItem = [];
                uses[trait] = byItem;
            }

            if (!byItem.TryGetValue((item.Kind, item.Id), out (ContentItemRef Item, List<string> Says) entry))
            {
                entry = (item, []);
                byItem[(item.Kind, item.Id)] = entry;
            }

            if (!entry.Says.Contains(says))
            {
                entry.Says.Add(says);
            }

            return true;
        }

        public TraitUses Uses(TraitModel trait)
        {
            Dictionary<(ContentKind, string), (ContentItemRef Item, List<string> Says)> byItem = uses.GetValueOrDefault(trait.Id) ?? [];
            List<TraitUseGroup> groups = Kinds
                .Select(k => new TraitUseGroup(k.Kind, k.Title, byItem.Values.Where(u => u.Item.Kind == k.Kind).Select(u => new TraitUse(u.Item, u.Says)).ToList()))
                .Where(g => g.Uses.Count > 0)
                .ToList();
            // A definition with a check bonus, launch rules, or onTargetDied gives the trait a bite of its own, as the text view counts it.
            bool own = trait.Mission is not null || trait.Launch is not null || trait.OnTargetDied.Count > 0;
            var warnings = new List<string>();
            if (read.Contains(trait.Id) && !raised.Contains(trait.Id))
            {
                warnings.Add($"{trait.Id} is read, but nothing in the pack sets it, so what reads it sees 0.");
            }
            else if (changed.Contains(trait.Id) && !read.Contains(trait.Id) && !own)
            {
                warnings.Add($"{trait.Id} is set, but nothing reads it and it has no rules of its own, so setting it does nothing yet.");
            }

            return new TraitUses(trait.Id, true, groups, warnings);
        }
    }
}
