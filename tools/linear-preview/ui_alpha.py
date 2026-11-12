"""Rewrites a USS file's translucent colours for Linear colour space, where UI Toolkit blends in linear light.

A colour over a background blended in sRGB (Gamma) mixes differently in linear light: white text at 62% over the dark
glass comes out much brighter, and a black dimmer at 28% much lighter. Each alpha is changed so the colour shows as it
did over the background it mostly sits on: dark glass under a light colour, the scene under a dark one. The same rule
is UiBlend.Alpha in the game (Assets/Rendering/Scripts/UiBlend.cs), for colours set from code.

Usage:
  python3 ui_alpha.py 255 255 255 0.62    prints the alpha to use for white picked at 62%
  python3 ui_alpha.py --file Menus.uss    rewrites every rgba() in a style sheet picked for sRGB blending (once only)
"""
import re
import sys

LUMA = (0.2126, 0.7152, 0.0722)
OVER_DARK, OVER_LIGHT = 0.12, 0.45


def lin(v):
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4


def alpha(rgb, a):
    """The alpha that, blended in linear light, shows this sRGB colour as it showed at alpha a blended in sRGB."""
    if a <= 0 or a >= 1: return a
    c = sum(k * v for k, v in zip(LUMA, rgb))
    b = OVER_DARK if c >= 0.3 else OVER_LIGHT
    lc, lb = lin(c), lin(b)
    if abs(lc - lb) < 0.02: return a
    return min(max((lin(a * c + (1 - a) * b) - lb) / (lc - lb), 0.0), 1.0)


def convert(text):
    def sub(m):
        r, g, b, a = (float(x) for x in m.group(1, 2, 3, 4))
        n = alpha((r / 255, g / 255, b / 255), a)
        return f"rgba({m.group(1)}, {m.group(2)}, {m.group(3)}, {round(n, 3):g})"
    return re.sub(r"rgba\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*([\d.]+)\s*\)", sub, text)


if __name__ == "__main__":
    if sys.argv[1] == "--file":
        for path in sys.argv[2:]:
            src = open(path).read()
            open(path, "w").write(convert(src))
    else:
        r, g, b, a = (float(x) for x in sys.argv[1:5])
        print(round(alpha((r / 255, g / 255, b / 255), a), 3))
