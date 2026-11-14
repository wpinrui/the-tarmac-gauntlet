using TarmacGauntlet.Simulation;
using TarmacGauntlet.Visuals;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// C cycles the car cameras: TV coverage locked to the car (a second <see cref="BroadcastDirector"/> with its
    /// Lock set, cutting between trackside shots and onboards), then the chase, bonnet and bumper views, whose
    /// places come from <see cref="CameraPresets"/> in the settings.
    /// The broadcast (<see cref="BroadcastDirector"/>) has no way in for now: its V toggle is off while Ivan rethinks
    /// the mode. Do not remove the broadcast mode or its director. It stays in the game, to come back. Holding B
    /// looks back on every camera, the broadcast showing the chase view turned round. = rides
    /// with the car behind in race order and - with the car ahead, leaving the broadcast for the chase view.
    /// Backspace comes back to your own car, in the chase view.
    /// While you are not driving your own car, the camera is yours to move: hold the right mouse button and
    /// drag to look, W A S D move, Q and E go down and up, Shift is 5x faster and the wheel changes speed.
    /// All of it stays relative to the car, so you still ride along. The locked TV camera takes the mouse
    /// only: within each shot the drag orbits it round the car and the wheel zooms.
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
        /// <summary>Directs the TV camera locked to the car ridden with.</summary>
        public BroadcastDirector Locked;
        /// <summary>Gives the race order for = and -, and is seen from the car ridden with.</summary>
        public RaceTower Tower;
        /// <summary>True while a stage before the green or after the flag places the camera itself, later in the frame (<see cref="SkippableStage"/>).</summary>
        [System.NonSerialized] public bool Held;

        private View view = View.Chase;
        /// <summary>
        /// True while the broadcast has the camera. Off from the start and with no key to turn it on while the mode is
        /// rethought. Keep the mode: it comes back.
        /// </summary>
        private bool broadcast;

        /// <summary>Starts in the chase view instead of the broadcast, as the scene does run on its own for AI testing.</summary>
        public void StartInChase() => broadcast = false;
        /// <summary>True while the broadcast has the camera.</summary>
        public bool Broadcasting => broadcast;
        /// <summary>True in the bonnet and bumper views, riding on the car.</summary>
        public bool Onboard => !broadcast && (view == View.Bonnet || view == View.Bumper);
        /// <summary>True while B is held to look back.</summary>
        public bool LookingBack => lookedBack;
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
        // The locked TV camera: your turn round the car and zoom within the shot, and the shot as the director placed it.
        private float orbit, rise, zoom = 1f;
        private int lockedShot = -1;
        private Vector3 shotPosition;
        private Quaternion shotRotation;
        private bool shotHeld;

        // Mouse and Shift, read from IMGUI events so either input system works.
        private Vector2 drag;
        private float wheel;
        private bool shift;
        private bool lookedBack;

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
            if (Keys.Pressed(KeyCode.C))
            {
                if (broadcast) broadcast = false;
                else view = (View)(((int)view + 1) % ViewCount);
                Apply();
            }
            if (Keys.Pressed(KeyCode.Equals)) Step(+1);
            if (Keys.Pressed(KeyCode.Minus)) Step(-1);
            if (Keys.Pressed(KeyCode.Backspace) && Own != null && (Car != Own || broadcast)) Watch(Own);
            if (Driving && (offset != Vector3.zero || yaw != 0f || pitch != 0f || orbit != 0f || rise != 0f || zoom != 1f)) Apply();   // taking the wheel puts the view back
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

        /// <summary>The cars were moved, as at a cut in the start: the chase view starts afresh where its car is now, with no swing.</summary>
        public void Cut() => chaseSettled = false;

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
            orbit = rise = 0f;
            zoom = 1f;
            shotHeld = false;
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
                rise = Mathf.Clamp(rise + look.y, -80f, 80f);
                if (scroll != 0f) zoom = Mathf.Clamp(zoom * (scroll > 0 ? 1.15f : 0.87f), 0.15f, 3f);
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
            if (Held || Car == null) return;
            Transform t = Car.transform;
            if (CameraPresets.Preview != null)
            {
                // Tuning a camera in the pause menu: exactly that preset, snapped, as the race stands still.
                Preview(t, CameraPresets.Preview);
                return;
            }
            bool back = Keys != null && Keys.Held(KeyCode.B);
            if (back != lookedBack) chaseSettled = false;   // cut straight round, no swing
            lookedBack = back;
            bool director = broadcast && Director != null && !back;
            bool locked = !broadcast && !back && view == View.Tv && Locked != null && LockIndex() >= 0;
            // The broadcast while the AI drives; the HUD is for driving.
            if (Hud != null) Hud.Hidden = director && Car.AiDriving;
            if (director)
            {
                drag = Vector2.zero;
                wheel = 0f;
                Director.Frame(cam);
                return;
            }
            Steer();
            if (locked)
            {
                FrameLocked();
                return;
            }
            shotHeld = false;

            // The car's heading on the ground, eased a little so bumps and slides don't shake the view.
            Vector3 flat = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            if (flat.sqrMagnitude > 1e-6f)
            {
                float want = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                carYaw = chaseSettled ? Mathf.LerpAngle(carYaw, want, 1f - Mathf.Exp(-10f * Time.deltaTime)) : want;
            }
            // Looking back turns the whole view round the car.
            Quaternion round = Quaternion.Euler(0f, back ? 180f : 0f, 0f);
            Quaternion ground = Quaternion.Euler(0f, carYaw, 0f) * round;
            // The broadcast and the TV camera turn round to the chase view behind the car.
            View shown = broadcast || view == View.Tv ? View.Chase : view;
            CameraPreset preset = shown == View.Bonnet ? CameraPresets.Bonnet : shown == View.Bumper ? CameraPresets.Bumper : CameraPresets.Chase;
            CameraPresets.InUse = broadcast || view == View.Tv ? null : preset;
            cam.fieldOfView = preset.Fov;
            Vector3 place = Place(preset, t);
            Quaternion tilt = Quaternion.Euler(-preset.Tilt, 0f, 0f);
            if (shown == View.Chase)
            {
                // On the car's eased heading, and eased into place, so the car moves a little in the frame.
                Vector3 want = t.position + ground * place;
                chasePosition = chaseSettled ? Vector3.Lerp(chasePosition, want, 1f - Mathf.Exp(-10f * Time.deltaTime)) : want;
                chaseSettled = true;
                Ride(chasePosition, ground, tilt);
                return;
            }
            chaseSettled = true;
            // Looking back moves the onboard cameras to the tail, so they see the road behind and not the car:
            // the bonnet camera to the top of the tail, the bumper camera to the back bumper.
            if (back && shown == View.Bonnet) place = CarCameraSpots.Rear(t);
            if (back && shown == View.Bumper) place.z = CarCameraSpots.TailZ(t) + 0.05f;
            Ride(t.TransformPoint(place), t.rotation * round, tilt);
        }

        private void Preview(Transform t, CameraPreset preset)
        {
            if (Hud != null) Hud.Hidden = false;
            cam.fieldOfView = preset.Fov;
            Quaternion tilt = Quaternion.Euler(-preset.Tilt, 0f, 0f);
            if (preset == CameraPresets.Chase)
            {
                Vector3 flat = Vector3.ProjectOnPlane(t.forward, Vector3.up);
                Quaternion ground = Quaternion.Euler(0f, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, 0f);
                transform.SetPositionAndRotation(t.position + ground * Place(preset, t), ground * tilt);
            }
            else transform.SetPositionAndRotation(t.TransformPoint(Place(preset, t)), t.rotation * tilt);
            chaseSettled = false;   // the chase view starts afresh once the race runs on
            shotHeld = false;
        }

        /// <summary>A preset's place on the car: the bonnet camera's is moved from the car's own bonnet spot.</summary>
        private static Vector3 Place(CameraPreset preset, Transform car) =>
            preset == CameraPresets.Bonnet ? CarCameraSpots.Bonnet(car) + preset.Position : preset.Position;

        /// <summary>The car ridden with in the locked director's list, or -1.</summary>
        private int LockIndex()
        {
            if (Locked.Cars == null || Car == null) return -1;
            for (int i = 0; i < Locked.Cars.Length; i++)
                if (Locked.Cars[i] != null && Locked.Cars[i].gameObject == Car.gameObject) return i;
            return -1;
        }

        /// <summary>
        /// The locked TV camera: the director places the shot, then your orbit and zoom go on top. The director's own
        /// pose is put back first each frame so its smooth aim doesn't follow your turn. Every cut starts from its own shot.
        /// </summary>
        private void FrameLocked()
        {
            Locked.Lock = LockIndex();
            if (shotHeld) transform.SetPositionAndRotation(shotPosition, shotRotation);
            Locked.Frame(cam);
            shotPosition = transform.position;
            shotRotation = transform.rotation;
            shotHeld = true;
            if (Locked.Shots != lockedShot)
            {
                lockedShot = Locked.Shots;
                orbit = rise = 0f;
                zoom = 1f;
            }
            cam.fieldOfView = Mathf.Clamp(cam.fieldOfView / zoom, 2f, 120f);
            if (orbit == 0f && rise == 0f) return;
            Vector3 centre = Locked.Centre;
            Quaternion turn = Quaternion.AngleAxis(orbit, Vector3.up) * Quaternion.AngleAxis(rise, shotRotation * Vector3.right);
            transform.SetPositionAndRotation(centre + turn * (shotPosition - centre), turn * shotRotation);
        }

        /// <summary>Places the camera at a view's own spot, moved and turned by your changes in the car's frame.</summary>
        private void Ride(Vector3 at, Quaternion frame, Quaternion look)
        {
            transform.SetPositionAndRotation(at + frame * offset, frame * Quaternion.Euler(pitch, yaw, 0f) * look);
        }
    }
}
