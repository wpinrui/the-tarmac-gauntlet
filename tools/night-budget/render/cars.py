"""Packs car models for the night renders: each OBJ as Unity imports it (x mirrored), its triangles sorted into the
looks the renderer draws them with, float32 positions, int8 normals, uint32 indices, and its headlamps' middle and
its front, as CarLights finds them.

    python3 -I cars.py <out-folder> <model.obj>...
"""
import json
import os
import sys

import numpy as np

# Material name prefixes and the look each takes: paint, glass, lens (headlamp), tail, chrome, dark, tyre, interior.
LOOKS = [
    ("lamp_head", "head"), ("head_glass", "headglass"), ("lamp_tail", "tail"), ("lamp_brake", "brake"), ("tail_glass", "tailglass"),
    ("body", "paint"), ("glass", "glass"), ("mirror_glass", "chrome"), ("chrome", "chrome"), ("rim", "chrome"),
    ("tyre", "tyre"), ("interior", "dark"), ("seat", "dark"), ("grille", "dark"), ("trim", "dark"), ("dark_metal", "dark"),
    ("housing", "dark"), ("reflector", "chrome"),
]
KINDS = ["paint", "glass", "head", "tail", "brake", "headglass", "tailglass", "chrome", "dark", "tyre"]


def look(name):
    for prefix, kind in LOOKS:
        if name.startswith(prefix):
            return kind
    return "dark"


def load(path):
    vs, ns = [], []
    faces = {k: [] for k in KINDS}
    kind = "dark"
    with open(path) as f:
        for line in f:
            if line.startswith("v "):
                vs.append(line[2:].split()[:3])
            elif line.startswith("vn "):
                ns.append(line[3:].split()[:3])
            elif line.startswith("usemtl"):
                kind = look(line.split(None, 1)[1].strip())
            elif line.startswith("f "):
                corners = []
                for c in line[2:].split():
                    p = c.split("/")
                    corners.append((int(p[0]), int(p[2]) if len(p) > 2 and p[2] else int(p[0])))
                for i in range(1, len(corners) - 1):
                    faces[kind].append((corners[0], corners[i], corners[i + 1]))
    v = np.array(vs, dtype=np.float32)
    n = np.array(ns, dtype=np.float32)
    return v, n, faces


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    models = []
    for path in sys.argv[2:]:
        name = os.path.splitext(os.path.basename(path))[0]
        v, n, faces = load(path)
        # Unity's OBJ importer mirrors x, and so the winding.
        v[:, 0] *= -1
        n[:, 0] *= -1
        blob = bytearray()
        parts = []
        for kind in KINDS:
            tris = faces[kind]
            if not tris:
                continue
            corners = np.array(tris, dtype=np.int64).reshape(-1, 2)
            corners[:, 0] = np.where(corners[:, 0] < 0, corners[:, 0] + len(v) + 1, corners[:, 0])
            corners[:, 1] = np.where(corners[:, 1] < 0, corners[:, 1] + len(n) + 1, corners[:, 1])
            corners -= 1
            keys, inverse = np.unique(corners, axis=0, return_inverse=True)
            inverse = inverse.reshape(-1, 3)[:, ::-1].astype(np.uint32).ravel()
            pos = v[keys[:, 0]]
            nrm = n[keys[:, 1]] if len(n) else np.zeros_like(pos)
            nrm /= np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-6)
            n8 = np.zeros((len(pos), 4), np.int8)
            n8[:, :3] = np.round(nrm * 127).astype(np.int8)
            at = len(blob)
            blob += pos.astype("<f4").tobytes()
            n_at = len(blob)
            blob += n8.tobytes()
            i_at = len(blob)
            blob += inverse.astype("<u4").tobytes()
            parts.append({"kind": kind, "vertices": len(pos), "indices": len(inverse), "pos": at, "nrm": n_at, "idx": i_at,
                          "min": pos.min(0).tolist(), "max": pos.max(0).tolist()})
        allv = v
        heads = [p for p in parts if p["kind"] == "head"]
        if heads:
            lo = np.min([p["min"] for p in heads], 0)
            hi = np.max([p["max"] for p in heads], 0)
            nose = ((lo + hi) / 2).tolist()
        else:
            nose = [0.0, 0.7, float(allv[:, 2].max())]
        front = max(float(allv[:, 2].max()), nose[2])
        with open(os.path.join(out, name + ".bin"), "wb") as f:
            f.write(blob)
        models.append({"name": name, "parts": parts, "nose": nose, "front": front,
                       "min": allv.min(0).tolist(), "max": allv.max(0).tolist()})
        print(name, len(allv), "vertices", sum(p["indices"] for p in parts) // 3, "triangles", "nose", [round(x, 2) for x in nose],
              "front", round(front, 2), "y", round(float(allv[:, 1].min()), 2), round(float(allv[:, 1].max()), 2))
    with open(os.path.join(out, "cars.json"), "w") as f:
        json.dump(models, f)


main()
