// A flat stand-in for CarController: the same engine, gearbox, steering rate, brakes, aero and tyre
// formulas, on a rigid body that moves across the ground plane. Suspension is replaced by load
// transfer worked out from the car's acceleration, and height only enters through the hill and dips
// along the racing line. It exists to check the AI's control loop, not to predict exact lap times.
using System;
using TarmacGauntlet.Raceday;
using UnityEngine;

sealed class StandInCar
{
    const float G = 9.81f;
    const float ShiftTime = 0.08f;

    public readonly CarSpec Spec;
    readonly RacingLine line;
    readonly Circuit circuit;

    public float X, Z, Heading;      // heading: radians clockwise from north (+z), like Unity's yaw
    public float Vx, Vz, YawRate;    // yaw rate positive turning right
    public int Gear = 1;
    public int TrackIndex = -1;
    public float Lateral;            // car centre from the track centre, positive left
    public SurfaceKind Surface;
    /// <summary>Share of the normal drag, below 1 in another car's slipstream.</summary>
    public float DragScale = 1f;
    /// <summary>Fuel, tyres and engine map, as CarController's, or null for endless.</summary>
    public Consumables Supplies;
    /// <summary>The pit lane limiter's speed while in the limiter zone, as CarController's.</summary>
    public float SpeedLimit = float.PositiveInfinity;
    public bool Hold;
    /// <summary>In reverse gear, as CarController's gear 0 for the AI: engaged once the car is all but stopped.</summary>
    public bool Reverse;
    /// <summary>Height of the car's up direction: 1 on its wheels, below Rejoin.FlippedUp on its roof or side. Set by a check; the stand-in never flips itself.</summary>
    public float UpY = 1f;
    /// <summary>The pit lane, so its surface counts as tarmac.</summary>
    public PitLane Pits;

    float steer, shiftTimer, ax, ay;

    struct WheelState
    {
        public float A, B, Load, Cap, Fy, VLong, Rolling, Fx, Fz, Rx, Rz;
        public SurfaceKind Kind;
    }
    readonly WheelState[] wheels = new WheelState[4];
    readonly float inertia;

    public StandInCar(CarSpec spec, RacingLine line)
    {
        Spec = spec;
        this.line = line;
        circuit = line.Circuit;
        inertia = spec.Mass * (1.9f * 1.9f + 4.6f * 4.6f) / 12f;   // Unity's tensor for the car's box collider
    }

    public Vector3 Forward => new Vector3((float)Math.Sin(Heading), 0f, (float)Math.Cos(Heading));
    public Vector3 Right => new Vector3((float)Math.Cos(Heading), 0f, -(float)Math.Sin(Heading));
    public Vector3 Velocity => new Vector3(Vx, 0f, Vz);
    public float Speed => (float)Math.Sqrt(Vx * Vx + Vz * Vz);
    public Vector3 Position => new Vector3(X, circuit.Centre[Math.Max(TrackIndex, 0)].y, Z);

    public void PlaceAt(int k, float lateral)
    {
        Vector3 p = circuit.Surface(k, lateral);
        X = p.x; Z = p.z;
        Vector3 f = circuit.Forward[k];
        Heading = (float)Math.Atan2(f.x, f.z);
        Vx = Vz = YawRate = 0f;
        Gear = 1; steer = 0f;
        TrackIndex = k;
    }

    /// <summary>Sets the car rolling straight ahead at a speed, for a flying start.</summary>
    public void Launch(float speed)
    {
        Vector3 f = Forward;
        Vx = f.x * speed; Vz = f.z * speed;
        float wheel = speed / Spec.WheelRadius;
        while (Gear < Spec.Gears.Length && wheel * Spec.Gears[Gear - 1] / Spec.Redline > 0.9f) Gear++;
    }

    /// <summary>Puts the car where a gliding car is drawn, moving along its path: no physics.</summary>
    public void Pose(Vector3 position, Vector3 forward, float speed, float yawRate)
    {
        X = position.x; Z = position.z;
        float flat = (float)Math.Sqrt(forward.x * forward.x + forward.z * forward.z);
        Heading = (float)Math.Atan2(forward.x, forward.z);
        Vx = forward.x / flat * speed; Vz = forward.z / flat * speed;
        YawRate = yawRate;
        ax = ay = 0f;
        UpdateTrackPosition();
    }

