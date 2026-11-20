using System.Text.Json.Serialization;
using Sim.Core;
using Sim.Core.Content;

namespace Sim.Cycle;

public sealed class CycleState
{
    public bool Founded { get; set; }

    // While a turn walks the days: the last day it passes. Never saved: a turn ends inside the command that began it.
    [JsonIgnore]
    public int? ArrivesAtDay { get; set; }
}

// Owns the cycle and the date (slice 1e-calendar, design 4.4). A turn rolls the cycle's length on the calendar stream, turns
// the cycle, then publishes one DayReached per day passed. The first day is published only when everything that heard the cycle
// turn has run (rest), and each later day only when the one before it, and everything it set off, has run; so an effect due on a
// later day never happens before one due on an earlier day, and the log reads in that order.
public static class CycleModule
{
    public const string Name = "cycle";

    // The stream the cycle's length is rolled on, and nothing else.
    public const string CalendarStream = "calendar";

    // Before every module, so Day is the day reached when they run; and after every module, so the next day waits for them.
    private const int DaySetOrder = 0;

    private const int NextDayOrder = 1000;

    public static void Register(EventBus bus, ContentPack pack)
    {
        CalendarDef calendar = pack.Calendar;
        CalendarTuning tuning = pack.CalendarTuning;
        bus.Module<CycleState>(Name);
        bus.Guard<FoundLegacy>(0, "not-yet-founded", (_, context) => context.Module<CycleState>().Founded ? "The legacy is already founded." : null);
        bus.Guard<AdvancePhase>(0, "founded", (_, context) => context.Module<CycleState>().Founded ? null : "The legacy has not been founded.");
        bus.Guard<Travel>(0, "founded", (command, context) => CheckTravel(command, context));
        bus.Handle<FoundLegacy>((_, context) => OnFoundLegacy(calendar, context));
        bus.Handle<AdvancePhase>((_, context) => OnAdvancePhase(calendar, tuning, context));
        bus.Handle<Travel>((command, context) => Turn(calendar, tuning, command.Weeks, context));
        bus.Subscribe<DayReached>(DaySetOrder, Name, (e, context) => context.State.Day = e.Day);
        bus.Subscribe<CycleAdvanced>(NextDayOrder, Name, (_, context) => NextDay(context));
        bus.Subscribe<DayReached>(NextDayOrder, Name, (_, context) => NextDay(context));
    }

    private static string? CheckTravel(Travel command, IBusContext context)
    {
        if (!context.Module<CycleState>().Founded)
        {
            return "The legacy has not been founded.";
        }

        if (context.State.Phase != CyclePhase.Advance)
        {
            return "The ship travels only as the cycle turns.";
        }

        return command.Weeks is < 1 or > Travel.Most ? $"A journey is 1 to {Travel.Most} weeks." : null;
    }

    private static void OnFoundLegacy(CalendarDef calendar, IBusContext context)
    {
        context.Module<CycleState>().Founded = true;
        context.State.StartDay = Calendar.DayNumber(calendar, calendar.Start);
        context.State.Day = context.State.StartDay;
        context.Publish(new LegacyFounded());
        context.Publish(new PhaseEntered(context.State.Cycle, context.State.Phase));
    }

    private static void OnAdvancePhase(CalendarDef calendar, CalendarTuning tuning, IBusContext context)
    {
        if (context.State.Phase == CyclePhase.Advance)
        {
            Turn(calendar, tuning, 1, context);
            return;
        }

        context.State.Phase++;
        context.Publish(new PhaseEntered(context.State.Cycle, context.State.Phase));
    }

    // Rolled before the cycle turns, so the stream is the ending cycle's, as every stream is. Travel of N weeks is one roll times
    // N, never N rolls: one roll per week would flatten every trip to its average.
    private static void Turn(CalendarDef calendar, CalendarTuning tuning, int weeks, IBusContext context)
    {
        LegacyState state = context.State;
        int roll = tuning.CycleMinDays + context.Rng(CalendarStream).Next(tuning.CycleMaxDays - tuning.CycleMinDays + 1);
        int days = roll * weeks;
        context.Publish(new CycleLengthRolled(days, Calendar.Days(calendar, weeks)));
        state.Cycle++;
        state.Phase = CyclePhase.Itinerary;
        context.Publish(new CycleAdvanced(state.Cycle));
        context.Module<CycleState>().ArrivesAtDay = state.Day + days;
    }

    // After the turn, and after each day's effects: the next day, or, past the last, the itinerary. Outside a turn (a test's own
    // DayReached) it does nothing.
    private static void NextDay(IBusContext context)
    {
        CycleState cycle = context.Module<CycleState>();
        if (cycle.ArrivesAtDay is not { } arrives)
        {
            return;
        }

        if (context.State.Day < arrives)
        {
            context.Publish(new DayReached(context.State.Day + 1));
            return;
        }

        cycle.ArrivesAtDay = null;
        context.Publish(new PhaseEntered(context.State.Cycle, context.State.Phase));
    }
}
