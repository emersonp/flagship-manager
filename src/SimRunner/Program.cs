using System.Text.Json;
using Sim.Content;
using SimRunner;

if (args.Length == 0 || args[0] != "cycles")
{
    Console.Error.WriteLine("Usage: SimRunner cycles [--legacies N] [--cycles N] [--seed N] [--content DIR] [--out FILE]");
    return 2;
}

string Option(string name, string fallback)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}

ContentLoadResult content = ContentLoader.LoadDirectory(Option("--content", "content"));
if (content.Pack is null)
{
    foreach (ContentError error in content.Errors)
    {
        Console.Error.WriteLine($"{error.File}: {error.Message}");
    }

    return 1;
}

var options = new SimOptions(int.Parse(Option("--legacies", "200")), int.Parse(Option("--cycles", "500")), ulong.Parse(Option("--seed", "1")));
SimSummary summary = Simulation.Run(options);
string json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
string outFile = Option("--out", "");
if (outFile != "")
{
    File.WriteAllText(outFile, json);
}

Console.WriteLine($"Content {content.Pack.Id} v{content.Pack.Version}: {summary.Legacies.Count} legacies x {options.Cycles} cycles, {summary.Legacies.Sum(l => l.Events)} events. OK.");
return 0;
