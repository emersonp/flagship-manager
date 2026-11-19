using System.Globalization;
using Sim.Core.Content;
using YamlDotNet.Serialization;

namespace Sim.Content;

public static partial class ContentLoader
{
    // The tuning file's numbers for the itinerary (1e parts one and two).
    private sealed record CycleNumbers(int StoryletsPerCycle, int VignettesPerCycle, int VignetteCooldown, int CaptainsTime, int HistoryCycles, int CastTries, SalienceTuning Salience)
    {
        public static readonly CycleNumbers None = new(0, 0, 0, 0, 0, 0, SalienceTuning.Default);
    }

    // One tuning file on its own, for comparing against the pack's.
    public static (MissionTuning? Tuning, BenchTuning? Bench, IReadOnlyList<ContentError> Errors) LoadTuningText(string text)
    {
        var errors = new List<ContentError>();
        IDeserializer yaml = new DeserializerBuilder().WithDuplicateKeyChecking().Build();
        (MissionTuning? tuning, BenchTuning? bench, _) = LoadTuning(yaml, new Dictionary<string, string> { [TuningFile] = text }, errors);
        return errors.Count == 0 ? (tuning, bench, errors) : (null, null, errors);
    }

    // The storylets module's numbers and the captain's time live here, so the pack keeps one tuning file for the cycle's rules.
    private static (MissionTuning? Tuning, BenchTuning? Bench, CycleNumbers Cycle) LoadTuning(IDeserializer yaml, IReadOnlyDictionary<string, string> files, List<ContentError> errors)
    {
        if (!files.TryGetValue(TuningFile, out string? text))
        {
            errors.Add(new ContentError(TuningFile, $"The pack has no {TuningFile}."));
            return (null, null, CycleNumbers.None);
        }

        Dictionary<object, object>? root = Parse<Dictionary<object, object>>(yaml, TuningFile, text, errors);
        if (root is null)
        {
            return (null, null, CycleNumbers.None);
        }

        var values = new Dictionary<string, string>();
        Flatten("", root, values);
        var read = new TuningReader(TuningFile, values, errors);
        int errorsBefore = errors.Count;

        var tuning = new MissionTuning
        {
            CheckTarget = read.Int("check.target"),
            CheckDice = Dice(read, "check.dice"),
            TierCritical = read.Int("check.tiers.critical"),
            TierSuccess = read.Int("check.tiers.success"),
            TierPartial = read.Int("check.tiers.partial"),
            TierFailure = read.Int("check.tiers.failure"),
            GearBonus = read.Int("check.gearBonus"),
            AssistDivisor = read.Int("check.assistDivisor", min: 1),
            Cap = read.Int("compounding.cap", min: 1),
            MomentumCritical = read.Int("compounding.momentum.critical"),
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
            SupervisorGapDivisor = read.Int("develop.supervisorGapDivisor", min: 1),
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
            InjuryDice = Dice(read, "injury.dice"),
            LightBelow = read.Int("injury.lightBelow"),
            DeadBelow = read.Int("injury.deadBelow"),
            IncapacitatedBelow = read.Int("injury.incapacitatedBelow"),
            MedicalBonus = read.Int("injury.medicalBonus"),
            MaxLight = read.Int("injury.maxLight"),
            PartialAtProgress = read.Int("result.partialAtProgress"),
        };
        var cycle = new CycleNumbers(
            read.Int("storylets.perCycle", min: 0),
            read.Int("vignettes.perCycle", min: 0),
            read.Int("vignettes.cooldown", min: 0),
            read.Int("captainsTime.perCycle", min: 0),
            read.Int("history.cycles", min: 1),
            read.Int("storylets.castTries", min: 1),
            new SalienceTuning(read.Int("salience.stakes", min: 0), read.Int("salience.specificity", min: 0), read.Int("salience.repeat", min: 0), read.Int("salience.window", min: 0)));
        var bench = new BenchTuning(BenchTeam(read, "rookie"), BenchTeam(read, "mixed"), BenchTeam(read, "veteran"));

        bool ordered = tuning.TierCritical > tuning.TierSuccess && tuning.TierSuccess > tuning.TierPartial && tuning.TierPartial > tuning.TierFailure;
        if (read.Complete && !ordered)
        {
            errors.Add(new ContentError(TuningFile, "check.tiers must fall from critical to failure."));
        }

        if (read.Complete)
        {
            CheckRanges(tuning, bench, errors);
        }

        return read.ReportUnused() && errors.Count == errorsBefore ? (tuning, bench, cycle) : (null, null, cycle);
    }

    // A threshold or range the wrong way round switches a rule off or crashes a draw, so it is a load error.
    private static void CheckRanges(MissionTuning t, BenchTuning bench, List<ContentError> errors)
    {
        void Above(string name, int value, string limitName, int limit)
        {
            if (value > limit)
            {
                errors.Add(new ContentError(TuningFile, $"{name} must not be above {limitName}."));
            }
        }

        Above("compounding.collapseAt", t.CollapseAt, "compounding.cap", t.Cap);
        Above("compounding.abortCallAt", t.AbortCallAt, "compounding.cap", t.Cap);
        Above("compounding.skipAtMomentum", t.SkipAtMomentum, "compounding.cap", t.Cap);
        Above("compounding.abortCallAt", t.AbortCallAt, "compounding.collapseAt", t.CollapseAt);
        Above("injury.deadBelow", t.DeadBelow, "injury.incapacitatedBelow", t.IncapacitatedBelow);
        Above("experience.closeMargin", t.CloseMargin, "experience.nearMargin", t.NearMargin);
        foreach ((string name, BenchTeamDef team) in new[] { ("rookie", bench.Rookie), ("mixed", bench.Mixed), ("veteran", bench.Veteran) })
        {
            string at = $"bench.teams.{name}";
            Above($"{at}.skill.0", team.SkillMin, $"{at}.skill.1", team.SkillMax);
            Above($"{at}.seniorSkill.0", team.SeniorSkillMin, $"{at}.seniorSkill.1", team.SeniorSkillMax);
            Above($"{at}.seniors", team.Seniors, $"{at}.size", team.Size);
        }
    }

    private static DiceDef Dice(TuningReader read, string at)
    {
        return new DiceDef(read.Int($"{at}.count", min: 1), read.Int($"{at}.sides", min: 2));
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
    private sealed class TuningReader(string file, Dictionary<string, string> values, List<ContentError> errors)
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

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value))
            {
                return value;
            }

            Fail(path, $"must be a finite number, not '{text}'.");
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
            errors.Add(new ContentError(file, $"{path} {message}"));
        }
    }
}
