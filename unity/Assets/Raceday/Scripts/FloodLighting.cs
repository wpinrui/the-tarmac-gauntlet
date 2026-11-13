using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Hands the floodlights' map (<see cref="FloodField"/>) to the lit shaders (Rendering/Shaders/NightLit.hlsl) as a
    /// texture, and sets how brightly it lights things now, and the most light the night's lamps give back from any surface.
    /// </summary>
    public static class FloodLighting
    {
        private static readonly int FieldId = Shader.PropertyToID("_TgFloodField"), RectId = Shader.PropertyToID("_TgFloodRect"),
            LightId = Shader.PropertyToID("_TgFloodLight"), MostId = Shader.PropertyToID("_TgNightMost");
        private static FloodField shown;
        private static Texture2D texture;

        /// <summary>Lights everything on the map as brightly as <paramref name="on"/> of the full light.</summary>
        public static void Publish(FloodField field, float on)
        {
            if (field == null) { Clear(); return; }
            if (field != shown) Upload(field);
            Color light = NightLook.Flood.linear * (NightLook.FloodGround * on);
            Shader.SetGlobalVector(LightId, new Vector4(light.r, light.g, light.b, on > 0.001f ? 1f : 0f));
            Shader.SetGlobalFloat(MostId, NightLook.NightMost);
        }

        /// <summary>Puts the light out, as when the race ends.</summary>
        public static void Clear()
        {
            Shader.SetGlobalVector(LightId, Vector4.zero);
            Shader.SetGlobalFloat(MostId, NightLook.NightMost);
            if (texture != null) Object.Destroy(texture);
            texture = null;
            shown = null;
        }

        private static void Upload(FloodField field)
        {
            if (texture != null) Object.Destroy(texture);
            texture = new Texture2D(field.Width, field.Height, TextureFormat.RGBAHalf, false, true)
            {
                name = "Floodlight map", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            var px = new Color[field.Width * field.Height];
            for (int k = 0; k < px.Length; k++)
            {
                Vector3 t = field.Toward[k];
                px[k] = new Color(field.Light[k], field.Ground[k], t.x, t.z);
            }
            texture.SetPixels(px);
            texture.Apply(false, true);
            shown = field;
            float w = (field.Width - 1) * FloodField.Texel, h = (field.Height - 1) * FloodField.Texel;
            // Texel centres at the cells: the map's first and last cells sit half a texel in from its edges.
            float x0 = field.X0 - 0.5f * FloodField.Texel, z0 = field.Z0 - 0.5f * FloodField.Texel;
            Shader.SetGlobalTexture(FieldId, texture);
            Shader.SetGlobalVector(RectId, new Vector4(x0, z0, 1f / (w + FloodField.Texel), 1f / (h + FloodField.Texel)));
        }
    }
}
