using System.Globalization;
using System.Text.RegularExpressions;
using Sim.Core.Content;
using Sim.Core.Narrative;
using Sim.Expressions;

namespace Sim.Content;

// Storylets (slice 1e): what arrives on the itinerary when its expressions hold on a cast, and the answers with their fixed effects.
// From part four (design 4.2) a storylet is cast with up to three named slots and read in the expression language.
public static partial class ContentLoader
{
    private const int MinOptions = 2;
    private const int MaxOptions = 4;
    private const int MaxVignetteEntries = 3;

    // The most value combinations the no-hidden-memory check tries when it looks for the lowest values that let a storylet arrive.
    private const int MaxWitnesses = 20_000;

    private const string ScopeIsNowCast = "scope was replaced by cast in part four; a storylet for crew casts subject.";

    private const string RequiresIsNowExpression = "requires is one expression since part four, not a list; write it as \"subject.trait.shaken >= 1 && subject.morale <= 25\".";

    private static List<StoryletDef> BuildStorylets(List<(string File, StoryletDto Dto)> items, TraitBook book, ExpressionBook expressions, IReadOnlySet<string> skills, List<ContentError> errors)
    {
        var defs = new List<StoryletDef>();
        foreach ((string file, StoryletDto dto) in items)
        {
            int before = errors.Count;
            void Error(string message)
            {
                errors.Add(new ContentError(file, $"{dto.Id}: {message}"));
            }

            if (dto.Stakes is < 1 or > 5)
            {
                Error("A storylet's stakes must be 1 to 5.");
            }

            Casting casting = CastOf(dto, expressions, Error);
            Cooldown cooldown = CooldownOf(dto.Cooldown, Error) ?? Cooldown.None;
            List<StoryletOption> options = Options(dto.Options, casting, book, expressions, skills, Error);
            ChronicleDef? chronicle = ChronicleOf(dto.Chronicle, option: false, Error);
            if (dto.Effects is not null || dto.Default is not null)
            {
                Error("A storylet's effects and default belong to a vignette; a storylet's effects are in its options.");
            }

            // Review focus 4: whatever else is shown, one answer always is, and it needs no one fit to send.
            if (options.Count > 0 && options.All(o => o.Requires is not null || o.Check is not null))
            {
                Error("A storylet needs an option with neither requires nor check, so it can always be answered.");
            }

            // A cast cooldown meets the rule instead (design 4.2): it, not the answers, stops the storylet returning at once.
            if (casting.Sound && casting.Reads.Count == 0 && cooldown.Cast == 0)
            {
                Error("A storylet's cast and requires must read at least one trait, so its answers can stop it returning.");
            }

            string? name = CheckName(file, dto.Id!, dto.Name, errors);

            // Only a storylet that is otherwise sound can be judged on whether it can arrive and whether its answers end it.
            Dictionary<TraitRead, int>? lowest = errors.Count == before ? Lowest(casting, book, expressions, Error) : null;
            // With cooldown.cast above 0 a storylet may recur by design (design 4.6).
            if (lowest is not null && cooldown.Cast == 0)
            {
                NoHiddenMemory(casting, lowest, options, book, expressions, Error);
            }

            if (errors.Count == before)
            {
                defs.Add(new StoryletDef(dto.Id!, dto.Stakes, casting.Cast, casting.Requires, options, name) { Reads = casting.Reads, Needs = Needs(lowest), Cooldown = cooldown, Chronicle = chronicle });
            }
        }

        return defs;
    }

