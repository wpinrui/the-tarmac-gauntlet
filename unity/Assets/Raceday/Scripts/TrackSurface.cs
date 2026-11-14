using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    public enum SurfaceKind { Tarmac, Kerb, Grass, Gravel, Barrier }

    /// <summary>Tags a circuit collider with what it is made of, so tyres know how much grip they get.</summary>
    public sealed class TrackSurface : MonoBehaviour
    {
        public SurfaceKind Kind;

        /// <summary>Grip relative to dry tarmac.</summary>
        public static float Grip(SurfaceKind kind) => kind switch
        {
            SurfaceKind.Kerb => 0.95f,
            SurfaceKind.Grass => 0.6f,
            SurfaceKind.Gravel => 0.5f,
            _ => 1f,
        };

        /// <summary>Rolling resistance as a fraction of the load on the tyre.</summary>
        public static float Rolling(SurfaceKind kind) => kind switch
        {
            SurfaceKind.Grass => 0.06f,
            SurfaceKind.Gravel => 0.35f,
            _ => 0.012f,
        };

        /// <summary>What each solid circuit part is made of, by part name.</summary>
        public static SurfaceKind ForPart(string name) => name switch
        {
            "Kerbs" => SurfaceKind.Kerb,
            "Verges" or "Grass runoff" or "Pit lane infield" or "Ground" => SurfaceKind.Grass,
            "Gravel traps" => SurfaceKind.Gravel,
            "Armco" or "Tyre walls" or "Pit wall" or "Garages" or "Pit entry bollard"
                or "Stand concrete" or "Stand steel" or "Stand cladding" or "Stand seats" or "Stand roofs" or "Stand banks"
                or "Catch fence" or "Catch fence posts" or "Pit building" or "Pit building glass" or "Pit building steel"
                or "Floodlight steel" => SurfaceKind.Barrier,
            _ => SurfaceKind.Tarmac,
        };
    }
}
