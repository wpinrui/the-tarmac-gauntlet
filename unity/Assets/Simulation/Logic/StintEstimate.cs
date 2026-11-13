#nullable enable

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// What a car uses in a lap, learned over the driver's stint: the raw fuel burnt and tyre wear
    /// added on each clean lap, averaged over the stint so far. Until the stint has a clean lap it
    /// knows nothing, and the caller falls back on the crew's estimate. A new driver starts a new
    /// stint. New tyres mid-stint turn the tyre average so far into one lap of data, the starting
    /// point for the new set, and learning carries on from there. Fuel is untouched by a stop.
    /// </summary>
    public sealed class StintEstimate
    {
        private double fuelSum, wearSum;
        private int fuelLaps, wearLaps;
        private double markFuel, markWear;
        private bool marked;

        /// <summary>Fuel a lap uses on average this stint, or null before the stint's first clean lap.</summary>
        public double? FuelPerLap => fuelLaps > 0 ? fuelSum / fuelLaps : (double?)null;
        /// <summary>Tyre wear a lap adds on average this stint, or null before the stint's first clean lap.</summary>
        public double? WearPerLap => wearLaps > 0 ? wearSum / wearLaps : (double?)null;
        /// <summary>Clean laps of fuel data this stint.</summary>
        public int Laps => fuelLaps;

        /// <summary>
        /// The car crosses the line with this fuel and wear. A clean lap, one with no time in the pit
        /// lane, adds what it used since the last crossing. Any other lap only sets the mark for the next.
        /// </summary>
        public void Line(double fuel, double wear, bool clean)
        {
            if (marked && clean)
            {
                double burnt = markFuel - fuel, worn = wear - markWear;
                if (burnt > 0)
                {
                    fuelSum += burnt;
                    fuelLaps++;
                }
                if (worn > 0)
                {
                    wearSum += worn;
                    wearLaps++;
                }
            }
            markFuel = fuel;
            markWear = wear;
            marked = true;
        }

        /// <summary>A new driver is in: the stint's data starts again.</summary>
        public void NewStint()
        {
            fuelSum = wearSum = 0;
            fuelLaps = 0;
            wearLaps = 0;
            marked = false;
        }

        /// <summary>New tyres are on: the tyre average so far becomes a single lap of data.</summary>
        public void NewTyres()
        {
            if (wearLaps <= 0) return;
            wearSum /= wearLaps;
            wearLaps = 1;
        }
    }
}
