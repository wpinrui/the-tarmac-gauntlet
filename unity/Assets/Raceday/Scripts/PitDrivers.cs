using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.TeamHq;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The race drivers of every box whose car has more than one: the crew's body in a race suit, without its cap and
    /// glasses, with a racing helmet on (tools/pit-crew/helmet.py). They change over at the stops in step with the stop's
    /// clock (<see cref="DriverSwap"/>). Drawn the crew's way: still figures far off, live rigs in the boxes played
    /// live, and nobody while the car is away.
    /// </summary>
    public sealed partial class PitCrews
    {
        /// <summary>How the race suits and helmets take the team's colours.</summary>
        public enum SuitLook { Team, White, Black }

        /// <summary>The look the drivers wear.</summary>
        public const SuitLook Suit = SuitLook.Team;
        /// <summary>The cars' driver's doors are on their left (slice F's door_l).</summary>
        private const bool DoorOnLeft = true;
        /// <summary>Height of the hips on a racing car's seat, m above the ground.</summary>
        private const float SeatHips = 0.36f;
        private static readonly Color SuitWhite = new Color(0.93f, 0.93f, 0.92f, 1f), SuitBlack = new Color(0.1f, 0.105f, 0.115f, 1f);
        private static readonly int ColourId = Lit.BaseColorId;

        private enum DriverPose { Standing, Walking, Seated }

        private sealed class LiveDriver
        {
            public HqCrewRig Rig;
            public SkinnedMeshRenderer Skin;
            public Renderer Helmet;
            public Transform Hips;
        }

        // By car: its box's driver change (null with one driver), still figures and live rigs.
        private DriverSwap[] swaps;
        private MeshFilter[][] driverFigures;
        private Renderer[][] driverRenderers;
        private LiveDriver[][] liveDrivers;
        /// <summary>Each car's opening doors, and how far open the change last set them.</summary>
        private CarDoors[] carDoors;
        private float[] doorSet;
        private MaterialPropertyBlock[] suits, helmets;
        private readonly Stack<LiveDriver> driverPool = new Stack<LiveDriver>();
        private readonly Mesh[] driverStills = new Mesh[3];
        /// <summary>Each still driver's hips in its own frame, to sit them on the seat.</summary>
        private readonly Vector3[] stillHips = new Vector3[3];
        private Mesh driverBody, helmetMesh;
        private Material[] driverMaterials, helmetMaterials;
        /// <summary>The hips' height at the end of the sitting clip, over the root: the seat drops the root by the rest.</summary>
        private float sitHips = 0.5f;
        private int driversBuilt;

        /// <summary>Gives a driver change to every box whose car has more than one driver.</summary>
        private void BuildDrivers(PitComplex complex, PitStopSpots spots, Color[] colours)
        {
            int n = cars.Length;
            swaps = new DriverSwap[n];
            driverFigures = new MeshFilter[n][];
            driverRenderers = new Renderer[n][];
            liveDrivers = new LiveDriver[n][];
            suits = new MaterialPropertyBlock[n];
            helmets = new MaterialPropertyBlock[n];
            carDoors = new CarDoors[n];
            doorSet = new float[n];
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                int g = cars[i].Garage;
                if (g < 0 || (controllers[i].Supplies?.Log?.Car.Lineup.Count ?? 0) < 2) continue;
                if (!any)
                {
                    DriverLooks();
                    any = true;
                }
                swaps[i] = new DriverSwap(complex, spots, g, DoorOnLeft, 3000 + g);
                if (crews[i] != null) crews[i].DoorSide = swaps[i].DoorSide;
                (suits[i], helmets[i]) = DriverKit(colours[i]);
                carDoors[i] = cars[i].GetComponentInChildren<CarDoors>();
                DriverFigures(i);
            }
        }

        /// <summary>The driver's body, helmet, materials and still figures: standing, mid stride and seated.</summary>
        private void DriverLooks()
        {
            helmetMesh = Helmet();
            var shell = new Material(Lit.Shader) { name = "Helmet shell", enableInstancing = true };
            shell.SetFloat("_Smoothness", 0.82f);
            shell.SetFloat("_Metallic", 0.05f);
            var visor = new Material(Lit.Shader) { name = "Helmet visor", color = new Color(0.03f, 0.035f, 0.045f), enableInstancing = true };
            visor.SetFloat("_Smoothness", 0.95f);
            visor.SetFloat("_Metallic", 0.6f);
            var lip = new Material(Lit.Shader) { name = "Helmet lip", color = new Color(0.06f, 0.06f, 0.065f), enableInstancing = true };
            lip.SetFloat("_Smoothness", 0.15f);
            helmetMaterials = new[] { shell, visor, lip };
            driverMaterials = new Material[materials.Length + 3];
            materials.CopyTo(driverMaterials, 0);
            helmetMaterials.CopyTo(driverMaterials, materials.Length);

            HqCrewRig rig = body.Build(transform, 0, true);
            var skin = rig.Root.GetComponentInChildren<SkinnedMeshRenderer>();
            // The body without its cap and glasses: the helmet goes on instead.
            driverBody = Instantiate(skin.sharedMesh);
            driverBody.name = "Driver body";
            for (int s = 0; s < driverBody.subMeshCount; s++)
            {
                string name = body.MaterialName(s);
                if (name.Contains("Headwear") || name.Contains("Glasses")) driverBody.SetTriangles(new int[0], s);
            }
            skin.sharedMesh = driverBody;
            Transform root = rig.Root.transform, head = rig.Find("Head"), hips = rig.Find("Hips");
            (string clip, float time)[] poses = { ("Idle", 0.8f), ("Walk", 0.24f), ("Sit", DriverSwap.SitClip) };
            for (int p = 0; p < poses.Length; p++)
            {
                layers.Clear();
                layers.Add((body.Clips[poses[p].clip], poses[p].time, 1f));
                rig.Pose(layers);
                var baked = new Mesh();
                skin.BakeMesh(baked, true);
                // The body's parts, then the helmet's three on the head as it is in this pose.
                var parts = new CombineInstance[baked.subMeshCount + 3];
                for (int s = 0; s < baked.subMeshCount; s++) parts[s] = new CombineInstance { mesh = baked, subMeshIndex = s, transform = Matrix4x4.identity };
                Matrix4x4 onHead = root.worldToLocalMatrix * head.localToWorldMatrix;
                for (int s = 0; s < 3; s++) parts[baked.subMeshCount + s] = new CombineInstance { mesh = helmetMesh, subMeshIndex = s, transform = onHead };
                var still = new Mesh { name = "Driver still " + poses[p].clip, indexFormat = IndexFormat.UInt32 };
                still.CombineMeshes(parts, false, true);
                still.RecalculateBounds();
                driverStills[p] = still;
                stillHips[p] = root.InverseTransformPoint(hips.position);
                Destroy(baked);
            }
            sitHips = stillHips[(int)DriverPose.Seated].y;
            Destroy(rig.Root);
        }

        /// <summary>The helmet from its packed file: shell, visor and lip.</summary>
        private static Mesh Helmet()
        {
            var file = Resources.Load<TextAsset>("PitCrew/helmet");
            var mesh = new Mesh { name = "Helmet" };
            if (file == null) return mesh;
            HelmetFile h = JsonUtility.FromJson<HelmetFile>(file.text);
            var v = new Vector3[h.positions.Length / 3];
            for (int i = 0; i < v.Length; i++) v[i] = new Vector3(h.positions[i * 3], h.positions[i * 3 + 1], h.positions[i * 3 + 2]);
            mesh.vertices = v;
            mesh.subMeshCount = 3;
            mesh.SetTriangles(h.shell, 0);
            mesh.SetTriangles(h.visor, 1);
            mesh.SetTriangles(h.lip, 2);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

#pragma warning disable 0649
        [System.Serializable]
        private sealed class HelmetFile
        {
            public float[] positions;
            public int[] shell, visor, lip;
        }
#pragma warning restore 0649

        /// <summary>The suit's and helmet's colours for a team, per the chosen look.</summary>
        private static (MaterialPropertyBlock suit, MaterialPropertyBlock helmet) DriverKit(Color team)
        {
            team.a = 1f;
            (Color cloth, Color panel, Color shell) = Suit switch
            {
                SuitLook.White => (SuitWhite, team, SuitWhite),
                SuitLook.Black => (SuitBlack, team, SuitBlack),
                _ => (team, SuitWhite, team),
            };
            var suit = new MaterialPropertyBlock();
            suit.SetColor(ClothId, cloth);
            suit.SetColor(PanelId, panel);
            var helmet = new MaterialPropertyBlock();
            helmet.SetColor(ColourId, shell);
            return (suit, helmet);
        }

        /// <summary>Paints a driver's renderer: the suit's parts, and the helmet's shell at submesh shellAt.</summary>
        private void DressDriver(Renderer r, int car, int shellAt)
        {
            Paint(r, suits[car]);
            r.SetPropertyBlock(helmets[car], shellAt);
        }

        /// <summary>Car i's two still drivers, under one LOD group that drops them when small.</summary>
        private void DriverFigures(int i)
        {
            DriverSwap swap = swaps[i];
            var group = new GameObject("Garage " + (swap.Frame.Garage + 1) + " drivers");
            group.transform.SetParent(transform, false);
            driverFigures[i] = new MeshFilter[2];
            driverRenderers[i] = new Renderer[2];
            liveDrivers[i] = new LiveDriver[2];
            for (int k = 0; k < 2; k++)
            {
                var go = new GameObject("Driver " + (k + 1));
                go.transform.SetParent(group.transform, false);
                driverFigures[i][k] = go.AddComponent<MeshFilter>();
                driverFigures[i][k].sharedMesh = driverStills[0];
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = driverMaterials;
                r.shadowCastingMode = ShadowCastingMode.On;
                DressDriver(r, i, materials.Length);
                driverRenderers[i][k] = r;
            }
            LODGroup lod = group.AddComponent<LODGroup>();
            lod.localReferencePoint = swap.Frame.World(new Vector2(0f, swap.Frame.Door));
            lod.size = CrewZone;
            lod.SetLODs(new[] { new LOD(Cull, driverRenderers[i]) });
            for (int k = 0; k < 2; k++) StillDriver(i, k);
        }

        // ---------------------------------------------------------------- each frame

        /// <summary>Moves car i's drivers on with the stop's clock and draws them where they are.</summary>
        private void StepDrivers(int i, in StopClock clock, float dt)
        {
            DriverSwap swap = swaps[i];
            bool wasResting = swap.AtRest;
            swap.Step(clock, dt);
            Swing(i, swap.Door);
            if (wasResting && swap.AtRest && liveDrivers[i][0] == null) return;
            for (int k = 0; k < 2; k++)
            {
                LiveDriver l = liveDrivers[i][k];
                if (l != null) LiveDriverPose(i, k, l);
                else StillDriver(i, k);
            }
        }

        /// <summary>Swings car i's driver's door to how far open the change wants it.</summary>
        private void Swing(int car, float openness)
        {
            CarDoors d = carDoors[car];
            if (d == null || !d.HasDoors || openness == doorSet[car]) return;
            doorSet[car] = openness;
            d.Snap(openness);
        }

        private static DriverPose PoseFor(DriverFigure d) => d.Lift > 0.5f ? DriverPose.Seated : d.Body.Clip == "Walk" ? DriverPose.Walking : DriverPose.Standing;

        private void StillDriver(int i, int k)
        {
            DriverFigure d = swaps[i].Figures[k];
            Renderer r = driverRenderers[i][k];
            r.enabled = d.Shown;
            if (!d.Shown) return;
            DriverPose pose = PoseFor(d);
            driverFigures[i][k].sharedMesh = driverStills[(int)pose];
            Transform t = driverFigures[i][k].transform;
            PlaceDriver(t, i, d);
            SitDown(t, d, t.TransformPoint(stillHips[(int)pose]));
        }

        private void LiveDriverPose(int i, int k, LiveDriver l)
        {
            DriverFigure d = swaps[i].Figures[k];
            l.Rig.Root.SetActive(d.Shown);
            if (!d.Shown) return;
            Transform t = l.Rig.Root.transform;
            PlaceDriver(t, i, d);
            Pose(l.Rig, d.Body);
            SitDown(t, d, l.Hips.position);
        }

        /// <summary>Sets a driver's root where they stand, or on the seat of the car they ride in, facing their way.</summary>
        private void PlaceDriver(Transform t, int i, DriverFigure d)
        {
            if (d.InCar)
            {
                Transform car = cars[i].transform;
                Vector3 seat = car.TransformPoint(new Vector3(DoorOnLeft ? -DriverSwap.SeatOut : DriverSwap.SeatOut, 0f, -DriverSwap.SeatBack));
                t.SetPositionAndRotation(seat, Quaternion.LookRotation(-car.forward, car.up));
                return;
            }
            GarageCrew frame = swaps[i].Frame;
            t.SetPositionAndRotation(frame.World(d.Body.At), Quaternion.LookRotation(-frame.WorldDirection(d.Body.Facing), Vector3.up));
        }

        /// <summary>As a driver sits down, moves the root so the hips come over their point and down onto the seat's height.</summary>
        private void SitDown(Transform t, DriverFigure d, Vector3 hips)
        {
            if (d.Lift <= 0f) return;
            Vector3 shift = t.position - hips;
            shift.y = 0f;
            t.position += shift * d.Lift + t.up * ((SeatHips - sitHips) * d.Lift);
        }

        /// <summary>Poses a live rig from a figure's clip, the last one fading out under it.</summary>
        private void Pose(HqCrewRig rig, CrewMember m)
        {
            layers.Clear();
            if (m.UnderWeight > 0f && m.Under != null) layers.Add((body.Clips[m.Under], ClipTime(m.Under, m.UnderTime, m.UnderLoop), m.UnderWeight));
            layers.Add((body.Clips[m.Clip], ClipTime(m.Clip, m.ClipTime, m.Loop), 1f - m.UnderWeight));
            rig.Pose(layers);
        }

        // ---------------------------------------------------------------- live rigs

        /// <summary>People car i's drivers add to its box while shown.</summary>
        private int DriversShown(int i)
        {
            if (swaps?[i] == null) return 0;
            int n = 0;
            foreach (DriverFigure d in swaps[i].Figures) if (d.Shown) n++;
            return n;
        }

        /// <summary>Car i's drivers turn live: each takes a rig from the pool, and their still figure hides.</summary>
        private void GoDrivers(int i)
        {
            if (swaps?[i] == null || liveDrivers[i][0] != null) return;
            for (int k = 0; k < 2; k++)
            {
                LiveDriver l = driverPool.Count > 0 ? driverPool.Pop() : NewDriverRig();
                Paint(l.Skin, suits[i]);
                l.Helmet.SetPropertyBlock(helmets[i], 0);
                liveDrivers[i][k] = l;
                driverRenderers[i][k].enabled = false;
                LiveDriverPose(i, k, l);
            }
        }

        /// <summary>Car i's drivers go back to still figures, their rigs back to the pool.</summary>
        private void FreezeDrivers(int i)
        {
            if (swaps?[i] == null || liveDrivers[i][0] == null) return;
            for (int k = 0; k < 2; k++)
            {
                LiveDriver l = liveDrivers[i][k];
                liveDrivers[i][k] = null;
                l.Rig.Root.SetActive(false);
                driverPool.Push(l);
                StillDriver(i, k);
            }
        }

        private LiveDriver NewDriverRig()
        {
            HqCrewRig rig = body.Build(transform, 0, true);
            rig.Root.name = "Live driver " + ++driversBuilt;
            var skin = rig.Root.GetComponentInChildren<SkinnedMeshRenderer>();
            skin.sharedMesh = driverBody;
            skin.sharedMaterials = materials;
            var helmet = new GameObject("Helmet");
            helmet.transform.SetParent(rig.Find("Head"), false);
            helmet.AddComponent<MeshFilter>().sharedMesh = helmetMesh;
            var r = helmet.AddComponent<MeshRenderer>();
            r.sharedMaterials = helmetMaterials;
            r.shadowCastingMode = ShadowCastingMode.On;
            return new LiveDriver { Rig = rig, Skin = skin, Helmet = r, Hips = rig.Find("Hips") };
        }
    }
}
