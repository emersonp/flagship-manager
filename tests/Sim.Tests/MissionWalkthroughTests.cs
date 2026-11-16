using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;

namespace Sim.Tests;

// Whole missions on the starter pack, at seeds chosen to show each behaviour the prototype's walkthroughs showed.
public class MissionWalkthroughTests
{
    [Fact]
    public void A_cautious_lead_calls_the_captain_and_brings_the_mission_home()
    {
        (_, List<Beat> beats, MissionResult result) = TestContent.Play(TestContent.Team(1, 1, 3, 5), seed: 1);

        Assert.True(beats.OfType<CallBeat>().Count() >= 2);
        Assert.Equal(MissionOutcome.FullSuccess, result.Outcome);
        Assert.Equal(beats.OfType<CallBeat>().Count(), result.Calls);
    }

    [Fact]
    public void Under_blocked_comms_the_same_lead_must_decide_alone()
    {
        (_, List<Beat> beats, _) = TestContent.Play(TestContent.Team(1, 1, 3, 5), seed: 1);

        Assert.Contains(beats.OfType<InitiativeBeat>(), n => n.Blocked && n.Stakes >= n.Threshold);
    }

    // Seed 3 since the lead chooses by skill (approaches plan, Task 4): at seed 1 the lead now takes their best skill and the team comes home.
    [Fact]
    public void A_weak_team_under_a_reckless_lead_can_collapse()
    {
        (_, List<Beat> beats, MissionResult result) = TestContent.Play(TestContent.Team(2, 2, 7, 8), seed: 3);

        Assert.Single(beats.OfType<CollapseBeat>());
        Assert.Equal(MissionOutcome.Loss, result.Outcome);
    }

    [Fact]
    public void The_same_weak_team_is_offered_the_abort_first_when_strain_builds_slowly()
    {
        (_, List<Beat> beats, _) = TestContent.Play(TestContent.Team(2, 2, 7, 8), seed: 3);

        Assert.Contains(beats.OfType<CallBeat>(), c => c.Call.DecisionId == DecisionIds.Abort);
    }

    [Fact]
    public void Develop_juniors_gives_the_junior_the_checks_within_reach_and_the_senior_the_rest()
    {
        (_, List<Beat> beats, MissionResult result) = TestContent.Play(TestContent.Team(3, 3, 5, 6), seed: 1, new Parameters(DevelopJuniors: true));

        List<CheckBeat> checks = beats.OfType<CheckBeat>().ToList();
        Assert.Contains(checks, c => c.Supervised);
        Assert.Contains(checks, c => !c.Supervised);
        AssertSupervisionCosts(beats, TestContent.Team(3, 3, 5, 6));
        Assert.True(result.Crew.Where(c => c.Id != "CREW_03").Sum(c => c.Experience.Values.Sum()) > 0);
    }

    [Fact]
    public void When_the_lead_falls_the_next_most_senior_takes_over()
    {
        (_, List<Beat> beats, _) = TestContent.Play(TestContent.Team(8, 7, 8), seed: 5);

        Assert.Equal("CREW_07", beats.OfType<LeadChangeBeat>().First().CrewId);
    }

    [Fact]
    public void A_mission_never_changes_the_state_it_was_given_and_always_plays_the_same()
    {
        (MissionState start, List<Beat> first, _) = TestContent.Play(TestContent.Team(1, 1, 3, 5), seed: 1);
        string before = MissionJson.Serialize(start);

        (_, List<Beat> second, _) = TestContent.Play(TestContent.Team(1, 1, 3, 5), seed: 1);

        Assert.Equal(before, MissionJson.Serialize(start));
        Assert.Equal(Json(first), Json(second));
    }

    [Fact]
    public void A_mission_stored_while_a_call_is_pending_resumes_exactly()
    {
        MissionState start = MissionResolver.Start(TestContent.Objective, TestContent.Pack.Encounters, TestContent.Team(1, 1, 3, 5), TestContent.Gear, new Parameters(), TestContent.Tuning, seed: 1);
        MissionStep waiting = MissionResolver.Advance(start);
        Assert.NotNull(waiting.Pending);

        MissionState restored = MissionJson.Deserialize(MissionJson.Serialize(waiting.State));

        Assert.Equal(Json(Finish(waiting.State)), Json(Finish(restored)));
    }

