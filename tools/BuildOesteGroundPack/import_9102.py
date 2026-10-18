from __future__ import annotations

import hashlib
import json
import re
import shutil
import subprocess
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
GFX_ID = 9102
TARGET_W = 160
RESVG_DIR = ROOT / "tools" / "_resvg_js"


def main() -> None:
    png_src = OESTE / "cactus2.png"
    svg_src = OESTE / "cactus2.svg"
    if not png_src.is_file() and not svg_src.is_file():
        raise SystemExit("missing cactus2.png / cactus2.svg in OESTE")

    out_png = OESTE / "cactus2_160.png"
    if svg_src.is_file():
        print("svg md5", hashlib.md5(svg_src.read_bytes()).hexdigest()[:12], "len", svg_src.stat().st_size)
        script = RESVG_DIR / "_render_once.js"
        svg_js = json.dumps(str(svg_src).replace("\\", "/"))
        out_js = json.dumps(str(out_png).replace("\\", "/"))
        script.write_text(
            f"""
const {{ Resvg }} = require('@resvg/resvg-js');
const fs = require('fs');
const svg = fs.readFileSync({svg_js});
const resvg = new Resvg(svg, {{ fitTo: {{ mode: 'width', value: {TARGET_W} }}, background: 'rgba(0,0,0,0)' }});
const png = resvg.render();
fs.writeFileSync({out_js}, png.asPng());
console.log(png.width + 'x' + png.height);
""",
            encoding="utf-8",
        )
        subprocess.check_call(["node", str(script)], cwd=str(RESVG_DIR))
        img = Image.open(out_png).convert("RGBA")
        print("from SVG render", img.size)
    else:
        img = Image.open(png_src).convert("RGBA")
        th = max(1, round(img.height * (TARGET_W / img.width)))
        img = img.resize((TARGET_W, th), Image.Resampling.LANCZOS)
        print("from PNG resize", img.size)

    ax, ay = img.width // 2, img.height - 2
    lib = ROOT / "Library" / "Images" / "objects" / "Oeste" / f"{GFX_ID}.png"
    lib.parent.mkdir(parents=True, exist_ok=True)
    img.save(lib, "PNG")
    img.save(OESTE / f"{GFX_ID}_object.png", "PNG")
    if svg_src.is_file():
        shutil.copy2(svg_src, lib.with_suffix(".svg"))
        shutil.copy2(svg_src, OESTE / f"{GFX_ID}_object.svg")

    xml_path = ROOT / "Library" / "XML" / "objects.xml"
    text = xml_path.read_text(encoding="utf-8")
    entry = (
        f"  <Pos>\n    <ID>{GFX_ID}</ID>\n    <X>{ax}</X>\n    <Y>{ay}</Y>\n  </Pos>\n"
    )
    if f"<ID>{GFX_ID}</ID>" in text:
        text2, n = re.subn(
            rf"  <Pos>\s*<ID>{GFX_ID}</ID>\s*<X>-?\d+</X>\s*<Y>-?\d+</Y>\s*</Pos>\s*",
            entry,
            text,
            count=1,
        )
        assert n == 1
        text = text2
    else:
        text = text.replace("</ArrayOfPos>", entry + "</ArrayOfPos>")
    xml_path.write_text(text, encoding="utf-8")

    pack = CLIENT / "clips" / "gfx" / "o_oeste.swf"
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    shutil.copy2(pack, pack.with_suffix(pack.suffix + f".bak_{stamp}"))
    tmp = OESTE / f"_tmp_{GFX_ID}.png"
    img.save(tmp, "PNG")
    out = CLIENT / "clips" / "gfx" / f"o_oeste_{GFX_ID}.tmp.swf"
    inject_symbol(pack, tmp, str(GFX_ID), out, ax, ay)
    shutil.move(out, pack)
    shutil.copy2(pack, ROOT / "artifacts" / "_oeste_gfx" / "o_oeste.swf")
    tmp.unlink(missing_ok=True)
    print(f"OK object {GFX_ID} size={img.size} anchor=({ax},{ay})")


if __name__ == "__main__":
    main()