    // Vignettes (1e part two, design 4.3): a storylet's shape with no stakes, and either effects that apply when it arrives or one
    // small choice of two options with a default. Its expressions need no trait: a cooldown, not its answers, stops it returning.
    private static List<VignetteDef> BuildVignettes(List<(string File, StoryletDto Dto)> items, TraitBook book, ExpressionBook expressions, IReadOnlySet<string> skills, List<ContentError> errors)
    {
        var defs = new List<VignetteDef>();
        foreach ((string file, StoryletDto dto) in items)
        {
            int before = errors.Count;
            void Error(string message)
            {
                errors.Add(new ContentError(file, $"{dto.Id}: {message}"));
            }

            if (dto.Stakes != 0)
            {
                Error("A vignette has no stakes.");
            }

            Casting casting = CastOf(dto, expressions, Error);
            Cooldown? cooldown = CooldownOf(dto.Cooldown, Error);
            if (dto.Chronicle is not null)
            {
                Error("A vignette leaves no chronicle entry, so it has no chronicle.");
            }

            bool effectsWritten = dto.Effects is { Count: > 0 };
            bool optionsWritten = dto.Options is { Count: > 0 };
            var effects = new List<StoryletEffect>();
            var options = new List<StoryletOption>();
            if (effectsWritten && optionsWritten)
            {
                Error("A vignette has effects or two options, not both.");
            }
            else if (optionsWritten)
            {
                options = Options(dto.Options, casting, book, expressions, skills, Error, vignette: true);
                if (dto.Default is null || options.All(o => o.Id != dto.Default))
                {
                    Error("A vignette with options needs a default, and it must be one of them.");
                }
            }
            else
            {
                if (!effectsWritten)
                {
                    Error("A vignette with no choice needs at least one effect.");
                }

                if (dto.Default is not null)
                {
                    Error("A vignette with no choice has no default.");
                }

                effects = (dto.Effects ?? []).Select(e => StoryletEffectOf(e ?? [], casting.Slots, book, Error)).OfType<StoryletEffect>().ToList();
            }

            if (dto.Text is { } text && text.Split('\n').Count(l => l.Trim().Length > 0) > MaxVignetteEntries)
            {
                Error("A vignette's text is one to three entries.");
            }

            string? name = CheckName(file, dto.Id!, dto.Name, errors);
            Dictionary<TraitRead, int>? lowest = errors.Count == before ? Lowest(casting, book, expressions, Error) : null;
            if (errors.Count == before)
            {
                string? first = casting.Slots.FirstOrDefault();
                string? fact = casting.Checked.SelectMany(c => c.Lasts).FirstOrDefault(l => l.Slot == first)?.Kind;
                defs.Add(new VignetteDef(dto.Id!, casting.Cast, casting.Requires, effects, options, dto.Default, name) { Reads = casting.Reads, Needs = Needs(lowest), Fact = fact, Cooldown = cooldown });
            }
        }

        return defs;
    }

    // A storylet's or vignette's cast and requires, each checked against the expression book with the slots it may read.
    private sealed record Casting(List<CastSlot> Cast, Expr? Requires, List<CheckedExpression> Checked, bool Sound)
    {
        public IReadOnlyList<string> Slots => Cast.Select(c => c.Name).ToList();

        // Every trait read, in reading order, each once.
        public IReadOnlyList<TraitRead> Reads => Checked.SelectMany(c => c.Traits).Distinct().ToList();
    }

    private static Casting CastOf(StoryletDto dto, ExpressionBook expressions, Action<string> error)
    {
        bool sound = true;
        if (dto.Scope is not null)
        {
            error(ScopeIsNowCast);
            sound = false;
        }

        var cast = new List<CastSlot>();
        var checkedExpressions = new List<CheckedExpression>();
        Dictionary<string, string?> written = dto.Cast ?? [];
        if (written.Count > StoryletScopes.MaxSlots)
        {
            error("A cast is at most three slots.");
            sound = false;
        }

        foreach ((string slot, string? source) in written.Take(StoryletScopes.MaxSlots))
        {
            if (!Ids.IsInnerId(slot))
            {
                error($"Slot name '{slot}' must be {Said(Ids.InnerRule)}");
                sound = false;
                continue;
            }

            if (StoryletScopes.Reserved.Contains(slot))
            {
                error($"'{slot}' cannot name a slot; {Either(StoryletScopes.Reserved)} each mean something already.");
                sound = false;
                continue;
            }

            cast.Add(new CastSlot(slot, new Expr(source ?? "")));
        }

        // A slot's own expression reads the slots before it and itself; requires reads them all. Expressions are checked only once
        // every slot's name is sound, so a bad name is not reported again as a slot no expression can read.
        List<string> names = cast.Select(c => c.Name).ToList();
        if (sound)
        {
            for (int k = 0; k < cast.Count; k++)
            {
                if (Check(expressions, cast[k].Condition.Source, names[..(k + 1)], $"cast.{cast[k].Name}", error) is { } read)
                {
                    checkedExpressions.Add(read);
                }
                else
                {
                    sound = false;
                }
            }
        }

        Expr? requires = null;
        switch (dto.Requires)
        {
            case null:
                break;
            case string source:
                requires = new Expr(source);
                if (!sound)
                {
                    break;
                }

                if (Check(expressions, source, names, "requires", error) is { } read)
                {
                    checkedExpressions.Add(read);
                }
                else
                {
                    sound = false;
                }

                break;
            default:
                error(RequiresIsNowExpression);
                sound = false;
                break;
        }

        return new Casting(cast, requires, checkedExpressions, sound);
    }

