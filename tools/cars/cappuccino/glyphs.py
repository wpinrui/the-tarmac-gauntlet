"""Kaiser Cappuccino: the model name as letter outlines in the Kaiser brand font (Unbounded
ExtraBold). Writes glyphs.json."""
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "common"))
from glyphs import outlines  # noqa: E402

NAME = "Cappuccino"

if __name__ == "__main__":
    out = outlines(HERE / "font" / "Unbounded-ExtraBold.ttf", NAME)
    (HERE / "glyphs.json").write_text(json.dumps(out))
    print(NAME, "pieces", len(out["pieces"]), "cap", out["cap"], "width", out["width"])
