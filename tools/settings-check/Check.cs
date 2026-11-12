using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TarmacGauntlet.Management;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Visuals;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// "first" launches the game's settings code fresh, changes every setting the way the settings menu does, and quits.
/// "second" launches again from the saved file, with the screen opening at its defaults as though Unity remembered
/// nothing, and checks every setting came back. Both scan the game's scripts for anything else that writes a setting,
/// such as a scene resetting one when it loads.
/// </summary>
internal static class Check
{
    private static int failed;

    // What "first" sets, each away from its default and from the High quality level's value.
    private const int Mode = 2; // Windowed
    private static readonly Vector2Int Size = new Vector2Int(1280, 720);
    private const int Quality = 2; // High
    private const bool VSync = false;
    private const float Volume = 0.35f;
    private const int RealLights = 17;

    private static int Main(string[] args)
    {
        PlayerPrefs.File = args[1];
        Launch();
        if (args[0] == "first")
        {
            Expect("a fresh game starts on Medium", GameSettings.Quality, 1);
            Expect("a fresh game starts with every option at its default", GraphicsOptions.All.Count(o => o.Value != o.Default), 0);
            GameSettings.DisplayMode = Mode;
            GameSettings.Resolution = Size;
            GameSettings.Quality = Quality;
            foreach (GraphicsRow row in GraphicsOptions.Rows) row.Value = Changed(row);
            GameSettings.VSync = VSync;
            GameSettings.Volume = Volume;
            Application.Quit();
        }
        else
        {
            Expect("display mode", GameSettings.DisplayModes[GameSettings.DisplayMode].Name, GameSettings.DisplayModes[Mode].Name);
            Expect("resolution", GameSettings.Resolution, Size);
            Expect("quality", GameSettings.QualityNames[GameSettings.Quality], GameSettings.QualityNames[Quality]);
            Expect("quality's pipeline asset in use", QualitySettings.renderPipeline?.name, "Quality/" + QualityLevels.Names[Quality]);
            Expect("v-sync", GameSettings.VSync, VSync);
            Expect("volume", GameSettings.Volume, Volume);
            foreach (GraphicsRow row in GraphicsOptions.Rows)
            {
                Expect(row.Label, row.Value, Changed(row));
                foreach (GraphicsOption o in row.Options) Expect(row.Label + ": " + o.Key, o.Value, Changed(row));
            }
            var occlusion = (ScriptableRendererFeature)Resources.Load<UniversalRendererData>("Quality/Renderer").rendererFeatures[0];
            Expect("ambient occlusion drawn as its option says", occlusion.isActive, GraphicsOptions.AmbientOcclusion.On);
        }
        ScanForOtherWriters(args[2]);
        Console.WriteLine(failed == 0 ? $"settings-check {args[0]}: all passed" : $"settings-check {args[0]}: {failed} FAILED");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>A row's value that differs from its High value and its default: off for a toggle, a set count otherwise.</summary>
    private static int Changed(GraphicsRow row) => row.IsToggle ? 0 : RealLights;

    /// <summary>What a launch does before the first scene: the screen opens at its defaults and every boot hook runs.</summary>
    private static void Launch()
    {
        Screen.width = 1920;
        Screen.height = 1080;
        Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
        QualitySettings.vSyncCount = 1;
        AudioListener.volume = 1;
        QualitySettings.renderPipeline = null;
        IEnumerable<MethodInfo> hooks = typeof(GameSettings).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(m => m.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>() != null);
        foreach (MethodInfo hook in hooks) hook.Invoke(null, null);
    }

    /// <summary>
    /// Only the settings code and the settings menu may write a setting. Anything else that does, such as a scene putting
    /// back a default as it loads or a quality level reapplied at start, would lose the player's choice.
    /// </summary>
    private static void ScanForOtherWriters(string assets)
    {
        string[] allowed = { "GameSettings.cs", "SettingsView.cs", "GraphicsOption.cs", "GraphicsOptions.cs", "GraphicsRow.cs", "QualityLevels.cs" };
        var writes = new Regex(@"ApplyQuality\s*\(|PlayerPrefs\.Delete|vSyncCount\s*=[^=]|fullScreenMode\s*=[^=]|SetResolution\s*\(|QualityLevels\.Level\s*=[^=]|AudioListener\.volume\s*=[^=]|GraphicsOptions\.\w+\.(Value|On)\s*=[^=]|GameSettings\.\w+\s*=[^=]|QualitySettings\.SetQualityLevel");
        foreach (string file in Directory.GetFiles(assets, "*.cs", SearchOption.AllDirectories))
        {
            if (allowed.Contains(Path.GetFileName(file)) || file.Replace('\\', '/').Contains("/Editor/")) continue;
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
                if (writes.IsMatch(lines[i]))
                    Fail($"only the settings code writes a setting: {Path.GetRelativePath(assets, file)}:{i + 1}: {lines[i].Trim()}");
        }
    }

    private static void Expect<T>(string what, T got, T want)
    {
        if (EqualityComparer<T>.Default.Equals(got, want)) Console.WriteLine("ok   " + what);
        else Fail($"{what}: got {got}, want {want}");
    }

    private static void Fail(string message)
    {
        failed++;
        Console.WriteLine("FAIL " + message);
    }
}
