#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// All inputs required to process a pit stop.
    /// Intentionally flat so it can be constructed from any source in tests or the race loop.
    /// </summary>
    public sealed record PitStopContext
    {
        /// <summary>Car's effective Pit Stop Time stat (seconds, lower = faster).</summary>
        public double PitStopTime { get; init; }
        /// <summary>Car's maximum fuel capacity (litres).</summary>
        public double FuelCapacity { get; init; }
        /// <summary>Current fuel level (litres).</summary>
        public double CurrentFuel { get; init; }
        /// <summary>Current tyre wear (0 = fresh, 100 = fully worn).</summary>
        public double CurrentTyreWear { get; init; }
        /// <summary>Tyre sets available in team inventory.</summary>
        public int TyreSetsAvailable { get; init; }
        /// <summary>Spare parts units available.</summary>
        public int SparePartsAvailable { get; init; }
        /// <summary>ID of the driver currently in the car.</summary>
        public string CurrentDriverId { get; init; } = "";
        /// <summary>
        /// Fatigue for all drivers on the team (keyed by driver ID, 0 to 100).
        /// Missing entries are treated as 0.
        /// </summary>
        public IReadOnlyDictionary<string, double> DriverFatigue { get; init; } = new Dictionary<string, double>();
        /// <summary>Issues currently affecting the car.</summary>
        public IReadOnlyList<ActiveIssue> ActiveIssues { get; init; } = Array.Empty<ActiveIssue>();
        /// <summary>
        /// Full issue template catalogue, used to look up spare parts cost
        /// and fix duration for issues being repaired.
        /// </summary>
        public IReadOnlyList<IssueTemplate> IssueTemplates { get; init; } = Array.Empty<IssueTemplate>();
        /// <summary>Pit crew headcount (0 to 16).</summary>
        public int CrewSize { get; init; }
        /// <summary>Player's Engineer skill level (0 to 20; pass 0 for AI teams).</summary>
        public int EngineerSkill { get; init; }
    }

    /// <summary>The outcome of a pit stop: updated resource values and the stop duration.</summary>
    public sealed record PitStopResult
    {
        /// <summary>Total stop duration in seconds.</summary>
        public double Duration { get; init; }
        /// <summary>Fuel level after refuelling (litres).</summary>
        public double FuelLevel { get; init; }
        /// <summary>Tyre wear after the stop (0 if tyres changed, otherwise unchanged).</summary>
        public double TyreWear { get; init; }
        /// <summary>Tyre sets remaining after the stop.</summary>
        public int TyreSetsRemaining { get; init; }
        /// <summary>Spare parts remaining after any issue fixes.</summary>
        public int SparePartsRemaining { get; init; }
        /// <summary>Driver ID at the end of the stop (may be same as before if no swap).</summary>
        public string CurrentDriverId { get; init; } = "";
        /// <summary>Updated fatigue map. Incoming driver's fatigue is reset to 0.</summary>
        public IReadOnlyDictionary<string, double> DriverFatigue { get; init; } = new Dictionary<string, double>();
        /// <summary>Active issues remaining after any fixes.</summary>
        public IReadOnlyList<ActiveIssue> ActiveIssues { get; init; } = Array.Empty<ActiveIssue>();
        /// <summary>Always Normal: instruction mode resets on every pit stop (GDD §2).</summary>
        public InstructionMode InstructionMode { get; init; } = InstructionMode.Normal;
    }

    /// <summary>Port of simulation/pitStop.ts.</summary>
    public static class PitStop
    {
        // ---------------------------------------------------------------------------
        // Tunable constants: balance values, adjust freely without touching logic
        // ---------------------------------------------------------------------------

        // Pit-time constants are tuned in concert with the degradation rates so that
        // a class-A car finishes ~45 to 51 laps in the 1440 s race budget (GDD §2). If
        // you change one, recheck the per-class lap-count test in the race loop tests.

        /// <summary>Seconds added to a stop when any fuel is being added (fixed overhead).</summary>
        private const double RefuelBaseSeconds = 1.0;
        /// <summary>Additional seconds per litre added.</summary>
        private const double RefuelSecondsPerLitre = 0.02;
        /// <summary>Seconds added to change a set of tyres.</summary>
        private const double TyreChangeSeconds = 2.5;
        /// <summary>Seconds added for a driver swap.</summary>
        private const double DriverSwapSeconds = 2.0;

        /// <summary>
        /// Duration multiplier when crew size is 0 (player alone).
        /// Crew size 16 → multiplier 1.0; crew size 0 → this value.
        /// </summary>
        private const double SoloCrewMultiplier = 1.8;
        /// <summary>Maximum crew size (matches GDD §5 and game constants).</summary>
        private const double MaxCrewSize = 16;

        /// <summary>Maximum Engineer skill level (matches PlayerSkills.Engineer).</summary>
        private const double MaxEngineerSkill = 20;
        /// <summary>Maximum fractional reduction in pit stop duration from Engineer skill.</summary>
        private const double MaxEngineerPitReduction = 0.25; // skill 20 → 25% faster

        /// <summary>
        /// Estimates the duration of a full-service pit stop (refuel + tyre change + driver swap).
        /// Used by the crew hiring screen to show a reference comparison.
        /// </summary>
        public static double EstimateFullServicePitDuration(
            double pitStopTimeStat,
            double fuelCapacity,
            int crewSize,
            int engineerSkill)
        {
            double taskSeconds = RefuelBaseSeconds + RefuelSecondsPerLitre * fuelCapacity + TyreChangeSeconds + DriverSwapSeconds;
            double crewMultiplier = 1 + (1 - crewSize / MaxCrewSize) * (SoloCrewMultiplier - 1);
            double engineerMultiplier = 1 - (engineerSkill / MaxEngineerSkill) * MaxEngineerPitReduction;
            return (pitStopTimeStat + taskSeconds) * crewMultiplier * engineerMultiplier;
        }

        /// <summary>
        /// How much the crew and the player's Engineer skill scale a stop's work:
        /// crew 16 and no skill 1.0, crew 0 <see cref="SoloCrewMultiplier"/>, skill 20 a quarter off.
        /// </summary>
        public static double CrewMultiplier(int crewSize, int engineerSkill)
        {
            double crewMultiplier = 1 + (1 - crewSize / MaxCrewSize) * (SoloCrewMultiplier - 1);
            double engineerMultiplier = 1 - (engineerSkill / MaxEngineerSkill) * MaxEngineerPitReduction;
            return crewMultiplier * engineerMultiplier;
        }

        /// <summary>
        /// Processes a pit stop and returns the resulting car/driver state.
        ///
        /// Duration formula (GDD §2):
        ///   (pitStopTime + taskSeconds) × crewMultiplier × engineerMultiplier
        ///
        /// Task seconds are accumulated for each operation actually performed:
        ///   - Refuel: base overhead + per-litre rate (only if fuelToAdd &gt; 0)
        ///   - Tyre change: flat cost (only if changeTyres and tyreSetsAvailable &gt; 0)
        ///   - Driver swap: flat cost (only if nextDriverId differs from current)
        ///   - Issue fix: each fixed issue adds its template's workUnits
        ///
        /// Crew multiplier: 1 + (1 − crewSize / MaxCrewSize) × (SoloCrewMultiplier − 1)
        ///   → crew 16 = 1.0×, crew 0 = SoloCrewMultiplier (very slow).
        ///
        /// Engineer multiplier: 1 − (skill / MaxEngineerSkill) × MaxEngineerPitReduction
        ///   → skill 20 = 25% faster, skill 0 = no benefit.
        ///
        /// Design note on driver fatigue on swap:
        ///   When a new driver takes over, their fatigue resets to 0 regardless of any fatigue
        ///   accumulated in earlier stints. This keeps the system simple and predictable.
        /// </summary>
        /// <param name="ctx">Current car, driver, and team state.</param>
        /// <param name="config">What to do during the stop. <c>config.IssueIdsToFix</c> lists the template IDs
        /// of issues to attempt to repair; each is skipped silently if parts are insufficient.</param>
        public static PitStopResult ExecutePitStop(PitStopContext ctx, PitStopConfig config)
        {
            var issueIdsToFix = config.IssueIdsToFix ?? Array.Empty<string>();
            double taskSeconds = 0;
            int tyreSetsRemaining = ctx.TyreSetsAvailable;
            int sparePartsRemaining = ctx.SparePartsAvailable;
            double newFuel = ctx.CurrentFuel;
            double newTyreWear = ctx.CurrentTyreWear;
            string newDriverId = ctx.CurrentDriverId;
            var newFatigue = new Dictionary<string, double>();
            foreach (var kv in ctx.DriverFatigue) newFatigue[kv.Key] = kv.Value;
            var newActiveIssues = new List<ActiveIssue>(ctx.ActiveIssues);

            // 1. Refuel
            if (config.FuelToAdd > 0)
            {
                double actualFuel = Math.Min(config.FuelToAdd, ctx.FuelCapacity - ctx.CurrentFuel);
                if (actualFuel > 0)
                {
                    newFuel = ctx.CurrentFuel + actualFuel;
                    taskSeconds += RefuelBaseSeconds + actualFuel * RefuelSecondsPerLitre;
                }
            }

            // 2. Tyre change: only possible if at least one set is available
            if (config.ChangeTyres && tyreSetsRemaining > 0)
            {
                newTyreWear = 0;
                tyreSetsRemaining -= 1;
                taskSeconds += TyreChangeSeconds;
            }

            // 3. Driver swap: only if a different driver is specified
            string? swapTarget = config.NextDriverId;
            if (swapTarget != null && swapTarget != ctx.CurrentDriverId)
            {
                newDriverId = swapTarget;
                newFatigue[swapTarget] = 0;
                taskSeconds += DriverSwapSeconds;
            }

            // 4. Issue fixes: process in order, stop if parts run out
            foreach (var templateId in issueIdsToFix)
            {
                int issueIndex = newActiveIssues.FindIndex(i => i.TemplateId == templateId);
                if (issueIndex == -1) continue; // issue not present on this car

                var template = ctx.IssueTemplates.FirstOrDefault(t => t.Id == templateId);
                if (template == null) continue; // template not found (data inconsistency, skip)

                if (sparePartsRemaining < template.SparePartsCost) continue; // insufficient parts

                sparePartsRemaining -= template.SparePartsCost;
                taskSeconds += template.WorkUnits;
                newActiveIssues.RemoveAt(issueIndex);
            }

            // 5. Duration calculation
            double crewMultiplier =
                1 + (1 - ctx.CrewSize / MaxCrewSize) * (SoloCrewMultiplier - 1);
            double engineerMultiplier =
                1 - (ctx.EngineerSkill / MaxEngineerSkill) * MaxEngineerPitReduction;
            double duration =
                (ctx.PitStopTime + taskSeconds) * crewMultiplier * engineerMultiplier;

            return new PitStopResult
            {
                Duration = duration,
                FuelLevel = newFuel,
                TyreWear = newTyreWear,
                TyreSetsRemaining = tyreSetsRemaining,
                SparePartsRemaining = sparePartsRemaining,
                CurrentDriverId = newDriverId,
                DriverFatigue = newFatigue,
                ActiveIssues = newActiveIssues,
                InstructionMode = InstructionMode.Normal,
            };
        }
    }
}
