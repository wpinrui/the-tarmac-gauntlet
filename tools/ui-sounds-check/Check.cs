using System; using System.IO; using System.Linq; using System.Text.RegularExpressions; using TarmacGauntlet.Management;
static class P {
  static int fails;
  static void Expect(string what, bool ok) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) fails++; }
  static string[] C(params string[] c) => c;
  static int Main(string[] args) {
    string root = args[0], res = Path.Combine(root, "unity/Assets/Management/Resources");

    foreach (UiCue cue in Enum.GetValues(typeof(UiCue))) {
      string clip = Path.Combine(res, "UiSounds", MenuSoundRules.ClipName(cue) + ".wav");
      Expect($"{cue} has its clip and import settings", File.Exists(clip) && File.Exists(clip + ".meta"));
    }

    Expect("a hub item hovers and clicks", MenuSoundRules.Hovers(C("item", "hub-item")) && MenuSoundRules.PressCue(C("item", "hub-item")) == UiCue.Click);
    Expect("a dimmed hub item doesn't hover, and a click on it is the error", !MenuSoundRules.Hovers(C("item", "dim")) && MenuSoundRules.PressCue(C("item", "dim")) == UiCue.Error);
    Expect("an unaffordable Hire doesn't hover, and a click on it is the error", !MenuSoundRules.Hovers(C("red", "panel-button", "disabled")) && MenuSoundRules.PressCue(C("red", "disabled")) == UiCue.Error);
    Expect("a crew figure you can't pay for is the error", MenuSoundRules.PressCue(C("member", "disabled")) == UiCue.Error);
    Expect("a list row hovers only when it can be clicked", MenuSoundRules.Hovers(C("row", "click")) && !MenuSoundRules.IsPressable(C("row")));
    Expect("a settings row is not pressable, its stepper is", !MenuSoundRules.IsPressable(C("setting")) && MenuSoundRules.IsPressable(C("chevron")));
    Expect("an empty lineup slot doesn't hover", !MenuSoundRules.Hovers(C("tile", "slot", "empty")));
    Expect("a plain label is silent", !MenuSoundRules.IsPressable(C("cell", "fill")));

    // Every pressable class is one Menus.uss gives a hover look, so the sound and the look agree.
    string uss = File.ReadAllText(Path.Combine(res, "Menus.uss"));
    var hovered = Regex.Matches(uss, @"([.\w-]+):hover").Select(m => m.Groups[1].Value).ToList();
    foreach (string[] need in new[] { C("item"), C("row", "click"), C("tab"), C("chevron"), C("red"), C("ghost-button"), C("small-button"), C("tile", "click"),
                                      C("popup-item"), C("dropdown"), C("skill-step"), C("slot"), C("chip"), C("ng-shape"), C("np-close"), C("head", "sort") })
      Expect($"{string.Join(".", need)} lights up on hover in Menus.uss", hovered.Any(h => need.All(n => h.Split('.', StringSplitOptions.RemoveEmptyEntries).Contains(n))));

    var q = new CueQueue();
    q.Ask(UiCue.Hover); q.Ask(UiCue.Click); q.Ask(UiCue.Confirm); q.Ask(UiCue.Race); q.Ask(UiCue.Glide);
    var due = q.Take();
    Expect("a click on Race plays Race alone on the touch layer", due.Count == 2 && due[0] == UiCue.Race);
    Expect("the camera glide plays under it", due.Count == 2 && due[1] == UiCue.Glide);
    Expect("the queue is empty after a frame", q.Take().Count == 0);
    q.Ask(UiCue.Go); q.Ask(UiCue.Click);
    Expect("a weaker cue later in the frame doesn't replace a stronger one", q.Take().Single() == UiCue.Go);
    q.Ask(UiCue.Click); q.Ask(UiCue.Error);
    Expect("the error wins over the click it came with", q.Take().Single() == UiCue.Error);
    q.Ask(UiCue.Click); q.Ask(UiCue.DropdownClose);
    Expect("picking from a dropdown is its close", q.Take().Single() == UiCue.DropdownClose);
    q.Ask(UiCue.Step); q.Ask(UiCue.ToggleOn);
    Expect("an Off and On stepper sounds like a switch", q.Take().Single() == UiCue.ToggleOn);
    q.Ask(UiCue.Hire); q.Ask(UiCue.Confirm);
    Expect("Hire wins over the red button's confirm", q.Take().Single() == UiCue.Hire);
    q.Ask(UiCue.Panel); q.Ask(UiCue.Glide);
    Expect("one motion sound at a time, the glide over a panel", q.Take().Single() == UiCue.Glide);

    Expect("the slider ticks 20 steps from 0 to 1", MenuSoundRules.SliderStep(0) == 0 && MenuSoundRules.SliderStep(1) == 20 && MenuSoundRules.SliderStep(0.52f) == 10);
    Expect("the slider rises in pitch", MenuSoundRules.SliderPitch(0) < MenuSoundRules.SliderPitch(1) && Math.Abs(MenuSoundRules.SliderPitch(0) - 0.8f) < 1e-6);

    Console.WriteLine(fails == 0 ? "all ok" : fails + " failed");
    return fails == 0 ? 0 : 1;
  }
}
