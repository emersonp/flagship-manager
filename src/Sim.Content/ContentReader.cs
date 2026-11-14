using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Sim.Content;

// Reads a list file into models without judging it: a value the loader would refuse is read as far as it goes, so a broken pack can still be opened.
internal static class ContentReader
{
    // One list entry as written, and the model read from it (null when it is not a mapping or has no id).
    public sealed record Entry(YamlNode Node, IContentModel? Model, string? RawId);

    // Null when the text does not parse or is not a list. Items with no usable id are skipped.
    public static List<IContentModel>? ReadItems(ContentKind kind, string text)
    {
        return ReadEntries(kind, text)?.Where(e => e.Model is not null).Select(e => e.Model!).ToList();
    }

    // Null when the text does not parse or is not a list; every entry otherwise, in order, whether or not it can be modelled.
    public static List<Entry>? ReadEntries(ContentKind kind, string text)
    {
        YamlNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            root = stream.Documents.Count == 0 ? null : stream.Documents[0].RootNode;
        }
        catch (Exception e) when (e is YamlException or ArgumentException)
        {
            return null;
        }

        // A blank or comment-only file is an empty list for editing purposes; the loader reports it separately.
        if (root is null || root is YamlScalarNode { Value: null or "" or "~" or "null" })
        {
            return [];
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

        return items;
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

                return new CrewModel(id, name, Int(Get(m, "seniority")) ?? 0, skills, Int(Get(m, "caution")) ?? 0, Int(Get(m, "discipline")) ?? 0);
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
                    stages.Add(new StageModel(Str(Get(s, "slot")) ?? "", Strings(Get(Map(Get(s, "draw")), "tags")), countMin, countMax, Bool(Get(s, "skippable"))));
                }

                return new ObjectiveModel(id, name, Strings(Get(m, "tags")), teamMin, teamMax, Int(Get(m, "need")) ?? 0, Int(Get(Map(Get(m, "ifUnattended")), "difficulty")), stages);
            case ContentKind.Encounter:
                Dictionary<string, YamlNode> approaches = Map(Get(m, "approaches"));
                DecisionModel? decision = null;
                if (Get(m, "decision") is YamlMappingNode d)
                {
                    Dictionary<string, YamlNode> dm = Map(d);
                    decision = new DecisionModel(Str(Get(dm, "id")) ?? "", Str(Get(dm, "name")), Int(Get(dm, "stakes")) ?? 0, Effects(Get(dm, "bold")), Effects(Get(dm, "safe")));
                }

                return new EncounterModel(id, name, Strings(Get(m, "tags")), Bool(Get(m, "commsBlocked")), Str(Get(m, "chain")), Approach(Get(approaches, "careful")), Approach(Get(approaches, "fast")), decision);
            default:
                return null;
        }
    }

    private static ApproachModel Approach(YamlNode? node)
    {
        Dictionary<string, YamlNode> a = Map(node);
        return new ApproachModel(Str(Get(a, "skill")) ?? "", Int(Get(a, "difficulty")) ?? 0, Str(Get(a, "gearTag")));
    }

    private static List<EffectModel> Effects(YamlNode? node)
    {
        var effects = new List<EffectModel>();
        foreach (YamlNode entry in Sequence(node))
        {
            Dictionary<string, YamlNode> e = Map(entry);
            string? injury = Str(Get(e, "injuryRoll"));
            effects.Add(new EffectModel(Int(Get(e, "progress")), Int(Get(e, "momentum")), Int(Get(e, "strain")), injury, Bool(Get(e, "withdraw"))));
        }

        return effects;
    }

    // A count written as one number or as [min, max].
    private static (int Min, int Max) Range(YamlNode? node)
    {
        if (node is YamlSequenceNode { Children.Count: 2 } pair)
        {
            return (Int(pair.Children[0]) ?? 0, Int(pair.Children[1]) ?? 0);
        }

        int? single = Int(node);
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

    private static bool Bool(YamlNode? node) => string.Equals(Str(node), "true", StringComparison.OrdinalIgnoreCase);
}
