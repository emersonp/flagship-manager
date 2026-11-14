# Phase 1, Slices 1a and 1b: Mission Resolver and Bench Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A headless mission resolver that reads its numbers from a tuning file, and a command-line bench that runs one mission thousands of times and compares two tuning files on the same seeds.

**Architecture:** Content definitions are domain types in `Sim.Core`; `Sim.Content` loads and validates them. `Sim.Missions` is a pure function pair, `Start` and `Advance`, over a self-contained, serialisable mission state. `MissionBench` (outside the pure core) generates teams, answers calls by policy, and gathers figures; `SimRunner` exposes it as `bench`.

**Tech Stack:** .NET 10, C#, xUnit 2, YamlDotNet 18.1.0, System.Text.Json, Microsoft.CodeAnalysis.BannedApiAnalyzers 3.3.4.

**Spec:** `docs/plans/2026-11-14-phase-1ab-design.md`, which argues from `docs/gdd.md` sections 3.4, 3.5, 4.1 to 4.7, and 10.1. Vocabulary from `GLOSSARY.md`.

**Verified:** every file in this plan was compiled and every test run on 2026-11-14 (.NET SDK 10.0.402). The five build tasks were replayed in order on a fresh copy of `main`; the build was clean and the tests passed after each: 41, 47, 78, 91, and 108 tests. The pinned values (the dice sequence in Task 1, the walkthrough seeds and golden hash in Task 3) come from that run. Task 3 was amended after its review (risk direction, dice keyed by encounter, chain targets never drawn, and the mentoring share), and the replay was run again on the amended code. If a pinned value does not match, the code has drifted from this plan; fix the code, not the constant.

**Built:** 2026-11-14. The whole-branch review then changed the loader, the bench report, and the command line (commit `477e4ec`: tuning cross-checks, the loader's slot check, bench rows by encounter position, and unknown gear tags rejected), ending at 126 tests. Where this plan and the repository differ, the repository is the record.

## Global Constraints

- Target framework `net10.0`; nullable enabled; warnings are errors. These are set once in `Directory.Build.props`; a csproj never repeats them.
- Every `if`, `else`, `for`, `foreach`, and `while` body is in braces on their own lines, even one-liners (enforced by `.editorconfig`, rule IDE0011).
- Comments are terse and explain why, never what. Each test has a one-line intent comment only where the name does not say it.
- Everything under `src/Sim.*` is pure: no database, no clock, and no ambient randomness. The banned-API analyser added in Task 1 fails the build otherwise.
- `Sim.*` projects reference only `Sim.Core`. Feature modules never reference each other. `MissionBench` references only `Sim.*` projects.
- Mission dice are keyed by encounter id, not by position: every roll is `RngStream.For(missionSeed, 0, key)`, with keys such as `check:ENCOUNTER_03`. Never draw two rolls from one stream where a different team could change how many are drawn.
- Every number the resolver uses comes from `MissionTuning`. No rule constant is written in resolver code, except the average of two dice (7).
- Enums are stored by name in JSON; an enum member that is stored is never renamed or removed.
- Use the terms in `GLOSSARY.md` (legacy, cycle, mission, away team, team lead, call, notice, check, momentum, strain, beat). Never "save", "turn", or "campaign".
- Creative content is the author's. Use obvious placeholders (`SKILL_A`, `CREW_01`, `ENCOUNTER_01`); never invent names, species, or story text.
- Commit messages are plain: no co-author trailer and no tool attribution of any kind.
- Prose uses the Oxford comma.
- Write files as UTF-8 without a byte-order mark and with a final newline.
- On this machine the SDK is at `~/.dotnet`. Run `export PATH=$HOME/.dotnet:$PATH` first.
- All commands run from the repository root, `~/repos/starship-sim`.

## File Structure

| Path | Responsibility |
|---|---|
| `Directory.Build.props`, `BannedSymbols.txt` | Shared build settings, and the APIs the pure core may not use |
| `src/Sim.Core/Content/ContentPack.cs` | The content pack and load-result records (moved from `Sim.Content`) |
| `src/Sim.Core/Content/MissionContent.cs` | Encounter, decision, effect, stage, and objective definitions |
| `src/Sim.Core/Content/MissionTuning.cs` | Every resolver number, and the bench's team shapes |
| `src/Sim.Content/ContentLoader.cs` | Manifest, skills, and the per-folder loading loop |
| `src/Sim.Content/ContentLoader.Missions.cs` | Encounters and objectives, with cross-reference checks |
| `src/Sim.Content/ContentLoader.Tuning.cs` | The tuning file: every value required, none unknown |
| `content/tuning/missions.yaml`, `content/encounters/base.yaml`, `content/objectives/base.yaml` | The starter pack (placeholders) |
| `src/Sim.Missions/MissionTypes.cs` | Inputs, outputs, and enums of the resolver contract |
| `src/Sim.Missions/MissionState.cs` | The mission in flight, and its JSON form |
| `src/Sim.Missions/Beats.cs` | The closed set of beat records |
| `src/Sim.Missions/MissionResolver.cs` | `Start` and `Advance` |
| `src/MissionBench/BenchOptions.cs`, `BenchFigures.cs` | What a bench is asked, and what it returns |
| `src/MissionBench/TeamGenerator.cs` | Generated teams at a named strength |
| `src/MissionBench/Bench.cs` | The run loop, call policies, and replay of one seed |
| `src/MissionBench/BenchReport.cs` | Plain-text tables and the beat-by-beat replay |
| `src/SimRunner/CommandLine.cs`, `Cli.cs`, `Program.cs` | Strict flag parsing, the `cycles` and `bench` commands, and exit codes |
| `tests/Sim.Tests/TestContent.cs`, `TestContent.Missions.cs` | The repository's pack, the benchmark crew, and a play-to-the-end helper |
| `tests/Sim.Tests/Mission*.cs`, `tests/MissionBench.Tests/`, `tests/SimRunner.Tests/CliTests.cs` | Tests |
| `check.sh` | The gate, now including a bench run |

---

### Task 1: Projects, the banned-API analyser, and the dice pin

**Files:**
- Create: `BannedSymbols.txt`, `src/Sim.Missions/Sim.Missions.csproj`, `src/MissionBench/MissionBench.csproj`, `tests/MissionBench.Tests/MissionBench.Tests.csproj`
- Modify: `Directory.Build.props`, `StarshipSim.slnx`, and every existing `.csproj` (replace each whole file)
- Modify: `tests/Sim.Tests/ArchitectureTests.cs` (replace), `tests/Sim.Tests/RngStreamTests.cs` (add one test)

**Interfaces:**
- Consumes: the Phase 0 solution on `main`.
- Produces: empty projects `Sim.Missions` (references `Sim.Core`), `MissionBench` (references `Sim.Core` and `Sim.Missions`), and `MissionBench.Tests`; `SimRunner` and `Sim.Tests` gain references to the new projects; RS0030 build errors for banned APIs under `src/Sim.*`.

- [ ] **Step 1: Pin the dice mapping (a test that passes at once: it guards existing behaviour)**

In `tests/Sim.Tests/RngStreamTests.cs`, add this test directly above `Dice_stay_in_range`:

```csharp
    // The mapping from raw draws to dice is part of every golden seed.
    [Fact]
    public void Dice_mapping_is_pinned()
    {
        RngStream dice = RngStream.For(1, 1, "golden");
        RngStream picks = RngStream.For(1, 1, "golden");

        Assert.Equal(new[] { 2, 6, 1, 1, 1, 4, 6, 6 }, Enumerable.Range(0, 8).Select(_ => dice.D6()));
        Assert.Equal(new[] { 29, 91, 6, 16, 3 }, Enumerable.Range(0, 5).Select(_ => picks.Next(100)));
    }
```

Run: `dotnet test tests/Sim.Tests --filter Dice_mapping_is_pinned`
Expected: PASS. If it fails, stop and report: the pinned values are wrong or `RngStream` has changed.

- [ ] **Step 2: Replace the shared build file and add the banned list**

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>

  <!-- The pure core: no clock, no database, and no ambient randomness (GDD 7.1). -->
  <ItemGroup Condition="$(MSBuildProjectName.StartsWith('Sim.')) And !$(MSBuildProjectName.EndsWith('.Tests'))">
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="3.3.4" PrivateAssets="all" />
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt" />
  </ItemGroup>
</Project>
```

`BannedSymbols.txt`:

```text
T:System.Random;Use RngStream, so a seed replays.
T:System.Security.Cryptography.RandomNumberGenerator;Use RngStream, so a seed replays.
M:System.Guid.NewGuid;Derive ids from the seed.
P:System.DateTime.Now;The simulation has no clock. Time is the cycle.
P:System.DateTime.UtcNow;The simulation has no clock. Time is the cycle.
P:System.DateTime.Today;The simulation has no clock. Time is the cycle.
P:System.DateTimeOffset.Now;The simulation has no clock. Time is the cycle.
P:System.DateTimeOffset.UtcNow;The simulation has no clock. Time is the cycle.
T:System.TimeProvider;The simulation has no clock. Time is the cycle.
T:System.Diagnostics.Stopwatch;The simulation has no clock. Time is the cycle.
P:System.Environment.TickCount;The simulation has no clock. Time is the cycle.
P:System.Environment.TickCount64;The simulation has no clock. Time is the cycle.
M:System.Threading.Thread.Sleep(System.Int32);The simulation never waits.
N:System.Data;Storage belongs to Persistence.
N:Microsoft.Data.Sqlite;Storage belongs to Persistence.
```


- [ ] **Step 3: Write every project file and the solution file**

Replace each existing file whole, and create the three new ones. No csproj repeats `TargetFramework`, `ImplicitUsings`, or `Nullable`.

`src/MissionBench/MissionBench.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\Sim.Core\Sim.Core.csproj" />
    <ProjectReference Include="..\Sim.Missions\Sim.Missions.csproj" />
  </ItemGroup>

</Project>
```

`src/Persistence/Persistence.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\Sim.Core\Sim.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="11.0.0" />
  </ItemGroup>

</Project>
```

`src/Sim.Content/Sim.Content.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\Sim.Core\Sim.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="YamlDotNet" Version="18.1.0" />
  </ItemGroup>

</Project>
```

`src/Sim.Core/Sim.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

</Project>
```

`src/Sim.Cycle/Sim.Cycle.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\Sim.Core\Sim.Core.csproj" />
  </ItemGroup>

</Project>
```

`src/Sim.Missions/Sim.Missions.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\Sim.Core\Sim.Core.csproj" />
  </ItemGroup>

</Project>
```

`src/SimRunner/SimRunner.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\Sim.Core\Sim.Core.csproj" />
    <ProjectReference Include="..\Sim.Cycle\Sim.Cycle.csproj" />
    <ProjectReference Include="..\Sim.Content\Sim.Content.csproj" />
    <ProjectReference Include="..\Sim.Missions\Sim.Missions.csproj" />
    <ProjectReference Include="..\MissionBench\MissionBench.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

</Project>
```

`tests/MissionBench.Tests/MissionBench.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\MissionBench\MissionBench.csproj" />
    <ProjectReference Include="..\..\src\Sim.Core\Sim.Core.csproj" />
    <ProjectReference Include="..\..\src\Sim.Missions\Sim.Missions.csproj" />
    <ProjectReference Include="..\..\src\Sim.Content\Sim.Content.csproj" />
  </ItemGroup>

</Project>
```

`tests/Persistence.Tests/Persistence.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Persistence\Persistence.csproj" />
    <ProjectReference Include="..\..\src\Sim.Core\Sim.Core.csproj" />
  </ItemGroup>

</Project>
```

`tests/Sim.Tests/Sim.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Sim.Core\Sim.Core.csproj" />
    <ProjectReference Include="..\..\src\Sim.Cycle\Sim.Cycle.csproj" />
    <ProjectReference Include="..\..\src\Sim.Content\Sim.Content.csproj" />
    <ProjectReference Include="..\..\src\Sim.Missions\Sim.Missions.csproj" />
  </ItemGroup>

</Project>
```

`tests/SimRunner.Tests/SimRunner.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\SimRunner\SimRunner.csproj" />
  </ItemGroup>

</Project>
```

`StarshipSim.slnx`:

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/MissionBench/MissionBench.csproj" />
    <Project Path="src/Persistence/Persistence.csproj" />
    <Project Path="src/Sim.Content/Sim.Content.csproj" />
    <Project Path="src/Sim.Core/Sim.Core.csproj" />
    <Project Path="src/Sim.Cycle/Sim.Cycle.csproj" />
    <Project Path="src/Sim.Missions/Sim.Missions.csproj" />
    <Project Path="src/SimRunner/SimRunner.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/MissionBench.Tests/MissionBench.Tests.csproj" />
    <Project Path="tests/Persistence.Tests/Persistence.Tests.csproj" />
    <Project Path="tests/Sim.Tests/Sim.Tests.csproj" />
    <Project Path="tests/SimRunner.Tests/SimRunner.Tests.csproj" />
  </Folder>
</Solution>
```


- [ ] **Step 4: Prove the analyser can fail the build**

Create `src/Sim.Cycle/Bad.cs` with this one line:

```csharp
namespace Sim.Cycle; public static class Bad { public static Guid Id() { return Guid.NewGuid(); } }
```

Run: `dotnet build --nologo -v quiet`
Expected: FAIL with `error RS0030: The symbol 'Guid.NewGuid()' is banned in this project: Derive ids from the seed.`

Delete `src/Sim.Cycle/Bad.cs`. Paste the failing line into your report.

- [ ] **Step 5: Replace the architecture tests**

The substring scan is gone; the analyser does that job at build time.

`tests/Sim.Tests/ArchitectureTests.cs`:

```csharp
using System.Xml.Linq;

namespace Sim.Tests;

// GDD 7.1: the rules that let a new mechanic be added without rewriting old modules.
public class ArchitectureTests
{
    private static readonly string Root = FindRoot();

    public static TheoryData<string> SimProjects()
    {
        var data = new TheoryData<string>();
        foreach (string directory in Directory.GetDirectories(Path.Combine(Root, "src"), "Sim.*"))
        {
            data.Add(Path.GetFileName(directory));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SimProjects))]
    public void Sim_projects_reference_only_Sim_Core(string project)
    {
        Assert.All(References(project), reference => Assert.Equal("Sim.Core", reference));
    }

    [Fact]
    public void Persistence_references_only_Sim_Core()
    {
        Assert.Equal(new[] { "Sim.Core" }, References("Persistence"));
    }

    [Fact]
    public void The_bench_references_only_the_pure_core()
    {
        Assert.All(References("MissionBench"), reference => Assert.StartsWith("Sim.", reference, StringComparison.Ordinal));
    }

    // The analyser fails the build on a clock, a database, or ambient randomness under src/Sim.*. This guards its wiring.
    [Fact]
    public void The_pure_core_is_built_with_the_banned_api_list()
    {
        string props = File.ReadAllText(Path.Combine(Root, "Directory.Build.props"));
        string[] banned = File.ReadAllLines(Path.Combine(Root, "BannedSymbols.txt"));

        Assert.Contains("Microsoft.CodeAnalysis.BannedApiAnalyzers", props, StringComparison.Ordinal);
        Assert.Contains("StartsWith('Sim.')", props, StringComparison.Ordinal);
        Assert.Contains(banned, line => line.StartsWith("T:System.Random;", StringComparison.Ordinal));
        Assert.Contains(banned, line => line.StartsWith("P:System.DateTime.UtcNow;", StringComparison.Ordinal));
        Assert.Contains(banned, line => line.StartsWith("N:Microsoft.Data.Sqlite;", StringComparison.Ordinal));
    }

    private static IEnumerable<string> References(string project)
    {
        XDocument csproj = XDocument.Load(Path.Combine(Root, "src", project, project + ".csproj"));
        return csproj.Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value.Replace('\\', '/')));
    }

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
```


- [ ] **Step 6: Build and test**

Run: `dotnet build --nologo -v quiet && dotnet test --nologo --no-build -v quiet`
Expected: build with 0 warnings and 0 errors; 41 tests pass (Sim.Tests 33, Persistence.Tests 6, and SimRunner.Tests 2). `MissionBench.Tests` has no tests yet and may print "No test is available".

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Add mission projects, ban impure APIs in the core, and pin the dice mapping"
```

---

### Task 2: Mission content and the tuning file

**Files:**
- Create: `src/Sim.Core/Content/ContentPack.cs`, `MissionContent.cs`, `MissionTuning.cs`
- Delete: `src/Sim.Content/ContentPack.cs`
- Modify: `src/Sim.Content/ContentLoader.cs` (replace); Create: `ContentLoader.Missions.cs`, `ContentLoader.Tuning.cs`
- Create: `content/tuning/missions.yaml`, `content/encounters/base.yaml`, `content/objectives/base.yaml`
- Modify: `src/SimRunner/Program.cs` (one `using`)
- Test: `tests/Sim.Tests/TestContent.cs` (create), `ContentLoaderTests.cs` (replace), `MissionContentTests.cs` (create)

**Interfaces:**
- Consumes: `ContentLoader.Load`, `LoadDirectory`, and the `skills/` folder from Phase 0.
- Produces, in namespace `Sim.Core.Content`: `SkillDef`, `ContentPack(Id, Version, Skills, Encounters, Objectives, Tuning, Bench)`, `ContentError`, `ContentLoadResult`, `InjurySeverity { Light, Severe }`, `ApproachDef(Skill, Difficulty, GearTag)`, `EffectDef(Momentum, Strain, Progress, InjuryRoll, Withdraw)`, `DecisionDef(Id, Stakes, Bold, Safe)`, `EncounterDef(Id, Tags, CommsBlocked, Chain, Careful, Fast, Decision)`, `StageDef(Slot, DrawTags, CountMin, CountMax, Skippable)`, `ObjectiveDef(Id, Tags, TeamSizeMin, TeamSizeMax, Need, Stages)`, `MissionTuning` (required init properties), `BenchTeamDef`, and `BenchTuning(Rookie, Mixed, Veteran)`.
- Produces: `ContentLoader.LoadTuningText(string) → (MissionTuning?, IReadOnlyList<ContentError>)`.
- Produces for tests: `TestContent.Root`, `TuningText`, `Pack`, `Tuning`, and `Objective` (a `partial` class; Task 3 adds to it).

- [ ] **Step 1: Write the failing tests**

`tests/Sim.Tests/TestContent.cs`:

```csharp
using Sim.Content;
using Sim.Core.Content;

namespace Sim.Tests;

// The repository's own content pack.
internal static partial class TestContent
{
    public static readonly string Root = FindRoot();

    public static readonly string TuningText = File.ReadAllText(Path.Combine(Root, "content", "tuning", "missions.yaml"));

    public static readonly ContentPack Pack = LoadPack();

    public static MissionTuning Tuning => Pack.Tuning;

    public static ObjectiveDef Objective => Pack.Objectives.First(o => o.Id == "OBJECTIVE_TEMPLATE_1");

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static ContentPack LoadPack()
    {
        ContentLoadResult result = ContentLoader.LoadDirectory(Path.Combine(Root, "content"));
        return result.Pack ?? throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => $"{e.File}: {e.Message}")));
    }
}
```

`tests/Sim.Tests/MissionContentTests.cs`:

```csharp
using Sim.Content;
using Sim.Core.Content;

namespace Sim.Tests;

public class MissionContentTests
{
    private const string Encounter = """
        - id: ENCOUNTER_01
          tags: [field]
          approaches:
            careful: { skill: SKILL_A, difficulty: 5, gearTag: scanner }
            fast: { skill: SKILL_A, difficulty: 6 }
          decision:
            id: DECISION_PROMPT_1
            stakes: 2
            bold: [{ momentum: 1 }, { strain: 1 }, { injuryRoll: light }]
            safe: [{ withdraw: true }]

        """;

    private const string Objective = """
        - id: OBJECTIVE_TEMPLATE_1
          teamSize: [2, 4]
          need: 1
          stages:
            - { slot: approach, draw: { tags: [field] }, count: 1 }

        """;

