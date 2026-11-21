using Sim.Core;
using Sim.Core.Content;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Sim.Content;

// Test ships (1e part six, design 4.6): content/testships/*.yaml, a list, checked against a pack that has already loaded. Load skips
// the folder, so a bad test ship never makes the game's pack invalid; only Web's developer pages and the tests call this.
public static partial class ContentLoader
{
    private const string TestShipsFolder = "testships";

    // Keys as Load's: paths relative to the pack root, with forward slashes. Files outside testships/ are ignored.
    public static TestShipsLoadResult LoadTestShips(IReadOnlyDictionary<string, string> files, ContentPack pack)
    {
        var errors = new List<ContentError>();
        var ships = new List<TestShipDef>();
        IDeserializer yaml = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).WithDuplicateKeyChecking().Build();
        var seenIn = new Dictionary<string, string>();
        foreach ((string path, string text) in files.Where(f => f.Key.StartsWith(TestShipsFolder + "/", StringComparison.Ordinal)).OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            foreach (TestShipDto dto in Items<TestShipDto>(yaml, path, text, d => d.Id, seenIn, errors, CheckLowerId))
            {
                int before = errors.Count;
                TestShipDef ship = BuildTestShip(path, dto, pack, errors);
                if (errors.Count == before)
                {
                    ships.Add(ship);
                }
            }
        }

