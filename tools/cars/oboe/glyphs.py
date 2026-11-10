"""The Oboe's name as letter outlines in the Kaiser brand font.

Run: python3 glyphs.py  -> glyphs.json"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "common"))
from glyphs import outlines  # noqa: E402

HERE = Path(__file__).resolve().parent

if __name__ == "__main__":
    g = outlines(HERE / "font" / "Unbounded-ExtraBold.ttf", "Oboe")
    (HERE / "glyphs.json").write_text(json.dumps(g))
    print("wrote glyphs.json", len(g["pieces"]), "pieces")
