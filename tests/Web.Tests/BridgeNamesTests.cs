using System.Text.RegularExpressions;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;
using Web.Bridge;

namespace Web.Tests;

// The read model carries names where a screen shows one, and ids wherever an id is a key.
public class BridgeNamesTests
{
    private static readonly ContentPack Plain = BridgePack.Load();
    private static readonly ContentPack Named = NamedPack.Of(Plain);

    private static BridgeView Read(Played played)
    {
        return BridgeReader.Read(played.Record(), played.Pack, played.CycleEvents());
    }

    private static Played Until(ContentPack pack, Func<Played, bool> condition)
    {
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var played = new Played(pack, seed);
            if (played.StepUntil(() => condition(played)))
            {
                return played;
            }
        }

        throw new InvalidOperationException("No seed reached the wanted state.");
    }

    private static string N(string id)
    {
        return NamedPack.Prefix + id;
    }

    // An id with no name prefix before it, so a name that contains the id does not count.
    private static bool ShowsBareId(string text)
    {
        return Regex.IsMatch(text, @"(?<![A-Z_])(CREW|SKILL|ENCOUNTER|DECISION|GEAR|OBJECTIVE_TEMPLATE|ASSIGNMENT_TEMPLATE)_");
    }

    [Fact]
    public void With_no_names_the_rows_name_is_the_id()
    {
        Played played = Until(Plain, p => p.State.Phase == CyclePhase.Orders && p.State.TryModule(MissionsModule.Name, out MissionsState? m) && m.Site is not null);

        BridgeView view = Read(played);

        Assert.All(view.Roster, r => Assert.Equal(r.Id, r.Name));
        Assert.Equal(view.Site!.AssignmentId, view.Site.AssignmentName);
        Assert.All(view.Site.Objectives, o => Assert.Equal(o.TemplateId, o.Name));
        Assert.All(view.Site.Objectives, o => Assert.Equal(o.PossibleSkills, o.PossibleSkillNames));
    }

    [Fact]
    public void Orders_carry_names_for_the_roster_the_site_and_the_skills_with_ids_kept_as_keys()
    {
        Played played = Until(Named, p => p.State.Phase == CyclePhase.Orders && p.State.TryModule(MissionsModule.Name, out MissionsState? m) && m.Site is not null);

        BridgeView view = Read(played);

        Assert.All(view.Roster, r =>
        {
            Assert.Equal(N(r.Id), r.Name);
            Assert.StartsWith("CREW_", r.Id);
            Assert.All(r.Skills, s => Assert.Equal(N(s.SkillId), s.Name));
        });
        Assert.Equal(N(view.Site!.AssignmentId), view.Site.AssignmentName);
        Assert.All(view.Site.Objectives, o =>
        {
            Assert.Equal(N(o.TemplateId), o.Name);
            Assert.Equal(o.PossibleSkills.Select(N), o.PossibleSkillNames);
            Assert.StartsWith("o_", o.Id);
        });
    }

    [Fact]
    public void The_briefing_carries_gear_names_and_crew_names()
    {
        Played played = Until(Named, p => p.State.Phase == CyclePhase.Briefing);

        BriefingView briefing = Read(played).Briefing!;

        Assert.Equal(Named.Gear.Select(g => g.Id), briefing.Gear.Select(g => g.Id));
        Assert.Equal(Named.Gear.Select(g => N(g.Id)), briefing.Gear.Select(g => g.Name));
        Assert.All(briefing.Crew, c => Assert.Equal(N(c.Id), c.Name));
    }

    [Fact]
    public void A_waiting_call_and_the_team_carry_names_and_every_log_line_shows_names_not_ids()
    {
        Played played = Until(Named, p => p.State.Phase == CyclePhase.Resolution && p.Mission?.State.Pending is not null);

        MissionView mission = Read(played).Mission!;

        Assert.Equal(N(mission.Call!.DecisionId), mission.Call.DecisionName);
        Assert.Equal(N(mission.Call.LeadId), mission.Call.LeadName);
        Assert.Equal(mission.Team.Select(N), mission.TeamNames);
        Assert.Equal(mission.GearIds.Select(N), mission.GearNames);
        Assert.All(mission.Log, l => Assert.False(ShowsBareId(l.Text), l.Text));
        Assert.All(mission.Log.Where(l => l.Kind == "stage"), l => Assert.Equal(N(l.EncounterId!), l.EncounterName));
    }

    [Fact]
    public void The_aftermath_changes_and_unattended_rolls_show_names_and_keep_ids_as_data()
    {
        Played played = Until(Named, p => p.State.Phase == CyclePhase.Aftermath && p.Mission is not null && p.CycleEvents().OfType<ObjectiveUnattended>().Any());

        AftermathView aftermath = Read(played).Aftermath!;

        Assert.NotEmpty(aftermath.Unattended);
        Assert.All(aftermath.Unattended, u => Assert.Equal(N(u.TemplateId), u.TemplateName));
        Assert.All(aftermath.Changes, c =>
        {
            Assert.False(ShowsBareId(c.Text), c.Text);
            Assert.StartsWith("CREW_", c.CrewId);
            Assert.Equal(c.Text, string.Concat(c.Segments.Select(p => p.Text)));
        });
    }

    // Names are display only: the played events are the same with or without them.
    [Fact]
    public void Names_do_not_change_a_single_published_event()
    {
        var plain = new Played(Plain, 3);
        var named = new Played(Named, 3);
        for (int i = 0; i < 60; i++)
        {
            plain.Step();
            named.Step();
        }

        Assert.Equal(plain.Events.Select(plain.Game.Events.ToEnvelope), named.Events.Select(named.Game.Events.ToEnvelope));
    }
}