    // A cooldown as written, each part 0 unless given (design 4.6); null when none is written.
    private static Cooldown? CooldownOf(CooldownDto? written, Action<string> error)
    {
        if (written is null)
        {
            return null;
        }

        if (written.Cast is < 0 || written.Global is < 0)
        {
            error("A cooldown's cast and global are whole numbers of cycles, 0 or more.");
        }

        return new Cooldown(written.Cast ?? 0, written.Global ?? 0);
    }

    // One expression, or null with its error: the storylet's key and the character the error is at, counted from 1.
    private static CheckedExpression? Check(ExpressionBook expressions, string source, IReadOnlyList<string> slots, string key, Action<string> error)
    {
        if (expressions.Check(source, slots, out ExpressionError? refused) is { } read)
        {
            return read;
        }

        error($"{key}, at character {refused!.Position + 1}: {refused.Message}");
        return null;
    }

    private static List<StoryletOption> Options(List<StoryletOptionDto?>? written, Casting casting, TraitBook book, ExpressionBook expressions, IReadOnlySet<string> skills, Action<string> error, bool vignette = false)
    {
        var options = new List<StoryletOption>();
        if (vignette ? written is not { Count: 2 } : written is not { Count: >= MinOptions and <= MaxOptions })
        {
            error(vignette ? "A vignette's choice is two options." : "A storylet needs two to four options.");
        }

        var ids = new HashSet<string>();
        for (int i = 0; i < (written?.Count ?? 0); i++)
        {
            if (written![i] is not { } dto)
            {
                error($"options[{i + 1}] is blank.");
                continue;
            }

            if (dto.Id is null || !Ids.IsInnerId(dto.Id))
            {
                error($"Option id '{dto.Id}' must be {Said(Ids.InnerRule)}");
            }
            else if (!ids.Add(dto.Id))
            {
                error($"Option id '{dto.Id}' is used twice.");
            }

            string? label = dto.Label;
            if (string.IsNullOrWhiteSpace(label) || label.Trim() != label || label.Length > 40 || label.IndexOfAny(['\r', '\n']) >= 0)
            {
                error($"Option '{dto.Id}': label must be 1 to 40 characters on one line, with no spaces at either end.");
            }

            // An option's requires reads every slot, and is read again when it is answered. A vignette's choice is small and free.
            Expr? requires = null;
            if (dto.Requires is { } source)
            {
                requires = new Expr(source);
                if (vignette)
                {
                    error($"Option '{dto.Id}': a vignette's options have no requires.");
                }
                else if (casting.Sound)
                {
                    Check(expressions, source, casting.Slots, $"options.{dto.Id}.requires", error);
                }
            }

            List<StoryletEffect> effects = (dto.Effects ?? []).Select(e => StoryletEffectOf(e ?? [], casting.Slots, book, error)).OfType<StoryletEffect>().ToList();
            StoryletCheck? check = CheckOf(dto, casting, book, skills, m => error($"Option '{dto.Id}': {m}"), vignette);
            ChronicleDef? chronicle = null;
            if (vignette && dto.Chronicle is not null)
            {
                error($"Option '{dto.Id}': {NoVignetteChronicle}");
            }
            else
            {
                chronicle = ChronicleOf(dto.Chronicle, option: true, m => error($"Option '{dto.Id}': {m}"));
            }

            options.Add(new StoryletOption(dto.Id ?? "", label ?? "", effects, requires, check) { Chronicle = chronicle });
        }

        return options;
    }

    private const string NoVignetteChronicle = "a vignette leaves no chronicle entry, so it has no chronicle.";

