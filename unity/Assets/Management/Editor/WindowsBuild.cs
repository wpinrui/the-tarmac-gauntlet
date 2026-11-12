using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
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
            if (Directory.Exists(BuildFolder)) Directory.Delete(BuildFolder, true);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(BuildFolder, ExeName),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"Built {GameName} to {BuildFolder} in {report.summary.totalTime.TotalSeconds:0}s");
                return true;
            }
            EditorUtility.DisplayDialog("Build failed", $"The build {report.summary.result.ToString().ToLowerInvariant()} with {report.summary.totalErrors} errors. The Console has the details.", "OK");
            return false;
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
