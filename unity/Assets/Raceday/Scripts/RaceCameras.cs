using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// C cycles the TV view, then the chase, bonnet and bumper views of the car.
    /// The TV view is the broadcast while the AI drives the car (<see cref="BroadcastDirector"/>), and
    /// the bonnet view while you drive it. = rides with the car behind in race order and - with the car
    /// ahead, both in the chase view if the TV view was on.
    /// The timing tower is seen from the car ridden with. Riding with another car leaves your own car
    /// in your hands: only it reads the driving keys.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class RaceCameras : MonoBehaviour
    {
        private enum View { Tv, Chase, Bonnet, Bumper }
        private const int ViewCount = 4;

        /// <summary>The car the cameras ride with.</summary>
        public CarController Car;
        /// <summary>The cars = and - step through, if more than one.</summary>
        public CarController[] Cars;
        public DriveHud Hud;
        public KeyboardState Keys;
        /// <summary>Directs the TV view.</summary>
        public BroadcastDirector Director;
        /// <summary>Gives the race order for = and -, and is seen from the car ridden with.</summary>
        public RaceTower Tower;

        private View view = View.Tv;

        /// <summary>Starts in the chase view instead of the TV view, as the scene does run on its own for AI testing.</summary>
        public void StartInChase() => view = View.Chase;
        private Camera cam;
        private Vector3 chasePosition;
        private bool chaseSettled;

        private void Start()
        {
            cam = GetComponent<Camera>();
            Apply();
        }

        private void Update()
        {
            if (Keys == null) return;
            if (Keys.Pressed(KeyCode.C))
            {
                view = (View)(((int)view + 1) % ViewCount);
                Apply();
            }
            if (Keys.Pressed(KeyCode.Equals)) Step(+1);
            if (Keys.Pressed(KeyCode.Minus)) Step(-1);
        }

        /// <summary>Rides with the next car in race order: +1 the car behind, -1 the car ahead, wrapping round the field.</summary>
        private void Step(int by)
        {
            if (Cars == null || Cars.Length < 2) return;
            int at = TowerIndex(Car);
            if (Tower != null && at >= 0 && Tower.Order.Count > 1)
            {
                var order = Tower.Order;
                int from = -1;
                for (int i = 0; i < order.Count; i++) if (order[i].Id == at) from = i;
                for (int i = 1; from >= 0 && i < order.Count; i++)
                {
                    RaceCar next = Tower.Cars[order[((from + by * i) % order.Count + order.Count) % order.Count].Id];
                    if (Racing(next)) { Watch(next.GetComponent<CarController>()); return; }
                }
                return;
            }
            // Outside the race, as in a debug set-up: the cars in the order they were made.
            int k = System.Array.IndexOf(Cars, Car);
            for (int i = 1; i < Cars.Length; i++)
            {
                CarController next = Cars[((k + by * i) % Cars.Length + Cars.Length) % Cars.Length];
                if (next != null && next.gameObject.activeInHierarchy) { Watch(next); return; }
            }
        }

        /// <summary>Still in the race: retired cars are skipped.</summary>
        private static bool Racing(RaceCar car) => car != null && car.enabled && car.gameObject.activeInHierarchy && car.GetComponent<CarController>() != null;

        private int TowerIndex(CarController car)
        {
            if (Tower == null || Tower.Cars == null || car == null) return -1;
            for (int i = 0; i < Tower.Cars.Length; i++)
                if (Tower.Cars[i] != null && Tower.Cars[i].gameObject == car.gameObject) return i;
            return -1;
        }

        public void Watch(CarController next)
        {
            if (next == null) return;
            Car = next;
            if (view == View.Tv) view = View.Chase;
            if (Hud != null) Hud.Car = Car;
            if (Tower != null) Tower.Player = TowerIndex(Car);
            Apply();
        }

        private void Apply()
        {
            cam.fieldOfView = view == View.Chase ? 60f : 70f;
            if (Hud != null) Hud.Hidden = false;
            chaseSettled = false;
        }

        private void LateUpdate()
        {
            if (Car == null) return;
            Transform t = Car.transform;
            bool tv = view == View.Tv;
            // The broadcast while the AI drives; the HUD is for driving.
            if (Hud != null) Hud.Hidden = tv && Car.AiDriving;
            if (tv && Car.AiDriving && Director != null)
            {
                Director.Frame(cam);
                return;
            }
            if (tv) cam.fieldOfView = 70f;
            switch (tv ? View.Bonnet : view)
            {
                case View.Chase:
                {
                    Vector3 flat = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
                    Vector3 want = t.position - flat * 7f + Vector3.up * 2.2f;
                    chasePosition = chaseSettled ? Vector3.Lerp(chasePosition, want, 1f - Mathf.Exp(-10f * Time.deltaTime)) : want;
                    chaseSettled = true;
                    transform.position = chasePosition;
                    transform.rotation = Quaternion.LookRotation(t.position + Vector3.up * 0.9f + flat * 4f - chasePosition, Vector3.up);
                    break;
                }
                case View.Bonnet:
                    transform.SetPositionAndRotation(t.TransformPoint(new Vector3(0f, 1.2f, 0.3f)), t.rotation);
                    break;
                case View.Bumper:
                    transform.SetPositionAndRotation(t.TransformPoint(new Vector3(0f, 0.4f, 2.45f)), t.rotation);
                    break;
            }
        }
    }
}
