using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Visuals;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The finish on a car model's materials past its paint, each part of a graphics option that changes live: tinted,
    /// glossy windows and mirrors (<see cref="GraphicsOptions.CarGlass"/>), clear lenses over the lamps that glow with
    /// them (<see cref="GraphicsOptions.LampLenses"/>, drawn by <see cref="LampMaterials"/>), and carbon weave, rubber,
    /// plastic and metal (<see cref="GraphicsOptions.CarTrims"/>). The model's material names say which is which (see
    /// docs/car-paint.md). Each finished material is made once from the model's own, keeping its colour, and shared by
    /// every car using it. With an option off its parts wear the model's own materials. The paint's clear coat and
    /// flakes (<see cref="GraphicsOptions.Clearcoat"/>, <see cref="GraphicsOptions.PaintFlake"/>) are switched here
    /// too, for the CarPaint and Livery shaders.
    /// </summary>
    public static class CarMaterials
    {
        private enum Finish { Glass, Mirror, Lens, Carbon, Rubber, Metal }

        private readonly struct Look
        {
            public readonly Finish Kind;
            public readonly float Smoothness;

            public Look(Finish finish, float smoothness = 0f)
            {
                Kind = finish;
                Smoothness = smoothness;
            }

            public GraphicsOption Option => Kind switch
            {
                Finish.Glass or Finish.Mirror => GraphicsOptions.CarGlass,
                Finish.Lens => GraphicsOptions.LampLenses,
                _ => GraphicsOptions.CarTrims,
            };
        }

        /// <summary>The finish for each model material name.</summary>
        private static readonly Dictionary<string, Look> Looks = new Dictionary<string, Look>
        {
            ["glass"] = new Look(Finish.Glass, 0.97f),
            ["mirror_glass"] = new Look(Finish.Mirror, 0.98f),
            ["head_glass"] = new Look(Finish.Lens),
            ["tail_glass"] = new Look(Finish.Lens),
            ["carbon"] = new Look(Finish.Carbon),
            ["tyre"] = new Look(Finish.Rubber, 0.22f),
            ["trim"] = new Look(Finish.Rubber, 0.5f),
            ["grille"] = new Look(Finish.Rubber, 0.3f),
            ["housing"] = new Look(Finish.Rubber, 0.4f),
            ["chrome"] = new Look(Finish.Metal, 0.95f),
            ["name"] = new Look(Finish.Metal, 0.9f),
            ["reflector"] = new Look(Finish.Metal, 0.92f),
            ["rim"] = new Look(Finish.Metal, 0.8f),
            ["hub"] = new Look(Finish.Metal, 0.6f),
            ["dark_metal"] = new Look(Finish.Metal, 0.65f),
            ["engine_metal"] = new Look(Finish.Metal, 0.6f),
        };

        /// <summary>A window's see-through amount when the model's own glass is opaque.</summary>
        private const float GlassAlpha = 0.45f;

        private sealed class Slot
        {
            public Renderer Renderer;
            public int Index;
            public Material Plain, Finished;
            public GraphicsOption Option;
        }

        private static readonly List<Slot> slots = new List<Slot>();
        private static readonly Dictionary<Material, Material> finished = new Dictionary<Material, Material>();
        private static Shader carbon;
        private static bool watching;

        /// <summary>
        /// Finishes a car model's glass, lenses and trims, with each option as it is now, and keeps track of them so the
        /// options can change them live. Call after the paint, before <see cref="CarLights.Fit"/>.
        /// </summary>
        public static void Dress(GameObject model)
        {
            Watch();
            slots.RemoveAll(s => s.Renderer == null);
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !Looks.TryGetValue(mats[i].name, out Look look)) continue;
                    Material done = Finished(mats[i], look);
                    if (done == null) continue;
                    var slot = new Slot { Renderer = r, Index = i, Plain = mats[i], Finished = done, Option = look.Option };
                    slots.Add(slot);
                    if (!slot.Option.On) continue;
                    mats[i] = done;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>Listens to the options, once, and sets the paint's keywords.</summary>
        private static void Watch()
        {
            if (watching) return;
            watching = true;
            GraphicsOptions.CarGlass.Changed += Swap;
            GraphicsOptions.LampLenses.Changed += Swap;
            GraphicsOptions.CarTrims.Changed += Swap;
            GraphicsOptions.Clearcoat.Changed += _ => PaintKeywords();
            GraphicsOptions.PaintFlake.Changed += _ => PaintKeywords();
            PaintKeywords();
        }

        private static void PaintKeywords()
        {
            Keyword("_TARMAC_CLEARCOAT", GraphicsOptions.Clearcoat.On);
            Keyword("_TARMAC_FLAKE", GraphicsOptions.PaintFlake.On);
        }

        private static void Keyword(string name, bool on)
        {
            if (on) Shader.EnableKeyword(name);
            else Shader.DisableKeyword(name);
        }

        /// <summary>Puts every part of the option in its finished or its own material.</summary>
        private static void Swap(GraphicsOption option)
        {
            slots.RemoveAll(s => s.Renderer == null);
            bool on = option.On;
            foreach (Slot s in slots)
            {
                if (s.Option != option) continue;
                Material[] mats = s.Renderer.sharedMaterials;
                if (s.Index >= mats.Length) continue;
                mats[s.Index] = on ? s.Finished : s.Plain;
                s.Renderer.sharedMaterials = mats;
            }
        }

        /// <summary>The finished copy of a model material, made once; null if it cannot be made.</summary>
        private static Material Finished(Material source, Look look)
        {
            if (look.Kind == Finish.Lens) return LampMaterials.Shared.LensFor(source, source.name == "tail_glass");
            if (finished.TryGetValue(source, out Material m)) return m;
            m = Make(source, look);
            if (m != null) finished[source] = m;
            return m;
        }

        private static Material Make(Material source, Look look)
        {
            Color colour = ColourOf(source);
            string name = source.name + " (finished)";
            switch (look.Kind)
            {
                case Finish.Carbon:
                    if (carbon == null) carbon = Resources.Load<Shader>("CarPaint/Carbon");
                    return carbon == null ? null : new Material(carbon) { name = name, enableInstancing = true };
                case Finish.Glass:
                {
                    Material glass = LitMaterial(name, colour, 0f, look.Smoothness);
                    glass.SetColor(Lit.BaseColorId, new Color(colour.r, colour.g, colour.b, colour.a < 0.99f ? colour.a : GlassAlpha));
                    Lit.Glass(glass);
                    return glass;
                }
                case Finish.Mirror:
                case Finish.Metal:
                    return LitMaterial(name, colour, 1f, look.Smoothness);
                default:
                    return LitMaterial(name, colour, 0f, look.Smoothness);
            }
        }

        private static Material LitMaterial(string name, Color colour, float metallic, float smoothness)
        {
            var m = new Material(Lit.Shader) { name = name, enableInstancing = true };
            m.SetColor(Lit.BaseColorId, new Color(colour.r, colour.g, colour.b, 1f));
            m.SetFloat(Lit.MetallicId, metallic);
            m.SetFloat(Lit.SmoothnessId, smoothness);
            return m;
        }

        /// <summary>A model material's colour, with its see-through amount in alpha.</summary>
        internal static Color ColourOf(Material m) =>
            m.HasProperty(Lit.BaseColorId) ? m.GetColor(Lit.BaseColorId) : m.HasProperty("_Color") ? m.color : Color.white;
    }
}
