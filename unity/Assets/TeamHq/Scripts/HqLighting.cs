using UnityEngine;

namespace TarmacGauntlet.TeamHq
{
    /// <summary>A reflection probe in the building: where it looks from, and the room box it projects onto.</summary>
    public struct HqProbe
    {
        public string Name;
        public Vector3 Capture, Centre, Size;
        /// <summary>Higher wins where boxes overlap.</summary>
        public int Importance;
    }

    /// <summary>
    /// The building's light, in Unity's frame, shared by <see cref="HqScene"/> and the bake (tools/team-hq/bake) so both
    /// light the same room. Colours are sRGB.
    /// </summary>
    public static class HqLighting
    {
        /// <summary>The sun's travel: from high over the front windows, as in the viewer.</summary>
        public static readonly Vector3 SunDirection = new Vector3(6, -16, 24).normalized;
        public static readonly Vector3 SunColour = new Vector3(1f, 0.96f, 0.89f);
        public const float SunIntensity = 1.25f;
        /// <summary>
        /// The live sun in the game, in Linear colour space: its patches on the floor about as bright over the baked light
        /// round them as they were in Gamma. The bake's bounce of the sun stays at <see cref="SunIntensity"/>.
        /// </summary>
        public const float LiveSunIntensity = 2.2f;
        /// <summary>
        /// The room's exposure against the race's, stops. In Linear the bake shows at its full strength, where Gamma
        /// showed it darker; this keeps the room as bright on the whole while its shadows open up.
        /// </summary>
        public const float Exposure = -0.3f;
        public static readonly Vector3 WarmLamp = new Vector3(1f, 0.93f, 0.82f);
        public static readonly Vector3 WhiteLamp = new Vector3(1f, 1f, 1f);


        /// <summary>The garage first, then the office and the trophy room on the mezzanine.</summary>
        public static readonly HqProbe[] Probes =
        {
            new HqProbe { Name = "garage", Capture = new Vector3(0, 1.6f, -1.5f), Centre = new Vector3(0, HqGeometry.H / 2, 0), Size = new Vector3(HqGeometry.W, HqGeometry.H, HqGeometry.D), Importance = 0 },
            new HqProbe { Name = "office", Capture = new Vector3(3, HqGeometry.MZ + 1.6f, 4.75f), Centre = new Vector3(3, (HqGeometry.MZ + HqGeometry.H) / 2, 4.75f), Size = new Vector3(20, HqGeometry.H - HqGeometry.MZ, 4.5f), Importance = 1 },
            new HqProbe { Name = "trophy", Capture = new Vector3(-10, HqGeometry.MZ + 1.6f, 4.75f), Centre = new Vector3(-10, (HqGeometry.MZ + HqGeometry.H) / 2, 4.75f), Size = new Vector3(6, HqGeometry.H - HqGeometry.MZ, 4.5f), Importance = 1 },
        };
    }
}
