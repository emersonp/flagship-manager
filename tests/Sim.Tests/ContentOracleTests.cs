using System.Text.Json;
using System.Text.RegularExpressions;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;

namespace Sim.Tests;

// The loader is the oracle: a save of one item must never change what the loader says about the rest of the pack.
public partial class ContentOracleTests : IClassFixture<RepoContentGuard>
{
    private static readonly string[] Awkward =
    [
        "0010", "0x10", "0b11", "1_000", "+5", "-0x10", "5.0", "1e1", "\"5\"", "True", "TRUE", "yes", "on", "~", "Null", "", "[1, 0010]", "abc", "!!str 5",
    ];

    private static readonly string[] SkipKeys = ["id"];

    private static readonly (string Path, ContentKind Kind)[] Files =
    [
        ("skills/base.yaml", ContentKind.Skill),
        ("gear/base.yaml", ContentKind.Gear),
        ("crew/starting.yaml", ContentKind.Crew),
        ("assignments/base.yaml", ContentKind.Assignment),
        ("objectives/base.yaml", ContentKind.Objective),
        ("encounters/base.yaml", ContentKind.Encounter),
    ];

    private const string SecondAssignment = "- id: ASSIGNMENT_TEMPLATE_2\n  objectives: { tags: [] }\n  offer: { 1: 6, 2: 3 }\n  loadoutLimit: 2\n";

    [GeneratedRegex(@"(?<k>[A-Za-z]+): (?<v>\[[^\]]*\]|\{[^}]*\}|[^,}\n]+)")]
    private static partial Regex KeyValue();

    [GeneratedRegex(@"(?m)^- (?:\{ )?id: (?<id>[A-Z0-9_]+)")]
    private static partial Regex IdAt();

    [GeneratedRegex(@"Line \d+")]
    private static partial Regex LineNumber();

    // What the loader says about a set: the pack as JSON, and the errors with line numbers masked (a rewrite moves lines).
    private static string Verdict(ContentFileSet files)
    {
        ContentLoadResult result = files.Validate();
        string pack = result.Pack is null ? "null" : JsonSerializer.Serialize(result.Pack, SimJson.Options);
        return pack + "\n" + string.Join("\n", result.Errors.Select(e => e.File + "|" + LineNumber().Replace(e.Message, "Line N")));
    }

    private static object? ModelOf(ContentFileSet files, ContentKind kind, string id) => kind switch
    {
        ContentKind.Skill => ContentEdits.Get<SkillModel>(files, kind, id),
        ContentKind.Gear => ContentEdits.Get<GearModel>(files, kind, id),
        ContentKind.Crew => ContentEdits.Get<CrewModel>(files, kind, id),
        ContentKind.Assignment => ContentEdits.Get<AssignmentModel>(files, kind, id),
        ContentKind.Objective => ContentEdits.Get<ObjectiveModel>(files, kind, id),
        ContentKind.Encounter => ContentEdits.Get<EncounterModel>(files, kind, id),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    // Rule: if a save is allowed, the loader's verdict on the pack is the same after it as before.
    [Fact]
    public void A_save_the_check_allows_never_changes_the_loaders_verdict()
    {
        var repo = ContentFileSet.ReadDirectory(Path.Combine(TestContent.Root, "tests", "fixtures", "content"));
        var baseFiles = new Dictionary<string, string>(repo.Files) { ["assignments/base.yaml"] = repo.Files["assignments/base.yaml"] + SecondAssignment };
        int allowed = 0;
        int refused = 0;
        var failures = new List<string>();
        foreach ((string path, ContentKind kind) in Files)
        {
            string text = baseFiles[path];
            var seen = new HashSet<string>();
            foreach (Match match in KeyValue().Matches(text))
            {
                string key = match.Groups["k"].Value;
                if (SkipKeys.Contains(key) || !seen.Add(key))
                {
                    continue;
                }

                string owner = IdAt().Matches(text[..match.Index]).Last().Groups["id"].Value;
                string other = new ContentFileSet(baseFiles).Index().First(i => i.File == path && i.Id != owner).Id;
                foreach (string value in Awkward)
                {
                    var set = new Dictionary<string, string>(baseFiles)
                    {
                        [path] = text[..match.Groups["v"].Index] + value + text[(match.Groups["v"].Index + match.Groups["v"].Length)..],
                    };
                    var mutated = new ContentFileSet(set);
                    object? model = ModelOf(mutated, kind, other);
                    if (model is null)
                    {
                        continue;
                    }

                    ContentFileSet after;
                    try
                    {
                        after = ContentEdits.Upsert(mutated, kind, model);
                    }
                    catch (ContentLossException)
                    {
                        refused++;
                        continue;
                    }

                    allowed++;
                    if (Verdict(mutated) != Verdict(after))
                    {
                        failures.Add($"{path} {key}: {value} (saving {other})");
                    }
                }
            }
        }

        Assert.True(allowed > 100 && refused > 100, $"allowed {allowed}, refused {refused}");
        Assert.Empty(failures);
    }
}
