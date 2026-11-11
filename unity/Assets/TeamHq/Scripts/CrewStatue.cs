using UnityEngine;

namespace TarmacGauntlet.TeamHq
{
    /// <summary>
    /// A crew body frozen in one frame of one of its clips, as a plain mesh: for people who stand still, such as
    /// the marshals round the race track, drawn without a skeleton to pose every frame.
    /// </summary>
    public static class CrewStatue
    {
        /// <summary>
        /// Bakes body <paramref name="id"/> in <paramref name="clip"/> at <paramref name="time"/> seconds, feet on y = 0
        /// and facing -z. Returns null when the body's files or the clip are missing.
        /// </summary>
        public static Mesh Bake(string id, string clip, float time, out Material[] materials)
        {
            materials = null;
            HqCrewBody body = HqCrewBody.Load(id);
            if (body == null || !body.Clips.TryGetValue(clip, out HqCrewBody.Clip c)) return null;
            var holder = new GameObject("Crew statue");
            try
            {
                HqCrewRig rig = body.Build(holder.transform, 0, true);
                rig.Pose(new[] { (c, time, 1f) });
                SkinnedMeshRenderer skin = holder.GetComponentInChildren<SkinnedMeshRenderer>();
                var mesh = new Mesh { name = id + " " + clip };
                skin.BakeMesh(mesh);
                mesh.RecalculateBounds();
                materials = skin.sharedMaterials;
                return mesh;
            }
            finally
            {
                Object.Destroy(holder);
            }
        }
    }
}
