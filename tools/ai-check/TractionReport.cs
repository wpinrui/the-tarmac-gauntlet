// Traction measurements, printed rather than checked: pass "traction" to run them.
//   launch   full throttle from the pole box, steered straight by the AI: 0-100 km/h and the share of
//            that time a rear wheel spins
//   AI exit  the AI's own lap 2 at Pace 100: speed 150 m after each slow corner's apex, lap time, and
//            steps with a rear wheel spinning
//   floored  the same lap, but from each apex to 150 m on the throttle is held flat, as a keyboard player
//            does: speed 150 m on, and how many exits end in a slide past 0.3 rad or off the track
using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Raceday;
using TarmacGauntlet.Simulation;

static partial class Check
{
    static readonly (string Id, int Condition)[] TractionCars =
    {
        ("f-01", 30), ("f-01", 100), ("e-07", 100), ("d-15", 100), ("c-20", 100), ("b-24", 100), ("a-29", 100),
    };

    sealed class TractionRun
    {
        public float Lap, ExitSpeed, SpinShare;
        public int Spins, Exits;
    }

    static Consumables TrimmedSupplies(string id, int condition, CarSpec spec, SpeedProfile profile)
    {
        CarModel model = CarModels.All.First(c => c.Id == id);
        var supplies = Consumables.For(spec, profile, 60f);
        var instance = new CarInstance { Id = "t", ModelId = id, Age = 0, Condition = condition, InstalledUpgrades = new InstalledUpgrades() };
        supplies.Trim(EffectiveStats.CalculateEffectiveStats(instance, model), model.BaseStats);
        return supplies;
    }

    static List<int> Apexes(SpeedProfile p)
    {
        int n = p.Speed.Length, reach = Math.Max(1, (int)(80f / circuit.Step));
        float top = p.Speed.Max();
        var list = new List<int>();
        for (int k = 0; k < n; k++)
        {
            if (p.Speed[k] > 0.75f * top) continue;
            bool lowest = true;
            for (int j = -reach; j <= reach && lowest; j++) if (j != 0 && p.Speed[circuit.Wrap(k + j)] < p.Speed[k]) lowest = false;
            if (lowest && (list.Count == 0 || k - list[list.Count - 1] > reach)) list.Add(k);
        }
        return list;
    }

    static (float zeroTo100, float spinShare) Launch(string id, int condition)
    {
        CarSpec spec = CarSpec.For(id);
        RacingLine l = LineFor(id);
        var pilot = new AiPilot(l, spec, 100f, 100f, 100f, 1);
        var car = new StandInCar(spec, l);
        car.Supplies = TrimmedSupplies(id, condition, spec, pilot.Profile);
        GridSlot pole = circuit.Data.grid.slots[0];
        car.PlaceAt(circuit.IndexOf(pole.d - 2.6f), pole.lateral);
        float time = 0f;
        int spin = 0, steps = 0;
        while (car.Speed < 100f / 3.6f && time < 60f)
        {
            AiPilot.Controls c = pilot.Drive(car.Position, car.Forward, car.Velocity, car.YawRate);
            car.Step(Dt, 1f, 0f, c.Steer);
            time += Dt;
            steps++;
            if (car.Spinning > 0) spin++;
        }
        return (time, spin / (float)Math.Max(steps, 1));
    }

    static TractionRun ExitRun(string id, int condition, bool floored)
    {
        CarSpec spec = CarSpec.For(id);
        RacingLine l = LineFor(id);
        var pilot = new AiPilot(l, spec, 100f, 100f, 100f, 1);
        var car = new StandInCar(spec, l);
        car.Supplies = TrimmedSupplies(id, condition, spec, pilot.Profile);
        GridSlot pole = circuit.Data.grid.slots[0];
        car.PlaceAt(circuit.IndexOf(pole.d - 2.6f), pole.lateral);
        List<int> apexes = Apexes(pilot.Profile);
        int window = (int)(150f / circuit.Step);
        var result = new TractionRun();
        float time = 0f, lapStart = -1f;
        int lap = 0, last = car.TrackIndex, spinSteps = 0, steps = 0;
        int inExit = -1;
        bool slid = false;
        var exitSpeeds = new List<float>();
        while (lap < 3 && time < 600f)
        {
            if (car.Supplies != null) pilot.Grip = car.Supplies.GripScale;
            AiPilot.Controls c = pilot.Drive(car.Position, car.Forward, car.Velocity, car.YawRate);
            int k = car.TrackIndex;
            float throttle = c.Throttle, brake = c.Brake;
            int exit = -1;
            foreach (int a in apexes)
            {
                int d = circuit.Wrap(k - a);
                if (d < window) { exit = a; break; }
            }
            if (lap == 2)
            {
                if (floored && exit >= 0) { throttle = 1f; brake = 0f; }
                if (exit >= 0 && inExit != exit) { inExit = exit; slid = false; }
                if (exit >= 0)
                {
                    float slide = car.Speed > 5f ? (float)Math.Abs(Math.Atan2(
                        car.Velocity.x * car.Right.x + car.Velocity.z * car.Right.z,
                        car.Velocity.x * car.Forward.x + car.Velocity.z * car.Forward.z)) : 0f;
                    float off = Math.Abs(car.Lateral) - circuit.HalfWidth(k);
                    if (slide > 0.3f || off > KerbWidth) slid = true;
                }
                else if (inExit >= 0)
                {
                    exitSpeeds.Add(car.Speed);
                    if (slid) result.Spins++;
                    result.Exits++;
                    inExit = -1;
                }
                steps++;
                if (car.Spinning > 0) spinSteps++;
            }
            car.Step(Dt, throttle, brake, c.Steer);
            time += Dt;
            k = car.TrackIndex;
            if (last > circuit.Count - 50 && k < 50)
            {
                lap++;
                if (lap == 3) result.Lap = time - lapStart;
                lapStart = time;
            }
            last = k;
        }
        result.ExitSpeed = exitSpeeds.Count > 0 ? exitSpeeds.Average() : 0f;
        result.SpinShare = spinSteps / (float)Math.Max(steps, 1);
        return result;
    }

    static void TractionReport()
    {
        Console.WriteLine("car                     | 0-100 s | launch spin | AI lap  | AI exit km/h | AI spin | floored exit km/h | floored slides");
        foreach (var (id, condition) in TractionCars)
        {
            CarSpec spec = CarSpec.For(id);
            var (t100, spin) = Launch(id, condition);
            TractionRun ai = ExitRun(id, condition, false);
            TractionRun flat = ExitRun(id, condition, true);
            string name = $"{spec.Name} {condition}%";
            Console.WriteLine($"{name,-23} | {t100,7:0.00} | {spin,10:P0} | {Time(ai.Lap),7} | {ai.ExitSpeed * 3.6f,12:0.0} | {ai.SpinShare,7:P1} | {flat.ExitSpeed * 3.6f,17:0.0} | {flat.Spins,3} of {flat.Exits}");
        }
    }
}
