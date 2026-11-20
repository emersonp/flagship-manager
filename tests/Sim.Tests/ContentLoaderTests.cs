using Sim.Content;
using Sim.Core.Content;

namespace Sim.Tests;

public class ContentLoaderTests
{
    internal const string Manifest = "id: CORE\nversion: 1\ncalendar: calendar_a\n";

    [Fact]
    public void A_valid_pack_loads()
    {
        ContentLoadResult result = ContentLoader.Load(TestContent.WithCrewFiles(new Dictionary<string, string>
        {
            ["pack.yaml"] = Manifest,
            ["tuning/missions.yaml"] = TestContent.TuningText,
            ["skills/base.yaml"] = "- id: SKILL_A\n  tags: [technical]\n- id: SKILL_B\n",
        }));

        Assert.Empty(result.Errors);
        Assert.Equal("CORE", result.Pack!.Id);
        Assert.Equal(1, result.Pack.Version);
        Assert.Equal(new[] { "SKILL_A", "SKILL_B", "FIXTURE_SKILL" }, result.Pack.Skills.Select(s => s.Id));
        Assert.Equal(new[] { "technical" }, result.Pack.Skills[0].Tags);
    }

    [Fact]
    public void A_skill_tag_outside_the_inner_rule_is_refused()
    {
        ContentLoadResult result = ContentLoader.Load(TestContent.WithCrewFiles(new Dictionary<string, string>
        {
            ["pack.yaml"] = Manifest,
            ["tuning/missions.yaml"] = TestContent.TuningText,
            ["skills/base.yaml"] = "- id: SKILL_A\n  tags: [Tech-Work]\n",
        }));

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.File == "skills/base.yaml" && e.Message == "SKILL_A: Tag 'Tech-Work' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all.");
    }

    // The author fixes content in batches, so every problem is reported at once and no pack is returned.
    [Fact]
    public void All_errors_are_reported_together()
    {
        ContentLoadResult result = ContentLoader.Load(TestContent.WithCrewFiles(new Dictionary<string, string>
        {
            ["pack.yaml"] = "id: core\nversion: 0\ncalendar: calendar_a\n",
            ["tuning/missions.yaml"] = TestContent.TuningText,
            ["skills/a.yaml"] = "- id: SKILL_A\n- id: skill b\n",
            ["skills/b.yaml"] = "- id: SKILL_A\n",
            ["skils/c.yaml"] = "- id: SKILL_C\n",
            ["skills/d.yaml"] = "- id: SKILL_D\n  tag: [typo]\n",
        }));

        Assert.Null(result.Pack);
        Assert.Equal(
            new[]
            {
                "pack.yaml|Id 'core' must be upper case: a letter, then letters, digits, and underscores, at most 40 in all.",
                "pack.yaml|version must be 1 or more.",
                "skills/a.yaml|Id 'skill b' must be upper case: a letter, then letters, digits, and underscores, at most 40 in all.",
                "skills/b.yaml|Duplicate id 'SKILL_A', first defined in skills/a.yaml.",
                "skills/d.yaml|Line 2: Property 'tag' not found on type 'Sim.Content.ContentLoader+SkillDto'.",
                "skils/c.yaml|Unknown content folder 'skils'.",
            },
            result.Errors.Select(e => $"{e.File}|{e.Message}"));
    }

    [Fact]
    public void A_missing_manifest_is_an_error()
    {
        ContentLoadResult result = ContentLoader.Load(TestContent.WithCrewFiles(new Dictionary<string, string> { ["tuning/missions.yaml"] = TestContent.TuningText }));

        Assert.Equal("The pack has no pack.yaml.", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void A_blank_list_item_is_reported_for_its_file()
    {
        ContentLoadResult result = ContentLoader.Load(TestContent.WithCrewFiles(new Dictionary<string, string>
        {
            ["pack.yaml"] = Manifest,
            ["tuning/missions.yaml"] = TestContent.TuningText,
            ["skills/a.yaml"] = "- id: SKILL_A\n-\n",
        }));

        Assert.Null(result.Pack);
        Assert.Equal("skills/a.yaml", Assert.Single(result.Errors).File);
    }

    [Fact]
    public void Tags_with_no_value_load_as_an_empty_list()
    {
        ContentLoadResult result = ContentLoader.Load(TestContent.WithCrewFiles(new Dictionary<string, string>
        {
            ["pack.yaml"] = Manifest,
            ["tuning/missions.yaml"] = TestContent.TuningText,
            ["skills/a.yaml"] = "- id: SKILL_A\n  tags:\n",
        }));

        Assert.Empty(result.Errors);
        Assert.Empty(result.Pack!.Skills[0].Tags);
    }

    [Fact]
    public void A_duplicated_key_is_reported_for_its_file()
    {
        ContentLoadResult result = ContentLoader.Load(TestContent.WithCrewFiles(new Dictionary<string, string>
        {
            ["pack.yaml"] = Manifest,
            ["tuning/missions.yaml"] = TestContent.TuningText,
            ["skills/a.yaml"] = "- id: SKILL_A\n  id: SKILL_B\n",
        }));

        Assert.Null(result.Pack);
        Assert.Equal("skills/a.yaml", Assert.Single(result.Errors).File);
    }

    // Slice 1e-calendar, Task 1: the calendar pack.yaml names, and its tuning.
    [Fact]
    public void The_pack_carries_the_calendar_it_names_and_its_tuning()
    {
        ContentPack pack = TestContent.Pack;

        Assert.Equal("calendar_a", pack.Calendar.Id);
        Assert.Equal(6, pack.Calendar.DaysPerWeek);
        Assert.Equal(360, pack.Calendar.DaysPerYear);
        Assert.Equal(new CalendarDate(105, 1, 13), pack.Calendar.Start);
        Assert.Equal("WD_A", pack.Calendar.Weekdays[0].Short);
        Assert.Equal(new CalendarTuning(5, 7), pack.CalendarTuning);
    }

    // The game's calendar is the lore's: a legacy starts on 13 Vortun 105, day 37482, a Stoneday; Pelpheär keeps its diaeresis.
    [Fact]
    public void The_games_calendar_is_the_Bastion_Calendar()
    {
        CalendarDef calendar = ContentLoader.LoadDirectory(Path.Combine(TestContent.Root, "content")).Pack!.Calendar;

        Assert.Equal("bastion", calendar.Id);
        Assert.Equal(37482, Sim.Core.Calendar.DayNumber(calendar, calendar.Start));
        Assert.Equal("Stoneday", Sim.Core.Calendar.Weekday(calendar, 37482).Name);
        Assert.Equal("Vortun", calendar.Months[calendar.Start.Month].Name);
        Assert.Equal(("pelphear", "Pelpheär"), (calendar.Months[5].Id, calendar.Months[5].Name));
        Assert.All(calendar.Months, m => Assert.Equal(30, m.Days));
    }

    public static TheoryData<string, string, string, string> CalendarRefusals => new()
    {
        { "calendars/base.yaml", "    - { id: month_a, name: MONTH_A, days: 30 }\n", "    - { id: month_a, name: MONTH_A, days: 0 }\n", "calendar_a: month month_a: days must be 1 or more, not 0." },
        { "calendars/base.yaml", "    - { id: weekday_b,", "    - { id: weekday_a,", "calendar_a: weekdays: duplicate id 'weekday_a'." },
        { "calendars/base.yaml", "    - { id: month_l,", "    - { id: month_a,", "calendar_a: months: duplicate id 'month_a'." },
        { "calendars/base.yaml", "    - { id: weekday_a,", "    - { id: WEEKDAY_A,", "calendar_a: weekdays: Id 'WEEKDAY_A' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all." },
        { "calendars/base.yaml", "start: { day: 13,", "start: { day: 31,", "calendar_a: start.day must be from 1 to 30, the days in month_b, not 31." },
        { "calendars/base.yaml", "start: { day: 13,", "start: { day: 0,", "calendar_a: start.day must be from 1 to 30, the days in month_b, not 0." },
        { "calendars/base.yaml", "month: month_b,", "month: month_z,", "calendar_a: start.month 'month_z' is not one of the calendar's months." },
        { "calendars/base.yaml", "year: 105 }", "year: 0 }", "calendar_a: start.year must be 1 or more, not 0." },
        { "calendars/base.yaml", "  start: { day: 13, month: month_b, year: 105 }\n", "", "calendar_a: start is missing." },
        { "calendars/base.yaml", "  name: CALENDAR_A\n", "", "calendar_a: name is missing." },
        { "pack.yaml", "calendar: calendar_a\n", "", "calendar is missing. Name the calendar in use, one defined in calendars/." },
        { "pack.yaml", "calendar: calendar_a\n", "calendar: nosuch\n", "calendar 'nosuch' is not defined in calendars/." },
        { "tuning/calendar.yaml", "minDays: 5", "minDays: 0", "cycle.minDays must be a whole number of 1 or more, not '0'." },
        { "tuning/calendar.yaml", "minDays: 5", "minDays: 8", "cycle.minDays must not be above cycle.maxDays." },
        { "tuning/calendar.yaml", "  maxDays: 7", "  maxDay: 7", "cycle.maxDays is missing." },
    };

    [Theory]
    [MemberData(nameof(CalendarRefusals))]
    public void A_calendar_that_cannot_work_is_refused(string file, string from, string to, string message)
    {
        ContentLoadResult result = LoadFixtureWith(file, from, to);

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.File == file && e.Message == message);
    }

    // Design 5.4: a duration written in days is refused with the rule, in an item and in tuning alike. A stray key in
    // tuning/calendar.yaml, whose numbers are days, is only stray.
    public static TheoryData<string, string, string, string> DaysRefusals => new()
    {
        { "traits/base.yaml", "every: { weeks: 3, add: -1 }", "every: { days: 18, add: -1 }", "Line 10: 'days' is in days. " + ContentLoader.DurationRule },
        { "traits/base.yaml", "expires: { weeks: 6 }", "expires: { days: 36 }", "Line 18: 'days' is in days. " + ContentLoader.DurationRule },
        { "tuning/crew.yaml", "lightWeeks: 2", "lightDays: 12", "injury.lightDays is in days. " + ContentLoader.DurationRule },
        { "tuning/calendar.yaml", "  maxDays: 7", "  maxDays: 7\n  restDays: 2", "cycle.restDays is not a tuning value." },
    };

    [Theory]
    [MemberData(nameof(DaysRefusals))]
    public void A_duration_in_days_is_refused_with_the_rule(string file, string from, string to, string message)
    {
        ContentLoadResult result = LoadFixtureWith(file, from, to);

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.File == file && e.Message == message);
    }

    [Fact]
    public void A_calendar_with_no_weekday_or_month_is_refused()
    {
        ContentLoadResult result = ContentLoader.Load(new Dictionary<string, string>(TestContent.RepoFiles())
        {
            ["calendars/base.yaml"] = "- { id: calendar_a, name: CALENDAR_A, weekdays: [], months: [], start: { day: 1, month: month_a, year: 1 } }\n",
        });

        Assert.Null(result.Pack);
        Assert.Equal(
            new[]
            {
                "calendars/base.yaml|calendar_a: weekdays must list at least one weekday.",
                "calendars/base.yaml|calendar_a: months must list at least one month.",
                "calendars/base.yaml|calendar_a: start.month 'month_a' is not one of the calendar's months.",
            },
            result.Errors.Select(e => $"{e.File}|{e.Message}"));
    }

    [Fact]
    public void A_calendar_id_is_lower_case_and_unique_across_files()
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        files["calendars/other.yaml"] = files["calendars/base.yaml"];
        files["calendars/upper.yaml"] = files["calendars/base.yaml"].Replace("id: calendar_a", "id: CALENDAR_B", StringComparison.Ordinal);

        ContentLoadResult result = ContentLoader.Load(files);

        Assert.Contains(result.Errors, e => e.File == "calendars/other.yaml" && e.Message == "Duplicate id 'calendar_a', first defined in calendars/base.yaml.");
        Assert.Contains(result.Errors, e => e.File == "calendars/upper.yaml" && e.Message == "Id 'CALENDAR_B' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all.");
    }

    // A second calendar loads beside the first; the pack uses the one it names.
    [Fact]
    public void The_pack_uses_the_calendar_it_names()
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        files["calendars/other.yaml"] = "- id: calendar_b\n  name: CALENDAR_B\n  weekdays: [{ id: weekday_a, name: WEEKDAY_A }]\n  months: [{ id: month_a, name: MONTH_A, days: 10 }]\n  start: { day: 1, month: month_a, year: 1 }\n";
        files["pack.yaml"] = files["pack.yaml"].Replace("calendar: calendar_a", "calendar: calendar_b", StringComparison.Ordinal);

        ContentLoadResult result = ContentLoader.Load(files);

        Assert.Empty(result.Errors);
        Assert.Equal("calendar_b", result.Pack!.Calendar.Id);
        Assert.Equal("WEEKDAY_A", result.Pack.Calendar.Weekdays[0].Short);
    }

    [Fact]
    public void A_pack_without_a_calendar_tuning_file_is_refused()
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        files.Remove("tuning/calendar.yaml");

        ContentLoadResult result = ContentLoader.Load(files);

        Assert.Equal("tuning/calendar.yaml|The pack has no tuning/calendar.yaml.", Assert.Single(result.Errors.Select(e => $"{e.File}|{e.Message}")));
    }

    private static ContentLoadResult LoadFixtureWith(string file, string from, string to)
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        string text = files[file].Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(from, text);
        files[file] = text.Replace(from, to, StringComparison.Ordinal);
        return ContentLoader.Load(files);
    }
}
