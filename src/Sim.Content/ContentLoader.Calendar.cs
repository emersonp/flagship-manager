using Sim.Core.Content;
using YamlDotNet.Serialization;

namespace Sim.Content;

// The calendar (slice 1e-calendar, design 4.2): content/calendars/, the one pack.yaml names, and tuning/calendar.yaml.
public static partial class ContentLoader
{
    private const string CalendarTuningFile = "tuning/calendar.yaml";

    // Design 5.4: content and tuning write durations only in weeks. Quoted from docs/conventions.md, "Calendars and durations".
    public const string DurationRule = "Durations are written in weeks, never in days (docs/conventions.md, \"Calendars and durations\"): weeks:, lightWeeks:.";

    // A key that writes a duration in days: days, or one ending in Days (lightDays). Only the calendar itself (a month's days, a
    // start date's day) and tuning/calendar.yaml (a cycle's minDays and maxDays) count in days, and both read those keys by name,
    // so a key like this is refused only where nothing reads it.
    private static bool InDays(string key)
    {
        return key == "days" || key.EndsWith("Days", StringComparison.Ordinal);
    }

    // Every calendar is checked; the one the pack names is returned. Null when it is missing or any calendar is refused.
    private static CalendarDef? BuildCalendar(List<(string File, CalendarDto Dto)> calendars, PackDto? manifest, List<ContentError> errors)
    {
        int errorsBefore = errors.Count;
        var defs = new Dictionary<string, CalendarDef>(StringComparer.Ordinal);
        foreach ((string file, CalendarDto dto) in calendars)
        {
            if (OneCalendar(file, dto, errors) is { } def)
            {
                defs[def.Id] = def;
            }
        }

        if (manifest is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(manifest.Calendar))
        {
            errors.Add(new ContentError(ManifestFile, "calendar is missing. Name the calendar in use, one defined in calendars/."));
            return null;
        }

        if (!defs.TryGetValue(manifest.Calendar, out CalendarDef? chosen))
        {
            // A calendar that was refused is reported in its own file; only one that is nowhere is reported here.
            if (!calendars.Any(c => c.Dto.Id == manifest.Calendar))
            {
                errors.Add(new ContentError(ManifestFile, $"calendar '{manifest.Calendar}' is not defined in calendars/."));
            }

            return null;
        }

        return errors.Count == errorsBefore ? chosen : null;
    }

    private static CalendarDef? OneCalendar(string file, CalendarDto dto, List<ContentError> errors)
    {
        int errorsBefore = errors.Count;
        string id = dto.Id!;
        void Error(string message) => errors.Add(new ContentError(file, $"{id}: {message}"));

        string? name = CheckName(file, id, dto.Name, errors);
        if (dto.Name is null)
        {
            Error("name is missing.");
        }

        var weekdays = new List<WeekdayDef>();
        var seenWeekdays = new HashSet<string>(StringComparer.Ordinal);
        foreach (WeekdayDto? w in dto.Weekdays ?? [])
        {
            if (Part("weekdays", w?.Id, seenWeekdays, Error) is not { } wid)
            {
                continue;
            }

            string? wname = CheckName(file, $"{id}: weekday {wid}", w!.Name, errors);
            if (w.Name is null)
            {
                Error($"weekday {wid}: name is missing.");
            }

            string? shortName = w.Short is null ? wname : CheckName(file, $"{id}: weekday {wid}: short", w.Short, errors);
            if (wname is not null && shortName is not null)
            {
                weekdays.Add(new WeekdayDef(wid, wname, shortName));
            }
        }

        if (dto.Weekdays is not { Count: > 0 })
        {
            Error("weekdays must list at least one weekday.");
        }

        var months = new List<MonthDef>();
        var seenMonths = new HashSet<string>(StringComparer.Ordinal);
        foreach (MonthDto? m in dto.Months ?? [])
        {
            if (Part("months", m?.Id, seenMonths, Error) is not { } mid)
            {
                continue;
            }

            string? mname = CheckName(file, $"{id}: month {mid}", m!.Name, errors);
            if (m.Name is null)
            {
                Error($"month {mid}: name is missing.");
            }

            if (m.Days is not { } days || days < 1)
            {
                Error(m.Days is null ? $"month {mid}: days is missing." : $"month {mid}: days must be 1 or more, not {m.Days}.");
                continue;
            }

            if (mname is not null)
            {
                months.Add(new MonthDef(mid, mname, days));
            }
        }

        if (dto.Months is not { Count: > 0 })
        {
            Error("months must list at least one month.");
        }

        CalendarDate? start = Start(dto.Start, months, Error);
        return errors.Count == errorsBefore && name is not null && start is { } date ? new CalendarDef(id, name, weekdays, months, date) : null;
    }

