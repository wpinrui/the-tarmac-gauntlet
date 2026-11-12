using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// The clouds' shadows on the ground: a cookie on the sun that darkens its light under each cloud of the published
    /// <see cref="Atmosphere"/>, so the track goes from shade to sudden sunlight as the clouds drift over. The cookie covers
    /// <see cref="Span"/> metres round a point (the camera) and is redrawn every frame, cast from the clouds' height along
    /// the sun's rays, so a low sun throws the shadows far. Every surface lit by the sun takes it, at every quality level:
    /// it costs one small draw a frame. The volumetric fog reads it too, so cloud shadows fall through the air.
    /// </summary>
    public sealed class CloudShadows
    {
        /// <summary>Texels across the cookie, and the ground it covers, m.</summary>
        public const int Size = 512;
        public const float Span = 8000f;

        private static readonly int CookieToWorldId = Shader.PropertyToID("_TgCookieToWorld"),
            ShadowTexId = Shader.PropertyToID("_TgCloudShadowTex"), WorldToShadowId = Shader.PropertyToID("_TgWorldToCloudShadow");

        private readonly Light lamp;
        private readonly UniversalAdditionalLightData data;
        private readonly RenderTexture cookie;
        private readonly Material material;
        private readonly CommandBuffer commands = new CommandBuffer { name = "Cloud shadows" };

        public CloudShadows(Light sun)
        {
            lamp = sun;
            Shader shader = Resources.Load<Shader>("Atmosphere/CloudShadow");
            if (shader == null)
            {
                Debug.LogError("Rendering: Resources/Atmosphere/CloudShadow is missing.");
                return;
            }
            material = CoreUtils.CreateEngineMaterial(shader);
            cookie = new RenderTexture(Size, Size, GraphicsFormat.R8_UNorm, GraphicsFormat.None)
            {
                name = "Cloud shadows",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
            };
            cookie.Create();
            data = lamp.GetUniversalAdditionalLightData();
            data.lightCookieSize = new Vector2(Span, Span);
            lamp.cookie = cookie;
            Shader.SetGlobalTexture(ShadowTexId, cookie);
        }

        /// <summary>Redraws the shadows of the clouds published now, centred on this point.</summary>
        public void Draw(Vector3 around)
        {
            if (material == null || !Atmosphere.Present) return;
            if (!cookie.IsCreated()) cookie.Create();
            Transform t = lamp.transform;
            Vector3 local = t.InverseTransformPoint(around);
            // Kept on whole texels, so the shadows stay still as the camera moves.
            float texel = Span / Size;
            var offset = new Vector2(Mathf.Round(local.x / texel) * texel, Mathf.Round(local.y / texel) * texel);
            data.lightCookieOffset = offset;
            // URP maps the light's own x and y to the cookie as (local - offset) / span + 0.5.
            Matrix4x4 uvToLocal = Matrix4x4.TRS(new Vector3(offset.x - Span * 0.5f, offset.y - Span * 0.5f, 0f), Quaternion.identity,
                new Vector3(Span, Span, 1f));
            Matrix4x4 toWorld = t.localToWorldMatrix * uvToLocal;
            material.SetMatrix(CookieToWorldId, toWorld);
            Shader.SetGlobalMatrix(WorldToShadowId, toWorld.inverse);
            Shader.SetGlobalTexture(ShadowTexId, cookie);
            commands.Clear();
            commands.SetRenderTarget(cookie);
            Blitter.BlitTexture(commands, new Vector4(1f, 1f, 0f, 0f), material, 0);
            Graphics.ExecuteCommandBuffer(commands);
        }

        public void Dispose()
        {
            if (lamp != null && lamp.cookie == cookie) lamp.cookie = null;
            if (cookie != null)
            {
                cookie.Release();
                Object.Destroy(cookie);
            }
            CoreUtils.Destroy(material);
            commands.Release();
        }
    }
}
