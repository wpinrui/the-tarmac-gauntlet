using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The kit at the stops in the race (<see cref="KitWork"/>): every box's kit follows its crew through each stop,
    /// but only the crews played live near the camera move it; every other box shows its kit at rest. A box near
    /// the camera takes its pieces off their rests (<see cref="PitBoxKitView.Take"/>) and puts them where the crew
    /// have them, swaps the car's own wheels for the old ones as they come off, and lifts the car on its jacks.
    /// Runs after the crews have moved and before the kit is drawn. Nothing here has a collider, holds the car or
    /// sets the stop's clock; the jacks lift only the car's model.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class PitKitWork : MonoBehaviour
    {
        private PitCrews crews;
        private PitBoxKitView view;
        private RaceCar[] cars;
        // By car: its kit's work (null with no crew), and what this box shows of it.
        private KitWork[] works;
        private bool[][] taken;
        private Renderer[][] pieceRenderers;
        private Transform[][] wheels;
        private Renderer[][][] wheelRenderers;
        private GameObject[][] oldWheels;
        private Quaternion[][] oldBase;
        private Transform[] models;
        private Vector3[] modelRest;
        private bool[] shown;
        private KitWork.HandOf[] hands;

        public void Build(PitComplex complex, PitCrews pitCrews, PitBoxKitView kitView, RaceCar[] raceCars)
        {
            crews = pitCrews;
            view = kitView;
            cars = raceCars;
            int n = cars.Length;
            var kit = new PitBoxKit(complex);
            works = new KitWork[n];
            taken = new bool[n][];
            pieceRenderers = new Renderer[n][];
            wheels = new Transform[n][];
            wheelRenderers = new Renderer[n][][];
            oldWheels = new GameObject[n][];
            oldBase = new Quaternion[n][];
            models = new Transform[n];
            modelRest = new Vector3[n];
            shown = new bool[n];
            hands = new KitWork.HandOf[n];
            for (int i = 0; i < n; i++)
            {
                GarageCrew crew = crews.Crew(i);
                if (crew == null || view == null) continue;
                works[i] = new KitWork(complex, kit, crew);
                int car = i;
                hands[i] = (person, right) => crews.Hand(car, person, right);
                taken[i] = new bool[PitBoxKit.ItemCount];
                pieceRenderers[i] = new Renderer[PitBoxKit.ItemCount];
                oldWheels[i] = new GameObject[4];
                oldBase[i] = new Quaternion[4];
                wheels[i] = new Transform[4];
                wheelRenderers[i] = new Renderer[4][];
                CarController controller = cars[i].GetComponent<CarController>();
                for (int w = 0; w < 4; w++)
                {
                    wheels[i][w] = controller != null ? controller.WheelVisual(w) : null;
                    wheelRenderers[i][w] = wheels[i][w] != null ? wheels[i][w].GetComponentsInChildren<Renderer>(true) : new Renderer[0];
                }
                models[i] = cars[i].transform.Find("Model");
                if (models[i] != null) modelRest[i] = models[i].localPosition;
            }
        }

        private void LateUpdate()
        {
            if (works == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < works.Length; i++)
            {
                KitWork work = works[i];
                if (work == null) continue;
                if (!work.Busy && work.Crew.AtRest)
                {
                    if (shown[i]) Clear(i);
                    continue;
                }
                StopClock clock = crews.ClockOf(i);
                work.Step(clock, Car(i, clock), hands[i], dt);
                if (crews.IsLive(i)) Show(i, work, clock);
                else if (shown[i]) Clear(i);
            }
        }

        /// <summary>Car i as it stands: its wheels where they are, the filler on the garage side and the socket at its tail.</summary>
        private KitCar Car(int i, in StopClock clock)
        {
            Transform t = cars[i].transform;
            GarageCrew crew = works[i].Crew;
            float length = clock.CarLength > 0f ? clock.CarLength : 4.6f, width = clock.CarWidth > 0f ? clock.CarWidth : 1.9f;
            Vector3 fwd = t.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : crew.Forward;
            KitCar car = KitCar.Standing(t.position, fwd, crew.ToGarage, length, width);
            car.Up = t.up;
            for (int w = 0; w < 4; w++)
            {
                if (wheels[i][w] == null) continue;
                Vector3 hub = wheels[i][w].position - t.up * (works[i].Lift * KitWork.JackLift);
                if (w == 0) car.Hub0 = hub;
                else if (w == 1) car.Hub1 = hub;
                else if (w == 2) car.Hub2 = hub;
                else car.Hub3 = hub;
            }
            return car;
        }

        /// <summary>Puts car i's kit where its work has it.</summary>
        private void Show(int i, KitWork work, in StopClock clock)
        {
            shown[i] = true;
            for (int k = 0; k < PitBoxKit.ItemCount; k++)
            {
                var item = (KitItem)k;
                if (!work.Out[k])
                {
                    if (taken[i][k]) view.PutBack(i, item);
                    taken[i][k] = false;
                    continue;
                }
                Transform piece = view.Take(i, item);
                if (piece == null) continue;
                if (!taken[i][k]) pieceRenderers[i][k] = piece.GetComponent<Renderer>();
                taken[i][k] = true;
                KitPlace p = work.Pieces[k];
                piece.SetPositionAndRotation(p.Position + Vector3.up * Lifted(i, work, item), Quaternion.LookRotation(p.Forward, p.Up));
                if (pieceRenderers[i][k] != null) pieceRenderers[i][k].enabled = !work.Hidden[k];
            }
            Transform car = cars[i].transform;
            for (int w = 0; w < 4; w++)
            {
                Wheel(i, w, !work.WheelOff[w]);
                if (work.OldOut[w] && wheels[i][w] != null)
                {
                    if (oldWheels[i][w] == null) Pull(i, w, car);
                    KitPlace p = work.OldWheel[w];
                    oldWheels[i][w].transform.SetPositionAndRotation(p.Position, Quaternion.LookRotation(p.Forward, p.Up) * oldBase[i][w]);
                }
                else Drop(i, w);
            }
            if (models[i] != null) models[i].localPosition = modelRest[i] + Vector3.up * (work.Lift * KitWork.JackLift);
        }

        /// <summary>A piece on the car rises with it on its jacks.</summary>
        private static float Lifted(int i, KitWork work, KitItem item)
        {
            bool onCar = item == KitItem.Nozzle && work.NozzleOn || item == KitItem.Lance && work.LanceIn
                         || item >= KitItem.TyreFrontLeft && item <= KitItem.TyreRearRight && work.Hidden[(int)item];
            for (int w = 0; w < 4; w++) onCar |= item == work.GunOf(w) && work.OnHub[w];
            return onCar ? work.Lift * KitWork.JackLift : 0f;
        }

        /// <summary>The old wheel comes off: a copy of the car's own wheel, its turn kept relative to the axle.</summary>
        private void Pull(int i, int w, Transform car)
        {
            Transform pivot = wheels[i][w];
            GameObject copy = Instantiate(pivot.gameObject, transform);
            copy.name = $"Car {i + 1} old wheel {w + 1}";
            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            Vector3 right = car.right;
            Vector3 outward = Vector3.Dot(pivot.position - car.position, right) >= 0f ? right : -right;
            oldBase[i][w] = Quaternion.Inverse(Quaternion.LookRotation(outward, car.up)) * pivot.rotation;
            oldWheels[i][w] = copy;
        }

        private void Drop(int i, int w)
        {
            if (oldWheels[i][w] == null) return;
            Destroy(oldWheels[i][w]);
            oldWheels[i][w] = null;
        }

        private void Wheel(int i, int w, bool on)
        {
            foreach (Renderer r in wheelRenderers[i][w])
                if (r != null && r.enabled != on) r.enabled = on;
        }

        /// <summary>Car i's kit back at rest, its wheels its own and the car down: far from the camera the kit does not move.</summary>
        private void Clear(int i)
        {
            shown[i] = false;
            for (int k = 0; k < PitBoxKit.ItemCount; k++)
            {
                if (taken[i][k]) view.PutBack(i, (KitItem)k);
                taken[i][k] = false;
            }
            for (int w = 0; w < 4; w++)
            {
                Wheel(i, w, true);
                Drop(i, w);
            }
            if (models[i] != null) models[i].localPosition = modelRest[i];
        }
    }
}
