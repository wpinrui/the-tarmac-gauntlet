using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The rear view mirror at the top centre while you drive: what is behind your car, flipped as a mirror shows it,
    /// 45 degrees across, framed in the HUD's panel colour. It shows while the driving HUD does
    /// (<see cref="DriveHud.Showing"/>) and it is on in the settings (<see cref="DriveSettings.RearMirror"/>), but not
    /// while you hold B to look back or while the chequered flag banner has the top of the screen. Laid out on a 1920
    /// by 1080 screen and scaled to its height.
    /// </summary>
    public sealed class RearMirror : MonoBehaviour
    {
        public DriveHud Hud;
        public RaceCameras Views;

        /// <summary>The mirror's size and its gap from the top of the screen, its frame and corners, px on the layout.</summary>
        private const float Width = 480f, Height = 120f, Top = 28f, Frame = 6f, Radius = 10f;
        /// <summary>Degrees the mirror sees across.</summary>
        private const float Across = 45f;
        /// <summary>Where the view is taken, m from the car's middle: up, and back behind the rear bumper.</summary>
        private const float Up = 1.05f, Behind = 0.15f;

        private static readonly Color Panel = new Color(0.08f, 0.08f, 0.09f, 0.86f);

        private Camera view;
        private RenderTexture seen, flipped;

        private bool Showing => DriveSettings.RearMirror && Hud != null && Hud.Showing && !(Views != null && (Views.LookingBack || Views.Tower != null && Views.Tower.BannerShowing));

        private void Start()
        {
            view = new GameObject("Rear mirror camera").AddComponent<Camera>();
            view.transform.SetParent(transform, false);
            view.nearClipPlane = 0.1f;
            view.farClipPlane = 1500f;
            view.fieldOfView = Camera.HorizontalToVerticalFieldOfView(Across, Width / Height);
            view.enabled = false;
        }

        private void LateUpdate()
        {
            if (view == null) return;
            bool on = Showing;
            if (on) Fit();
            view.enabled = on;
            if (!on) return;
            Transform t = Hud.Car.transform;
            float back = Hud.Car.TryGetComponent(out BoxCollider box) ? box.center.z - box.size.z / 2f : -2.24f;
            view.transform.SetPositionAndRotation(t.TransformPoint(new Vector3(0f, Up, back - Behind)), t.rotation * Quaternion.Euler(0f, 180f, 0f));
        }

        /// <summary>Textures one screen pixel to one layout px of the mirror, made again when the screen's height changes.</summary>
        private void Fit()
        {
            float k = Screen.height / 1080f;
            int w = Mathf.Max(16, Mathf.RoundToInt(Width * k)), h = Mathf.Max(4, Mathf.RoundToInt(Height * k));
            if (seen != null && seen.width == w && seen.height == h) return;
            Release();
            seen = new RenderTexture(w, h, 24) { hideFlags = HideFlags.DontSave };
            flipped = new RenderTexture(w, h, 0) { hideFlags = HideFlags.DontSave };
            view.targetTexture = seen;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || view == null || !view.enabled || seen == null || !Showing) return;
            // Turned left for right, as a mirror shows it. Before the gamma drawing, so the copy keeps its colours.
            RenderTexture active = RenderTexture.active;
            Graphics.Blit(seen, flipped, new Vector2(-1f, 1f), new Vector2(1f, 0f));
            RenderTexture.active = active;
            using (GammaGui.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            float k = Screen.height / 1080f;
            var r = new Rect(Screen.width / 2f - Width * k / 2f, Top * k, Width * k, Height * k);
            float f = Frame * k;
            GUI.DrawTexture(new Rect(r.x - f, r.y - f, r.width + 2f * f, r.height + 2f * f), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Panel, 0f, (Radius + Frame) * k);
            GUI.DrawTexture(r, flipped, ScaleMode.StretchToFill, false, 0f, Color.white, 0f, Radius * k);
        }

        private void Release()
        {
            if (view != null) view.targetTexture = null;
            if (seen != null) { seen.Release(); Destroy(seen); }
            if (flipped != null) { flipped.Release(); Destroy(flipped); }
            seen = flipped = null;
        }

        private void OnDestroy() => Release();
    }
}
