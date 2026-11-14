#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// Port of simulation/issueTemplates.ts. Complete mechanical issue catalogue.
    ///
    /// Issues use a two-step roll: first the sim rolls whether *any* issue occurs
    /// (base probability modified by condition, reliability, mode), then picks
    /// *which* issue via a weighted random draw from this catalogue.
    ///
    /// Crash issues are drawn separately when a non-terminal crash occurs.
    /// </summary>
    public static class IssueTemplates
    {
        // Mechanical issues: minor

        private static readonly IssueTemplate[] MechanicalMinor =
        {
            new IssueTemplate
            {
                Id = "engine-misfire",
                Description = "Engine misfire",
                Severity = IssueSeverity.Minor, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Power] = 0.1 },
                Weight = 10, SparePartsCost = 1, WorkUnits = 15,
            },
            new IssueTemplate
            {
                Id = "worn-brake-pads",
                Description = "Worn brake pads",
                Severity = IssueSeverity.Minor, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.08 },
                Weight = 10, SparePartsCost = 1, WorkUnits = 18,
            },
            new IssueTemplate
            {
                Id = "sensor-glitch",
                Description = "Sensor glitch",
                Severity = IssueSeverity.Minor, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.FuelEfficiency] = 0.1 },
                Weight = 10, SparePartsCost = 1, WorkUnits = 10,
            },
            new IssueTemplate
            {
                Id = "radiator-leak",
                Description = "Minor radiator leak",
                Severity = IssueSeverity.Minor, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.FuelEfficiency] = 0.08, [CarStatKey.Reliability] = 0.1 },
                Weight = 8, SparePartsCost = 1, WorkUnits = 18,
            },
            new IssueTemplate
            {
                Id = "loose-exhaust-bracket",
                Description = "Loose exhaust bracket",
                Severity = IssueSeverity.Minor, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Power] = 0.08 },
                Weight = 10, SparePartsCost = 1, WorkUnits = 14,
            },
            new IssueTemplate
            {
                Id = "sticky-synchro",
                Description = "Sticky gear synchro",
                Severity = IssueSeverity.Minor, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Power] = 0.05, [CarStatKey.PitStopTime] = 0.1 },
                Weight = 8, SparePartsCost = 1, WorkUnits = 18,
            },
            new IssueTemplate
            {
                Id = "suspension-knock",
                Description = "Suspension knock",
                Severity = IssueSeverity.Minor, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.1, [CarStatKey.Comfort] = 0.1 },
                Weight = 8, SparePartsCost = 1, WorkUnits = 20,
            },
            new IssueTemplate
            {
                Id = "loose-wheel-nut",
                Description = "Loose wheel nut",
                Severity = IssueSeverity.Minor, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.08, [CarStatKey.Reliability] = 0.1 },
                Weight = 10, SparePartsCost = 1, WorkUnits = 14,
            },
            new IssueTemplate
            {
                Id = "loose-bodywork",
                Description = "Loose bodywork panel",
                Severity = IssueSeverity.Minor, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.05, [CarStatKey.FuelEfficiency] = 0.05 },
                Weight = 10, SparePartsCost = 1, WorkUnits = 10,
            },
            new IssueTemplate
            {
                Id = "steering-play",
                Description = "Steering play",
                Severity = IssueSeverity.Minor, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.1 },
                Weight = 8, SparePartsCost = 1, WorkUnits = 18,
            },
            new IssueTemplate
            {
                Id = "fuel-pump-stutter",
                Description = "Fuel pump stutter",
                Severity = IssueSeverity.Minor, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.FuelEfficiency] = 0.12, [CarStatKey.Power] = 0.05 },
                Weight = 8, SparePartsCost = 1, WorkUnits = 18,
            },
            new IssueTemplate
            {
                Id = "clutch-slip",
                Description = "Slight clutch slip",
                Severity = IssueSeverity.Minor, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Power] = 0.06, [CarStatKey.TyreDurability] = 0.05 },
                Weight = 8, SparePartsCost = 1, WorkUnits = 16,
            },
            new IssueTemplate
            {
                Id = "sticky-throttle",
                Description = "Sticky throttle",
                Severity = IssueSeverity.Minor, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Power] = 0.08, [CarStatKey.FuelEfficiency] = 0.06 },
                Weight = 8, SparePartsCost = 1, WorkUnits = 12,
            },
            new IssueTemplate
            {
                Id = "mirror-damage",
                Description = "Mirror damage",
                Severity = IssueSeverity.Minor, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.04, [CarStatKey.Comfort] = 0.06 },
                Weight = 10, SparePartsCost = 1, WorkUnits = 8,
            },
            new IssueTemplate
            {
                Id = "loose-heat-shielding",
                Description = "Loose heat shielding",
                Severity = IssueSeverity.Minor, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Reliability] = 0.08, [CarStatKey.Comfort] = 0.05 },
                Weight = 8, SparePartsCost = 1, WorkUnits = 14,
            },
            new IssueTemplate
            {
                Id = "headlight-flicker",
                Description = "Headlight flicker",
                Severity = IssueSeverity.Minor, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.06 },
                Weight = 10, SparePartsCost = 1, WorkUnits = 10,
            },
        };

        // Mechanical issues: medium

        private static readonly IssueTemplate[] MechanicalMedium =
        {
            new IssueTemplate
            {
                Id = "oil-pressure-drop",
                Description = "Oil pressure drop",
                Severity = IssueSeverity.Medium, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Power] = 0.2, [CarStatKey.Reliability] = 0.15 },
                Weight = 4, SparePartsCost = 2, WorkUnits = 35,
            },
            new IssueTemplate
            {
                Id = "brake-fade",
                Description = "Brake fade",
                Severity = IssueSeverity.Medium, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.18, [CarStatKey.TyreDurability] = 0.1 },
                Weight = 4, SparePartsCost = 2, WorkUnits = 30,
            },
            new IssueTemplate
            {
                Id = "gearbox-grinding",
                Description = "Gearbox grinding",
                Severity = IssueSeverity.Medium, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Power] = 0.15, [CarStatKey.Handling] = 0.1 },
                Weight = 4, SparePartsCost = 3, WorkUnits = 40,
            },
            new IssueTemplate
            {
                Id = "ecu-fault",
                Description = "ECU fault",
                Severity = IssueSeverity.Medium, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Power] = 0.15, [CarStatKey.FuelEfficiency] = 0.15 },
                Weight = 4, SparePartsCost = 2, WorkUnits = 28,
            },
            new IssueTemplate
            {
                Id = "shock-absorber-failure",
                Description = "Shock absorber failure",
                Severity = IssueSeverity.Medium, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.2, [CarStatKey.Comfort] = 0.2, [CarStatKey.TyreDurability] = 0.15 },
                Weight = 3, SparePartsCost = 3, WorkUnits = 40,
            },
            new IssueTemplate
            {
                Id = "driveshaft-vibration",
                Description = "Driveshaft vibration",
                Severity = IssueSeverity.Medium, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.12, [CarStatKey.Comfort] = 0.15 },
                Weight = 4, SparePartsCost = 2, WorkUnits = 35,
            },
            new IssueTemplate
            {
                Id = "splitter-damage",
                Description = "Splitter damage",
                Severity = IssueSeverity.Medium, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.15, [CarStatKey.FuelEfficiency] = 0.1 },
                Weight = 4, SparePartsCost = 2, WorkUnits = 28,
            },
        };

        // Mechanical issues: major

        private static readonly IssueTemplate[] MechanicalMajor =
        {
            new IssueTemplate
            {
                Id = "engine-overheating",
                Description = "Engine overheating",
                Severity = IssueSeverity.Major, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Power] = 0.35, [CarStatKey.FuelEfficiency] = 0.2 },
                Weight = 1, SparePartsCost = 4, WorkUnits = 55,
            },
            new IssueTemplate
            {
                Id = "brake-disc-crack",
                Description = "Brake disc crack",
                Severity = IssueSeverity.Major, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.3, [CarStatKey.Reliability] = 0.15 },
                Weight = 1, SparePartsCost = 4, WorkUnits = 50,
            },
            new IssueTemplate
            {
                Id = "gearbox-seizure",
                Description = "Gearbox seizure",
                Severity = IssueSeverity.Major, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Power] = 0.3, [CarStatKey.Handling] = 0.15 },
                Weight = 1, SparePartsCost = 5, WorkUnits = 60,
            },
            new IssueTemplate
            {
                Id = "coolant-failure",
                Description = "Coolant system failure",
                Severity = IssueSeverity.Major, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Power] = 0.25, [CarStatKey.Reliability] = 0.3 },
                Weight = 1, SparePartsCost = 4, WorkUnits = 50,
            },
            new IssueTemplate
            {
                Id = "cracked-exhaust-manifold",
                Description = "Cracked exhaust manifold",
                Severity = IssueSeverity.Major, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Power] = 0.25, [CarStatKey.FuelEfficiency] = 0.15 },
                Weight = 1, SparePartsCost = 4, WorkUnits = 45,
            },
            new IssueTemplate
            {
                Id = "injector-failure",
                Description = "Injector failure",
                Severity = IssueSeverity.Major, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Power] = 0.3, [CarStatKey.FuelEfficiency] = 0.25 },
                Weight = 1, SparePartsCost = 4, WorkUnits = 50,
            },
            new IssueTemplate
            {
                Id = "broken-spring",
                Description = "Broken spring",
                Severity = IssueSeverity.Major, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.35, [CarStatKey.Comfort] = 0.3 },
                Weight = 1, SparePartsCost = 4, WorkUnits = 55,
            },
            new IssueTemplate
            {
                Id = "differential-wear",
                Description = "Differential wear",
                Severity = IssueSeverity.Major, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.25, [CarStatKey.Power] = 0.15, [CarStatKey.TyreDurability] = 0.1 },
                Weight = 1, SparePartsCost = 5, WorkUnits = 55,
            },
            new IssueTemplate
            {
                Id = "power-steering-failure",
                Description = "Power steering failure",
                Severity = IssueSeverity.Major, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.35, [CarStatKey.Comfort] = 0.25 },
                Weight = 1, SparePartsCost = 4, WorkUnits = 50,
            },
            new IssueTemplate
            {
                Id = "wheel-bearing-damage",
                Description = "Wheel bearing damage",
                Severity = IssueSeverity.Major, Category = IssueCategory.Mechanical,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.3, [CarStatKey.TyreDurability] = 0.2 },
                Weight = 1, SparePartsCost = 4, WorkUnits = 55,
            },
        };

        // Crash issues (non-terminal)

        private static readonly IssueTemplate[] CrashIssues =
        {
            new IssueTemplate
            {
                Id = "front-wing-damage",
                Description = "Front wing damage",
                Severity = IssueSeverity.Major, Category = IssueCategory.Crash,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.25, [CarStatKey.FuelEfficiency] = 0.15 },
                Weight = 3, SparePartsCost = 4, WorkUnits = 50,
            },
            new IssueTemplate
            {
                Id = "rear-impact-damage",
                Description = "Rear impact damage",
                Severity = IssueSeverity.Major, Category = IssueCategory.Crash,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Power] = 0.2, [CarStatKey.Handling] = 0.2 },
                Weight = 3, SparePartsCost = 4, WorkUnits = 55,
            },
            new IssueTemplate
            {
                Id = "side-panel-crush",
                Description = "Side panel crush",
                Severity = IssueSeverity.Medium, Category = IssueCategory.Crash,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.15, [CarStatKey.Comfort] = 0.2, [CarStatKey.FuelEfficiency] = 0.1 },
                Weight = 3, SparePartsCost = 3, WorkUnits = 45,
            },
            new IssueTemplate
            {
                Id = "bent-suspension-arm",
                Description = "Bent suspension arm",
                Severity = IssueSeverity.Major, Category = IssueCategory.Crash,
                StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.35, [CarStatKey.TyreDurability] = 0.15 },
                Weight = 1, SparePartsCost = 5, WorkUnits = 60,
            },
        };

        // Exports

        /// <summary>All mechanical issue templates (used for the standard per-lap issue roll).</summary>
        public static readonly IReadOnlyList<IssueTemplate> MechanicalIssues =
            MechanicalMinor.Concat(MechanicalMedium).Concat(MechanicalMajor).ToArray();

        /// <summary>Crash issue templates (used when a non-terminal crash occurs).</summary>
        public static readonly IReadOnlyList<IssueTemplate> CrashIssueTemplates = CrashIssues;

        /// <summary>Complete catalogue: all issues combined.</summary>
        public static readonly IReadOnlyList<IssueTemplate> AllIssueTemplates =
            MechanicalIssues.Concat(CrashIssues).ToArray();

        /// <summary>The race-ending mechanical failures: a car that fails is out, and this names what broke.</summary>
        public static readonly IReadOnlyList<IssueTemplate> MechanicalFailures = MechanicalMajor;

        /// <summary>The template with this id, or null.</summary>
        public static IssueTemplate? Find(string? id) =>
            id == null ? null : AllIssueTemplates.FirstOrDefault(t => t.Id == id);
    }
}
