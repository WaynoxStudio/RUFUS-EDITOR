"""Import 9100 from OESTE exec-7771 PNG+SVG at native pixel size (no downscale)."""
from __future__ import annotations

import re
import shutil
import sys
from datetime import datetime
from pathlib import Path

from PIL import Image

ROOT = Path(r"c:\Users\rubez\Desktop\RUFUS EDITOR")
sys.path.insert(0, str(ROOT / "artifacts" / "_inject1389"))
from inject_motao_style import inject as inject_symbol  # noqa: E402

OESTE = Path(r"C:\Users\rubez\Desktop\OESTE")
CLIENT = Path(
    r"C:\Users\rubez\AppData\Roaming\Jeifus Launchers\rufus\cliente\resources\app\retroclient"
)
GFX_ID = 9100
SRC_PNG = OESTE / "exec-7771d743-8dd3-4b0e-9d9d-c0ac61a57b8c.png"
SRC_SVG = OESTE / "exec-7771d743-8dd3-4b0e-9d9d-c0ac61a57b8c.svg"


def content_bbox(im: Image.Image, alpha_min: int = 10) -> tuple[int, int, int, int]:
    """Tight crop of non-black opaque content; does not scale."""
    px = im.load()
    w, h = im.size
    minx, miny, maxx, maxy = w, h, -1, -1
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < alpha_min:
                continue
            if r < 18 and g < 18 and b < 18:
                continue
            minx = min(minx, x)
            miny = min(miny, y)
            maxx = max(maxx, x)
            maxy = max(maxy, y)
    if maxx < 0:
        return 0, 0, w - 1, h - 1
    pad = 2
    return (
        max(0, minx - pad),
        max(0, miny - pad),
        min(w - 1, maxx + pad),
        min(h - 1, maxy + pad),
    )


def main() -> None:
    if not SRC_PNG.is_file():
        raise SystemExit(f"missing {SRC_PNG}")
    if not SRC_SVG.is_file():
        raise SystemExit(f"missing {SRC_SVG}")

    src = Image.open(SRC_PNG).convert("RGBA")
    x0, y0, x1, y1 = content_bbox(src)
    img = src.crop((x0, y0, x1 + 1, y1 + 1))
    print(
        f"source {src.size} crop={(x1 - x0 + 1)}x{(y1 - y0 + 1)} "
        f"-> {img.size} (NO resize)"
    )

    ax, ay = img.width // 2, img.height - 2
    lib = ROOT / "Library" / "Images" / "objects" / "Oeste" / f"{GFX_ID}.png"
    lib.parent.mkdir(parents=True, exist_ok=True)
    img.save(lib, "PNG")
    img.save(OESTE / "9100_object.png", "PNG")
    shutil.copy2(SRC_SVG, lib.with_suffix(".svg"))
    shutil.copy2(SRC_SVG, OESTE / "9100_object.svg")

    xml_path = ROOT / "Library" / "XML" / "objects.xml"
    text = xml_path.read_text(encoding="utf-8")
    entry = (
        f"  <Pos>\n    <ID>{GFX_ID}</ID>\n    <X>{ax}</X>\n    <Y>{ay}</Y>\n  </Pos>\n"
    )
    if f"<ID>{GFX_ID}</ID>" in text:
        text, n = re.subn(
            rf"  <Pos>\s*<ID>{GFX_ID}</ID>\s*<X>-?\d+</X>\s*<Y>-?\d+</Y>\s*</Pos>\s*",
            entry,
            text,
            count=1,
        )
        assert n == 1
    else:
        text = text.replace("</ArrayOfPos>", entry + "</ArrayOfPos>")
    xml_path.write_text(text, encoding="utf-8")
    print(f"Pos {GFX_ID} -> ({ax},{ay})")

    pack = CLIENT / "clips" / "gfx" / "o_oeste.swf"
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    shutil.copy2(pack, pack.with_suffix(pack.suffix + f".bak_{stamp}"))
    tmp = OESTE / f"_tmp_{GFX_ID}.png"
    img.save(tmp, "PNG")
    out = CLIENT / "clips" / "gfx" / f"o_oeste_{GFX_ID}.tmp.swf"
    inject_symbol(pack, tmp, str(GFX_ID), out, ax, ay)
    shutil.move(out, pack)
    art = ROOT / "artifacts" / "_oeste_gfx"
    art.mkdir(parents=True, exist_ok=True)
    shutil.copy2(pack, art / "o_oeste.swf")
    tmp.unlink(missing_ok=True)
    print(f"OK {GFX_ID} native {img.size} in o_oeste (kept 9101/9102/9103)")


if __name__ == "__main__":
    main()
