// --props-export <file>: writes the trackside props with everything round them (track, ground, grandstands, furniture,
// armco, trees, people) in a few views, as JSON for tools/furniture-check/props-preview. Positions are centimetres
// from each view's centre.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TarmacGauntlet.Raceday;
using UnityEngine;

static class PropsExport
{
    public static void Write(string path, Circuit c, CircuitBuilder b, List<CircuitPart> track, List<CircuitPart> stands, List<StandFootprint> footprints)
    {
        var f = new TrackFurniture(c, b, footprints);
        var furniture = new FurnitureBuilder(c, f, null);
        var armco = new ArmcoBuilder(c, b.ArmcoRuns, footprints, f.GantryLeft, f.GantryRight);
        var plan = new LandscapePlan(c, b.RunoffReach, LandscapeLooks.Current, footprints);
        var props = new TracksideProps(c, b, footprints, f, armco.Ends, plan.Trees);
        var built = new PropBuilder(c, props, null);

        var views = new List<(string name, Vector3 centre, Vector3 eye, Vector3 look, float fov)>();
        // The longest row of hoardings, from the driver's seat coming up to it.
        var rows = new List<List<PropSpot>>();
        foreach (PropSpot s in props.Spots.Where(s => s.Kind == PropKind.Hoarding))
        {
            if (rows.Count > 0 && rows[^1][0].Side == s.Side && Math.Abs(rows[^1][^1].Distance - s.Distance) < 6f) rows[^1].Add(s);
            else rows.Add(new List<PropSpot> { s });
        }
        // Rows behind armco first, as most of the lap has it.
        foreach (List<PropSpot> row in rows.OrderByDescending(r => b.Fenced(r[0].Side, c.IndexOf(r[0].Distance)) ? 1 : 0).ThenByDescending(r => r.Count).Take(3))
        {
            PropSpot mid = row[row.Count / 2];
            int k = c.IndexOf(mid.Distance);
            float sign = mid.Side == 0 ? 1f : -1f;
            int e = c.Wrap(k - 18);
            views.Add(($"hoardings-{mid.Distance:F0}", mid.Base, c.Surface(e, sign * 2.5f) + Vector3.up * 1.1f, mid.Base + Vector3.up * 1f, 40f));
        }
        foreach (PropSpot tower in props.Spots.Where(s => s.Kind == PropKind.CameraTower).Take(3))
        {
            int k = c.IndexOf(tower.Distance);
            float sign = tower.Side == 0 ? 1f : -1f;
            int e = c.Wrap(k - 22);
            views.Add(($"tower-{tower.Distance:F0}", tower.Base, c.Surface(e, -sign * 2f) + Vector3.up * 1.6f, tower.Base + Vector3.up * 4f, 32f));
        }
        foreach (PropSpot cabin in props.Spots.Where(s => s.Kind == PropKind.Cabin).Take(2))
        {
            int k = c.IndexOf(cabin.Distance);
            float sign = cabin.Side == 0 ? 1f : -1f;
            int e = c.Wrap(k - 14);
            views.Add(($"cabin-{cabin.Distance:F0}", cabin.Base, c.Surface(e, sign * 3f) + Vector3.up * 1.4f, cabin.Base + Vector3.up * 1.4f, 45f));
        }
        // From up high over the outside of the first tower's corner, as the helicopter sees it.
        PropSpot first = props.Spots.First(s => s.Kind == PropKind.CameraTower);
        {
            int k = c.IndexOf(first.Distance);
            Vector3 eye = first.Base + first.ToRoad * -40f - first.Along * 70f + Vector3.up * 55f;
            views.Add(("helicopter", first.Base, eye, c.Centre[k], 40f));
        }

        var sb = new StringBuilder("{");
        for (int v = 0; v < views.Count; v++)
        {
            var (name, centre, eye, look, fov) = views[v];
            const float radius = 220f;
            if (v > 0) sb.Append(',');
            Vector3 ee = eye - centre, l = look - centre;
            sb.Append($"\"{name}\":{{\"fov\":{fov},\"eye\":[{ee.x:F2},{ee.y:F2},{ee.z:F2}],\"look\":[{l.x:F2},{l.y:F2},{l.z:F2}],\"meshes\":[");
            bool firstMesh = true;
            void Group(string group, IEnumerable<(string name, MeshKit kit)> parts)
            {
                foreach (var g in parts.GroupBy(p => p.name))
                {
                    var kit = new MeshKit();
                    foreach (var p in g) kit.Append(p.kit);
                    firstMesh = Mesh(sb, group, g.Key, kit, centre, radius, firstMesh);
                }
            }
            Group("track", track.Where(p => p.Name != "Ground").Select(p => (p.Name, p.Kit)));
            Group("stands", stands.Select(p => (p.Name, p.Kit)));
            Group("furniture", furniture.Parts.Select(p => (p.Name, p.Kit)).Concat(new[] { ("Panels", Join(furniture.Panels)), ("Red lenses", furniture.RedLenses), ("Green lenses", furniture.GreenLenses) }));
            Group("furniture", new[] { ("Armco steel", armco.Steel), ("Armco catch fence", armco.Fence), ("Armco catch fence posts", armco.FencePosts) });
            Group("props", built.Cells.SelectMany(cell => cell.Coarse.Concat(cell.Fine)).Select(p => (p.Name, p.Kit)));
            sb.Append("],\"ground\":");
            Ground(sb, c, centre, radius);
            sb.Append(",\"trees\":[");
            sb.Append(string.Join(",", plan.Trees.Where(t => Flat(t.Base - centre) < radius * 1.6f).Select(t =>
            {
                Vector3 d = t.Base - centre;
                return $"[{d.x:F1},{d.y:F1},{d.z:F1},{LandscapePlan.ModelHeight[(int)t.Kind] * t.Scale * t.Squash:F1},{LandscapePlan.ModelRadius[(int)t.Kind] * t.Scale:F1},{(int)t.Kind}]";
            })));
            sb.Append("],\"people\":[");
            var people = furniture.Marshals.Select(m => (m.feet, 0)).Concat(built.Operators.Select(o => (o.feet, 1)));
            sb.Append(string.Join(",", people.Where(p => Flat(p.feet - centre) < radius).Select(p =>
            {
                Vector3 d = p.feet - centre;
                return $"[{d.x:F2},{d.y:F2},{d.z:F2},{p.Item2}]";
            })));
            sb.Append("]}");
        }
        sb.Append('}');
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine($"wrote {views.Count} views to {path}");
    }