    [Fact]
    public void The_starter_pack_loads_from_disk()
    {
        ContentPack pack = TestContent.Pack;

        Assert.Equal(9, pack.Encounters.Count);
        Assert.Equal(new[] { "OBJECTIVE_TEMPLATE_1", "OBJECTIVE_TEMPLATE_2" }, pack.Objectives.Select(o => o.Id));
        Assert.Equal(7, pack.Tuning.CheckTarget);
        Assert.Equal(3, pack.Bench.Mixed.Size);
    }

    [Fact]
    public void Encounters_and_objectives_load()
    {
        ContentPack pack = Load(Encounter, Objective).Pack!;

        EncounterDef encounter = Assert.Single(pack.Encounters);
        Assert.Equal(new ApproachDef("SKILL_A", 5, "scanner"), encounter.Careful);
        Assert.Equal(new EffectDef(Momentum: 1, Strain: 1, InjuryRoll: InjurySeverity.Light), encounter.Decision!.Bold);
        Assert.True(encounter.Decision.Safe.Withdraw);
        ObjectiveDef objective = Assert.Single(pack.Objectives);
        Assert.Equal((2, 4, 1), (objective.TeamSizeMin, objective.TeamSizeMax, objective.Need));
        Assert.Equal(new StageDef("approach", ["field"], 1, 1, false), objective.Stages[0], StageComparer.Instance);
    }

    [Fact]
    public void Broken_references_are_reported_together()
    {
        string encounter = Encounter
            .Replace("skill: SKILL_A, difficulty: 6", "skill: SKILL_Z, difficulty: 0")
            .Replace("stakes: 2", "stakes: 9")
            .Replace("{ strain: 1 }", "{ luck: 1 }")
            .Replace("tags: [field]", "tags: [field]\n  chain: ENCOUNTER_99");
        string objective = Objective.Replace("count: 1", "count: [1, 3]").Replace("need: 1", "need: 0");

        ContentLoadResult result = Load(encounter, objective);

        Assert.Null(result.Pack);
        Assert.Equal(
            new[]
            {
                "encounters/a.yaml|ENCOUNTER_01: The fast approach uses unknown skill 'SKILL_Z'.",
                "encounters/a.yaml|ENCOUNTER_01: The fast approach's difficulty must be 1 or more.",
                "encounters/a.yaml|ENCOUNTER_01: Chains to unknown encounter 'ENCOUNTER_99'.",
                "encounters/a.yaml|ENCOUNTER_01: The decision's stakes must be 1 to 5.",
                "encounters/a.yaml|ENCOUNTER_01: Unknown effect 'luck: 1'.",
                "objectives/a.yaml|OBJECTIVE_TEMPLATE_1: need must be 1 or more.",
                "objectives/a.yaml|OBJECTIVE_TEMPLATE_1: Stage 'approach' draws up to 3 encounters tagged [field], but only 0 match.",
            },
            result.Errors.Select(e => $"{e.File}|{e.Message}"));
    }

    // A tuning number never falls back to a default: a typo must not quietly change the game.
    [Fact]
    public void A_missing_misspelt_or_malformed_tuning_value_is_an_error()
    {
        string tuning = TestContent.TuningText
            .Replace("  gearBonus: 2\n", "  gearBonis: 2\n")
            .Replace("  cautionWeight: 0.5\n", "  cautionWeight: lots\n");

        ContentLoadResult result = Load(Encounter, Objective, tuning);

        Assert.Null(result.Pack);
        Assert.Equal(
            new[]
            {
                "check.gearBonus is missing.",
                "calls.cautionWeight must be a number, not 'lots'.",
                "check.gearBonis is not a tuning value.",
            },
            result.Errors.Select(e => e.Message));
    }

