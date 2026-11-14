using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Your car in a picture in picture while you watch the broadcast: its onboard, top right, under the live timing
    /// button and above the track map. Only while the director has the camera, so never while you drive, and not while
    /// live timing covers the race. Above 2x the second view of the scene costs too much, so it goes, as in live timing.
    /// Laid out on a 1920 by 1080 screen and scaled to the screen's height.
    /// </summary>
    public sealed class PlayerPip : MonoBehaviour
    {
        public BroadcastDirector Director;
        public RaceCar Car;
        public RaceTower Tower;

        /// <summary>The picture's place and size, px on the layout: right edge in line with the track map's.</summary>
        private const float Width = 384f, Height = 216f, Right = 72f, Top = 100f, Radius = 9f;
        /// <summary>Above this speed the second view of the scene is off.</summary>
        private const float TopSpeed = 2f;
        private const int TextureWidth = 768, TextureHeight = 432;

        private RenderTexture texture;
        private Camera onboard;

        private bool Showing => Director != null && Director.OnAir && Car != null && Car.isActiveAndEnabled
            && Time.timeScale <= TopSpeed && !(Tower != null && Tower.Hidden) && !DebugMenu.Bare;

        private void Start()
        {
            texture = new RenderTexture(TextureWidth, TextureHeight, 24) { hideFlags = HideFlags.DontSave };
            onboard = new GameObject("Picture in picture camera").AddComponent<Camera>();
            onboard.transform.SetParent(transform, false);
            onboard.targetTexture = texture;
            onboard.fieldOfView = 70f;
            onboard.nearClipPlane = 0.1f;
            onboard.farClipPlane = 8000f;
            onboard.enabled = false;
        }

        private void LateUpdate()
        {
            if (onboard == null) return;
            bool on = Showing;
            onboard.enabled = on;
            if (!on) return;
            Transform t = Car.transform;
            onboard.transform.SetPositionAndRotation(t.TransformPoint(new Vector3(0f, 1.2f, 0.3f)), t.rotation);
        }

        private void OnGUI()
        {
            using (GammaGui.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (Event.current.type != EventType.Repaint || texture == null || onboard == null || !onboard.enabled || !Showing) return;
            float k = Screen.height / 1080f;
            float offset = (Screen.width / k - 1920f) / 2f;
            var r = new Rect((1920f - Right - Width + offset) * k, Top * k, Width * k, Height * k);
            GUI.DrawTexture(r, texture, ScaleMode.ScaleAndCrop, false, 0f, Color.white, 0f, Radius * k);
        }

        private void OnDestroy()
        {
            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
            }
        }
    }
}
