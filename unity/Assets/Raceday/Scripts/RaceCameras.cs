using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// C cycles the TV view, then the chase, bonnet and bumper views of the car, then the free camera.
    /// The TV view is the broadcast while the AI drives your car (<see cref="BroadcastDirector"/>), and
    /// the bonnet view while you drive it. V moves to the next car, and P to the next car making a pit
    /// stop, both in the chase view if the TV view was on.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class RaceCameras : MonoBehaviour
    {
        private enum View { Tv, Chase, Bonnet, Bumper, Free }

        public CarController Car;
        /// <summary>The cars V steps through, if more than one.</summary>
        public CarController[] Cars;
        public DriveHud Hud;
        public KeyboardState Keys;
        /// <summary>Directs the TV view.</summary>
        public BroadcastDirector Director;

        private View view = View.Tv;

        /// <summary>Starts in the chase view instead of the TV view, as the scene does run on its own for AI testing.</summary>
        public void StartInChase() => view = View.Chase;
        private Camera cam;
        private FreeCamera free;
        private Vector3 chasePosition;
        private bool chaseSettled;

        private void Start()
        {
            cam = GetComponent<Camera>();
            free = GetComponent<FreeCamera>();
            Apply();
        }

        private void Update()
        {
            if (Keys != null && Keys.Pressed(KeyCode.C))
            {
                view = (View)(((int)view + 1) % 5);
                Apply();
            }
            if (Keys != null && Keys.Pressed(KeyCode.V) && Cars != null && Cars.Length > 1)
            {
                int from = System.Array.IndexOf(Cars, Car);
                for (int i = 1; i < Cars.Length; i++)
                {
                    CarController next = Cars[(from + i) % Cars.Length];
                    if (next.gameObject.activeInHierarchy) { Watch(next); break; }   // retired cars are skipped
                }
            }
            if (Keys != null && Keys.Pressed(KeyCode.P) && Cars != null)
            {
                int from = System.Array.IndexOf(Cars, Car);
                for (int i = 1; i <= Cars.Length; i++)
                {
                    CarController next = Cars[(from + i + Cars.Length) % Cars.Length];
                    AiDriver d = next.GetComponent<AiDriver>();
                    if (d != null && d.isActiveAndEnabled && d.Pitting) { Watch(next); break; }
                }
            }
        }

        public void Watch(CarController next)
        {
            // Only the car being watched shows its speed and takes the driving keys.
            if (Car != null) Car.InputEnabled = false;
            Car = next;
            if (view == View.Tv) view = View.Chase;
            if (Hud != null) Hud.Car = Car;
            Apply();
        }

        private void Apply()
        {
            bool isFree = view == View.Free;
            if (free != null) free.enabled = isFree;
            if (Car != null) Car.InputEnabled = !isFree;
            cam.fieldOfView = view == View.Chase ? 60f : 70f;
            if (Hud != null) Hud.Hidden = false;
            chaseSettled = false;
        }

        private void LateUpdate()
        {
            if (Car == null || view == View.Free) return;
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
