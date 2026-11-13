using System; using System.Linq; using UnityEngine; using UnityEngine.SceneManagement; using TarmacGauntlet.Audio; using TarmacGauntlet.Management; using TarmacGauntlet.Rendering;
static class P {
  const float Dt = 0.1f;
  static int fails;
  static void Expect(string what, bool ok) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) fails++; }
  static void Tick(int n) {
    var u = typeof(MenuMusic).GetMethod("Update", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
    for (int i = 0; i < n; i++) { foreach (var m in GameObject.Live.ToList()) u.Invoke(m, null); Mix.Tick(Dt, Dt); }
  }
  static float Race => Mix.Gain(SoundGroup.Engines) / (Mix.Taper(Mix.Position(VolumeSlider.Cars)) * Mix.Trim(SoundGroup.Engines));
  static int Main() {
    Resources.Clips = Enumerable.Range(0, 5).Select(i => new AudioClip { name = "t" + i }).ToArray();
    SceneManager.Active = new Scene { name = "TeamHq" };
    var hqEar = new AudioListener();
    typeof(MenuMusic).GetMethod("Hook", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null, null);
    var mm = (MenuMusic)GameObject.Live.Single();
    var src = (AudioSource)typeof(MenuMusic).GetField("source", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(mm);
    Tick(1); Expect("a track starts as the HQ opens", src.isPlaying && src.Played.Count == 1);
    float level = Mix.Gain(SoundGroup.Music);
    Tick(30); Expect("fades in to the music group's level in 2 s", level > 0 && Math.Abs(src.volume - level) < 1e-4);
    Expect("plays past the listener's muffle", src.bypassListenerEffects);
    for (int k = 0; k < 19; k++) { src.isPlaying = false; Tick(21); }
    var order = src.Played.Select(c => c.name).ToList();
    Expect("20 tracks played, 2 s apart", order.Count == 20);
    Expect("every track once per round of five", Enumerable.Range(0, 4).All(d => order.Skip(d * 5).Take(5).Distinct().Count() == 5));
    Expect("never the same track twice running", order.Zip(order.Skip(1)).All(p => p.First != p.Second));

    // HQ to race: the music plays on through the whole load, then crosses over.
    SceneLoader.Load("Raceday"); Tick(10);
    Expect("plays on at full as the race starts loading", Math.Abs(src.volume - level) < 1e-4 && src.isPlaying);
    Expect("the race is silent behind the loading screen", Race == 0);
    AudioListener.All.Remove(hqEar); SceneManager.Fire(new Scene { name = "Raceday" }); Tick(1);
    Expect("carries its own listener while the scene has none", AudioListener.All.Count == 1);
    Tick(80); Expect("still at full while the race builds for 8 s", Math.Abs(src.volume - level) < 1e-4);
    var raceEar = new AudioListener(); Tick(1);
    Expect("drops its listener once the race brings one", AudioListener.All.Count == 1 && AudioListener.All[0] == raceEar);
    SceneLoader.Arrive(); Tick(12); SceneLoader.Done();
    float m = src.volume / level, r = Race;
    Expect("halfway through the crossfade both sides are up", m > 0.4f && m < 0.95f && r > 0.4f && r < 0.95f);
    Expect("and they hold the loudness steady (equal power)", Math.Abs(m * m + r * r - 1) < 0.05f);
    Tick(14); Expect("race at full and the music silent and stopped 2.5 s after the race appears", Race == 1 && src.volume == 0 && !src.isPlaying);
    Tick(50); Expect("stays silent in the race", src.volume == 0 && src.Played.Count == 20);

    // Race to HQ: the music rises as the race falls, from the moment the load starts.
    SceneLoader.Load("TeamHq"); Tick(1);
    Expect("a fresh track starts as the HQ starts loading", src.isPlaying && src.Played.Count == 21);
    Tick(12); Expect("crossing back: both sides up halfway", src.volume / level > 0.4f && Race > 0.4f && Race < 0.95f);
    Tick(14); Expect("music at full and the race silent 2.5 s after", Math.Abs(src.volume - level) < 1e-4 && Race == 0);
    AudioListener.All.Remove(raceEar); SceneManager.Fire(new Scene { name = "TeamHq" }); Tick(5);
    Expect("still heard while neither scene has a listener", AudioListener.All.Count == 1 && src.volume > 0);
    new AudioListener(); SceneLoader.Arrive(); Tick(2); SceneLoader.Done(); Tick(1);
    Expect("back in the HQ: music on, the race's groups reset, one listener", src.isPlaying && Race == 1 && AudioListener.All.Count == 1);
    Expect("one music player across scenes", GameObject.Live.Count == 1);
    return fails == 0 ? 0 : 1;
  }
}
