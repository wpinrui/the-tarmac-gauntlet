namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The sounds of one box's stop, read off its kit as the crew move it each frame: a wheel gun fires as it lands on
    /// the hub and vents as it comes off, the jacks fire as the lance goes in, the car thumps down once it is fully
    /// down again, and the nozzle clunks on and off with the fuel running in between. No Unity in here, so the tools
    /// can check it.
    /// </summary>
    public sealed class PitCues
    {
        /// <summary>Least time a gun is off the hub before landing on it fires it again, s, so a gun resettling fires once.</summary>
        public const float GunGap = 0.2f;

        private readonly bool[] onHub = new bool[4];
        private readonly float[] left = { -1f, -1f, -1f, -1f };
        private bool nozzle, lance, up;
        private float time;

        /// <summary>This frame's cues. Wheels are by wheel: 0 front left, 1 front right, 2 rear left, 3 rear right.</summary>
        public readonly bool[] GunOn = new bool[4], GunOff = new bool[4];
        public bool JackUp, JackVent, JackDrop, NozzleOn, NozzleOff;
        /// <summary>True while fuel runs in.</summary>
        public bool Fuelling => nozzle;

        /// <summary>Reads one frame of the kit; dt is the frame's time, s. Lift is how far up the car is, 0 to 1.</summary>
        public void Step(float dt, bool[] hub, bool nozzleOn, bool lanceIn, float lift)
        {
            time += dt;
            for (int w = 0; w < 4; w++)
            {
                GunOn[w] = hub[w] && !onHub[w] && (left[w] < 0f || time - left[w] >= GunGap);
                GunOff[w] = !hub[w] && onHub[w];
                if (GunOff[w]) left[w] = time;
                onHub[w] = hub[w];
            }
            NozzleOn = nozzleOn && !nozzle;
            NozzleOff = !nozzleOn && nozzle;
            nozzle = nozzleOn;
            JackUp = lanceIn && !lance;
            JackVent = !lanceIn && lance;
            lance = lanceIn;
            // Down once it has been fully up and has come all the way back down.
            if (lift >= 0.9f) up = true;
            JackDrop = up && lift <= 0f;
            if (JackDrop) up = false;
        }

        /// <summary>Forgets the stop: the box went out of earshot or its crew are back at rest.</summary>
        public void Reset()
        {
            for (int w = 0; w < 4; w++) { onHub[w] = GunOn[w] = GunOff[w] = false; left[w] = -1f; }
            nozzle = lance = up = false;
            JackUp = JackVent = JackDrop = NozzleOn = NozzleOff = false;
        }
    }
}
