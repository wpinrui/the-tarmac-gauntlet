using System; using System.Linq; using UnityEngine; using UnityEngine.SceneManagement; using TarmacGauntlet.Management; using TarmacGauntlet.Rendering;
static class P {
  static int fails;
  static void Expect(string what, bool ok) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) fails++; }
  static void Tick(int n) { var u = typeof(MenuMusic).GetMethod("Update", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance); for (int i = 0; i < n; i++) foreach (var m in GameObject.Live.ToList()) u.Invoke(m, null); }
  static int Main() {
    Resources.Clips = Enumerable.Range(0, 5).Select(i => new AudioClip { name = "t" + i }).ToArray();
    SceneManager.Active = new Scene { name = "TeamHq" };
    typeof(MenuMusic).GetMethod("Hook", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null, null);
    var mm = (MenuMusic)GameObject.Live.Single(); AudioSource src;
    src = (AudioSource)typeof(MenuMusic).GetField("source", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(mm);
    Tick(1); Expect("a track starts as the HQ opens", src.isPlaying && src.Played.Count == 1);
    float level = TarmacGauntlet.Audio.Mix.Gain(TarmacGauntlet.Audio.SoundGroup.Music);
    Tick(30); Expect("fades in to the music group's level in 2 s", level > 0 && Math.Abs(src.volume - level) < 1e-4);
    Expect("plays past the listener's muffle", src.bypassListenerEffects);
    for (int k = 0; k < 19; k++) { src.isPlaying = false; Tick(21); }
    var order = src.Played.Select(c => c.name).ToList();
    Expect("20 tracks played, 2 s apart", order.Count == 20);
    bool decks = Enumerable.Range(0, 4).All(d => order.Skip(d * 5).Take(5).Distinct().Count() == 5);
    Expect("every track once per round of five", decks);
    Expect("never the same track twice running", order.Zip(order.Skip(1)).All(p => p.First != p.Second));
    SceneLoader.Load(); Tick(4); Expect("fading out as a race starts loading", src.volume > 0 && src.volume < level);
    Tick(5); Expect("silent and stopped 0.8 s after", src.volume == 0 && !src.isPlaying);
    SceneManager.Fire(new Scene { name = "Raceday" }); Tick(50); Expect("stays silent in the race", src.volume == 0 && !src.isPlaying && src.Played.Count == 20);
    SceneManager.Fire(new Scene { name = "TeamHq" }); Tick(1); Expect("a fresh track starts at once back in the HQ", src.isPlaying && src.Played.Count == 21);
    Expect("one music player across scenes", GameObject.Live.Count == 1);
    return fails == 0 ? 0 : 1;
  }
}
