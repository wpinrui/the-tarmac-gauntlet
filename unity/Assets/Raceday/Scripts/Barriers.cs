using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>Makes scenery solid: cars hit it as a barrier, crash into it and make the barrier's sound.</summary>
    public static class Barriers
    {
        /// <summary>A collider the shape of a static mesh.</summary>
        public static void FromMesh(GameObject go, Mesh mesh)
        {
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            Tag(go);
        }

        /// <summary>Tags every collider on an object as a barrier.</summary>
        public static void Tag(GameObject go) => go.AddComponent<TrackSurface>().Kind = SurfaceKind.Barrier;

        /// <summary>A standing person, as an upright capsule from the feet of an object standing on them.</summary>
        public static void Person(GameObject go)
        {
            CapsuleCollider body = go.AddComponent<CapsuleCollider>();
            body.radius = 0.3f;
            body.height = 1.8f;
            body.center = new Vector3(0f, 0.9f, 0f);
            Tag(go);
        }
    }
}
