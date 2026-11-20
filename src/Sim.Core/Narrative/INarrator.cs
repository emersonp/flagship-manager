namespace Sim.Core.Narrative;

// Words for a module that may not reference Sim.Narrative (slice 1e part five): the chronicle asks for its text through this, and
// GameHost passes in Sim.Narrative's implementation when it registers the module.
public interface INarrator
{
    // The best line for the situation, expanded to plain text; null when no line holds or the one that does cannot be expanded.
    // Key breaks ties between equally specific lines and picks symbol options, so the same key always gives the same words.
    string? Line(Situation situation, string key);
}
