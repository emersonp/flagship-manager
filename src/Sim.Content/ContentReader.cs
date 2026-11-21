using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Sim.Content;

// Reads a list file into models without judging it: a value the loader would refuse is read as far as it goes, so a broken pack can still be opened.
internal static class ContentReader
{
    // One list entry as written, and the model read from it (null when it is not a mapping or has no id).
    public sealed record Entry(YamlNode Node, IContentModel? Model, string? RawId);

    // The entries of the first document, and whether more documents follow (the loader reads only the first).
    public sealed record FileRead(List<Entry> Entries, bool ExtraDocuments);

    // Null when the text does not parse or is not a list. Items with no usable id are skipped.
    public static List<IContentModel>? ReadItems(ContentKind kind, string text)
    {
        return ReadEntries(kind, text)?.Entries.Where(e => e.Model is not null).Select(e => e.Model!).ToList();
    }

    // Null when the text does not parse or is not a list; every entry otherwise, in order, whether or not it can be modelled.
    public static FileRead? ReadEntries(ContentKind kind, string text)
    {
        YamlNode? root;
        bool extra;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            root = stream.Documents.Count == 0 ? null : stream.Documents[0].RootNode;
            extra = stream.Documents.Count > 1;
        }
        // The scanner throws InvalidOperationException for some malformed flow collections.
        catch (Exception e) when (e is YamlException or ArgumentException or InvalidOperationException)
        {
            return null;
        }

        // A blank or comment-only file is an empty list for editing purposes; the loader reports it separately.
        if (root is null || root is YamlScalarNode { Value: null or "" or "~" or "null" })
        {
            return new FileRead([], extra);
        }

        if (root is not YamlSequenceNode sequence)
        {
            return null;
        }

        var items = new List<Entry>();
        foreach (YamlNode node in sequence.Children)
        {
            IContentModel? item = node is YamlMappingNode map ? Item(kind, Map(map)) : null;
            item = item is { Id.Length: > 0 } ? item : null;
            items.Add(new Entry(node, item, node is YamlMappingNode raw ? Str(Get(Map(raw), "id")) : null));
        }

