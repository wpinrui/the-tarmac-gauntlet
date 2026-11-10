using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// C cycles the chase, bonnet and bumper views of the car, then the free camera. V moves to the
    /// next car, and P to the next car making a pit stop.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class RaceCameras : MonoBehaviour
    {
        private enum View { Chase, Bonnet, Bumper, Free }

        public CarController Car;
        /// <summary>The cars V steps through, if more than one.</summary>
        public CarController[] Cars;
        public DriveHud Hud;
        public KeyboardState Keys;

        private View view = View.Chase;
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
                view = (View)(((int)view + 1) % 4);
                Apply();
            }
            if (Keys != null && Keys.Pressed(KeyCode.V) && Cars != null && Cars.Length > 1)
                Watch(Cars[(System.Array.IndexOf(Cars, Car) + 1) % Cars.Length]);
            if (Keys != null && Keys.Pressed(KeyCode.P) && Cars != null)
            {
                int from = System.Array.IndexOf(Cars, Car);
                for (int i = 1; i <= Cars.Length; i++)
                {
                    CarController next = Cars[(from + i + Cars.Length) % Cars.Length];
                    AiDriver d = next.GetComponent<AiDriver>();
                    if (d != null && d.enabled && d.Pitting) { Watch(next); break; }
                }
            }
        }

        public void Watch(CarController next)
        {
            // Only the car being watched shows its speed and takes the driving keys.
            if (Car != null) Car.InputEnabled = false;
            Car = next;
            if (Hud != null) Hud.Car = Car;
            Apply();
        }

        private void Apply()
        {
            bool isFree = view == View.Free;
            if (free != null) free.enabled = isFree;
            if (Car != null) Car.InputEnabled = !isFree;
            cam.fieldOfView = view == View.Chase ? 60f : 70f;
            chaseSettled = false;
        }

        private void LateUpdate()
        {
            if (Car == null || view == View.Free) return;
            Transform t = Car.transform;
            switch (view)
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
