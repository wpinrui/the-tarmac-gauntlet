using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The cars' headlamps on the ground ahead at night: where each car's lamps are and where they aim, for the ground's
    /// own shading to light it (Rendering/Shaders/Headlamps.hlsl, in the track, the ground and the grass). The surface
    /// takes the light in its own colour, so the lines and kerbs catch it further out than the asphalt. The cars nearest
    /// the camera are lit, up to <see cref="Most"/>. A car whose beam falls in the floodlights' pools throws only
    /// <see cref="NightLook.HeadlampUnderFloods"/> of its light. A reflection probe or any camera but the game's sees none of it.
    /// </summary>
    public static class HeadlampGround
    {
        /// <summary>Most cars whose lamps light the ground at once: Headlamps.hlsl's TG_MOST_HEADLAMPS.</summary>
        public const int Most = 24;

        /// <summary>A car's beam is judged lit by the floodlights where it is brightest, this far ahead of the car, m.</summary>
        private const float FloodCheck = 12f;

        private static readonly int LightId = Shader.PropertyToID("_TgHeadlamps"),
            AtId = Shader.PropertyToID("_TgHeadlampAt"), AimId = Shader.PropertyToID("_TgHeadlampAim"),
            ReachId = Shader.PropertyToID("_TgHeadlampReach");
        private static readonly Vector4[] at = new Vector4[Most], aim = new Vector4[Most];
        private static readonly List<CarLamps> near = new List<CarLamps>();
        private static Vector4 light;
        private static Vector3 eye;
        private static readonly System.Comparison<CarLamps> nearer =
            (a, b) => (a.transform.position - eye).sqrMagnitude.CompareTo((b.transform.position - eye).sqrMagnitude);
        private static bool hooked;

        /// <summary>
        /// Lights the ground ahead of the cars nearest the camera, as bright as <paramref name="on"/> of the full light, less
        /// where the floodlights already light it.
        /// </summary>
        public static void Publish(Camera cam, float on, Floodlights floods)
        {
            Hook();
            int count = 0;
            if (on > 0.001f && cam != null)
            {
                eye = cam.transform.position;
                near.Clear();
                foreach (CarLamps c in CarLamps.All)
                    if (c.HasHeadlamps) near.Add(c);
                if (near.Count > Most) near.Sort(nearer);
                count = Mathf.Min(near.Count, Most);
                for (int i = 0; i < count; i++)
                {
                    CarLamps c = near[i];
                    Transform t = c.transform;
                    at[i] = t.TransformPoint(new Vector3(0f, c.Nose.y, c.Front));
                    at[i].w = c.Nose.y;
                    aim[i] = t.forward;
                    aim[i].w = Mathf.Lerp(1f, NightLook.HeadlampUnderFloods, Floodlit(floods, t.position + t.forward * FloodCheck));
                }
            }
            Color tint = NightLook.Headlamps.linear * (NightLook.HeadlampGround * on);
            light = new Vector4(count, tint.r, tint.g, tint.b);
            Shader.SetGlobalVectorArray(AtId, at);
            Shader.SetGlobalVectorArray(AimId, aim);
            Shader.SetGlobalVector(LightId, light);
            Shader.SetGlobalFloat(ReachId, NightLook.HeadlampReach);
        }

        /// <summary>How far a spot on the ground lies in a floodlight's pool: 1 well inside one, 0 clear of them all.</summary>
        private static float Floodlit(Floodlights floods, Vector3 p)
        {
            if (floods == null) return 0f;
            float most = 0f;
            foreach (Floodlight f in floods.Lights)
            {
                Vector3 d = p - f.Target;
                d.y = 0f;
                float along = Vector3.Dot(d, f.PoolAxis) / f.Long;
                float across = Vector3.Dot(d, Vector3.Cross(Vector3.up, f.PoolAxis)) / f.Wide;
                float r2 = along * along + across * across;
                if (r2 >= 2.25f) continue;
                most = Mathf.Max(most, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.64f, 2.25f, r2)));
                if (most >= 1f) break;
            }
            return most;
        }

        /// <summary>Puts the light out, as when the race ends.</summary>
        public static void Clear()
        {
            light = Vector4.zero;
            Shader.SetGlobalVector(LightId, light);
        }

        private static void Hook()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginCameraRendering += (context, camera) =>
            {
                if (camera != null && camera.cameraType != CameraType.Game) Shader.SetGlobalVector(LightId, Vector4.zero);
            };
            RenderPipelineManager.endCameraRendering += (context, camera) => Shader.SetGlobalVector(LightId, light);
        }
    }
}