    /// <summary>Back to physics from a glide, as CarController.Wake: moving, in gear, wheels already turned for the bend.</summary>
    public void Wake(Vector3 position, Vector3 forward, float speed, float yawRate, float steerAngle)
    {
        Pose(position, forward, speed, yawRate);
        steer = steerAngle;
        shiftTimer = 0f;
        Gear = Spec.GearFor(speed);
    }

    /// <summary>The pedals and steering asked for in the last step, for traces.</summary>
    public float LastThrottle, LastBrake, LastSteer;
    /// <summary>Rear wheels spinning in the last step: the drive overpowered the tyre.</summary>
    public int Spinning;

    public void Step(float dt, float throttle, float brake, float steerTarget)
    {
        LastThrottle = throttle; LastBrake = brake; LastSteer = steerTarget;
        UpdateTrackPosition();
        if (Hold) { throttle = 0f; brake = 1f; }
        Vector3 F = Forward, R = Right;
        float vLongCar = Vx * F.x + Vz * F.z;
        float speed = Speed;

        // Steering, at CarController's rate.
        float target = Mathf.Clamp(steerTarget, -Spec.SteerLock, Spec.SteerLock);
        float rate = (Math.Abs(target) < Math.Abs(steer) || target * steer < 0f ? 3f : 1.3f) * Spec.SteerLock;
        steer = Mathf.MoveTowards(steer, target, rate * dt);

        float drive = Engine(dt, vLongCar, throttle);
        float gripUsed = 0f;
        int gripCount = 0;

        // Loads: weight split by the centre of mass, dips and crests, aero 40/60, load transfer.
        int li = line.Nearest(new Vector3(X, line.Point[Math.Max(TrackIndex, 0)].y, Z), -1 < TrackIndex ? TrackIndex : -1);
        float weight = Spec.Mass * Math.Max(G + line.VerticalCurvature[li] * speed * speed, 0f);
        float q = 0.5f * CarSpec.AirDensity * speed * speed;
        float down = q * Spec.LiftArea;
        float hw = Spec.HalfWheelbase, zc = Spec.CentreOfMass.z, h = Spec.CentreOfMass.y;
        float frontShare = (hw + zc) / (2f * hw);
        float frontAxle = weight * frontShare + down * 0.4f - Spec.Mass * ax * h / (2f * hw);
        float rearAxle = weight * (1f - frontShare) + down * 0.6f + Spec.Mass * ax * h / (2f * hw);
        float roll = Spec.Mass * ay * h / (2f * Spec.HalfTrack);   // to the outside (left wheels in a right-hander)

        float fx = 0f, fz = 0f, torque = 0f;
        Spinning = 0;
        float brakeTotal = brake * Spec.BrakeDecel * Spec.Mass * G;
        float corner = Spec.Mass / 4f;
        for (int i = 0; i < 4; i++)
        {
            bool front = i < 2, left = i % 2 == 0;
            float axle = front ? frontAxle : rearAxle;
            float share = axle / Math.Max(frontAxle + rearAxle, 1f);
            float load = Math.Max(axle * 0.5f + (left ? 1f : -1f) * roll * share, 0f);
            float a = left ? -Spec.HalfTrack : Spec.HalfTrack;          // to the right of the centre of mass
            float b = (front ? hw : -hw) - zc;                          // forwards of the centre of mass
            // Contact point velocity: V + yaw × offset.
            float pvx = Vx + YawRate * (a * -F.x + b * R.x);
            float pvz = Vz + YawRate * (a * -F.z + b * R.z);
            float angle = front ? steer : 0f;
            float ca = (float)Math.Cos(angle), sa = (float)Math.Sin(angle);
            // Wheel forward is the car forward turned right by the steering angle.
            float wfx = F.x * ca + R.x * sa, wfz = F.z * ca + R.z * sa;
            float wrx = R.x * ca - F.x * sa, wrz = R.z * ca - F.z * sa;   // wheel right
            float vLong = pvx * wfx + pvz * wfz;
            float vLat = pvx * wrx + pvz * wrz;                             // CarController's side axis points right
            SurfaceKind kind = WheelSurface(a, b);
            float cap = Spec.Grip * TrackSurface.Grip(kind) * load * (front ? 0.97f : 1.06f);
            if (Supplies != null) cap *= Supplies.GripScale;

            float fy;
            if (Math.Abs(vLong) < 4f && Math.Abs(vLat) < 4f) fy = Mathf.Clamp(-vLat * corner * 8f, -cap, cap);
            else
            {
                float slip = (float)Math.Atan2(vLat, Math.Abs(vLong));
                float bb = 2.64f / Spec.PeakSlip;
                fy = -cap * (float)Math.Sin(1.3f * Math.Atan(bb * slip));
            }
            float rolling = Mathf.Clamp(vLong * 4f, -1f, 1f) * TrackSurface.Rolling(kind) * load;
            wheels[i] = new WheelState { A = a, B = b, Load = load, Cap = cap, Fy = fy, VLong = vLong, Rolling = rolling, Kind = kind, Fx = wfx, Fz = wfz, Rx = wrx, Rz = wrz };
        }
        float roomLeft = Traction.Room(wheels[2].Cap, wheels[2].Fy), roomRight = Traction.Room(wheels[3].Cap, wheels[3].Fy);
        drive = Traction.Control(drive, roomLeft + roomRight, Math.Max(wheels[2].Rolling, 0f) + Math.Max(wheels[3].Rolling, 0f));
        Supplies?.Burn(drive * vLongCar, dt);
        Traction.SplitDrive(drive, roomLeft, roomRight, out float driveLeft, out float driveRight);
        for (int i = 0; i < 4; i++)
        {
            bool front = i < 2, left = i % 2 == 0;
            WheelState w = wheels[i];
            float cap = w.Cap, fy = w.Fy, vLong = w.VLong;
            float wheelDrive = front ? 0f : left ? driveLeft : driveRight;
            float wheelBrake = brakeTotal * (front ? Spec.FrontBrakeShare : 1f - Spec.FrontBrakeShare) * 0.5f;
            float fl = wheelDrive;
            bool braking = wheelBrake > 0f;
            bool holding = throttle < 0.01f && Math.Abs(vLong) < 0.5f;
            if (holding) fl += Mathf.Clamp(-vLong * corner * 10f, -cap, cap);
            else
            {
                if (braking) fl -= Math.Sign(vLong) * wheelBrake;
                fl -= w.Rolling;
            }
            if (Traction.Fit(ref fl, ref fy, cap, wheelDrive, braking) > 0.1f && !front) Spinning++;
            if (cap > 1f) { gripUsed += Math.Min((fl * fl + fy * fy) / (cap * cap), 1f); gripCount++; }
            float wx = w.Fx * fl + w.Rx * fy, wz = w.Fz * fl + w.Rz * fy;
            fx += wx; fz += wz;
            torque += wx * (-w.A * F.x + w.B * R.x) + wz * (-w.A * F.z + w.B * R.z);
        }

        if (speed > 0.1f) { fx -= Vx / speed * q * Spec.DragArea * DragScale; fz -= Vz / speed * q * Spec.DragArea * DragScale; }
        Vector3 t = line.Tangent[li];
        float hill = Spec.Mass * G * line.Grade[li];
        fx -= t.x * hill; fz -= t.z * hill;

        if (Supplies != null && gripCount > 0) Supplies.Scrub(speed * dt, (float)Math.Sqrt(gripUsed / gripCount));
        float accX = fx / Spec.Mass, accZ = fz / Spec.Mass;
        ax = accX * F.x + accZ * F.z;
        ay = accX * R.x + accZ * R.z;
        Vx += accX * dt; Vz += accZ * dt;
        YawRate += torque / inertia * dt;
        X += Vx * dt; Z += Vz * dt;
        Heading += YawRate * dt;
    }

