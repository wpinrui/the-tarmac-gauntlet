using System;
using System.Collections.Generic;
using TarmacGauntlet.Visuals;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Puts a team's livery on a car's paint, with the team's race number in the livery's number
    /// boxes. The livery is an atlas and a livery.json baked by tools/liveries/common/bake.js for
    /// one model; it is never put on another. A livery folder can hold its own Digits.png and
    /// Digits.json (tools/liveries/common/digits.js), drawing its numbers in them instead of the
    /// shared digits. See docs/car-paint.md.
    /// </summary>
    public static class CarLivery
    {
        [Serializable]
        private class Panel
        {
            public string view;
            public float[] a;
            public float[] b;
        }

        [Serializable]
        private class NumberBox
        {
            public float x, y, w, h, rotate;
            public string ink;
            // The digits' outline colour, for digits baked with one. Empty: the ink.
            public string edge;
        }

        [Serializable]
        private class LiveryFile
        {
            public string model;
            public int size;
            public string @base;
            public Panel[] panels;
            public NumberBox[] numbers;
        }

        [Serializable]
        private class DigitFile
        {
            public float cell, height, pad, padX;
            public float[] advance;
            // How far apart digits sit, over their height; 0: their advance.
            public float step;
        }

        private static readonly string[] Views = { "left", "right", "top", "front", "rear" };
        private static readonly string[] Props = { "Left", "Right", "Top", "Front", "Rear" };
        private const int MaxBoxes = 4;

        private static Shader shader;
        private static readonly Dictionary<string, (Texture2D Png, DigitFile File)> Digits = new Dictionary<string, (Texture2D, DigitFile)>();
        private static readonly Dictionary<string, (LiveryFile File, Texture2D Atlas)> Loaded = new Dictionary<string, (LiveryFile, Texture2D)>();

        /// <summary>
        /// Paints the model in the livery at liveryPath, numbered. False, leaving the model as it
        /// was, when the livery cannot be loaded or was drawn for another model.
        /// </summary>
        public static bool Apply(GameObject model, string modelPath, string liveryPath, int number)
        {
            // Each car gets its own material, built whole: a copied material keeps only the
            // properties its shader declares, so the projection vectors and number arrays would
            // come out zero and the car would show the base colour alone.
            Material painted = Build(liveryPath, modelPath);
            if (painted == null) return false;
            SetNumber(painted, number);
            // The shader projects in the car's own space, in metres. Each part's mesh space can carry
            // the importer's scale or offset, so give every painted part its way into the car's
            // space, taken now, with the doors shut.
            Transform car = model.transform.parent != null ? model.transform.parent : model.transform;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            {
                Material[] mats = r.sharedMaterials;
                Matrix4x4 toCar = car.worldToLocalMatrix * r.transform.localToWorldMatrix;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || mats[i].name != CarPaint.PaintMaterial) continue;
                    mats[i] = painted;
                    changed = true;
                    var block = new MaterialPropertyBlock();
                    block.SetMatrix("_ToCar", toCar);
                    r.SetPropertyBlock(block, i);
                }
                if (changed) r.sharedMaterials = mats;
            }
            return true;
        }

        /// <summary>The livery's file and atlas, loaded once; null file if it cannot be loaded.</summary>
        private static (LiveryFile File, Texture2D Atlas) Load(string liveryPath)
        {
            if (Loaded.TryGetValue(liveryPath, out var cached)) return cached;
            string json = LoadText(liveryPath + "/livery.json");
            Texture2D atlas = LoadTexture(liveryPath + "/atlas.png");
            LiveryFile file = string.IsNullOrEmpty(json) || atlas == null ? null : JsonUtility.FromJson<LiveryFile>(json);
            var made = (file?.panels == null ? null : file, atlas);
            Loaded[liveryPath] = made;
            return made;
        }

        /// <summary>The livery's base paint colour, the team's own colour; null when the livery cannot be loaded.</summary>
        public static Color? BaseColour(string liveryPath)
        {
            LiveryFile file = Load(liveryPath).File;
            return file != null && ColorUtility.TryParseHtmlString(file.@base, out Color c) ? c : null;
        }

        /// <summary>A material in the livery, without a number; null if it cannot be used on this model.</summary>
        private static Material Build(string liveryPath, string modelPath)
        {
            (LiveryFile file, Texture2D atlas) = Load(liveryPath);
            if (file == null || file.model != modelPath) return null;
            if (shader == null) shader = Shader.Find("TarmacGauntlet/Livery");
            if (shader == null) return null;

            var mat = new Material(shader) { name = CarPaint.PaintMaterial + " (livery)" };
            mat.SetTexture("_Atlas", atlas);
            if (ColorUtility.TryParseHtmlString(file.@base, out Color baseColour)) mat.SetColor("_Base", baseColour);
            foreach (Panel p in file.panels)
            {
                int k = Array.IndexOf(Views, p.view);
                if (k < 0) continue;
                mat.SetVector("_A" + Props[k], new Vector4(p.a[0], p.a[1], p.a[2], p.a[3]));
                mat.SetVector("_B" + Props[k], new Vector4(p.b[0], p.b[1], p.b[2], p.b[3]));
            }

            var boxes = new Vector4[MaxBoxes];
            var inks = new Vector4[MaxBoxes];
            var edges = new Vector4[MaxBoxes];
            var widths = Vector4.zero;
            int count = 0;
            foreach (NumberBox n in file.numbers ?? Array.Empty<NumberBox>())
            {
                if (count == MaxBoxes) break;
                boxes[count] = new Vector4(n.x / file.size, n.y / file.size, n.h / file.size, n.rotate * Mathf.Deg2Rad);
                widths[count] = n.w / file.size;
                inks[count] = ColorUtility.TryParseHtmlString(n.ink, out Color ink) ? (Vector4)ink : Vector4.one;
                edges[count] = ColorUtility.TryParseHtmlString(n.edge, out Color edge) ? (Vector4)edge : inks[count];
                count++;
            }
            mat.SetVectorArray("_NumBox", boxes);
            mat.SetVectorArray("_NumInk", inks);
            mat.SetVectorArray("_NumEdge", edges);
            mat.SetVector("_NumWidth", widths);
            mat.SetFloat("_NumBoxes", count);

            (Texture2D digits, DigitFile digitFile) = LoadDigits(liveryPath);
            if (digits != null)
            {
                float advance = digitFile.advance[0];
                float glyph = advance * digitFile.height * digits.height / digits.width;
                mat.SetTexture("_Digits", digits);
                mat.SetVector("_DigitCell", new Vector4(digitFile.cell, digitFile.padX, glyph, digitFile.pad));
                mat.SetFloat("_DigitHeight", digitFile.height);
                mat.SetFloat("_DigitAdvance", advance);
                mat.SetFloat("_DigitStep", digitFile.step > 0 ? digitFile.step : advance);
            }
            return mat;
        }

        /// <summary>The number's digits, most significant first, for the shader. 0 or less draws none.</summary>
        private static void SetNumber(Material mat, int number)
        {
            if (number <= 0 || mat.GetTexture("_Digits") == null)
            {
                mat.SetVector("_NumDigits", Vector4.zero);
                return;
            }
            string s = Mathf.Min(number, 999).ToString();
            var d = new Vector4(0f, 0f, 0f, s.Length);
            for (int i = 0; i < s.Length; i++) d[i] = s[i] - '0';
            mat.SetVector("_NumDigits", d);
        }

        /// <summary>The livery's own digits if its folder holds them, else the shared ones; null png if neither loads.</summary>
        private static (Texture2D Png, DigitFile File) LoadDigits(string liveryPath)
        {
            if (Digits.TryGetValue(liveryPath, out var cached)) return cached;
            Texture2D png = LoadTexture(liveryPath + "/Digits.png");
            string json = LoadText(liveryPath + "/Digits.json");
            if (png == null || string.IsNullOrEmpty(json))
            {
                png = Resources.Load<Texture2D>("Livery/Digits");
                json = Resources.Load<TextAsset>("Livery/Digits")?.text;
            }
            DigitFile file = png == null || string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<DigitFile>(json);
            (Texture2D, DigitFile) made = file?.advance != null && file.advance.Length == 10 ? (png, file) : (null, null);
            Digits[liveryPath] = made;
            return made;
        }

        private static string LoadText(string path) => ArtLibrary.Load<TextAsset>(path)?.text;

        private static Texture2D LoadTexture(string path) => ArtLibrary.Load<Texture2D>(path);
    }
}