    [Fact]
    public void The_same_seed_draws_the_same_encounters_whatever_team_is_sent()
    {
        (MissionState strong, _, _) = TestContent.Play(TestContent.Team(1, 1, 3, 5), seed: 7);
        (MissionState weak, _, _) = TestContent.Play(TestContent.Team(8, 7, 8), seed: 7);

        Assert.Equal(strong.Encounters.Select(e => e.Encounter.Id), weak.Encounters.Select(e => e.Encounter.Id));
        Assert.Equal(5, strong.Encounters.Count);
    }

    // The content pack may change under a mission in flight; the mission must not.
    [Fact]
    public void A_mission_keeps_the_tuning_it_started_with()
    {
        MissionTuning harsh = TestContent.Tuning with { CheckTarget = 20 };
        MissionState start = MissionResolver.Start(TestContent.Objective, TestContent.Pack.Encounters, TestContent.Team(1, 1, 3, 5), TestContent.Gear, new Parameters(), harsh, seed: 1);

        MissionState restored = MissionJson.Deserialize(MissionJson.Serialize(start));

        Assert.Equal(harsh, restored.Tuning);
        Assert.All(MissionResolver.Advance(restored).Beats.OfType<CheckBeat>(), c => Assert.True(c.Target >= 25));
    }

    // Golden mission: this hash changes only when the resolver's behaviour changes. If the change is intended, update it in the same commit and say why. Re-pinned: the notice beat is named initiative.
    // Re-pinned in slice 1d: check beats gained MomentumAfter and StrainAfter (additive; no rule changed).
    // Re-pinned for authored approaches: a check's approach is written as its id ("careful") where the enum wrote "Careful"; every other field is unchanged.
    // Re-pinned: approaches are chosen by the lead's skill and outcomes are authored (approaches plan, Task 4).
    [Fact]
    public void Golden_mission_matches_the_accepted_beats()
    {
        (_, List<Beat> beats, _) = TestContent.Play(TestContent.Team(1, 1, 3, 5), seed: 1);

        Assert.Equal("228FC64AB185EC43377F03BC373422874D6DDB54E44EDF3967702858E78202A3", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json(beats)))));
    }

    // Replays injuries from the beats to find the score the senior would have had acting as best qualified.
    private static void AssertSupervisionCosts(List<Beat> beats, Team team)
    {
        var injury = team.Members.ToDictionary(m => m.Id, _ => 0);
        var gone = new HashSet<string>();
        foreach (Beat beat in beats)
        {
            if (beat is InjuryBeat i)
            {
                if (i.Outcome == InjuryOutcome.LightInjury)
                {
                    injury[i.CrewId] = Math.Min(TestContent.Tuning.MaxLight, injury[i.CrewId] + 1);
                }
                else if (i.Outcome is InjuryOutcome.Dead or InjuryOutcome.Incapacitated)
                {
                    gone.Add(i.CrewId);
                }
            }
            else if (beat is CheckBeat { Supervised: true } c)
            {
                List<int> skills = team.Members.Where(m => !gone.Contains(m.Id)).Select(m => Math.Max(0, m.Skills[c.Skill] - injury[m.Id])).OrderByDescending(s => s).ToList();
                int bestQualified = skills[0] + (skills[1] / TestContent.Tuning.AssistDivisor) + c.Gear + c.Momentum - c.Strain;
                Assert.True(c.Score < bestQualified, $"supervised {c.Score}, best qualified {bestQualified}");
            }
        }
    }

    private static List<Beat> Finish(MissionState state)
    {
        var beats = new List<Beat>();
        MissionStep step = MissionResolver.Advance(state, state.Pending is { } pending ? new CallReply(pending.CallId, pending.LeadPrefers) : null);
        beats.AddRange(step.Beats);
        while (step.Pending is { } call)
        {
            step = MissionResolver.Advance(step.State, new CallReply(call.CallId, call.LeadPrefers));
            beats.AddRange(step.Beats);
        }

        return beats;
    }

    private static string Json(IEnumerable<Beat> beats)
    {
        return JsonSerializer.Serialize(beats, MissionJson.Options);
    }
}