    [Fact]
    public void A_pack_without_a_tuning_file_is_an_error()
    {
        ContentLoadResult result = ContentLoader.Load(new Dictionary<string, string> { ["pack.yaml"] = "id: CORE\nversion: 1\n" });

        Assert.Equal("The pack has no tuning/missions.yaml.", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void A_tuning_file_loads_on_its_own_for_comparison()
    {
        (MissionTuning? tuning, IReadOnlyList<ContentError> errors) = ContentLoader.LoadTuningText(TestContent.TuningText.Replace("  gearBonus: 2\n", "  gearBonus: 3\n"));

        Assert.Empty(errors);
        Assert.Equal(TestContent.Tuning with { GearBonus = 3 }, tuning);
    }

    private static ContentLoadResult Load(string encounters, string objectives, string? tuning = null)
    {
        return ContentLoader.Load(new Dictionary<string, string>
        {
            ["pack.yaml"] = "id: CORE\nversion: 1\n",
            ["skills/a.yaml"] = "- id: SKILL_A\n",
            ["encounters/a.yaml"] = encounters,
            ["objectives/a.yaml"] = objectives,
            ["tuning/missions.yaml"] = tuning ?? TestContent.TuningText,
        });
    }

    // Records compare lists by reference, so stages are compared field by field.
    private sealed class StageComparer : IEqualityComparer<StageDef>
    {
        public static readonly StageComparer Instance = new();

        public bool Equals(StageDef? x, StageDef? y)
        {
            return x is not null && y is not null && x.Slot == y.Slot && x.DrawTags.SequenceEqual(y.DrawTags)
                && x.CountMin == y.CountMin && x.CountMax == y.CountMax && x.Skippable == y.Skippable;
        }

        public int GetHashCode(StageDef obj)
        {
            return obj.Slot.GetHashCode(StringComparison.Ordinal);
        }
    }
}
```

Replace `tests/Sim.Tests/ContentLoaderTests.cs` whole. Every pack now needs a tuning file, so each test supplies the repository's own:

`tests/Sim.Tests/ContentLoaderTests.cs`:

```csharp
using Sim.Content;
using Sim.Core.Content;

namespace Sim.Tests;

public class ContentLoaderTests
{
    private const string Manifest = "id: CORE\nversion: 1\n";

    [Fact]
    public void A_valid_pack_loads()
    {
        ContentLoadResult result = ContentLoader.Load(new Dictionary<string, string>
        {
            ["pack.yaml"] = Manifest,
            ["tuning/missions.yaml"] = TestContent.TuningText,
            ["skills/base.yaml"] = "- id: SKILL_A\n  tags: [technical]\n- id: SKILL_B\n",
        });

        Assert.Empty(result.Errors);
        Assert.Equal("CORE", result.Pack!.Id);
        Assert.Equal(1, result.Pack.Version);
        Assert.Equal(new[] { "SKILL_A", "SKILL_B" }, result.Pack.Skills.Select(s => s.Id));
        Assert.Equal(new[] { "technical" }, result.Pack.Skills[0].Tags);
    }

    // The author fixes content in batches, so every problem is reported at once and no pack is returned.
    [Fact]
    public void All_errors_are_reported_together()
    {
        ContentLoadResult result = ContentLoader.Load(new Dictionary<string, string>
        {
            ["pack.yaml"] = "id: core\nversion: 0\n",
            ["tuning/missions.yaml"] = TestContent.TuningText,
            ["skills/a.yaml"] = "- id: SKILL_A\n- id: skill b\n",
            ["skills/b.yaml"] = "- id: SKILL_A\n",
            ["skils/c.yaml"] = "- id: SKILL_C\n",
            ["skills/d.yaml"] = "- id: SKILL_D\n  tag: [typo]\n",
        });

        Assert.Null(result.Pack);
        Assert.Equal(
            new[]
            {
                "pack.yaml|Id 'core' must be upper case letters, digits, and underscores, starting with a letter.",
                "pack.yaml|version must be 1 or more.",
                "skills/a.yaml|Id 'skill b' must be upper case letters, digits, and underscores, starting with a letter.",
                "skills/b.yaml|Duplicate id 'SKILL_A', first defined in skills/a.yaml.",
                "skills/d.yaml|Line 2: Property 'tag' not found on type 'Sim.Content.ContentLoader+SkillDto'.",
                "skils/c.yaml|Unknown content folder 'skils'.",
            },
            result.Errors.Select(e => $"{e.File}|{e.Message}"));
    }

    [Fact]
    public void A_missing_manifest_is_an_error()
    {
        ContentLoadResult result = ContentLoader.Load(new Dictionary<string, string> { ["tuning/missions.yaml"] = TestContent.TuningText });

        Assert.Equal("The pack has no pack.yaml.", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void A_blank_list_item_is_reported_for_its_file()
    {
        ContentLoadResult result = ContentLoader.Load(new Dictionary<string, string>
        {
            ["pack.yaml"] = Manifest,
            ["tuning/missions.yaml"] = TestContent.TuningText,
            ["skills/a.yaml"] = "- id: SKILL_A\n-\n",
        });

        Assert.Null(result.Pack);
        Assert.Equal("skills/a.yaml", Assert.Single(result.Errors).File);
    }

    [Fact]
    public void Tags_with_no_value_load_as_an_empty_list()
    {
        ContentLoadResult result = ContentLoader.Load(new Dictionary<string, string>
        {
            ["pack.yaml"] = Manifest,
            ["tuning/missions.yaml"] = TestContent.TuningText,
            ["skills/a.yaml"] = "- id: SKILL_A\n  tags:\n",
        });

        Assert.Empty(result.Errors);
        Assert.Empty(result.Pack!.Skills[0].Tags);
    }

    [Fact]
    public void A_duplicated_key_is_reported_for_its_file()
    {
        ContentLoadResult result = ContentLoader.Load(new Dictionary<string, string>
        {
            ["pack.yaml"] = Manifest,
            ["tuning/missions.yaml"] = TestContent.TuningText,
            ["skills/a.yaml"] = "- id: SKILL_A\n  id: SKILL_B\n",
        });

        Assert.Null(result.Pack);
        Assert.Equal("skills/a.yaml", Assert.Single(result.Errors).File);
    }
}
```


- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sim.Tests`
Expected: FAIL to build: the namespace `Sim.Core.Content` does not exist.

- [ ] **Step 3: Move the pack records to the core and add the mission definitions**

Delete `src/Sim.Content/ContentPack.cs`.

`src/Sim.Core/Content/ContentPack.cs`:

```csharp
namespace Sim.Core.Content;

public sealed record SkillDef(string Id, IReadOnlyList<string> Tags);

public sealed record ContentPack(
    string Id,
    int Version,
    IReadOnlyList<SkillDef> Skills,
    IReadOnlyList<EncounterDef> Encounters,
    IReadOnlyList<ObjectiveDef> Objectives,
    MissionTuning Tuning,
    BenchTuning Bench);

public sealed record ContentError(string File, string Message);

public sealed record ContentLoadResult(ContentPack? Pack, IReadOnlyList<ContentError> Errors);
```

`src/Sim.Core/Content/MissionContent.cs`:

```csharp
namespace Sim.Core.Content;

public enum InjurySeverity
{
    Light,
    Severe,
}

public sealed record ApproachDef(string Skill, int Difficulty, string? GearTag);

// What a decision option does. Fields are additive, so several effects fold into one record.
public sealed record EffectDef(int Momentum = 0, int Strain = 0, int Progress = 0, InjurySeverity? InjuryRoll = null, bool Withdraw = false);

public sealed record DecisionDef(string Id, int Stakes, EffectDef Bold, EffectDef Safe);

public sealed record EncounterDef(
    string Id,
    IReadOnlyList<string> Tags,
    bool CommsBlocked,
    string? Chain,
    ApproachDef Careful,
    ApproachDef Fast,
    DecisionDef? Decision);

public sealed record StageDef(string Slot, IReadOnlyList<string> DrawTags, int CountMin, int CountMax, bool Skippable);

public sealed record ObjectiveDef(
    string Id,
    IReadOnlyList<string> Tags,
    int TeamSizeMin,
    int TeamSizeMax,
    int Need,
    IReadOnlyList<StageDef> Stages);
```

`src/Sim.Core/Content/MissionTuning.cs`:

```csharp
namespace Sim.Core.Content;

// Every number the mission resolver uses (GDD 4.7). A mission copies this at its start.
public sealed record MissionTuning
{
    public required int CheckTarget { get; init; }

    public required int TierCritical { get; init; }

    public required int TierSuccess { get; init; }

    public required int TierPartial { get; init; }

    public required int TierFailure { get; init; }

    public required int GearBonus { get; init; }

    public required int AssistDivisor { get; init; }

    public required int Cap { get; init; }

    public required int MomentumCritical { get; init; }

    public required int MomentumFastSuccess { get; init; }

    public required int StrainPartial { get; init; }

    public required int StrainFailure { get; init; }

    public required int StrainDisaster { get; init; }

    public required int StrainReliefOnSuccess { get; init; }

    public required int SkipAtMomentum { get; init; }

    public required int SkipCost { get; init; }

    public required int AbortCallAt { get; init; }

    public required int CollapseAt { get; init; }

    public required int ReachMargin { get; init; }

    public required int SupervisorPenalty { get; init; }

    public required double CallBase { get; init; }

    public required double CautionWeight { get; init; }

    public required double DisciplineWeight { get; init; }

    public required double AutonomyWeight { get; init; }

    public required double BoldCautionWeight { get; init; }

    public required double BoldPriorityWeight { get; init; }

    public required int WithdrawOfferedAtStakes { get; init; }

    public required int AbortStakes { get; init; }

    public required int BackedLoyalty { get; init; }

    public required int OverruledLoyalty { get; init; }

    public required double ExperienceClose { get; init; }

    public required double ExperienceNear { get; init; }

    public required double ExperienceFar { get; init; }

    public required int CloseMargin { get; init; }

    public required int NearMargin { get; init; }

    public required int TrivialAtMargin { get; init; }

    public required double AssistShare { get; init; }

    public required int MentorGap { get; init; }

    public required double MentorBonus { get; init; }

    public required int LightBelow { get; init; }

    public required int DeadBelow { get; init; }

    public required int IncapacitatedBelow { get; init; }

    public required int MedicalBonus { get; init; }

    public required int MaxLight { get; init; }

    public required int PartialAtProgress { get; init; }
}

public sealed record BenchTeamDef(int Size, int SkillMin, int SkillMax, int Seniors, int SeniorSkillMin, int SeniorSkillMax);

// Shapes the bench's generated teams. Never copied into a mission.
public sealed record BenchTuning(BenchTeamDef Rookie, BenchTeamDef Mixed, BenchTeamDef Veteran);
```


- [ ] **Step 4: Write the loader**

Replace `src/Sim.Content/ContentLoader.cs` whole, and create the two new partial files.

`src/Sim.Content/ContentLoader.cs`:

```csharp
using System.Text.RegularExpressions;
using Sim.Core.Content;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Sim.Content;

public static partial class ContentLoader
{
    private const string ManifestFile = "pack.yaml";
    private const string TuningFile = "tuning/missions.yaml";
    private static readonly string[] KnownFolders = ["skills", "encounters", "objectives", "tuning"];

    // Keys are paths relative to the pack root, with forward slashes.
    public static ContentLoadResult Load(IReadOnlyDictionary<string, string> files)
    {
        var errors = new List<ContentError>();
        IDeserializer yaml = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).WithDuplicateKeyChecking().Build();

        PackDto? manifest = null;
        if (files.TryGetValue(ManifestFile, out string? manifestText))
        {
            manifest = Parse<PackDto>(yaml, ManifestFile, manifestText, errors);
            if (manifest is not null)
            {
                CheckId(ManifestFile, manifest.Id, errors);
                if (manifest.Version < 1)
                {
                    errors.Add(new ContentError(ManifestFile, "version must be 1 or more."));
                }
            }
        }
        else
        {
            errors.Add(new ContentError(ManifestFile, "The pack has no pack.yaml."));
        }

        var skills = new List<SkillDef>();
        var encounters = new List<(string File, EncounterDto Dto)>();
        var objectives = new List<(string File, ObjectiveDto Dto)>();
        var seenIn = new Dictionary<string, string>();
        foreach ((string path, string text) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            if (path == ManifestFile || path == TuningFile)
            {
                continue;
            }

            string folder = path.Contains('/') ? path[..path.IndexOf('/')] : "";
            switch (folder)
            {
                case "skills":
                    foreach (SkillDto dto in Items<SkillDto>(yaml, path, text, d => d.Id, seenIn, errors))
                    {
                        skills.Add(new SkillDef(dto.Id!, dto.Tags ?? []));
                    }

                    break;
                case "encounters":
                    encounters.AddRange(Items<EncounterDto>(yaml, path, text, d => d.Id, seenIn, errors).Select(d => (path, d)));
                    break;
                case "objectives":
                    objectives.AddRange(Items<ObjectiveDto>(yaml, path, text, d => d.Id, seenIn, errors).Select(d => (path, d)));
                    break;
                case "tuning":
                    errors.Add(new ContentError(path, $"Unknown tuning file. The only tuning file is {TuningFile}."));
                    break;
                default:
                    errors.Add(new ContentError(path, $"Unknown content folder '{folder}'."));
                    break;
            }
        }

        List<EncounterDef> encounterDefs = BuildEncounters(encounters, skills, errors);
        List<ObjectiveDef> objectiveDefs = BuildObjectives(objectives, encounterDefs, errors);
        (MissionTuning? tuning, BenchTuning? bench) = LoadTuning(yaml, files, errors);

        ContentPack? pack = errors.Count == 0
            ? new ContentPack(manifest!.Id!, manifest.Version, skills, encounterDefs, objectiveDefs, tuning!, bench!)
            : null;
        return new ContentLoadResult(pack, errors);
    }

    public static ContentLoadResult LoadDirectory(string root)
    {
        if (!Directory.Exists(root))
        {
            return new ContentLoadResult(null, [new ContentError(root, "Content folder not found.")]);
        }

        Dictionary<string, string> files = Directory
            .EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllText);
        return Load(files);
    }

    // The valid, uniquely named items of one list file. Ids are unique across the whole pack.
    private static List<T> Items<T>(IDeserializer yaml, string path, string text, Func<T, string?> id, Dictionary<string, string> seenIn, List<ContentError> errors)
        where T : class
    {
        var items = new List<T>();
        foreach (T? dto in Parse<List<T?>>(yaml, path, text, errors) ?? [])
        {
            if (dto is null)
            {
                errors.Add(new ContentError(path, "A list item is blank."));
                continue;
            }

            if (!CheckId(path, id(dto), errors))
            {
                continue;
            }

            if (seenIn.TryGetValue(id(dto)!, out string? first))
            {
                errors.Add(new ContentError(path, $"Duplicate id '{id(dto)}', first defined in {first}."));
                continue;
            }

            seenIn[id(dto)!] = path;
            items.Add(dto);
        }

        return items;
    }

    private static T? Parse<T>(IDeserializer yaml, string file, string text, List<ContentError> errors)
        where T : class
    {
        try
        {
            T? value = yaml.Deserialize<T>(text);
            if (value is null)
            {
                errors.Add(new ContentError(file, "The file is empty."));
            }

            return value;
        }
        catch (YamlException e)
        {
            errors.Add(new ContentError(file, $"Line {e.Start.Line}: {e.InnerException?.Message ?? e.Message}"));
            return null;
        }
    }

    private static bool CheckId(string file, string? id, List<ContentError> errors)
    {
        if (id is not null && IdPattern().IsMatch(id))
        {
            return true;
        }

        errors.Add(new ContentError(file, $"Id '{id}' must be upper case letters, digits, and underscores, starting with a letter."));
        return false;
    }

    [GeneratedRegex("^[A-Z][A-Z0-9_]*$")]
    private static partial Regex IdPattern();

    private sealed class PackDto
    {
        public string? Id { get; set; }

        public int Version { get; set; }
    }

    private sealed class SkillDto
    {
        public string? Id { get; set; }

        public List<string>? Tags { get; set; }
    }
}
```

`src/Sim.Content/ContentLoader.Missions.cs`:

```csharp
using Sim.Core.Content;

namespace Sim.Content;

public static partial class ContentLoader
{
    private static List<EncounterDef> BuildEncounters(List<(string File, EncounterDto Dto)> items, List<SkillDef> skills, List<ContentError> errors)
    {
        var skillIds = skills.Select(s => s.Id).ToHashSet();
        var ids = items.Select(i => i.Dto.Id!).ToHashSet();
        var defs = new List<EncounterDef>();
        foreach ((string file, EncounterDto dto) in items)
        {
            int before = errors.Count;
            void Error(string message)
            {
                errors.Add(new ContentError(file, $"{dto.Id}: {message}"));
            }

            ApproachDef? Approach(string name, ApproachDto? approach)
            {
                if (approach is null)
                {
                    Error($"The {name} approach is missing.");
                    return null;
                }

                if (approach.Skill is null || !skillIds.Contains(approach.Skill))
                {
                    Error($"The {name} approach uses unknown skill '{approach.Skill}'.");
                }

                if (approach.Difficulty < 1)
                {
                    Error($"The {name} approach's difficulty must be 1 or more.");
                }

                return new ApproachDef(approach.Skill ?? "", approach.Difficulty, approach.GearTag);
            }

            ApproachDef? careful = Approach("careful", dto.Approaches?.Careful);
            ApproachDef? fast = Approach("fast", dto.Approaches?.Fast);
            if (dto.Chain is not null && !ids.Contains(dto.Chain))
            {
                Error($"Chains to unknown encounter '{dto.Chain}'.");
            }

            DecisionDef? decision = null;
            if (dto.Decision is not null)
            {
                if (dto.Decision.Id is null)
                {
                    Error("The decision has no id.");
                }

                if (dto.Decision.Stakes is < 1 or > 5)
                {
                    Error("The decision's stakes must be 1 to 5.");
                }

                decision = new DecisionDef(dto.Decision.Id ?? "", dto.Decision.Stakes, Effects(dto.Decision.Bold, Error), Effects(dto.Decision.Safe, Error));
            }

            if (errors.Count == before)
            {
                defs.Add(new EncounterDef(dto.Id!, dto.Tags ?? [], dto.CommsBlocked, dto.Chain, careful!, fast!, decision));
            }
        }

        return defs;
    }

    private static EffectDef Effects(List<Dictionary<string, string>?>? items, Action<string> error)
    {
        var effect = new EffectDef();
        foreach ((string key, string value) in (items ?? []).SelectMany(i => i ?? []))
        {
            switch (key)
            {
                case "momentum" when int.TryParse(value, out int n):
                    effect = effect with { Momentum = effect.Momentum + n };
                    break;
                case "strain" when int.TryParse(value, out int n):
                    effect = effect with { Strain = effect.Strain + n };
                    break;
                case "progress" when int.TryParse(value, out int n):
                    effect = effect with { Progress = effect.Progress + n };
                    break;
                case "injuryRoll" when value is "light" or "severe":
                    effect = effect with { InjuryRoll = value == "light" ? InjurySeverity.Light : InjurySeverity.Severe };
                    break;
                case "withdraw" when value == "true":
                    effect = effect with { Withdraw = true };
                    break;
                default:
                    error($"Unknown effect '{key}: {value}'.");
                    break;
            }
        }

        return effect;
    }

    private static List<ObjectiveDef> BuildObjectives(List<(string File, ObjectiveDto Dto)> items, List<EncounterDef> encounters, List<ContentError> errors)
    {
        var defs = new List<ObjectiveDef>();
        foreach ((string file, ObjectiveDto dto) in items)
        {
            int before = errors.Count;
            void Error(string message)
            {
                errors.Add(new ContentError(file, $"{dto.Id}: {message}"));
            }

            (int min, int max) = Range(dto.TeamSize, "teamSize", Error);
            if (dto.Need < 1)
            {
                Error("need must be 1 or more.");
            }

            var stages = new List<StageDef>();
            foreach (StageDto? stage in dto.Stages ?? [])
            {
                if (stage?.Slot is null)
                {
                    Error("A stage has no slot.");
                    continue;
                }

                List<string> tags = stage.Draw?.Tags ?? [];
                (int countMin, int countMax) = Range(stage.Count, $"stage '{stage.Slot}' count", Error);
                int matches = encounters.Count(e => tags.All(e.Tags.Contains));
                if (matches < countMax)
                {
                    Error($"Stage '{stage.Slot}' draws up to {countMax} encounters tagged [{string.Join(", ", tags)}], but only {matches} match.");
                }

                stages.Add(new StageDef(stage.Slot, tags, countMin, countMax, stage.Skippable));
            }

            if (stages.Count == 0)
            {
                Error("An objective needs at least one stage.");
            }

            if (errors.Count == before)
            {
                defs.Add(new ObjectiveDef(dto.Id!, dto.Tags ?? [], min, max, dto.Need, stages));
            }
        }

        return defs;
    }

    // A count written as one number, or as [min, max].
    private static (int Min, int Max) Range(object? value, string name, Action<string> error)
    {
        List<object> parts = value switch
        {
            List<object> list => list,
            null => [],
            _ => [value, value],
        };
        if (parts.Count == 2
            && int.TryParse(parts[0].ToString(), out int min)
            && int.TryParse(parts[1].ToString(), out int max)
            && min >= 1
            && max >= min)
        {
            return (min, max);
        }

        error($"{name} must be a number of 1 or more, or [min, max].");
        return (1, 1);
    }

    private sealed class EncounterDto
    {
        public string? Id { get; set; }

        public List<string>? Tags { get; set; }

        public bool CommsBlocked { get; set; }

        public string? Chain { get; set; }

        public ApproachesDto? Approaches { get; set; }

        public DecisionDto? Decision { get; set; }
    }

    private sealed class ApproachesDto
    {
        public ApproachDto? Careful { get; set; }

        public ApproachDto? Fast { get; set; }
    }

    private sealed class ApproachDto
    {
        public string? Skill { get; set; }

        public int Difficulty { get; set; }

        public string? GearTag { get; set; }
    }

    private sealed class DecisionDto
    {
        public string? Id { get; set; }

        public int Stakes { get; set; }

        public List<Dictionary<string, string>?>? Bold { get; set; }

        public List<Dictionary<string, string>?>? Safe { get; set; }
    }

    private sealed class ObjectiveDto
    {
        public string? Id { get; set; }

        public List<string>? Tags { get; set; }

        public object? TeamSize { get; set; }

        public int Need { get; set; }

        public List<StageDto?>? Stages { get; set; }
    }

    private sealed class StageDto
    {
        public string? Slot { get; set; }

        public DrawDto? Draw { get; set; }

        public object? Count { get; set; }

        public bool Skippable { get; set; }
    }

    private sealed class DrawDto
    {
        public List<string>? Tags { get; set; }
    }
}
```

`src/Sim.Content/ContentLoader.Tuning.cs`:

```csharp
using System.Globalization;
using Sim.Core.Content;
using YamlDotNet.Serialization;

namespace Sim.Content;

public static partial class ContentLoader
{
    // One tuning file on its own, for comparing against the pack's.
    public static (MissionTuning? Tuning, IReadOnlyList<ContentError> Errors) LoadTuningText(string text)
    {
        var errors = new List<ContentError>();
        IDeserializer yaml = new DeserializerBuilder().WithDuplicateKeyChecking().Build();
        (MissionTuning? tuning, _) = LoadTuning(yaml, new Dictionary<string, string> { [TuningFile] = text }, errors);
        return (errors.Count == 0 ? tuning : null, errors);
    }

    private static (MissionTuning? Tuning, BenchTuning? Bench) LoadTuning(IDeserializer yaml, IReadOnlyDictionary<string, string> files, List<ContentError> errors)
    {
        if (!files.TryGetValue(TuningFile, out string? text))
        {
            errors.Add(new ContentError(TuningFile, $"The pack has no {TuningFile}."));
            return (null, null);
        }

        Dictionary<object, object>? root = Parse<Dictionary<object, object>>(yaml, TuningFile, text, errors);
        if (root is null)
        {
            return (null, null);
        }

        var values = new Dictionary<string, string>();
        Flatten("", root, values);
        var read = new TuningReader(values, errors);

        var tuning = new MissionTuning
        {
            CheckTarget = read.Int("check.target"),
            TierCritical = read.Int("check.tiers.critical"),
            TierSuccess = read.Int("check.tiers.success"),
            TierPartial = read.Int("check.tiers.partial"),
            TierFailure = read.Int("check.tiers.failure"),
            GearBonus = read.Int("check.gearBonus"),
            AssistDivisor = read.Int("check.assistDivisor", min: 1),
            Cap = read.Int("compounding.cap", min: 1),
            MomentumCritical = read.Int("compounding.momentum.critical"),
            MomentumFastSuccess = read.Int("compounding.momentum.fastSuccess"),
            StrainPartial = read.Int("compounding.strain.partial"),
            StrainFailure = read.Int("compounding.strain.failure"),
            StrainDisaster = read.Int("compounding.strain.disaster"),
            StrainReliefOnSuccess = read.Int("compounding.strain.reliefOnSuccess"),
            SkipAtMomentum = read.Int("compounding.skipAtMomentum"),
            SkipCost = read.Int("compounding.skipCost"),
            AbortCallAt = read.Int("compounding.abortCallAt"),
            CollapseAt = read.Int("compounding.collapseAt"),
            ReachMargin = read.Int("develop.reachMargin"),
            SupervisorPenalty = read.Int("develop.supervisorPenalty"),
            CallBase = read.Number("calls.base"),
            CautionWeight = read.Number("calls.cautionWeight"),
            DisciplineWeight = read.Number("calls.disciplineWeight"),
            AutonomyWeight = read.Number("calls.autonomyWeight"),
            BoldCautionWeight = read.Number("calls.boldCautionWeight"),
            BoldPriorityWeight = read.Number("calls.boldPriorityWeight"),
            WithdrawOfferedAtStakes = read.Int("calls.withdrawOfferedAtStakes"),
            AbortStakes = read.Int("calls.abortStakes"),
            BackedLoyalty = read.Int("calls.backedLoyalty"),
            OverruledLoyalty = read.Int("calls.overruledLoyalty"),
            ExperienceClose = read.Number("experience.close"),
            ExperienceNear = read.Number("experience.near"),
            ExperienceFar = read.Number("experience.far"),
            CloseMargin = read.Int("experience.closeMargin"),
            NearMargin = read.Int("experience.nearMargin"),
            TrivialAtMargin = read.Int("experience.trivialAtMargin"),
            AssistShare = read.Number("experience.assistShare"),
            MentorGap = read.Int("experience.mentorGap"),
            MentorBonus = read.Number("experience.mentorBonus"),
            LightBelow = read.Int("injury.lightBelow"),
            DeadBelow = read.Int("injury.deadBelow"),
            IncapacitatedBelow = read.Int("injury.incapacitatedBelow"),
            MedicalBonus = read.Int("injury.medicalBonus"),
            MaxLight = read.Int("injury.maxLight"),
            PartialAtProgress = read.Int("result.partialAtProgress"),
        };
        var bench = new BenchTuning(BenchTeam(read, "rookie"), BenchTeam(read, "mixed"), BenchTeam(read, "veteran"));

        bool ordered = tuning.TierCritical > tuning.TierSuccess && tuning.TierSuccess > tuning.TierPartial && tuning.TierPartial > tuning.TierFailure;
        if (read.Complete && !ordered)
        {
            errors.Add(new ContentError(TuningFile, "check.tiers must fall from critical to failure."));
        }

        return read.ReportUnused() ? (tuning, bench) : (null, null);
    }

    private static BenchTeamDef BenchTeam(TuningReader read, string name)
    {
        string at = $"bench.teams.{name}";
        return new BenchTeamDef(
            read.Int($"{at}.size", min: 1),
            read.Int($"{at}.skill.0"),
            read.Int($"{at}.skill.1"),
            read.Int($"{at}.seniors"),
            read.Int($"{at}.seniorSkill.0"),
            read.Int($"{at}.seniorSkill.1"));
    }

    private static void Flatten(string prefix, object? node, Dictionary<string, string> values)
    {
        switch (node)
        {
            case Dictionary<object, object> map:
                foreach ((object key, object value) in map)
                {
                    Flatten(prefix == "" ? key.ToString()! : $"{prefix}.{key}", value, values);
                }

                break;
            case List<object> list:
                for (int i = 0; i < list.Count; i++)
                {
                    Flatten($"{prefix}.{i}", list[i], values);
                }

                break;
            default:
                values[prefix] = node?.ToString() ?? "";
                break;
        }
    }

    // Reads each value once by its dotted path. A missing, malformed, or unread value is an error:
    // a tuning number never falls back to a default.
    private sealed class TuningReader(Dictionary<string, string> values, List<ContentError> errors)
    {
        private readonly HashSet<string> _read = [];

        public bool Complete { get; private set; } = true;

        public int Int(string path, int min = int.MinValue)
        {
            if (Text(path) is not { } text)
            {
                return 0;
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= min)
            {
                return value;
            }

            Fail(path, min == int.MinValue ? $"must be a whole number, not '{text}'." : $"must be a whole number of {min} or more, not '{text}'.");
            return 0;
        }

        public double Number(string path)
        {
            if (Text(path) is not { } text)
            {
                return 0;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                return value;
            }

            Fail(path, $"must be a number, not '{text}'.");
            return 0;
        }

        public bool ReportUnused()
        {
            foreach (string path in values.Keys.Where(k => !_read.Contains(k)).Order(StringComparer.Ordinal))
            {
                Fail(path, "is not a tuning value.");
            }

            return Complete;
        }

        private string? Text(string path)
        {
            _read.Add(path);
            if (values.TryGetValue(path, out string? text))
            {
                return text;
            }

            Fail(path, "is missing.");
            return null;
        }

        private void Fail(string path, string message)
        {
            Complete = false;
            errors.Add(new ContentError(TuningFile, $"{path} {message}"));
        }
    }
}
```


- [ ] **Step 5: Write the starter content**

Placeholders only. The fast approach is authored one harder than the careful one.

`content/tuning/missions.yaml`:

```yaml
# Every number the mission resolver uses. All are required. See docs/gdd.md section 4.
check:
  target: 7                  # added to an approach's difficulty
  tiers:                     # the lowest margin for each tier; below failure is a disaster
    critical: 5
    success: 0
    partial: -3
    failure: -7
  gearBonus: 2
  assistDivisor: 2           # assist = the helper's skill divided by this, rounded down
compounding:
  cap: 5
  momentum:
    critical: 1
    fastSuccess: 1           # a fast critical earns both
  strain:
    partial: 1
    failure: 2
    disaster: 3
    reliefOnSuccess: 1
  skipAtMomentum: 4
  skipCost: 2
  abortCallAt: 4
  collapseAt: 5
develop:
  reachMargin: -2            # a junior acts only at this expected margin or better
  supervisorPenalty: 1       # the supervising senior assists at skill minus this
calls:
  base: 3
  cautionWeight: 0.5
  disciplineWeight: 0.25
  autonomyWeight: 1
  boldCautionWeight: 2       # the lead prefers bold when priority * boldPriorityWeight
  boldPriorityWeight: 1      #   exceeds caution * boldCautionWeight
  withdrawOfferedAtStakes: 3
  abortStakes: 4
  backedLoyalty: 1
  overruledLoyalty: -1
experience:
  close: 3                   # margin within closeMargin of zero
  near: 2                    # margin within nearMargin of zero
  far: 1
  closeMargin: 3
  nearMargin: 6
  trivialAtMargin: 7         # at this margin or above, nothing is learned
  assistShare: 0.5
  mentorGap: 4
  mentorBonus: 1
injury:
  lightBelow: 7              # light roll: 2d6 below this is a light injury
  deadBelow: 5               # severe roll: 2d6 below this is death
  incapacitatedBelow: 8      # severe roll: 2d6 below this is incapacitation
  medicalBonus: 2
  maxLight: 2                # each light injury is -1 to every skill
result:
  partialAtProgress: 2
bench:
  teams:
    rookie:  { size: 3, skill: [1, 3], seniors: 0, seniorSkill: [1, 3] }
    mixed:   { size: 3, skill: [2, 4], seniors: 1, seniorSkill: [5, 6] }
    veteran: { size: 3, skill: [4, 7], seniors: 0, seniorSkill: [4, 7] }
```

`content/encounters/base.yaml`:

```yaml
# Placeholders. The author replaces these. Fast is authored one harder than careful.
- id: ENCOUNTER_01
  tags: [field, hazard]
  chain: ENCOUNTER_CHAIN
  approaches:
    careful: { skill: SKILL_A, difficulty: 5, gearTag: scanner }
    fast: { skill: SKILL_C, difficulty: 6 }
  decision:
    id: DECISION_PROMPT_1
    stakes: 1
    bold: [{ momentum: 1 }, { strain: 1 }]
    safe: []
- id: ENCOUNTER_02
  tags: [field, social]
  chain: ENCOUNTER_CHAIN
  approaches:
    careful: { skill: SKILL_D, difficulty: 5 }
    fast: { skill: SKILL_B, difficulty: 6 }
  decision:
    id: DECISION_PROMPT_2
    stakes: 2
    bold: [{ progress: 1 }, { injuryRoll: light }]
    safe: [{ strain: -1 }]
- id: ENCOUNTER_03
  tags: [field, technical]
  chain: ENCOUNTER_CHAIN
  approaches:
    careful: { skill: SKILL_B, difficulty: 6, gearTag: tools }
    fast: { skill: SKILL_A, difficulty: 7 }
  decision:
    id: DECISION_PROMPT_3
    stakes: 3
    bold: [{ progress: 1 }, { strain: 2 }]
    safe: [{ momentum: -1 }]
- id: ENCOUNTER_04
  tags: [field, hazard]
  chain: ENCOUNTER_CHAIN
  approaches:
    careful: { skill: SKILL_C, difficulty: 5 }
    fast: { skill: SKILL_C, difficulty: 6 }
- id: ENCOUNTER_05
  tags: [field, technical]
  chain: ENCOUNTER_CHAIN
  approaches:
    careful: { skill: SKILL_A, difficulty: 6, gearTag: scanner }
    fast: { skill: SKILL_B, difficulty: 7 }
  decision:
    id: DECISION_PROMPT_4
    stakes: 4
    bold: [{ progress: 2 }, { injuryRoll: severe }]
    safe: [{ strain: -1 }, { momentum: -2 }]
- id: ENCOUNTER_06
  tags: [field, hazard]
  commsBlocked: true
  chain: ENCOUNTER_CHAIN
  approaches:
    careful: { skill: SKILL_C, difficulty: 6 }
    fast: { skill: SKILL_D, difficulty: 7 }
  decision:
    id: DECISION_PROMPT_5
    stakes: 3
    bold: [{ progress: 1 }, { strain: 1 }]
    safe: []
- id: ENCOUNTER_07
  tags: [field, social]
  chain: ENCOUNTER_CHAIN
  approaches:
    careful: { skill: SKILL_D, difficulty: 6 }
    fast: { skill: SKILL_D, difficulty: 7 }
- id: ENCOUNTER_08
  tags: [field, technical]
  chain: ENCOUNTER_CHAIN
  approaches:
    careful: { skill: SKILL_B, difficulty: 5, gearTag: tools }
    fast: { skill: SKILL_C, difficulty: 6 }
  decision:
    id: DECISION_PROMPT_6
    stakes: 2
    bold: [{ momentum: 2 }, { injuryRoll: light }]
    safe: []
- id: ENCOUNTER_CHAIN
  tags: [chained, hazard]
  approaches:
    careful: { skill: SKILL_C, difficulty: 6 }
    fast: { skill: SKILL_A, difficulty: 7 }
```

`content/objectives/base.yaml`:

```yaml
# Placeholders. The author replaces these.
- id: OBJECTIVE_TEMPLATE_1
  tags: [science]
  teamSize: [2, 4]
  need: 4
  stages:
    - { slot: approach, draw: { tags: [field] }, count: 1 }
    - { slot: complication, draw: { tags: [field] }, count: 2 }
    - { slot: objective, draw: { tags: [field] }, count: 1 }
    - { slot: extraction, draw: { tags: [field] }, count: 1, skippable: true }
- id: OBJECTIVE_TEMPLATE_2
  tags: [repair]
  teamSize: [2, 3]
  need: 3
  stages:
    - { slot: approach, draw: { tags: [hazard, field] }, count: 1 }
    - { slot: objective, draw: { tags: [technical] }, count: [1, 2] }
    - { slot: extraction, draw: { tags: [social] }, count: 1, skippable: true }
```


- [ ] **Step 6: Keep `SimRunner` compiling**

In `src/SimRunner/Program.cs`, add `using Sim.Core.Content;` on the line after `using Sim.Content;`. Task 5 replaces this file.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet build --nologo -v quiet && dotnet test --nologo --no-build -v quiet`
Expected: build clean; 47 tests pass (Sim.Tests 39, Persistence.Tests 6, and SimRunner.Tests 2).

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "Add mission content, the tuning file, and their loader"
```

---

### Task 3: The mission resolver

**Files:**
- Create: `src/Sim.Missions/MissionTypes.cs`, `MissionState.cs`, `Beats.cs`, `MissionResolver.cs`
- Test: `tests/Sim.Tests/TestContent.Missions.cs`, `MissionRulesTests.cs`, `MissionWalkthroughTests.cs`

**Interfaces:**
- Consumes: the `Sim.Core.Content` records and `TestContent` from Task 2; `RngStream.For(ulong, int, string)`, `Next(int)`, and `TwoD6()` from Phase 0.
- Produces: `MissionResolver.Start(ObjectiveDef, IReadOnlyList<EncounterDef> pool, Team, IReadOnlyList<string> gearTags, Parameters, MissionTuning, ulong seed, ulong? drawSeed = null) → MissionState`.
- Produces: `MissionResolver.Advance(MissionState, CallAnswer? answer = null) → MissionStep(State, Beats, Pending, Result)`; `MissionResolver.AskThreshold`, `Prefers`, and `AbortDecisionId`.
- Produces: `TeamMember(Id, Seniority, Skills, Caution, Discipline)`, `Team(Members, LeadId)`, `Parameters(Priority, Risk, Autonomy, DevelopJuniors)`, `PendingCall`, `MissionResult(Outcome, Progress, Need, Calls, Notices, Crew)`, `CrewResult`, the enums `MissionOutcome`, `Tier`, `Approach`, `CallAnswer`, `MemberStatus`, `InjuryOutcome`, and `MissionPhase`, the beat records, and `MissionJson.Serialize`, `Deserialize`, and `Options`.
- Produces for tests: `TestContent.Team(lead, members...)`, `Member(n)`, `Gear`, and `Play(team, seed, parameters, answer)`.

- [ ] **Step 1: Write the failing tests**

`tests/Sim.Tests/TestContent.Missions.cs`:

```csharp
using Sim.Missions;

namespace Sim.Tests;

// The eight placeholder crew the mission numbers were first benched on (2026-11-13), and a way to play a whole mission.
internal static partial class TestContent
{
    public static readonly string[] Gear = ["scanner", "tools"];

    private static readonly Dictionary<int, (int Seniority, int[] Skills, int Caution, int Discipline)> Crew = new()
    {
        [1] = (5, [6, 5, 4, 3], 2, 2),
        [2] = (4, [4, 6, 5, 2], -2, -1),
        [3] = (4, [5, 3, 6, 5], 0, 1),
        [4] = (3, [3, 4, 3, 6], 1, -2),
        [5] = (2, [2, 3, 2, 3], -1, 0),
        [6] = (2, [3, 1, 3, 2], 1, 1),
        [7] = (1, [1, 2, 1, 2], 0, 0),
        [8] = (1, [2, 1, 2, 1], -2, 2),
    };

    public static Team Team(int lead, params int[] members)
    {
        return new Team(members.Select(Member).ToList(), $"CREW_{lead:00}");
    }

    public static TeamMember Member(int n)
    {
        (int seniority, int[] skills, int caution, int discipline) = Crew[n];
        var levels = new Dictionary<string, int> { ["SKILL_A"] = skills[0], ["SKILL_B"] = skills[1], ["SKILL_C"] = skills[2], ["SKILL_D"] = skills[3] };
        return new TeamMember($"CREW_{n:00}", seniority, levels, caution, discipline);
    }

    // Starts a mission on the starter pack and plays it to the end, answering every call the same way.
    public static (MissionState Start, List<Beat> Beats, MissionResult Result) Play(Team team, ulong seed, Parameters? parameters = null, CallAnswer? answer = null)
    {
        MissionState start = MissionResolver.Start(Objective, Pack.Encounters, team, Gear, parameters ?? new Parameters(), Tuning, seed);
        var beats = new List<Beat>();
        MissionStep step = MissionResolver.Advance(start);
        beats.AddRange(step.Beats);
        while (step.Pending is { } call)
        {
            step = MissionResolver.Advance(step.State, answer ?? call.LeadPrefers);
            beats.AddRange(step.Beats);
        }

        return (start, beats, step.Result!);
    }
}
```

`tests/Sim.Tests/MissionRulesTests.cs`:

```csharp
using Sim.Core.Content;
using Sim.Missions;

namespace Sim.Tests;

// One rule per test (GDD 4.2 to 4.5), on small hand-built missions.
public class MissionRulesTests
{
    private static readonly DecisionDef Stakes2 = new("DECISION_PROMPT_1", 2, new EffectDef(Progress: 1), new EffectDef());
    private static readonly DecisionDef Stakes3 = new("DECISION_PROMPT_2", 3, new EffectDef(Progress: 1), new EffectDef());

    [Fact]
    public void The_score_is_skill_plus_half_the_helper_plus_gear()
    {
        MissionStep step = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(5, gearTag: "scanner")));

        CheckBeat check = step.Beats.OfType<CheckBeat>().Single();
        Assert.Equal(("CREW_01", "CREW_03", 6, 2, 2, 10, 12), (check.ActorId, check.HelperId, check.ActorSkill, check.Assist, check.Gear, check.Score, check.Target));
        Assert.Equal(check.Score + check.Roll - check.Target, check.Margin);
    }

    [Fact]
    public void Every_check_adds_up_and_lands_in_the_tier_its_margin_gives()
    {
        MissionTuning t = TestContent.Tuning;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            foreach (CheckBeat check in TestContent.Play(TestContent.Team(2, 2, 7, 8), seed).Beats.OfType<CheckBeat>())
            {
                Assert.Equal(check.ActorSkill + check.Assist + check.Gear + check.Momentum - check.Strain, check.Score);
                Assert.Equal(check.Score + check.Roll - check.Target, check.Margin);
                Tier expected = check.Margin >= t.TierCritical ? Tier.Critical
                    : check.Margin >= t.TierSuccess ? Tier.Success
                    : check.Margin >= t.TierPartial ? Tier.Partial
                    : check.Margin >= t.TierFailure ? Tier.Failure
                    : Tier.Disaster;
                Assert.Equal(expected, check.Tier);
            }
        }
    }

    // Skill A: CREW_03 has 5, CREW_06 has 3, and CREW_05 has 2.
    [Fact]
    public void Develop_juniors_puts_the_junior_forward_when_the_check_is_within_reach()
    {
        MissionStep step = MissionResolver.Advance(Start(TestContent.Team(3, 3, 5, 6), new Parameters(DevelopJuniors: true), Encounter(5)));

        CheckBeat check = step.Beats.OfType<CheckBeat>().Single();
        Assert.True(check.Supervised);
        Assert.Equal(("CREW_05", "CREW_03", 2, 4), (check.ActorId, check.HelperId, check.ActorSkill, check.Assist));
    }

    [Fact]
    public void Develop_juniors_leaves_a_check_out_of_reach_to_the_best_qualified()
    {
        MissionStep step = MissionResolver.Advance(Start(TestContent.Team(3, 3, 5, 6), new Parameters(DevelopJuniors: true), Encounter(9)));

        CheckBeat check = step.Beats.OfType<CheckBeat>().Single();
        Assert.False(check.Supervised);
        Assert.Equal(("CREW_03", "CREW_06", 5, 1), (check.ActorId, check.HelperId, check.ActorSkill, check.Assist));
    }

    [Fact]
    public void Risk_is_tolerance_so_a_higher_risk_pushes_the_lead_toward_the_fast_approach()
    {
        Assert.Equal(
            new[] { Approach.Careful, Approach.Careful, Approach.Fast },
            new[] { -1, 0, 1 }.Select(r => ApproachFor(TestContent.Team(3, 3, 5), r)));
        Assert.Equal(Approach.Careful, ApproachFor(TestContent.Team(1, 1, 3), 1));
        Assert.Equal(Approach.Fast, ApproachFor(TestContent.Team(2, 2, 3), 0));
    }

    [Fact]
    public void The_mentoring_bonus_goes_to_the_junior_alone_and_the_helper_gets_a_share_of_the_base()
    {
        MissionStep step = MissionResolver.Advance(Start(TestContent.Team(1, 1, 7), new Parameters(DevelopJuniors: true), Encounter(1)));

        CheckBeat check = step.Beats.OfType<CheckBeat>().Single();
        MissionTuning t = TestContent.Tuning;
        Assert.True(check.Mentoring);
        Assert.Equal(("CREW_07", "CREW_01"), (check.ActorId, check.HelperId));
        double baseExperience = check.Experience - t.MentorBonus;
        Assert.Equal(check.Experience, step.Result!.Crew.Single(c => c.Id == "CREW_07").Experience);
        Assert.Equal(baseExperience * t.AssistShare, step.Result.Crew.Single(c => c.Id == "CREW_01").Experience);
    }

    [Fact]
    public void A_chain_target_that_carries_the_drawn_tag_is_never_drawn()
    {
        EncounterDef chain = Encounter(5, id: "ENCOUNTER_CHAIN");
        EncounterDef source = Encounter(5, chain: "ENCOUNTER_CHAIN");
        for (ulong seed = 1; seed <= 50; seed++)
        {
            var objective = new ObjectiveDef("OBJECTIVE_TEMPLATE_1", [], 1, 4, 1, [new StageDef("stage", ["field"], 1, 1, false)]);
            MissionState state = MissionResolver.Start(objective, [chain, source], TestContent.Team(1, 1, 3), TestContent.Gear, new Parameters(), TestContent.Tuning, seed);

            Assert.Equal("ENCOUNTER_01", state.Encounters.Single().Encounter.Id);
        }
    }

    // The pool's other encounter keeps its dice whether or not a disaster chained an extra one in before it.
    [Fact]
    public void A_chain_does_not_move_the_dice_of_later_encounters()
    {
        EncounterDef later = Encounter(5, id: "ENCOUNTER_02");
        EncounterDef chain = Encounter(5, id: "ENCOUNTER_CHAIN", tags: ["chained"]);
        var objective = new ObjectiveDef("OBJECTIVE_TEMPLATE_1", [], 1, 4, 1, [new StageDef("stage", ["field"], 2, 2, false)]);
        int compared = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            int? Roll(params EncounterDef[] pool)
            {
                MissionState state = MissionResolver.Start(objective, pool, TestContent.Team(1, 1, 3, 5, 7), TestContent.Gear, new Parameters(), TestContent.Tuning, seed);
                MissionStep step = MissionResolver.Advance(state);
                var beats = new List<Beat>(step.Beats);
                while (step.Pending is not null)
                {
                    step = MissionResolver.Advance(step.State, CallAnswer.Bold);
                    beats.AddRange(step.Beats);
                }

                return beats.OfType<CheckBeat>().FirstOrDefault(c => c.EncounterId == "ENCOUNTER_02")?.Roll;
            }

            int? chained = Roll(Encounter(40, chain: "ENCOUNTER_CHAIN"), later, chain);
            int? plain = Roll(Encounter(40), later, chain);
            Assert.Equal(plain, chained);
            compared += chained is null ? 0 : 1;
        }

        Assert.True(compared > 0);
    }

    [Fact]
    public void A_success_removes_one_strain()
    {
        MissionState state = Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(1));
        state.Strain = 2;

        MissionStep step = MissionResolver.Advance(state);

        Assert.Equal(1, step.State.Strain);
        Assert.Equal(MissionOutcome.FullSuccess, step.Result!.Outcome);
    }

    [Fact]
    public void The_call_threshold_weighs_the_lead_and_autonomy()
    {
        MissionTuning t = TestContent.Tuning;
        TeamMember cautious = TestContent.Member(1);
        TeamMember reckless = TestContent.Member(2);

        Assert.Equal(new[] { 0.5, 1.5, 2.5 }, new[] { -1, 0, 1 }.Select(a => MissionResolver.AskThreshold(t, cautious, new Parameters(Autonomy: a))));
        Assert.Equal(4.25, MissionResolver.AskThreshold(t, reckless, new Parameters()));
        Assert.Equal(CallAnswer.Safe, MissionResolver.Prefers(t, cautious, new Parameters(Priority: 1)));
        Assert.Equal(CallAnswer.Bold, MissionResolver.Prefers(t, reckless, new Parameters()));
        Assert.Equal(CallAnswer.Bold, MissionResolver.Prefers(t, TestContent.Member(3), new Parameters(Priority: 1)));
    }

    [Fact]
    public void A_cautious_lead_calls_where_a_reckless_one_sends_a_notice()
    {
        MissionStep cautious = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(1, Stakes2)));
        MissionStep reckless = MissionResolver.Advance(Start(TestContent.Team(2, 2, 3), new Parameters(), Encounter(1, Stakes2)));

        Assert.Equal("DECISION_PROMPT_1", cautious.Pending!.DecisionId);
        Assert.Equal(new[] { CallAnswer.Bold, CallAnswer.Safe }, cautious.Pending.Options);
        Assert.Null(reckless.Pending);
        NoticeBeat notice = reckless.Beats.OfType<NoticeBeat>().Single();
        Assert.Equal((CallAnswer.Bold, false), (notice.Chose, notice.Blocked));
        Assert.Equal((0, 1), (reckless.Result!.Calls, reckless.Result.Notices));
    }

    [Fact]
    public void Blocked_comms_force_a_notice_unless_the_team_carries_comms_gear()
    {
        EncounterDef blocked = Encounter(1, Stakes2, commsBlocked: true);

        MissionStep without = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), blocked));
        MissionStep with = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), ["comms"], blocked));

        Assert.True(without.Beats.OfType<NoticeBeat>().Single().Blocked);
        Assert.NotNull(with.Pending);
    }

    [Fact]
    public void Backing_the_lead_raises_their_loyalty_and_overruling_lowers_it()
    {
        MissionStep call = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(1, Stakes2)));

        MissionStep backed = MissionResolver.Advance(call.State, CallAnswer.Safe);
        MissionStep overruled = MissionResolver.Advance(call.State, CallAnswer.Bold);

        Assert.Equal(1, backed.Result!.Crew.Single(c => c.Id == "CREW_01").Loyalty);
        Assert.Equal(-1, overruled.Result!.Crew.Single(c => c.Id == "CREW_01").Loyalty);
        Assert.Equal(2, overruled.Result.Progress);
    }

    [Fact]
    public void Withdrawal_is_offered_only_at_high_stakes()
    {
        MissionStep low = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(1, Stakes2)));
        MissionStep high = MissionResolver.Advance(Start(TestContent.Team(1, 1, 3), new Parameters(), Encounter(1, Stakes3)));

        Assert.Throws<ArgumentException>(() => MissionResolver.Advance(low.State, CallAnswer.Withdraw));
        Assert.Equal(MissionOutcome.Withdrawal, MissionResolver.Advance(high.State, CallAnswer.Withdraw).Result!.Outcome);
    }

    // A reckless lead's threshold (4.25) is above the abort's stakes (4), so only the rule makes this a call.
    [Fact]
    public void At_the_abort_threshold_even_a_reckless_lead_calls()
    {
        MissionState state = Start(TestContent.Team(2, 2, 7), new Parameters(), Encounter(5));
        state.Phase = MissionPhase.Strain;
        state.Strain = 4;

        MissionStep step = MissionResolver.Advance(state);

        Assert.Equal(MissionResolver.AbortDecisionId, step.Pending!.DecisionId);
        Assert.Equal(new[] { CallAnswer.Bold, CallAnswer.Safe }, step.Pending.Options);
        Assert.Equal(MissionOutcome.Withdrawal, MissionResolver.Advance(step.State, CallAnswer.Safe).Result!.Outcome);
        Assert.Null(MissionResolver.Advance(step.State, CallAnswer.Bold).Pending);
    }

    [Fact]
    public void With_comms_blocked_the_lead_decides_the_abort_alone()
    {
        MissionState state = Start(TestContent.Team(2, 2, 7), new Parameters(), Encounter(5, commsBlocked: true));
        state.Phase = MissionPhase.Strain;
        state.Strain = 4;

        MissionStep step = MissionResolver.Advance(state);

        NoticeBeat notice = step.Beats.OfType<NoticeBeat>().Single();
        Assert.Equal((MissionResolver.AbortDecisionId, CallAnswer.Bold, true), (notice.DecisionId, notice.Chose, notice.Blocked));
        Assert.Equal((0, 1), (step.Result!.Calls, step.Result.Notices));
    }

    [Fact]
    public void At_the_strain_cap_the_mission_collapses_with_one_severe_roll_and_light_rolls_for_the_rest()
    {
        MissionState state = Start(TestContent.Team(1, 1, 3, 5), new Parameters(), Encounter(5));
        state.Phase = MissionPhase.Strain;
        state.Strain = 5;

        MissionStep step = MissionResolver.Advance(state);

        Assert.Single(step.Beats.OfType<CollapseBeat>());
        List<InjuryBeat> rolls = step.Beats.OfType<InjuryBeat>().ToList();
        Assert.Equal(1, rolls.Count(r => r.Severity == InjurySeverity.Severe));
        Assert.Equal(2, rolls.Count(r => r.Severity == InjurySeverity.Light));
        Assert.Equal(3, rolls.Select(r => r.CrewId).Distinct().Count());
        Assert.Equal(MissionOutcome.Loss, step.Result!.Outcome);
    }

    [Fact]
    public void High_momentum_skips_a_skippable_stage_at_a_cost()
    {
        MissionState state = Start(TestContent.Team(1, 1, 3), new Parameters(), [], skippable: true, Encounter(5));
        state.Momentum = 4;

        MissionStep step = MissionResolver.Advance(state);

        Assert.Single(step.Beats.OfType<SkipBeat>());
        Assert.Empty(step.Beats.OfType<CheckBeat>());
        Assert.Equal((2, 1), (step.State.Momentum, step.Result!.Progress));
    }

    // Difficulty 40 cannot be met, so every check is a disaster.
    [Fact]
    public void A_disaster_chains_one_extra_encounter_and_only_once()
    {
        EncounterDef chain = Encounter(40, id: "ENCOUNTER_CHAIN", tags: ["chained"], chain: "ENCOUNTER_CHAIN");
        MissionState state = Start(TestContent.Team(1, 1, 3, 5, 7), new Parameters(), Encounter(40, chain: "ENCOUNTER_CHAIN"), chain);

        MissionStep step = MissionResolver.Advance(state);

        Assert.Equal(2, step.Beats.OfType<CheckBeat>().Count(c => c.Tier == Tier.Disaster));
        Assert.Equal("ENCOUNTER_CHAIN", step.Beats.OfType<ChainBeat>().Single().EncounterId);
        Assert.Equal(2, step.State.Encounters.Count);
    }

    [Fact]
    public void The_lead_must_be_on_the_team()
    {
        var team = new Team([TestContent.Member(1)], "CREW_02");

        Assert.Throws<ArgumentException>(() => Start(team, new Parameters(), Encounter(5)));
    }

    private static Approach ApproachFor(Team team, int risk)
    {
        return MissionResolver.Advance(Start(team, new Parameters(Risk: risk), Encounter(5))).Beats.OfType<CheckBeat>().Single().Approach;
    }

    private static EncounterDef Encounter(int difficulty, DecisionDef? decision = null, bool commsBlocked = false, string? gearTag = null, string id = "ENCOUNTER_01", string[]? tags = null, string? chain = null)
    {
        return new EncounterDef(id, tags ?? ["field"], commsBlocked, chain, new ApproachDef("SKILL_A", difficulty, gearTag), new ApproachDef("SKILL_A", difficulty + 1, null), decision);
    }

    private static MissionState Start(Team team, Parameters parameters, params EncounterDef[] pool)
    {
        return Start(team, parameters, TestContent.Gear, pool);
    }

    private static MissionState Start(Team team, Parameters parameters, string[] gear, params EncounterDef[] pool)
    {
        return Start(team, parameters, gear, skippable: false, pool);
    }

    // One stage that draws the single encounter tagged "field".
    private static MissionState Start(Team team, Parameters parameters, string[] gear, bool skippable, params EncounterDef[] pool)
    {
        var objective = new ObjectiveDef("OBJECTIVE_TEMPLATE_1", [], 1, 4, 1, [new StageDef("stage", ["field"], 1, 1, skippable)]);
        return MissionResolver.Start(objective, pool, team, gear, parameters, TestContent.Tuning, seed: 1);
    }
}
```

`tests/Sim.Tests/MissionWalkthroughTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sim.Core.Content;
using Sim.Missions;

namespace Sim.Tests;

// Whole missions on the starter pack, at seeds chosen to show each behaviour the prototype's walkthroughs showed.
public class MissionWalkthroughTests
{
    [Fact]
    public void A_cautious_lead_calls_the_captain_and_brings_the_mission_home()
    {
        (_, List<Beat> beats, MissionResult result) = TestContent.Play(TestContent.Team(1, 1, 3, 5), seed: 1);

        Assert.True(beats.OfType<CallBeat>().Count() >= 2);
        Assert.Equal(MissionOutcome.FullSuccess, result.Outcome);
        Assert.Equal(beats.OfType<CallBeat>().Count(), result.Calls);
    }

    [Fact]
    public void Under_blocked_comms_the_same_lead_must_decide_alone()
    {
        (_, List<Beat> beats, _) = TestContent.Play(TestContent.Team(1, 1, 3, 5), seed: 1);

        Assert.Contains(beats.OfType<NoticeBeat>(), n => n.Blocked && n.Stakes >= n.Threshold);
    }

    [Fact]
    public void A_weak_team_under_a_reckless_lead_can_collapse()
    {
        (_, List<Beat> beats, MissionResult result) = TestContent.Play(TestContent.Team(2, 2, 7, 8), seed: 1);

        Assert.Single(beats.OfType<CollapseBeat>());
        Assert.Equal(MissionOutcome.Loss, result.Outcome);
    }

    [Fact]
    public void The_same_weak_team_is_offered_the_abort_first_when_strain_builds_slowly()
    {
        (_, List<Beat> beats, _) = TestContent.Play(TestContent.Team(2, 2, 7, 8), seed: 1);

        Assert.Contains(beats.OfType<CallBeat>(), c => c.Call.DecisionId == MissionResolver.AbortDecisionId);
    }

    [Fact]
    public void Develop_juniors_gives_the_junior_the_checks_within_reach_and_the_senior_the_rest()
    {
        (_, List<Beat> beats, MissionResult result) = TestContent.Play(TestContent.Team(3, 3, 5, 6), seed: 5, new Parameters(DevelopJuniors: true));

        List<CheckBeat> checks = beats.OfType<CheckBeat>().ToList();
        Assert.Contains(checks, c => c.Supervised);
        Assert.Contains(checks, c => !c.Supervised);
        Assert.All(checks.Where(c => c.Supervised), c => Assert.True(c.ActorSkill <= c.Assist + TestContent.Tuning.SupervisorPenalty));
        Assert.True(result.Crew.Where(c => c.Id != "CREW_03").Sum(c => c.Experience) > 0);
    }

    [Fact]
    public void When_the_lead_falls_the_next_most_senior_takes_over()
    {
        (_, List<Beat> beats, _) = TestContent.Play(TestContent.Team(8, 7, 8), seed: 5);

        Assert.Equal("CREW_07", beats.OfType<LeadChangeBeat>().First().CrewId);
    }

    [Fact]
    public void A_mission_never_changes_the_state_it_was_given_and_always_plays_the_same()
    {
        (MissionState start, List<Beat> first, _) = TestContent.Play(TestContent.Team(1, 1, 3, 5), seed: 1);
        string before = MissionJson.Serialize(start);

        (_, List<Beat> second, _) = TestContent.Play(TestContent.Team(1, 1, 3, 5), seed: 1);

        Assert.Equal(before, MissionJson.Serialize(start));
        Assert.Equal(Json(first), Json(second));
    }

    [Fact]
    public void A_mission_stored_while_a_call_is_pending_resumes_exactly()
    {
        MissionState start = MissionResolver.Start(TestContent.Objective, TestContent.Pack.Encounters, TestContent.Team(1, 1, 3, 5), TestContent.Gear, new Parameters(), TestContent.Tuning, seed: 1);
        MissionStep waiting = MissionResolver.Advance(start);
        Assert.NotNull(waiting.Pending);

        MissionState restored = MissionJson.Deserialize(MissionJson.Serialize(waiting.State));

        Assert.Equal(Json(Finish(waiting.State)), Json(Finish(restored)));
    }

    [Fact]
    public void The_same_seed_draws_the_same_encounters_whatever_team_is_sent()
    {
        (MissionState strong, _, _) = TestContent.Play(TestContent.Team(1, 1, 3, 5), seed: 7);
        (MissionState weak, _, _) = TestContent.Play(TestContent.Team(8, 7, 8), seed: 7);

        Assert.Equal(strong.Encounters.Select(e => e.Encounter.Id), weak.Encounters.Select(e => e.Encounter.Id));
        Assert.Equal(5, strong.Encounters.Count);
    }

    // The content pack may change under a mission in flight; the mission must not.
    [Fact]
    public void A_mission_keeps_the_tuning_it_started_with()
    {
        MissionTuning harsh = TestContent.Tuning with { CheckTarget = 20 };
        MissionState start = MissionResolver.Start(TestContent.Objective, TestContent.Pack.Encounters, TestContent.Team(1, 1, 3, 5), TestContent.Gear, new Parameters(), harsh, seed: 1);

        MissionState restored = MissionJson.Deserialize(MissionJson.Serialize(start));

        Assert.Equal(harsh, restored.Tuning);
        Assert.All(MissionResolver.Advance(restored).Beats.OfType<CheckBeat>(), c => Assert.True(c.Target >= 25));
    }

    // Golden mission: this hash changes only when the resolver's behaviour changes. If the change is intended, update it in the same commit and say why.
    [Fact]
    public void Golden_mission_matches_the_accepted_beats()
    {
        (_, List<Beat> beats, _) = TestContent.Play(TestContent.Team(1, 1, 3, 5), seed: 1);

        Assert.Equal("D745D5ED80EA0F8A9ADDD0971E1B4C8C520DFA7E3870F80C341D0180AF40A074", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json(beats)))));
    }

    private static List<Beat> Finish(MissionState state)
    {
        var beats = new List<Beat>();
        MissionStep step = MissionResolver.Advance(state, state.Pending?.LeadPrefers);
        beats.AddRange(step.Beats);
        while (step.Pending is { } call)
        {
            step = MissionResolver.Advance(step.State, call.LeadPrefers);
            beats.AddRange(step.Beats);
        }

        return beats;
    }

    private static string Json(IEnumerable<Beat> beats)
    {
        return JsonSerializer.Serialize(beats, MissionJson.Options);
    }
}
```


- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sim.Tests`
Expected: FAIL to build: the namespace `Sim.Missions` has no types.

- [ ] **Step 3: Write the contract types, the state, and the beats**

`src/Sim.Missions/MissionTypes.cs`:

```csharp
using Sim.Core.Content;

namespace Sim.Missions;

public enum MemberStatus
{
    Active,
    Incapacitated,
    Dead,
}

public enum MissionOutcome
{
    FullSuccess,
    PartialSuccess,
    Withdrawal,
    Loss,
}

public enum Tier
{
    Critical,
    Success,
    Partial,
    Failure,
    Disaster,
}

public enum Approach
{
    Careful,
    Fast,
}

public enum CallAnswer
{
    Bold,
    Safe,
    Withdraw,
}

public enum InjuryOutcome
{
    Unharmed,
    LightInjury,
    Incapacitated,
    Dead,
}

// The resolver's whole view of a crew member. Caution and discipline run from -2 to +2.
public sealed record TeamMember(string Id, int Seniority, IReadOnlyDictionary<string, int> Skills, int Caution, int Discipline);

public sealed record Team(IReadOnlyList<TeamMember> Members, string LeadId);

// Operational parameters, each from -1 to +1. Risk is risk tolerance: +1 favours the fast approach.
public sealed record Parameters(int Priority = 0, int Risk = 0, int Autonomy = 0, bool DevelopJuniors = false);

public sealed record PendingCall(
    string DecisionId,
    int Stakes,
    double Threshold,
    string LeadId,
    CallAnswer LeadPrefers,
    IReadOnlyList<CallAnswer> Options,
    string Key);

public sealed record CrewResult(string Id, MemberStatus Status, int Injury, double Experience, int Loyalty);

public sealed record MissionResult(MissionOutcome Outcome, int Progress, int Need, int Calls, int Notices, IReadOnlyList<CrewResult> Crew);

public sealed record MissionStep(MissionState State, IReadOnlyList<Beat> Beats, PendingCall? Pending, MissionResult? Result);

public sealed record DrawnEncounter(string Slot, bool Skippable, EncounterDef Encounter);
```

`src/Sim.Missions/MissionState.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Sim.Core.Content;

namespace Sim.Missions;

public enum MissionPhase
{
    Check,
    Decision,
    Strain,
    Advance,
}

public sealed class MemberState
{
    public required TeamMember Member { get; set; }

    public int Injury { get; set; }

    public MemberStatus Status { get; set; }

    public double Experience { get; set; }

    public int Loyalty { get; set; }
}

// A mission in flight (GDD 4.1). Plain data: it carries its own tuning and encounters,
// so it replays the same whatever the content pack says later.
public sealed class MissionState
{
    public ulong Seed { get; set; }

    public string ObjectiveId { get; set; } = "";

    public int Need { get; set; }

    public required MissionTuning Tuning { get; set; }

    public List<DrawnEncounter> Encounters { get; set; } = [];

    public Dictionary<string, EncounterDef> Chains { get; set; } = [];

    public int Cursor { get; set; }

    public MissionPhase Phase { get; set; }

    public int Momentum { get; set; }

    public int Strain { get; set; }

    public int Progress { get; set; }

    public List<MemberState> Team { get; set; } = [];

    public string LeadId { get; set; } = "";

    public List<string> GearTags { get; set; } = [];

    public Parameters Parameters { get; set; } = new();

    public PendingCall? Pending { get; set; }

    public MissionResult? Result { get; set; }

    public bool AbortAsked { get; set; }

    public bool Chained { get; set; }

    public int Calls { get; set; }

    public int Notices { get; set; }
}

public static class MissionJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    public static string Serialize(MissionState state)
    {
        return JsonSerializer.Serialize(state, Options);
    }

    public static MissionState Deserialize(string json)
    {
        return JsonSerializer.Deserialize<MissionState>(json, Options) ?? throw new InvalidOperationException("The mission state is empty.");
    }
}
```

`src/Sim.Missions/Beats.cs`:

```csharp
using System.Text.Json.Serialization;
using Sim.Core.Content;

namespace Sim.Missions;

// One recorded moment of a mission: the raw material for reports and the bench.
[JsonDerivedType(typeof(StageBeat), "stage")]
[JsonDerivedType(typeof(SkipBeat), "skip")]
[JsonDerivedType(typeof(CheckBeat), "check")]
[JsonDerivedType(typeof(EffectsBeat), "effects")]
[JsonDerivedType(typeof(InjuryBeat), "injury")]
[JsonDerivedType(typeof(LeadChangeBeat), "leadChange")]
[JsonDerivedType(typeof(ChainBeat), "chain")]
[JsonDerivedType(typeof(CallBeat), "call")]
[JsonDerivedType(typeof(NoticeBeat), "notice")]
[JsonDerivedType(typeof(AnswerBeat), "answer")]
[JsonDerivedType(typeof(CollapseBeat), "collapse")]
[JsonDerivedType(typeof(ResultBeat), "result")]
public abstract record Beat;

public sealed record StageBeat(int Cursor, string EncounterId, string Slot, bool CommsBlocked, int Momentum, int Strain) : Beat;

public sealed record SkipBeat(string EncounterId) : Beat;

public sealed record CheckBeat(
    string EncounterId,
    Approach Approach,
    string Skill,
    string ActorId,
    string? HelperId,
    bool Supervised,
    int ActorSkill,
    int Assist,
    int Gear,
    int Momentum,
    int Strain,
    int Score,
    int Roll,
    int Target,
    int Margin,
    Tier Tier,
    double Experience,
    bool Mentoring) : Beat;

public sealed record EffectsBeat(EffectDef Effects) : Beat;

public sealed record InjuryBeat(string CrewId, string EncounterId, InjurySeverity Severity, int Roll, InjuryOutcome Outcome) : Beat;

public sealed record LeadChangeBeat(string CrewId) : Beat;

public sealed record ChainBeat(string EncounterId) : Beat;

public sealed record CallBeat(PendingCall Call) : Beat;

public sealed record NoticeBeat(string DecisionId, int Stakes, double Threshold, string LeadId, CallAnswer Chose, bool Blocked) : Beat;

public sealed record AnswerBeat(CallAnswer Answer, bool Backed, string LeadId) : Beat;

public sealed record CollapseBeat : Beat;

public sealed record ResultBeat(MissionOutcome Outcome) : Beat;
```


- [ ] **Step 4: Write the resolver**

One encounter is four phases: check, decision, strain, and advance.

`src/Sim.Missions/MissionResolver.cs`:

```csharp
using Sim.Core;
using Sim.Core.Content;

namespace Sim.Missions;

// The default resolver (GDD 4.6). Pure: the same inputs always give the same step.
public static class MissionResolver
{
    public const string AbortDecisionId = "DECISION_ABORT";
    private const int AverageRoll = 7;

    // drawSeed lets the bench hold the encounters fixed while the dice vary.
    public static MissionState Start(
        ObjectiveDef objective,
        IReadOnlyList<EncounterDef> pool,
        Team team,
        IReadOnlyList<string> gearTags,
        Parameters parameters,
        MissionTuning tuning,
        ulong seed,
        ulong? drawSeed = null)
    {
        if (team.Members.Count == 0 || team.Members.All(m => m.Id != team.LeadId))
        {
            throw new ArgumentException("The team needs at least one member, and its lead must be one of them.", nameof(team));
        }

        var state = new MissionState
        {
            Seed = seed,
            ObjectiveId = objective.Id,
            Need = objective.Need,
            Tuning = tuning,
            Team = team.Members.Select(m => new MemberState { Member = m }).ToList(),
            LeadId = team.LeadId,
            GearTags = gearTags.ToList(),
            Parameters = parameters,
        };

        ulong draw = drawSeed ?? seed;
        var drawn = new HashSet<string>();
        var chainTargets = pool.Where(e => e.Chain is not null).Select(e => e.Chain!).ToHashSet();
        for (int i = 0; i < objective.Stages.Count; i++)
        {
            StageDef stage = objective.Stages[i];
            int count = stage.CountMin + RngStream.For(draw, 0, $"count:{i}").Next(stage.CountMax - stage.CountMin + 1);
            for (int j = 0; j < count; j++)
            {
                List<EncounterDef> candidates = pool
                    .Where(e => !drawn.Contains(e.Id) && !chainTargets.Contains(e.Id) && stage.DrawTags.All(e.Tags.Contains))
                    .OrderBy(e => e.Id, StringComparer.Ordinal)
                    .ToList();
                if (candidates.Count == 0)
                {
                    break;
                }

                EncounterDef encounter = candidates[RngStream.For(draw, 0, $"draw:{i}:{j}").Next(candidates.Count)];
                drawn.Add(encounter.Id);
                state.Encounters.Add(new DrawnEncounter(stage.Slot, stage.Skippable, encounter));
                if (encounter.Chain is not null && pool.FirstOrDefault(e => e.Id == encounter.Chain) is { } chain)
                {
                    state.Chains[chain.Id] = chain;
                }
            }
        }

        return state;
    }

    // Runs until the mission needs the captain's answer or ends. Never changes the state it is given.
    public static MissionStep Advance(MissionState state, CallAnswer? answer = null)
    {
        var run = new Run(MissionJson.Deserialize(MissionJson.Serialize(state)));
        run.Go(answer);
        return new MissionStep(run.State, run.Beats, run.State.Pending, run.State.Result);
    }

    public static double AskThreshold(MissionTuning tuning, TeamMember lead, Parameters parameters)
    {
        return tuning.CallBase - (tuning.CautionWeight * lead.Caution) - (tuning.DisciplineWeight * lead.Discipline) + (tuning.AutonomyWeight * parameters.Autonomy);
    }

    public static CallAnswer Prefers(MissionTuning tuning, TeamMember lead, Parameters parameters)
    {
        return (tuning.BoldPriorityWeight * parameters.Priority) - (tuning.BoldCautionWeight * lead.Caution) > 0 ? CallAnswer.Bold : CallAnswer.Safe;
    }

    private sealed class Run(MissionState state)
    {
        public MissionState State { get; } = state;

        public List<Beat> Beats { get; } = [];

        private MissionTuning T => State.Tuning;

        private List<MemberState> Active => State.Team.Where(m => m.Status == MemberStatus.Active).ToList();

        private MemberState Lead => State.Team.First(m => m.Member.Id == State.LeadId);

        private DrawnEncounter Current => State.Encounters[State.Cursor];

        public void Go(CallAnswer? answer)
        {
            if (State.Pending is { } pending)
            {
                if (answer is not { } given)
                {
                    return;
                }

                Answer(pending, given);
            }

            while (State.Result is null && State.Pending is null)
            {
                if (Active.Count == 0)
                {
                    Finish(MissionOutcome.Loss);
                    break;
                }

                switch (State.Phase)
                {
                    case MissionPhase.Check:
                        if (State.Cursor >= State.Encounters.Count)
                        {
                            Finish(State.Progress >= State.Need ? MissionOutcome.FullSuccess
                                : State.Progress >= T.PartialAtProgress ? MissionOutcome.PartialSuccess
                                : MissionOutcome.Withdrawal);
                            break;
                        }

                        Beats.Add(new StageBeat(State.Cursor, Current.Encounter.Id, Current.Slot, Current.Encounter.CommsBlocked, State.Momentum, State.Strain));
                        if (Current.Skippable && State.Momentum >= T.SkipAtMomentum)
                        {
                            State.Momentum = Clamp(State.Momentum - T.SkipCost);
                            State.Progress++;
                            Beats.Add(new SkipBeat(Current.Encounter.Id));
                            State.Phase = MissionPhase.Strain;
                        }
                        else
                        {
                            Check(Current.Encounter);
                            State.Phase = MissionPhase.Decision;
                        }

                        break;
                    case MissionPhase.Decision:
                        State.Phase = MissionPhase.Strain;
                        if (Current.Encounter.Decision is { } decision && Active.Count > 0)
                        {
                            Raise(decision.Id, decision.Stakes, decision.Bold, decision.Safe, $"dec:{Current.Encounter.Id}", alwaysCall: false);
                        }

                        break;
                    case MissionPhase.Strain:
                        State.Phase = MissionPhase.Advance;
                        if (State.Strain >= T.CollapseAt)
                        {
                            Collapse();
                        }
                        else if (State.Strain >= T.AbortCallAt && !State.AbortAsked)
                        {
                            State.AbortAsked = true;
                            Raise(AbortDecisionId, T.AbortStakes, new EffectDef(), new EffectDef(Withdraw: true), $"abort:{Current.Encounter.Id}", alwaysCall: true);
                        }

                        break;
                    default:
                        State.Cursor++;
                        State.Phase = MissionPhase.Check;
                        break;
                }
            }
        }

        private void Check(EncounterDef encounter)
        {
            Parameters p = State.Parameters;
            Approach approach = Lead.Member.Caution - p.Risk >= 0 ? Approach.Careful : Approach.Fast;
            ApproachDef def = approach == Approach.Careful ? encounter.Careful : encounter.Fast;
            int Skill(MemberState m)
            {
                return Math.Max(0, m.Member.Skills.GetValueOrDefault(def.Skill) - m.Injury);
            }

            List<MemberState> best = Active.OrderByDescending(Skill).ToList();
            int gear = def.GearTag is not null && State.GearTags.Contains(def.GearTag) ? T.GearBonus : 0;
            int target = def.Difficulty + T.CheckTarget;

            // "Develop juniors" only when the junior is within reach under supervision (GDD 4.2).
            bool supervised = p.DevelopJuniors && best.Count > 1;
            if (supervised)
            {
                int expected = Skill(best[^1]) + Math.Max(0, Skill(best[0]) - T.SupervisorPenalty) + gear + State.Momentum - State.Strain + AverageRoll - target;
                supervised = expected >= T.ReachMargin;
            }

            MemberState actor = supervised ? best[^1] : best[0];
            MemberState? helper = supervised ? best[0] : best.Count > 1 ? best[1] : null;
            int assist = helper is null ? 0
                : supervised ? Math.Max(0, Skill(helper) - T.SupervisorPenalty)
                : Skill(helper) / T.AssistDivisor;
            int actorSkill = Skill(actor);
            int momentum = State.Momentum;
            int strain = State.Strain;
            int score = actorSkill + assist + gear + momentum - strain;
            int roll = Dice($"check:{encounter.Id}").TwoD6();
            int margin = score + roll - target;
            Tier tier = margin >= T.TierCritical ? Tier.Critical
                : margin >= T.TierSuccess ? Tier.Success
                : margin >= T.TierPartial ? Tier.Partial
                : margin >= T.TierFailure ? Tier.Failure
                : Tier.Disaster;

            switch (tier)
            {
                case Tier.Critical or Tier.Success:
                    State.Strain = Clamp(State.Strain - T.StrainReliefOnSuccess);
                    State.Progress++;
                    State.Momentum = Clamp(State.Momentum + (tier == Tier.Critical ? T.MomentumCritical : 0) + (approach == Approach.Fast ? T.MomentumFastSuccess : 0));
                    break;
                case Tier.Partial:
                    State.Strain = Clamp(State.Strain + T.StrainPartial);
                    break;
                case Tier.Failure:
                    State.Strain = Clamp(State.Strain + T.StrainFailure);
                    break;
                default:
                    State.Strain = Clamp(State.Strain + T.StrainDisaster);
                    break;
            }

            bool mentoring = helper is not null && Skill(helper) - actorSkill >= T.MentorGap;
            double baseExperience = margin >= T.TrivialAtMargin ? 0
                : Math.Abs(margin) <= T.CloseMargin ? T.ExperienceClose
                : Math.Abs(margin) <= T.NearMargin ? T.ExperienceNear
                : T.ExperienceFar;
            double experience = baseExperience + (mentoring ? T.MentorBonus : 0);
            actor.Experience += experience;
            if (helper is not null)
            {
                helper.Experience += baseExperience * T.AssistShare;
            }

            Beats.Add(new CheckBeat(
                encounter.Id, approach, def.Skill, actor.Member.Id, helper?.Member.Id, supervised,
                actorSkill, assist, gear, momentum, strain, score, roll, target, margin, tier, experience, mentoring));

            if (tier == Tier.Failure)
            {
                InjuryRoll(actor, InjurySeverity.Light, $"checkinj:{encounter.Id}");
            }
            else if (tier == Tier.Disaster)
            {
                InjuryRoll(actor, InjurySeverity.Severe, $"checkinj:{encounter.Id}");
                if (!State.Chained && encounter.Chain is not null && State.Chains.TryGetValue(encounter.Chain, out EncounterDef? chain))
                {
                    State.Chained = true;
                    State.Encounters.Insert(State.Cursor + 1, new DrawnEncounter("chain", false, chain));
                    Beats.Add(new ChainBeat(chain.Id));
                }
            }
        }

        // A decision becomes a call or a notice (GDD 4.4).
        private void Raise(string decisionId, int stakes, EffectDef bold, EffectDef safe, string key, bool alwaysCall)
        {
            TeamMember lead = Lead.Member;
            double threshold = AskThreshold(T, lead, State.Parameters);
            CallAnswer prefers = Prefers(T, lead, State.Parameters);
            bool blocked = Current.Encounter.CommsBlocked && !State.GearTags.Contains("comms");
            if (!blocked && (alwaysCall || stakes >= threshold))
            {
                var options = new List<CallAnswer> { CallAnswer.Bold, CallAnswer.Safe };
                if (stakes >= T.WithdrawOfferedAtStakes && !safe.Withdraw)
                {
                    options.Add(CallAnswer.Withdraw);
                }

                State.Calls++;
                State.Pending = new PendingCall(decisionId, stakes, threshold, lead.Id, prefers, options, key);
                Beats.Add(new CallBeat(State.Pending));
            }
            else
            {
                State.Notices++;
                Beats.Add(new NoticeBeat(decisionId, stakes, threshold, lead.Id, prefers, blocked));
                Apply(prefers == CallAnswer.Bold ? bold : safe, key);
            }
        }

        private void Answer(PendingCall pending, CallAnswer answer)
        {
            if (!pending.Options.Contains(answer))
            {
                throw new ArgumentException($"'{answer}' is not an option for {pending.DecisionId}.", nameof(answer));
            }

            bool backed = answer == pending.LeadPrefers;
            State.Team.First(m => m.Member.Id == pending.LeadId).Loyalty += backed ? T.BackedLoyalty : T.OverruledLoyalty;
            State.Pending = null;
            Beats.Add(new AnswerBeat(answer, backed, pending.LeadId));

            DecisionDef? decision = Current.Encounter.Decision;
            bool abort = pending.DecisionId == AbortDecisionId;
            EffectDef effect = answer switch
            {
                CallAnswer.Withdraw => new EffectDef(Withdraw: true),
                CallAnswer.Bold => abort ? new EffectDef() : decision!.Bold,
                _ => abort ? new EffectDef(Withdraw: true) : decision!.Safe,
            };
            Apply(effect, pending.Key);
        }

        private void Apply(EffectDef effect, string key)
        {
            State.Momentum = Clamp(State.Momentum + effect.Momentum);
            State.Strain = Clamp(State.Strain + effect.Strain);
            State.Progress += effect.Progress;
            Beats.Add(new EffectsBeat(effect));
            if (effect.InjuryRoll is { } severity && Active.Count > 0)
            {
                InjuryRoll(Active[Dice($"{key}:who").Next(Active.Count)], severity, $"{key}:inj");
            }

            if (effect.Withdraw)
            {
                Finish(MissionOutcome.Withdrawal);
            }
        }

        // At the strain cap the mission is lost: one severe roll, and light rolls for the rest.
        private void Collapse()
        {
            Beats.Add(new CollapseBeat());
            List<MemberState> team = Active;
            MemberState worst = team[Dice("collapse:who").Next(team.Count)];
            InjuryRoll(worst, InjurySeverity.Severe, "collapse:severe");
            foreach (MemberState member in team.Where(m => m != worst))
            {
                InjuryRoll(member, InjurySeverity.Light, $"collapse:{member.Member.Id}");
            }

            Finish(MissionOutcome.Loss);
        }

        private void InjuryRoll(MemberState member, InjurySeverity severity, string key)
        {
            int roll = Dice(key).TwoD6() + (State.GearTags.Contains("medical") ? T.MedicalBonus : 0);
            InjuryOutcome outcome = InjuryOutcome.Unharmed;
            if (severity == InjurySeverity.Light)
            {
                if (roll < T.LightBelow)
                {
                    outcome = InjuryOutcome.LightInjury;
                    member.Injury = Math.Min(T.MaxLight, member.Injury + 1);
                }
            }
            else if (roll < T.DeadBelow)
            {
                outcome = InjuryOutcome.Dead;
                member.Status = MemberStatus.Dead;
            }
            else if (roll < T.IncapacitatedBelow)
            {
                outcome = InjuryOutcome.Incapacitated;
                member.Status = MemberStatus.Incapacitated;
            }

            Beats.Add(new InjuryBeat(member.Member.Id, Current.Encounter.Id, severity, roll, outcome));
            if (member.Status != MemberStatus.Active && member.Member.Id == State.LeadId && Active.OrderByDescending(m => m.Member.Seniority).FirstOrDefault() is { } next)
            {
                State.LeadId = next.Member.Id;
                Beats.Add(new LeadChangeBeat(next.Member.Id));
            }
        }

        private void Finish(MissionOutcome outcome)
        {
            State.Result = new MissionResult(
                outcome, State.Progress, State.Need, State.Calls, State.Notices,
                State.Team.Select(m => new CrewResult(m.Member.Id, m.Status, m.Injury, m.Experience, m.Loyalty)).ToList());
            Beats.Add(new ResultBeat(outcome));
        }

        // Dice are keyed, not sequential, so the same seed rolls the same whatever team is sent.
        private RngStream Dice(string key)
        {
            return RngStream.For(State.Seed, 0, key);
        }

        private int Clamp(int value)
        {
            return Math.Clamp(value, 0, T.Cap);
        }
    }
}
```


- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet build --nologo -v quiet && dotnet test --nologo --no-build -v quiet`
Expected: build clean; 78 tests pass (Sim.Tests 70). The walkthrough seeds and the golden hash match without edits.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Add the mission resolver"
```

---

### Task 4: The mission bench library

**Files:**
- Create: `src/MissionBench/BenchOptions.cs`, `BenchFigures.cs`, `TeamGenerator.cs`, `Bench.cs`, `BenchReport.cs`
- Test: `tests/MissionBench.Tests/PrototypeCrew.cs`, `BenchTests.cs`, `ShapeTests.cs`

**Interfaces:**
- Consumes: `MissionResolver.Start` and `Advance`, the beat records, and `ContentPack` (with `Bench` and `Skills`) from Tasks 2 and 3.
- Produces: `BenchOptions(ObjectiveId) { Runs, Seed, Strength, Lead, Parameters, GearTags, Policy, FixedDraw, Team }`; enums `TeamStrength { Rookie, Mixed, Veteran }`, `LeadStyle { Cautious, Average, Reckless }`, and `CallPolicy { BackLead, Bold, Safe, Withdraw }`.
- Produces: `Bench.Run(ContentPack, MissionTuning, BenchOptions) → BenchFigures`; `Bench.Replay(ContentPack, MissionTuning, BenchOptions, ulong seed) → MissionTrace`; `Bench.Answer(CallPolicy, PendingCall) → CallAnswer`.
- Produces: `TeamGenerator.Generate(BenchTuning, TeamStrength, LeadStyle, IReadOnlyList<SkillDef>, ulong seed) → Team`.
- Produces: `BenchReport.Summary(columns, showChange)`, `Encounters(figures)`, `Replay(trace)`, and `Describe(beat)`.

- [ ] **Step 1: Write the failing tests**

`ShapeTests` holds the accepted shape of the mission numbers in wide bands: it catches a rule breaking, not a number being tuned.

`tests/MissionBench.Tests/PrototypeCrew.cs`:

```csharp
using Sim.Content;
using Sim.Core.Content;
using Sim.Missions;

namespace MissionBench.Tests;

// The eight placeholder crew the mission numbers were first benched on (2026-11-13).
internal static class PrototypeCrew
{
    private static readonly Dictionary<int, (int Seniority, int[] Skills, int Caution, int Discipline)> Crew = new()
    {
        [1] = (5, [6, 5, 4, 3], 2, 2),
        [2] = (4, [4, 6, 5, 2], -2, -1),
        [3] = (4, [5, 3, 6, 5], 0, 1),
        [4] = (3, [3, 4, 3, 6], 1, -2),
        [5] = (2, [2, 3, 2, 3], -1, 0),
        [6] = (2, [3, 1, 3, 2], 1, 1),
        [7] = (1, [1, 2, 1, 2], 0, 0),
        [8] = (1, [2, 1, 2, 1], -2, 2),
    };

    public static readonly string[] Gear = ["scanner", "tools"];

    public static ContentPack Pack { get; } = LoadPack();

    public static Team Team(int lead, params int[] members)
    {
        return new Team(members.Select(Member).ToList(), $"CREW_{lead:00}");
    }

    public static TeamMember Member(int n)
    {
        (int seniority, int[] skills, int caution, int discipline) = Crew[n];
        var levels = new Dictionary<string, int> { ["SKILL_A"] = skills[0], ["SKILL_B"] = skills[1], ["SKILL_C"] = skills[2], ["SKILL_D"] = skills[3] };
        return new TeamMember($"CREW_{n:00}", seniority, levels, caution, discipline);
    }

    private static ContentPack LoadPack()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        ContentLoadResult result = ContentLoader.LoadDirectory(Path.Combine(directory!.FullName, "content"));
        return result.Pack ?? throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => $"{e.File}: {e.Message}")));
    }
}
```

`tests/MissionBench.Tests/BenchTests.cs`:

```csharp
using System.Text.Json;
using Sim.Core.Content;
using Sim.Missions;

