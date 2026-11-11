using System;
using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Fly-around camera. The mouse looks around, WASD moves along the ground whichever way the
    /// camera points, Q and E go down and up, Shift is faster and the scroll wheel changes speed.
    /// Click to capture the mouse and press Escape to free it. Keys come through IMGUI events so
    /// it works with either of Unity's input systems. With a <see cref="Target"/> it rides along
    /// with that car: it starts as a chase view and the look and moves are kept relative to the car.
    /// </summary>
    public sealed class FreeCamera : MonoBehaviour
    {
        private readonly HashSet<KeyCode> held = new HashSet<KeyCode>();
        private float speed = 100f;
        private bool shift;
        private float yaw, pitch;
        private Vector2 guiLook;
        private bool legacyMouse = true;

        /// <summary>The car to follow, or null to fly freely. Set before enabling.</summary>
        public Transform Target;
        private float followSpeed = 10f;
        // While following, the offset and yaw are in the car's flat heading frame.
        private Vector3 offset;
        private float carYaw;
        private bool carYawSettled;

        private void OnEnable()
        {
            held.Clear();
            Capture(true);
            if (Target != null)
            {
                // Starts where the chase view sits: 7 m behind, 2.2 m up, looking just ahead of the car.
                offset = new Vector3(0f, 2.2f, -7f);
                Vector3 look = Quaternion.LookRotation(new Vector3(0f, 0.9f, 4f) - offset).eulerAngles;
                yaw = look.y > 180 ? look.y - 360 : look.y;
                pitch = look.x > 180 ? look.x - 360 : look.x;
                carYawSettled = false;
                return;
            }
            Vector3 e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180 ? e.x - 360 : e.x;
        }

        private void OnDisable() => Capture(false);

        private static void Capture(bool on)
        {
            Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !on;
        }

        private void OnGUI()
        {
            Event ev = Event.current;
            switch (ev.type)
            {
                case EventType.KeyDown:
                    if (ev.keyCode == KeyCode.Escape) Capture(false);
                    else if (ev.keyCode != KeyCode.None) held.Add(ev.keyCode);
                    break;
                case EventType.KeyUp:
                    held.Remove(ev.keyCode);
                    break;
                case EventType.MouseDown:
                    Capture(true);
                    break;
                case EventType.MouseMove:
                case EventType.MouseDrag:
                    guiLook += ev.delta;
                    break;
                case EventType.ScrollWheel:
                    if (Target != null) followSpeed = Mathf.Clamp(followSpeed * (ev.delta.y < 0 ? 1.4f : 0.7f), 1f, 300f);
                    else speed = Mathf.Clamp(speed * (ev.delta.y < 0 ? 1.4f : 0.7f), 5f, 3000f);
                    break;
            }
            shift = ev.shift;
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!focus) held.Clear();
        }

        private Vector2 MouseDelta()
        {
            // The old input manager gives smooth deltas while the cursor is locked. If the
            // project only has the new input system it throws, so fall back to IMGUI deltas.
            if (legacyMouse)
            {
                try
                {
                    guiLook = Vector2.zero;
                    return new Vector2(Input.GetAxisRaw("Mouse X"), -Input.GetAxisRaw("Mouse Y")) * 6f;
                }
                catch (InvalidOperationException)
                {
                    legacyMouse = false;
                }
            }
            Vector2 d = guiLook;
            guiLook = Vector2.zero;
            return d;
        }

        private void Update()
        {
            Vector2 look = Cursor.lockState == CursorLockMode.Locked ? MouseDelta() : Vector2.zero;
            if (Cursor.lockState != CursorLockMode.Locked) guiLook = Vector2.zero;
            yaw += look.x * 0.15f;
            pitch = Mathf.Clamp(pitch + look.y * 0.15f, -89f, 89f);

            Quaternion heading = Quaternion.Euler(0, yaw, 0);
            Vector3 move = Vector3.zero;
            if (held.Contains(KeyCode.W)) move += Vector3.forward;
            if (held.Contains(KeyCode.S)) move += Vector3.back;
            if (held.Contains(KeyCode.A)) move += Vector3.left;
            if (held.Contains(KeyCode.D)) move += Vector3.right;
            Vector3 step = heading * move;
            if (held.Contains(KeyCode.E)) step += Vector3.up;
            if (held.Contains(KeyCode.Q)) step += Vector3.down;
            step *= (Target != null ? followSpeed : speed) * (shift ? 5f : 1f) * Time.unscaledDeltaTime;

            if (Target != null)
            {
                offset += step;   // placed on the car in LateUpdate, after it has moved
                return;
            }
            transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            transform.position += step;
        }

        private void LateUpdate()
        {
            if (Target == null) return;
            // The car's heading on the ground, eased a little so bumps and slides don't shake the view.
            Vector3 flat = Vector3.ProjectOnPlane(Target.forward, Vector3.up);
            if (flat.sqrMagnitude > 1e-6f)
            {
                float want = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                carYaw = carYawSettled ? Mathf.LerpAngle(carYaw, want, 1f - Mathf.Exp(-10f * Time.deltaTime)) : want;
                carYawSettled = true;
            }
            Quaternion car = Quaternion.Euler(0, carYaw, 0);
            transform.SetPositionAndRotation(Target.position + car * offset, car * Quaternion.Euler(pitch, yaw, 0));
        }
    }
}