    float Engine(float dt, float along, float throttle)
    {
        float wheelSpeed = Math.Abs(along) / Spec.WheelRadius;
        float ratio = Spec.Gears[Gear - 1];
        float x = wheelSpeed * ratio / Spec.Redline;
        if (shiftTimer <= 0f)
        {
            if (x > 0.97f && Gear < Spec.Gears.Length && throttle > 0f) { Gear++; shiftTimer = ShiftTime; }
            else if (Gear > 1 && wheelSpeed * Spec.Gears[Gear - 2] / Spec.Redline < 0.8f) { Gear--; shiftTimer = ShiftTime; }
        }
        if (shiftTimer > 0f) { shiftTimer -= dt; return 0f; }
        bool cut = (Supplies != null && Supplies.Empty) || along > SpeedLimit;
        // Reverse engages, as CarController's, only once the car is all but stopped.
        bool wrongWay = Reverse ? along > 0.5f : false;
        if (throttle > 0f && x < 1f && !cut && !wrongWay)
        {
            float xe = Math.Max(x, 0.2f);
            float torque = Spec.PowerAt(xe) / (xe * Spec.Redline);
            return (Reverse ? -1f : 1f) * throttle * torque * (Supplies != null ? Supplies.PowerScale : 1f) * ratio / Spec.WheelRadius;
        }
        float drag = 0.08f * Spec.PeakPower / Spec.Redline * ratio / Spec.WheelRadius * Mathf.Clamp01(x);
        return -Math.Sign(along) * drag;
    }

