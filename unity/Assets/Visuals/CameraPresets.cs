using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Visuals
{
    /// <summary>
    /// One car camera's place: field of view, position in the car's frame and tilt. Each value is saved at once and
    /// remembered between runs; the race reads them every frame, so a change shows live.
    /// </summary>
    public sealed class CameraPreset
    {
        public readonly string Name;
        private readonly string key;
        private readonly float[] defaults;
        /// <summary>The range of each value, in the order of <see cref="CameraPresets.Fields"/>.</summary>
        public readonly Vector2[] Ranges;
        private float[] values;

        internal CameraPreset(string name, string key, float fov, Vector3 at, float tilt, Vector2 x, Vector2 y, Vector2 z)
        {
            Name = name;
            this.key = key;
            defaults = new[] { fov, at.x, at.y, at.z, tilt };
            Ranges = new[] { new Vector2(20f, 110f), x, y, z, new Vector2(-45f, 45f) };
        }

        /// <summary>Read from the saved keys on first use, not in a static constructor where Unity forbids it.</summary>
        private float[] Values
        {
            get
            {
                if (values != null) return values;
                values = new float[defaults.Length];
                for (int i = 0; i < values.Length; i++) values[i] = PlayerPrefs.GetFloat(Key(i), defaults[i]);
                return values;
            }
        }

        private string Key(int i) => key + "." + CameraPresets.Keys[i];

        public float this[int i]
        {
            get => Values[i];
            set
            {
                Values[i] = Mathf.Clamp(value, Ranges[i].x, Ranges[i].y);
                PlayerPrefs.SetFloat(Key(i), Values[i]);
                PlayerPrefs.Save();
            }
        }

        public float Fov => Values[0];
        public Vector3 Position => new Vector3(Values[1], Values[2], Values[3]);
        /// <summary>Degrees, up positive.</summary>
        public float Tilt => Values[4];

        public void Reset()
        {
            for (int i = 0; i < Values.Length; i++)
            {
                Values[i] = defaults[i];
                PlayerPrefs.DeleteKey(Key(i));
            }
            PlayerPrefs.Save();
        }
    }

    /// <summary>The car cameras that hold one place on the car, for the settings menu to tune.</summary>
    public static class CameraPresets
    {
        public static readonly string[] Fields = { "Field of view", "X", "Y", "Z", "Tilt" };
        internal static readonly string[] Keys = { "fov", "x", "y", "z", "tilt" };

        /// <summary>Behind the car on its heading, looking just ahead of it.</summary>
        public static readonly CameraPreset Chase = new CameraPreset("Chase", "settings.camera.chase", 60f, new Vector3(0f, 2.2f, -7f), -6.7f,
            new Vector2(-5f, 5f), new Vector2(0f, 10f), new Vector2(-20f, 5f));
        /// <summary>Moved from each car's own bonnet spot, measured from its model, so one setting suits every car.</summary>
        public static readonly CameraPreset Bonnet = new CameraPreset("Bonnet", "settings.camera.bonnetspot", 70f, Vector3.zero, -3f,
            new Vector2(-1f, 1f), new Vector2(-1f, 1f), new Vector2(-2f, 2f));
        public static readonly CameraPreset Bumper = new CameraPreset("Bumper", "settings.camera.bumper", 70f, new Vector3(0f, 0.4f, 2.45f), 0f,
            new Vector2(-2f, 2f), new Vector2(0f, 3f), new Vector2(-3f, 4f));

        public static readonly IReadOnlyList<CameraPreset> All = new[] { Chase, Bonnet, Bumper };

        /// <summary>The preset being tuned in the race's camera settings: the race camera shows exactly it while set.</summary>
        public static CameraPreset Preview;

        /// <summary>The preset the race camera is on, or null on the TV camera or the broadcast.</summary>
        public static CameraPreset InUse;
    }
}
