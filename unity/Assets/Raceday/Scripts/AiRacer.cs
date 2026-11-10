using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// An AI car's whole race, with no Unity objects so it can be tested outside Unity: it holds its
    /// place in the formation behind the pace car, races with <see cref="Racecraft"/> after the green,
    /// and takes its pit stops through <see cref="PitPlan"/>. Each runs its own <see cref="AiPilot"/>,
    /// and the racer hands the car from one to the next where their lines meet.
    /// Far from the camera it can glide instead (<see cref="Glide"/>): racing or in the formation, with
    /// no physics. A pit stop is always driven in physics.
    /// </summary>
    public sealed class AiRacer
    {
        /// <summary>The racing pilot.</summary>
        public AiPilot Pilot { get; private set; }
        /// <summary>The pilot driving the car now: racing, formation or pit route.</summary>
        public AiPilot Active { get; private set; }
        public readonly Racecraft Craft;
        public readonly Consumables Supplies;
        /// <summary>The car's pit stops, or null with no pit lane.</summary>
        public readonly PitPlan Pit;
        public readonly int Seed;

        /// <summary>The pilot for the car's formation column, set while it is in the formation.</summary>
        public AiPilot Formation;
        public bool InFormation;
        /// <summary>Fastest the car may go in the formation, m/s.</summary>
        public float FormationCap = float.PositiveInfinity;

        /// <summary>True while the car must stand still (in its box).</summary>
        public bool Hold { get; private set; }
        public bool Pitting => Pit != null && Pit.State != PitPlan.Stage.Racing;

        public AiRacer(AiPilot pilot, Racecraft craft, Consumables supplies, PitPlan pit, int seed)
        {
            Pilot = pilot;
            Active = pilot;
            Craft = craft;
            Supplies = supplies;
            Pit = pit;
            Seed = seed;
        }

        /// <summary>Swaps in a new racing pilot, such as one on the line tuned for this car.</summary>
        public void UsePilot(AiPilot pilot)
        {
            pilot.Offset = Pilot.Offset;
            if (Active == Pilot)
            {
                if (Pilot.Gliding)
                {
                    // Glide on along the new line from where the car is.
                    Pilot.GlidePose(0f, out Vector3 at, out _, out _);
                    pilot.StartGlide(at, Pilot.GlideSpeed);
                    Pilot.StopGlide();
                }
                else if (Pilot.Index >= 0) pilot.Relocate(Pilot.Index);
                Active = pilot;
            }
            Pilot = pilot;
        }

        /// <summary>True when the car may glide: racing or in the formation, not making a pit stop.</summary>
        public bool CanGlide => !Pitting && Active.Index >= 0 && (Active == Pilot || Active == Formation);

        /// <summary>
        /// One step of gliding, in place of <see cref="Step"/>: the same racecraft or formation limit,
        /// then the active pilot moves the car along its path and the car burns fuel and wears its
        /// tyres for the driving it does. Returns false when the car needs its physics back because
        /// its pit stop is due: the stop is booked, and the next <see cref="Step"/> takes the pit route.
        /// </summary>
        public bool Glide(TrafficCar me, IReadOnlyList<TrafficCar> field, bool racing, CarSpec spec, float dt)
        {
            Hold = false;
            AiPilot want = InFormation && Formation != null ? Formation : Pilot;
            if (want != Active)
            {
                Active.GlidePose(0f, out Vector3 at, out _, out _);
                float speed = Active.GlideSpeed;
                Active.StopGlide();
                HandOver(want);
                want.StartGlide(at, speed);
            }
            if (racing && Pit != null && Active == Pilot && Supplies != null && Pit.Due(Pilot.Index, Supplies))
            {
                Pit.Box(Pilot, Seed);
                return false;
            }

            if (Active == Formation)
            {
                Active.SpeedScale = 1f;
                Active.SpeedCap = FormationCap;
            }
            else Craft.Think(Pilot, me, field, dt);
            Active.Grip = Supplies != null ? Supplies.GripScale : 1f;
            float before = Active.GlideSpeed;
            int k = Active.Index;
            Active.Glide(dt);
            if (Supplies != null) Use(spec, k, before, Active.GlideSpeed, dt);
            return true;
        }

        /// <summary>Fuel for the engine's work and tyre wear for the grip used, over a glide step.</summary>
        private void Use(CarSpec spec, int k, float from, float to, float dt)
        {
            const float G = 9.81f;
            float v = 0.5f * (from + to);
            float along = (to - from) / Mathf.Max(dt, 1e-3f);
            float resist = Active.Profile.Resist(k, v);
            float power = spec.Mass * (along + resist) * v;
            float sideways = v * v * Mathf.Abs(Active.GlideCurvature);
            float limit = spec.Grip * Supplies.GripScale * (G + 0.5f * CarSpec.AirDensity * spec.LiftArea * v * v / spec.Mass);
            float usage = Mathf.Sqrt(along * along + sideways * sideways) / Mathf.Max(limit, 0.1f);
            Supplies.Tick(dt, v, power, usage);
        }

        /// <summary>The car was moved back onto the track: drop any stop and find the car again.</summary>
        public void Lost()
        {
            Pit?.Abort();
            if (!InFormation) Active = Pilot;
            Active.Lost();
            Active.Offset = 0f;
        }

        /// <summary>
        /// One physics step: works out what the car should do and returns its pedals and steering.
        /// <c>cars</c> is every car's position, for the pit lane. Pit stops start only when <c>racing</c>.
        /// </summary>
        public AiPilot.Controls Step(Vector3 position, Vector3 forward, Vector3 velocity, float yawRate, TrafficCar me,
            IReadOnlyList<TrafficCar> field, IReadOnlyList<CarState> cars, bool racing, CarSpec spec, float dt)
        {
            Hold = false;
            AiPilot want = InFormation && Formation != null ? Formation : Pitting ? Pit.Route : Pilot;
            if (want == Pilot && racing && Pit != null && Active == Pilot && Pilot.Index >= 0 && Supplies != null && Pit.Due(Pilot.Index, Supplies))
            {
                Pit.Box(Pilot, Seed);
                want = Pit.Route;
            }
            if (want != Active) HandOver(want);

            if (Active == Formation)
            {
                Active.SpeedScale = 1f;
                Active.SpeedCap = FormationCap;
            }
            else if (Active == Pilot)
            {
                if (Pilot.Index >= 0) Craft.Think(Pilot, me, field, dt);
            }
            else Hold = Pit.Step(position, velocity, Supplies, spec, me.Id, cars, dt);

            Active.Grip = Supplies != null ? Supplies.GripScale : 1f;
            AiPilot.Controls c = Active.Drive(position, forward, velocity, yawRate);
            if (Hold)
            {
                c.Throttle = 0f;
                c.Brake = 1f;
            }
            // Back from the pits: the race pilot takes over on the next step.
            if (Active != Pilot && !InFormation && !Pitting) HandOver(Pilot);
            return c;
        }

        /// <summary>
        /// Gives the car to another pilot where it is now: every line has a sample per track sample, so
        /// the new one starts its search from the old one's, and a racing pilot takes over the car's
        /// offset from its line so it doesn't swerve.
        /// </summary>
        private void HandOver(AiPilot to)
        {
            int k = Active.Index;
            float lateral = k >= 0 ? Active.Line.Lateral[k] + Active.Shift : 0f;
            to.Lost();
            if (k >= 0) to.Relocate(k);
            to.Offset = to == Pilot && k >= 0 ? Mathf.Clamp(lateral - Pilot.Line.Lateral[k], AiPilot.LaneShifts[0], AiPilot.LaneShifts[AiPilot.LaneShifts.Length - 1]) : 0f;
            Active = to;
        }
    }
}
