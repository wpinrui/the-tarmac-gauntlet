using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Visuals;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TarmacGauntlet.Management.Editor
{
    /// <summary>
    /// One click Windows builds. "Build" writes the game to Builds/Windows next to Assets. "Build and Install" then
    /// copies it to %LOCALAPPDATA%\Programs\The Tarmac Gauntlet, replacing the last install, and puts the game on the
    /// Start menu and the desktop.
    /// </summary>
    internal static class WindowsBuild
    {
        private const string GameName = "The Tarmac Gauntlet";

        // Unity writes these next to the exe for debugging only. They are not part of the game.
        private static readonly string[] DoNotShip = { "_BurstDebugInformation_DoNotShip", "_BackUpThisFolder_ButDontShipItWithYourGame" };

        private static string BuildFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Windows"));

        private static string InstallFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", GameName);

        private static string ExeName => (string.IsNullOrWhiteSpace(PlayerSettings.productName) ? GameName : PlayerSettings.productName) + ".exe";

        [MenuItem("Tarmac Gauntlet/Build", priority = 100)]
        private static void BuildOnly()
        {
            if (Build()) EditorUtility.RevealInFinder(Path.Combine(BuildFolder, ExeName));
        }

        [MenuItem("Tarmac Gauntlet/Build and Install", priority = 101)]
        private static void BuildAndInstall()
        {
            if (!Build()) return;
            try
            {
                Install();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Install failed", e.Message + "\n\nClose the game if it is running and try again.", "OK");
                return;
            }
            Debug.Log($"Installed {GameName} to {InstallFolder}");
        }

        [MenuItem("Tarmac Gauntlet/Build", true)]
        [MenuItem("Tarmac Gauntlet/Build and Install", true)]
        private static bool CanBuild() => !EditorApplication.isPlayingOrWillChangePlaymode && !BuildPipeline.isBuildingPlayer;

        private static bool Build()
        {
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            FillArtLibrary();
            IncludeRuntimeShaders();
            FillLitVariants();
            // ProjectSettings is not in the repository, so this is set here: the collector clears garbage a little each
            // frame instead of stopping the game to clear it all at once.
            PlayerSettings.gcIncremental = true;
            // A release build only records CPU and GPU frame times for the FPS counter with this on.
            PlayerSettings.enableFrameTimingStats = true;
            if (Directory.Exists(BuildFolder)) Directory.Delete(BuildFolder, true);
            UnityEngine.Rendering.RenderPipelineAsset[] pipelines = UseQualityAssets();
            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = Path.Combine(BuildFolder, ExeName),
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                });
            }
            finally
            {
                PutBackQualityAssets(pipelines);
            }
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"Built {GameName} to {BuildFolder} in {report.summary.totalTime.TotalSeconds:0}s");
                return true;
            }
            EditorUtility.DisplayDialog("Build failed", $"The build {report.summary.result.ToString().ToLowerInvariant()} with {report.summary.totalErrors} errors. The Console has the details.", "OK");
            return false;
        }

        private const string ArtFolder = "Assets/Art";
        private const string LibraryFolder = "Assets/Visuals/Generated/Resources";

        /// <summary>
        /// A built game has no asset database, so every asset under Assets/Art goes into the ArtLibrary resource,
        /// which the game loads by path instead.
        /// </summary>
        private static void FillArtLibrary()
        {
            var entries = AssetDatabase.FindAssets("", new[] { ArtFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !AssetDatabase.IsValidFolder(path))
                .Select(path => new ArtLibrary.Entry { Path = path, Asset = AssetDatabase.LoadMainAssetAtPath(path) })
                .Where(e => e.Asset != null && !(e.Asset is DefaultAsset))
                .ToArray();
            string assetPath = LibraryFolder + "/" + ArtLibrary.ResourceName + ".asset";
            var library = AssetDatabase.LoadAssetAtPath<ArtLibrary>(assetPath);
            if (library == null)
            {
                Directory.CreateDirectory(LibraryFolder);
                AssetDatabase.Refresh();
                library = ScriptableObject.CreateInstance<ArtLibrary>();
                AssetDatabase.CreateAsset(library, assetPath);
            }
            library.Entries = entries;
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// The game makes its menu panels in code, so nothing in a scene references UI Toolkit's runtime shaders, and
        /// the build would leave them out. They go on the Always Included list.
        /// </summary>
        private static void IncludeRuntimeShaders()
        {
            string[] prefixes = { "Hidden/UIE-", "Hidden/TextCore/", "TextMeshPro/" };
            Shader[] wanted = ShaderUtil.GetAllShaderInfo()
                .Select(info => info.name)
                .Where(name => prefixes.Any(prefix => name.StartsWith(prefix)) && !name.Contains("Editor"))
                .Select(Shader.Find)
                .Where(shader => shader != null)
                .ToArray();
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            SerializedProperty list = settings.FindProperty("m_AlwaysIncludedShaders");
            var have = Enumerable.Range(0, list.arraySize).Select(i => list.GetArrayElementAtIndex(i).objectReferenceValue).ToList();
            foreach (Shader shader in wanted.Where(s => !have.Contains(s)))
            {
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            }
            if (settings.ApplyModifiedPropertiesWithoutUndo()) AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// URP keeps only the shader variants the build's pipeline assets use, and finds those assets on Unity's quality
        /// levels. The game picks its own asset in code (QualityLevels), so while the build runs each of Unity's levels holds
        /// one of the game's, Low to Ultra. Without this the variants only High and Ultra use, such as their soft shadows,
        /// the other lights' shadows and the screen shadows, would be left out. Returns the levels' own assets.
        /// </summary>
        private static UnityEngine.Rendering.RenderPipelineAsset[] UseQualityAssets()
        {
            int current = QualitySettings.GetQualityLevel();
            var before = new UnityEngine.Rendering.RenderPipelineAsset[QualitySettings.count];
            if (before.Length < QualityLevels.Names.Length)
                Debug.LogWarning($"Build: Unity has {before.Length} quality levels, fewer than the game's {QualityLevels.Names.Length}, so some of its shader variants may be left out.");
            for (int i = 0; i < before.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                before[i] = QualitySettings.renderPipeline;
                string name = QualityLevels.Names[Mathf.Min(i, QualityLevels.Names.Length - 1)];
                QualitySettings.renderPipeline = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>(
                    "Assets/Rendering/Resources/Quality/" + name + ".asset");
            }
            QualitySettings.SetQualityLevel(current, false);
            return before;
        }

        private static void PutBackQualityAssets(UnityEngine.Rendering.RenderPipelineAsset[] before)
        {
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < before.Length && i < QualitySettings.count; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = before[i];
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        /// <summary>
        /// The game makes its URP Lit materials in code, so the build only keeps the Lit variants some built material
        /// uses. One material per mix of Lit.cs modes and emission, in a Resources folder, keeps every one the game
        /// can ask for.
        /// </summary>
        private static void FillLitVariants()
        {
            string folder = LibraryFolder + "/LitVariants";
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            string[] surfaces = { "Opaque", "Cutout", "Fade", "Glass" };
            for (int i = 0; i < surfaces.Length * 8; i++)
            {
                var m = new Material(Lit.Shader);
                string surface = surfaces[i / 8];
                if (surface == "Cutout") Lit.Cutout(m);
                if (surface == "Fade") Lit.Fade(m);
                if (surface == "Glass") Lit.Glass(m);
                bool normal = (i & 1) != 0, metallic = (i & 2) != 0, emission = (i & 4) != 0;
                if (normal) Lit.NormalMap(m, null);
                if (metallic) Lit.MetallicSmoothnessMap(m, null);
                if (emission) m.EnableKeyword("_EMISSION");
                AssetDatabase.CreateAsset(m, $"{folder}/{surface}{(normal ? "Normal" : "")}{(metallic ? "Metallic" : "")}{(emission ? "Emission" : "")}.mat");
            }
            AssetDatabase.SaveAssets();
        }

        private static void Install()
        {
            if (Directory.Exists(InstallFolder)) Directory.Delete(InstallFolder, true);
            Copy(BuildFolder, InstallFolder);
            string exe = Path.Combine(InstallFolder, ExeName);
            string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), GameName + ".lnk");
            string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), GameName + ".lnk");
            Shortcut(startMenu, exe);
            Shortcut(desktop, exe);
        }

        private static void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(from))
            {
                string name = Path.GetFileName(dir);
                if (DoNotShip.Any(suffix => name.EndsWith(suffix))) continue;
                Copy(dir, Path.Combine(to, name));
            }
        }

        private static void Shortcut(string link, string target)
        {
            string Quote(string s) => "'" + s.Replace("'", "''") + "'";
            string script = $"$s = (New-Object -ComObject WScript.Shell).CreateShortcut({Quote(link)}); " +
                            $"$s.TargetPath = {Quote(target)}; $s.WorkingDirectory = {Quote(Path.GetDirectoryName(target))}; $s.Save()";
            using Process process = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            });
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new IOException($"Could not make the shortcut {link}: {error}");
        }
    }
}
