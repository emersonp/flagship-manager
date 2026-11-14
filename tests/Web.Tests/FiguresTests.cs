using System.Net;
using Web.Components;

namespace Web.Tests;

public class FiguresTests
{
    private static string Decoded(Microsoft.AspNetCore.Components.MarkupString markup)
    {
        return WebUtility.HtmlDecode(markup.Value);
    }

    // The design sets score plus roll, the target, and the signed margin each as one mono run.
    [Fact]
    public void A_check_line_sets_the_sum_the_target_and_the_margin_in_mono()
    {
        string html = Decoded(Figures.Check("CREW_01 checks SKILL_A at ENCOUNTER_03, helped by CREW_02: 9 + 8 against 13, success (+4)."));

        Assert.Equal("CREW_01 checks SKILL_A at ENCOUNTER_03, helped by CREW_02: <span class=\"m\">9 + 8</span> against <span class=\"m\">13</span>, <span class=\"res\">success</span> <span class=\"m\">(+4)</span>.", html);
    }

    [Fact]
    public void A_negative_margin_with_the_true_minus_is_one_mono_run_and_a_failure_is_harm()
    {
        string html = Decoded(Figures.Check("CREW_02 checks SKILL_B at ENCOUNTER_01: 6 + 3 against 15, failure (−6)."));

        Assert.Contains("<span class=\"res res--fail\">failure</span> <span class=\"m\">(−6)</span>.", html);
    }

    // CREW_01 and ENCOUNTER_03 keep their digits bare while the figures around them are wrapped.
    [Fact]
    public void Digits_inside_an_id_are_never_wrapped_but_the_figures_beside_them_are()
    {
        string html = Decoded(Figures.Injury("CREW_01 takes a light injury roll at ENCOUNTER_03: 5, light injury."));

        Assert.Contains("CREW_01 takes", html);
        Assert.Contains("ENCOUNTER_03: <span class=\"m\">5</span>, ", html);
        Assert.DoesNotContain("<span class=\"m\">01", html);
        Assert.DoesNotContain("<span class=\"m\">03", html);
        Assert.Contains("DECISION_05. Stakes <span class=\"m\">4</span>.", Decoded(Figures.Mono("CREW_01 calls: DECISION_05. Stakes 4.")));
    }

    [Fact]
    public void Markup_in_a_line_is_encoded_before_any_span_is_added()
    {
        string html = Figures.Mono("<b>7</b> & CREW_01").Value;

        Assert.DoesNotContain("<b>", html);
        Assert.Contains("&lt;b&gt;", html);
        Assert.Contains("<span class=\"m\">7</span>", html);
    }

    [Fact]
    public void Signed_effect_figures_are_each_a_mono_run()
    {
        string html = Decoded(Figures.Mono("Progress +1. Momentum +1. Strain -2."));

        Assert.Equal("Progress <span class=\"m\">+1</span>. Momentum <span class=\"m\">+1</span>. Strain <span class=\"m\">-2</span>.", html);
    }
}
