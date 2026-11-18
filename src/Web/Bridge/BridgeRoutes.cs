namespace Web.Bridge;

public static class BridgeRoutes
{
    // Where a bridge page posts an action. Dev Play's pages post under /dev, so the route itself has the answer drawn for Dev Play.
    public static string Action(TopBarView bar, Guid legacyId, string action)
    {
        return $"{(bar.Dev ? "/dev" : "")}/legacy/{legacyId}/{action}";
    }
}
