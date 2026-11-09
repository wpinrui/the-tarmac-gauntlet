using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>C cycles the chase, bonnet and bumper views of the car, then the free camera.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class RaceCameras : MonoBehaviour
    {
        private enum View { Chase, Bonnet, Bumper, Free }

        public CarController Car;
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
