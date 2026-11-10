using System;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Keeps the race for the career (<see cref="RaceDay"/>). Each time a car crosses the line it
    /// logs the lap, which ages the car and may give it an issue or retire it: issues cut the car's
    /// power, grip, fuel efficiency and tyre life until a stop fixes them, and a retired car leaves
    /// the track. After a stop's driver change, the new driver drives at their own pace. When the
    /// race clock runs out each car takes the flag the next time it crosses the line, and once all
    /// have, or <see cref="FlagWait"/> after the time ran out, the race is settled into the career
    /// (<see cref="RaceWeekend.Finish"/>).
    /// </summary>
    public sealed class RaceDirector : MonoBehaviour
    {
        /// <summary>The race is called this long after the time runs out, s, for cars that never come round.</summary>
        public const float FlagWait = 400f;

        /// <summary>The cars and their drivers, in the same order as the race day's.</summary>
        public RaceDay Day;
        public RaceCar[] Cars;
        public AiDriver[] Drivers;
        public RaceTower Tower;

        public bool Finished { get; private set; }

        private CarController[] controllers;
        private int[] counted;
        private int[] driverShown;
        private float[] crossed;
        private bool started;
        private float startTime;
        private float timeUpAt = -1f;

        private void Start()
        {
            controllers = new CarController[Cars.Length];
            counted = new int[Cars.Length];
            driverShown = new int[Cars.Length];
            crossed = new float[Cars.Length];
            for (int i = 0; i < Cars.Length; i++) controllers[i] = Cars[i].GetComponent<CarController>();
        }

        private void Update()
        {
            if (Finished || Day == null || controllers == null) return;
            if (Tower.Racing != null && !Tower.Racing()) return;
            if (!started)
            {
                started = true;
                startTime = Time.time;
                for (int i = 0; i < Cars.Length; i++)
                {
                    counted[i] = Cars[i].Laps;
                    crossed[i] = Time.time;
                }
            }
            bool timeUp = Tower.RaceTime >= RaceTower.RaceLength;
            if (timeUp && timeUpAt < 0f) timeUpAt = Time.time;

            for (int i = 0; i < Cars.Length; i++)
            {
                CarRaceLog log = Day.Cars[i];
                if (Cars[i] == null) continue;
                // A retired car stays off the track, even after the debug menu shows the race again.
                if (log.Retired && Cars[i].gameObject.activeSelf) Cars[i].gameObject.SetActive(false);
                if (log.Retired || log.Flagged) continue;
                Consumables supplies = controllers[i].Supplies;
                log.FuelLeft = supplies.Fuel;
                // Until the first lap is in, follow the count down too: the formation lap comes off it at the green.
                if (log.Laps == 0 && Cars[i].Laps < counted[i]) counted[i] = Cars[i].Laps;
                if (Cars[i].Laps > counted[i])
                {
                    counted[i] = Cars[i].Laps;
                    float lap = Time.time - crossed[i];
                    crossed[i] = Time.time;
                    Day.CompleteLap(i, Time.time - startTime, lap, supplies.Mode, timeUp);
                    supplies.Effects = log.Effects;
                    if (log.Retired)
                    {
                        Cars[i].gameObject.SetActive(false);
                        continue;
                    }
                }
                if (log.DriverIndex != driverShown[i])
                {
                    driverShown[i] = log.DriverIndex;
                    DriverStats d = log.Driver.Stats;
                    Drivers[i].SetDriver((float)d.Pace, (float)d.Consistency, (float)d.Safety);
                }
            }

            if (Day.AllFlagged || (timeUpAt >= 0f && Time.time - timeUpAt > FlagWait)) Finish();
        }

        private void Finish()
        {
            Finished = true;
            try
            {
                RaceWeekend.Finish(Day);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }
            RaceSummary s = RaceWeekend.Summary;
            if (s?.Player != null) Debug.Log($"Race over: P{s.Player.Position} after {s.Player.Laps} laps, {s.PitStops.Count} stops, {s.Issues.Count} issues");
        }
    }
}
