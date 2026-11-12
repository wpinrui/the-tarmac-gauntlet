using UnityEngine;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// The race's sky as the sky effects draw it: the light, where the clouds are and how thick the air is. The race's
    /// <c>TimeOfDay</c> publishes it every frame (<see cref="Publish"/>) and the floodlights their beams
    /// (<see cref="Beams"/>). It all goes to shader globals, read by the cloud shadows (<see cref="CloudShadows"/>), the
    /// volumetric clouds and fog (<see cref="VolumetricsFeature"/>) and the sun's rays (<see cref="SunRaysFeature"/>).
    /// Outside a race nothing is published and those effects draw nothing.
    /// </summary>
    public static class Atmosphere
    {
        /// <summary>Height the cloud map lies at, m: the clouds' bases on a fair day, and where their shadows are cast from.</summary>
        public const float CloudBase = 1500f;
        /// <summary>The volumetric clouds' 3D noise repeats every this many metres.</summary>
        public const float NoisePeriod = 1800f;
        /// <summary>Most floodlight beams drawn in the fog at once.</summary>
        public const int MostBeams = 16;

        /// <summary>The sky at one moment.</summary>
        public struct Sky
        {
            /// <summary>Unit vector towards whichever of the sun or moon lights the scene.</summary>
            public Vector3 SunDir;
            /// <summary>Its light reaching the ground under the weather: colour times intensity.</summary>
            public Color Sun;
            /// <summary>The clouds' colours where the sun lights them and in their shade, as the sky shader draws them.</summary>
            public Color CloudLit, CloudShade;
            /// <summary>The haze's colour, and its density, per m.</summary>
            public Color Haze;
            public float HazeDensity;
            /// <summary>The cloud noise: big masses in red, finer detail in green.</summary>
            public Texture CloudTex;
            /// <summary>The cloud map's tiles a metre, its cover threshold (lower is more cloud) and how overcast it is, 0 to 1.</summary>
            public float CloudTiles, Cover, Overcast;
            /// <summary>Share of the sun's light a cloud's shadow takes.</summary>
            public float ShadowStrength;
            /// <summary>The cloud map's drift, tiles: xy the big layer, zw the small one.</summary>
            public Vector4 CloudOffset;
            /// <summary>How far the wind has carried the clouds, m, east and north, wrapped at <see cref="NoisePeriod"/>.</summary>
            public Vector2 Drift;
            /// <summary>The low mist's density at the ground, per m, how fast it thins with height, m, and the ground's height, m.</summary>
            public float MistDensity, MistHeight, Ground;
        }

        /// <summary>True while a race sky is published.</summary>
        public static bool Present { get; private set; }
        /// <summary>The sky last published.</summary>
        public static Sky Now { get; private set; }

        private static readonly Vector4[] beamPos = new Vector4[MostBeams], beamAim = new Vector4[MostBeams];

        private static readonly int CloudTexId = Shader.PropertyToID("_TgCloudTex"), CloudMapId = Shader.PropertyToID("_TgCloudMap"),
            CloudOffsetId = Shader.PropertyToID("_TgCloudOffset"), CloudDriftId = Shader.PropertyToID("_TgCloudDrift"),
            SunDirId = Shader.PropertyToID("_TgSunDir"), SunLightId = Shader.PropertyToID("_TgSunLight"),
            CloudLitId = Shader.PropertyToID("_TgCloudLit"), CloudShadeId = Shader.PropertyToID("_TgCloudShade"),
            HazeId = Shader.PropertyToID("_TgHaze"), MistId = Shader.PropertyToID("_TgMist"),
            BeamPosId = Shader.PropertyToID("_TgBeamPos"), BeamAimId = Shader.PropertyToID("_TgBeamAim"),
            BeamsId = Shader.PropertyToID("_TgBeams");

        /// <summary>The sky now. Call it every frame the race sky is up.</summary>
        public static void Publish(in Sky s)
        {
            Present = true;
            Now = s;
            if (s.CloudTex != null) Shader.SetGlobalTexture(CloudTexId, s.CloudTex);
            Shader.SetGlobalVector(CloudMapId, new Vector4(s.CloudTiles, s.Cover, s.Overcast, s.ShadowStrength));
            Shader.SetGlobalVector(CloudOffsetId, s.CloudOffset);
            Shader.SetGlobalVector(CloudDriftId, new Vector4(s.Drift.x, s.Drift.y, 0f, 0f));
            Shader.SetGlobalVector(SunDirId, s.SunDir);
            Shader.SetGlobalColor(SunLightId, s.Sun);
            Shader.SetGlobalColor(CloudLitId, s.CloudLit);
            Shader.SetGlobalColor(CloudShadeId, s.CloudShade);
            Shader.SetGlobalVector(HazeId, new Vector4(s.Haze.r, s.Haze.g, s.Haze.b, s.HazeDensity));
            Shader.SetGlobalVector(MistId, new Vector4(s.MistDensity, Mathf.Max(s.MistHeight, 1f), s.Ground, 0f));
        }

        /// <summary>The race sky is gone: the sky effects stop.</summary>
        public static void Clear()
        {
            Present = false;
            Beams(null, null, 0, Color.black);
        }

        /// <summary>
        /// The floodlight beams to draw in the fog, up to <see cref="MostBeams"/>: each lens's position and the beam's reach,
        /// m, in <paramref name="positions"/>, its aim and the cosine of half its cone in <paramref name="aims"/>, and their
        /// light, colour times strength, 0 for none.
        /// </summary>
        public static void Beams(Vector4[] positions, Vector4[] aims, int count, Color light)
        {
            count = positions == null || aims == null ? 0 : Mathf.Min(count, MostBeams);
            for (int i = 0; i < MostBeams; i++)
            {
                beamPos[i] = i < count ? positions[i] : Vector4.zero;
                beamAim[i] = i < count ? aims[i] : Vector4.zero;
            }
            Shader.SetGlobalVectorArray(BeamPosId, beamPos);
            Shader.SetGlobalVectorArray(BeamAimId, beamAim);
            Shader.SetGlobalVector(BeamsId, new Vector4(count, light.r, light.g, light.b));
        }
    }
}
