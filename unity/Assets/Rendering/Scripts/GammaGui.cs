using System;
using UnityEngine;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// The race's IMGUI overlays were made while the game rendered in Gamma colour space. In Linear, IMGUI still draws
    /// its colours as they are, and the frame then turns them to sRGB again, washing them out. Drawn with sRGB writes off,
    /// as Unity advises, they blend and show just as they did: <c>using (GammaGui.Begin()) { ... }</c> round the drawing.
    /// </summary>
    public readonly struct GammaGui : IDisposable
    {
        private readonly bool was;

        private GammaGui(bool was) => this.was = was;

        public static GammaGui Begin()
        {
            var g = new GammaGui(GL.sRGBWrite);
            GL.sRGBWrite = false;
            return g;
        }

        public void Dispose() => GL.sRGBWrite = was;
    }
}
