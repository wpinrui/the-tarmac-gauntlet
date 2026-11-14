using System;

namespace TarmacGauntlet.Audio
{
    /// <summary>
    /// A live synth's volume, applied to its samples rather than to its AudioSource. The engines, tyres and wet road play
    /// a clip of constant ones that their synth multiplies in OnAudioFilterRead. Unity takes a source at volume 0 off the
    /// mixer, and a source whose sound comes from OnAudioFilterRead may not come back when its volume rises again: the
    /// race fading in as it opens, and the pause cutting the cars to silence, left the player's engine silent until the
    /// audio restarted (an alt-tab). So the source stays at <see cref="Carrier"/>, and the group's gain from the
    /// <see cref="Mix"/>, which can be 0, ramps across each buffer here. The game thread writes <see cref="Target"/>;
    /// the audio thread calls <see cref="Apply"/>. No Unity in here, so the tools can check it.
    /// </summary>
    public sealed class SynthGain
    {
        /// <summary>The AudioSource's volume for every live synth, always. Never 0.</summary>
        public const float Carrier = 1f;

        private volatile float target;
        private float current;
        private bool started;

        /// <summary>The gain to head for, 0 and up: the group's <see cref="Mix.Gain"/>, times the layer's own level.</summary>
        public float Target
        {
            get => target;
            set => target = value < 0f || float.IsNaN(value) ? 0f : value;
        }

        /// <summary>The gain the last buffer ended on.</summary>
        public float Current => current;

        /// <summary>
        /// Multiplies <paramref name="samples"/>' first <paramref name="count"/> by the gain, moving in a straight line
        /// from where the last buffer ended to <see cref="Target"/>, so a change never clicks. The first buffer starts
        /// at the target.
        /// </summary>
        public void Apply(float[] samples, int count)
        {
            float to = target;
            float from = started ? current : to;
            started = true;
            if (count <= 0)
            {
                current = to;
                return;
            }
            float step = (to - from) / count;
            for (int i = 0; i < count; i++) samples[i] *= from + step * (i + 1);
            current = to;
        }
    }
}
