using System;
using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.TeamHq
{
    /// <summary>
    /// The menu camera. Each menu screen has a shot, set by Ivan in the Team HQ viewer and exported to
    /// Resources/CameraPresets.json; switching screens glides to the new shot in half a second.
    /// </summary>
    public sealed class HqCamera : MonoBehaviour
    {
        public const float GlideSeconds = 0.5f;

        /// <summary>Menu screens in the viewer's order.</summary>
        public static readonly string[] Screens =
        {
            "main-menu", "hub", "car-workshop", "new-car-dealer", "second-hand-dealer", "crew",
            "drivers", "finances", "newspaper", "race-history", "standings", "scouting-report",
        };

        [Serializable]
        private sealed class Preset { public string id; public float[] pos; public float[] target; public float fov; }

        [Serializable]
        private sealed class PresetFile { public Preset[] presets; }

        private struct Shot { public Vector3 Position; public Quaternion Rotation; public float Fov; }

        private readonly Dictionary<string, Shot> shots = new Dictionary<string, Shot>();
        private Camera cam;
        private Shot from, to;
        private float glideStart = -1;

        public string Current { get; private set; }

        private void Awake()
        {
            cam = GetComponent<Camera>();
            var file = Resources.Load<TextAsset>("CameraPresets");
            PresetFile data = file != null ? JsonUtility.FromJson<PresetFile>(file.text) : null;
            if (data?.presets == null) return;
            foreach (Preset p in data.presets)
            {
                // The viewer's frame is right-handed with +Z towards the bay doors; Unity's +Z points away from them.
                var pos = new Vector3(p.pos[0], p.pos[1], -p.pos[2]);
                var target = new Vector3(p.target[0], p.target[1], -p.target[2]);
                shots[p.id] = new Shot { Position = pos, Rotation = Quaternion.LookRotation(target - pos, Vector3.up), Fov = p.fov };
            }
        }

        /// <summary>Cuts to a screen's shot at once.</summary>
        public void Cut(string screen)
        {
            if (!shots.TryGetValue(screen, out Shot s)) return;
            Current = screen;
            glideStart = -1;
            Apply(s);
        }

        /// <summary>Glides to a screen's shot.</summary>
        public void Show(string screen)
        {
            if (!shots.TryGetValue(screen, out Shot s) || screen == Current) return;
            Current = screen;
            from = new Shot { Position = transform.position, Rotation = transform.rotation, Fov = cam.fieldOfView };
            to = s;
            glideStart = Time.unscaledTime;
        }

        private void LateUpdate()
        {
            if (glideStart < 0) return;
            float t = Mathf.Clamp01((Time.unscaledTime - glideStart) / GlideSeconds);
            float k = t < 0.5f ? 2 * t * t : 1 - Mathf.Pow(-2 * t + 2, 2) / 2;
            Apply(new Shot
            {
                Position = Vector3.Lerp(from.Position, to.Position, k),
                Rotation = Quaternion.Slerp(from.Rotation, to.Rotation, k),
                Fov = Mathf.Lerp(from.Fov, to.Fov, k),
            });
            if (t >= 1) glideStart = -1;
        }

        private void Apply(Shot s)
        {
            transform.SetPositionAndRotation(s.Position, s.Rotation);
            cam.fieldOfView = s.Fov;
        }
    }
}
