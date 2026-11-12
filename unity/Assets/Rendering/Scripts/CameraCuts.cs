using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// Before each game camera draws: when it has cut to another shot, the temporal upscaler forgets the old one, so no
    /// ghost of it is blended into the new. A cut is a jump or a sharp turn in one frame, or a camera that did not draw last
    /// frame. Also publishes where the camera was last frame (<see cref="PrevCameraPosId"/>, where it is now after a cut),
    /// for the shaders that turn their cards to the camera and draw their own motion vectors.
    /// </summary>
    public static class CameraCuts
    {
        /// <summary>A camera that moves further than this in a frame, m, or turns further, degrees, has cut to another shot.</summary>
        private const float CutMove = 8f, CutTurn = 30f;
        public static readonly int PrevCameraPosId = Shader.PropertyToID("_TgPrevCameraPos");

        private static readonly Dictionary<Camera, (Pose pose, int frame)> seen = new Dictionary<Camera, (Pose, int)>();
        private static readonly List<Camera> gone = new List<Camera>();
        private static bool started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Begin()
        {
            if (started) return;
            started = true;
            seen.Clear();
            RenderPipelineManager.beginCameraRendering += Watch;
            Application.quitting += End;
        }

        private static void End()
        {
            Application.quitting -= End;
            RenderPipelineManager.beginCameraRendering -= Watch;
            seen.Clear();
            started = false;
        }

        private static void Watch(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.Game) return;
            var now = new Pose(camera.transform.position, camera.transform.rotation);
            int frame = Time.frameCount;
            bool had = seen.TryGetValue(camera, out var before);
            bool cut = had && (before.frame < frame - 1
                || (now.position - before.pose.position).sqrMagnitude > CutMove * CutMove
                || Quaternion.Angle(now.rotation, before.pose.rotation) > CutTurn);
            seen[camera] = (now, frame);
            if (cut) camera.GetUniversalAdditionalCameraData().resetHistory = true;
            Vector3 last = had && !cut ? before.pose.position : now.position;
            Shader.SetGlobalVector(PrevCameraPosId, new Vector4(last.x, last.y, last.z, 0f));
            if (seen.Count > 8) Forget();
        }

        private static void Forget()
        {
            gone.Clear();
            foreach (Camera c in seen.Keys) if (c == null) gone.Add(c);
            foreach (Camera c in gone) seen.Remove(c);
        }
    }
}
