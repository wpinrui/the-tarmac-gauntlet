// --export <file>: writes the furniture and the track around it, near the start line, one marshal post and one
// corner's boards, as JSON for the look preview. Positions are centimetres from each view's centre.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TarmacGauntlet.Raceday;
using UnityEngine;

static class Export
{
    public static void Write(string path, Circuit c, CircuitBuilder b, List<CircuitPart> track)
    {
        var stands = new Grandstands(c, b, null, StandLook.Main, StandLook.Corners);
        stands.Build();
        var f = new TrackFurniture(c, b, stands.Footprints);
        var built = new FurnitureBuilder(c, f, null);
        // Each view: its centre, how far round it to send, and a camera to start from.
        var views = new List<(string name, Vector3 centre, float radius, Vector3 eye, Vector3 look)>();
        int g = f.GantrySample;
        views.Add(("start", c.Centre[g], 140f, c.Surface(c.Wrap(g - 17), 3f) + Vector3.up * 3.2f, c.Centre[g] + Vector3.up * 5.5f));
        MarshalPost post = f.Posts.First(p => Math.Abs(p.Distance - 3927f) < 30f);
        int pk = c.IndexOf(post.Distance);
        float pside = post.Side == 0 ? 1f : -1f;
        views.Add(("post", post.Base, 90f, PostEye(c, post, pk, pside), post.Base + Vector3.up * 1.5f));
        DistanceBoard mid = f.Boards.First(d => d.Metres == 200 && Math.Abs(d.Distance - 3767f) < 5f);
        int bk = c.IndexOf(mid.Distance);
        float bs = mid.Side == 0 ? 1f : -1f;
        views.Add(("boards", mid.Base, 230f, c.Surface(c.Wrap(bk - 80), bs * 2f) + Vector3.up * 1.2f, c.Surface(c.Wrap(bk + 40), 0f) + Vector3.up * 1f));

        var sb = new StringBuilder("{");
        for (int v = 0; v < views.Count; v++)
        {
            var (name, centre, radius, eye, look) = views[v];
            if (v > 0) sb.Append(',');
            Vector3 e = eye - centre, l = look - centre;
            sb.Append($"\"{name}\":{{\"eye\":[{e.x:F2},{e.y:F2},{e.z:F2}],\"look\":[{l.x:F2},{l.y:F2},{l.z:F2}],\"track\":[");
            bool first = true;
            foreach (var group in track.Where(p => p.Name != "Ground").GroupBy(p => p.Name))
            {
                var kit = new MeshKit();
                foreach (CircuitPart p in group) kit.Append(p.Kit);
                first = Mesh(sb, group.Key, kit, centre, radius, false, first);
            }
            sb.Append("],\"ground\":");
            Ground(sb, c, centre, radius);
            sb.Append(",\"furniture\":[");
            bool fm = true;
            foreach (var group in built.Parts.GroupBy(p => p.Name))
            {
                var kit = new MeshKit();
                foreach (CircuitPart p in group) kit.Append(p.Kit);
                fm = Mesh(sb, group.Key, kit, centre, radius, true, fm);
            }
            fm = Mesh(sb, "Red lenses", built.RedLenses, centre, radius, true, fm);
            fm = Mesh(sb, "Green lenses", built.GreenLenses, centre, radius, true, fm);
            var panels = new MeshKit();
            foreach (MeshKit p in built.Panels) panels.Append(p);
            fm = Mesh(sb, "Panels", panels, centre, radius, true, fm);
            sb.Append("],\"marshals\":[");
            bool fmar = true;
            foreach (var (feet, facing, pose) in built.Marshals)
            {
                Vector3 d = feet - centre;
                if (d.x * d.x + d.z * d.z > radius * radius) continue;
                if (!fmar) sb.Append(',');
                fmar = false;
                sb.Append($"[{d.x:F2},{d.y:F2},{d.z:F2},{facing.x:F3},{facing.z:F3},{pose}]");
            }
            sb.Append(']');
            sb.Append('}');
        }
        sb.Append('}');
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>A camera on the runoff, upstream of the post and nearer the road, looking at the marshals.</summary>
    static Vector3 PostEye(Circuit c, MarshalPost post, int k, float side)
    {
        Vector3 p = c.Centre[c.Wrap(k - 6)] + c.Left[c.Wrap(k - 6)] * side * (c.HalfWidth(k) + 20f);
        p.y = c.TerrainHeight(p.x, p.z) + 3f;
        return p;
    }

    /// <summary>Appends a mesh's triangles whose middle lies within the view, as {name, p, n, uv?, i}.</summary>
    static bool Mesh(StringBuilder sb, string name, MeshKit kit, Vector3 centre, float radius, bool uv, bool first)
    {
        var map = new Dictionary<int, int>();
        var idx = new List<int>();
        var v = kit.Vertices;
        var t = kit.Triangles;
        for (int i = 0; i < t.Count; i += 3)
        {
            Vector3 m = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f - centre;
            if (m.x * m.x + m.z * m.z > radius * radius) continue;
            for (int k = 0; k < 3; k++)
            {
                if (!map.TryGetValue(t[i + k], out int j)) map[t[i + k]] = j = map.Count;
                idx.Add(j);
            }
        }
        if (idx.Count == 0) return first;
        if (!first) sb.Append(',');
        var order = map.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();
        sb.Append($"{{\"name\":\"{name}\",\"p\":[");
        sb.Append(string.Join(",", order.Select(i => { Vector3 p = v[i] - centre; return $"{(int)Math.Round(p.x * 100)},{(int)Math.Round(p.y * 100)},{(int)Math.Round(p.z * 100)}"; })));
        sb.Append("],\"n\":[");
        sb.Append(string.Join(",", order.Select(i => { Vector3 n = kit.Normals[i]; return $"{(int)Math.Round(n.x * 100)},{(int)Math.Round(n.y * 100)},{(int)Math.Round(n.z * 100)}"; })));
        if (uv)
        {
            sb.Append("],\"uv\":[");
            sb.Append(string.Join(",", order.Select(i => $"{kit.UVs[i].x:F3},{kit.UVs[i].y:F3}")));
        }
        sb.Append("],\"i\":[");
        sb.Append(string.Join(",", idx));
        sb.Append("]}");
        return false;
    }

    /// <summary>The ground as a height grid round the view's centre, 2 m apart.</summary>
    static void Ground(StringBuilder sb, Circuit c, Vector3 centre, float radius)
    {
        const float step = 2f;
        int n = (int)(radius * 1.6f / step);
        sb.Append($"{{\"step\":{step},\"n\":{n},\"h\":[");
        var hs = new List<string>();
        for (int z = -n; z <= n; z++)
            for (int x = -n; x <= n; x++)
                hs.Add(((int)Math.Round((c.TerrainHeight(centre.x + x * step, centre.z + z * step) - centre.y) * 100)).ToString());
        sb.Append(string.Join(",", hs));
        sb.Append("]}");
    }
}