        return new FileRead(items, extra);
    }

    private static IContentModel? Item(ContentKind kind, Dictionary<string, YamlNode> m)
    {
        string? id = Str(Get(m, "id"));
        if (id is null)
        {
            return null;
        }

        string? name = Str(Get(m, "name"));
        switch (kind)
        {
            case ContentKind.Skill:
                return new SkillModel(id, name, Strings(Get(m, "tags")));
            case ContentKind.Gear:
                return new GearModel(id, name, Str(Get(m, "tag")) ?? "");
            case ContentKind.Crew:
                var skills = new Dictionary<string, int>();
                foreach ((string key, YamlNode value) in Map(Get(m, "skills")))
                {
                    skills[key] = Int(value) ?? 0;
                }

                return new CrewModel(id, name, Int(Get(m, "seniority")) ?? 0, skills, Int(Get(m, "caution")) ?? 0, Int(Get(m, "discipline")) ?? 0, Str(Get(m, "pronouns")));
            case ContentKind.Assignment:
                var offer = new Dictionary<int, int>();
                foreach ((string key, YamlNode value) in Map(Get(m, "offer")))
                {
                    if (YamlInt(key) is { } count)
                    {
                        offer[count] = Int(value) ?? 0;
                    }
                }

                return new AssignmentModel(id, name, Strings(Get(Map(Get(m, "objectives")), "tags")), offer, Int(Get(m, "loadoutLimit")) ?? 0);
            case ContentKind.Objective:
                (int teamMin, int teamMax) = Range(Get(m, "teamSize"));
                var stages = new List<StageModel>();
                foreach (YamlNode stage in Sequence(Get(m, "stages")))
                {
                    Dictionary<string, YamlNode> s = Map(stage);
                    (int countMin, int countMax) = Range(Get(s, "count"));
                    stages.Add(new StageModel(Str(Get(s, "slot")) ?? "", Strings(Get(Map(Get(s, "draw")), "tags")), countMin, countMax, YamlBool(Str(Get(s, "skippable")))));
                }

                return new ObjectiveModel(id, name, Strings(Get(m, "tags")), teamMin, teamMax, Int(Get(m, "need")) ?? 0, Int(Get(Map(Get(m, "ifUnattended")), "difficulty")), stages, Texts(Get(m, "text")), OwnLines(Get(m, "lines")));
            case ContentKind.Encounter:
                DecisionModel? decision = null;
                if (Get(m, "decision") is YamlMappingNode d)
                {
                    Dictionary<string, YamlNode> dm = Map(d);
                    decision = new DecisionModel(Str(Get(dm, "id")) ?? "", Str(Get(dm, "name")), Int(Get(dm, "stakes")) ?? 0, Effects(Get(dm, "bold")), Effects(Get(dm, "safe")), Texts(Get(dm, "text")), Texts(Get(dm, "labels")), Int(Get(Map(Get(dm, "chronicle")), "weight")));
                }

                return new EncounterModel(id, name, Strings(Get(m, "tags")), YamlBool(Str(Get(m, "commsBlocked"))), Str(Get(m, "chain")), Sequence(Get(m, "approaches")).Select(Approach).ToList(), decision, Texts(Get(m, "text")), OwnLines(Get(m, "lines")), Str(Get(m, "notes")));
            case ContentKind.Line:
                OwnLineModel line = OwnLine(m);
                return new LineModel(id, name, line.Beat, line.When, line.Text);
            case ContentKind.Symbol:
                return new SymbolModel(id, name, Sequence(Get(m, "options")).Select(o => Str(o) ?? "").ToList());
            case ContentKind.Pronoun:
                return new PronounModel(id, name, Str(Get(m, "subject")) ?? "", Str(Get(m, "object")) ?? "", Str(Get(m, "possessive")) ?? "", Str(Get(m, "possessivePronoun")) ?? "", Str(Get(m, "reflexive")) ?? "", YamlBool(Str(Get(m, "plural"))));
            case ContentKind.Storylet:
                Dictionary<string, YamlNode> cooldown = Map(Get(m, "cooldown"));
                return new StoryletModel(id, name, Int(Get(m, "stakes")) ?? 0, Str(Get(m, "requires")), Cast(Get(m, "cast")), Sequence(Get(m, "options")).Select(Option).ToList(),
                    Int(Get(cooldown, "cast")), Int(Get(cooldown, "global")), Str(Get(m, "text")), Chronicle(Get(m, "chronicle")), Str(Get(m, "notes")));
            case ContentKind.Vignette:
                Dictionary<string, YamlNode> wait = Map(Get(m, "cooldown"));
                return new VignetteModel(id, name, Str(Get(m, "requires")), Cast(Get(m, "cast")), StoryletEffects(Get(m, "effects")), Sequence(Get(m, "options")).Select(Option).ToList(), Str(Get(m, "default")),
                    Int(Get(wait, "cast")), Int(Get(wait, "global")), Str(Get(m, "text")), Str(Get(m, "notes")));
            case ContentKind.Trait:
                return Trait(id, name, m);
            case ContentKind.TestShip:
                return TestShip(id, name, m);
            default:
                return null;
        }
    }

    private static TraitModel Trait(string id, string? name, Dictionary<string, YamlNode> m)
    {
        Dictionary<int, string>? levels = null;
        if (Get(m, "levels") is YamlMappingNode)
        {
            levels = [];
            foreach ((string key, YamlNode value) in Map(Get(m, "levels")))
            {
                if (YamlInt(key) is { } level)
                {
                    levels[level] = Str(value) ?? "";
                }
            }
        }

        TraitLaunchModel? launch = null;
        if (Get(m, "launch") is YamlMappingNode)
        {
            Dictionary<string, YamlNode> l = Map(Get(m, "launch"));
            launch = new TraitLaunchModel(StoryletEffects(Get(l, "sent")), StoryletEffects(Get(l, "notSent")), StoryletEffects(Get(l, "withTarget")));
        }

        TraitMissionModel? mission = null;
        if (Get(m, "mission") is YamlMappingNode)
        {
            Dictionary<string, YamlNode> b = Map(Get(m, "mission"));
            mission = new TraitMissionModel(Int(Get(b, "checkBonus")) ?? 0, YamlBool(Str(Get(b, "withTarget"))));
        }

        TraitStepModel? every = null;
        if (Get(m, "every") is YamlMappingNode)
        {
            Dictionary<string, YamlNode> e = Map(Get(m, "every"));
            every = new TraitStepModel(Int(Get(e, "weeks")) ?? 0, Int(Get(e, "add")) ?? 0);
        }

        int? expires = Get(m, "expires") is YamlMappingNode ? Int(Get(Map(Get(m, "expires")), "weeks")) ?? 0 : null;
        return new TraitModel(id, name, IsBool(Str(Get(m, "shown"))), Str(Get(m, "on")), levels, Int(Get(m, "max")), Str(Get(m, "briefing")), launch, mission, every, expires, StoryletEffects(Get(m, "onTargetDied")), Str(Get(m, "notes")));
    }

    private static TestShipModel TestShip(string id, string? name, Dictionary<string, YamlNode> m)
    {
        var crew = Sequence(Get(m, "crew")).Select(Map).Select(c =>
        {
            Dictionary<string, int>? skills = Get(c, "skills") is YamlMappingNode ? Map(Get(c, "skills")).ToDictionary(s => s.Key, s => Int(s.Value) ?? 0) : null;
            return new TestShipCrewModel(Str(Get(c, "id")) ?? "", Int(Get(c, "morale")), Int(Get(c, "loyalty")), skills, IsBool(Str(Get(c, "fit"))), IsBool(Str(Get(c, "aboard"))), IsBool(Str(Get(c, "alive"))));
        }).ToList();
        var traits = Sequence(Get(m, "traits")).Select(Map)
            .Select(t => new TestShipTraitModel(Str(Get(t, "trait")) ?? "", Str(Get(t, "on")), Str(Get(t, "toward")), Str(Get(t, "value")), Int(Get(t, "daysAgo")), Int(Get(t, "cyclesAgo")))).ToList();
        var history = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>>();
        foreach ((string crewId, YamlNode kinds) in Map(Get(m, "history")))
        {
            history[crewId] = Map(kinds).ToDictionary(k => k.Key, k => (IReadOnlyList<int>)Sequence(k.Value).Select(a => Int(a) ?? 0).ToList());
        }

        var chronicle = Sequence(Get(m, "chronicle")).Select(Map).Select(e => new TestShipEntryModel(Str(Get(e, "kind")) ?? "", Int(Get(e, "daysAgo")), Strings(Get(e, "subjects")))).ToList();
        var storylets = Sequence(Get(m, "storylets")).Select(Map).Select(a => new TestShipArrivalModel(Str(Get(a, "id")) ?? "", Int(Get(a, "daysAgo")), Int(Get(a, "cyclesAgo")))).ToList();
        return new TestShipModel(id, name, Int(Get(m, "date")), Int(Get(m, "cycle")), crew, traits, history, chronicle, storylets, Str(Get(m, "notes")));
    }

    // Each slot to its expression, in the order written.
    private static List<CastSlotModel> Cast(YamlNode? node)
    {
        return Map(node).Select(c => new CastSlotModel(c.Key, Str(c.Value) ?? "")).ToList();
    }

    private static StoryletOptionModel Option(YamlNode node)
    {
        Dictionary<string, YamlNode> o = Map(node);
        StoryletCheckModel? check = null;
        if (Get(o, "check") is YamlMappingNode)
        {
            Dictionary<string, YamlNode> c = Map(Get(o, "check"));
            check = new StoryletCheckModel(Str(Get(c, "skill")) ?? "", Int(Get(c, "difficulty")) ?? 0, Outcome(Get(o, "success")), Outcome(Get(o, "failure")));
        }

        return new StoryletOptionModel(Str(Get(o, "id")) ?? "", Str(Get(o, "label")) ?? "", Str(Get(o, "requires")), StoryletEffects(Get(o, "effects")), check, Str(Get(o, "text")), Chronicle(Get(o, "chronicle")));
    }

    private static StoryletOutcomeModel? Outcome(YamlNode? node)
    {
        if (node is not YamlMappingNode)
        {
            return null;
        }

        Dictionary<string, YamlNode> o = Map(node);
        return new StoryletOutcomeModel(StoryletEffects(Get(o, "effects")), Str(Get(o, "text")));
    }

    private static ChronicleModel? Chronicle(YamlNode? node)
    {
        if (node is not YamlMappingNode)
        {
            return null;
        }

        Dictionary<string, YamlNode> c = Map(node);
        return new ChronicleModel(Int(Get(c, "weight")), Str(Get(c, "text")));
    }

    // Loyalty or morale first, as the loader looks for them; a trait change is any entry with trait, add, or set.
    private static List<StoryletEffectModel> StoryletEffects(YamlNode? node)
    {
        var effects = new List<StoryletEffectModel>();
        foreach (YamlNode entry in Sequence(node))
        {
            Dictionary<string, YamlNode> e = Map(entry);
            string? stat = new[] { "loyalty", "morale" }.FirstOrDefault(e.ContainsKey);
            if (stat is not null)
            {
                effects.Add(new StoryletEffectModel(stat, RawInt(Get(e, stat)), On: Str(Get(e, "on")), Toward: Str(Get(e, "toward"))));
                continue;
            }

            bool set = e.ContainsKey("set");
            string? amount = Str(Get(e, set ? "set" : "add"));
            int? number = RawInt(amount);
            bool any = e.ContainsKey("trait") || set || e.ContainsKey("add");
            effects.Add(new StoryletEffectModel(any ? "trait" : "", number, Str(Get(e, "trait")), set, number is null ? amount : null, Str(Get(e, "on")), Str(Get(e, "toward"))));
        }

        return effects;
    }

    private static OwnLineModel OwnLine(Dictionary<string, YamlNode> m)
    {
        Dictionary<string, YamlNode> when = Map(Get(m, "when"));
        return new OwnLineModel(Str(Get(when, "beat")) ?? "", when.Where(w => w.Key != "beat").Select(w => Criterion(w.Key, w.Value)).ToList(), Str(Get(m, "text")) ?? "");
    }

    // The lines written inside an item, or null when it has none.
    private static List<OwnLineModel>? OwnLines(YamlNode? node)
    {
        List<OwnLineModel> lines = Sequence(node).Select(n => OwnLine(Map(n))).ToList();
        return lines.Count == 0 ? null : lines;
    }

    private static CriterionModel Criterion(string fact, YamlNode value)
    {
        if (value is YamlMappingNode)
        {
            Dictionary<string, YamlNode> range = Map(value);
            return new CriterionModel(fact, [], false, RawInt(Get(range, "min")), RawInt(Get(range, "max")));
        }

        if (value is YamlSequenceNode)
        {
            return new CriterionModel(fact, Strings(value), true);
        }

        return new CriterionModel(fact, Str(value) is { } single ? [single] : []);
    }

    private static ApproachModel Approach(YamlNode? node)
    {
        Dictionary<string, YamlNode> a = Map(node);
        return new ApproachModel(Str(Get(a, "id")) ?? "", Str(Get(a, "name")), Str(Get(a, "skill")) ?? "", Int(Get(a, "difficulty")) ?? 0, Str(Get(a, "gearTag")), Requirements(Get(a, "requires")), Outcomes(Get(a, "outcomes")), Texts(Get(a, "text")));
    }

    private static List<RequirementModel> Requirements(YamlNode? node)
    {
        return Sequence(node).Select(Map).Select(r => new RequirementModel(
            Str(Get(r, "skill")), Int(Get(r, "level")), Str(Get(r, "gear")),
            Str(Get(r, "trait")), Int(Get(r, "min")), Int(Get(r, "max")), Str(Get(r, "is")), Str(Get(r, "on")))).ToList();
    }

    // In the order written; a tier listed with no entries is kept, empty.
    private static Dictionary<string, IReadOnlyList<EffectModel>> Outcomes(YamlNode? node)
    {
        return Map(node).ToDictionary(o => o.Key, o => (IReadOnlyList<EffectModel>)Effects(o.Value));
    }

    // An item's own words by key, or null when it has none. A key with no value is not kept.
    private static Dictionary<string, string>? Texts(YamlNode? node)
    {
        var texts = new Dictionary<string, string>();
        foreach ((string key, YamlNode value) in Map(node))
        {
            if (Str(value) is { } text)
            {
                texts[key] = text;
            }
        }

        return texts.Count == 0 ? null : texts;
    }

    private static List<EffectModel> Effects(YamlNode? node)
    {
        var effects = new List<EffectModel>();
        foreach (YamlNode entry in Sequence(node))
        {
            Dictionary<string, YamlNode> e = Map(entry);
            string? injury = Str(Get(e, "injuryRoll"));
            bool set = e.ContainsKey("set");
            effects.Add(new EffectModel(
                RawInt(Get(e, "progress")), RawInt(Get(e, "momentum")), RawInt(Get(e, "strain")), injury, Str(Get(e, "withdraw")) == "true",
                Str(Get(e, "trait")), RawInt(Get(e, set ? "set" : "add")), set, Str(Get(e, "on")), Str(Get(e, "toward"))));
        }

        return effects;
    }

    // A count written as one number or as [min, max].
    private static (int Min, int Max) Range(YamlNode? node)
    {
        if (node is YamlSequenceNode { Children.Count: 2 } pair)
        {
            return (RawInt(pair.Children[0]) ?? 0, RawInt(pair.Children[1]) ?? 0);
        }

        int? single = RawInt(node);
        return (single ?? 0, single ?? 0);
    }

    private static Dictionary<string, YamlNode> Map(YamlNode? node)
    {
        var map = new Dictionary<string, YamlNode>();
        if (node is YamlMappingNode mapping)
        {
            foreach ((YamlNode key, YamlNode value) in mapping.Children)
            {
                if (key is YamlScalarNode { Value: { } k })
                {
                    map[k] = value;
                }
            }
        }

        return map;
    }

    private static YamlNode? Get(Dictionary<string, YamlNode> map, string key) => map.GetValueOrDefault(key);

    private static IEnumerable<YamlNode> Sequence(YamlNode? node) => node is YamlSequenceNode s ? s.Children : [];

    private static List<string> Strings(YamlNode? node) => Sequence(node).Select(Str).OfType<string>().ToList();

    // An unquoted null, ~, or empty value is no value; a quoted one is a string.
    private static string? Str(YamlNode? node)
    {
        if (node is not YamlScalarNode scalar)
        {
            return null;
        }

        return scalar.Style == ScalarStyle.Plain && scalar.Value is null or "" or "~" or "null" or "Null" or "NULL" ? null : scalar.Value;
    }

    private static int? Int(YamlNode? node) => YamlInt(Str(node));

    private static readonly IDeserializer IntReader = new DeserializerBuilder().Build();

    // An integer exactly as the loader's deserialiser reads one (0x10, 1_0, +5, and 0010 as octal included), or null.
    public static int? YamlInt(string? text)
    {
        if (text is null || !IntShape.IsMatch(text))
        {
            return null;
        }

        try
        {
            return IntReader.Deserialize<int>(text);
        }
        catch (Exception e) when (e is YamlException or FormatException or OverflowException or InvalidCastException)
        {
            return null;
        }
    }

    private static readonly System.Text.RegularExpressions.Regex IntShape = new("^[+-]?[0-9][0-9A-Za-z_]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    // The loader reads effect values, teamSize, and a stage's count from the raw text with int.TryParse, not as YAML integers.
    public static int? RawInt(YamlNode? node) => RawInt(Str(node));

    public static int? RawInt(string? text) => int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : null;

    private static readonly System.Text.RegularExpressions.Regex BoolShape = new("^[A-Za-z]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    // A flag exactly as the loader's deserialiser reads a bool property (skippable, commsBlocked); false when it is not one.
    public static bool YamlBool(string? text) => IsBool(text) ?? false;

    // The value the loader's deserialiser reads for a bool property, or null when it reads none.
    public static bool? IsBool(string? text)
    {
        if (text is null || !BoolShape.IsMatch(text))
        {
            return null;
        }

        try
        {
            return IntReader.Deserialize<bool>(text);
        }
        catch (Exception e) when (e is YamlException or FormatException or InvalidCastException)
        {
            return null;
        }
    }
}