namespace MissionBench.Tests;

public class BenchTests
{
    private static readonly ContentPack Pack = PrototypeCrew.Pack;

    [Fact]
    public void The_same_seeds_give_the_same_figures()
    {
        var options = new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 200, Seed = 5 };

        BenchFigures first = Bench.Run(Pack, Pack.Tuning, options);
        BenchFigures second = Bench.Run(Pack, Pack.Tuning, options);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(200, first.Outcomes.Values.Sum());
        Assert.Equal(600, first.CrewMissions);
    }

    [Fact]
    public void A_harsher_tuning_moves_the_figures_on_the_same_seeds()
    {
        var options = new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 300 };

        BenchFigures before = Bench.Run(Pack, Pack.Tuning, options);
        BenchFigures after = Bench.Run(Pack, Pack.Tuning with { CheckTarget = 9 }, options);

        Assert.True(after.Outcomes[MissionOutcome.FullSuccess] < before.Outcomes[MissionOutcome.FullSuccess]);
        Assert.Equal(before.Encounters.Select(e => e.EncounterId), after.Encounters.Select(e => e.EncounterId).Where(id => id != "ENCOUNTER_CHAIN"));
    }

    [Fact]
    public void Veterans_succeed_more_often_than_rookies()
    {
        int FullSuccesses(TeamStrength strength)
        {
            return Bench.Run(Pack, Pack.Tuning, new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 300, Strength = strength }).Outcomes[MissionOutcome.FullSuccess];
        }

        Assert.True(FullSuccesses(TeamStrength.Rookie) < FullSuccesses(TeamStrength.Mixed));
        Assert.True(FullSuccesses(TeamStrength.Mixed) < FullSuccesses(TeamStrength.Veteran));
    }

    [Fact]
    public void A_fixed_draw_meets_the_same_encounters_every_run()
    {
        BenchFigures figures = Bench.Run(Pack, Pack.Tuning, new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 100, Strength = TeamStrength.Veteran, FixedDraw = true });

        Assert.Equal(5, figures.Encounters.Count(e => e.EncounterId != "ENCOUNTER_CHAIN"));
    }

    [Fact]
    public void A_run_is_named_by_its_seed_and_replays_the_same()
    {
        var options = new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 50, Strength = TeamStrength.Rookie };
        ulong worst = Bench.Run(Pack, Pack.Tuning, options).WorstSeed;

        MissionTrace first = Bench.Replay(Pack, Pack.Tuning, options, worst);
        MissionTrace second = Bench.Replay(Pack, Pack.Tuning, options, worst);

        Assert.Equal(BenchReport.Replay(first), BenchReport.Replay(second));
        Assert.StartsWith($"Seed {worst}. Lead CREW_01.", BenchReport.Replay(first), StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_teams_follow_the_bench_tuning_and_the_lead_style()
    {
        Team mixed = TeamGenerator.Generate(Pack.Bench, TeamStrength.Mixed, LeadStyle.Reckless, Pack.Skills, seed: 3);
        Team again = TeamGenerator.Generate(Pack.Bench, TeamStrength.Mixed, LeadStyle.Reckless, Pack.Skills, seed: 3);

        Assert.Equal(JsonSerializer.Serialize(mixed), JsonSerializer.Serialize(again));
        Assert.Equal(("CREW_01", 3), (mixed.LeadId, mixed.Members.Count));
        Assert.Equal((-2, -1), (mixed.Members[0].Caution, mixed.Members[0].Discipline));
        Assert.All(mixed.Members[0].Skills.Values, level => Assert.InRange(level, 5, 6));
        Assert.All(mixed.Members.Skip(1).SelectMany(m => m.Skills.Values), level => Assert.InRange(level, 2, 4));
        Assert.All(mixed.Members.Skip(1), m => Assert.InRange(m.Caution, -1, 1));
    }

    [Fact]
    public void Each_policy_answers_calls_its_own_way()
    {
        var call = new PendingCall("DECISION_PROMPT_1", 3, 1.5, "CREW_01", CallAnswer.Bold, [CallAnswer.Bold, CallAnswer.Safe, CallAnswer.Withdraw], "dec:0");
        PendingCall abort = call with { DecisionId = MissionResolver.AbortDecisionId, Options = [CallAnswer.Bold, CallAnswer.Safe] };

        Assert.Equal(CallAnswer.Bold, Bench.Answer(CallPolicy.BackLead, call));
        Assert.Equal(CallAnswer.Safe, Bench.Answer(CallPolicy.BackLead, abort));
        Assert.Equal(CallAnswer.Safe, Bench.Answer(CallPolicy.Safe, call));
        Assert.Equal(CallAnswer.Withdraw, Bench.Answer(CallPolicy.Withdraw, call));
        Assert.Equal(CallAnswer.Safe, Bench.Answer(CallPolicy.Withdraw, abort));
    }

    [Fact]
    public void The_summary_shows_before_after_and_change()
    {
        var options = new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 100 };
        BenchFigures figures = Bench.Run(Pack, Pack.Tuning, options);

        string same = BenchReport.Summary([("before", figures), ("after", figures)], showChange: true);

        Assert.Contains("before", same.Split('\n')[0], StringComparison.Ordinal);
        Assert.Contains("change", same.Split('\n')[0], StringComparison.Ordinal);
        Assert.All(same.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1), row => Assert.Matches(@"(\+0\.0+|-)$", row));
    }
}
```

`tests/MissionBench.Tests/ShapeTests.cs`:

```csharp
using Sim.Missions;

