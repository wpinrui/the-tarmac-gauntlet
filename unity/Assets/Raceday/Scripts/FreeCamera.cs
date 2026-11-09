using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Fly-around camera. WASD moves, Q and E go down and up, Shift is faster, the right mouse
    /// button looks around and the scroll wheel changes speed. Reads keys through IMGUI events so
    /// it works with either of Unity's input systems.
    /// </summary>
    public sealed class FreeCamera : MonoBehaviour
    {
        private readonly HashSet<KeyCode> held = new HashSet<KeyCode>();
        private float speed = 30f;
        private float yaw, pitch;
        private Vector2 look;

        private void Start()
        {
            Vector3 e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180 ? e.x - 360 : e.x;
        }

        private void OnGUI()
        {
            Event ev = Event.current;
            switch (ev.type)
            {
                case EventType.KeyDown:
                    if (ev.keyCode != KeyCode.None) held.Add(ev.keyCode);
                    break;
                case EventType.KeyUp:
                    held.Remove(ev.keyCode);
                    break;
                case EventType.MouseDrag when ev.button == 1:
                    look += ev.delta;
                    break;
                case EventType.ScrollWheel:
                    speed = Mathf.Clamp(speed * (ev.delta.y < 0 ? 1.25f : 0.8f), 2f, 400f);
                    break;
            }
            if (!ev.shift) held.Remove(KeyCode.LeftShift);
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!focus) held.Clear();
        }

        private void Update()
        {
            yaw += look.x * 0.15f;
            pitch = Mathf.Clamp(pitch + look.y * 0.15f, -89f, 89f);
            look = Vector2.zero;
            transform.rotation = Quaternion.Euler(pitch, yaw, 0);

            Vector3 move = Vector3.zero;
            if (held.Contains(KeyCode.W)) move += Vector3.forward;
            if (held.Contains(KeyCode.S)) move += Vector3.back;
            if (held.Contains(KeyCode.A)) move += Vector3.left;
            if (held.Contains(KeyCode.D)) move += Vector3.right;
            Vector3 world = transform.TransformDirection(move);
            if (held.Contains(KeyCode.E)) world += Vector3.up;
            if (held.Contains(KeyCode.Q)) world += Vector3.down;
            bool fast = Event.current != null ? Event.current.shift : false;
            fast |= held.Contains(KeyCode.LeftShift) || held.Contains(KeyCode.RightShift);
            transform.position += world * (speed * (fast ? 4f : 1f) * Time.unscaledDeltaTime);
        }
    }
}
