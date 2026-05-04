import { useEffect, useRef, useState } from "react";

// Throttle React re-renders to ~30 fps. RAF fires up to 60 fps but per-snapshot
// interpolation never needs more than 30 (GDD §2 / issue #29 spec).
const RENDER_INTERVAL_MS = 33;

/**
 * RAF-driven wall-clock accumulator for race playback.
 *
 * Returns the number of seconds the clock has accumulated while `running` was
 * true. Pausing (`running=false`) freezes the clock; flipping back to true
 * resumes from the same elapsed value.
 *
 * Tab-out behaviour: while `document.hidden` no time accrues, and the next
 * visible frame after `visibilitychange` re-establishes a fresh baseline — so
 * a 30-second tab-out doesn't fast-forward the wall clock by 30 seconds.
 *
 * Stops scheduling frames once `capSec` is reached and pins the value at the
 * cap, which lets callers detect the auto-finish boundary by reading
 * `elapsed >= capSec`.
 */
export function useRaceClock(running: boolean, capSec: number): number {
  const [elapsedSec, setElapsedSec] = useState(0);
  const elapsedRef = useRef(0);
  const lastFrameRef = useRef<number | null>(null);
  const lastRenderRef = useRef(0);

  useEffect(() => {
    if (!running) {
      lastFrameRef.current = null;
      return;
    }
    if (elapsedRef.current >= capSec) return;

    let rafId = 0;

    const onVisibilityChange = () => {
      if (document.visibilityState === "visible") {
        // Re-establish baseline on the next frame — without this the first
        // post-resume frame's `now - lastFrame` would equal the tab-out
        // duration and fast-forward the clock.
        lastFrameRef.current = null;
      }
    };

    const tick = (now: number) => {
      if (typeof document !== "undefined" && document.hidden) {
        rafId = requestAnimationFrame(tick);
        return;
      }
      if (lastFrameRef.current === null) {
        lastFrameRef.current = now;
        lastRenderRef.current = now;
        rafId = requestAnimationFrame(tick);
        return;
      }

      const deltaMs = now - lastFrameRef.current;
      lastFrameRef.current = now;
      const next = Math.min(capSec, elapsedRef.current + deltaMs / 1000);
      elapsedRef.current = next;

      if (next >= capSec) {
        setElapsedSec(capSec);
        return;
      }

      if (now - lastRenderRef.current >= RENDER_INTERVAL_MS) {
        lastRenderRef.current = now;
        setElapsedSec(next);
      }

      rafId = requestAnimationFrame(tick);
    };

    if (typeof document !== "undefined") {
      document.addEventListener("visibilitychange", onVisibilityChange);
    }
    rafId = requestAnimationFrame(tick);

    return () => {
      cancelAnimationFrame(rafId);
      if (typeof document !== "undefined") {
        document.removeEventListener("visibilitychange", onVisibilityChange);
      }
      lastFrameRef.current = null;
    };
  }, [running, capSec]);

  return elapsedSec;
}
