using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>Push gives more power for more fuel and tyre wear. Conserve gives less of all three.</summary>
    public enum EngineMap { Conserve, Normal, Push }

    /// <summary>
    /// What the team asks for at the car's next stop, from live timing or the pit panel (<see cref="PitPanel"/>).
    /// Left alone, the car pits when it needs to, and the crew make each call (<see cref="StopPlanner"/>). Every
    /// stop puts the orders back to that.
    /// </summary>
    public sealed class PitOrders
    {
        /// <summary>True once the team calls the car in: it pits the next time it reaches the pit entry.</summary>
        public bool Requested;
        /// <summary>Laps of fuel to leave with, or null for the crew's call.</summary>
        public int? Laps;
        /// <summary>True for new tyres, false to keep them on, or null for the crew's call.</summary>
        public bool? Tyres;
        public RepairMode Repairs;

        public void Reset()
        {
            Requested = false;
            Laps = null;
            Tyres = null;
            Repairs = RepairMode.Crew;
        }
    }

    /// <summary>
    /// A car's fuel, tyre wear and engine map, with no Unity objects so it can be tested outside Unity.
    /// How much one lap uses at Normal comes from the race maths (<see cref="Degradation"/>): the car's
    /// Fuel Efficiency and Tyre Durability and the driver's Smoothness. Within a lap, fuel follows the
    /// work the engine does and wear follows how hard the tyres are worked, so a driver who cruises
    /// uses less than one at the limit. In a career race it carries the car's <see cref="CarRaceLog"/>,
    /// whose active issues cut power, grip, fuel efficiency and tyre life (<see cref="Effects"/>).
    /// </summary>
    public sealed class Consumables
    {
        /// <summary>kg per litre of fuel.</summary>
        public const float FuelDensity = 0.75f;
        private const float IdleBurn = 0.0004f;   // L/s with the engine just running

        public readonly float Capacity;
        /// <summary>Litres a lap at the limit uses at Normal.</summary>
        public readonly float FuelPerLap;
        /// <summary>Share of a tyre set a lap at the limit wears at Normal.</summary>
        public readonly float WearPerLap;

        public float Fuel;
        /// <summary>0 for new tyres, 1 for worn out.</summary>
        public float Wear;
        public EngineMap Map = EngineMap.Normal;
        /// <summary>The car's race in the career, or null outside one: its stops use its tyre sets, spares and lineup.</summary>
        public CarRaceLog Log;
        /// <summary>How the car's active issues scale its stats.</summary>
        public IssueEffects Effects = IssueEffects.None;
        /// <summary>What the team asks for at the next stop.</summary>
        public readonly PitOrders Orders = new PitOrders();
        /// <summary>Fuel and tyre wear a lap uses, learned over the driver's stint (<see cref="RaceDirector"/>, <see cref="PitService"/>).</summary>
        public readonly StintEstimate Stint = new StintEstimate();
        /// <summary>Plans the player's stops from <see cref="Orders"/>, or null for a car that fills up and changes everything.</summary>
        public StopPlanner Planner;
        /// <summary>
        /// A career car's power and grip next to a new one of its model: its age and condition wear
        /// them down, and its upgrades lift them. The physics car is the model's, so the tuned lines are shared.
        /// </summary>
        public float PowerTrim = 1f, GripTrim = 1f;
        /// <summary>True once the car is out of the race: it limps to its box and parks there.</summary>
        public bool Retired => Log != null && Log.Retired;
        /// <summary>True once the car's race is over, out of it or past the chequered flag: it goes to its box and parks there, with no service.</summary>
        public bool RaceOver => Log != null && (Log.Retired || Log.Flagged);
        /// <summary>False once the team has no tyre sets left, so a stop can't change them.</summary>
        public bool TyresLeft => Log == null || Log.TyreSets > 0;

        private readonly float litresPerJoule;
        private readonly float wearPerMetre;

        public Consumables(float capacity, float fuelPerLap, float wearPerLap, float lapWork, float lapLength)
        {
            Capacity = capacity;
            FuelPerLap = fuelPerLap;
            WearPerLap = wearPerLap;
            Fuel = capacity;
            litresPerJoule = fuelPerLap / Mathf.Max(lapWork, 1f);
            wearPerMetre = wearPerLap / lapLength;
        }

        /// <summary>
        /// A car's supplies, full: the race maths sets the litres and wear per lap, and the car's
        /// speed profile round the circuit sets how much work its engine does in a lap.
        /// </summary>
        public static Consumables For(CarSpec spec, SpeedProfile lap, float smoothness)
        {
            CarStats s = spec.Stats;
            float litres = (float)(1000.0 - Degradation.UpdateFuelLevel(1000.0, s.FuelEfficiency, InstructionMode.Normal));
            float wear = (float)Degradation.UpdateTyreWear(0.0, s.TyreDurability, smoothness, InstructionMode.Normal) / 100f;
            return new Consumables((float)s.FuelCapacity, litres, wear, LapWork(spec, lap), lap.Line.Length);
        }

        /// <summary>Work the engine does in one lap of a speed profile, J.</summary>
        public static float LapWork(CarSpec spec, SpeedProfile p)
        {
            float work = 0f;
            for (int k = 0; k < p.Speed.Length; k++)
            {
                float push = p.Accel[k] + p.Resist(k, p.Speed[k]);
                if (push > 0f) work += push * spec.Mass * p.Line.Segment[k];
            }
            return work;
        }

        public bool Empty => Fuel <= 0f;
        /// <summary>Weight of the fuel on board, kg.</summary>
        public float FuelMass => Fuel * FuelDensity;

        public float PowerScale => (Map == EngineMap.Push ? 1.05f : Map == EngineMap.Conserve ? 0.92f : 1f) * (float)Effects.Power * PowerTrim;
        private float FuelScale => (Map == EngineMap.Push ? 1.15f : Map == EngineMap.Conserve ? 0.85f : 1f) / Mathf.Max((float)Effects.FuelEfficiency, 0.1f);
        /// <summary>Wear by engine map and the car's durability; a wet track runs the tyres cool and wears them less.</summary>
        private float WearScale => (Map == EngineMap.Push ? 1.3f : Map == EngineMap.Conserve ? 0.7f : 1f) / Mathf.Max((float)Effects.TyreDurability, 0.1f)
            * (1f - 0.35f * RaceWeather.Now.Wet);

        /// <summary>Litres a lap at the limit uses on the engine map in use.</summary>
        public float LitresPerLap => FuelPerLap * FuelScale;
        /// <summary>Tyre wear a lap at the limit adds on the engine map in use.</summary>
        public float WearPerLapNow => WearPerLap * WearScale;

        /// <summary>Litres a lap uses, as learned this stint, or the crew's estimate before its first clean lap.</summary>
        public float StintLitresPerLap => Stint.FuelPerLap is double f ? (float)f : LitresPerLap;
        /// <summary>Tyre wear a lap adds, as learned this stint, or the crew's estimate before its first clean lap.</summary>
        public float StintWearPerLap => Stint.WearPerLap is double w ? (float)w : WearPerLapNow;

        /// <summary>Laps the fuel lasts, at the stint's learned rate.</summary>
        public float FuelLaps => Fuel / Mathf.Max(StintLitresPerLap, 0.01f);

        /// <summary>Laps before the tyres reach <paramref name="limit"/> worn, at the stint's learned rate.</summary>
        public float TyreLaps(float limit) => Mathf.Max(limit - Wear, 0f) / Mathf.Max(StintWearPerLap, 1e-5f);

        /// <summary>The race maths' mode for the engine map.</summary>
        public InstructionMode Mode => Map == EngineMap.Push ? InstructionMode.Push : Map == EngineMap.Conserve ? InstructionMode.Conserve : InstructionMode.Normal;

        /// <summary>
        /// Share of new tyres' grip on a dry track: falls slowly, then quickly once the tyres are past
        /// three quarters worn, and falls again on a wet track (<see cref="RaceWeather"/>).
        /// </summary>
        public float GripScale
        {
            get
            {
                float past = Mathf.Max(Wear - 0.75f, 0f);
                return (1f - 0.08f * Wear - 2.4f * past * past) * (float)Effects.Handling * GripTrim * RaceWeather.Now.Grip;
            }
        }

        /// <summary>Burns fuel for the engine's output, W, over a step.</summary>
        public void Burn(float power, float dt)
        {
            if (Fuel <= 0f) return;
            Fuel = Mathf.Max(0f, Fuel - (Mathf.Max(power, 0f) * litresPerJoule * FuelScale + IdleBurn) * dt);
        }

        /// <summary>
        /// Wears the tyres over a distance, m. <c>usage</c> is how much of their grip they used,
        /// 0 to 1: cruising wears them least and sliding most.
        /// </summary>
        public void Scrub(float distance, float usage)
        {
            float u = Mathf.Clamp01(usage);
            Wear = Mathf.Min(1f, Wear + wearPerMetre * Mathf.Abs(distance) * (0.52f + 1.9f * u * u) * WearScale);
        }

        /// <summary>
        /// One step of driving without the physics car: the engine's output, W, the speed, m/s, and
        /// how hard the tyres are worked, 0 to 1.
        /// </summary>
        public void Tick(float dt, float speed, float power, float usage)
        {
            Burn(power, dt);
            Scrub(speed * dt, usage);
        }
    }
}
