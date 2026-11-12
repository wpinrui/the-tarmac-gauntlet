using System;
using System.Collections.Generic;
using TarmacGauntlet.Visuals;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Swings a car model's opening doors on their hinges. Each door's hinge axis and open angle
    /// come from the model's &lt;Name&gt;.doors.json, written by the car's export script; the door's
    /// parts are the model's children whose names start with the door's name (door_l,
    /// door_l_glass, door_l_card and so on). Drive it with Open and Shut, or set Target.
    /// </summary>
    public class CarDoors : MonoBehaviour
    {
        /// <summary>Seconds a door takes to swing fully open or shut.</summary>
        public const float SwingSeconds = 0.9f;

        [Serializable]
        private class DoorData
        {
            public string name;
            public float[] hinge0;
            public float[] hinge1;
            public float open;
        }

        [Serializable]
        private class DoorFile
        {
            public DoorData[] doors;
        }

        private class Door
        {
            public string Name;
            public Transform Pivot;
            public Vector3 Axis;
            public float OpenAngle;
        }

        private readonly List<Door> doors = new List<Door>();

        /// <summary>How far the doors are open: 0 shut, 1 fully open.</summary>
        public float Openness { get; private set; }

        /// <summary>Where the doors are swinging to: 0 shut, 1 fully open.</summary>
        public float Target { get; set; }

        /// <summary>Whether this car has a door that opens.</summary>
        public bool HasDoors => doors.Count > 0;

        public void Open() => Target = 1f;

        public void Shut() => Target = 0f;

        /// <summary>Opens or shuts the doors at once, with no swing.</summary>
        public void Snap(float openness)
        {
            Target = Openness = Mathf.Clamp01(openness);
            Pose();
        }

        /// <summary>
        /// Gives a model its doors, from the .doors.json beside its OBJ. A model with no file, or
        /// none of the named parts, gets a CarDoors with no doors.
        /// </summary>
        public static CarDoors Attach(GameObject model, string modelPath)
        {
            CarDoors carDoors = model.AddComponent<CarDoors>();
            string json = LoadJson(modelPath);
            if (string.IsNullOrEmpty(json)) return carDoors;
            DoorFile file = JsonUtility.FromJson<DoorFile>(json);
            if (file?.doors == null) return carDoors;
            foreach (DoorData d in file.doors) carDoors.Add(d);
            return carDoors;
        }

        private void Add(DoorData d)
        {
            var parts = new List<Transform>();
            foreach (Transform t in transform.GetComponentsInChildren<Transform>(true))
                if (t != transform && (t.name == d.name || t.name.StartsWith(d.name + "_"))) parts.Add(t);
            if (parts.Count == 0) return;

            Vector3 h0 = ToVector(d.hinge0), h1 = ToVector(d.hinge1);
            var pivot = new GameObject("Hinge " + d.name).transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = h0;
            pivot.gameObject.layer = gameObject.layer;
            foreach (Transform p in parts) p.SetParent(pivot, true);
            doors.Add(new Door { Name = d.name, Pivot = pivot, Axis = (h1 - h0).normalized, OpenAngle = d.open });
        }

        private void Update()
        {
            if (Mathf.Approximately(Openness, Target)) return;
            Openness = Mathf.MoveTowards(Openness, Target, Time.deltaTime / SwingSeconds);
            Pose();
        }

        private void Pose()
        {
            float eased = Openness * Openness * (3f - 2f * Openness);
            foreach (Door d in doors) d.Pivot.localRotation = Quaternion.AngleAxis(d.OpenAngle * eased, d.Axis);
        }

        private static Vector3 ToVector(float[] v) => v != null && v.Length == 3 ? new Vector3(v[0], v[1], v[2]) : Vector3.zero;

        private static string LoadJson(string modelPath) =>
            ArtLibrary.Load<TextAsset>(System.IO.Path.ChangeExtension(modelPath, ".doors.json"))?.text;
    }
}
