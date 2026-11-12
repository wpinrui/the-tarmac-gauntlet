using System.Collections.Generic;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Which keys are held, and which went down this frame. Reads IMGUI events so it works with
    /// either of Unity's input systems. While the race is paused (<see cref="RaceWeekend.Paused"/>) no key is held or pressed.
    /// </summary>
    public sealed class KeyboardState : MonoBehaviour
    {
        private readonly HashSet<KeyCode> held = new HashSet<KeyCode>();
        private readonly HashSet<KeyCode> downNow = new HashSet<KeyCode>();
        private readonly HashSet<KeyCode> downLast = new HashSet<KeyCode>();
        private int frame = -1;

        public bool Held(KeyCode key) => !RaceWeekend.Paused && held.Contains(key);

        /// <summary>True on the frame the key went down.</summary>
        public bool Pressed(KeyCode key)
        {
            Roll();
            return !RaceWeekend.Paused && downLast.Contains(key);
        }

        private void Roll()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            downLast.Clear();
            downLast.UnionWith(downNow);
            downNow.Clear();
        }

        private void OnGUI()
        {
            Event ev = Event.current;
            if (ev.type == EventType.KeyDown && ev.keyCode != KeyCode.None)
            {
                // Held keys repeat their KeyDown event; only the first counts as a press.
                if (held.Add(ev.keyCode)) downNow.Add(ev.keyCode);
            }
            else if (ev.type == EventType.KeyUp) held.Remove(ev.keyCode);
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!focus) held.Clear();
        }
    }
}
