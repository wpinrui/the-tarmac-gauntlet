using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// A stage after the flag: one podium ceremony (<see cref="Podium"/>). The overall podium plays first, then the
    /// player's class podium, skipped when its top three are the overall top three, or with no car of the player's.
    /// A stage starts once the race is settled (<see cref="RaceDirector.Finished"/>), takes the camera and hides the
    /// timing tower and the driving HUD while it plays. The last podium opens the race summary when it ends or is
    /// skipped (<see cref="RaceDirector.ShowSummary"/>).
    /// </summary>
    public sealed class PodiumStage : SkippableStage
    {
        public Podium Podium;
        public RaceDirector Director;
        public RaceCameras Cameras;
        public RaceTower Tower;
        /// <summary>The team colour of each car in the field, by car.</summary>
        public Color[] Colours;
        /// <summary>The class podium rather than the overall one.</summary>
        public bool ClassPodium;
        /// <summary>The last podium stage: it opens the race summary.</summary>
        public bool Last;

        private bool begun, started, done;

        public override string Name => ClassPodium ? "class podium" : "overall podium";

        public override bool Done => done;

        /// <summary>Only once the ceremony has started: until the race is settled there is nothing to skip.</summary>
        public override bool Skippable => started;

        public override void Begin() => begun = true;

        public override void Skip() => End();

        private void Update()
        {
            if (!begun || done) return;
            if (!started)
            {
                if (Director == null || !Director.Finished) return;
                Open();
                return;
            }
            if (!Podium.Advance(Time.unscaledDeltaTime)) End();
        }

        /// <summary>The ceremony's three places from the settled race, or none.</summary>
        private void Open()
        {
            started = true;
            RaceSummary summary = RaceWeekend.Summary;
            int[] cars = summary != null && Podium != null ? Places(summary) : null;
            if (cars == null)
            {
                End();
                return;
            }
            var drivers = new int[3];
            var colours = new Color[3];
            for (int p = 0; p < 3; p++)
            {
                if (cars[p] < 0) continue;
                drivers[p] = Mathf.Clamp(Director.Day.Cars[cars[p]].Car.Lineup.Count, 1, 3);
                colours[p] = Colours != null && cars[p] < Colours.Length ? Colours[cars[p]] : Color.grey;
            }
            Time.timeScale = 1f;
            // The crews stand in the fast lane: every car still on its cool-down lap is put in its box.
            Director.ParkAll();
            Podium.Play(new PodiumShow(drivers, Podium.CrowdSpots.ToList()), colours);
            if (Cameras != null) Cameras.Held = true;
            if (Tower != null) Tower.Hidden = true;
            if (Cameras != null && Cameras.Hud != null) Cameras.Hud.Hidden = true;
        }

        /// <summary>
        /// The cars in this ceremony's places by index into the field, winner first, -1 for an empty place; null when
        /// there is no ceremony to play.
        /// </summary>
        private int[] Places(RaceSummary summary)
        {
            List<ClassifiedCar> overall = summary.Classification.Where(c => !c.Retired).Take(3).ToList();
            List<ClassifiedCar> places = overall;
            if (ClassPodium)
            {
                if (summary.Player == null) return null;
                places = summary.Classification.Where(c => !c.Retired && c.CarClass == summary.Player.CarClass).Take(3).ToList();
                if (places.Select(c => c.CarId).SequenceEqual(overall.Select(c => c.CarId))) return null;
            }
            if (places.Count == 0) return null;
            var cars = new[] { -1, -1, -1 };
            for (int p = 0; p < places.Count; p++)
                for (int i = 0; i < Director.Day.Cars.Count; i++)
                    if (Director.Day.Cars[i].Car.Car.Id == places[p].CarId) cars[p] = i;
            return cars[0] < 0 ? null : cars;
        }

        private void LateUpdate()
        {
            if (!started || done || Podium == null || !Podium.Playing) return;
            Camera cam = Cameras != null ? Cameras.GetComponent<Camera>() : Camera.main;
            if (cam == null) return;
            (Vector3 eye, Vector3 look, float fov) = Podium.Shot();
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
            cam.fieldOfView = fov;
            if (Cameras != null && Cameras.Hud != null) Cameras.Hud.Hidden = true;
        }

        /// <summary>Ends the ceremony and gives the camera back; the last one opens the race summary.</summary>
        private void End()
        {
            if (done) return;
            done = true;
            if (Podium != null) Podium.Stop();
            // Between the overall and the class podium the camera stays the podium's: no frame of the race shows.
            if (Cameras != null && Last) Cameras.Held = false;
            if (Last && Director != null)
            {
                if (!Director.Finished) Director.FinishNow();
                Director.ShowSummary();
            }
            enabled = false;
        }
    }
}
