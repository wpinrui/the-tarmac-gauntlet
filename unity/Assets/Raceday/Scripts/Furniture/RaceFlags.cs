using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Race control for the flag panels and start lights. It only shows what is happening; nothing races by it.
    /// Behind the pace car every panel shows SC and the start lights are red. At the green the lights go green
    /// and every panel shows green for the first racing lap. A car stopped on the track brings out yellow at the
    /// post before it, which flags the stretch it is in, and green at the post after, where the yellow ends,
    /// until a little after the car gets going again or leaves the track. A car out of the race after a crash
    /// stays flagged until it is recovered.
    /// </summary>
    public sealed class RaceFlags
    {
        public enum Panel { Off, Green, Yellow, SafetyCar }
        public enum Lights { Off, Red, Green }

        /// <summary>A car is stopped below this speed, m/s, and going again above <see cref="GoingSpeed"/>.</summary>
        public const float StoppedSpeed = 8f;
        public const float GoingSpeed = 15f;
        /// <summary>How long a car must be stopped before the yellow comes out, seconds.</summary>
        public const float StoppedFor = 2f;
        /// <summary>The yellow stays out this long after the car gets going or leaves the track.</summary>
        public const float YellowHold = 6f;
        /// <summary>A car parked by the track after a crash is flagged this long, until it is recovered.</summary>
        public const float RecoveryTime = 180f;
        /// <summary>Every panel shows green this long after the start, about a lap of the slowest cars.</summary>
        public const float StartGreen = 200f;
        /// <summary>The start lights stay green this long after the start.</summary>
        public const float LightsGreen = 20f;

        /// <summary>One car as race control sees it.</summary>
        public struct Car
        {
            /// <summary>Lap distance from the start line.</summary>
            public float Distance;
            public float Speed;
            /// <summary>False in the pit lane, or once a car parked by the track has been recovered.</summary>
            public bool OnTrack;
            /// <summary>Out of the race and left beside the track after a crash, until it is recovered.</summary>
            public bool Parked;
        }

        public readonly Panel[] Panels;
        public Lights StartLights { get; private set; }

        private readonly float lap;
        private readonly float[] posts;
        private float sinceGreen = -1f;
        private float[] stoppedTime = new float[0];
        private readonly float[] yellowLeft;

        /// <param name="postDistances">Lap distance of each post, in lap order. Each flags the track up to the next.</param>
        public RaceFlags(float lapLength, IReadOnlyList<float> postDistances)
        {
            lap = lapLength;
            posts = new float[postDistances.Count];
            for (int i = 0; i < posts.Length; i++) posts[i] = postDistances[i];
            Panels = new Panel[posts.Length];
            yellowLeft = new float[posts.Length];
        }

        /// <summary>The post flagging the track at a lap distance: the last post at or before it.</summary>
        public int PostFor(float d)
        {
            int best = posts.Length - 1;
            float ahead = float.MaxValue;
            for (int i = 0; i < posts.Length; i++)
            {
                float a = Mathf.Repeat(d - posts[i], lap);
                if (a < ahead) (ahead, best) = (a, i);
            }
            return best;
        }

        /// <param name="paceCarOut">True while the field runs behind the pace car before the start.</param>
        /// <param name="racing">True once the race has started.</param>
        public void Tick(float dt, bool paceCarOut, bool racing, IReadOnlyList<Car> cars)
        {
            if (stoppedTime.Length != cars.Count) stoppedTime = new float[cars.Count];
            if (racing) sinceGreen = sinceGreen < 0f ? 0f : sinceGreen + dt;

            for (int p = 0; p < yellowLeft.Length; p++) yellowLeft[p] = Mathf.Max(0f, yellowLeft[p] - dt);
            for (int i = 0; i < cars.Count; i++)
            {
                Car car = cars[i];
                if (!racing || !car.OnTrack)
                {
                    stoppedTime[i] = 0f;
                    continue;
                }
                if (car.Speed < StoppedSpeed) stoppedTime[i] += dt;
                else if (car.Speed > GoingSpeed) stoppedTime[i] = 0f;
                bool recovered = car.Parked && stoppedTime[i] > StoppedFor + RecoveryTime;
                if (stoppedTime[i] >= StoppedFor && !recovered) yellowLeft[PostFor(car.Distance)] = YellowHold;
            }

            bool startLap = racing && sinceGreen < StartGreen;
            for (int p = 0; p < Panels.Length; p++)
            {
                int before = (p + Panels.Length - 1) % Panels.Length;
                if (paceCarOut && !racing) Panels[p] = Panel.SafetyCar;
                else if (yellowLeft[p] > 0f) Panels[p] = Panel.Yellow;
                else if (startLap || yellowLeft[before] > 0f) Panels[p] = Panel.Green;
                else Panels[p] = Panel.Off;
            }

            StartLights = racing ? (sinceGreen < LightsGreen ? Lights.Green : Lights.Off) : paceCarOut ? Lights.Red : Lights.Off;
        }
    }
}
