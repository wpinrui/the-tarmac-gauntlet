"""Defect checks on out/FerettiF256V8T.obj: open or inward shells, floating parts,
intersecting pairs and z-fighting. Run after Ivan confirms the direction.

Run: python3 export.py out && python3 check.py"""
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "common"))
import check  # noqa: E402

if __name__ == "__main__":
    hits = check.run(HERE / "out" / "FerettiF256V8T.obj")
    print("\n".join(hits) or "clean")
    sys.exit(1 if hits else 0)
