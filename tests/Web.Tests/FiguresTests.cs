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
        string html = Decoded(Figures.Mono("Progress +1. Momentum +1. Strain \u22122."));

        Assert.Equal("Progress <span class=\"m\">+1</span>. Momentum <span class=\"m\">+1</span>. Strain <span class=\"m\">\u22122</span>.", html);
    }

    [Fact]
    public void A_no_break_list_keeps_each_item_whole_with_the_oxford_comma()
    {
        Assert.Equal("<span class=\"nw\">SKILL_A</span>, <span class=\"nw\">SKILL_B</span>, and <span class=\"nw\">SKILL_C</span>", Figures.Items(["SKILL_A", "SKILL_B", "SKILL_C"]).Value);
        Assert.Equal("<span class=\"nw\">SKILL_A</span> and <span class=\"nw\">SKILL_B</span>", Figures.Items(["SKILL_A", "SKILL_B"]).Value);
    }

    private static readonly Sim.Missions.CheckBeat Check = new("ENCOUNTER_03", "careful", "SKILL_A", "CREW_02", null, false, 3, 0, 0, 0, 0, 3, 6, 11, -2, Sim.Missions.Tier.Partial, 0, false, 0);

    private static Web.Bridge.Names NamedCrew02(string name)
    {
        Sim.Core.Content.ContentPack pack = BridgePack.Load();
        return new Web.Bridge.Names(pack with { Crew = [new Sim.Core.Content.CrewDef("CREW_02", 1, new Dictionary<string, int>(), 0, 0, name)] });
    }

    // A name is plain text exactly as typed, and the line's own numbers stay figures.
    [Fact]
    public void A_name_with_digits_signs_and_markup_is_plain_text_and_the_lines_own_figures_stay_figures()
    {
        Web.Bridge.LogLine line = Web.Bridge.BeatLines.Line(0, Check, 0, NamedCrew02("NAME 7 <b> & -3"));

        string raw = Figures.Check(line.Segments).Value;
        string html = WebUtility.HtmlDecode(raw);

        Assert.DoesNotContain("<b>", raw);
        Assert.Contains("NAME 7 &lt;b&gt; &amp; -3 checks", raw);
        Assert.DoesNotContain("<span class=\"m\">7", html);
        Assert.DoesNotContain("<span class=\"m\">-3", html);
        Assert.Contains("<span class=\"m\">3 + 6</span> against <span class=\"m\">11</span>, <span class=\"res\">partial</span> <span class=\"m\">(\u22122)</span>.", html);
    }

    // A name with a comma or a tier word does not move the injury outcome or the result word.
    [Fact]
    public void A_name_with_a_comma_or_a_tier_word_does_not_disturb_the_injury_outcome_or_the_tier()
    {
        var injury = new Sim.Missions.InjuryBeat("CREW_02", "ENCOUNTER_03", Sim.Core.Content.InjurySeverity.Light, 5, Sim.Missions.InjuryOutcome.LightInjury);
        Web.Bridge.Names names = NamedCrew02("NAME 7, success");

        string hurt = WebUtility.HtmlDecode(Figures.Injury(Web.Bridge.BeatLines.Line(0, injury, 0, names).Segments).Value);
        string check = WebUtility.HtmlDecode(Figures.Check(Web.Bridge.BeatLines.Line(0, Check, 0, names).Segments).Value);

        Assert.Contains("NAME 7, success takes a light injury roll at ENCOUNTER_03: <span class=\"m\">5</span>, <span class=\"res res--fail\">light injury</span>.", hurt);
        Assert.StartsWith("NAME 7, success checks", check);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(check, "class=\"res\""));
    }
}
