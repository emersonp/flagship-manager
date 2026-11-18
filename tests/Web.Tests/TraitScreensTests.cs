using System.Net;
using Sim.Core;
using Sim.Traits;

namespace Web.Tests;

// Traits on screen (1e part three, Task 5; design 4.7 and 4.8), through the real app on the fixture: trait_e is the stood_down shape
// (levels, a briefing line, launch rules), trait_g the avoids shape (a pair, a briefing line, a rule for both sent), trait_b a plain
// counter with a definition and no briefing line, and trait_f hidden.
public sealed class TraitScreensTests : IDisposable
{
    private readonly TestApp app = new();
    private readonly HttpClient client;

    public TraitScreensTests()
    {
        client = app.NewClient();
    }

    public void Dispose()
    {
        app.Dispose();
    }

    private static StoredTrait Held(string trait, string crewId, int value, string? targetId = null)
    {
        return new StoredTrait { Trait = trait, CrewId = crewId, TargetId = targetId, Value = value, ChangedAtWeek = 1, EncounterId = null };
    }

    // A legacy at its first briefing, holding these traits.
    private async Task<string> Briefing(params StoredTrait[] traits)
    {
        Guid id = await app.CreateAtOrders("LEGACY", 1);
        await app.Sessions.Run(app.Live, id, legacy =>
        {
            while (legacy.State.Phase != CyclePhase.Briefing)
            {
                legacy.Send(new AdvancePhase());
            }

            legacy.State.Module<TraitsState>(TraitsModule.Name).Traits.AddRange(traits);
            return legacy;
        });
        return WebUtility.HtmlDecode(await client.GetStringAsync($"/legacy/{id}"));
    }

    [Fact]
    public async Task Nothing_is_shown_without_traits()
    {
        string page = await Briefing();

        Assert.DoesNotContain("What launching will do", page);
        Assert.DoesNotContain("trait-chips", page);
    }

    [Fact]
    public async Task Chips_show_for_traits_with_a_briefing_line()
    {
        string page = await Briefing(Held("trait_e", "CREW_01", 1), Held("trait_b", "CREW_02", 2), Held("trait_f", "CREW_02", 1));

        Assert.Contains("<span class=\"tag\" style=\"display: none\" data-show=\"$_team.includes(' CREW_01 ')\">TRAIT_E_BRIEFING</span>", page);
        // CREW_02's traits have no briefing line, or are hidden, so nothing waits on picking them.
        Assert.DoesNotContain("includes(' CREW_02 ')", page);
    }

    [Fact]
    public async Task A_pair_chip_needs_both_picked()
    {
        string page = await Briefing(Held("trait_g", "CREW_02", 2, "CREW_03"));

        Assert.Contains("data-show=\"$_team.includes(' CREW_02 ') && $_team.includes(' CREW_03 ')\">TRAIT_G_BRIEFING CREW_03</span>", page);
    }

    // trait_e's notSent only clears it, so keeping the promise costs nothing and is not listed.
    [Fact]
    public async Task The_preview_lists_what_launching_would_do_for_each_condition()
    {
        string page = await Briefing(Held("trait_e", "CREW_01", 1), Held("trait_g", "CREW_02", 2, "CREW_03"));

        Assert.Contains("<li style=\"display: none\" data-show=\"$_team.includes(' CREW_01 ')\">CREW_01 sent, trait_e: trait_e to level_b, Loyalty −2, Morale −4</li>", page);
        Assert.Contains("<li style=\"display: none\" data-show=\"$_team.includes(' CREW_02 ') && $_team.includes(' CREW_03 ')\">CREW_02 and CREW_03 together, trait_g: Morale −3 CREW_02</li>", page);
        Assert.DoesNotContain("left behind", page);
        Assert.Contains("data-show=\"($_team.includes(' CREW_01 ')) || ($_team.includes(' CREW_02 ') && $_team.includes(' CREW_03 '))\"", page);
        // The count keeps the picked ids for these conditions; nothing blocks the launch.
        Assert.Contains("$_team = ' ' + [...el.form.querySelectorAll('input[name=crew]:checked')].map(i => i.value).join(' ') + ' '", page);
    }

    // The roster's interim cell (until the crew file): a level's name, a value, a pair's target; hidden traits never.
    [Fact]
    public async Task Shown_traits_appear_and_hidden_do_not()
    {
        string page = await Briefing(Held("trait_e", "CREW_01", 1), Held("trait_b", "CREW_02", 2), Held("trait_f", "CREW_02", 1), Held("trait_g", "CREW_02", 2, "CREW_03"));
        string roster = page[page.IndexOf("<aside id=\"roster\">", StringComparison.Ordinal)..];

        Assert.Contains("<th scope=\"col\">Traits</th>", roster);
        Assert.Contains("<span class=\"tag\">trait_e: level_a</span>", roster);
        Assert.Contains("<span class=\"tag\">trait_b 2</span><span class=\"tag\">trait_g CREW_03</span>", roster);
        Assert.DoesNotContain("trait_f", roster);
    }
}