namespace MissionBench.Tests;

// The accepted shape of the mission numbers (GDD 4.4 and 4.7), on the crew they were first benched on.
// The bands are wide on purpose: they catch a rule breaking, not a number being tuned.
public class ShapeTests
{
    [Theory]
    [InlineData(1, new[] { 1, 3, 5 }, 2.6, 3.6, 2.0, 3.0, 0.8, 1.8)]
    [InlineData(3, new[] { 3, 4, 5 }, 2.0, 3.0, 0.8, 1.8, 0.3, 1.1)]
    [InlineData(2, new[] { 2, 3, 5 }, 0.4, 1.2, 0.0, 0.5, 0.0, 0.5)]
    public void Calls_per_mission_fall_as_the_lead_gets_bolder_and_autonomy_rises(int lead, int[] team, double lowMin, double lowMax, double midMin, double midMax, double highMin, double highMax)
    {
        double[] calls = new[] { -1, 0, 1 }.Select(a => Calls(Run(lead, team, new Parameters(Autonomy: a)))).ToArray();

        Assert.InRange(calls[0], lowMin, lowMax);
        Assert.InRange(calls[1], midMin, midMax);
        Assert.InRange(calls[2], highMin, highMax);
    }

    [Fact]
    public void A_reckless_lead_loses_more_and_calls_less_than_a_cautious_one()
    {
        BenchFigures cautious = Run(1, [1, 3, 5], new Parameters());
        BenchFigures reckless = Run(2, [2, 3, 5], new Parameters());

        Assert.InRange(100.0 * reckless.Outcomes[MissionOutcome.Loss] / reckless.Runs, 8, 24);
        Assert.InRange(100.0 * reckless.Deaths / reckless.CrewMissions, 2, 6);
        Assert.True(cautious.Outcomes[MissionOutcome.Loss] < reckless.Outcomes[MissionOutcome.Loss]);
    }