        return new TestShipsLoadResult(ships, errors);
    }

    private static TestShipDef BuildTestShip(string file, TestShipDto dto, ContentPack pack, List<ContentError> errors)
    {
        void Error(string field, string message)
        {
            errors.Add(new ContentError(file, $"{dto.Id}: {field}: {message}"));
        }

        HashSet<string> crewIds = pack.Crew.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        bool IsCrew(string field, string? id)
        {
            if (id is not null && crewIds.Contains(id))
            {
                return true;
            }

            Error(field, id is null ? "a crew member's id is needed." : $"no crew member {id} in the starting crew.");
            return false;
        }

        string? name = dto.Name is null ? null : CheckName(file, dto.Id!, dto.Name, errors);
        if (dto.Name is null)
        {
            Error("name", "a test ship needs a name.");
        }

        int cycle = dto.Cycle ?? 0;
        if (dto.Date is not >= 0)
        {
            Error("date", "the ship's today, as a day number: a whole number of 0 or more.");
        }

        if (dto.Cycle is not >= 1)
        {
            Error("cycle", "the cycle now starting: a whole number of 1 or more.");
        }

        // An age: a whole number of days or cycles ago, from 0, and never before the first cycle.
        bool Ages(string field, int? daysAgo, int? cyclesAgo, bool cycles = true)
        {
            bool ok = true;
            if (daysAgo is not >= 0)
            {
                Error($"{field}.daysAgo", "a whole number of days ago, 0 or more.");
                ok = false;
            }

            if (cycles && (cyclesAgo is not >= 0 || cyclesAgo > cycle))
            {
                Error($"{field}.cyclesAgo", $"a whole number of cycles ago, 0 to the ship's cycle ({cycle}).");
                ok = false;
            }

            return ok;
        }

        var crew = new List<TestShipCrew>();
        var listed = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> skills = pack.Skills.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach ((CrewChangeDto c, int i) in (dto.Crew ?? []).Select((c, i) => (c, i)))
        {
            string field = $"crew[{i}]";
            if (!IsCrew($"{field}.id", c.Id))
            {
                continue;
            }

            if (!listed.Add(c.Id!))
            {
                Error($"{field}.id", $"{c.Id} is listed twice; give each crew member one entry.");
                continue;
            }

            if (c.Morale is { } morale && (morale < pack.CrewTuning.MoraleMin || morale > pack.CrewTuning.MoraleMax))
            {
                Error($"{field}.morale", $"morale is {pack.CrewTuning.MoraleMin} to {pack.CrewTuning.MoraleMax}.");
            }

            if (c.Loyalty is { } loyalty && (loyalty < pack.CrewTuning.LoyaltyMin || loyalty > pack.CrewTuning.LoyaltyMax))
            {
                Error($"{field}.loyalty", $"loyalty is {pack.CrewTuning.LoyaltyMin} to {pack.CrewTuning.LoyaltyMax}.");
            }

            foreach ((string skill, int level) in c.Skills ?? [])
            {
                if (!skills.Contains(skill))
                {
                    Error($"{field}.skills.{skill}", $"no skill named {skill}.");
                }
                else if (level < 0)
                {
                    Error($"{field}.skills.{skill}", "a skill's level is 0 or more.");
                }
            }

            crew.Add(new TestShipCrew(c.Id!, c.Morale, c.Loyalty, c.Skills, c.Fit, c.Aboard, c.Alive));
        }

        var book = new TraitBook(pack.Traits);
        var traits = new List<TestShipTrait>();
        var held = new HashSet<(string, string?, string?)>();
        foreach ((TestShipTraitDto t, int i) in (dto.Traits ?? []).Select((t, i) => (t, i)))
        {
            string field = $"traits[{i}]";
            if (t.Trait is null || book.Def(t.Trait) is not { } def)
            {
                Error($"{field}.trait", t.Trait is null ? "the trait held is needed." : $"no trait {t.Trait} in traits/.");
                continue;
            }

            // Where it is held, as written: toward makes it a pair's, on alone a crew member's, neither the ship's.
            string place = t.Toward is not null ? TraitPlaces.Pair : t.On is not null ? TraitPlaces.Crew : TraitPlaces.Ship;
            bool sound = book.CheckPlace(t.Trait, place, message => Error($"{field}.{(place == TraitPlaces.Pair ? "toward" : "on")}", message + (def.On is TraitPlaces.Crew or TraitPlaces.Pair && t.On is null ? " Say who holds it with on." : "")));
            if (sound && place == TraitPlaces.Pair && t.On is null)
            {
                Error($"{field}.on", "a pair's holder is named with on, and its target with toward.");
                sound = false;
            }

            sound &= t.On is null || IsCrew($"{field}.on", t.On);
            sound &= t.Toward is null || IsCrew($"{field}.toward", t.Toward);
            if (t.Toward is not null && t.Toward == t.On)
            {
                Error($"{field}.toward", "a pair's holder and target are two crew members.");
                sound = false;
            }

            int? value = t.Value is null ? null : book.SetValue(t.Trait, t.Value, message => Error($"{field}.value", message));
            if (t.Value is null || value == 0 || value > def.Cap)
            {
                Error($"{field}.value", t.Value is null || value == 0
                    ? "a held trait's value is 1 or more, or a level's name; a trait at 0 is not held, so leave it out."
                    : $"{t.Trait} is at most {def.Cap}.");
                value = null;
            }

            sound &= Ages(field, t.DaysAgo, t.CyclesAgo);
            if (sound && !held.Add((t.Trait, t.On, t.Toward)))
            {
                Error(field, $"{t.Trait} is listed twice on the same holder.");
                sound = false;
            }

            if (sound && value is { } v)
            {
                traits.Add(new TestShipTrait(t.Trait, t.On, t.Toward, v, t.DaysAgo!.Value, t.CyclesAgo!.Value));
            }
        }

        var history = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>>(StringComparer.Ordinal);
        int keep = Math.Min(cycle, pack.HistoryCycles);
        foreach ((string crewId, Dictionary<string, List<int>>? kinds) in dto.History ?? [])
        {
            if (!IsCrew($"history.{crewId}", crewId))
            {
                continue;
            }

            var byKind = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
            foreach ((string kind, List<int>? ages) in kinds ?? [])
            {
                string field = $"history.{crewId}.{kind}";
                if (!HistoryKinds.All.Contains(kind))
                {
                    Error(field, $"no history kind named {kind}; the kinds are {string.Join(", ", HistoryKinds.All)}.");
                    continue;
                }

                // The history keeps whole cycles before this one, and only the last history.cycles of them.
                if ((ages ?? []).Any(a => a < 1 || a > keep))
                {
                    Error(field, $"each is how many cycles ago it happened: 1 (last cycle) to {keep}, the most the history keeps here.");
                    continue;
                }

                byKind[kind] = (ages ?? []).Distinct().ToList();
            }

            history[crewId] = byKind;
        }

        var chronicle = new List<TestShipEntry>();
        foreach ((TestShipEntryDto e, int i) in (dto.Chronicle ?? []).Select((e, i) => (e, i)))
        {
            string field = $"chronicle[{i}]";
            bool sound = true;
            if (e.Kind is null || !ChronicleKinds.All.Contains(e.Kind))
            {
                Error($"{field}.kind", $"{(e.Kind is null ? "an entry's kind is needed" : $"no chronicle kind named {e.Kind}")}; the kinds are {string.Join(", ", ChronicleKinds.All)}.");
                sound = false;
            }

            sound &= Ages(field, e.DaysAgo, null, cycles: false);
            List<string> subjects = e.Subjects ?? [];
            for (int s = 0; s < subjects.Count; s++)
            {
                sound &= IsCrew($"{field}.subjects[{s}]", subjects[s]);
            }

            if (sound)
            {
                chronicle.Add(new TestShipEntry(e.Kind!, e.DaysAgo!.Value, subjects));
            }
        }

        var arrivals = new List<TestShipArrival>();
        HashSet<string> arrives = pack.Storylets.Select(s => s.Id).Concat(pack.Vignettes.Select(v => v.Id)).ToHashSet(StringComparer.Ordinal);
        foreach ((TestShipArrivalDto a, int i) in (dto.Storylets ?? []).Select((a, i) => (a, i)))
        {
            string field = $"storylets[{i}]";
            bool sound = true;
            if (a.Id is null || !arrives.Contains(a.Id))
            {
                Error($"{field}.id", a.Id is null ? "the storylet's id is needed." : $"no storylet or vignette {a.Id}.");
                sound = false;
            }
            else if (arrivals.Any(x => x.Storylet == a.Id))
            {
                Error($"{field}.id", $"{a.Id} is listed twice; give its last arrival only.");
                sound = false;
            }

            sound &= Ages(field, a.DaysAgo, a.CyclesAgo);
            if (sound)
            {
                arrivals.Add(new TestShipArrival(a.Id!, a.DaysAgo!.Value, a.CyclesAgo!.Value));
            }
        }

        return new TestShipDef(dto.Id!, name ?? "", dto.Date ?? 0, cycle, crew, traits, history, chronicle, arrivals);
    }

    private sealed class TestShipDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        // The author's notes: read, and dropped.
        public string? Notes { get; set; }

        public int? Date { get; set; }

        public int? Cycle { get; set; }

        public List<CrewChangeDto>? Crew { get; set; }

        public List<TestShipTraitDto>? Traits { get; set; }

        public Dictionary<string, Dictionary<string, List<int>>?>? History { get; set; }

        public List<TestShipEntryDto>? Chronicle { get; set; }

        public List<TestShipArrivalDto>? Storylets { get; set; }
    }

    private sealed class CrewChangeDto
    {
        public string? Id { get; set; }

        public int? Morale { get; set; }

        public int? Loyalty { get; set; }

        public Dictionary<string, int>? Skills { get; set; }

        public bool? Fit { get; set; }

        public bool? Aboard { get; set; }

        public bool? Alive { get; set; }
    }

    private sealed class TestShipTraitDto
    {
        public string? Trait { get; set; }

        public string? On { get; set; }

        public string? Toward { get; set; }

        // A whole number, or a level's name.
        public string? Value { get; set; }

        public int? DaysAgo { get; set; }

        public int? CyclesAgo { get; set; }
    }

    private sealed class TestShipEntryDto
    {
        public string? Kind { get; set; }

        public int? DaysAgo { get; set; }

        public List<string>? Subjects { get; set; }
    }

    private sealed class TestShipArrivalDto
    {
        public string? Id { get; set; }

        public int? DaysAgo { get; set; }

        public int? CyclesAgo { get; set; }
    }
}
