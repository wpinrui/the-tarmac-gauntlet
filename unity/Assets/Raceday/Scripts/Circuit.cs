using System;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The circuit in world space: x east, y up, z north, metres. Distances run along the
    /// centreline from the start line. Lateral offsets are positive to the left.
    /// </summary>
    public sealed class Circuit
    {
        public readonly CircuitData Data;
        public readonly int Count;
        public readonly float Step;
        public readonly float Lap;
        public readonly Vector3[] Centre;
        public readonly Vector3[] Forward;
        public readonly Vector3[] Left;
        public readonly Vector3[] Lane;
        /// <summary>Lateral offset of the pit lane centre from the track centre at each track sample, or NaN.</summary>
        public readonly float[] LaneLateral;

        private readonly float[] heights;

        public Circuit(CircuitData data)
        {
            Data = data;
            Count = data.centre.Length / 3;
            Step = data.step;
            Lap = data.lapLength;
            Centre = ToPoints(data.centre);
            Forward = new Vector3[Count];
            Left = new Vector3[Count];
            for (int k = 0; k < Count; k++)
            {
                Vector3 t = Centre[Wrap(k + 1)] - Centre[Wrap(k - 1)];
                t.y = 0;
                t.Normalize();
                Forward[k] = t;
                Left[k] = new Vector3(-t.z, 0, t.x);
            }

            Lane = ToPoints(data.pit.lane);
            LaneLateral = new float[Count];
            for (int k = 0; k < Count; k++) LaneLateral[k] = float.NaN;
            for (int i = 0; i < Lane.Length; i++)
            {
                int k = IndexOf(data.pit.laneTrackDistance[i]);
                Vector3 v = Lane[i] - Centre[k];
                LaneLateral[k] = Vector3.Dot(v, Left[k]);
            }

            heights = DecodeHeights(data.terrain);
        }

        public int Wrap(int k) => ((k % Count) + Count) % Count;

        public int IndexOf(float d) => Wrap(Mathf.RoundToInt(d / Step));

        /// <summary>Lap distance from a to b going forwards.</summary>
        public float Ahead(float a, float b) => Mathf.Repeat(b - a, Lap);

        public float HalfWidth(int k) => Data.width[k] * 0.5f;

        /// <summary>A point on the road surface (or its extension) at a lateral offset.</summary>
        public Vector3 Surface(int k, float lateral)
        {
            float cross = Data.crossfall[k];
            float half = HalfWidth(k);
            float banked = Mathf.Clamp(lateral, -half, half);
            return Centre[k] + Left[k] * lateral + Vector3.up * (cross * banked);
        }

        public float TerrainHeight(float x, float z)
        {
            GroundData t = Data.terrain;
            float fx = Mathf.Clamp((x - t.originX) / t.spacing, 0, t.countX - 1.001f);
            float fz = Mathf.Clamp((z - t.originZ) / t.spacing, 0, t.countZ - 1.001f);
            int ix = (int)fx, iz = (int)fz;
            float ux = fx - ix, uz = fz - iz;
            float h00 = heights[iz * t.countX + ix], h10 = heights[iz * t.countX + ix + 1];
            float h01 = heights[(iz + 1) * t.countX + ix], h11 = heights[(iz + 1) * t.countX + ix + 1];
            return Mathf.Lerp(Mathf.Lerp(h00, h10, ux), Mathf.Lerp(h01, h11, ux), uz);
        }

        public float TerrainHeightAt(int ix, int iz) => heights[iz * Data.terrain.countX + ix];

        private static Vector3[] ToPoints(float[] xyz)
        {
            var points = new Vector3[xyz.Length / 3];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector3(xyz[3 * i], xyz[3 * i + 1], xyz[3 * i + 2]);
            return points;
        }

        private static float[] DecodeHeights(GroundData t)
        {
            byte[] bytes = Convert.FromBase64String(t.heightsCm);
            var h = new float[bytes.Length / 2];
            for (int i = 0; i < h.Length; i++)
                h[i] = t.baseHeight + (bytes[2 * i] | (bytes[2 * i + 1] << 8)) / 100f;
            return h;
        }
    }
}
