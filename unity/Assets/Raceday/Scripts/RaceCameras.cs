using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// C cycles the car cameras: a TV camera locked to the car, then the chase, bonnet and bumper views.
    /// B puts the broadcast on (<see cref="BroadcastDirector"/>) and B or C takes it off again. = rides
    /// with the car behind in race order and - with the car ahead, leaving the broadcast for the chase view.
    /// While you are not driving your own car, the camera is yours to move: hold the right mouse button and
    /// drag to look, W A S D move, Q and E go down and up, Shift is 5x faster and the wheel changes speed.
    /// All of it stays relative to the car, so you still ride along. The locked TV camera takes the mouse
    /// only: the drag orbits it round the car and the wheel zooms.
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
        /// <summary>Your own car: while you drive it the keys and mouse are for driving.</summary>
        public CarController Own;
        /// <summary>The cars = and - step through, if more than one.</summary>
        public CarController[] Cars;
        public DriveHud Hud;
        public KeyboardState Keys;
        /// <summary>Directs the broadcast.</summary>
        public BroadcastDirector Director;
        /// <summary>Gives the race order for = and -, and is seen from the car ridden with.</summary>
        public RaceTower Tower;

        private View view = View.Chase;
        private bool broadcast = true;

        /// <summary>Starts in the chase view instead of the broadcast, as the scene does run on its own for AI testing.</summary>
        public void StartInChase() => broadcast = false;
        /// <summary>True while the broadcast has the camera.</summary>
        public bool Broadcasting => broadcast;
        /// <summary>True in the bonnet and bumper views, riding on the car.</summary>
        public bool Onboard => !broadcast && (view == View.Bonnet || view == View.Bumper);
        /// <summary>True in the chase view.</summary>
        public bool Chasing => !broadcast && view == View.Chase;
        private Camera cam;
        private Vector3 chasePosition;
        private bool chaseSettled;
        private float carYaw;

        // Your changes to the view, in the car's frame. Put back on every switch.
        private Vector3 offset;
        private float yaw, pitch;
        private float speed = 10f;
        // The locked TV camera: its angle round the car, height angle and lens.
        private float orbit, rise, lens;

        // Mouse and Shift, read from IMGUI events so either input system works.
        private Vector2 drag;
        private float wheel;
        private bool shift;

        private void Start()
        {
            cam = GetComponent<Camera>();
            Apply();
        }

        private bool Driving => Own != null && Own.InputEnabled && !Own.AiDriving;
        private bool Steerable => !broadcast && !Driving && !RaceWeekend.Paused;

        private void Update()
        {
            if (Keys == null) return;
            if (Keys.Pressed(KeyCode.B))
            {
                broadcast = !broadcast;
                Apply();
            }
            if (Keys.Pressed(KeyCode.C))
            {
                if (broadcast) broadcast = false;
                else view = (View)(((int)view + 1) % ViewCount);
                Apply();
            }
            if (Keys.Pressed(KeyCode.Equals)) Step(+1);
            if (Keys.Pressed(KeyCode.Minus)) Step(-1);
            if (Driving && (offset != Vector3.zero || yaw != 0f || pitch != 0f)) Apply();   // taking the wheel puts the view back
        }

        private void OnGUI()
        {
            Event ev = Event.current;
            shift = ev.shift;
            if (ev.type == EventType.MouseDrag && ev.button == 1) drag += ev.delta;
            else if (ev.type == EventType.ScrollWheel) wheel += ev.delta.y;
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
            if (broadcast) { broadcast = false; view = View.Chase; }
            if (Hud != null) Hud.Car = Car;
            if (Tower != null) Tower.Player = TowerIndex(Car);
            Apply();
        }

        private void Apply()
        {
            cam.fieldOfView = view == View.Chase ? 60f : 70f;
            if (Hud != null) Hud.Hidden = false;
            chaseSettled = false;
            offset = Vector3.zero;
            yaw = pitch = 0f;
            orbit = 215f;   // behind and to the left
            rise = 9f;
            lens = 25f;
        }

        /// <summary>Takes this frame's mouse and keys into the view, if you are not driving.</summary>
        private void Steer()
        {
            Vector2 look = drag * 0.15f;
            float scroll = wheel;
            drag = Vector2.zero;
            wheel = 0f;
            if (!Steerable) return;
            if (view == View.Tv)
            {
                orbit += look.x;
                rise = Mathf.Clamp(rise + look.y, -5f, 80f);
                if (scroll != 0f) lens = Mathf.Clamp(lens * (scroll > 0 ? 1.15f : 0.87f), 8f, 70f);
                return;
            }
            yaw += look.x;
            pitch = Mathf.Clamp(pitch + look.y, -89f, 89f);
            if (scroll != 0f) speed = Mathf.Clamp(speed * (scroll < 0 ? 1.4f : 0.7f), 1f, 300f);
            Vector3 move = Vector3.zero;
            if (Keys.Held(KeyCode.W)) move += Vector3.forward;
            if (Keys.Held(KeyCode.S)) move += Vector3.back;
            if (Keys.Held(KeyCode.A)) move += Vector3.left;
            if (Keys.Held(KeyCode.D)) move += Vector3.right;
            Vector3 step = Quaternion.Euler(0f, yaw, 0f) * move;
            if (Keys.Held(KeyCode.E)) step += Vector3.up;
            if (Keys.Held(KeyCode.Q)) step += Vector3.down;
            offset += step * (speed * (shift ? 5f : 1f) * Time.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            if (Car == null) return;
            Transform t = Car.transform;
            // The broadcast while the AI drives; the HUD is for driving.
            if (Hud != null) Hud.Hidden = broadcast && Car.AiDriving;
            if (broadcast && Director != null)
            {
                drag = Vector2.zero;
                wheel = 0f;
                Director.Frame(cam);
                return;
            }
            Steer();

            // The car's heading on the ground, eased a little so bumps and slides don't shake the view.
            Vector3 flat = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            if (flat.sqrMagnitude > 1e-6f)
            {
                float want = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                carYaw = chaseSettled ? Mathf.LerpAngle(carYaw, want, 1f - Mathf.Exp(-10f * Time.deltaTime)) : want;
            }
            Quaternion ground = Quaternion.Euler(0f, carYaw, 0f);

            switch (view)
            {
                case View.Tv:
                {
                    // A long lens on the car from a fixed angle in its frame: the car holds still in the shot.
                    cam.fieldOfView = lens;
                    Quaternion at = ground * Quaternion.Euler(rise, orbit, 0f);
                    Vector3 aim = t.position + Vector3.up * 0.7f;
                    transform.SetPositionAndRotation(aim - at * Vector3.forward * 18f, at);
                    chaseSettled = true;
                    break;
                }
                case View.Chase:
                {
                    Vector3 f = ground * Vector3.forward;
                    Vector3 want = t.position - f * 7f + Vector3.up * 2.2f;
                    chasePosition = chaseSettled ? Vector3.Lerp(chasePosition, want, 1f - Mathf.Exp(-10f * Time.deltaTime)) : want;
                    chaseSettled = true;
                    Quaternion basis = Quaternion.LookRotation(t.position + Vector3.up * 0.9f + f * 4f - chasePosition, Vector3.up);
                    Ride(chasePosition, ground, Quaternion.Inverse(ground) * basis);
                    break;
                }
                case View.Bonnet:
                    chaseSettled = true;
                    Ride(t.TransformPoint(new Vector3(0f, 1.2f, 0.3f)), t.rotation, Quaternion.identity);
                    break;
                case View.Bumper:
                    chaseSettled = true;
                    Ride(t.TransformPoint(new Vector3(0f, 0.4f, 2.45f)), t.rotation, Quaternion.identity);
                    break;
            }
        }

        /// <summary>Places the camera at a view's own spot, moved and turned by your changes in the car's frame.</summary>
        private void Ride(Vector3 at, Quaternion frame, Quaternion look)
        {
            transform.SetPositionAndRotation(at + frame * offset, frame * Quaternion.Euler(pitch, yaw, 0f) * look);
        }
    }
}
