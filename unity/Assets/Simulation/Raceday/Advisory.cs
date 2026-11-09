#nullable enable
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>How long the player's resources last at the chosen mode (GDD §2 advisory).</summary>
    public static class Advisory
    {
        private const int MaxLaps = 99;

        /// <summary>Laps until the tyres are fully worn, at the given mode from now on.</summary>
        public static int TyreLapsLeft(CarEntry entry, CarLapSnapshot snap, InstructionMode mode)
        {
            CarStats stats = Stats(entry, snap);
            RaceDriver driver = Driver(entry, snap);
            double wear = snap.TyreWear;
            int laps = 0;
            while (laps < MaxLaps)
            {
                wear = Degradation.UpdateTyreWear(wear, stats.TyreDurability, driver.Stats.Smoothness, mode);
                if (wear >= 100) break;
                laps++;
            }
            return laps;
        }

        /// <summary>Laps until the tank runs dry, at the given mode from now on.</summary>
        public static int FuelLapsLeft(CarEntry entry, CarLapSnapshot snap, InstructionMode mode)
        {
            CarStats stats = Stats(entry, snap);
            double fuel = snap.FuelRemaining;
            int laps = 0;
            while (laps < MaxLaps)
            {
                fuel = Degradation.UpdateFuelLevel(fuel, stats.FuelEfficiency, mode);
                if (fuel <= 0) break;
                laps++;
            }
            return laps;
        }

        private static CarStats Stats(CarEntry entry, CarLapSnapshot snap) =>
            EffectiveStats.CalculateEffectiveStats(entry.Instance with { Condition = snap.Condition }, entry.Model);

        private static RaceDriver Driver(CarEntry entry, CarLapSnapshot snap) =>
            entry.Drivers.FirstOrDefault(d => d.Id == snap.CurrentDriverId) ?? entry.Drivers[0];
    }
}
