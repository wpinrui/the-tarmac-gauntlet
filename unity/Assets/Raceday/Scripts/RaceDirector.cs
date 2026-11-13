using System;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Keeps the race for the career (<see cref="RaceDay"/>). Each time a car crosses the line it
    /// logs the lap, which ages the car and may give it an issue or retire it: issues cut the car's
    /// power, grip, fuel efficiency and tyre life until a stop fixes them, and a retired car limps to
    /// its box, or stops beside the track after a fatal crash (<see cref="Retire"/>). After a stop's driver change, the new driver drives at their own pace.
    /// The driver at the wheel tires lap by lap (<see cref="CarRaceLog.Fatigue"/>), which slows the car until a stop puts in someone fresh. When the
    /// race clock runs out the leader takes the chequered flag the next time it crosses the line, and every car
    /// after it as each crosses the line (<see cref="RaceDay.CompleteLap"/>). Past the flag a car drives a
    /// cooldown lap at <see cref="CooldownSpeed"/>, off the racing line, and parks in its box. Once every car
    /// is parked or out of the race, or <see cref="FlagWait"/> and <see cref="CooldownWait"/> after the time ran out,
    /// the race is settled into the career (<see cref="RaceWeekend.Finish"/>) and the player's car is parked in its
    /// box for the race summary. Once the player's race is over the cooldown can be skipped
    /// (<see cref="RaceWeekend.SkipToEnd"/>): the race is settled there and then.
    /// </summary>
    public sealed class RaceDirector : MonoBehaviour
    {
        /// <summary>The race is called this long after the time runs out, s, for cars that never come round.</summary>
        public const float FlagWait = 400f;
        /// <summary>How long the cooldown laps may take once <see cref="FlagWait"/> is up, s, for cars that never park.</summary>
        public const float CooldownWait = 700f;
        /// <summary>Share of its racing speed a car drives its cooldown lap at.</summary>
        public const float CooldownSpeed = 0.6f;
        /// <summary>Share of its power a car limping back to the pits has.</summary>
        public const float LimpPower = 0.6f;
        /// <summary>Share of its racing speed a car limping back to the pits drives at.</summary>
        public const float LimpSpeed = 0.45f;
        /// <summary>How far beyond the track edge a car that crashed out comes to rest, m.</summary>
        public const float OffTrack = 4f;
        /// <summary>How far across the pit lane the summary's camera stands from the player's box, m.</summary>
        public const float SummaryDistance = 11f;

        /// <summary>The cars and their drivers, in the same order as the race day's.</summary>
        public RaceDay Day;
        public RaceCar[] Cars;
        public AiDriver[] Drivers;
        public RaceTower Tower;

        public bool Finished { get; private set; }
        /// <summary>True once the race is under way: from the green.</summary>
        public bool Started => started;
        /// <summary>Seconds since the green.</summary>
        public float Elapsed => started ? Time.time - startTime : 0f;
        /// <summary>Seconds since the race time ran out, or null while it hasn't.</summary>
        public float? SinceTimeUp => timeUpAt >= 0f ? Time.time - timeUpAt : (float?)null;
        /// <summary>Seconds since car <paramref name="i"/> last crossed the line.</summary>
        public float SinceLine(int i) => started && crossed != null ? Time.time - crossed[i] : 0f;

        private CarController[] controllers;
        private int[] counted;
        private int[] driverShown;
        private float[] crossed;
        private bool started;
        private float startTime;
        private float timeUpAt = -1f;
        private RaceSave resume;

        /// <summary>Carries on a saved race: its clocks pick up where the save left them.</summary>
        public void Resume(RaceSave save) => resume = save;

        private void OnEnable()
        {
            RaceWeekend.Withdrawing += Withdraw;
            RaceWeekend.SkippingToEnd += SkipToEnd;
        }

        private void OnDisable()
        {
            RaceWeekend.Withdrawing -= Withdraw;
            RaceWeekend.SkippingToEnd -= SkipToEnd;
            RaceWeekend.CanSkipToEnd = false;
        }

        /// <summary>The player's car out of the race now, and the race settled from where every car is.</summary>
        private void Withdraw()
        {
            if (Finished || Day == null) return;
            for (int i = 0; i < Day.Cars.Count; i++)
                if (Day.Cars[i].Car.Player && !Day.Cars[i].Flagged) RetireNow(i, FailureType.Withdrawn);
            Finish();
        }

        /// <summary>The cooldown skipped: the race settled from where every car is.</summary>
        private void SkipToEnd()
        {
            if (RaceWeekend.CanSkipToEnd) FinishNow();
        }

        /// <summary>
        /// True once the time is up and the player's race is over: their car has taken the flag or is out.
        /// With no car of the player's in the race, once the first car has taken the flag.
        /// </summary>
        private bool PlayerDone()
        {
            if (timeUpAt < 0f) return false;
            bool any = false;
            foreach (CarRaceLog log in Day.Cars)
            {
                if (log.Car.Player) return log.Flagged || log.Retired;
                any |= log.Flagged;
            }
            return any;
        }

        /// <summary>A car past the flag: its cooldown lap, slow and off the line, then into its box (<see cref="Consumables.RaceOver"/>).</summary>
        private void TakeFlag(int i)
        {
            AiPilot pilot = Drivers[i] != null ? Drivers[i].Pilot : null;
            if (pilot != null && pilot.Limp <= 0f) pilot.Limp = CooldownSpeed;
        }

        /// <summary>True once car i is parked for good: held in the pit lane, or taken off the track.</summary>
        private bool Parked(int i) => Cars[i] == null || !Cars[i].enabled || (Cars[i].InPitLane && controllers[i].Hold);

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
                startTime = Time.time - (resume?.Elapsed ?? 0f);
                if (resume?.SinceTimeUp is float since) timeUpAt = Time.time - since;
                for (int i = 0; i < Cars.Length; i++)
                {
                    counted[i] = Cars[i].Laps;
                    crossed[i] = Time.time - (resume != null && i < resume.Cars.Count ? resume.Cars[i].SinceLine : 0f);
                    if (Day.Cars[i].Flagged) TakeFlag(i);
                }
                resume = null;
            }
            bool timeUp = Tower.RaceTime >= RaceTower.RaceLength;
            if (timeUp && timeUpAt < 0f) timeUpAt = Time.time;

            for (int i = 0; i < Cars.Length; i++)
            {
                CarRaceLog log = Day.Cars[i];
                if (Cars[i] == null) continue;
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
                        Retire(i);
                        continue;
                    }
                    if (log.Flagged)
                    {
                        TakeFlag(i);
                        continue;
                    }
                }
                if (log.DriverIndex != driverShown[i])
                {
                    driverShown[i] = log.DriverIndex;
                    DriverStats d = log.Driver.Stats;
                    Drivers[i].SetDriver((float)d.Pace, (float)d.Consistency, (float)d.Safety);
                }
                if (Drivers[i].Fatigue != (float)log.Fatigue) Drivers[i].Fatigue = (float)log.Fatigue;
            }

            RaceWeekend.CanSkipToEnd = PlayerDone();
            if (AllParked() || (timeUpAt >= 0f && Time.time - timeUpAt > FlagWait + CooldownWait)) Finish();
        }

        /// <summary>
        /// True once every car still in the race has taken the flag, or been given up on after <see cref="FlagWait"/>:
        /// the results stand, whatever is left of the cooldown laps.
        /// </summary>
        public bool FieldFlagged
        {
            get
            {
                if (Day == null || timeUpAt < 0f) return false;
                if (Time.time - timeUpAt > FlagWait) return true;
                foreach (CarRaceLog log in Day.Cars)
                    if (!log.Retired && !log.Flagged) return false;
                return true;
            }
        }

        /// <summary>True once every car still in the race has taken the flag and parked in its box.</summary>
        private bool AllParked()
        {
            if (timeUpAt < 0f) return false;
            for (int i = 0; i < Cars.Length; i++)
            {
                CarRaceLog log = Day.Cars[i];
                if (log.Retired) continue;
                // A car that hasn't come round in FlagWait isn't waited on.
                if (!log.Flagged)
                {
                    if (Time.time - timeUpAt > FlagWait) continue;
                    return false;
                }
                if (!Parked(i)) return false;
            }
            return true;
        }

        /// <summary>
        /// A car out of the race. A fatal crash leaves it stopped on the grass beside the track, out
        /// of the race. Any other failure cuts its power, and it limps to its box at the next pit
        /// entry, keeping its traffic manners, and parks there.
        /// </summary>
        /// <summary>Takes a car out of the race now. For the debug menu.</summary>
        public void RetireNow(int i, FailureType reason)
        {
            if (Finished || Day.Cars[i].Retired) return;
            Day.Cars[i].Retire(Tower.RaceTime, reason);
            Retire(i);
        }

        /// <summary>Ends the race now and settles it, as if every car had taken the flag. For the debug menu.</summary>
        public void FinishNow()
        {
            if (!Finished && Day != null) Finish();
        }

        private void Retire(int i)
        {
            CarController car = controllers[i];
            AiDriver driver = Drivers[i];
            if (Day.Cars[i].RetirementReason == FailureType.Crash)
            {
                Circuit c = car.Circuit;
                int k = c.IndexOf(Mathf.Repeat(Cars[i].Traffic.Distance, c.Lap));
                float side = Cars[i].Traffic.Lateral >= 0f ? 1f : -1f;
                Vector3 ground = c.Surface(k, side * (c.HalfWidth(k) + OffTrack));
                ground.y = c.TerrainHeight(ground.x, ground.z);
                driver.enabled = false;
                Cars[i].enabled = false;   // out of every other car's traffic
                car.PlaceAt(ground, Quaternion.AngleAxis(side * 25f, Vector3.up) * c.Forward[k]);
                car.Hold = true;
                car.InputEnabled = false;
                return;
            }
            car.Supplies.PowerTrim *= LimpPower;
            driver.Limping = true;
            if (driver.Pilot != null) driver.Pilot.Limp = LimpSpeed;
        }

        private void Finish()
        {
            Finished = true;
            RaceWeekend.CanSkipToEnd = false;
            try
            {
                RaceWeekend.Finish(Day);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }
            // After the flag the podium plays first, and opens the summary when it is done (ShowSummary).
            if (HoldSummary && timeUpAt >= 0f) return;
            ShowSummary();
        }

        /// <summary>
        /// True when the stages after the flag play the podium before the race summary: a race settled once its time is
        /// up waits for <see cref="ShowSummary"/>. A race settled before then, the player retiring, opens it at once.
        /// </summary>
        [NonSerialized] public bool HoldSummary;

        /// <summary>Parks the player's car for the race summary and opens it. Once only.</summary>
        public void ShowSummary()
        {
            if (!Finished || summaryShown || RaceWeekend.Summary == null) return;
            summaryShown = true;
            ParkForSummary();
            RaceWeekend.ShowSummary();
        }

        private bool summaryShown;

        /// <summary>
        /// Sets the scene for the race summary: the player's car parked in its box, seen from across the pit lane, at
        /// normal speed, with the race views, timing tower and driving HUD gone.
        /// </summary>
        /// <summary>
        /// Every car still driving put in its box and held there, out of the pit lane's traffic: the podium's crowd
        /// stands in the fast lane. A car with no box is taken off the track. Cars already parked, or stopped out on the
        /// track, stay where they are.
        /// </summary>
        public void ParkAll()
        {
            if (Cars == null || controllers == null) return;
            for (int i = 0; i < Cars.Length; i++)
            {
                if (Cars[i] == null || !Cars[i].enabled || controllers[i] == null || controllers[i].Hold) continue;
                if (Cars[i].Pits != null && Cars[i].Garage >= 0) PlaceInBox(i);
                else Cars[i].gameObject.SetActive(false);
            }
        }

        /// <summary>Puts car i in its box, held, its driver off: the box, the way into the garage, and the way down the lane.</summary>
        private (Vector3 box, Vector3 toGarage, Vector3 along) PlaceInBox(int i)
        {
            PitLane pits = Cars[i].Pits;
            int garage = Cars[i].Garage;
            Vector3 box = pits.BoxPoint(garage);
            Vector3 toGarage = pits.LaneLeft(pits.BoxLane[garage]);
            var along = new Vector3(toGarage.z, 0f, -toGarage.x);   // down the lane, the way cars drive it
            if (Drivers[i] != null) Drivers[i].enabled = false;
            Cars[i].enabled = false;   // out of every other car's traffic
            CarController car = controllers[i];
            car.PlaceAt(box, along);
            car.Hold = true;
            car.InputEnabled = false;
            return (box, toGarage, along);
        }

        private void ParkForSummary()
        {
            Time.timeScale = 1f;
            if (Tower != null) Tower.gameObject.SetActive(false);
            Camera cam = Camera.main;
            if (cam != null)
            {
                foreach (Behaviour b in new Behaviour[] { cam.GetComponent<RaceCameras>(), cam.GetComponent<DriveHud>(), cam.GetComponent<FpsCounter>() })
                    if (b != null) b.enabled = false;
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            int me = -1;
            for (int i = 0; i < Day.Cars.Count; i++) if (Day.Cars[i].Car.Player) me = i;
            if (me < 0 || Cars[me] == null || Cars[me].Pits == null || Cars[me].Garage < 0) return;

            (Vector3 box, Vector3 toGarage, Vector3 along) = PlaceInBox(me);

            if (cam == null) return;
            cam.fieldOfView = 34f;
            Vector3 eye = box - toGarage * SummaryDistance + Vector3.up * 2.4f + along * 2f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(box + Vector3.up * 0.7f - along * 0.4f - eye, Vector3.up));
        }
    }
}
