using System;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// One car in a <see cref="Situation"/>: what it is, where it starts and how fast.
    /// </summary>
    public sealed class SituationCar
    {
        public string Model;
        /// <summary>Metres ahead of the situation's spot along the track. Negative is behind.</summary>
        public float Ahead;
        /// <summary>Metres left of the track centre. NaN: see <see cref="Off"/>.</summary>
        public float Lateral = float.NaN;
        /// <summary>Metres left of the first car's racing line at this car's spot. NaN with no <see cref="Lateral"/>: on its own line.</summary>
        public float Off = float.NaN;
        /// <summary>Speed, km/h. NaN: its own racing speed at its spot.</summary>
        public float Speed = float.NaN;
        public float Pace = 90f, Consistency = 100f, Safety = 90f;
        public int Seed = 1;
        /// <summary>Laps behind the others: a lapped car.</summary>
        public int LapsDown;
        /// <summary>Tyre wear, 0 new to 1 gone.</summary>
        public float Wear;
        /// <summary>Degrees the car faces right of along the track: a spun car.</summary>
        public float Turned;
        /// <summary>The car the player takes in the debug menu when they drive.</summary>
        public bool Player;
        /// <summary>Stopped and turned across the track: it starts as a car that has just spun, a ghost getting back.</summary>
        public bool Spun => Speed == 0f && Math.Abs(Turned) >= Rejoin.SpunAngle;

        /// <summary>
        /// Where the car starts: its track sample, metres left of the centre and speed in m/s.
        /// <paramref name="first"/> is the first car's racing line, <paramref name="own"/> this car's
        /// line and <paramref name="pace"/> the speeds its driver runs on it.
        /// </summary>
        public void Spot(Situation s, Circuit c, RacingLine first, RacingLine own, SpeedProfile pace,
            out int k, out float lateral, out float speed)
        {
            k = c.IndexOf(Wrap(s.At + Ahead, c.Lap));
            lateral = !float.IsNaN(Lateral) ? Lateral : !float.IsNaN(Off) ? first.Lateral[k] + Off : own.Lateral[k];
            speed = !float.IsNaN(Speed) ? Speed / 3.6f : pace.Speed[k];
        }

        private static float Wrap(float d, float lap) => ((d % lap) + lap) % lap;
    }

    /// <summary>
    /// A short set-up that shows one rule of the AI racecraft spec at work: a few cars at a known spot
    /// and speed. The test suite runs each one and scores it, and the raceday debug menu lets Ivan
    /// place and watch it. The first car is the one the rule is about.
    /// </summary>
    public sealed class Situation
    {
        /// <summary>The spec line, e.g. "T2".</summary>
        public string Id;
        /// <summary>What it shows, as the debug menu lists it.</summary>
        public string Title;
        /// <summary>Lap distance the cars are placed from, m.</summary>
        public float At;
        /// <summary>How long it runs, race seconds.</summary>
        public float Seconds = 20f;
        public SituationCar[] Cars;
        /// <summary>What the set-up waits on before it can run, or null when it is ready.</summary>
        public string Needs;

        public bool Ready => Needs == null;
        public string Name => $"{Id} {Title}";
    }

    /// <summary>
    /// Every set-up, one per line of the spec's hard limits and behaviours. Most run on the back
    /// straight (2917 m to 3967 m) into the 40 m right-hander at its end, Turn 7. Its inside is on the
    /// right, negative lateral.
    /// </summary>
    public static class Situations
    {
        private const float T7 = 3966.8f;

        public static readonly Situation[] All =
        {
            // ------------------------------------------------------------ hard limits
            new Situation
            {
                Id = "H1", Title = "three slow cars abreast ahead", At = T7 - 300f, Seconds = 12f,
                Cars = new[]
                {
                    new SituationCar { Model = "a-29" },
                    new SituationCar { Model = "f-01", Ahead = 130f, Seed = 2 },
                    new SituationCar { Model = "f-02", Ahead = 130f, Off = -4.5f, Seed = 3 },
                    new SituationCar { Model = "f-03", Ahead = 130f, Off = -9f, Seed = 4 },
                },
            },
            new Situation
            {
                Id = "H2", Title = "car alongside on my inside", At = T7 - 180f, Seconds = 8f,
                Cars = new[]
                {
                    new SituationCar { Model = "b-25" },
                    new SituationCar { Model = "b-24", Ahead = -2f, Off = -4f, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "H3", Title = "outside car almost ahead", At = T7 - 180f, Seconds = 8f,
                Cars = new[]
                {
                    new SituationCar { Model = "b-25", Off = -4f },
                    new SituationCar { Model = "b-24", Ahead = 0.5f, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "H4", Title = "one defensive move on the straight", At = 2960f, Seconds = 15f,
                Cars = new[]
                {
                    new SituationCar { Model = "b-21" },
                    new SituationCar { Model = "b-25", Ahead = -20f, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "H5", Title = "no defending in the braking zone", At = T7 - 150f, Seconds = 8f,
                Cars = new[]
                {
                    new SituationCar { Model = "b-24", Pace = 75f },
                    new SituationCar { Model = "b-25", Ahead = -10f, Off = -3.5f, Pace = 100f, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "H6", Title = "car closing much faster", At = 3100f, Seconds = 10f,
                Cars = new[]
                {
                    new SituationCar { Model = "e-11" },
                    new SituationCar { Model = "a-29", Ahead = -80f, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "H7", Title = "no dive into the corner", At = T7 - 180f, Seconds = 8f,
                Cars = new[]
                {
                    new SituationCar { Model = "a-29", Pace = 100f, Safety = 80f },
                    new SituationCar { Model = "b-25", Ahead = 15f, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "H8", Title = "late-braking pass not overlapped", At = T7 - 120f, Seconds = 8f,
                Cars = new[]
                {
                    new SituationCar { Model = "b-25", Off = -3.5f, Pace = 100f },
                    new SituationCar { Model = "b-24", Ahead = 9f, Pace = 75f, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "H9", Title = "lapped car lets the leader by", At = 3100f, Seconds = 12f,
                Cars = new[]
                {
                    new SituationCar { Model = "e-11", LapsDown = 1 },
                    new SituationCar { Model = "a-29", Ahead = -220f, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "H10", Title = "on the outside through the corner", At = T7 - 180f, Seconds = 8f,
                Cars = new[]
                {
                    new SituationCar { Model = "a-29", Speed = 250f },
                    new SituationCar { Model = "a-28", Ahead = 1f, Off = -4f, Speed = 250f, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "H11", Title = "pit exit line", At = 1690f, Needs = "pit lane set-ups",
                Cars = new[]
                {
                    new SituationCar { Model = "e-11" },
                    new SituationCar { Model = "b-25", Ahead = -150f, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "H12", Title = "spun car on the track", At = 3500f, Seconds = 12f,
                Cars = new[]
                {
                    new SituationCar { Model = "f-01", Speed = 0f, Turned = 90f },
                    new SituationCar { Model = "a-29", Ahead = -400f, Seed = 2 },
                },
            },

            // ------------------------------------------------------------ behaviour
            new Situation
            {
                Id = "T1", Title = "slipstream a slower car", At = 2960f, Seconds = 15f,
                Cars = new[]
                {
                    new SituationCar { Model = "b-25" },
                    new SituationCar { Model = "b-21", Ahead = 30f, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "T2", Title = "pull out instead of braking", At = T7 - 650f, Seconds = 12f,
                Cars = new[]
                {
                    new SituationCar { Model = "a-29" },
                    new SituationCar { Model = "e-11", Ahead = 150f, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "T3", Title = "fastest behind slowest in the twisty part", At = 980f, Seconds = 40f,
                Cars = new[]
                {
                    new SituationCar { Model = "a-29" },
                    new SituationCar { Model = "f-01", Ahead = 40f, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "T4", Title = "back on the line, a car far up the road", At = T7 - 450f, Seconds = 8f,
                Cars = new[]
                {
                    new SituationCar { Model = "a-29", Off = -4.5f },
                    new SituationCar { Model = "e-11", Ahead = -12f, Seed = 2 },
                    new SituationCar { Model = "d-15", Ahead = 110f, Seed = 3 },
                },
            },
            new Situation
            {
                Id = "T5", Title = "squeezed on the outside", At = T7 - 150f, Seconds = 8f,
                Cars = new[]
                {
                    new SituationCar { Model = "b-25" },
                    new SituationCar { Model = "b-24", Ahead = 2f, Off = -4f, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "T6", Title = "outbrake a worse braker", At = T7 - 220f, Seconds = 8f,
                Cars = new[]
                {
                    new SituationCar { Model = "a-29", Off = -4f, Speed = 285f },
                    new SituationCar { Model = "b-25", Ahead = 6f, Speed = 285f, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "T6b", Title = "alongside a better braker", At = T7 - 220f, Seconds = 8f,
                Cars = new[]
                {
                    new SituationCar { Model = "b-25", Off = -4f, Speed = 285f },
                    new SituationCar { Model = "a-29", Ahead = 6f, Speed = 285f, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "T6c", Title = "two slow cars abreast into the corner", At = T7 - 300f, Seconds = 8f,
                Cars = new[]
                {
                    new SituationCar { Model = "a-29" },
                    new SituationCar { Model = "f-01", Ahead = 120f, Seed = 2 },
                    new SituationCar { Model = "f-02", Ahead = 120f, Off = -4.5f, Seed = 3 },
                },
            },
            new Situation
            {
                Id = "T7", Title = "lapping a slow car", At = 3000f, Seconds = 20f,
                Cars = new[]
                {
                    new SituationCar { Model = "a-29" },
                    new SituationCar { Model = "e-11", Ahead = 120f, LapsDown = 1, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "T8", Title = "move back across after a pass", At = T7 - 450f, Seconds = 10f,
                Cars = new[]
                {
                    new SituationCar { Model = "a-29", Off = -4.5f },
                    new SituationCar { Model = "d-15", Ahead = -3f, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "T9", Title = "train of slower cars", At = 2960f, Seconds = 30f,
                Cars = new[]
                {
                    new SituationCar { Model = "a-29" },
                    new SituationCar { Model = "f-01", Ahead = 50f, Seed = 2 },
                    new SituationCar { Model = "f-02", Ahead = 62f, Seed = 3 },
                    new SituationCar { Model = "f-03", Ahead = 74f, Seed = 4 },
                    new SituationCar { Model = "f-04", Ahead = 86f, Seed = 5 },
                    new SituationCar { Model = "f-05", Ahead = 98f, Seed = 6 },
                },
            },
            new Situation
            {
                Id = "T10", Title = "pit entry and exit", At = 5800f, Needs = "pit lane set-ups",
                Cars = new[] { new SituationCar { Model = "b-25" } },
            },
            new Situation
            {
                Id = "T11", Title = "lap one from the grid", At = 7341.5f, Seconds = 40f,
                Cars = Grid("a-29", "b-25", "c-19", "d-15", "e-11", "f-01", "c-18", "b-24", "e-10", "a-28"),
            },
            new Situation
            {
                Id = "T12", Title = "catching the player's car", At = T7 - 650f, Seconds = 12f,
                Cars = new[]
                {
                    new SituationCar { Model = "b-25" },
                    new SituationCar { Model = "d-15", Ahead = 120f, Seed = 2, Player = true },
                },
            },
            new Situation
            {
                Id = "T13", Title = "slow damaged car", At = 3100f, Needs = "damage",
                Cars = new[]
                {
                    new SituationCar { Model = "b-24" },
                    new SituationCar { Model = "b-25", Ahead = -150f, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "T14", Title = "a pass while gliding", At = 2960f, Needs = "far cars",
                Cars = new[]
                {
                    new SituationCar { Model = "a-29" },
                    new SituationCar { Model = "c-19", Ahead = 60f, Seed = 2 },
                },
            },
            new Situation { Id = "T15", Title = "same results at 10x", Needs = "race scoring", Cars = new SituationCar[0] },
            new Situation { Id = "T16", Title = "contacts per hour", Needs = "race scoring", Cars = new SituationCar[0] },
            new Situation
            {
                Id = "T17", Title = "rejoin from the grass", At = 3300f, Seconds = 15f,
                Cars = new[]
                {
                    new SituationCar { Model = "e-11", Lateral = 11f, Speed = 60f, Turned = -10f },
                    new SituationCar { Model = "a-29", Ahead = -200f, Seed = 2 },
                    new SituationCar { Model = "b-25", Ahead = -240f, Seed = 3 },
                },
            },
            new Situation
            {
                Id = "T18", Title = "behind an early braker", At = T7 - 350f, Seconds = 8f,
                Cars = new[]
                {
                    new SituationCar { Model = "a-29", Speed = 290f },
                    new SituationCar { Model = "b-25", Ahead = 15f, Speed = 290f, Seed = 2 },
                },
            },
            new Situation
            {
                Id = "T18b", Title = "behind an early braker, blocked", At = T7 - 350f, Seconds = 8f,
                Cars = new[]
                {
                    new SituationCar { Model = "a-29", Speed = 290f },
                    new SituationCar { Model = "b-25", Ahead = 15f, Speed = 290f, Seed = 2 },
                    new SituationCar { Model = "b-24", Ahead = 2f, Off = -4.5f, Speed = 290f, Seed = 3 },
                },
            },
            new Situation
            {
                Id = "T19", Title = "worn tyres behind a car", At = T7 - 300f, Seconds = 8f,
                Cars = new[]
                {
                    new SituationCar { Model = "b-25", Speed = 285f, Wear = 0.95f },
                    new SituationCar { Model = "b-24", Ahead = 15f, Speed = 285f, Seed = 2 },
                },
            },
        };

        /// <summary>The set-up with this id, or null.</summary>
        public static Situation Find(string id) => Array.Find(All, s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>A standing start: the cars on the painted grid slots in this order, pole first.</summary>
        private static SituationCar[] Grid(params string[] models)
        {
            var cars = new SituationCar[models.Length];
            for (int i = 0; i < models.Length; i++)
                cars[i] = new SituationCar
                {
                    Model = models[i], Ahead = -8f * i, Lateral = i % 2 == 0 ? 3.5f : -3.5f, Speed = 0f,
                    Pace = 80f + 2f * i, Seed = i + 1,
                };
            return cars;
        }
    }
}
