"""Re-import 9100 from OESTE 9100_object.png/.svg at native size (no resize)."""
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


def main() -> None:
    src = OESTE / "9100_object.png"
    svg = OESTE / "9100_object.svg"
    if not src.is_file():
        raise SystemExit(f"missing {src}")
    if not svg.is_file():
        # fallback to exec svg used for this asset
        alt = OESTE / "exec-7771d743-8dd3-4b0e-9d9d-c0ac61a57b8c.svg"
        if alt.is_file():
            svg = alt
        else:
            raise SystemExit("missing 9100_object.svg")

    img = Image.open(src).convert("RGBA")
    ax, ay = img.width // 2, img.height - 2
    print(f"import {src.name} {img.size} (no resize) -> {GFX_ID} anchor=({ax},{ay})")
    print(f"svg {svg.name} bytes={svg.stat().st_size}")

    lib = ROOT / "Library" / "Images" / "objects" / "Oeste" / f"{GFX_ID}.png"
    lib.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, lib)
    shutil.copy2(svg, lib.with_suffix(".svg"))
    # keep OESTE working copies in sync
    img.save(OESTE / "9100_object.png", "PNG")
    if svg.name != "9100_object.svg":
        shutil.copy2(svg, OESTE / "9100_object.svg")

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

    pack = CLIENT / "clips" / "gfx" / "o_oeste.swf"
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    shutil.copy2(pack, pack.with_suffix(pack.suffix + f".bak_{stamp}"))
    tmp = OESTE / f"_tmp_{GFX_ID}.png"
    shutil.copy2(src, tmp)
    out = CLIENT / "clips" / "gfx" / f"o_oeste_{GFX_ID}.tmp.swf"
    inject_symbol(pack, tmp, str(GFX_ID), out, ax, ay)
    shutil.move(out, pack)
    art = ROOT / "artifacts" / "_oeste_gfx"
    art.mkdir(parents=True, exist_ok=True)
    shutil.copy2(pack, art / "o_oeste.swf")
    tmp.unlink(missing_ok=True)
    print(f"OK {GFX_ID} native {img.size} (kept 9101/9102/9103)")


if __name__ == "__main__":
    main()
