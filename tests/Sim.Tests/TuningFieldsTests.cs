using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Sim.Content;
using Sim.Core.Content;

namespace Sim.Tests;

// Slice 1f, design 4.2 and 4.3: the form's fields are read from the tuning file's own text, and a save replaces their values in place.
public class TuningFieldsTests : IClassFixture<RepoContentGuard>
{
    private static readonly string GameTuning = File.ReadAllText(Path.Combine(TestContent.Root, "content", "tuning", "missions.yaml"));

    private static List<TuningField> Fields(string text) => TuningFields.Read(text).SelectMany(s => s.Fields).ToList();

    private static Dictionary<string, IReadOnlyList<string>> Values(string text) => Fields(text).ToDictionary(f => f.Path, f => f.Values);

    // The loader's own paths: on a file with none of them, each path it reads is reported missing.
    private static HashSet<string> PathsTheLoaderReads()
    {
        (_, _, IReadOnlyList<ContentError> errors) = ContentLoader.LoadTuningText("unread: 1\n");
        return errors.Select(e => e.Message).Where(m => m.EndsWith(" is missing.", StringComparison.Ordinal)).Select(m => m[..^" is missing.".Length]).ToHashSet();
    }

    private static IEnumerable<string> LoaderPaths(TuningField field) => field.Kind switch
    {
        TuningFieldKind.Dice => [$"{field.Path}.count", $"{field.Path}.sides"],
        TuningFieldKind.Range => [$"{field.Path}.0", $"{field.Path}.1"],
        _ => [field.Path],
    };

