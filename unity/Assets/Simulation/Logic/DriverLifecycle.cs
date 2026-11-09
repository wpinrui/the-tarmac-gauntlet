#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>Specification for a new rookie entry (ID and name supplied by caller).</summary>
    public sealed record RookieSpec
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Nationality { get; init; } = "";
    }

    /// <summary>Result returned by <see cref="DriverLifecycle.AdvanceDriverYear"/>.</summary>
    public sealed record DriverYearResult
    {
        /// <summary>Updated full driver pool: aged survivors + new rookies.</summary>
        public IReadOnlyList<Driver> Drivers { get; init; } = Array.Empty<Driver>();
        /// <summary>IDs of the 15 drivers removed from the pool this year.</summary>
        public IReadOnlyList<string> RetiredIds { get; init; } = Array.Empty<string>();
        /// <summary>The 15 newly generated rookie drivers added to the pool.</summary>
        public IReadOnlyList<Driver> Rookies { get; init; } = Array.Empty<Driver>();
    }

    /// <summary>Port of simulation/driverLifecycle.ts.</summary>
    public static class DriverLifecycle
    {
        // ---------------------------------------------------------------------------
        // Tunable constants: balance values, adjust freely without touching logic
        // ---------------------------------------------------------------------------

        // --- Career curve ---
        /// <summary>
        /// Half-width of the sinusoidal career arc in years.
        /// A driver's curve spans peakAge ± HALF_CAREER; outside this window the floor applies.
        /// </summary>
        private const double HalfCareer = 16;
        /// <summary>
        /// Minimum stat fraction at career extremes.
        /// An 18-year-old with HALF_CAREER = 16 and peakAge = 30 is ~12 years before peak;
        /// STAT_FLOOR ensures they show roughly 15% of their peak potential at entry.
        /// </summary>
        private const double StatFloor = 0.15;

        // --- Market value ---
        /// <summary>Minimum market value ($/driver): even the worst driver has some nominal worth.</summary>
        private const double MinMarketValue = 500;
        /// <summary>Maximum market value: a theoretically perfect 5×100 driver.</summary>
        private const double MaxMarketValue = 500_000;
        /// <summary>Maximum possible total stat sum (5 stats × 100 each).</summary>
        private const double MaxTotalStats = 500;
        /// <summary>Exponent for the market value curve (&gt;1 concentrates value at the top).</summary>
        private const double MarketValueExponent = 2;

        // --- Annual salary ---
        /// <summary>Minimum annual salary (GDD §4: bottom rookies ~$500).</summary>
        private const double MinSalary = 500;
        /// <summary>Maximum annual salary (GDD §4: elite peak-age ~$150,000).</summary>
        private const double MaxSalary = 150_000;
        /// <summary>
        /// Exponent for the salary curve.
        /// Higher exponent steepens the curve: elite drivers command much more than midfield.
        /// Calibrated so a 200-total-stat midfield driver earns ~$10k and 350-stat earns ~$52k.
        /// </summary>
        private const double SalaryExponent = 3;

        // --- Contract discounts ---
        /// <summary>Salary discount applied when signing a multi-year contract (GDD §4).</summary>
        private static readonly IReadOnlyDictionary<int, double> ContractLengthDiscounts = new Dictionary<int, double>
        {
            [1] = 0,
            [2] = 0.10, // 10% for two-year contracts
            [3] = 0.20, // 20% for three-year contracts
        };
        /// <summary>Maximum additional discount from the player's Business skill.</summary>
        private const double MaxBusinessExtraDiscount = 0.10;
        /// <summary>Maximum Business skill level (matches PlayerSkills.business in team.ts).</summary>
        private const double MaxBusinessSkill = 20;

        // --- Buyout ---
        /// <summary>Buyout cost multiplier on remaining salary (GDD §4: ×1.5).</summary>
        private const double BuyoutMultiplier = 1.5;

        // --- Rookie generation ---
        /// <summary>Age of all newly generated rookie drivers (GDD §4).</summary>
        private const int RookieAge = 18;
        /// <summary>Minimum career peak age for generated rookies.</summary>
        private const double RookiePeakAgeMin = 28;
        /// <summary>Inclusive range above the minimum for peak age selection (gives 28–32).</summary>
        private const double RookiePeakAgeRange = 4;
        /// <summary>Minimum peak-stat value for a generated rookie.</summary>
        private const double RookiePeakStatMin = 40;
        /// <summary>Inclusive range above the minimum (gives peak stats in [40, 80]).</summary>
        private const double RookiePeakStatRange = 40;
        /// <summary>Full range for per-stat phase offset in years; offset is in [−half, +half].</summary>
        private const double RookiePhaseOffsetRange = 4;

        // --- Annual turnover ---
        /// <summary>Number of drivers retired and replaced each year (GDD §4).</summary>
        private const int AnnualRetirements = 15;

        // ---------------------------------------------------------------------------
        // Stat curve
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Computes a driver's current stats from their sinusoidal career curve (GDD §4).
        ///
        /// Each stat peaks at <c>peakAge + phaseOffset[stat]</c> then declines symmetrically.
        /// The shape function uses a raised cosine bounded by STAT_FLOOR so stats never
        /// reach zero: even late-career drivers retain STAT_FLOOR × peakStat.
        ///
        /// Shape formula:
        ///   t = age − (peakAge + phaseOffset)
        ///   rawShape = max(0, (1 + cos(t × π / HALF_CAREER)) / 2)
        ///   shape    = STAT_FLOOR + (1 − STAT_FLOOR) × rawShape
        ///   stat     = round(peakStat × shape), clamped to [0, 100]
        /// </summary>
        public static DriverStats CalculateDriverStats(Driver driver)
        {
            var peakAge = driver.CurveParams.PeakAge;
            var peakStats = driver.CurveParams.PeakStats;
            var phaseOffsets = driver.CurveParams.PhaseOffsets;

            double StatAtAge(double peakStat, double phaseOffset)
            {
                var t = driver.Age - (peakAge + phaseOffset);
                var rawShape = Math.Max(0, (1 + Math.Cos((t * Math.PI) / HalfCareer)) / 2);
                var shape = StatFloor + (1 - StatFloor) * rawShape;
                return Math.Min(100, Math.Max(0, JsMath.Round(peakStat * shape)));
            }

            return new DriverStats
            {
                Pace        = StatAtAge(peakStats.Pace,        phaseOffsets.Pace),
                Consistency = StatAtAge(peakStats.Consistency, phaseOffsets.Consistency),
                Stamina     = StatAtAge(peakStats.Stamina,     phaseOffsets.Stamina),
                Safety      = StatAtAge(peakStats.Safety,      phaseOffsets.Safety),
                Smoothness  = StatAtAge(peakStats.Smoothness,  phaseOffsets.Smoothness),
            };
        }

        // ---------------------------------------------------------------------------
        // Market value and salary
        // ---------------------------------------------------------------------------

        /// <summary>Returns the sum of all five driver stats (0–500).</summary>
        public static double TotalDriverStats(DriverStats stats) =>
            stats.Pace + stats.Consistency + stats.Stamina + stats.Safety + stats.Smoothness;

        /// <summary>
        /// Calculates a driver's market value from their current stats.
        ///
        /// Uses a power-law curve from MIN_MARKET_VALUE (total=0) to MAX_MARKET_VALUE (total=500).
        /// Market value is displayed on the driver market screen and informs salary calculation.
        /// </summary>
        public static double CalculateMarketValue(DriverStats stats)
        {
            var normalised = TotalDriverStats(stats) / MaxTotalStats;
            return JsMath.Round(
                MinMarketValue +
                    (MaxMarketValue - MinMarketValue) * Math.Pow(normalised, MarketValueExponent));
        }

        /// <summary>
        /// Calculates the base annual salary for a driver based on their current stats (GDD §4).
        ///
        /// Uses a steeper power-law curve than market value to better reflect real salary ranges:
        ///   - Total ≈  50 stats → ~$650    (entry-level rookie)
        ///   - Total ≈ 200 stats → ~$10k   (solid but unremarkable)
        ///   - Total ≈ 350 stats → ~$52k   (strong midfield/front runner)
        ///   - Total = 500 stats → $150k   (theoretically perfect driver)
        ///
        /// Note: these are balance placeholders. Adjust SALARY_EXPONENT and MIN/MAX_SALARY freely.
        /// </summary>
        public static double CalculateAnnualSalary(DriverStats stats)
        {
            var normalised = TotalDriverStats(stats) / MaxTotalStats;
            return JsMath.Round(
                MinSalary + (MaxSalary - MinSalary) * Math.Pow(normalised, SalaryExponent));
        }

        // ---------------------------------------------------------------------------
        // Contract system
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Returns the per-year contract salary after applying length discount and
        /// Business-skill amplification (GDD §4).
        ///
        /// Total discount = contractLengthDiscount + (businessSkill / MAX_BUSINESS_SKILL) × MAX_BUSINESS_EXTRA_DISCOUNT
        ///
        /// Example: 3-year contract + Business skill 20 → 20% + 10% = 30% off base salary.
        /// </summary>
        /// <param name="length">TS <c>ContractLength</c>: 1, 2 or 3.</param>
        public static double CalculateContractSalary(double annualSalary, int length, int businessSkill)
        {
            var lengthDiscount = ContractLengthDiscounts[length];
            var businessDiscount = (businessSkill / MaxBusinessSkill) * MaxBusinessExtraDiscount;
            var totalDiscount = Math.Min(1, lengthDiscount + businessDiscount);
            return JsMath.Round(annualSalary * (1 - totalDiscount));
        }

        /// <summary>
        /// Calculates the one-time cost to buy out an active contract early (GDD §4).
        ///
        /// Buyout cost = annualSalary × remainingYears × BUYOUT_MULTIPLIER (1.5×).
        /// </summary>
        public static double CalculateBuyoutCost(Contract contract) =>
            JsMath.Round(contract.AnnualSalary * contract.RemainingYears * BuyoutMultiplier);

        /// <summary>
        /// Returns true if the driver currently has no active contract with any team.
        ///
        /// A driver with remainingYears = 0 is treated as expired and therefore a free agent;
        /// the caller is responsible for decrementing remainingYears at year-end.
        /// </summary>
        public static bool IsDriverFreeAgent(string driverId, IReadOnlyList<Contract> contracts) =>
            !contracts.Any(c => c.DriverId == driverId && c.RemainingYears > 0);

        // ---------------------------------------------------------------------------
        // Rookie generation
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Generates a new 18-year-old rookie driver with randomised career potential (GDD §4).
        ///
        /// Peak stats are drawn uniformly from [ROOKIE_PEAK_STAT_MIN, ROOKIE_PEAK_STAT_MIN + ROOKIE_PEAK_STAT_RANGE].
        /// At age 18 (≈12 years before a typical peak of 30), actual stats are roughly 25–35% of peak:
        /// naturally "low starting stats" without needing a separate formula.
        /// </summary>
        /// <param name="spec">ID and name for the new driver.</param>
        /// <param name="random">Injectable random source.</param>
        public static Driver GenerateRookie(RookieSpec spec, Func<double> random)
        {
            var peakAge = RookiePeakAgeMin + Math.Floor(random() * (RookiePeakAgeRange + 1));

            double RandomPeakStat() => JsMath.Round(RookiePeakStatMin + random() * RookiePeakStatRange);

            double RandomPhaseOffset() => (random() - 0.5) * RookiePhaseOffsetRange;

            var peakStats = new DriverStats
            {
                Pace        = RandomPeakStat(),
                Consistency = RandomPeakStat(),
                Stamina     = RandomPeakStat(),
                Safety      = RandomPeakStat(),
                Smoothness  = RandomPeakStat(),
            };

            var phaseOffsets = new DriverStats
            {
                Pace        = RandomPhaseOffset(),
                Consistency = RandomPhaseOffset(),
                Stamina     = RandomPhaseOffset(),
                Safety      = RandomPhaseOffset(),
                Smoothness  = RandomPhaseOffset(),
            };

            var curveParams = new DriverCurveParams { PeakAge = peakAge, PeakStats = peakStats, PhaseOffsets = phaseOffsets };
            var stub = new Driver
            {
                Id = spec.Id,
                Name = spec.Name,
                Nationality = spec.Nationality,
                Age = RookieAge,
                CurveParams = curveParams,
                MarketValue = 0,
            };
            var stats = CalculateDriverStats(stub);
            return stub with { MarketValue = CalculateMarketValue(stats) };
        }

        // ---------------------------------------------------------------------------
        // Annual lifecycle
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Advances the driver pool by one year (GDD §4):
        ///
        ///   1. Ages every driver by 1 year.
        ///   2. Recalculates market value for each (stats change with age).
        ///   3. Retires the ANNUAL_RETIREMENTS (15) drivers with the lowest total stat sum.
        ///      Retirement applies regardless of contract status; the caller handles contract cleanup.
        ///   4. Adds the provided rookies (generated externally so IDs and names are caller-controlled).
        /// </summary>
        /// <param name="drivers">Current full driver pool.</param>
        /// <param name="rookieSpecs">Exactly ANNUAL_RETIREMENTS entries: new rookies to add.</param>
        /// <param name="random">Injectable random source (used only by GenerateRookie internally).</param>
        public static DriverYearResult AdvanceDriverYear(
            IReadOnlyList<Driver> drivers,
            IReadOnlyList<RookieSpec> rookieSpecs,
            Func<double> random)
        {
            // 1. Age every driver and recalculate market value
            var aged = drivers.Select(d =>
            {
                var older = d with { Age = d.Age + 1 };
                var stats = CalculateDriverStats(older);
                return older with { MarketValue = CalculateMarketValue(stats) };
            }).ToList();

            // 2. Sort by total stat sum ascending; retire the bottom ANNUAL_RETIREMENTS.
            // OrderBy is stable, like JS Array.prototype.sort.
            var sorted = aged.OrderBy(d => TotalDriverStats(CalculateDriverStats(d))).ToList();

            var retirees = sorted.Take(AnnualRetirements).ToList();
            var retiredIds = retirees.Select(d => d.Id).ToList();
            var survivors = aged.Where(d => !retiredIds.Contains(d.Id)).ToList();

            // 3. Generate rookies from provided specs
            var rookies = rookieSpecs.Select(spec => GenerateRookie(spec, random)).ToList();

            return new DriverYearResult
            {
                Drivers = survivors.Concat(rookies).ToList(),
                RetiredIds = retiredIds,
                Rookies = rookies,
            };
        }
    }
}
