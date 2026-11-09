using System;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>The JSON written by tools/circuit/generate.py. Lateral offsets are positive to the left.</summary>
    [Serializable]
    public sealed class CircuitData
    {
        public float lapLength;
        public float step;
        /// <summary>Centreline samples as x, y, z triples, one every <see cref="step"/> metres from the start line.</summary>
        public float[] centre;
        public float[] width;
        /// <summary>Rise of the left edge over the right edge, per metre across.</summary>
        public float[] crossfall;
        public float[] clearLeft;
        public float[] clearRight;
        public CornerData[] corners;
        public GridData grid;
        public float[] safetyCarLines;
        public GroundData terrain;
        public PitData pit;

        public static CircuitData Load()
        {
            TextAsset text = Resources.Load<TextAsset>("Tracks/circuit");
            return JsonUtility.FromJson<CircuitData>(text.text);
        }
    }

    [Serializable]
    public sealed class CornerData
    {
        public int number;
        public float s;
        public float e;
        public float radius;
        public bool offCamber;
        public ArcData[] arcs;
    }

    [Serializable]
    public sealed class ArcData
    {
        public float s;
        public float e;
        /// <summary>+1 for a right-hander, -1 for a left-hander.</summary>
        public int dir;
        public float radius;
    }

    [Serializable]
    public sealed class GridData
    {
        public float slotWidth;
        public float slotLength;
        public float yellowLength;
        public GridSlot[] slots;
    }

    [Serializable]
    public sealed class GridSlot
    {
        /// <summary>Lap distance of the slot's front line.</summary>
        public float d;
        public float lateral;
    }

    [Serializable]
    public sealed class GroundData
    {
        public float originX;
        public float originZ;
        public float spacing;
        public int countX;
        public int countZ;
        public float baseHeight;
        /// <summary>Little-endian uint16 heights in centimetres above <see cref="baseHeight"/>, row by row along x.</summary>
        public string heightsCm;
    }

    [Serializable]
    public sealed class PitData
    {
        public string side;
        public float laneWidth;
        public float fastLane;
        public float limiterKph;
        public float entry;
        public float exit;
        public float limitStart;
        public float limitEnd;
        public float garageDepth;
        public float garageWidth;
        /// <summary>Pit lane centre samples as x, y, z triples.</summary>
        public float[] lane;
        /// <summary>Lap distance of the track alongside each pit lane sample.</summary>
        public float[] laneTrackDistance;
        public GarageData[] garages;
    }

    [Serializable]
    public sealed class GarageData
    {
        public float x;
        public float y;
        public float z;
        /// <summary>Compass heading of the garage row at this garage, degrees.</summary>
        public float heading;
    }
}
