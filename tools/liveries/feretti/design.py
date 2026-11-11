"""Scuderia Feretti on the Feretti Vortice: rosso corsa, and nothing else of the real Scuderia.
Writes one folder of panels per option under out/.

Run: node ../common/views.js ../../../unity/Assets/Art/Vehicles/FerettiVortice/FerettiVortice.obj out/views
     python3 design.py
"""
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "common"))
from livery import Livery  # noqa: E402

ROSSO = "#d40000"
BONE = "#ece5d3"
GRAPHITE = "#2a2d32"
WHITE = "#ffffff"

# Door number, centred on the door below the window.
DOOR_Z, DOOR_Y, DOOR_W, DOOR_H = 0.26, 0.52, 0.56, 0.30


def chamfer(z, y, w, h, c):
    """A plate w by h centred on (z, y), corners cut by c."""
    z0, z1, y0, y1 = z - w / 2, z + w / 2, y - h / 2, y + h / 2
    return [(z0 + c, y0), (z1 - c, y0), (z1, y0 + c), (z1, y1 - c), (z1 - c, y1), (z0 + c, y1), (z0, y1 - c), (z0, y0 + c)]


def common(lv, roof_x, bonnet_x):
    """Marks every option shares: door and roof numbers, the sponsors."""
    lv.side_number(DOOR_Z, DOOR_Y, DOOR_W, DOOR_H, WHITE)
    lv.side_text("VELARIA", DOOR_Z, 0.27, 0.075, WHITE, spacing=0.12)
    lv.number("top", (roof_x, 0, -0.08), 0.56, 0.34, WHITE, rotate=180)  # reads from the car's left
    lv.text("top", "MONTALBA", (bonnet_x, 0, 1.78), 0.075, WHITE, rotate=-90, spacing=0.12)  # reads from the nose


def puro(views):
    lv = Livery(views, ROSSO)
    common(lv, 0.0, 0.0)
    return lv


def stripes(views):
    lv = Livery(views, ROSSO)
    lv.band_x(0.08, 0.34, BONE)
    lv.band_x(0.40, 0.47, BONE)
    lv.side_poly(chamfer(DOOR_Z, DOOR_Y, 0.66, 0.38, 0.05), BONE)
    common(lv, -0.27, -0.33)
    lv.numbers = [dict(n, ink=GRAPHITE) if n["view"] in ("left", "right") else n for n in lv.numbers]
    return lv


OPTIONS = {"puro": puro, "stripes": stripes}

if __name__ == "__main__":
    views = HERE / "out" / "views" / "views.json"
    for name, make in OPTIONS.items():
        make(views).write(HERE / "out" / name)