    void UpdateTrackPosition()
    {
        int start = TrackIndex < 0 ? 0 : TrackIndex - 10, end = TrackIndex < 0 ? circuit.Count : TrackIndex + 40;
        float best = float.MaxValue;
        for (int i = start; i < end; i++)
        {
            int k = circuit.Wrap(i);
            float dx = circuit.Centre[k].x - X, dz = circuit.Centre[k].z - Z;
            float d = dx * dx + dz * dz;
            if (d < best) { best = d; TrackIndex = k; }
        }
        Vector3 c = circuit.Centre[TrackIndex];
        Lateral = (X - c.x) * circuit.Left[TrackIndex].x + (Z - c.z) * circuit.Left[TrackIndex].z;
        Surface = SurfaceAt(Lateral);
        if (Surface != SurfaceKind.Tarmac && OnPitLane(new Vector3(X, 0f, Z))) Surface = SurfaceKind.Tarmac;
    }

    SurfaceKind WheelSurface(float a, float b)
    {
        Vector3 F = Forward, R = Right;
        float wx = X + R.x * a + F.x * b, wz = Z + R.z * a + F.z * b;
        Vector3 c = circuit.Centre[TrackIndex];
        float lat = (wx - c.x) * circuit.Left[TrackIndex].x + (wz - c.z) * circuit.Left[TrackIndex].z;
        SurfaceKind kind = SurfaceAt(lat);
        return kind != SurfaceKind.Tarmac && OnPitLane(new Vector3(wx, 0f, wz)) ? SurfaceKind.Tarmac : kind;
    }

    /// <summary>True on the pit lane surface, entry and exit roads included.</summary>
    public bool OnPitLane(Vector3 p)
    {
        return Pits != null && TrackIndex >= 0 && Pits.OnLane(p, TrackIndex);
    }

    /// <summary>Past this far from the tarmac edge the runoff is a gravel trap, metres (none by default).</summary>
    public float GravelFrom = float.TryParse(Environment.GetEnvironmentVariable("AI_GRAVEL"), out float g) ? g : float.PositiveInfinity;

    SurfaceKind SurfaceAt(float lateral)
    {
        float edge = Math.Abs(lateral) - circuit.HalfWidth(TrackIndex);
        return edge < 0f ? SurfaceKind.Tarmac : edge < 1.5f ? SurfaceKind.Kerb : edge > GravelFrom ? SurfaceKind.Gravel : SurfaceKind.Grass;
    }
}