    [Fact]
    public void Develop_juniors_costs_some_success_and_teaches_the_juniors()
    {
        BenchFigures best = Run(3, [3, 5, 6], new Parameters());
        BenchFigures develop = Run(3, [3, 5, 6], new Parameters(DevelopJuniors: true));

        double gap = 100.0 * (best.Outcomes[MissionOutcome.FullSuccess] - develop.Outcomes[MissionOutcome.FullSuccess]) / best.Runs;
        Assert.InRange(gap, 2, 20);
        Assert.True(develop.ExperienceByMember["CREW_05"] > 1.5 * best.ExperienceByMember["CREW_05"]);
        Assert.True(develop.ExperienceByMember["CREW_03"] < best.ExperienceByMember["CREW_03"]);
    }

    private static BenchFigures Run(int lead, int[] team, Parameters parameters)
    {
        return Bench.Run(PrototypeCrew.Pack, PrototypeCrew.Pack.Tuning, new BenchOptions("OBJECTIVE_TEMPLATE_1")
        {
            Runs = 1000,
            Team = PrototypeCrew.Team(lead, team),
            GearTags = PrototypeCrew.Gear,
            Parameters = parameters,
        });
    }

    private static double Calls(BenchFigures figures)
    {
        return (double)figures.Calls / figures.Runs;
    }
}
```


- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MissionBench.Tests`
Expected: FAIL to build: `Bench` and `BenchOptions` do not exist.

