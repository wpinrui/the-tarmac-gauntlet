# AI suite scoreboard

One row per run, win or lose. Full-suite runs only, plus dev runs marked as such.

**Dev run:** Race A only, full 15 minutes, seeds 11 to 14, set-ups not run. Use dev runs to compare E1 revisions while the quick suite is too noisy. Always quote v1 on the same seeds.

| Date | Kind | Build | What was tried | Field lap % | A lap % | Rear-ends /car-h | Pile-ups | Race A |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2026-11-11 | dev | v1 (E0) | Baseline | 4.0 | 10.3 | 0.35 | 0 | 64.0 |
| 2026-11-11 | dev | E1.5 | No lap-one pile-ups: a car with no clear path drops back only from a car alongside | 5.7 | 14.3 | 1.75 | 0.75 | 39.9 |
| 2026-11-11 | dev | E1.6 | Braking also slows for a car a little across, not only one in its lane | 5.8 | 12.1 | 1.33 | 0.50 | 49.8 |
| 2026-11-11 | dev, failed | E1.6 + TOUCH 0.2 | Inside the safety gap behind a car, only a path that gets closer breaks H1 | | | 1.47 | 1.00 | 41.2 |
| 2026-11-11 | dev, failed | E1.6 + ZSWITCH 0.5 | In the braking zone, stay on the brakes rather than flick back to the throttle | | 14.1 | 1.86 | 0.75 | 41.9 |
| 2026-11-11 | dev, failed | E1.6 + ZSWITCH 2 | The same, stronger | | 14.8 | 1.75 | 0.25 | 47.7 |
| 2026-11-11 | dev, failed | E1.6 + SSWITCH 1 | Switching sides of the car ahead mid-move costs 1 (T9 back to 5 of 5 seeds) | 5.8 | 14.3 | 1.39 | 0.75 | 41.6 |
| 2026-11-11 | dev, failed | E1.6 + SSWITCH 0.5 | The same, weaker | 5.8 | 12.0 | 1.47 | 1.00 | 43.4 |
| 2026-11-11 | dev | E1.7 | A car slowed to a crawl may pass through a stopped ghost it can't get round (H12's safety net) | 5.8 | 12.3 | 1.24 | 0 | 53.0 |
| 2026-11-11 | dev, failed | E1.7 + BOVER | TORCS full braking leaves out a car already overlapping (never triggered: no change) | 5.8 | 12.3 | 1.24 | 0 | 53.0 |
| 2026-11-11 | dev, failed | E1.7 + SSWITCH 1 | Switching sides of the car ahead mid-move costs 1 | 5.9 | 13.3 | 1.43 | 0 | 50.4 |
| 2026-11-11 | dev | E1.8 | Switching sides of the car ahead mid-move costs 0.3: less weaving while braking into the hairpin | 5.7 | 14.5 | 1.08 | 0 | 55.0 |
| 2026-11-11 | dev, failed | E1.8 + FALLTOL 5 | With every path failing, prefer the smaller move among those that keep the limit within 5 ticks | 5.8 | 12.7 | 2.00 | 1.00 | 36.3 |
| 2026-11-11 | dev, failed | E1.8 + FALLTOL 10 | The same, 10 ticks | 5.5 | 12.3 | 1.61 | 0 | 53.5 |
| 2026-11-11 | dev, failed | E1.8 + HEAD 0.1 | Nose-to-tail gap 0.1 s of speed instead of 0.15 s | | | | | 43.1 |
| 2026-11-11 | dev, failed | E1.8 + HEAD 0.05 | The same, 0.05 s: the A car is quicker than v1 but rear-ends treble | 5.0 | 8.4 | 3.18 | 0.25 | 51.4 |
| 2026-11-11 | dev, failed | E1.8 + PASS 1.5 | Passing weighed 1.5 times as much | | | | | 48.6 |
| 2026-11-11 | dev | E1.8 + FALLBEHIND 3 | With every path failing, moving across into a car from behind counts as failing 3 ticks sooner (within the spread: not merged) | 5.5 | 11.4 | 1.39 | 0 | 55.9 |
| 2026-11-11 | dev, failed | E1.8 + FALLBEHIND 10 | The same, 10 ticks | 5.7 | 13.2 | 1.55 | 0.50 | 44.7 |
| 2026-11-11 | dev | E1.8 + HEADZONE 0.15, HEAD 0.05 | Short nose-to-tail gap on straights, the full one from 4 s before a corner and on lap one (within the spread) | 5.5 | 14.3 | 0.94 | 0 | 56.0 |
| 2026-11-11 | dev | the same, HEAD 0.08 | | 5.6 | 12.1 | 1.24 | 0 | 55.8 |
| 2026-11-11 | dev, failed | the same, from 7 s before | | 5.8 | 14.3 | 1.33 | 0 | 53.1 |
| 2026-11-11 | dev | FALLBEHIND 3 + HEADZONE 0.15, HEAD 0.05 | Both together | 5.5 | 12.3 | 1.33 | 0 | 55.4 |
| 2026-11-11 | dev | FALLBEHIND 3 + HEADZONE 0.15, HEAD 0.08 | Both together | 5.7 | 10.6 | 1.49 | 0 | 55.9 |
| 2026-11-11 | dev, failed | FALLBEHIND 3 + HEADZONE 0.2 | A longer gap before corners | 5.9 | 13.9 | 0.90 | 0 | 53.2 |
| 2026-11-11 | dev | E1.8 + BOPEN 4 | Braking opens a short gap at 4 m/s² instead of 12 (within the spread) | 5.8 | 12.1 | 1.12 | 0 | 56.5 |
| 2026-11-11 | dev, failed | E1.8 + BOPEN 7 | The same, 7 m/s² | 5.6 | 12.8 | 1.57 | 0 | 53.2 |
| 2026-11-11 | dev, failed | BOPEN 4 + FALLBEHIND 3 | Both together | 5.6 | 11.5 | 1.37 | 0 | 52.9 |
| 2026-11-11 | dev | E1.8 + ZHYST 0.1 | In a braking zone, each metre of change of lane costs 0.1 more (within the spread) | 5.6 | 12.5 | 1.24 | 0 | 55.5 |
| 2026-11-11 | dev, failed | E1.8 + ZHYST 0.3 | The same, 0.3 | 6.3 | 15.9 | 1.41 | 0 | 48.3 |
| 2026-11-11 | dev, failed | ZHYST 0.3 + BOPEN 4 | Both together | 6.1 | 13.0 | 1.22 | 0 | 54.3 |
| 2026-11-11 | dev | v1 + RJ LANE 3 | Rejoin: a car more than 3 m across from the joining lane doesn't hold a rejoining car (no gain on v1) | 4.2 | 9.4 | 0.47 | 0 | 63.9 |
| 2026-11-11 | dev, failed | E1.8 + RJ LANE 3 | The same on E1 | 6.0 | 14.2 | 1.63 | 0 | 48.6 |
| 2026-11-11 | dev, unfinished | RJ BRAKE 9, SECS 0.5 (v1 and E1); RJ LANE 4.5 (E1) | Rejoin assumes the car coming brakes at 9 m/s² with 0.5 s to spare; stopped when Ivan paused the work | | | | | |
| 2026-11-11 | dev, unfinished | E1.8 + back to the line | Stopped for Ivan's method change: shipped as E1.9 without a race run | | | | | |
| 2026-11-11 | set-ups | E1.9 | Back on the racing line once clear: a metre off the line costs 0.05 s (was 0.01), a move back to the line costs no hysteresis, a car behind adds no side risk. Ivan tests in Unity; no metric run. T1 100, T8 85, H8 pass, H10 pass | | | | | |
| 2026-11-11 | set-ups | E1.10, E2.1 | T17: rejoins at its safe angle whenever ready, no wait for a gap (E2 is E0 with only this). T17 100 on all: 2.4 s on the grass against E0's 5.4 s, no contacts. Ivan tests in Unity | | | | | |
| 2026-11-11 | set-ups | E1.11 | H2: room left to a car alongside on the inside goes 70% of the way from the passing margin to the room owed. H2 still fails: 4.4 m room (was 4.8), 1.0 m more than owed, lifts 0.8 s (was 1.6). H3 pass. H10's leader now 1.7 s off the line once clear (was 0.4) | | | | | |
| 2026-11-11 | set-ups | E1.12 | A car placed off its line starts steering where it is, not for the line (all three in H1 swerved for it at once). H1 pass, T1 100. H10 and T6c now hold their placed lanes and show a contact each (H10 at the exit, T6c into the corner) | | | | | |
| 2026-11-11 | set-ups | E1.13 | H4: with a car 10 m to 0.45 s behind on a straight, a path covering the inside of the next corner scores up to 0.3 s. H4 pass (1 move), H5 pass, T1 100, T2 100. Found since E1.12: T8 0 and H8 contact, from the placed cars now holding their lanes | | | | | |
| 2026-11-11 | set-ups | E1.14 | Only a move off the line waits for its last safe moment: a move back to the line is made at once. T8 66 (was 0 in E1.12), T1 100, T2 100, H4 pass. Still open: H8 a 0 m/s touch at the exit, H10 and T6c one contact each, H2 1.1 m more than owed | | | | | |
| 2026-11-11 | set-ups | E1.15 | Moving across in front of a car behind keeps 1 m clear of its nose (H1 only checked to 0.3 car lengths back). T6c 100, H8 pass, H10 pass, T8 66, T1 100, T2 100, H1 pass, H4 pass. T3 still 2 contacts | | | | | |
