using UnityEngine;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// Sits on the menu camera and asks for a blurred, more colourful copy of the scene for the glass panels: CSS blur(26px)
    /// saturate(150%) on the 1600 x 900 reference screen. <see cref="GlassBlurFeature"/> on the renderer draws it.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class GlassBackdrop : MonoBehaviour
    {
        /// <summary>The blurred scene at a quarter of the screen's size; null until the first frame.</summary>
        public static RenderTexture Texture { get; private set; }

        /// <summary>The texture at w x h, made again when the screen's size changes.</summary>
        internal static RenderTexture Sized(int w, int h)
        {
            if (Texture == null || Texture.width != w || Texture.height != h)
            {
                Release();
                Texture = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
                    { name = "Menu glass", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                Texture.Create();
            }
            return Texture;
        }

        private static void Release()
        {
            if (Texture == null) return;
            Texture.Release();
            Destroy(Texture);
            Texture = null;
        }

        private void OnDestroy() => Release();
    }
}
