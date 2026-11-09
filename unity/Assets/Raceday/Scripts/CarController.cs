using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// A drivable car: raycast suspension, a simple tyre model with a friction circle, engine and
    /// gearbox, brakes with anti-lock, downforce and drag. Arrows steer, accelerate and brake,
    /// A and Z shift up and down (and switch to manual), M toggles automatic and manual, H toggles steering help
    /// and R puts the car back on track. 1, 2 and 3 pick the Conserve, Normal and Push engine maps.
    /// It burns fuel and wears its tyres through <see cref="Supplies"/>, and the pit lane limiter cuts its
    /// power above <see cref="SpeedLimit"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CarController : MonoBehaviour
    {
        private const float Gravity = 9.81f;
        private const float HardHit = 18f;        // m/s into a barrier that ends your run until you reset
        private const float ShiftTime = 0.08f;

        public CarSpec Spec { get; private set; }
        public KeyboardState Keys;
        public Circuit Circuit;

        /// <summary>When false (free camera), the driving keys are ignored.</summary>
        public bool InputEnabled = true;

        /// <summary>Share of the normal drag, below 1 in another car's slipstream.</summary>
        public float DragScale = 1f;

        /// <summary>When true, an AI driver sets the pedals and steering through <see cref="SetControls"/> and the driving keys are ignored.</summary>
        public bool AiDriving;

        /// <summary>Fuel, tyres and engine map, or null to run on an endless tank and tyres.</summary>
        public Consumables Supplies;
        /// <summary>The limiter's speed while in the pit lane, m/s, otherwise infinity. Set each step.</summary>
        public float SpeedLimit = float.PositiveInfinity;
        /// <summary>True while the car is held on its brakes, stopped in its box.</summary>
        public bool Hold;
        public bool Limiting => !float.IsPositiveInfinity(SpeedLimit);

        public bool Automatic { get; private set; } = true;
        public bool SteeringHelp { get; private set; } = true;
        public bool Crashed { get; private set; }
        /// <summary>0 is reverse, 1 and up are forward gears.</summary>
        public int Gear { get; private set; } = 1;
        public float Speed { get; private set; }
        public float EngineFraction { get; private set; }
        public Vector3 Velocity => body.GetPointVelocity(body.worldCenterOfMass);
        /// <summary>Turn rate about the car's up axis, rad/s, positive turning right.</summary>
        public float YawRate => Vector3.Dot(body.angularVelocity, transform.up);

        private Rigidbody body;
        private readonly Wheel[] wheels = new Wheel[4];
        private readonly Dictionary<Collider, SurfaceKind> surfaces = new Dictionary<Collider, SurfaceKind>();
        private float throttle, brake, steerInput, steer;
        private float aiThrottle, aiBrake, aiSteer;
        private float shiftTimer, reverseTimer;
        private float gripUsed;   // sum of each grounded tyre's share of its grip used, squared, this step
        private int gripCount;

        private sealed class Wheel
        {
            public Vector3 Mount;         // top of suspension travel, car space
            public bool Front, Left;
            public Transform Visual;
            public Vector3 VisualRest;
            public float Compression, Spin;
            public bool Grounded;
        }

        public void Setup(CarSpec spec, Transform[] wheelVisuals)
        {
            Spec = spec;
            body = GetComponent<Rigidbody>();
            body.mass = spec.Mass;
            body.centerOfMass = spec.CentreOfMass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.maxAngularVelocity = 20f;

            float mountY = spec.WheelRadius + spec.Travel - spec.StaticCompression;
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2, left = i % 2 == 0;
                var w = new Wheel
                {
                    Front = front,
                    Left = left,
                    Mount = new Vector3(left ? -spec.HalfTrack : spec.HalfTrack, mountY, front ? spec.HalfWheelbase : -spec.HalfWheelbase),
                    Visual = wheelVisuals[i],
                };
                if (w.Visual != null) w.VisualRest = w.Visual.localPosition;
                wheels[i] = w;
            }
        }

        // ---------------------------------------------------------------- input

        /// <summary>
        /// Pedals from 0 to 1 and a front wheel angle in radians, positive to the right. The wheels
        /// turn towards that angle at the same rate as they do for a player.
        /// </summary>
        public void SetControls(float throttleIn, float brakeIn, float steerAngle)
        {
            aiThrottle = Mathf.Clamp01(throttleIn);
            aiBrake = Mathf.Clamp01(brakeIn);
            aiSteer = Mathf.Clamp(steerAngle, -Spec.SteerLock, Spec.SteerLock);
        }

        private void Update()
        {
            if (Spec == null || Keys == null || AiDriving) return;
            bool on = InputEnabled && !Crashed;
            float left = on && Keys.Held(KeyCode.LeftArrow) ? 1f : 0f;
            float right = on && Keys.Held(KeyCode.RightArrow) ? 1f : 0f;
            bool up = on && Keys.Held(KeyCode.UpArrow);
            bool down = on && Keys.Held(KeyCode.DownArrow);
            steerInput = right - left;

            if (InputEnabled)
            {
                if (Keys.Pressed(KeyCode.R)) ResetToTrack();
                if (Keys.Pressed(KeyCode.H)) SteeringHelp = !SteeringHelp;
                if (Keys.Pressed(KeyCode.M)) Automatic = !Automatic;
                if (Keys.Pressed(KeyCode.A)) Shift(+1);
                if (Keys.Pressed(KeyCode.Z)) Shift(-1);
                if (Supplies != null)
                {
                    if (Keys.Pressed(KeyCode.Alpha1)) Supplies.Map = EngineMap.Conserve;
                    if (Keys.Pressed(KeyCode.Alpha2)) Supplies.Map = EngineMap.Normal;
                    if (Keys.Pressed(KeyCode.Alpha3)) Supplies.Map = EngineMap.Push;
                }
            }

            // Reverse: hold brake at a standstill to engage it, press accelerate to leave it.
            float along = Vector3.Dot(Velocity, transform.forward);
            // Keys are all or nothing, so pedals move over a fraction of a second, like a foot would.
            float dt = Time.deltaTime;
            bool gas, stop;
            if (Gear == 0)
            {
                gas = down;
                stop = up;
                if (up && along > -0.5f) Gear = 1;
            }
            else
            {
                gas = up;
                stop = down;
                reverseTimer = down && !up && along < 0.5f ? reverseTimer + Time.deltaTime : 0f;
                if (reverseTimer > 0.4f) Gear = 0;
            }
            throttle = Mathf.MoveTowards(throttle, gas ? 1f : 0f, (gas ? 3f : 8f) * dt);
            brake = Mathf.MoveTowards(brake, stop ? 1f : 0f, (stop ? 6f : 10f) * dt);
            if (Crashed) brake = 1f;
        }

        private void Shift(int direction)
        {
            Automatic = false;
            int next = Mathf.Clamp(Gear + direction, 0, Spec.Gears.Length);
            if (next == 0 && Vector3.Dot(Velocity, transform.forward) > 1f) return;
            if (next != Gear) shiftTimer = ShiftTime;
            Gear = next;
        }

        // ---------------------------------------------------------------- physics

        private void FixedUpdate()
        {
            if (Spec == null) return;
            float dt = Time.fixedDeltaTime;
            if (AiDriving)
            {
                // The AI drives forwards in automatic, with its pedals set this step.
                if (Gear == 0) Gear = 1;
                Automatic = true;
                throttle = Crashed ? 0f : aiThrottle;
                brake = Crashed ? 1f : aiBrake;
            }
            if (Hold)
            {
                throttle = 0f;
                brake = 1f;
            }
            if (Supplies != null) body.mass = Spec.Mass + Supplies.FuelMass;
            Vector3 v = Velocity;
            float speed = v.magnitude;
            Speed = speed;
            float along = Vector3.Dot(v, transform.forward);

            UpdateSteering(dt, speed);
            float driveForce = Engine(dt, along);
            if (Supplies != null) Supplies.Burn(driveForce * along, dt);

            // Aerodynamics: drag at the centre of mass, downforce split 40/60 front to rear for stability.
            float q = 0.5f * CarSpec.AirDensity * speed * speed;
            if (speed > 0.1f) body.AddForce(-v / speed * (q * Spec.DragArea * DragScale));
            Vector3 down = -transform.up * (q * Spec.LiftArea);
            body.AddForceAtPosition(down * 0.4f, transform.TransformPoint(new Vector3(0, 0, Spec.HalfWheelbase)));
            body.AddForceAtPosition(down * 0.6f, transform.TransformPoint(new Vector3(0, 0, -Spec.HalfWheelbase)));

            float brakeTotal = brake * Spec.BrakeDecel * Spec.Mass * Gravity;
            gripUsed = 0f;
            gripCount = 0;
            for (int i = 0; i < 4; i++)
            {
                Wheel w = wheels[i];
                float drive = w.Front ? 0f : driveForce * 0.5f;
                float wheelBrake = brakeTotal * (w.Front ? Spec.FrontBrakeShare : 1f - Spec.FrontBrakeShare) * 0.5f;
                TyreAndSpring(w, dt, drive, wheelBrake);
            }
            if (Supplies != null && gripCount > 0) Supplies.Scrub(speed * dt, Mathf.Sqrt(gripUsed / gripCount));
        }

        private void UpdateSteering(float dt, float speed)
        {
            float limit = Spec.SteerLock;
            if (SteeringHelp && !AiDriving && speed > 5f)
            {
                // Limit lock to roughly what the tyres can use at this speed, plus the peak slip angle.
                float down = 0.5f * CarSpec.AirDensity * Spec.LiftArea * speed * speed;
                float lateral = Spec.Grip * (Spec.Mass * Gravity + down) / Spec.Mass;
                float radius = speed * speed / lateral;
                limit = Mathf.Min(limit, Mathf.Atan(2f * Spec.HalfWheelbase / radius) + Spec.PeakSlip * 0.8f);
            }
            float target = AiDriving ? Mathf.Clamp(aiSteer, -limit, limit) : steerInput * limit;
            float rate = (Mathf.Abs(target) < Mathf.Abs(steer) || target * steer < 0f ? 3f : 1.3f) * Spec.SteerLock;
            steer = Mathf.MoveTowards(steer, target, rate * dt);
        }

        /// <summary>Drive force at the rear wheels, and automatic gear changes.</summary>
        private float Engine(float dt, float along)
        {
            float wheelSpeed = Mathf.Abs(along) / Spec.WheelRadius;
            int g = Mathf.Max(Gear, 1);
            float ratio = Gear == 0 ? Spec.Gears[0] : Spec.Gears[g - 1];
            float x = wheelSpeed * ratio / Spec.Redline;
            EngineFraction = Mathf.Clamp01(x);

            if (Automatic && Gear > 0 && shiftTimer <= 0f)
            {
                if (x > 0.97f && Gear < Spec.Gears.Length && throttle > 0f) { Gear++; shiftTimer = ShiftTime; }
                else if (Gear > 1 && wheelSpeed * Spec.Gears[Gear - 2] / Spec.Redline < 0.8f) { Gear--; shiftTimer = ShiftTime; }
            }
            if (shiftTimer > 0f)
            {
                shiftTimer -= dt;
                return 0f;
            }

            float direction = Gear == 0 ? -1f : 1f;
            // The limiter cuts the power at the pit lane speed, and an empty tank cuts it for good.
            bool cut = (Supplies != null && Supplies.Empty) || along > SpeedLimit;
            if (throttle > 0f && x < 1f && !cut)
            {
                // Below a fifth of the redline the clutch slips, so the car still pulls away.
                float xe = Mathf.Max(x, 0.2f);
                float torque = Spec.PowerAt(xe) / (xe * Spec.Redline);
                float map = Supplies != null ? Supplies.PowerScale : 1f;
                return direction * throttle * torque * map * ratio / Spec.WheelRadius;
            }
            // Engine braking off the throttle.
            float drag = 0.08f * Spec.PeakPower / Spec.Redline * ratio / Spec.WheelRadius * Mathf.Clamp01(x);
            return -Mathf.Sign(along) * drag;
        }

        private void TyreAndSpring(Wheel w, float dt, float drive, float wheelBrake)
        {
            Vector3 origin = transform.TransformPoint(w.Mount);
            Vector3 up = transform.up;
            float reach = Spec.Travel + Spec.WheelRadius;
            float previous = w.Compression;
            w.Grounded = Physics.Raycast(origin, -up, out RaycastHit hit, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            w.Compression = w.Grounded ? reach - hit.distance : 0f;

            if (w.Grounded)
            {
                float load = Spec.SpringRate * w.Compression + Spec.Damping * (w.Compression - previous) / dt;
                load = Mathf.Max(0f, load);
                body.AddForceAtPosition(up * load, origin);

                SurfaceKind kind = SurfaceOf(hit.collider);
                float angle = w.Front ? steer : 0f;
                Vector3 n = hit.normal;
                Vector3 forward = Vector3.ProjectOnPlane(Quaternion.AngleAxis(angle * Mathf.Rad2Deg, up) * transform.forward, n).normalized;
                Vector3 side = Vector3.Cross(n, forward);
                Vector3 pv = body.GetPointVelocity(hit.point);
                float vLong = Vector3.Dot(pv, forward);
                float vLat = Vector3.Dot(pv, side);
                float corner = Spec.Mass / 4f;
                // Rear tyres grip a little more than the fronts, so the car leans towards understeer.
                float cap = Spec.Grip * TrackSurface.Grip(kind) * load * (w.Front ? 0.97f : 1.06f);
                if (Supplies != null) cap *= Supplies.GripScale;

                // Static grip: when nearly stopped the tyre holds against the slope it stands on,
                // up to its grip limit, instead of creeping down it.
                float upright = Mathf.Max(-Vector3.Dot(Physics.gravity, n), 1f);
                float slopeSide = -Vector3.Dot(Physics.gravity, side) / upright * load;
                float slopeAlong = -Vector3.Dot(Physics.gravity, forward) / upright * load;

                // Lateral: slip-angle curve at speed, static grip when nearly stopped.
                float fy;
                if (Mathf.Abs(vLong) < 4f && Mathf.Abs(vLat) < 4f)
                    fy = Mathf.Clamp(-vLat * corner * 8f + slopeSide, -cap, cap);
                else
                {
                    float slip = Mathf.Atan2(vLat, Mathf.Abs(vLong));
                    // Peaks at PeakSlip and falls away gently past it.
                    float b = 2.64f / Spec.PeakSlip;
                    fy = -cap * Mathf.Sin(1.3f * Mathf.Atan(b * slip));
                }

                // Longitudinal: drive, brakes (holding the car still when nearly stopped), rolling resistance.
                float fx = drive;
                bool braking = wheelBrake > 0f;
                bool holding = throttle < 0.01f && Mathf.Abs(vLong) < 0.5f;
                if (holding)
                    fx += Mathf.Clamp(-vLong * corner * 10f + slopeAlong, -cap, cap);
                else
                {
                    if (braking) fx -= Mathf.Sign(vLong) * wheelBrake;
                    fx -= Mathf.Clamp(vLong * 4f, -1f, 1f) * TrackSurface.Rolling(kind) * load;
                }

                // Friction circle. Anti-lock keeps steering under braking; wheelspin costs side grip.
                if (fx * fx + fy * fy > cap * cap)
                {
                    if (braking && drive == 0f)
                        fx = Mathf.Sign(fx) * Mathf.Sqrt(Mathf.Max(cap * cap - fy * fy, 0.09f * cap * cap));
                    else if (Mathf.Abs(fx) > cap * 0.95f)
                    {
                        fx = Mathf.Sign(fx) * cap * 0.9f;
                        fy *= 0.6f;
                    }
                    float total = Mathf.Sqrt(fx * fx + fy * fy);
                    if (total > cap)
                    {
                        fx *= cap / total;
                        fy *= cap / total;
                    }
                }
                body.AddForceAtPosition(forward * fx + side * fy, hit.point);
                if (cap > 1f)
                {
                    gripUsed += Mathf.Min((fx * fx + fy * fy) / (cap * cap), 1f);
                    gripCount++;
                }
                w.Spin += vLong / Spec.WheelRadius * dt;
            }
            else if (!w.Front && drive != 0f)
                w.Spin += Mathf.Sign(drive) * 30f * dt;
        }

        private SurfaceKind SurfaceOf(Collider c)
        {
            if (!surfaces.TryGetValue(c, out SurfaceKind kind))
            {
                TrackSurface s = c.GetComponent<TrackSurface>();
                kind = s != null ? s.Kind : SurfaceKind.Tarmac;
                surfaces[c] = kind;
            }
            return kind;
        }

        // ---------------------------------------------------------------- visuals

        private void LateUpdate()
        {
            if (Spec == null) return;
            foreach (Wheel w in wheels)
            {
                if (w.Visual == null) continue;
                float drop = Spec.StaticCompression - w.Compression;
                if (!w.Grounded) drop = Spec.StaticCompression - 0f;
                w.Visual.localPosition = w.VisualRest + Vector3.down * Mathf.Min(drop, Spec.Travel);
                float yaw = w.Front ? steer * Mathf.Rad2Deg : 0f;
                w.Visual.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(w.Spin * Mathf.Rad2Deg, 0f, 0f);
            }
        }

        // ---------------------------------------------------------------- crashes and resets

        private void OnCollisionEnter(Collision collision)
        {
            if (Crashed || SurfaceOf(collision.collider) != SurfaceKind.Barrier) return;
            float into = 0f;
            for (int i = 0; i < collision.contactCount; i++)
                into = Mathf.Max(into, Mathf.Abs(Vector3.Dot(collision.relativeVelocity, collision.GetContact(i).normal)));
            if (into < HardHit) return;
            Crashed = true;
            Stop();
        }

        private void Stop()
        {
            body.AddForce(-Velocity, ForceMode.VelocityChange);
            body.AddTorque(-body.angularVelocity, ForceMode.VelocityChange);
        }

        /// <summary>Puts the car back on the centre line at the nearest point of the track, at rest.</summary>
        public void ResetToTrack()
        {
            if (Circuit == null) return;
            Vector3 p = transform.position;
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int k = 0; k < Circuit.Count; k++)
            {
                float d = (Circuit.Centre[k] - p).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = k; }
            }
            PlaceAt(best, 0f);
        }

        /// <summary>Puts the car at rest on the track at a sample and lateral offset, facing forwards.</summary>
        public void PlaceAt(int k, float lateral)
        {
            Vector3 p = Circuit.Surface(k, lateral) + Vector3.up * 0.05f;
            Quaternion r = Quaternion.LookRotation(Circuit.Forward[k], Vector3.up);
            body.position = p;
            body.rotation = r;
            transform.SetPositionAndRotation(p, r);
            Stop();
            Crashed = false;
            Gear = 1;
            steer = 0f;
            foreach (Wheel w in wheels) w.Compression = Spec.StaticCompression;
        }
    }
}
