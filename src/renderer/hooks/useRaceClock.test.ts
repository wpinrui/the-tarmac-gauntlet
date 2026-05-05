// @vitest-environment jsdom
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { useRaceClock } from "./useRaceClock";

type RafCallback = (time: number) => void;

let pending: { id: number; cb: RafCallback }[];
let nextId: number;

function flushFrame(time: number) {
  act(() => {
    const queued = pending;
    pending = [];
    for (const { cb } of queued) cb(time);
  });
}

function setHidden(hidden: boolean) {
  Object.defineProperty(document, "hidden", { value: hidden, configurable: true });
  Object.defineProperty(document, "visibilityState", {
    value: hidden ? "hidden" : "visible",
    configurable: true,
  });
  document.dispatchEvent(new Event("visibilitychange"));
}

beforeEach(() => {
  pending = [];
  nextId = 1;
  vi.stubGlobal("requestAnimationFrame", (cb: RafCallback) => {
    const id = nextId++;
    pending.push({ id, cb });
    return id;
  });
  vi.stubGlobal("cancelAnimationFrame", (id: number) => {
    pending = pending.filter((p) => p.id !== id);
  });
  setHidden(false);
});

afterEach(() => {
  vi.unstubAllGlobals();
  setHidden(false);
});

describe("useRaceClock", () => {
  it("starts at 0 and accumulates elapsed seconds across frames", () => {
    const { result } = renderHook(() => useRaceClock(true, 60));
    expect(result.current).toBe(0);

    // First visible frame is baseline-only — no advance.
    flushFrame(1000);
    flushFrame(2000);
    expect(result.current).toBeCloseTo(1, 5);

    flushFrame(3500);
    expect(result.current).toBeCloseTo(2.5, 5);
  });

  it("does not advance while document.hidden, and re-baselines on resume", () => {
    const { result } = renderHook(() => useRaceClock(true, 60));

    flushFrame(1000); // baseline
    flushFrame(2000); // +1s
    expect(result.current).toBeCloseTo(1, 5);

    setHidden(true);
    flushFrame(3000);
    flushFrame(31000); // long tab-out
    expect(result.current).toBeCloseTo(1, 5);

    setHidden(false);
    // First post-resume frame is baseline-only.
    flushFrame(31016);
    expect(result.current).toBeCloseTo(1, 5);
    // Subsequent frame advances normally — no 30s catch-up jump.
    flushFrame(32016);
    expect(result.current).toBeCloseTo(2, 3);
  });

  it("pins at the cap and stops scheduling once the race ends", () => {
    const { result } = renderHook(() => useRaceClock(true, 1));
    flushFrame(1000); // baseline
    flushFrame(3000); // +2s elapsed, capped at 1s
    expect(result.current).toBe(1);
    expect(pending.length).toBe(0);
  });

  it("freezes elapsed when running flips to false", () => {
    const { result, rerender } = renderHook(
      ({ running }: { running: boolean }) => useRaceClock(running, 60),
      { initialProps: { running: true } },
    );

    flushFrame(1000);
    flushFrame(2000);
    const t = result.current;
    expect(t).toBeGreaterThan(0);

    rerender({ running: false });
    expect(pending.length).toBe(0);
    expect(result.current).toBe(t);
  });
});
