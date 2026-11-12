"""The Fantasy's name as letter outlines in the Liberty brand font (Yellowtail).

Run: python3 glyphs.py  -> glyphs.json"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "common"))
from glyphs import outlines  # noqa: E402

HERE = Path(__file__).resolve().parent

if __name__ == "__main__":
    g = outlines(HERE / "font" / "Yellowtail-Regular.ttf", "Fantasy")
    (HERE / "glyphs.json").write_text(json.dumps(g))
    print("wrote glyphs.json", len(g["pieces"]), "pieces")
