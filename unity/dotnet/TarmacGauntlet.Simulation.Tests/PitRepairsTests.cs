#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    public class PitRepairsTests
    {
        private static readonly CarStats Stats = new CarStats { Power = 60, Handling = 60, FuelEfficiency = 60, Reliability = 60 };

        private static StintOutlook Stint(double laps) => new StintOutlook
        {
            Laps = laps,
            LapSeconds = 100,
            LitresPerLap = 3,
            SecondsPerLitre = 0.33,
            CrewMultiplier = 1,
        };

        private static ActiveIssue[] Issues(params string[] ids) => ids.Select(id => new ActiveIssue { TemplateId = id }).ToArray();

        [Test]
        public void AllAndNoneAreAsSaid()
        {
            ActiveIssue[] active = Issues("engine-misfire", "mirror-damage");
            Assert.That(PitRepairs.Choose(RepairMode.All, active, Stats, Stint(5)), Is.EqualTo(new[] { "engine-misfire", "mirror-damage" }));
            Assert.That(PitRepairs.Choose(RepairMode.None, active, Stats, Stint(5)), Is.Empty);
        }

        [Test]
        public void TheCrewAlwaysFixARiskToTheFinish()
        {
            // A loose heat shield costs no pace, but it cuts Reliability.
            ActiveIssue[] active = Issues("loose-heat-shielding");
            Assert.That(PitRepairs.Choose(RepairMode.Crew, active, Stats, Stint(0.1)), Is.EqualTo(new[] { "loose-heat-shielding" }));
        }

        [Test]
        public void TheCrewFixAPaceLossOnlyWhenTheStintPaysItBack()
        {
            ActiveIssue[] active = Issues("engine-misfire");
            Assert.That(PitRepairs.Choose(RepairMode.Crew, active, Stats, Stint(1)), Is.Empty);
            Assert.That(PitRepairs.Choose(RepairMode.Crew, active, Stats, Stint(20)), Is.EqualTo(new[] { "engine-misfire" }));
        }

        [Test]
        public void ExtraFuelCountsAgainstAFuelEfficiencyCut()
        {
            IssueTemplate glitch = IssueTemplates.AllIssueTemplates.First(t => t.Id == "sensor-glitch");
            double lost = PitRepairs.Lost(glitch, Stats, Stint(10));
            // 30 litres over the stint, a tenth more for the cut, at a third of a second a litre.
            Assert.That(lost, Is.EqualTo(10 * 3 * (1 / 0.9 - 1) * 0.33).Within(1e-9));
        }

        [Test]
        public void RisksGetTheSparesFirst()
        {
            ActiveIssue[] active = Issues("engine-misfire", "radiator-leak");
            IReadOnlyList<string> fix = PitRepairs.Choose(RepairMode.Crew, active, Stats, Stint(50));
            Assert.That(fix, Is.EqualTo(new[] { "radiator-leak", "engine-misfire" }));
        }
    }
}
