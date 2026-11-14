#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>TS <c>FailureType</c>.</summary>
    public enum FailureType
    {
        Mechanical,
        Crash,
        /// <summary>The player's team took the car out of the race from the pause menu.</summary>
        Withdrawn,
    }

    public sealed record LapRiskContext
    {
        /// <summary>The lap number being simulated (stored on newly created ActiveIssue).</summary>
        public int CurrentLap { get; init; }
        /// <summary>Current car condition (0 to 100).</summary>
        public double Condition { get; init; }
        /// <summary>Car age in years.</summary>
        public int CarAge { get; init; }
        /// <summary>Effective Reliability stat (0 to 100). Higher = fewer incidents.</summary>
        public double Reliability { get; init; }
        /// <summary>Driver's Safety stat (0 to 100). Higher = lower failure probability.</summary>
        public double DriverSafety { get; init; }
        /// <summary>Current driver instruction mode.</summary>
        public InstructionMode InstructionMode { get; init; }
        /// <summary>
        /// Issues already active on the car.
        /// Templates with an already-active issue are skipped to prevent duplicates.
        /// </summary>
        public IReadOnlyList<ActiveIssue> ActiveIssues { get; init; } = Array.Empty<ActiveIssue>();
        /// <summary>Mechanical issue templates for the weighted draw.</summary>
        public IReadOnlyList<IssueTemplate> IssueTemplates { get; init; } = Array.Empty<IssueTemplate>();
        /// <summary>
        /// Injectable random source for deterministic testing.
        /// Roll result &lt; probability → event occurs.
        /// Null uses <see cref="DefaultRandom"/>.
        /// </summary>
        public Func<double>? Random { get; init; }
        /// <summary>
        /// How many of the race maths' laps this roll covers: 1 for one lap, 0.5 for half the time.
        /// Each chance is compounded over it, so the risk follows time on track, not laps counted.
        /// </summary>
        public double Exposure { get; init; } = 1;
        /// <summary>
        /// False skips the race-ending failure roll: the driven race rolls it on its own through the lap
        /// (<see cref="RiskRolls.RollFailure"/>), so a failure can strike anywhere on track, not only at the line.
        /// </summary>
        public bool RollFailure { get; init; } = true;
    }

    public sealed record LapRiskResult
    {
        /// <summary>Issues that newly occurred this lap.</summary>
        public IReadOnlyList<ActiveIssue> NewIssues { get; init; } = Array.Empty<ActiveIssue>();
        /// <summary>
        /// Race-ending failure type, or null if none occurred.
        /// If non-null, the car should be retired; NewIssues will be empty.
        /// </summary>
        public FailureType? Failure { get; init; }
        /// <summary>For a mechanical failure, the id of what broke (<see cref="IssueTemplates.MechanicalFailures"/>); else null.</summary>
        public string? FailureId { get; init; }
    }

    /// <summary>Port of simulation/riskRolls.ts.</summary>
    public static class RiskRolls
    {
        // ---------------------------------------------------------------------------
        // Tunable constants: balance values, adjust freely without touching logic
        // ---------------------------------------------------------------------------

        // --- Issue probability modifiers ---
        /// <summary>Base chance of any issue occurring per lap under ideal conditions.</summary>
        private const double BaseIssueProb = 0.05;

        /// <summary>
        /// At 0% condition this factor is added on top of 1.0, tripling the base issue probability.
        /// At 100% condition the modifier is 1.0 (no change).
        /// </summary>
        private const double MaxConditionIssueScale = 2.0; // condition=0 → ×3.0, condition=100 → ×1.0

        /// <summary>At Reliability=100 the issue probability is reduced by this fraction.</summary>
        private const double MaxReliabilityIssueReduction = 0.6; // reliability=100 → ×0.40

        /// <summary>Per-lap issue probability multipliers by instruction mode.</summary>
        private static double IssueModeScale(InstructionMode mode) => mode switch
        {
            InstructionMode.Push => 1.5,
            InstructionMode.Normal => 1.0,
            InstructionMode.Conserve => 0.7,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        // --- Failure probability modifiers ---
        /// <summary>Base chance of a race-ending failure per lap under ideal conditions.</summary>
        private const double BaseFailureProb = 0.003;

        /// <summary>
        /// At 0% condition this factor is added on top of 1.0, quintupling the failure probability.
        /// Stronger than the issue scale: condition is a primary failure driver.
        /// </summary>
        private const double MaxConditionFailureScale = 4.0; // condition=0 → ×5.0, condition=100 → ×1.0

        /// <summary>Fractional increase in failure probability per year of car age.</summary>
        private const double AgeFailureRate = 0.04; // age 10 → ×1.4
        /// <summary>Cap on the age failure multiplier (mirrors EffectiveStats / Degradation patterns).</summary>
        private const double MaxAgeFailureFactor = 2.5; // reached at age ~37.5

        /// <summary>At Reliability=100 the failure probability is reduced by this fraction.</summary>
        private const double MaxReliabilityFailureReduction = 0.6;

        /// <summary>At Safety=100 the failure probability is reduced by this fraction.</summary>
        private const double MaxSafetyReduction = 0.5; // skilled driver can halve failure risk

        /// <summary>Per-lap failure probability multipliers by instruction mode. Push is noticeably dangerous.</summary>
        private static double FailureModeScale(InstructionMode mode) => mode switch
        {
            InstructionMode.Push => 2.0,
            InstructionMode.Normal => 1.0,
            InstructionMode.Conserve => 0.6,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        // ---------------------------------------------------------------------------
        // Probability helpers (public for testing, as in TS)
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Effective probability that any issue occurs this lap.
        /// Two-step system: this is the "does something break?" roll.
        /// </summary>
        public static double IssueEffectiveProbability(double condition, double reliability, InstructionMode mode)
        {
            double conditionFactor = 1 + (1 - condition / 100) * MaxConditionIssueScale;
            double reliabilityFactor = 1 - (reliability / 100) * MaxReliabilityIssueReduction;
            return BaseIssueProb * conditionFactor * reliabilityFactor * IssueModeScale(mode);
        }

        /// <summary>Effective failure probability given the current car and driver state.</summary>
        public static double FailureEffectiveProbability(
            double condition,
            int carAge,
            double reliability,
            double driverSafety,
            InstructionMode mode)
        {
            double conditionFactor = 1 + (1 - condition / 100) * MaxConditionFailureScale;
            double ageFactor = Math.Min(MaxAgeFailureFactor, 1 + carAge * AgeFailureRate);
            double reliabilityFactor = 1 - (reliability / 100) * MaxReliabilityFailureReduction;
            double safetyFactor = 1 - (driverSafety / 100) * MaxSafetyReduction;
            return
                BaseFailureProb *
                conditionFactor *
                ageFactor *
                reliabilityFactor *
                safetyFactor *
                FailureModeScale(mode);
        }

        /// <summary>The chance of at least one event in <paramref name="exposure"/> laps, at <paramref name="perLap"/> a lap.</summary>
        public static double Over(double perLap, double exposure)
        {
            if (exposure <= 0) return 0;
            if (exposure == 1) return perLap;
            return 1 - Math.Pow(1 - Math.Min(1, Math.Max(0, perLap)), exposure);
        }

        /// <summary>
        /// Picks one template from the list via weighted random selection.
        /// Templates already active on the car are excluded.
        /// Returns null if no eligible templates remain.
        /// </summary>
        public static IssueTemplate? WeightedPick(
            IReadOnlyList<IssueTemplate> templates,
            IReadOnlyList<ActiveIssue> activeIssues,
            Func<double> random)
        {
            var activeIds = new HashSet<string>(activeIssues.Select(i => i.TemplateId));
            var eligible = templates.Where(t => !activeIds.Contains(t.Id)).ToList();
            if (eligible.Count == 0) return null;

            double totalWeight = eligible.Sum(t => t.Weight);
            double roll = random() * totalWeight;
            foreach (var template in eligible)
            {
                roll -= template.Weight;
                if (roll <= 0) return template;
            }
            return eligible[eligible.Count - 1]; // floating point safety
        }

        /// <summary>
        /// Rolls for mechanical issues and race-ending mechanical failures for one lap (GDD §2).
        ///
        /// Two-step issue system:
        ///   1. Roll base issue probability (modified by condition, reliability, mode).
        ///   2. If triggered, weighted pick from the mechanical issue catalogue.
        ///
        /// Failure: one roll for a terminal mechanical failure (modified by condition, age,
        /// reliability, safety, mode), rolled first and taking precedence. Crashes are never rolled:
        /// they come only from real hits on track.
        /// </summary>
        public static LapRiskResult RollLapRisks(LapRiskContext ctx)
        {
            var random = ctx.Random ?? DefaultRandom.Next;

            // 1. Check for terminal failure first.
            if (ctx.RollFailure)
            {
                LapRiskResult failure = RollFailure(ctx);
                if (failure.Failure.HasValue) return failure;
            }

            // 2. Two-step issue roll: does something break?
            double issueProb = IssueEffectiveProbability(ctx.Condition, ctx.Reliability, ctx.InstructionMode);
            var newIssues = new List<ActiveIssue>();

            if (random() < Over(issueProb, ctx.Exposure))
            {
                // Weighted pick from mechanical catalogue
                var picked = WeightedPick(ctx.IssueTemplates, ctx.ActiveIssues, random);
                if (picked != null)
                {
                    newIssues.Add(new ActiveIssue { TemplateId = picked.Id, LapOccurred = ctx.CurrentLap });
                }
            }

            return new LapRiskResult { NewIssues = newIssues, Failure = null };
        }

        /// <summary>
        /// The race-ending mechanical failure roll alone, over <see cref="LapRiskContext.Exposure"/> laps' worth of
        /// driving, with what broke picked at random (<see cref="PickFailure"/>). No failure gives an empty result.
        /// </summary>
        public static LapRiskResult RollFailure(LapRiskContext ctx)
        {
            var random = ctx.Random ?? DefaultRandom.Next;
            double failureProb = FailureEffectiveProbability(
                ctx.Condition,
                ctx.CarAge,
                ctx.Reliability,
                ctx.DriverSafety,
                ctx.InstructionMode);
            if (random() >= Over(failureProb, ctx.Exposure)) return new LapRiskResult();
            return new LapRiskResult { Failure = FailureType.Mechanical, FailureId = PickFailure(random) };
        }

        /// <summary>What broke in a race-ending mechanical failure: a weighted draw over <see cref="IssueTemplates.MechanicalFailures"/>.</summary>
        public static string PickFailure(Func<double> random) =>
            WeightedPick(IssueTemplates.MechanicalFailures, Array.Empty<ActiveIssue>(), random)!.Id;
    }
}
