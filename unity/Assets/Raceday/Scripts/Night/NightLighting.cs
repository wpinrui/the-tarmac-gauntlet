using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Switches the night lighting on as the sky darkens. The lenses, the stands' glow and every car's lamps come on with
    /// how dark it is for the lights (<see cref="TimeOfDay.Lights"/>), which brings them on earlier under cloud and rain.
    /// The light the floodlights (<see cref="FloodlightRig"/>) and the headlamps (<see cref="HeadlampRig"/>) throw follows
    /// how dark the sky really is (<see cref="NightBudget.FloodDark"/>, <see cref="NightBudget.HeadlampDark"/>), so at an
    /// overcast dusk it is faint and never washes out a track still lit by the day. While the race runs every lit shader
    /// shades those lights under the night's budget (Lamps.hlsl).
    /// </summary>
    public sealed class NightLighting : MonoBehaviour
    {
        public TimeOfDay Sky;
        public FloodlightRig Floods;
        public HeadlampRig Headlamps;
        public StandMaterials Stands;

        private static readonly int BudgetId = Shader.PropertyToID("_TgLampBudget");
        private float shown = -1f;

        private void OnEnable() => Shader.SetGlobalVector(BudgetId, NightBudget.ShaderBudget);

        private void OnDisable() => Shader.SetGlobalVector(BudgetId, Vector4.zero);

        private void Update()
        {
            if (Sky == null) return;
            float lights = Sky.Lights;
            float on = NightBudget.On(lights);
            if (Floods != null)
            {
                Floods.Glow = on;
                Floods.Level = on * NightBudget.FloodDark(Sky.SunElevation);
            }
            if (Headlamps != null) Headlamps.Level = on * NightBudget.HeadlampDark(Sky.SunElevation);
            if (Mathf.Abs(lights - shown) < 0.005f) return;
            shown = lights;
            LampMaterials.Shared.Night(lights);
            Stands?.Night(on, NightBudget.Flood);
        }
    }
}
