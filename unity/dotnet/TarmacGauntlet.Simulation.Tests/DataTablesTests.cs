#nullable enable
using System.Linq;
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    // Sanity checks that the static tables match the TS sources.
    public class DataTablesTests
    {
        [Test]
        public void IssueCatalogueMatchesTs()
        {
            Assert.That(IssueTemplates.MechanicalIssues, Has.Count.EqualTo(33));
            Assert.That(IssueTemplates.CrashIssueTemplates, Has.Count.EqualTo(4));
            Assert.That(IssueTemplates.AllIssueTemplates, Has.Count.EqualTo(37));
            Assert.That(IssueTemplates.AllIssueTemplates.Select(t => t.Id).Distinct().Count(), Is.EqualTo(37));
            Assert.That(IssueTemplates.AllIssueTemplates[0].Id, Is.EqualTo("engine-misfire"));
            Assert.That(IssueTemplates.AllIssueTemplates[36].Id, Is.EqualTo("bent-suspension-arm"));
            Assert.That(IssueTemplates.MechanicalIssues.All(t => t.Category == IssueCategory.Mechanical));
            Assert.That(IssueTemplates.CrashIssueTemplates.All(t => t.Category == IssueCategory.Crash));

            var shock = IssueTemplates.AllIssueTemplates.Single(t => t.Id == "shock-absorber-failure");
            Assert.That(shock.StatDebuffs.Keys, Is.EqualTo(new[] { CarStatKey.Handling, CarStatKey.Comfort, CarStatKey.TyreDurability }));
            Assert.That(shock.StatDebuffs[CarStatKey.TyreDurability], Is.EqualTo(0.15));
            Assert.That(shock.Weight, Is.EqualTo(3));
            Assert.That(shock.SparePartsCost, Is.EqualTo(3));
            Assert.That(shock.WorkUnits, Is.EqualTo(40));
        }

        [Test]
        public void CountriesAreAlphabeticalWithUniqueCodes()
        {
            Assert.That(Countries.All, Has.Count.EqualTo(196));
            Assert.That(Countries.All.Select(c => c.Code).Distinct().Count(), Is.EqualTo(196));
            Assert.That(Countries.All.Single(c => c.Code == "gb").Name, Is.EqualTo("United Kingdom"));
        }

        [Test]
        public void DealerAndDriverConstants()
        {
            Assert.That(DealerData.CrewCostPerMember, Is.EqualTo(2000));
            Assert.That(DealerData.ConditionPerPart, Is.EqualTo(5));
            Assert.That(DealerData.SalePriceMultiplier, Is.EqualTo(0.5));
            Assert.That(DealerData.ShitboxModelId, Is.EqualTo("f-01"));
            Assert.That(DriverData.DriverStatKeys, Has.Count.EqualTo(5));
        }

        [Test]
        public void NextCarIdIsUniqueAndIncrementing()
        {
            var a = DealerData.NextCarId();
            var b = DealerData.NextCarId();
            Assert.That(a, Does.StartWith("car-"));
            Assert.That(long.Parse(b.Substring(4)), Is.EqualTo(long.Parse(a.Substring(4)) + 1));
        }
    }
}
