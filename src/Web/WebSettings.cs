namespace Web;

public sealed class WebSettings
{
    public string ContentDirectory { get; set; } = "content";

    public string DatabasePath { get; set; } = "starship-sim.db";

    public int BeatDelayMs { get; set; } = 700;
}