- [ ] **Step 3: Write the options, figures, and team generator**

`src/MissionBench/BenchOptions.cs`:

```csharp
using Sim.Missions;

namespace MissionBench;

public enum TeamStrength
{
    Rookie,
    Mixed,
    Veteran,
}

public enum LeadStyle
{
    Cautious,
    Average,
    Reckless,
}

// How the bench answers calls. BackLead backs the lead's preference, but takes the withdrawal when the abort call comes.
public enum CallPolicy
{
    BackLead,
    Bold,
    Safe,
    Withdraw,
}

public sealed record BenchOptions(string ObjectiveId)
{
    public int Runs { get; init; } = 10000;

    public ulong Seed { get; init; } = 1;

    public TeamStrength Strength { get; init; } = TeamStrength.Mixed;

    public LeadStyle Lead { get; init; } = LeadStyle.Average;

    public Parameters Parameters { get; init; } = new();

    public IReadOnlyList<string> GearTags { get; init; } = [];

    public CallPolicy Policy { get; init; } = CallPolicy.BackLead;

    public bool FixedDraw { get; init; }

    // A real team, used for every run in place of generated ones.
    public Team? Team { get; init; }
}
```

`src/MissionBench/BenchFigures.cs`:

```csharp
using Sim.Missions;

namespace MissionBench;

public sealed record EncounterFigures(string EncounterId, int Checks, int Careful, int Fast, IReadOnlyDictionary<Tier, int> Tiers, int Deaths, int Incapacitated);

public sealed record StageFigures(int Stage, int Reached, double Momentum, double Strain);

// Counts and means from one bench (GDD 10.1). Rates are derived when printed.
public sealed record BenchFigures(
    int Runs,
    int CrewMissions,
    IReadOnlyDictionary<MissionOutcome, int> Outcomes,
    int Deaths,
    int Incapacitated,
    int Calls,
    int Notices,
    IReadOnlyDictionary<string, double> ExperienceByMember,
    IReadOnlyList<EncounterFigures> Encounters,
    IReadOnlyList<StageFigures> Stages,
    ulong WorstSeed);

public sealed record MissionTrace(ulong Seed, Team Team, IReadOnlyList<Beat> Beats, MissionResult Result);
```

`src/MissionBench/TeamGenerator.cs`:

```csharp
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;

namespace MissionBench;

public static class TeamGenerator
{
    // The first member is the lead and the most senior. Keyed by seed, so the same seed gives the same team under any tuning of the resolver.
    public static Team Generate(BenchTuning tuning, TeamStrength strength, LeadStyle lead, IReadOnlyList<SkillDef> skills, ulong seed)
    {
        BenchTeamDef def = strength switch
        {
            TeamStrength.Rookie => tuning.Rookie,
            TeamStrength.Veteran => tuning.Veteran,
            _ => tuning.Mixed,
        };
        (int caution, int discipline) = lead switch
        {
            LeadStyle.Cautious => (2, 1),
            LeadStyle.Reckless => (-2, -1),
            _ => (0, 0),
        };

        var members = new List<TeamMember>();
        for (int i = 0; i < def.Size; i++)
        {
            (int min, int max) = i < def.Seniors ? (def.SeniorSkillMin, def.SeniorSkillMax) : (def.SkillMin, def.SkillMax);
            Dictionary<string, int> levels = skills.ToDictionary(s => s.Id, s => min + RngStream.For(seed, 0, $"team:{i}:{s.Id}").Next(max - min + 1));
            members.Add(new TeamMember(
                $"CREW_{i + 1:00}",
                def.Size - i,
                levels,
                i == 0 ? caution : RngStream.For(seed, 0, $"team:{i}:caution").Next(3) - 1,
                i == 0 ? discipline : RngStream.For(seed, 0, $"team:{i}:discipline").Next(3) - 1));
        }

        return new Team(members, members[0].Id);
    }
}
```


- [ ] **Step 4: Write the run loop and the report**

`src/MissionBench/Bench.cs`:

```csharp
using Sim.Core.Content;
using Sim.Missions;

namespace MissionBench;

// Runs one mission many times in memory. It holds no legacy and writes nothing.
public static class Bench
{
    public static BenchFigures Run(ContentPack pack, MissionTuning tuning, BenchOptions options)
    {
        var outcomes = Enum.GetValues<MissionOutcome>().ToDictionary(o => o, _ => 0);
        var experience = new SortedDictionary<string, double>(StringComparer.Ordinal);
        var encounters = new SortedDictionary<string, EncounterTally>(StringComparer.Ordinal);
        var stages = new List<StageTally>();
        int crewMissions = 0, deaths = 0, incapacitated = 0, calls = 0, notices = 0, worst = -1;
        ulong worstSeed = options.Seed;

        for (int i = 0; i < options.Runs; i++)
        {
            MissionTrace trace = Replay(pack, tuning, options, options.Seed + (ulong)i);
            outcomes[trace.Result.Outcome]++;
            calls += trace.Result.Calls;
            notices += trace.Result.Notices;
            crewMissions += trace.Result.Crew.Count;
            int harm = 0;
            foreach (CrewResult crew in trace.Result.Crew)
            {
                experience[crew.Id] = experience.GetValueOrDefault(crew.Id) + crew.Experience;
                deaths += crew.Status == MemberStatus.Dead ? 1 : 0;
                incapacitated += crew.Status == MemberStatus.Incapacitated ? 1 : 0;
                harm += crew.Status == MemberStatus.Dead ? 10 : crew.Status == MemberStatus.Incapacitated ? 1 : 0;
            }

            if (harm > worst)
            {
                worst = harm;
                worstSeed = trace.Seed;
            }

            foreach (Beat beat in trace.Beats)
            {
                switch (beat)
                {
                    case StageBeat stage:
                        while (stages.Count <= stage.Cursor)
                        {
                            stages.Add(new StageTally());
                        }

                        stages[stage.Cursor].Reached++;
                        stages[stage.Cursor].Momentum += stage.Momentum;
                        stages[stage.Cursor].Strain += stage.Strain;
                        break;
                    case CheckBeat check:
                        EncounterTally tally = Tally(encounters, check.EncounterId);
                        tally.Checks++;
                        tally.Careful += check.Approach == Approach.Careful ? 1 : 0;
                        tally.Tiers[check.Tier]++;
                        break;
                    case InjuryBeat { Outcome: InjuryOutcome.Dead } injury:
                        Tally(encounters, injury.EncounterId).Deaths++;
                        break;
                    case InjuryBeat { Outcome: InjuryOutcome.Incapacitated } injury:
                        Tally(encounters, injury.EncounterId).Incapacitated++;
                        break;
                    default:
                        break;
                }
            }
        }

        return new BenchFigures(
            options.Runs,
            crewMissions,
            outcomes,
            deaths,
            incapacitated,
            calls,
            notices,
            experience.ToDictionary(e => e.Key, e => e.Value / options.Runs),
            encounters.Select(e => new EncounterFigures(e.Key, e.Value.Checks, e.Value.Careful, e.Value.Checks - e.Value.Careful, e.Value.Tiers, e.Value.Deaths, e.Value.Incapacitated)).ToList(),
            stages.Select((s, i) => new StageFigures(i + 1, s.Reached, s.Momentum / s.Reached, s.Strain / s.Reached)).ToList(),
            worstSeed);
    }

    // One run, named by its seed, with every beat kept.
    public static MissionTrace Replay(ContentPack pack, MissionTuning tuning, BenchOptions options, ulong seed)
    {
        ObjectiveDef objective = pack.Objectives.FirstOrDefault(o => o.Id == options.ObjectiveId)
            ?? throw new ArgumentException($"Unknown objective '{options.ObjectiveId}'.", nameof(options));
        Team team = options.Team ?? TeamGenerator.Generate(pack.Bench, options.Strength, options.Lead, pack.Skills, seed);
        var beats = new List<Beat>();
        MissionStep step = MissionResolver.Advance(MissionResolver.Start(
            objective, pack.Encounters, team, options.GearTags, options.Parameters, tuning, seed, options.FixedDraw ? options.Seed : null));
        beats.AddRange(step.Beats);
        while (step.Pending is { } call)
        {
            step = MissionResolver.Advance(step.State, Answer(options.Policy, call));
            beats.AddRange(step.Beats);
        }

        return new MissionTrace(seed, team, beats, step.Result!);
    }

    public static CallAnswer Answer(CallPolicy policy, PendingCall call)
    {
        return policy switch
        {
            CallPolicy.Bold => CallAnswer.Bold,
            CallPolicy.Safe => CallAnswer.Safe,
            CallPolicy.Withdraw => call.Options.Contains(CallAnswer.Withdraw) ? CallAnswer.Withdraw : CallAnswer.Safe,
            _ => call.DecisionId == MissionResolver.AbortDecisionId ? CallAnswer.Safe : call.LeadPrefers,
        };
    }

    private static EncounterTally Tally(SortedDictionary<string, EncounterTally> encounters, string id)
    {
        if (!encounters.TryGetValue(id, out EncounterTally? tally))
        {
            tally = new EncounterTally();
            encounters[id] = tally;
        }

        return tally;
    }

    private sealed class EncounterTally
    {
        public int Checks { get; set; }

        public int Careful { get; set; }

        public int Deaths { get; set; }

        public int Incapacitated { get; set; }

        public Dictionary<Tier, int> Tiers { get; } = Enum.GetValues<Tier>().ToDictionary(t => t, _ => 0);
    }

    private sealed class StageTally
    {
        public int Reached { get; set; }

        public double Momentum { get; set; }

        public double Strain { get; set; }
    }
}
```

`src/MissionBench/BenchReport.cs`:

```csharp
using System.Globalization;
using System.Text;
using Sim.Missions;

namespace MissionBench;

// Plain-text tables for the command line. The same figures feed the tuning page later.
public static class BenchReport
{
    // One column per bench. With showChange, a last column gives the second minus the first.
    public static string Summary(IReadOnlyList<(string Name, BenchFigures Figures)> columns, bool showChange)
    {
        var rows = new List<(string Label, Func<BenchFigures, double?> Value, string Format)>
        {
            ("Full success %", f => Share(f, MissionOutcome.FullSuccess), "0.0"),
            ("Partial success %", f => Share(f, MissionOutcome.PartialSuccess), "0.0"),
            ("Withdrawal %", f => Share(f, MissionOutcome.Withdrawal), "0.0"),
            ("Loss %", f => Share(f, MissionOutcome.Loss), "0.0"),
            ("Deaths per 100 crew-missions", f => 100.0 * f.Deaths / f.CrewMissions, "0.0"),
            ("Incapacitated per 100 crew-missions", f => 100.0 * f.Incapacitated / f.CrewMissions, "0.0"),
            ("Calls per mission", f => (double)f.Calls / f.Runs, "0.00"),
            ("Notices per mission", f => (double)f.Notices / f.Runs, "0.00"),
        };
        foreach (string member in columns.SelectMany(c => c.Figures.ExperienceByMember.Keys).Distinct().Order(StringComparer.Ordinal))
        {
            rows.Add(($"Experience, {member}", f => f.ExperienceByMember.TryGetValue(member, out double xp) ? xp : null, "0.0"));
        }

        int stages = columns.Max(c => c.Figures.Stages.Count);
        for (int i = 0; i < stages; i++)
        {
            int stage = i;
            rows.Add(($"Momentum entering stage {i + 1}", f => stage < f.Stages.Count ? f.Stages[stage].Momentum : null, "0.00"));
            rows.Add(($"Strain entering stage {i + 1}", f => stage < f.Stages.Count ? f.Stages[stage].Strain : null, "0.00"));
        }

        var table = new List<string[]> { new[] { "" }.Concat(columns.Select(c => c.Name)).Concat(showChange ? ["change"] : Array.Empty<string>()).ToArray() };
        foreach ((string label, Func<BenchFigures, double?> value, string format) in rows)
        {
            List<double?> values = columns.Select(c => value(c.Figures)).ToList();
            var cells = new List<string> { label };
            cells.AddRange(values.Select(v => Number(v, format)));
            if (showChange)
            {
                double? change = values[0] is { } before && values[1] is { } after ? after - before : null;
                cells.Add(change is { } c ? (c >= 0 ? "+" : "") + Number(c, format) : "-");
            }

            table.Add(cells.ToArray());
        }

        return Render(table);
    }

    public static string Encounters(BenchFigures figures)
    {
        var table = new List<string[]> { new[] { "Encounter", "checks", "careful %", "crit %", "succ %", "part %", "fail %", "dis %", "deaths", "incap." } };
        foreach (EncounterFigures e in figures.Encounters)
        {
            string Tier(Tier tier)
            {
                return Number(e.Checks == 0 ? null : 100.0 * e.Tiers[tier] / e.Checks, "0");
            }

            table.Add(
            [
                e.EncounterId, e.Checks.ToString(CultureInfo.InvariantCulture), Number(e.Checks == 0 ? null : 100.0 * e.Careful / e.Checks, "0"),
                Tier(Sim.Missions.Tier.Critical), Tier(Sim.Missions.Tier.Success), Tier(Sim.Missions.Tier.Partial), Tier(Sim.Missions.Tier.Failure), Tier(Sim.Missions.Tier.Disaster),
                e.Deaths.ToString(CultureInfo.InvariantCulture), e.Incapacitated.ToString(CultureInfo.InvariantCulture),
            ]);
        }

        return Render(table);
    }

    public static string Replay(MissionTrace trace)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Seed {trace.Seed}. Lead {trace.Team.LeadId}.");
        foreach (TeamMember m in trace.Team.Members)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {m.Id}: {string.Join(", ", m.Skills.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => $"{s.Key} {s.Value}"))}; caution {m.Caution}, discipline {m.Discipline}");
        }

        foreach (Beat beat in trace.Beats)
        {
            text.AppendLine(Describe(beat));
        }

        return text.ToString();
    }

    public static string Describe(Beat beat)
    {
        return beat switch
        {
            StageBeat b => $"Stage {b.Cursor + 1} ({b.Slot}): {b.EncounterId}{(b.CommsBlocked ? ", comms blocked" : "")}. Momentum {b.Momentum}, strain {b.Strain}.",
            SkipBeat b => $"  Skipped {b.EncounterId} on momentum.",
            CheckBeat b => $"  {b.Approach} {b.Skill}: {b.ActorId} {b.ActorSkill}{(b.HelperId is null ? "" : $" + {b.HelperId} {b.Assist}{(b.Supervised ? " supervising" : "")}")} + gear {b.Gear} + momentum {b.Momentum} - strain {b.Strain} = {b.Score}; rolled {b.Roll} against {b.Target}: margin {b.Margin}, {b.Tier}. Experience {b.Experience.ToString("0.0", CultureInfo.InvariantCulture)}{(b.Mentoring ? " (mentored)" : "")}.",
            EffectsBeat b => $"  Effects: momentum {b.Effects.Momentum:+0;-0;0}, strain {b.Effects.Strain:+0;-0;0}, progress {b.Effects.Progress:+0;-0;0}{(b.Effects.InjuryRoll is { } r ? $", {r} injury roll" : "")}{(b.Effects.Withdraw ? ", withdraw" : "")}.",
            InjuryBeat b => $"  {b.Severity} injury roll for {b.CrewId}: {b.Roll}, {b.Outcome}.",
            LeadChangeBeat b => $"  {b.CrewId} takes the lead.",
            ChainBeat b => $"  The disaster adds {b.EncounterId}.",
            CallBeat b => $"  CALL {b.Call.DecisionId} (stakes {b.Call.Stakes}, threshold {b.Call.Threshold.ToString("0.00", CultureInfo.InvariantCulture)}): {b.Call.LeadId} prefers {b.Call.LeadPrefers}.",
            NoticeBeat b => $"  Notice {b.DecisionId} (stakes {b.Stakes}, threshold {b.Threshold.ToString("0.00", CultureInfo.InvariantCulture)}): {b.LeadId} chose {b.Chose}{(b.Blocked ? ", comms blocked" : "")}.",
            AnswerBeat b => $"  The captain answers {b.Answer}{(b.Backed ? ", backing the lead" : ", overruling the lead")}.",
            CollapseBeat => "  The mission collapses under strain.",
            ResultBeat b => $"Result: {b.Outcome}.",
            _ => beat.ToString() ?? "",
        };
    }

    private static double Share(BenchFigures figures, MissionOutcome outcome)
    {
        return 100.0 * figures.Outcomes[outcome] / figures.Runs;
    }

    private static string Number(double? value, string format)
    {
        return value is { } v ? v.ToString(format, CultureInfo.InvariantCulture) : "-";
    }

    private static string Render(List<string[]> table)
    {
        int[] widths = Enumerable.Range(0, table[0].Length).Select(i => table.Max(r => r[i].Length)).ToArray();
        var text = new StringBuilder();
        foreach (string[] row in table)
        {
            text.AppendLine(string.Join("  ", row.Select((cell, i) => i == 0 ? cell.PadRight(widths[i]) : cell.PadLeft(widths[i]))).TrimEnd());
        }

        return text.ToString();
    }
}
```


- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet build --nologo -v quiet && dotnet test --nologo --no-build -v quiet`
Expected: build clean; 91 tests pass (MissionBench.Tests 13). The shape tests take about two seconds.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Add the mission bench library"
```

---

### Task 5: The bench command, strict flags, and the gate

**Files:**
- Create: `src/SimRunner/CommandLine.cs`, `Cli.cs`
- Modify: `src/SimRunner/Program.cs` (replace), `check.sh` (replace)
- Test: `tests/SimRunner.Tests/CliTests.cs`

**Interfaces:**
- Consumes: `Bench`, `BenchOptions`, `BenchReport`, `ContentLoader.LoadDirectory`, `LoadTuningText`, and `Simulation.Run`.
- Produces: `Cli.Run(string[] args, TextWriter output, TextWriter error) → int` (0 done, 1 content error, and 2 bad usage); the commands `SimRunner cycles` and `SimRunner bench`.

- [ ] **Step 1: Write the failing tests**

