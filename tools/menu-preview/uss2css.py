"""Converts Menus.uss to browser CSS, with base rules that copy UI Toolkit's defaults.

UI Toolkit lays out with Yoga, a flexbox engine, so most USS is CSS already. The rest is mapped here:
text alignment, background scale modes and image tints. Every rule is scoped under .stage.
"""
import re
import sys

BASE = """
.stage{position:relative;width:1600px;height:900px;overflow:hidden;font-size:12px;line-height:normal;color:#fff;
  font-family:'Titillium Web';font-weight:600;-webkit-font-smoothing:antialiased;font-variant-numeric:normal}
.stage ve,.stage lbl,.stage sv,.stage tf,.stage chart{display:flex;flex-direction:column;position:relative;box-sizing:border-box;
  flex-shrink:1;flex-grow:0;flex-basis:auto;border:0 solid transparent;margin:0;padding:0;
  background-repeat:no-repeat;background-position:center;background-size:100% 100%;
  -webkit-mask-repeat:no-repeat;-webkit-mask-position:center;-webkit-mask-size:100% 100%;
  mask-repeat:no-repeat;mask-position:center;mask-size:100% 100%}
.stage lbl{justify-content:var(--tv,flex-start);white-space:normal}
.stage .t{display:block;overflow:inherit;text-overflow:inherit;white-space:inherit;min-width:0}
.stage sv{overflow:hidden}
.stage .unity-scroll-view__content-viewport{flex-grow:1;flex-shrink:1;overflow:hidden}
.stage .unity-scroll-view__content-container{flex-shrink:0}
.stage .unity-list-view>sv{flex-grow:1}
.stage .tex{background-image:var(--img)}
.stage .tex-mask{-webkit-mask-image:var(--img);mask-image:var(--img);background-color:var(--tint,#fff) !important}
.stage .unity-base-text-field__input{justify-content:center}
.stage chart{flex-grow:1}
.stage .chart-svg{position:absolute;left:34px;top:8px;right:6px;bottom:22px;width:calc(100% - 40px);height:calc(100% - 30px)}
.stage .glass{backdrop-filter:blur(26px) saturate(150%);-webkit-backdrop-filter:blur(26px) saturate(150%);box-shadow:0 24px 60px rgba(0,0,0,.35)}
.stage .glass-sheen{background-image:linear-gradient(140deg,rgba(255,255,255,.08),rgba(255,255,255,0) 60%)}
.stage .glass-blur{display:none}
"""

V = {"upper": "flex-start", "middle": "center", "lower": "flex-end"}
SCALE = {"scale-to-fit": "contain", "scale-and-crop": "cover", "stretch-to-fill": "100% 100%"}


def declaration(prop, value):
    if prop == "-unity-text-align":
        v, h = value.split("-")
        return f"text-align:{ {'left':'left','center':'center','right':'right'}[h] };--tv:{V[v]}"
    if prop == "-unity-background-scale-mode":
        s = SCALE[value]
        return f"background-size:{s};-webkit-mask-size:{s};mask-size:{s}"
    if prop == "-unity-background-image-tint-color":
        return f"--tint:{value}"
    if prop.startswith("-unity-") or prop.startswith("--unity-"):
        return None
    if prop == "cursor":
        return "cursor:pointer"
    return f"{prop}:{value}"


def selector(sel):
    sel = re.sub(r"(^|[\s>,])Label\b", r"\1lbl", sel.strip())
    return ",".join(".stage " + s.strip() for s in sel.split(","))


def convert(uss):
    uss = re.sub(r"/\*.*?\*/", "", uss, flags=re.S)
    out = [BASE]
    for sel, body in re.findall(r"([^{}]+)\{([^}]*)\}", uss):
        decls = []
        for d in body.split(";"):
            if ":" not in d:
                continue
            p, v = d.split(":", 1)
            c = declaration(p.strip(), v.strip())
            if c:
                decls.append(c)
        if decls:
            out.append(selector(sel) + "{" + ";".join(decls) + "}")
    return "\n".join(out)


if __name__ == "__main__":
    print(convert(open(sys.argv[1], encoding="utf-8").read()))
