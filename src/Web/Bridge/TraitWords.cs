using Sim.Core;
using Sim.Core.Content;
using Sim.Traits;

namespace Web.Bridge;

// The words traits are shown with (1e part three, design 4.7 and 4.8): chips on the roster and in the briefing, the rows of what
// launching will do, and a trait's value as its level's name where it has one.
public static class TraitWords
{
    // "Shaken 2", "Stood down: promised", "Avoids CREW_03".
    public static string Chip(ContentPack pack, Names names, StoredTrait trait, Func<string, string> crew)
    {
        TraitDef? def = Def(pack, trait.Trait);
        string name = names.Trait(trait.Trait);
        return trait.TargetId is { } target ? $"{name} {crew(target)}"
            : def?.LevelName(trait.Value) is { } level ? $"{name}: {level}"
            : $"{name} {trait.Value}";
    }

    // A trait's value as its level's name, or the number.
    public static string Value(ContentPack pack, string trait, int value)
    {
        return Def(pack, trait)?.LevelName(value) ?? value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // The chips of every shown trait a crew member holds, pairs included, in the order first set (the roster's interim cell).
    public static IReadOnlyList<string> Shown(ContentPack pack, Names names, IReadOnlyList<StoredTrait> traits, string crewId, Func<string, string> crew)
    {
        return traits.Where(t => t.CrewId == crewId && (Def(pack, t.Trait)?.Shown ?? true)).Select(t => Chip(pack, names, t, crew)).ToList();
    }

    // What one launch rule does, in the figure style: "Stood down to broken, Loyalty −2, Morale −4".
    public static string Effects(ContentPack pack, Names names, LaunchPreview rule, Func<string, string> crew)
    {
        var parts = new List<string>();
        foreach (TraitRuleEffect effect in rule.Effects)
        {
            string who = effect.On switch
            {
                TraitRuleOn.Crew => " all crew",
                TraitRuleOn.Target when rule.TargetId is { } target => $" {crew(target)}",
                _ => rule.TargetId is null ? "" : $" {crew(rule.HolderId)}",
            };
            if (effect.Loyalty != 0)
            {
                parts.Add($"Loyalty {BeatLines.Signed(effect.Loyalty)}{who}");
            }
            else if (effect.Morale != 0)
            {
                parts.Add($"Morale {BeatLines.Signed(effect.Morale)}{who}");
            }
            else
            {
                string trait = effect.Trait ?? rule.Trait;
                parts.Add(effect.Set ? $"{names.Trait(trait)} to {Value(pack, trait, effect.Amount)}{who}" : $"{names.Trait(trait)} {BeatLines.Signed(effect.Amount)}{who}");
            }
        }

        return string.Join(", ", parts);
    }

    // Keeping a promise costs nothing: a rule for one left behind that only clears its own trait is not shown.
    public static bool Costs(LaunchPreview rule)
    {
        return rule.When != LaunchWhen.NotSent || rule.Effects.Any(e => !e.ChangesTrait || e.Amount != 0 || !e.Set || (e.Trait is not null && e.Trait != rule.Trait));
    }

    private static TraitDef? Def(ContentPack pack, string trait)
    {
        return pack.Traits.FirstOrDefault(t => t.Id == trait);
    }
}
