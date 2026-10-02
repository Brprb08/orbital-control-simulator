using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Burn = TrajectoryMatchedPredictor.EncounterBurn;

public class RendezvousPlanner_EditModeTests
{
    [Test]
    public void Completed_burn_queue_keeps_plan_active_for_arrival_coast()
    {
        var owner = new GameObject("Rendezvous lifecycle test");
        try
        {
            var manager = owner.AddComponent<ManeuverNodeManager>();
            var first = new ManeuverNode { burnTime = 10, duration = 2, isFinalized = true };
            var second = new ManeuverNode { burnTime = 20, duration = 3, isFinalized = true };
            typeof(ManeuverNodeManager).GetProperty("CurrentNode").SetValue(manager, first);
            typeof(ManeuverNodeManager).GetProperty("HasRendezvousPlan").SetValue(manager, true);
            typeof(ManeuverNodeManager).GetField("rendezvousPlan", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, new RendezvousPlanner.Result { ArrivalSeconds = 40 });
            var queue = (Queue<ManeuverNode>)typeof(ManeuverNodeManager)
                .GetField("rendezvousQueue", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
            queue.Enqueue(second);

            manager.CompleteNode(first);
            Assert.AreSame(second, manager.CurrentNode);
            Assert.AreEqual(1, manager.GetRendezvousSchedule().Length);
            manager.CompleteNode(second);
            Assert.IsNull(manager.CurrentNode);
            Assert.IsTrue(manager.HasRendezvousPlan, "Arrival must still be verified after the final burn.");
            Assert.IsEmpty(manager.GetRendezvousSchedule());
            Assert.IsFalse(RendezvousFlightSchedule.TryGetLastBurnEnd(
                Array.Empty<Burn>(), out _));
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void Remaining_burn_schedule_reports_last_end_without_assuming_a_burn_exists()
    {
        var burns = new[] {
            new Burn(10, 2, BurnType.Prograde, 1000, 500, 20, 300, true),
            new Burn(20, 3, BurnType.Retrograde, 1000, 500, 20, 300, true)
        };
        Assert.IsTrue(RendezvousFlightSchedule.TryGetLastBurnEnd(burns, out double end));
        Assert.AreEqual(23, end);
        Assert.IsFalse(RendezvousFlightSchedule.TryGetLastBurnEnd(null, out end));
        Assert.AreEqual(0, end);
    }

    [TestCase(RendezvousPlanner.WaypointDirection.Prograde, 0, 10, 0)]
    [TestCase(RendezvousPlanner.WaypointDirection.Retrograde, 0, -10, 0)]
    [TestCase(RendezvousPlanner.WaypointDirection.RadialOut, 10, 0, 0)]
    [TestCase(RendezvousPlanner.WaypointDirection.RadialIn, -10, 0, 0)]
    public void Waypoint_offsets_follow_target_orbital_frame(
        RendezvousPlanner.WaypointDirection direction, double x, double y, double z)
    {
        var goal = new RendezvousPlanner.ArrivalGoal(
            RendezvousPlanner.ArrivalMode.Waypoint, direction, 10, 1);
        double3 offset = goal.Offset(new double3(700, 0, 0), new double3(0, 0.75, 0));
        Assert.AreEqual(x, offset.x * SimulationUnits.MetersPerUnit, 1e-8);
        Assert.AreEqual(y, offset.y * SimulationUnits.MetersPerUnit, 1e-8);
        Assert.AreEqual(z, offset.z * SimulationUnits.MetersPerUnit, 1e-8);
    }

    [Test]
    public void Safe_arrival_rejects_contact_even_when_position_and_velocity_match()
    {
        var goal = new RendezvousPlanner.ArrivalGoal(
            RendezvousPlanner.ArrivalMode.Safe,
            RendezvousPlanner.WaypointDirection.Prograde, 20, 5);
        var target = new double3(700, 0, 0);
        var velocity = new double3(0, 0.75, 0);
        Assert.IsTrue(goal.IsSatisfied(target + goal.Offset(target, velocity),
            velocity + goal.OffsetVelocity(target, velocity, 398.6), target, velocity,
            398.6, 5, 0.5));
        Assert.IsFalse(goal.IsSatisfied(target, velocity, target, velocity,
            398.6, 100, 100));
    }

    [Test]
    public void Intercept_arrival_is_confirmed_by_collision_not_proximity_rule()
    {
        var goal = new RendezvousPlanner.ArrivalGoal(
            RendezvousPlanner.ArrivalMode.Intercept,
            RendezvousPlanner.WaypointDirection.Prograde, 0, 10);
        var same = new double3(700, 0, 0);
        Assert.IsFalse(goal.IsSatisfied(same, double3.zero, same, double3.zero,
            398.6, 100, 100));
    }

    [Test]
    public void RankPlans_changes_choice_with_wait_preference_but_preserves_candidate_pool()
    {
        var early = Plan(120, 100);
        var efficient = Plan(100, 7200);
        var settings = RendezvousPlanner.Settings.Default;
        var fuelChoice = RendezvousPlanner.RankPlans(new[] { early, efficient },
            RendezvousPlanner.TimingPreference.SaveFuel, settings);
        Assert.AreSame(efficient, fuelChoice);
        var soonChoice = RendezvousPlanner.RankPlans(new[] { early, efficient },
            RendezvousPlanner.TimingPreference.StartSooner, settings);
        Assert.AreSame(early, soonChoice);
        Assert.AreEqual(2, soonChoice.Candidates.Length);
    }

    [Test]
    public void Settings_enforce_valid_search_and_validation_ranges()
    {
        var defaults = RendezvousPlanner.Settings.Default;
        var settings = new RendezvousPlanner.Settings(double.NaN, double.PositiveInfinity,
            0, 100, 0, 0, 100, -1, -3, -2, 0, 0, 0, 0, 0, 0, 0);
        Assert.GreaterOrEqual(settings.MinHorizon, 3600);
        Assert.GreaterOrEqual(settings.MaxHorizon, settings.MinHorizon);
        Assert.AreEqual(1, settings.ScalarCandidates);
        Assert.AreEqual(16, settings.VectorCandidates);
        Assert.AreEqual(0, settings.FuelIterations);
        Assert.GreaterOrEqual(settings.ValidationDistance, settings.OptimizerDistance);
        Assert.GreaterOrEqual(settings.ValidationSpeed, settings.OptimizerSpeed);
        Assert.AreEqual(defaults.Goal.Mode, settings.Goal.Mode);
    }

    private static RendezvousPlanner.Result Plan(double deltaV, double start) =>
        new RendezvousPlanner.Result {
            DeltaV = deltaV, ArrivalSeconds = start + 1000,
            Burns = new[] { new Burn(start, 5, BurnType.Prograde, 1000, 500, 20, 300, true) }
        };
}