    // chronicle: on a storylet or an option (slice 1e part five, design 4.4). A storylet's is a weight alone; an option's is a weight,
    // a text, or both. The text's words are checked with the storylet's lines (BuildStoryletLines). Null when none is written or it
    // is refused.
    private static ChronicleDef? ChronicleOf(ChronicleDto? written, bool option, Action<string> error)
    {
        if (written is null)
        {
            return null;
        }

        bool sound = true;
        if (!option && written.Text is not null)
        {
            error("A storylet's chronicle has no text; an option's may.");
            sound = false;
        }

        if (written.Weight is { } weight ? weight is < 0 or > Sim.Core.ChronicleTuning.Most : !option)
        {
            error($"{(option ? "the" : "The storylet's")} chronicle weight must be a whole number from 0 to {Sim.Core.ChronicleTuning.Most}.");
            sound = false;
        }
        else if (written.Weight is null && written.Text is null)
        {
            error("the chronicle needs a weight, a text, or both.");
            sound = false;
        }

        return sound ? new ChronicleDef(written.Weight, written.Text?.ReplaceLineEndings("\n")) : null;
    }

    // An option settled by a check (design 4.7), or null when it has none or it is refused. Its effects and text are in success and
    // failure, where handler is a slot beside the storylet's own.
    private static StoryletCheck? CheckOf(StoryletOptionDto dto, Casting casting, TraitBook book, IReadOnlySet<string> skills, Action<string> error, bool vignette)
    {
        if (dto.Check is null)
        {
            if (dto.Success is not null || dto.Failure is not null)
            {
                error("success and failure belong to a check; add check: { skill, difficulty }.");
            }

            return null;
        }

        if (vignette)
        {
            error("a vignette's options are not settled by a check.");
            return null;
        }

        bool ok = true;
        if (dto.Effects is not null || dto.Text is not null)
        {
            error("a check's effects and text are in its success and failure, not beside them.");
            ok = false;
        }

        if (dto.Success is null || dto.Failure is null)
        {
            error("a check needs both success and failure.");
            ok = false;
        }

        if (dto.Check.Skill is not { } skill || !skills.Contains(skill))
        {
            error($"the check uses unknown skill '{dto.Check.Skill}'.");
            ok = false;
        }

        if (dto.Check.Difficulty is not >= 1)
        {
            error("the check's difficulty must be 1 or more.");
            ok = false;
        }

        List<string> slots = [.. casting.Slots, StoryletScopes.Handler];
        StoryletOutcome Outcome(OutcomeDto? written)
        {
            return new StoryletOutcome((written?.Effects ?? []).Select(e => StoryletEffectOf(e ?? [], slots, book, error, handler: true)).OfType<StoryletEffect>().ToList());
        }

        StoryletOutcome success = Outcome(dto.Success);
        StoryletOutcome failure = Outcome(dto.Failure);
        return ok ? new StoryletCheck(dto.Check.Skill!, dto.Check.Difficulty!.Value, success, failure) : null;
    }

    // One loyalty, morale, or trait change, or null when it is refused (each problem reported). On and toward name the storylet's
    // slots; loyalty and morale may also be on crew, a trait on the ship. A trait with no on is on the first slot, or on the ship
    // when there is no cast.
    private static StoryletEffect? StoryletEffectOf(Dictionary<string, string> entry, IReadOnlyList<string> slots, TraitBook book, Action<string> error, bool handler = false)
    {
        // Design 4.7: the handler is named only once the captain sends someone on a check.
        if (!handler && (entry.GetValueOrDefault("on") == StoryletScopes.Handler || entry.GetValueOrDefault("toward") == StoryletScopes.Handler))
        {
            error("handler is a slot only in a check's success and failure.");
            return null;
        }

        string[] kinds = ["loyalty", "morale", "trait"];
        string[] present = kinds.Where(entry.ContainsKey).ToArray();
        if (present.Length > 1)
        {
            error($"An effect changes one thing; this one changes {string.Join(" and ", present)}.");
            return null;
        }

        string? kind = present.FirstOrDefault();
        string[] keys = kind switch
        {
            "loyalty" or "morale" => [kind, "on"],
            "trait" => ["trait", "add", "set", "on", "toward"],
            _ => [],
        };
        bool ok = true;
        foreach (string key in entry.Keys.Where(k => !keys.Contains(k)))
        {
            error(key == "flag" ? FlagIsNowTrait : $"A storylet's effect can change loyalty, morale, or a trait; '{key}' is not one of them.");
            ok = false;
        }

        if (entry.Count == 0)
        {
            error("A storylet's effect is blank.");
            return null;
        }

        List<string> own = slots.Where(s => s != StoryletScopes.Handler).ToList();
        string casts = own.Count == 0 ? "casts no one" : $"casts {Either(own)}";
        string? on = entry.GetValueOrDefault("on");
        bool slot = on is not null && slots.Contains(on);
        if (kind is "loyalty" or "morale")
        {
            if (!int.TryParse(entry[kind], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
            {
                error($"A {kind} change must be a whole number.");
                ok = false;
            }

            if (on is null || (on != StoryletScopes.Crew && !slot))
            {
                error(on is null || on == StoryletScopes.Ship
                    ? $"A {kind} change needs on: crew{(slots.Count == 0 ? "" : " or a slot")}; this storylet {casts}."
                    : $"on: {on} names no slot; this storylet {casts}.");
                ok = false;
            }

            return !ok ? null
                : kind == "loyalty" ? new StoryletEffect(Loyalty: amount, LoyaltyOn: on)
                : new StoryletEffect(Morale: amount, MoraleOn: on);
        }

        if (kind is null)
        {
            return null;
        }

        string trait = entry["trait"];
        if (!Ids.IsInnerId(trait))
        {
            error($"Trait id '{trait}' must be {Said(Ids.InnerRule)}");
            ok = false;
        }

        bool set = entry.TryGetValue("set", out string? setText);
        bool add = entry.TryGetValue("add", out string? addText);
        int value = 0;
        if (set == add)
        {
            error("A trait change needs add or set, and only one of them.");
            ok = false;
        }
        else if (book.Def(trait) is { HasLevels: true } levelled)
        {
            if (!set)
            {
                error(levelled.LevelsAreSet);
                ok = false;
            }
            else if (book.SetValue(trait, setText!, error) is { } level)
            {
                value = level;
            }
            else
            {
                ok = false;
            }
        }
        else if (!int.TryParse(set ? setText : addText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error("A trait change's amount must be a whole number.");
            ok = false;
        }
        else if (set && value < 0)
        {
            error("A trait's set must be 0 or more.");
            ok = false;
        }

        string? toward = entry.GetValueOrDefault("toward");
        string holder = on ?? slots.FirstOrDefault(s => s != StoryletScopes.Handler) ?? StoryletScopes.Ship;
        if (on is not null && on != StoryletScopes.Ship && !slot)
        {
            error(on == StoryletScopes.Crew ? $"A storylet's trait change takes on: ship or a slot, or no on; this storylet {casts}." : $"on: {on} names no slot; this storylet {casts}.");
            ok = false;
        }
        else if (toward is not null && !slots.Contains(toward))
        {
            error($"toward: {toward} names no slot; this storylet {casts}.");
            ok = false;
        }
        else if (toward is not null && toward == holder)
        {
            error($"toward: {toward} names the slot the trait is on; a pair trait is held toward someone else.");
            ok = false;
        }
        else if (toward is not null && holder == StoryletScopes.Ship)
        {
            error("toward is for a trait held by a crew member, not the ship.");
            ok = false;
        }
        else if (ok && Ids.IsInnerId(trait))
        {
            ok &= book.CheckPlace(trait, toward is not null ? TraitPlaces.Pair : holder == StoryletScopes.Ship ? TraitPlaces.Ship : TraitPlaces.Crew, error);
        }

        return ok ? new StoryletEffect(Trait: new TraitChange(trait, value, set, on ?? "", toward)) : null;
    }

    // The lowest values of the traits a storylet's or vignette's expressions read that let every one of them hold, read with only
    // the traits known (a clause on anything else may still hold). Null with an error when no values do: it could never arrive.
    private static Dictionary<TraitRead, int>? Lowest(Casting casting, TraitBook book, ExpressionBook expressions, Action<string> error)
    {
        if (casting.Reads.Count == 0)
        {
            return [];
        }

        if (Witness(casting, casting.Reads, book).FirstOrDefault(v => MayHold(casting, expressions, v)) is { } lowest)
        {
            return lowest;
        }

        error("No values of the traits it reads let every expression hold, so it can never arrive.");
        return null;
    }

    private static bool MayHold(Casting casting, ExpressionBook expressions, IReadOnlyDictionary<TraitRead, int> values)
    {
        return casting.Checked.All(c => expressions.OnTraits(c, r => values.GetValueOrDefault(r)) != false);
    }

    // The traits it reads that its lowest values have above 0: something must set them before it can arrive (the lint's "Nothing
    // sets").
    private static IReadOnlyList<TraitRead> Needs(Dictionary<TraitRead, int>? lowest)
    {
        return lowest is null ? [] : lowest.Where(v => v.Value > 0).Select(v => v.Key).ToList();
    }

    // No hidden memory (design 4.6): no answer may leave the storylet's expressions as they were, or it would arrive again at once,
    // and every cycle after. The expressions are read with only their traits known, at the lowest values that let every one hold
    // and then at the values each option leaves there, clamped as play clamps them. An option that leaves none of them false is
    // refused.
    private static void NoHiddenMemory(Casting casting, Dictionary<TraitRead, int> witness, IReadOnlyList<StoryletOption> options, TraitBook book, ExpressionBook expressions, Action<string> error)
    {
        if (witness.Count == 0)
        {
            return;
        }

        string own = casting.Slots.FirstOrDefault() ?? StoryletScopes.Ship;
        foreach (StoryletOption option in options)
        {
            // A check's success and failure are each an outcome, judged as an option is (Task 6). A change on or toward the handler
            // touches no trait the expressions read, since the handler is never in the cast.
            IEnumerable<(string? Name, IReadOnlyList<StoryletEffect> Effects)> outcomes = option.Check is { } check
                ? [("success", check.Success.Effects), ("failure", check.Failure.Effects)]
                : [(null, option.Effects)];
            foreach ((string? name, IReadOnlyList<StoryletEffect> effects) in outcomes)
            {
                var values = new Dictionary<TraitRead, int>(witness);
                foreach (TraitChange change in effects.Select(e => e.Trait).OfType<TraitChange>())
                {
                    var read = new TraitRead(change.Trait, change.On.Length == 0 ? own : change.On, change.Toward);
                    if (values.TryGetValue(read, out int current))
                    {
                        values[read] = (book.Def(change.Trait) ?? TraitDef.Plain(change.Trait)).Clamp(change.Set ? change.Amount : current + change.Amount);
                    }
                }

                if (MayHold(casting, expressions, values))
                {
                    string on = name is null ? "" : $" on {name}";
                    error($"Option '{option.Id}' leaves every expression true{on}, so this storylet would come back every cycle.");
                }
            }
        }
    }

    // The values of the traits read, lowest total first, then in reading order: each from 0 to one past the highest number written
    // in the expressions, or to its cap; a trait with levels over all of them, since they are written by name.
    private static IEnumerable<Dictionary<TraitRead, int>> Witness(Casting casting, IReadOnlyList<TraitRead> reads, TraitBook book)
    {
        int highest = casting.Checked.SelectMany(c => Numbers().Matches(c.Source)).Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();
        int top = Math.Min(highest, MaxWitnesses) + 1;
        int[] tops = reads.Select(r => book.Def(r.Trait) ?? TraitDef.Plain(r.Trait)).Select(d => d.HasLevels ? d.Cap : Math.Min(top, d.Cap)).ToArray();
        while (tops.Aggregate(1L, (n, t) => n * (t + 1)) > MaxWitnesses)
        {
            int widest = Array.IndexOf(tops, tops.Max());
            tops[widest]--;
        }

        var combinations = new List<int[]> { new int[reads.Count] };
        for (int k = 0; k < reads.Count; k++)
        {
            combinations = combinations.SelectMany(c => Enumerable.Range(0, tops[k] + 1).Select(v =>
            {
                int[] next = (int[])c.Clone();
                next[k] = v;
                return next;
            })).ToList();
        }

        return combinations
            .OrderBy(c => c.Sum())
            .ThenBy(c => c, Comparer<int[]>.Create((a, b) => a.Zip(b, (x, y) => x.CompareTo(y)).FirstOrDefault(d => d != 0)))
            .Select(c => reads.Select((r, k) => (r, c[k])).ToDictionary(p => p.r, p => p.Item2));
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Numbers();

    // A storylet's text and each option's text, as lines whose criteria are where they sit, the way an encounter's own text is.
    // Its speakers and the people its text names are its slots; a storylet for the ship names no one.
    // Chronicle: for storylets, whose options' chronicle text is checked here too, as a chronicle line with the cast as its people
    // (slice 1e part five, design 4.9); it is kept on the option, not made a line.
    private static List<LineDef> BuildStoryletLines(List<(string File, StoryletDto Dto)> items, List<SymbolDef> symbols, List<SkillDef> skills, List<ContentError> errors, bool chronicle = false)
    {
        Dictionary<string, SymbolDef> symbolsById = symbols.ToDictionary(s => s.Id);
        HashSet<string> skillIds = skills.Select(s => s.Id).ToHashSet();
        var lines = new List<LineDef>();
        foreach ((string file, StoryletDto dto) in items)
        {
            string id = dto.Id!;
            List<string> slots = (dto.Cast ?? []).Keys.Where(k => Ids.IsInnerId(k) && !StoryletScopes.Reserved.Contains(k)).Take(StoryletScopes.MaxSlots).ToList();
            void Add(string where, string? text, string kind, CriterionDef[] when, bool handler = false, bool keep = true)
            {
                if (text is null)
                {
                    return;
                }

                int before = errors.Count;
                void Error(string message)
                {
                    errors.Add(new ContentError(file, $"{id}: {where} {message}"));
                }

                if (text.Length == 0)
                {
                    Error("must not be empty.");
                    return;
                }

                BeatSpec scoped = NarrativeFacts.Beats[kind] with { People = handler ? [.. slots, StoryletScopes.Handler] : slots };
                CheckScript(text, scoped, symbolsById, skillIds, m => Error($"cannot be read: {m}"), m => Error($"cannot be used: {m}"));
                if (errors.Count == before && NarrativeFacts.Chronicle.Is(kind) && ChronicleScript(text) is { } problem)
                {
                    Error($"cannot be used: {problem}");
                }

                if (errors.Count == before && keep)
                {
                    lines.Add(new LineDef($"{id}.{where}", kind, when, text.ReplaceLineEndings("\n"), null, id));
                }
            }

            var here = new CriterionDef("storylet", [id]);
            Add("text", dto.Text, "storylet", [here]);
            foreach (StoryletOptionDto option in (dto.Options ?? []).OfType<StoryletOptionDto>().Where(o => o.Id is not null))
            {
                Add($"{option.Id}.text", option.Text, "storyletAnswer", [here, new CriterionDef("option", [option.Id!])]);

                // A check's outcomes (Task 6): the fact outcome tells them apart, and the handler may be named.
                if (option.Check is not null)
                {
                    foreach ((string outcome, OutcomeDto? written) in new[] { ("success", option.Success), ("failure", option.Failure) })
                    {
                        Add($"{option.Id}.{outcome}.text", written?.Text, "storyletAnswer", [here, new CriterionDef("option", [option.Id!]), new CriterionDef("outcome", [outcome])], handler: true);
                    }
                }

                // A check option's chronicle text may name the handler, as its outcomes' text may.
                if (chronicle)
                {
                    Add($"{option.Id}.chronicle.text", option.Chronicle?.Text, NarrativeFacts.Chronicle.Storylet, [], handler: option.Check is not null, keep: false);
                }
            }
        }

        return lines;
    }

    private sealed class StoryletDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public int Stakes { get; set; }

        // Gone in part four: read only to say so.
        public string? Scope { get; set; }

        // Each slot to its expression, in reading order (1e part four).
        public Dictionary<string, string?>? Cast { get; set; }

        // One expression; a list is part three's form, read only to say so.
        public object? Requires { get; set; }

        public string? Text { get; set; }

        public List<StoryletOptionDto?>? Options { get; set; }

        // A vignette's only (1e part two).
        public List<Dictionary<string, string>?>? Effects { get; set; }

        public string? Default { get; set; }

        // In cycles (1e part four, design 4.6).
        public CooldownDto? Cooldown { get; set; }

        // Slice 1e part five: a weight alone; refused on a vignette.
        public ChronicleDto? Chronicle { get; set; }
    }

    private sealed class CooldownDto
    {
        public int? Cast { get; set; }

        public int? Global { get; set; }
    }

    private sealed class StoryletOptionDto
    {
        public string? Id { get; set; }

        public string? Label { get; set; }

        public string? Requires { get; set; }

        public List<Dictionary<string, string>?>? Effects { get; set; }

        public string? Text { get; set; }

        // 1e part four, Task 6: an option settled by a check.
        public CheckDto? Check { get; set; }

        public OutcomeDto? Success { get; set; }

        public OutcomeDto? Failure { get; set; }

        // Slice 1e part five: a weight, a text, or both; refused on a vignette's option.
        public ChronicleDto? Chronicle { get; set; }
    }

    private sealed class CheckDto
    {
        public string? Skill { get; set; }

        public int? Difficulty { get; set; }
    }

    private sealed class OutcomeDto
    {
        public List<Dictionary<string, string>?>? Effects { get; set; }

        public string? Text { get; set; }
    }
}
