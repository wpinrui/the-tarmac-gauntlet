using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Rendering.Editor
{
    /// <summary>
    /// Counts the shader variants a build compiles, after every stripper has run, and logs them when the build ends: the
    /// fragment programs of every shader together, and of each pass of the game's own shaders (TarmacGauntlet/...), so a
    /// build shows what it cost and an Ultra-only build can be set against a full one.
    /// </summary>
    internal sealed class ShaderVariantTally : IPreprocessBuildWithReport, IPreprocessShaders, IPostprocessBuildWithReport
    {
        private const string Ours = "TarmacGauntlet/";
        private static readonly Dictionary<string, int> passes = new Dictionary<string, int>();
        private static int total;

        // Last, after URP's and Unity's strippers.
        public int callbackOrder => int.MaxValue;

        public void OnPreprocessBuild(BuildReport report)
        {
            passes.Clear();
            total = 0;
        }

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (snippet.shaderType != ShaderType.Fragment) return;
            total += data.Count;
            if (!shader.name.StartsWith(Ours)) return;
            string key = shader.name + " " + snippet.passName;
            passes.TryGetValue(key, out int n);
            passes[key] = n + data.Count;
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            var log = new StringBuilder($"Shader variants compiled: {total} fragment programs in all.");
            foreach (KeyValuePair<string, int> p in passes.OrderByDescending(p => p.Value))
                log.Append($"\n  {p.Key}: {p.Value}");
            Debug.Log(log.ToString());
        }
    }
}