    static MeshKit Join(IEnumerable<MeshKit> kits)
    {
        var k = new MeshKit();
        foreach (MeshKit p in kits) k.Append(p);
        return k;
    }

    static float Flat(Vector3 v) => (float)Math.Sqrt(v.x * v.x + v.z * v.z);

    static bool Mesh(StringBuilder sb, string group, string name, MeshKit kit, Vector3 centre, float radius, bool first)
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
        sb.Append($"{{\"group\":\"{group}\",\"name\":\"{name}\",\"p\":[");
        sb.Append(string.Join(",", order.Select(i => { Vector3 p = v[i] - centre; return $"{(int)Math.Round(p.x * 100)},{(int)Math.Round(p.y * 100)},{(int)Math.Round(p.z * 100)}"; })));
        sb.Append("],\"n\":[");
        sb.Append(string.Join(",", order.Select(i => { Vector3 n = kit.Normals[i]; return $"{(int)Math.Round(n.x * 100)},{(int)Math.Round(n.y * 100)},{(int)Math.Round(n.z * 100)}"; })));
        sb.Append("],\"uv\":[");
        sb.Append(string.Join(",", order.Select(i => $"{kit.UVs[i].x:F3},{kit.UVs[i].y:F3}")));
        sb.Append("],\"i\":[");
        sb.Append(string.Join(",", idx));
        sb.Append("]}");
        return false;
    }

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
