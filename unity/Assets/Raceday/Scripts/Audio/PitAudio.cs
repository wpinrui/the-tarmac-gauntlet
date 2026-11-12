using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The pit stops' sounds, on the kit as the crews move it (<see cref="PitKitWork"/>): wheel guns where they land on
    /// the hubs, the air jacks at the lance, the fuel rig at the filler and the car thumping down off its jacks. Only
    /// boxes played live near the camera sound; <see cref="PitCues"/> reads each one's kit for what to play. Runs after
    /// the kit has moved.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class PitAudio : MonoBehaviour
    {
        private const float Near = 2.5f, Far = 90f;

        private PitKitWork kit;
        private PitCues[] cues;
        private readonly bool[] hub = new bool[4];

        public void Build(PitKitWork kitWork)
        {
            kit = kitWork;
            cues = new PitCues[kit.Count];
            for (int i = 0; i < cues.Length; i++) cues[i] = new PitCues();
        }

        private void LateUpdate()
        {
            if (kit == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < cues.Length; i++)
            {
                KitWork work = kit.Work(i);
                PitCues c = cues[i];
                if (work == null || !kit.Live(i))
                {
                    c.Reset();
                    continue;
                }
                for (int w = 0; w < 4; w++) hub[w] = work.OnHub[w];
                c.Step(dt, hub, work.NozzleOn, work.LanceIn, work.Lift);

                for (int w = 0; w < 4; w++)
                {
                    Vector3 gun = work.Pieces[(int)work.GunOf(w)].Position;
                    if (c.GunOn[w]) RaceSfx.Play(SfxBank.Gun, gun, 1f, Near, Far);
                    // The gun's own exhaust as the trigger lets go.
                    if (c.GunOff[w]) RaceSfx.Play(SfxBank.Air, gun, 0.3f, Near, Far * 0.5f);
                }
                Vector3 lance = work.Pieces[(int)KitItem.Lance].Position;
                if (c.JackUp) RaceSfx.Play(SfxBank.JackUp, lance, 0.85f, Near, Far);
                if (c.JackVent) RaceSfx.Play(SfxBank.Air, lance, 0.55f, Near, Far);
                if (c.JackDrop) RaceSfx.Play(SfxBank.JackDrop, kit.CarAt(i), 0.9f, Near, Far);

                Vector3 nozzle = work.Pieces[(int)KitItem.Nozzle].Position;
                if (c.NozzleOn || c.NozzleOff) RaceSfx.Play(SfxBank.FuelClunk, nozzle, 0.6f, Near, Far * 0.6f);
                if (c.Fuelling) RaceSfx.Hold(c, SfxBank.FuelFlow, nozzle, 0.5f, 1f, Near, Far * 0.4f);
            }
        }
    }
}
