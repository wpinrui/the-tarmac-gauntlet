using System; using System.IO; using System.Linq; using System.Reflection; using UnityEngine; using TarmacGauntlet.Raceday; using TarmacGauntlet.Rendering;
static class P {
  static int fails;
  static void Expect(string what, bool ok) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) fails++; }
  static void Call(object o, string m, params object[] a) => o.GetType().GetMethod(m, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(o, a);
  static AudioSource[] Voices(string prefix) => GameObject.All.Where(g => g.name.StartsWith(prefix + " ")).Select(g => (AudioSource)g.Components[0]).ToArray();
  static string[] Playing(string prefix) => Voices(prefix).Where(s => s.isPlaying).Select(s => s.clip.name.Split('/')[1]).ToArray();
  static void Frame(params object[] late) { Time.Step(); foreach (var o in late) Call(o, "LateUpdate"); }

  static int Main(string[] args) {
    string disk = args[0];
    string[] banks = { "gun", "air", "jack-up", "jack-drop", "fuel-clunk", "fuel-flow", "hit-light", "hit-heavy", "hit-soft", "scrape" };

    // ---- the takes on disk
    foreach (string b in banks) {
      string d = Path.Combine(disk, b);
      int oggs = Directory.Exists(d) ? Directory.GetFiles(d, "*.ogg").Length : 0;
      bool metas = Directory.Exists(d) && Directory.GetFiles(d, "*.ogg").All(f => File.Exists(f + ".meta")) && File.Exists(d + ".meta");
      Expect($"{b}: takes on disk, each with its meta", oggs > 0 && metas);
      Resources.Takes["RaceSfx/" + b] = oggs;
    }
    Expect("no bank folder the game doesn't know", Directory.GetDirectories(disk).Select(Path.GetFileName).All(banks.Contains));
    Expect("several takes for each one-shot that repeats", new[] { "gun", "air", "jack-up", "hit-light", "hit-heavy", "hit-soft" }.All(b => Resources.Takes["RaceSfx/" + b] >= 3));

    // ---- voice budgets
    Expect("the B390's Medium plays 6 one-shots and 3 held sounds", SfxVoices.OneShotsAt(1) == 6 && SfxVoices.LoopsAt(1) == 3);
    Expect("Low plays fewest, Ultra most", SfxVoices.OneShotsAt(0) < SfxVoices.OneShotsAt(3) && SfxVoices.LoopsAt(0) < SfxVoices.LoopsAt(3));
    Expect("with the engines and tyres, Ultra stays inside Unity's 32 real voices", 10 + 8 + SfxVoices.OneShotsAt(3) + SfxVoices.LoopsAt(3) <= 32);
    Expect("full volume inside the near distance", SfxVoices.Heard(0.8f, 1f, 3f, 100f) == 0.8f);
    Expect("half at twice it", Math.Abs(SfxVoices.Heard(1f, 6f, 3f, 100f) - 0.5f) < 1e-6);
    Expect("silent past the far distance", SfxVoices.Heard(1f, 101f, 3f, 100f) == 0f);
    Expect("a free voice first", SfxVoices.Choose(new[] { true, false, true }, new[] { 0.1f, 0f, 0.9f }, 3, 0.05f) == 1);
    Expect("the quietest gives way to a louder sound", SfxVoices.Choose(new[] { true, true, true }, new[] { 0.5f, 0.2f, 0.9f }, 3, 0.3f) == 1);
    Expect("a quieter sound is dropped when all are busy", SfxVoices.Choose(new[] { true, true }, new[] { 0.5f, 0.2f }, 2, 0.1f) == -1);
    Expect("voices past the budget are never used", SfxVoices.Choose(new[] { true, false }, new[] { 0.5f, 0f }, 1, 0.1f) == -1);
    var r = new Random(3); int last = -1, repeats = 0; var seen = new bool[5];
    for (int i = 0; i < 2000; i++) { int t = SfxVoices.Take(5, last, r); if (t == last) repeats++; seen[t] = true; last = t; }
    Expect("never the same take twice running, and every take used", repeats == 0 && seen.All(s => s));

    // ---- hits and scrapes
    Expect("a touch under 1 m/s is silent", !Impacts.Of(HitKind.Hard, 0.6f, out _));
    Impacts.Of(HitKind.Hard, 3f, out var light); Impacts.Of(HitKind.Hard, 12f, out var heavy); Impacts.Of(HitKind.Hard, 60f, out var huge);
    Expect("3 m/s into armco knocks, 12 m/s crumples", light.Bank == SfxBank.HitLight && heavy.Bank == SfxBank.HitHeavy);
    Expect("louder the harder, and never past full", light.Volume < heavy.Volume && heavy.Volume < huge.Volume + 1e-6 && huge.Volume <= 1f);
    Impacts.Of(HitKind.Tyres, 12f, out var tyres); Impacts.Of(HitKind.Car, 12f, out var car);
    Expect("tyre walls thud", tyres.Bank == SfxBank.HitSoft);
    Expect("car on car is a little softer than car on steel", car.Bank == SfxBank.HitHeavy && car.Volume < heavy.Volume);
    Expect("no scrape under 1.5 m/s, or on tyre walls", !Impacts.Scrape(HitKind.Hard, 1f, out _, out _) && !Impacts.Scrape(HitKind.Tyres, 20f, out _, out _));
    Impacts.Scrape(HitKind.Hard, 5f, out float v5, out float p5); Impacts.Scrape(HitKind.Hard, 25f, out float v25, out float p25);
    Impacts.Scrape(HitKind.Car, 25f, out float vc, out float pc);
    Expect("a faster scrape is louder and higher", v25 > v5 && p25 > p5);
    Expect("paint on paint is quieter and duller", vc < v25 && pc < p25);

    // ---- pit cues
    var c = new PitCues(); var hub = new bool[4]; float lift = 0f; bool lance = false, nozzle = false;
    int jackUp = 0, vent = 0, drop = 0, gunOn = 0, gunOff = 0, nOn = 0, nOff = 0, fuelFrames = 0;
    for (int f = 0; f < 260; f++) {
      lance = f >= 10 && f < 160; nozzle = f >= 20 && f < 150;
      hub[0] = f >= 15 && f < 41 || f >= 43 && f < 50 || f >= 90 && f < 110;
      lift = Mathf.MoveTowards(lift, lance ? 1f : 0f, (1f / 60f) / 0.35f);
      c.Step(1f / 60f, hub, nozzle, lance, lift);
      if (f < 10) Expect("nothing before the stop", !c.JackUp && !c.JackDrop && !c.GunOn[0] && !c.NozzleOn);
      jackUp += c.JackUp ? 1 : 0; vent += c.JackVent ? 1 : 0; drop += c.JackDrop ? 1 : 0;
      gunOn += c.GunOn[0] ? 1 : 0; gunOff += c.GunOff[0] ? 1 : 0; nOn += c.NozzleOn ? 1 : 0; nOff += c.NozzleOff ? 1 : 0;
      fuelFrames += c.Fuelling ? 1 : 0;
      if (f == 15) Expect("the gun fires the frame it lands on the hub", c.GunOn[0]);
      if (f == 160) Expect("the jacks vent the frame the lance comes out", c.JackVent);
    }
    Expect("the jacks fire once", jackUp == 1 && vent == 1);
    Expect("the car thumps down once, only after it was up", drop == 1);
    Expect("the gun fires twice: undoing, then tightening, not again as it resettles", gunOn == 2);
    Expect("and vents each time it comes off", gunOff == 3);
    Expect("the nozzle clunks on and off once each", nOn == 1 && nOff == 1);
    Expect("fuel runs the whole time the nozzle is on", fuelFrames == 130);
    c.Reset(); c.Step(1f / 60f, new bool[4], false, false, 0f);
    Expect("a reset stop starts quiet", !c.JackDrop && !c.NozzleOff && !c.GunOff[0]);

    // ---- the voice pool, through a pit stop
    new GameObject("Camera").AddComponent<AudioListener>();
    var kitGo = new GameObject("Pit crews"); var kit = kitGo.AddComponent<PitKitWork>();
    var work = new KitWork(); kit.Works = new[] { work, new KitWork() }; kit.Lives = new[] { true, false };
    for (int k = 0; k < 13; k++) work.Pieces[k].Position = new Vector3(2f, 0f, 0f);
    var pit = kitGo.AddComponent<PitAudio>(); pit.Build(kit);
    Frame(pit);
    var sfx = GameObject.All.SingleOrDefault(g => g.name == "Race sounds")?.Components.OfType<RaceSfx>().SingleOrDefault();
    Expect("no sound until the stop begins", sfx == null || Playing("Sound").Length == 0);
    work.LanceIn = true; work.NozzleOn = true; for (int w = 0; w < 4; w++) work.OnHub[w] = true;
    kit.Works[1].LanceIn = true;
    sfx = null; Frame(pit); sfx = GameObject.All.Single(g => g.name == "Race sounds").Components.OfType<RaceSfx>().Single();
    var now = Playing("Sound");
    Expect("four guns, the jacks and the nozzle at once", now.Count(b => b == "gun") == 4 && now.Contains("jack-up") && now.Contains("fuel-clunk"));
    Expect("Medium caps it at 6 one-shots", now.Length == 6);
    Frame(sfx); Expect("fuel runs in as a held sound", Playing("Held sound").SequenceEqual(new[] { "fuel-flow" }));
    Expect("a box out of camera plays nothing", Voices("Sound").All(s => !s.isPlaying || s.transform.position.x < 5f));

    foreach (var s in Voices("Sound")) s.Stop();
    for (int i = 0; i < 6; i++) RaceSfx.Play(SfxBank.HitLight, new Vector3(10f, 0f, 0f), 0.5f, 8f, 320f);
    RaceSfx.Play(SfxBank.HitHeavy, new Vector3(300f, 0f, 0f), 0.5f, 8f, 320f);
    Expect("a far, quieter hit is dropped when every voice is busy", !Playing("Sound").Contains("hit-heavy"));
    RaceSfx.Play(SfxBank.HitHeavy, new Vector3(2f, 0f, 0f), 1f, 8f, 320f);
    Expect("a near, louder one takes a voice", Playing("Sound").Count(b => b == "hit-heavy") == 1 && Playing("Sound").Length == 6);
    RaceSfx.Play(SfxBank.Gun, new Vector3(500f, 0f, 0f), 1f, 2.5f, 90f);
    Expect("nothing past its far distance", !Playing("Sound").Contains("gun"));
    var pitches = Voices("Sound").Where(s => s.isPlaying).Select(s => s.pitch).ToArray();
    Expect("pitches vary a little, take to take", pitches.Distinct().Count() > 1 && pitches.All(p => p > 0.94f && p < 1.06f));

    QualityLevels.Level = 0; Time.unscaledTime += 2f; Frame(sfx);
    Expect("dropping to Low stops voices past its budget", Playing("Sound").Length <= 4);
    QualityLevels.Level = 1; Time.unscaledTime += 2f; Frame(sfx);

    // ---- a car's hits and scrapes
    foreach (var s in Voices("Sound")) s.Stop();
    work.NozzleOn = false; for (int i = 0; i < 30; i++) Frame(pit, sfx);
    Expect("the fuel stops with the nozzle off and fades out", Playing("Held sound").Length == 0);
    Expect("with a clunk as the nozzle comes off", Playing("Sound").SequenceEqual(new[] { "fuel-clunk" }));
    foreach (var s in Voices("Sound")) s.Stop();
    ImpactAudio Car(string n) { var g = new GameObject(n); var rb = g.AddComponent<Rigidbody>(); g.AddComponent<CarController>(); g.AddComponent<Collider>().attachedRigidbody = rb; return g.AddComponent<ImpactAudio>(); }
    var a = Car("Car 1"); var b2 = Car("Car 2");
    Collider Wall(string n, SurfaceKind k) { var g = new GameObject(n); g.AddComponent<TrackSurface>().Kind = k; return g.AddComponent<Collider>(); }
    Collision Hit(Collider other, Vector3 v) => new Collision { collider = other, relativeVelocity = v, Contacts = new[] { new ContactPoint { normal = new Vector3(1f, 0f, 0f), point = new Vector3(5f, 0f, 0f) } } };
    Collider bodyOf(ImpactAudio x) => x.gameObject.Components.OfType<Collider>().Single();
    Call(a, "OnCollisionEnter", Hit(bodyOf(b2), new Vector3(12f, 0f, 0f))); Call(b2, "OnCollisionEnter", Hit(bodyOf(a), new Vector3(-12f, 0f, 0f)));
    Expect("two cars meeting make one hit, not two", Playing("Sound").Length == 1 && Playing("Sound")[0] == "hit-heavy");
    foreach (var s in Voices("Sound")) s.Stop();
    Time.time += 1f;
    Call(a, "OnCollisionEnter", Hit(Wall("Armco", SurfaceKind.Barrier), new Vector3(3f, 0f, 0f)));
    Expect("a nudge into the armco knocks", Playing("Sound").SequenceEqual(new[] { "hit-light" }));
    Call(a, "OnCollisionEnter", Hit(Wall("Armco", SurfaceKind.Barrier), new Vector3(3f, 0f, 0f)));
    Expect("a second contact in the same moment doesn't hit again", Playing("Sound").Length == 1);
    Time.time += 1f;
    Call(b2, "OnCollisionEnter", Hit(Wall("Tyre walls", SurfaceKind.Barrier), new Vector3(15f, 0f, 0f)));
    Expect("into the tyre wall, a thud", Playing("Sound").Contains("hit-soft"));
    Time.time += 1f; int before = Playing("Sound").Length;
    Call(a, "OnCollisionEnter", Hit(Wall("Grass runoff", SurfaceKind.Grass), new Vector3(15f, 0f, 0f)));
    Expect("landing on grass is the tyres' business, not a hit", Playing("Sound").Length == before);
    var armco = Wall("Armco", SurfaceKind.Barrier);
    for (int i = 0; i < 20; i++) { Time.Step(); Call(a, "OnCollisionStay", Hit(armco, new Vector3(0.5f, 0f, 25f))); Call(a, "Update"); Call(sfx, "LateUpdate"); }
    var held = Voices("Held sound").Where(s => s.isPlaying).ToArray();
    Expect("grinding along the armco scrapes", held.Length == 1 && held[0].clip.name.StartsWith("RaceSfx/scrape") && held[0].volume > 0.5f);
    for (int i = 0; i < 30; i++) { Time.Step(); Call(a, "Update"); Call(sfx, "LateUpdate"); }
    Expect("and the scrape dies away once clear", Playing("Held sound").Length == 0);
    for (int i = 0; i < 5; i++) { Time.Step(); Call(b2, "OnCollisionStay", Hit(Wall("Tyre walls", SurfaceKind.Barrier), new Vector3(0f, 0f, 25f))); Call(b2, "Update"); Call(sfx, "LateUpdate"); }
    Expect("sliding along tyre walls doesn't scrape", Playing("Held sound").Length == 0);

    Console.WriteLine(fails == 0 ? "All checks passed." : $"{fails} checks failed.");
    return fails == 0 ? 0 : 1;
  }
}
