// What the grandstands need of UnityEngine beyond the circuit check's stubs.
namespace UnityEngine
{
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
}
namespace TarmacGauntlet.Raceday
{
    public sealed class StandMaterials
    {
        public UnityEngine.Material Concrete, Steel, Cladding, Seats, Roof, Bank, Crowd, FenceMesh, FencePosts;
    }
}