    // A weekday's or a month's id: lower case, as everything inside an item is (docs/conventions.md, "Ids"), and unique in its list.
    private static string? Part(string list, string? id, HashSet<string> seen, Action<string> error)
    {
        if (id is null || !Ids.IsInnerId(id))
        {
            error($"{list}: Id '{id}' must be {Said(Ids.InnerRule)}");
            return null;
        }

        if (!seen.Add(id))
        {
            error($"{list}: duplicate id '{id}'.");
            return null;
        }

        return id;
    }

    // The start date: a month of the calendar, a day inside it, and a year of the era (1 or more).
    private static CalendarDate? Start(StartDto? start, List<MonthDef> months, Action<string> error)
    {
        if (start is null)
        {
            error("start is missing.");
            return null;
        }

        bool ok = true;
        if (start.Year is not { } year || year < 1)
        {
            error(start.Year is null ? "start.year is missing." : $"start.year must be 1 or more, not {start.Year}.");
            ok = false;
        }

        int month = months.FindIndex(m => m.Id == start.Month);
        if (month < 0)
        {
            error(start.Month is null ? "start.month is missing." : $"start.month '{start.Month}' is not one of the calendar's months.");
            ok = false;
        }

        if (start.Day is not { } day)
        {
            error("start.day is missing.");
            ok = false;
        }
        else if (month >= 0 && (day < 1 || day > months[month].Days))
        {
            error($"start.day must be from 1 to {months[month].Days}, the days in {months[month].Id}, not {day}.");
            ok = false;
        }

        return ok ? new CalendarDate(start.Year!.Value, month, start.Day!.Value) : null;
    }

    private static CalendarTuning? LoadCalendarTuning(IDeserializer yaml, IReadOnlyDictionary<string, string> files, List<ContentError> errors)
    {
        if (!files.TryGetValue(CalendarTuningFile, out string? text))
        {
            errors.Add(new ContentError(CalendarTuningFile, $"The pack has no {CalendarTuningFile}."));
            return null;
        }

        Dictionary<object, object>? root = Parse<Dictionary<object, object>>(yaml, CalendarTuningFile, text, errors);
        if (root is null)
        {
            return null;
        }

        var values = new Dictionary<string, string>();
        Flatten("", root, values);
        var read = new TuningReader(CalendarTuningFile, values, errors);
        int errorsBefore = errors.Count;
        var tuning = new CalendarTuning(read.Int("cycle.minDays", min: 1), read.Int("cycle.maxDays", min: 1));
        if (read.Complete && tuning.CycleMinDays > tuning.CycleMaxDays)
        {
            errors.Add(new ContentError(CalendarTuningFile, "cycle.minDays must not be above cycle.maxDays."));
        }

        return read.ReportUnused() && errors.Count == errorsBefore ? tuning : null;
    }

    internal sealed class CalendarDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public List<WeekdayDto?>? Weekdays { get; set; }

        public List<MonthDto?>? Months { get; set; }

        public StartDto? Start { get; set; }
    }

    internal sealed class WeekdayDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Short { get; set; }
    }

    internal sealed class MonthDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public int? Days { get; set; }
    }

    internal sealed class StartDto
    {
        public int? Day { get; set; }

        public string? Month { get; set; }

        public int? Year { get; set; }
    }
}