`tests/SimRunner.Tests/CliTests.cs`:

```csharp
using SimRunner;

namespace SimRunner.Tests;

public class CliTests
{
    private static readonly string Content = Path.Combine(FindRoot(), "content");

    [Theory]
    [InlineData()]
    [InlineData("fly")]
    [InlineData("cycles", "--cycles", "abc")]
    [InlineData("cycles", "--cycles")]
    [InlineData("cycles", "--speed", "3")]
    [InlineData("bench")]
    [InlineData("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--team", "heroes")]
    [InlineData("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--autonomy", "2")]
    [InlineData("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--policy", "random")]
    [InlineData("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--team", "sweep", "--replay", "3")]
    public void Bad_usage_exits_2_and_prints_the_usage(params string[] args)
    {
        (int exit, string output, string error) = Run(args);

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("Usage:", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_content_or_an_unknown_objective_exits_1()
    {
        Assert.Equal(1, Run("cycles", "--content", "no-such-folder").Exit);
        (int exit, _, string error) = Run("bench", "--objective", "OBJECTIVE_TEMPLATE_9", "--content", Content);

        Assert.Equal(1, exit);
        Assert.Contains("OBJECTIVE_TEMPLATE_1", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Cycles_runs_and_writes_its_summary()
    {
        string file = Path.Combine(Path.GetTempPath(), $"simrunner-{Guid.NewGuid():N}.json");
        try
        {
            (int exit, string output, _) = Run("cycles", "--legacies", "2", "--cycles", "3", "--content", Content, "--out", file);

            Assert.Equal(0, exit);
            Assert.Equal("Content CORE v1: 2 legacies x 3 cycles, 36 events. OK.", output.Trim());
            Assert.Contains("LogHash", File.ReadAllText(file), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Bench_prints_the_figures()
    {
        (int exit, string output, string error) = Run("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--runs", "50", "--lead", "cautious", "--autonomy", "-1", "--gear", "scanner,tools", "--content", Content);

        Assert.Equal((0, ""), (exit, error));
        Assert.Contains("OBJECTIVE_TEMPLATE_1: 50 missions from seed 1; lead Cautious, policy BackLead.", output, StringComparison.Ordinal);
        Assert.Contains("Full success %", output, StringComparison.Ordinal);
        Assert.Contains("Encounters (mixed); worst run: seed", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sweep_shows_the_three_strengths_side_by_side()
    {
        (int exit, string output, _) = Run("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--runs", "20", "--team", "sweep", "--content", Content);

        Assert.Equal(0, exit);
        Assert.Matches(@"rookie\s+mixed\s+veteran", output);
    }

    // The tuning loop: the same seeds under the pack's tuning and under an edited copy.
    [Fact]
    public void Compare_shows_before_after_and_change()
    {
        string file = Path.Combine(Path.GetTempPath(), $"tuning-{Guid.NewGuid():N}.yaml");
        try
        {
            File.WriteAllText(file, File.ReadAllText(Path.Combine(Content, "tuning", "missions.yaml")).Replace("  target: 7 ", "  target: 9 ", StringComparison.Ordinal));

            (int exit, string output, _) = Run("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--runs", "200", "--compare", file, "--content", Content);

            Assert.Equal(0, exit);
            Assert.Matches(@"before\s+after\s+change", output);
            Assert.Matches(@"Full success %\s+\S+\s+\S+\s+-\d", output);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void A_broken_comparison_file_exits_1_and_names_the_value()
    {
        string file = Path.Combine(Path.GetTempPath(), $"tuning-{Guid.NewGuid():N}.yaml");
        try
        {
            File.WriteAllText(file, "check:\n  target: 7\n");

            (int exit, _, string error) = Run("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--compare", file, "--content", Content);

            Assert.Equal(1, exit);
            Assert.Contains("check.gearBonus is missing.", error, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Replay_prints_one_mission_beat_by_beat()
    {
        (int exit, string output, _) = Run("bench", "--objective", "OBJECTIVE_TEMPLATE_1", "--replay", "7", "--content", Content);

        Assert.Equal(0, exit);
        Assert.StartsWith("Seed 7. Lead CREW_01.", output, StringComparison.Ordinal);
        Assert.Contains("Stage 1 (approach):", output, StringComparison.Ordinal);
        Assert.Contains("Result:", output, StringComparison.Ordinal);
    }

    private static (int Exit, string Output, string Error) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exit = Cli.Run(args, output, error);
        return (exit, output.ToString(), error.ToString());
    }

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
```


- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SimRunner.Tests`
Expected: FAIL to build: `Cli` does not exist.

- [ ] **Step 3: Write the flag parser and the commands**

A bad value, a flag with no value, or an unknown flag is a usage error. Nothing falls back silently.

`src/SimRunner/CommandLine.cs`:

```csharp
using System.Globalization;

namespace SimRunner;

public sealed class UsageException(string message) : Exception(message);

// Flags after the command name. Anything unexpected is a usage error, never a silent default.
public sealed class CommandLine
{
    private readonly Dictionary<string, string> _values = [];
    private readonly HashSet<string> _switches = [];

    public CommandLine(IReadOnlyList<string> args, IReadOnlyCollection<string> valueFlags, IReadOnlyCollection<string> switches)
    {
        for (int i = 0; i < args.Count; i++)
        {
            string flag = args[i];
            if (switches.Contains(flag))
            {
                _switches.Add(flag);
            }
            else if (valueFlags.Contains(flag))
            {
                if (i + 1 >= args.Count)
                {
                    throw new UsageException($"{flag} needs a value.");
                }

                _values[flag] = args[++i];
            }
            else
            {
                throw new UsageException($"Unknown option '{flag}'.");
            }
        }
    }

    public bool Has(string flag)
    {
        return _switches.Contains(flag) || _values.ContainsKey(flag);
    }

    public string Text(string flag, string fallback)
    {
        return _values.GetValueOrDefault(flag, fallback);
    }

    public int Int(string flag, int fallback, int min = int.MinValue, int max = int.MaxValue)
    {
        if (!_values.TryGetValue(flag, out string? text))
        {
            return fallback;
        }

        if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) && value >= min && value <= max)
        {
            return value;
        }

        throw new UsageException(min == int.MinValue ? $"{flag} must be a whole number, not '{text}'." : $"{flag} must be a whole number from {min} to {max}, not '{text}'.");
    }

    public ulong Seed(string flag, ulong fallback)
    {
        if (!_values.TryGetValue(flag, out string? text))
        {
            return fallback;
        }

        return ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value) ? value : throw new UsageException($"{flag} must be a whole number of 0 or more, not '{text}'.");
    }

    public T Choice<T>(string flag, T fallback)
        where T : struct, Enum
    {
        if (!_values.TryGetValue(flag, out string? text))
        {
            return fallback;
        }

        foreach (T value in Enum.GetValues<T>())
        {
            if (string.Equals(Kebab(value.ToString()), text, StringComparison.Ordinal))
            {
                return value;
            }
        }

        throw new UsageException($"{flag} must be one of {string.Join(", ", Enum.GetValues<T>().Select(v => Kebab(v.ToString())))}, not '{text}'.");
    }

    private static string Kebab(string name)
    {
        return string.Concat(name.Select((c, i) => char.IsUpper(c) ? (i > 0 ? "-" : "") + char.ToLowerInvariant(c) : c.ToString()));
    }
}
```

`src/SimRunner/Cli.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using MissionBench;
using Sim.Content;
using Sim.Core.Content;
using Sim.Missions;

namespace SimRunner;

// The command line. Exit codes: 0 done, 1 content error, and 2 bad usage.
public static class Cli
{
    public const string Usage = """
        Usage:
          SimRunner cycles [--legacies N] [--cycles N] [--seed N] [--content DIR] [--out FILE]
          SimRunner bench --objective ID [--runs N] [--seed N]
                          [--team rookie|mixed|veteran|sweep] [--lead cautious|average|reckless]
                          [--autonomy -1|0|1] [--risk -1|0|1] [--priority -1|0|1] [--develop]
                          [--gear TAG,TAG] [--policy back-lead|bold|safe|withdraw]
                          [--fixed-draw] [--compare TUNING_FILE] [--replay SEED]
                          [--content DIR] [--out FILE]
        """;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            return args.FirstOrDefault() switch
            {
                "cycles" => Cycles(args[1..], output, error),
                "bench" => BenchCommand(args[1..], output, error),
                _ => throw new UsageException(args.Length == 0 ? "No command given." : $"Unknown command '{args[0]}'."),
            };
        }
        catch (UsageException e)
        {
            error.WriteLine(e.Message);
            error.WriteLine(Usage);
            return 2;
        }
    }

    private static int Cycles(string[] args, TextWriter output, TextWriter error)
    {
        var line = new CommandLine(args, ["--legacies", "--cycles", "--seed", "--content", "--out"], []);
        var options = new SimOptions(line.Int("--legacies", 200, 1, 1_000_000), line.Int("--cycles", 500, 1, 1_000_000), line.Seed("--seed", 1));
        if (LoadContent(line.Text("--content", "content"), error) is not { } pack)
        {
            return 1;
        }

        SimSummary summary = Simulation.Run(options);
        if (line.Has("--out"))
        {
            File.WriteAllText(line.Text("--out", ""), JsonSerializer.Serialize(summary, Json));
        }

        output.WriteLine($"Content {pack.Id} v{pack.Version}: {summary.Legacies.Count} legacies x {options.Cycles} cycles, {summary.Legacies.Sum(l => l.Events)} events. OK.");
        return 0;
    }

    private static int BenchCommand(string[] args, TextWriter output, TextWriter error)
    {
        var line = new CommandLine(
            args,
            ["--objective", "--runs", "--seed", "--team", "--lead", "--autonomy", "--risk", "--priority", "--gear", "--policy", "--compare", "--replay", "--content", "--out"],
            ["--develop", "--fixed-draw"]);
        if (!line.Has("--objective"))
        {
            throw new UsageException("bench needs --objective.");
        }

        string team = line.Text("--team", "mixed");
        if (team is not ("rookie" or "mixed" or "veteran" or "sweep"))
        {
            throw new UsageException($"--team must be one of rookie, mixed, veteran, sweep, not '{team}'.");
        }

        var options = new BenchOptions(line.Text("--objective", ""))
        {
            Runs = line.Int("--runs", 10000, 1, 10_000_000),
            Seed = line.Seed("--seed", 1),
            Lead = line.Choice("--lead", LeadStyle.Average),
            Parameters = new Parameters(line.Int("--priority", 0, -1, 1), line.Int("--risk", 0, -1, 1), line.Int("--autonomy", 0, -1, 1), line.Has("--develop")),
            GearTags = line.Text("--gear", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Policy = line.Choice("--policy", CallPolicy.BackLead),
            FixedDraw = line.Has("--fixed-draw"),
        };
        if (team == "sweep" && (line.Has("--compare") || line.Has("--replay")))
        {
            throw new UsageException("--team sweep cannot be combined with --compare or --replay.");
        }

        if (LoadContent(line.Text("--content", "content"), error) is not { } pack)
        {
            return 1;
        }

        if (pack.Objectives.All(o => o.Id != options.ObjectiveId))
        {
            error.WriteLine($"Unknown objective '{options.ObjectiveId}'. The pack has: {string.Join(", ", pack.Objectives.Select(o => o.Id))}.");
            return 1;
        }

        MissionTuning? other = null;
        if (line.Has("--compare"))
        {
            other = LoadOtherTuning(line.Text("--compare", ""), error);
            if (other is null)
            {
                return 1;
            }
        }

        if (line.Has("--replay"))
        {
            options = options with { Strength = Strength(team) };
            output.Write(BenchReport.Replay(Bench.Replay(pack, other ?? pack.Tuning, options, line.Seed("--replay", 0))));
            return 0;
        }

        var columns = new List<(string Name, BenchFigures Figures)>();
        if (team == "sweep")
        {
            foreach (TeamStrength strength in Enum.GetValues<TeamStrength>())
            {
                columns.Add((strength.ToString().ToLowerInvariant(), Bench.Run(pack, pack.Tuning, options with { Strength = strength })));
            }
        }
        else
        {
            options = options with { Strength = Strength(team) };
            columns.Add((other is null ? team : "before", Bench.Run(pack, pack.Tuning, options)));
            if (other is not null)
            {
                columns.Add(("after", Bench.Run(pack, other, options)));
            }
        }

        output.WriteLine($"{options.ObjectiveId}: {options.Runs} missions from seed {options.Seed}; lead {options.Lead}, policy {options.Policy}.");
        output.WriteLine();
        output.Write(BenchReport.Summary(columns, showChange: other is not null));
        foreach ((string name, BenchFigures figures) in columns)
        {
            output.WriteLine();
            output.WriteLine($"Encounters ({name}); worst run: seed {figures.WorstSeed}");
            output.Write(BenchReport.Encounters(figures));
        }

        IEnumerable<string> uneven = pack.Encounters.Where(e => e.Fast.Difficulty != e.Careful.Difficulty + 1).Select(e => e.Id);
        if (uneven.Any())
        {
            output.WriteLine();
            output.WriteLine($"Fast is not careful + 1 in: {string.Join(", ", uneven)}.");
        }

        if (line.Has("--out"))
        {
            File.WriteAllText(line.Text("--out", ""), JsonSerializer.Serialize(columns.ToDictionary(c => c.Name, c => c.Figures), Json));
        }

        return 0;
    }

    private static TeamStrength Strength(string team)
    {
        return team switch
        {
            "rookie" => TeamStrength.Rookie,
            "veteran" => TeamStrength.Veteran,
            _ => TeamStrength.Mixed,
        };
    }

    private static ContentPack? LoadContent(string directory, TextWriter error)
    {
        ContentLoadResult content = ContentLoader.LoadDirectory(directory);
        foreach (ContentError e in content.Errors)
        {
            error.WriteLine($"{e.File}: {e.Message}");
        }

        return content.Pack;
    }

    private static MissionTuning? LoadOtherTuning(string file, TextWriter error)
    {
        if (!File.Exists(file))
        {
            error.WriteLine($"{file}: Tuning file not found.");
            return null;
        }

        (MissionTuning? tuning, IReadOnlyList<ContentError> errors) = ContentLoader.LoadTuningText(File.ReadAllText(file));
        foreach (ContentError e in errors)
        {
            error.WriteLine($"{file}: {e.Message}");
        }

        return tuning;
    }
}
```

Replace `src/SimRunner/Program.cs` whole:

```csharp
return SimRunner.Cli.Run(args, Console.Out, Console.Error);
```


- [ ] **Step 4: Add the bench to the gate**

`check.sh`:

```bash
#!/usr/bin/env bash
# The gate every change must pass: build, tests, a headless simulator run, and a mission bench.
set -euo pipefail
cd "$(dirname "$0")"
dotnet build --nologo -v quiet
dotnet test --nologo --no-build -v quiet
dotnet run --no-build --project src/SimRunner -- cycles --legacies 20 --cycles 50 --seed 1 --content content
dotnet run --no-build --project src/SimRunner -- bench --objective OBJECTIVE_TEMPLATE_1 --runs 2000 --team sweep --content content > /dev/null
echo "Mission bench: 3 x 2000 missions. OK."
```


- [ ] **Step 5: Run the gate**

Run: `./check.sh`
Expected: exit 0; 108 tests pass (Sim.Tests 70, MissionBench.Tests 13, SimRunner.Tests 19, and Persistence.Tests 6); the last two lines are `Content CORE v1: 20 legacies x 50 cycles, 6000 events. OK.` and `Mission bench: 3 x 2000 missions. OK.`

- [ ] **Step 6: Try the tuning loop by hand, and paste the output into your report**

```bash
cp content/tuning/missions.yaml /tmp/tuning-try.yaml
sed -i 's/  gearBonus: 2/  gearBonus: 3/' /tmp/tuning-try.yaml
dotnet run --no-build --project src/SimRunner -- bench --objective OBJECTIVE_TEMPLATE_1 --runs 2000 --gear scanner,tools --compare /tmp/tuning-try.yaml
rm /tmp/tuning-try.yaml
```

Expected: a table headed `before`, `after`, and `change`, with full success higher under `after`.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Add the bench command with strict flags, and bench the gate"
```

---

### Task 6: Record what was built

**Files:**
- Modify: `GLOSSARY.md`, `docs/gdd.md` (section 4.7), `docs/plans/phase-1-outline.md`, `wayfinder/map.md`

**Interfaces:**
- Consumes: the finished slices.
- Produces: documents that match the code. No code changes.

- [ ] **Step 1: Add the new terms to the glossary**

In `GLOSSARY.md`, under `### Missions`, add after the **Check** entry:

```markdown
**Approach**:
One of the two ways an away team can attempt a check: careful or fast. The team lead chooses.

**Tier**:
The graded result of a check: critical, success, partial, failure, or disaster.
_Avoid_: Grade, degree of success
```

And at the end of `### Missions`, add:

```markdown
**Tuning file**:
The one file in the content pack that holds every number the mission resolver uses. A mission keeps the tuning it started with.
_Avoid_: Config, settings, balance file

**Mission bench**:
The developer tool that runs one mission many times and reports how it tends to go. It never touches a legacy.
_Avoid_: Simulator (that plays whole cycles), test harness
```

- [ ] **Step 2: Note the bench in the GDD**

In `docs/gdd.md` section 4.7, add as the last bullet of the first list:

```markdown
- Built in Phase 1 (slices 1a and 1b): the tuning file is `content/tuning/missions.yaml`, and `SimRunner bench --objective ID --compare FILE` runs the same seeds under the pack's tuning and under `FILE`. On the benchmark crew (10,000 missions per briefing) the C# resolver gives calls per mission of 3.1, 2.5, and 1.3 for a cautious lead across the three autonomy settings, 2.5, 1.3, and 0.6 for an average one, and 0.7, 0.2, and 0.2 for a reckless one. "Develop juniors" gives 50% full success against 58% for best qualified. A reckless lead loses about 17% of missions at about 3.6 deaths per 100 crew-missions (the prototype gave 13% and 3.7: skips now belong to a stage, not an encounter, and the dice differ).
```

Also in `docs/gdd.md`: in section 4.2 step 1, add the sentence "In the resolver the lead goes careful when caution minus risk tolerance is zero or more."; in section 4.3, replace "Loss with a severe injury roll is where death comes from." with "Death comes only from a failed severe injury roll: on a disaster, on a bold option that risks one, or in a collapse."; and after the strain 4 bullet add: "- The collapse is checked after the encounter's decision, so a call at the strain cap can still pull the team out by withdrawing. This is the order the prototype played in. Checking first would cost weak teams under a lead who calls (an average lead with a rookie team: 21% lost becomes 28%); it is one condition in the resolver."

- [ ] **Step 3: Mark the slices built**

In `docs/plans/phase-1-outline.md`, add a line directly under the slice table:

```markdown
Slices 1a and 1b were built on 2026-11-14 from `2026-11-14-phase-1ab-plan.md`. Next is 1c.
```

In `wayfinder/map.md`, add to the end of "Decisions so far":

```markdown
- Phase 1, slices 1a and 1b: the mission resolver and command-line bench are built. Module state will be one JSON row per module per legacy (built in 1c). Design in `docs/plans/2026-11-14-phase-1ab-design.md`.
```

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Record the mission resolver and bench in the glossary, GDD, outline, and map"
```

---

## Done when

- `./check.sh` exits 0 on a clean clone, with 108 tests passing.
- `SimRunner bench --objective OBJECTIVE_TEMPLATE_1 --compare FILE` prints before, after, and change for the same seeds.
- `SimRunner bench --objective OBJECTIVE_TEMPLATE_1 --replay SEED` prints one mission beat by beat.
- A banned API under `src/Sim.*` fails the build.

## Deliberately not in these slices

Expressions (`leadPrefers`, `skipIf`), per-encounter outcome overrides, traits in the score, report text, assignments and sites, applying results to crew, module state rows, the SQLite schema version, bus events for beats, and bench alarms. See the design's section 2 for where each arrives.
