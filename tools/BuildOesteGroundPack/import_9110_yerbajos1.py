"""Import OESTE yerbajos1 as object 9110 (PNG bitmap; SVG is empty stub)."""
from __future__ import annotations

import re
import shutil
import subprocess
import sys
from datetime import datetime
from pathlib import Path

from PIL import Image

ROOT = Path(r"C:\Users\rubez\Desktop\RUFUS EDITOR")
sys.path.insert(0, str(ROOT / "artifacts" / "_inject1389"))
from inject_motao_style import inject as inject_symbol  # noqa: E402

OESTE = Path(r"C:\Users\rubez\Desktop\OESTE")
CLIENT = Path(
    r"C:\Users\rubez\AppData\Roaming\Jeifus Launchers\rufus\cliente"
    r"\resources\app\retroclient"
)
FFDEC = ROOT / "tools" / "_ffdec" / "ffdec-cli.exe"
GFX_ID = 9110
SRC_PNG = OESTE / "yerbajos1.png"
SRC_SVG = OESTE / "yerbajos1.svg"
WORK = ROOT / "artifacts" / "_oeste_inspect" / "9110_yerbajos1"
# opaque foot mid
AX, AY = 612, 1429


def upsert_objects_xml(ax: int, ay: int) -> None:
    xml_path = ROOT / "Library" / "XML" / "objects.xml"
    raw = xml_path.read_bytes()
    orig_len = len(raw)
    text = raw.rstrip(b"\x00").decode("utf-8")
    entry = (
        f"  <Pos>\n"
        f"    <ID>{GFX_ID}</ID>\n"
        f"    <X>{ax}</X>\n"
        f"    <Y>{ay}</Y>\n"
        f"  </Pos>\n"
    )
    if f"<ID>{GFX_ID}</ID>" in text:
        text2, n = re.subn(
            rf"  <Pos>\s*<ID>{GFX_ID}</ID>\s*<X>-?\d+</X>\s*<Y>-?\d+</Y>\s*</Pos>\s*",
            entry,
            text,
            count=1,
        )
        if n != 1:
            raise SystemExit(f"objects.xml replace failed n={n}")
        text = text2
    else:
        if "</ArrayOfPos>" not in text:
            raise SystemExit("objects.xml missing </ArrayOfPos>")
        text = text.replace("</ArrayOfPos>", entry + "</ArrayOfPos>")
    out = text.encode("utf-8")
    if len(out) <= orig_len:
        xml_path.write_bytes(out + b"\x00" * (orig_len - len(out)))
    else:
        xml_path.write_bytes(out)
    print(f"objects.xml {GFX_ID}=({ax},{ay}) size={xml_path.stat().st_size}")


def dump_exports(swf: Path) -> str:
    r = subprocess.run(
        [str(FFDEC), "-dumpSWF", str(swf)],
        capture_output=True,
        text=True,
        check=False,
    )
    out = (r.stdout or "") + (r.stderr or "")
    return "\n".join(ln for ln in out.splitlines() if "ExportAssets" in ln or "exp:" in ln)


def main() -> None:
    if not SRC_PNG.is_file():
        raise SystemExit("missing yerbajos1.png")

    img = Image.open(SRC_PNG).convert("RGBA")
    print(f"yerbajos1 {img.size} -> {GFX_ID} anchor=({AX},{AY})")

    svg_ok = False
    if SRC_SVG.is_file():
        text = SRC_SVG.read_text(encoding="utf-8")
        npaths = len(re.findall(r"<path", text, re.I))
        has_image = "<image" in text.lower()
        print(f"svg bytes={SRC_SVG.stat().st_size} paths={npaths} image={has_image}")
        svg_ok = npaths >= 10 and not has_image
        if not svg_ok:
            print("WARN: SVG unusable — installing as Lossless2 bitmap only")

    lib = ROOT / "Library" / "Images" / "objects" / "Oeste" / f"{GFX_ID}.png"
    lib.parent.mkdir(parents=True, exist_ok=True)
    img.save(lib, "PNG")
    if SRC_SVG.is_file():
        shutil.copy2(SRC_SVG, lib.with_suffix(".svg"))
    img.save(OESTE / f"{GFX_ID}_object.png", "PNG")
    upsert_objects_xml(AX, AY)

    pack = CLIENT / "clips" / "gfx" / "o_oeste.swf"
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    shutil.copy2(pack, pack.with_suffix(pack.suffix + f".bak_9110_{stamp}"))

    if WORK.exists():
        shutil.rmtree(WORK)
    WORK.mkdir(parents=True)

    tmp_png = WORK / f"_tmp_{GFX_ID}.png"
    img.save(tmp_png, "PNG")
    out = WORK / f"o_oeste_{GFX_ID}.swf"
    inject_symbol(pack, tmp_png, str(GFX_ID), out, AX, AY)

    after = dump_exports(out)
    (WORK / "dump_after.txt").write_text(after, encoding="utf-8")
    for name in (
        "9100",
        "9102",
        "9104",
        "9105",
        "9106",
        "9107",
        "9108",
        "9109",
        "9110",
        "[Link_oeste-objects]",
    ):
        ok = f'exp: "{name}"' in after
        print(f"export {name}: {ok}")
        if not ok:
            raise SystemExit(f"missing export {name}")

    shutil.copy2(out, pack)
    art = ROOT / "artifacts" / "_oeste_gfx"
    art.mkdir(parents=True, exist_ok=True)
    shutil.copy2(out, art / "o_oeste.swf")
    mode = "vector" if svg_ok else "bitmap"
    print(f"OK {GFX_ID} yerbajos1 {mode} size={img.size} anchor=({AX},{AY})")


if __name__ == "__main__":
    main()
