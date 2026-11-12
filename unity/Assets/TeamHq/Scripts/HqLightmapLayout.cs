using System;
using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.TeamHq
{
    /// <summary>
    /// Lays every lit surface of the building out in one lightmap, a flat piece to a rectangle at
    /// <see cref="TexelsPerMetre"/>, and fills each part's <see cref="HqPart.Uv2s"/>. The bake
    /// (tools/team-hq/bake) and the game run the same layout, and <see cref="Signature"/> tells the game whether the
    /// committed bake still fits the building.
    /// </summary>
    public sealed class HqLightmapLayout
    {
        public const float TexelsPerMetre = 20;
        /// <summary>Texels round each piece, so filtering never reads a neighbour.</summary>
        private const int Pad = 2;
        /// <summary>Parts the lightmap leaves out: they glow or let light through.</summary>
        public static readonly HashSet<string> Skip = new HashSet<string> { "window", "glass", "lamp", "screen", "blob" };

        /// <summary>The lightmap's size in texels.</summary>
        public readonly int Width, Height;
        /// <summary>A hash of the building's lit surfaces and their places in the lightmap.</summary>
        public readonly string Signature;

        public HqLightmapLayout(Dictionary<string, HqPart> parts)
        {
            var cells = new List<(HqPart part, HqChart chart, int w, int h)>();
            var keys = new List<string>(parts.Keys);
            keys.Sort(string.CompareOrdinal);
            foreach (string key in keys)
            {
                if (Skip.Contains(key)) continue;
                HqPart p = parts[key];
                foreach (HqChart c in p.Charts)
                    cells.Add((p, c, Texels(c.W), Texels(c.H)));
            }
            // Tallest first into shelves; ties keep the build order, so the layout never changes by itself.
            var order = new int[cells.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => cells[a].h != cells[b].h ? cells[b].h.CompareTo(cells[a].h) : a.CompareTo(b));

            double area = 0;
            foreach (var c in cells) area += (c.w + 2 * Pad) * (c.h + 2 * Pad);
            int width = 256;
            while (width * width < area * 1.15) width *= 2;
            var place = new (int x, int y)[cells.Count];
            int height;
            while (true)
            {
                int x = 0, y = 0, shelf = 0;
                foreach (int i in order)
                {
                    int w = cells[i].w + 2 * Pad, h = cells[i].h + 2 * Pad;
                    if (x + w > width) { x = 0; y += shelf; shelf = 0; }
                    place[i] = (x, y);
                    x += w;
                    shelf = Mathf.Max(shelf, h);
                }
                height = y + shelf;
                if (height <= width) break;
                width *= 2;
            }
            Width = width;
            Height = 256;
            while (Height < height) Height *= 2;

            for (int i = 0; i < cells.Count; i++)
            {
                (HqPart p, HqChart c, int w, int h) = cells[i];
                while (p.Uv2s.Count < p.Vertices.Count) p.Uv2s.Add(Vector2.zero);
                float x0 = place[i].x + Pad, y0 = place[i].y + Pad;
                float sx = c.W > 0 ? w / c.W : 0, sy = c.H > 0 ? h / c.H : 0;
                for (int v = c.First; v < c.First + c.Count; v++)
                {
                    Vector2 l = p.ChartUvs[v];
                    p.Uv2s[v] = new Vector2((x0 + l.x * sx) / Width, (y0 + l.y * sy) / Height);
                }
            }
            Signature = Hash(parts, keys);
        }

        /// <summary>Texels across a length, with a little give so float noise never adds one.</summary>
        private static int Texels(float metres) => Mathf.Max(1, (int)Math.Ceiling(metres * TexelsPerMetre - 0.01));

        /// <summary>Rounds off the half way points the building's numbers sit on, so float noise never moves the hash.</summary>
        private static long Snap(float v) => (long)Math.Floor(v + 0.37);

        /// <summary>FNV-1a over the lit parts' vertices to the centimetre and their lightmap places to the texel.</summary>
        private string Hash(Dictionary<string, HqPart> parts, List<string> keys)
        {
            ulong h = 14695981039346656037UL;
            void Add(long v)
            {
                for (int b = 0; b < 8; b++) { h ^= (ulong)((v >> (b * 8)) & 0xff); h *= 1099511628211UL; }
            }
            Add(Width);
            Add(Height);
            foreach (string key in keys)
            {
                if (Skip.Contains(key)) continue;
                foreach (char ch in key) Add(ch);
                HqPart p = parts[key];
                Add(p.Vertices.Count);
                for (int i = 0; i < p.Vertices.Count; i++)
                {
                    Vector3 v = p.Vertices[i];
                    Add(Snap(v.x * 100)); Add(Snap(v.y * 100)); Add(Snap(v.z * 100));
                    Add(Snap(p.Uv2s[i].x * Width)); Add(Snap(p.Uv2s[i].y * Height));
                }
            }
            return h.ToString("x16");
        }
    }
}
