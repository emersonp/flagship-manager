namespace Web.Bridge;

public static class BridgeRoutes
{
    // Where a bridge page posts an action. Dev Play's pages post under /dev, so the route itself has the answer drawn for Dev Play.
    public static string Action(TopBarView bar, Guid legacyId, string action)
    {
        return $"{(bar.Dev ? "/dev" : "")}/legacy/{legacyId}/{action}";
    }

    // The bridge, which draws the phase as it stands; Dev Play's stays in Dev Play.
    public static string Bridge(TopBarView bar)
    {
        return $"{(bar.Dev ? "/dev" : "")}/legacy/{bar.LegacyId}";
    }

    // The Chronicle page (1e part five, design 4.7): All, or Notable alone.
    public static string Chronicle(TopBarView bar, bool notable = false)
    {
        return $"{Bridge(bar)}/chronicle{(notable ? "?show=notable" : "")}";
    }
}
