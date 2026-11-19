using Sim.Core;
using Sim.Core.Content;

namespace Sim.Storylets;

// The answers open on a waiting storylet, read as the guard reads them (1e part four, Task 6): its options whose requires holds,
// and for a check option, who may be sent and their chance.
internal sealed class StoryletView(ContentPack pack, Casting casting, StoryletsState state, IRosterView roster, ITraitView traits, int cycle) : IStoryletView
{
    public Contest? Opened => state.Opened;

    public IReadOnlyList<OpenAnswer> Answers(string offerId)
    {
        if (state.Waiting.FirstOrDefault(w => w.OfferId == offerId) is not { } waiting
            || pack.Storylets.FirstOrDefault(s => s.Id == waiting.StoryletId) is not { } storylet)
        {
            return [];
        }

        var source = new StateSource(roster, traits, state, cycle);
        return storylet.Options
            .Where(o => casting.Open(storylet.Id, o, source, waiting.Cast))
            .Select(o => new OpenAnswer(o.Id, o.Check is not { } check ? null
                : Check.Handlers(roster, waiting.Cast).Select(id => Check.Odds(check, id, waiting.Cast, roster, traits, pack.Tuning.CheckDice)).ToList()))
            .ToList();
    }
}