    [Fact]
    public void The_fields_are_every_key_the_loader_reads_and_no_other()
    {
        HashSet<string> read = PathsTheLoaderReads();

        Assert.NotEmpty(read);
        Assert.Equal(read.Order(StringComparer.Ordinal), Fields(GameTuning).SelectMany(LoaderPaths).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Fields_are_in_file_order_under_their_top_level_sections()
    {
        IReadOnlyList<TuningSection> sections = TuningFields.Read(GameTuning);

        Assert.Equal(
            ["check", "compounding", "develop", "calls", "experience", "injury", "result", "storylets", "salience", "vignettes", "captainsTime", "history", "bench"],
            sections.Select(s => s.Name));
        TuningSection check = sections[0];
        Assert.Equal(["dice", "target", "tiers.critical", "tiers.success", "tiers.partial", "tiers.failure", "gearBonus", "assistDivisor"], check.Fields.Select(f => f.Key));
        Assert.All(check.Fields, f => Assert.Equal("check", f.Section));
        Assert.Equal("check.tiers.critical", check.Fields[2].Path);

        List<TuningField> all = Fields(GameTuning);
        Assert.Equal(all.Select(f => f.Line).Order(), all.Select(f => f.Line));
        string[] lines = GameTuning.Split('\n');
        Assert.All(all, f => Assert.Contains(f.Key[(f.Key.LastIndexOf('.') + 1)..] + ":", lines[f.Line - 1]));
    }

    [Fact]
    public void Dice_and_ranges_are_one_field_each()
    {
        Dictionary<string, TuningField> fields = Fields(GameTuning).ToDictionary(f => f.Path);

        Assert.Equal(TuningFieldKind.Dice, fields["check.dice"].Kind);
        Assert.Equal(["2", "6"], fields["check.dice"].Values);
        Assert.Equal(TuningFieldKind.Range, fields["bench.teams.rookie.skill"].Kind);
        Assert.Equal(["1", "3"], fields["bench.teams.rookie.skill"].Values);
        Assert.Equal("teams.rookie.skill", fields["bench.teams.rookie.skill"].Key);
        Assert.Equal(TuningFieldKind.Number, fields["calls.cautionWeight"].Kind);
        Assert.Equal(["0.5"], fields["calls.cautionWeight"].Values);
    }

    [Fact]
    public void A_fields_hint_is_the_comment_on_its_line_and_a_continuation_joins_the_field_above()
    {
        Dictionary<string, TuningField> fields = Fields(GameTuning).ToDictionary(f => f.Path);

        Assert.Equal("added to an approach's difficulty", fields["check.target"].Hint);
        Assert.Equal("every check rolls the sum of count dice of this many sides", fields["check.dice"].Hint);
        Assert.Equal("the lead prefers bold when priority * boldPriorityWeight exceeds caution * boldCautionWeight", fields["calls.boldCautionWeight"].Hint);
        Assert.Null(fields["calls.boldPriorityWeight"].Hint);
        Assert.Equal(
            "the senior carries the junior's check at the best-qualified score minus this and minus one more for every this many levels between senior and junior",
            fields["develop.supervisorPenalty"].Hint);
        Assert.Null(fields["develop.supervisorGapDivisor"].Hint);
        Assert.Null(fields["check.gearBonus"].Hint);
        // A nested map's comment ("tiers:   # ...") is the hint of the first field under it.
        Assert.Equal("the lowest margin for each tier; below failure is a disaster", fields["check.tiers.critical"].Hint);
        Assert.Null(fields["check.tiers.success"].Hint);
    }

    [Fact]
    public void A_sections_note_is_the_comment_on_its_line_and_the_files_leading_comment_is_nobodys()
    {
        Dictionary<string, TuningSection> sections = TuningFields.Read(GameTuning).ToDictionary(s => s.Name);

        Assert.Equal("1e part four: see docs/plans/2026-11-18-phase-1e-part-four-design.md section 4.5", sections["salience"].Note);
        Assert.Null(sections["check"].Note);
        Assert.Null(sections["bench"].Note);
        Assert.DoesNotContain(Fields(GameTuning), f => f.Hint?.Contains("Every number", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Comment_lines_above_a_section_are_its_note_and_above_a_field_its_hint()
    {
        string text = "# The file.\n\n# About a.\n# More about a.\na:   # and its line\n  # About x.\n  x: 1   # x's own\n  y: 2\nb: 3\n";

        IReadOnlyList<TuningSection> sections = TuningFields.Read(text);

        Assert.Equal("About a. More about a. and its line", sections[0].Note);
        Assert.Equal("About x. x's own", sections[0].Fields[0].Hint);
        Assert.Null(sections[0].Fields[1].Hint);
        Assert.Equal("b", sections[1].Name);
        TuningField b = sections[1].Fields.Single();
        Assert.Equal(("b", "b", TuningFieldKind.Number, null, 9), (b.Path, b.Key, b.Kind, b.Hint, b.Line));
        Assert.Equal(["3"], b.Values);
    }

    [Fact]
    public void Replacing_every_field_changes_only_its_scalars()
    {
        // Each value becomes a marker of a different width; putting the original values back over the markers gives the file exactly.
        List<TuningField> fields = Fields(GameTuning);
        var originals = new List<string>();
        var values = new Dictionary<string, IReadOnlyList<string>>();
        foreach (TuningField field in fields)
        {
            values[field.Path] = field.Values.Select(v =>
            {
                originals.Add(v);
                return $"Z{originals.Count - 1}Z";
            }).ToList();
        }

        string replaced = TuningFields.Replace(GameTuning, values);

        Assert.NotEqual(GameTuning, replaced);
        Assert.Equal(GameTuning, Regex.Replace(replaced, @"Z(\d+)Z", m => originals[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)]));
        Assert.Equal(values, Values(replaced));
    }

    [Fact]
    public void Every_field_changed_to_a_valid_value_is_read_back_by_the_loader()
    {
        // One more than each value keeps every order the loader checks (tiers falling, a threshold under the cap, a range low to high).
        static string More(string value) => (double.Parse(value, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
        var values = Fields(GameTuning).ToDictionary(f => f.Path, f => (IReadOnlyList<string>)f.Values.Select(More).ToList());

        string replaced = TuningFields.Replace(GameTuning, values);
        (MissionTuning? before, BenchTuning? benchBefore, _) = ContentLoader.LoadTuningText(GameTuning);
        (MissionTuning? after, BenchTuning? benchAfter, IReadOnlyList<ContentError> errors) = ContentLoader.LoadTuningText(replaced);

        Assert.Empty(errors);
        Assert.Equal(values, Values(replaced));
        foreach (PropertyInfo property in typeof(MissionTuning).GetProperties())
        {
            object? was = property.GetValue(before);
            object? now = property.GetValue(after);
            switch (was)
            {
                case int i:
                    Assert.Equal(i + 1, now);
                    break;
                case double d:
                    Assert.Equal(d + 1, now);
                    break;
                case DiceDef dice:
                    Assert.Equal(new DiceDef(dice.Count + 1, dice.Sides + 1), now);
                    break;
                default:
                    Assert.Fail($"MissionTuning.{property.Name} is a {property.PropertyType.Name}, which this test does not know.");
                    break;
            }
        }

        static BenchTeamDef Up(BenchTeamDef t) => new(t.Size + 1, t.SkillMin + 1, t.SkillMax + 1, t.Seniors + 1, t.SeniorSkillMin + 1, t.SeniorSkillMax + 1);
        Assert.Equal(new BenchTuning(Up(benchBefore!.Rookie), Up(benchBefore.Mixed), Up(benchBefore.Veteran)), benchAfter);
    }

    [Fact]
    public void A_wider_value_keeps_the_comment_on_its_line()
    {
        string replaced = TuningFields.Replace(GameTuning, new Dictionary<string, IReadOnlyList<string>> { ["check.target"] = ["12"] });

        string line = replaced.Split('\n').Single(l => l.StartsWith("  target:", StringComparison.Ordinal));
        Assert.Equal("  target: 12                  # added to an approach's difficulty", line);
        Assert.Equal(GameTuning.Split('\n').Length, replaced.Split('\n').Length);
    }

    [Fact]
    public void Dice_and_ranges_round_trip_in_their_flow_style()
    {
        string replaced = TuningFields.Replace(GameTuning, new Dictionary<string, IReadOnlyList<string>>
        {
            ["check.dice"] = ["3", "8"],
            ["bench.teams.rookie.skill"] = ["2", "4"],
        });

        Assert.Contains("  dice: { count: 3, sides: 8 }   # every check rolls", replaced, StringComparison.Ordinal);
        Assert.Contains("    rookie:  { size: 3, skill: [2, 4], seniors: 0, seniorSkill: [1, 3] }", replaced, StringComparison.Ordinal);
        Dictionary<string, IReadOnlyList<string>> read = Values(replaced);
        Assert.Equal(["3", "8"], read["check.dice"]);
        Assert.Equal(["2", "4"], read["bench.teams.rookie.skill"]);
        Assert.Equal(["2", "6"], read["injury.dice"]);
    }

    [Fact]
    public void A_file_with_windows_line_endings_is_replaced_in_place()
    {
        string text = "a:\r\n  x: 1   # one\r\n  y: { count: 2, sides: 6 }\r\n";

        string replaced = TuningFields.Replace(text, new Dictionary<string, IReadOnlyList<string>> { ["a.x"] = ["10"], ["a.y"] = ["3", "4"] });

        Assert.Equal("a:\r\n  x: 10   # one\r\n  y: { count: 3, sides: 4 }\r\n", replaced);
        Assert.Equal("one", TuningFields.Read(text)[0].Fields[0].Hint);
    }

    [Fact]
    public void A_path_that_is_not_a_field_throws()
    {
        Assert.Throws<ArgumentException>(() => TuningFields.Replace(GameTuning, new Dictionary<string, IReadOnlyList<string>> { ["check.nothing"] = ["1"] }));
        Assert.Throws<ArgumentException>(() => TuningFields.Replace(GameTuning, new Dictionary<string, IReadOnlyList<string>> { ["check.tiers"] = ["1"] }));
        Assert.Throws<ArgumentException>(() => TuningFields.Replace(GameTuning, new Dictionary<string, IReadOnlyList<string>> { ["check.dice"] = ["1"] }));
    }

    [Fact]
    public void A_value_that_would_not_stay_a_plain_scalar_throws()
    {
        Assert.Throws<ArgumentException>(() => TuningFields.Replace(GameTuning, new Dictionary<string, IReadOnlyList<string>> { ["check.target"] = ["7 # more"] }));
        Assert.Throws<ArgumentException>(() => TuningFields.Replace(GameTuning, new Dictionary<string, IReadOnlyList<string>> { ["check.target"] = ["[1, 2]"] }));
        Assert.Throws<ArgumentException>(() => TuningFields.Replace(GameTuning, new Dictionary<string, IReadOnlyList<string>> { ["check.target"] = ["7\n  extra: 1"] }));
        Assert.Throws<ArgumentException>(() => TuningFields.Replace(GameTuning, new Dictionary<string, IReadOnlyList<string>> { ["check.target"] = [""] }));
    }
}
