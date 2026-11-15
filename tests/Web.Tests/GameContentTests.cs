using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;
using Web.Bridge;

namespace Web.Tests;

// The game's own pack in content/, played: its words must stand up in real missions, not only pass the loader.
public sealed class GameContentTests
{
    private static ContentPack Load()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        string root = directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        ContentLoadResult result = ContentLoader.LoadDirectory(Path.Combine(root, "content"));
        return result.Pack ?? throw new InvalidOperationException("The game's content did not load: " + string.Join(" | ", result.Errors.Select(e => $"{e.File}: {e.Message}")));
    }

    // Every beat of every mission reads as authored prose: no line of the pack fails to expand and drops a beat to the systems line.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_beat_of_the_games_missions_reads_as_authored_prose(bool wide)
    {
        ContentPack pack = wide ? BridgePack.Wide(Load()) : Load();
        int missions = 0;
        var used = new HashSet<string>();
        var dump = new System.Text.StringBuilder();
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var played = new Played(pack, seed);
            if (!played.StepUntil(() => played.State.Phase == CyclePhase.Aftermath && played.Mission?.State.Result is not null))
            {
                continue;
            }

            missions++;
            BridgeView view = BridgeReader.Read(played.Record(), played.Pack, played.CycleEvents());
            dump.AppendLine().AppendLine($"=== seed {seed}{(wide ? " (wide dice)" : "")}: {view.Mission!.Outcome}");
            foreach (LogLine line in view.Mission!.Log)
            {
                Assert.True(line.Authored, $"Seed {seed}, beat {line.Index} ({line.Kind}) fell back to the systems line: {line.Text}");
                dump.AppendLine($"[{line.Kind}]");
                foreach (ProseEntry entry in line.Script!)
                {
                    string words = string.Concat(entry.Parts.Select(p => p.Text));
                    dump.AppendLine(entry.Kind switch { Sim.Core.Narrative.EntryKind.Speech => $"    {entry.Speaker}: {words}", Sim.Core.Narrative.EntryKind.Note => $"    <{words}>", _ => $"    {words}" });
                }
            }

            dump.AppendLine("--- report");
            foreach (IReadOnlyList<LinePart> sentence in view.Aftermath!.Report!.Sentences)
            {
                dump.AppendLine("    " + string.Concat(sentence.Select(p => p.Text)));
            }

            Assert.InRange(view.Aftermath!.Report!.Sentences.Count, 1, Report.Most);
        }

        Assert.True(missions >= 20, $"Only {missions} missions ended.");
        if (Environment.GetEnvironmentVariable("PLAYS_DUMP") is { Length: > 0 } path)
        {
            File.AppendAllText(path, dump.ToString());
        }
    }
}
