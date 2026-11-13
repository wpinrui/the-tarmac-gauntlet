using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// URP Lit for materials made in code: the game's copy of it (Resources/TarmacLit/Lit.shader), which is URP's own but
    /// shades the night's lamps under their budget (Lamps.hlsl). Lit's inspector sets its blend state and keywords for
    /// materials made in the editor; these set them for the game's own, with the Standard shader's modes mapped across.
    /// </summary>
    public static class Lit
    {
        public static readonly int BaseColorId = UnityEngine.Shader.PropertyToID("_BaseColor"),
            SmoothnessId = UnityEngine.Shader.PropertyToID("_Smoothness"),
            MetallicId = UnityEngine.Shader.PropertyToID("_Metallic"),
            EmissionColorId = UnityEngine.Shader.PropertyToID("_EmissionColor");

        private static readonly int MetallicGlossMapId = UnityEngine.Shader.PropertyToID("_MetallicGlossMap"),
            BumpMapId = UnityEngine.Shader.PropertyToID("_BumpMap"), CutoffId = UnityEngine.Shader.PropertyToID("_Cutoff"),
            AlphaClipId = UnityEngine.Shader.PropertyToID("_AlphaClip"), SurfaceId = UnityEngine.Shader.PropertyToID("_Surface"),
            BlendId = UnityEngine.Shader.PropertyToID("_Blend"), PreserveSpecularId = UnityEngine.Shader.PropertyToID("_BlendModePreserveSpecular"),
            SrcBlendId = UnityEngine.Shader.PropertyToID("_SrcBlend"), DstBlendId = UnityEngine.Shader.PropertyToID("_DstBlend"),
            SrcBlendAlphaId = UnityEngine.Shader.PropertyToID("_SrcBlendAlpha"), DstBlendAlphaId = UnityEngine.Shader.PropertyToID("_DstBlendAlpha"),
            ZWriteId = UnityEngine.Shader.PropertyToID("_ZWrite");

        private static Shader shader;

        /// <summary>The game's copy of URP Lit, or URP's own if it is missing.</summary>
        public static Shader Shader
        {
            get
            {
                if (shader != null) return shader;
                shader = Resources.Load<Shader>("TarmacLit/Lit");
                if (shader == null) shader = UnityEngine.Shader.Find("Universal Render Pipeline/Lit");
                return shader;
            }
        }

        /// <summary>A normal map, as Standard's _BumpMap.</summary>
        public static void NormalMap(Material m, Texture map)
        {
            m.SetTexture(BumpMapId, map);
            m.EnableKeyword("_NORMALMAP");
        }

        /// <summary>Metallic in R and smoothness in A, as Standard's _MetallicGlossMap with a gloss scale of 1.</summary>
        public static void MetallicSmoothnessMap(Material m, Texture map)
        {
            m.SetTexture(MetallicGlossMapId, map);
            m.SetFloat(SmoothnessId, 1f);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
        }

        /// <summary>Alpha tested, as Standard's cutout mode.</summary>
        public static void Cutout(Material m, float cutoff = 0.5f)
        {
            m.SetFloat(AlphaClipId, 1f);
            m.SetFloat(CutoffId, cutoff);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)RenderQueue.AlphaTest;
        }

        /// <summary>Alpha blended with its highlights faded too, as Standard's fade mode.</summary>
        public static void Fade(Material m) => Transparent(m, false);

        /// <summary>Alpha blended with its highlights kept at full strength, as Standard's transparent mode, for glass.</summary>
        public static void Glass(Material m) => Transparent(m, true);

        private static void Transparent(Material m, bool keepHighlights)
        {
            m.SetFloat(SurfaceId, 1f);
            m.SetFloat(BlendId, 0f);
            m.SetFloat(PreserveSpecularId, keepHighlights ? 1f : 0f);
            m.SetFloat(SrcBlendId, (float)(keepHighlights ? BlendMode.One : BlendMode.SrcAlpha));
            m.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat(SrcBlendAlphaId, (float)BlendMode.One);
            m.SetFloat(DstBlendAlphaId, (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat(ZWriteId, 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (keepHighlights) m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            else m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("DepthOnly", false);
            // A see-through surface casts no solid shadow.
            m.SetShaderPassEnabled("ShadowCaster", false);
        }
    }
}
