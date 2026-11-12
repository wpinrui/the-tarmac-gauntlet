namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// The values of the volumetric clouds' look the CPU needs. CloudLook.hlsl holds them all; the atmosphere check fails if
    /// these differ from it.
    /// </summary>
    public static class CloudLook
    {
        /// <summary>Most steps a cloud ray takes, at High and at Ultra: TG_CLOUD_HIGH_STEPS and TG_CLOUD_ULTRA_STEPS.</summary>
        public const int HighSteps = 48, UltraSteps = 64;
        /// <summary>Where the wind's drift of the clouds wraps, m: TG_CLOUD_REPEAT.</summary>
        public const float Repeat = 21600f;
    }
}
