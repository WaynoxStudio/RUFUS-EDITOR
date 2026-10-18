"""Import 9100_object - copia as 9106 with TOP anchor (depth layer)."""
from __future__ import annotations

import re
import shutil
import struct
import subprocess
import sys
import zlib
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
GFX_ID = 9106
SRC_PNG = OESTE / "9100_object - copia.png"
SRC_SVG = OESTE / "9100_object - copia.svg"
WORK = ROOT / "artifacts" / "_oeste_inspect" / "9106_canyon_top"
# mirror of 9100 bottom (671,985): top-center
AX, AY = 671, 2


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


def prepare_svg(src: Path, dest: Path, w: int, h: int) -> None:
    text = src.read_text(encoding="utf-8")
    text = re.sub(r"<metadata[\s\S]*?</metadata>", "", text, flags=re.I)
    text = re.sub(
        r"<svg[^>]*>",
        '<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" '
        f'width="{w}" height="{h}" viewBox="0 0 {w} {h}">',
        text,
        count=1,
        flags=re.I,
    )
    if "<image" in text.lower():
        raise SystemExit("SVG contains <image>; refusing raster substitute")
    dest.write_text(text, encoding="utf-8")
    npaths = len(re.findall(r"<path", text, re.I))
    print(f"prepared SVG paths={npaths} bytes={dest.stat().st_size}")


def iter_tags(body: bytes):
    nbits = body[0] >> 3
    i = (5 + nbits * 4 + 7) // 8 + 4
    while i < len(body):
        cl = struct.unpack_from("<H", body, i)[0]
        code = cl >> 6
        ln = cl & 0x3F
        hdr = 2
        if ln == 0x3F:
            ln = struct.unpack_from("<I", body, i + 2)[0]
            hdr = 6
        yield code, body[i + hdr : i + hdr + ln]
        i += hdr + ln
        if code == 0:
            break


def swf_body(path: Path) -> bytes:
    raw = path.read_bytes()
    return zlib.decompress(raw[8:]) if raw[:3] == b"CWS" else raw[8:]


def find_export_shape_id(swf_path: Path, export_name: str) -> tuple[int, int]:
    body = swf_body(swf_path)
    sprite_id = None
    for code, payload in iter_tags(body):
        if code != 56:
            continue
        n = struct.unpack_from("<H", payload, 0)[0]
        p = 2
        for _ in range(n):
            cid = struct.unpack_from("<H", payload, p)[0]
            p += 2
            end = payload.index(b"\x00", p)
            name = payload[p:end].decode("utf-8", "replace")
            p = end + 1
            if name == export_name:
                sprite_id = cid
                break
    if sprite_id is None:
        raise SystemExit(f"export {export_name} not found")
    for code, payload in iter_tags(body):
        if code != 39 or struct.unpack_from("<H", payload, 0)[0] != sprite_id:
            continue
        p = 4
        while p < len(payload):
            cl = struct.unpack_from("<H", payload, p)[0]
            p += 2
            t = cl >> 6
            ln = cl & 0x3F
            if ln == 0x3F:
                ln = struct.unpack_from("<I", payload, p)[0]
                p += 4
            chunk = payload[p : p + ln]
            p += ln
            if t == 26 and (chunk[0] & 2):
                return struct.unpack_from("<H", chunk, 3)[0], sprite_id
            if t in (0, 1):
                break
    raise SystemExit(f"shape for sprite {sprite_id} not found")


def dump_exports(swf: Path) -> str:
    r = subprocess.run(
        [str(FFDEC), "-dumpSWF", str(swf)],
        capture_output=True,
        text=True,
        check=False,
    )
    out = (r.stdout or "") + (r.stderr or "")
    return "\n".join(
        ln
        for ln in out.splitlines()
        if any(k in ln for k in ("ExportAssets", "DefineShape", "DefineBits", "DefineSprite"))
    )


def main() -> None:
    if not SRC_PNG.is_file() or not SRC_SVG.is_file():
        raise SystemExit("missing 9100_object - copia.png/svg")
    if not FFDEC.is_file():
        raise SystemExit(f"missing ffdec {FFDEC}")

    img = Image.open(SRC_PNG).convert("RGBA")
    w, h = img.size
    print(f"canyon-top {img.size} -> {GFX_ID} TOP anchor=({AX},{AY})")

    lib = ROOT / "Library" / "Images" / "objects" / "Oeste" / f"{GFX_ID}.png"
    lib.parent.mkdir(parents=True, exist_ok=True)
    img.save(lib, "PNG")
    shutil.copy2(SRC_SVG, lib.with_suffix(".svg"))
    img.save(OESTE / f"{GFX_ID}_object.png", "PNG")
    shutil.copy2(SRC_SVG, OESTE / f"{GFX_ID}_object.svg")
    upsert_objects_xml(AX, AY)

    pack = CLIENT / "clips" / "gfx" / "o_oeste.swf"
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    shutil.copy2(pack, pack.with_suffix(pack.suffix + f".bak_9106_{stamp}"))

    if WORK.exists():
        shutil.rmtree(WORK)
    WORK.mkdir(parents=True)

    tmp_png = WORK / f"_tmp_{GFX_ID}.png"
    img.save(tmp_png, "PNG")
    mid = WORK / f"o_oeste_{GFX_ID}_bitmap.swf"
    inject_symbol(pack, tmp_png, str(GFX_ID), mid, AX, AY)
    shape_id, sprite_id = find_export_shape_id(mid, str(GFX_ID))
    print(f"shape_id={shape_id} sprite_id={sprite_id}")

    shapes = WORK / "import_shapes"
    shapes.mkdir()
    prepare_svg(SRC_SVG, shapes / f"{shape_id}.svg", w, h)
    cand = WORK / "o_oeste_9106_vector.swf"
    cmd = [str(FFDEC), "-importShapes", str(mid), str(cand), str(shapes)]
    print("RUN", " ".join(cmd))
    r = subprocess.run(cmd, capture_output=True, text=True)
    print("rc", r.returncode)
    print((r.stdout or "")[-800:])
    if r.returncode != 0:
        print((r.stderr or "")[-800:])
        raise SystemExit("importShapes failed")

    export_dir = WORK / "export_after"
    export_dir.mkdir()
    subprocess.run(
        [
            str(FFDEC),
            "-format",
            "shape:svg",
            "-selectid",
            str(shape_id),
            "-export",
            "shape",
            str(export_dir),
            str(cand),
        ],
        check=True,
    )
    exported = list(export_dir.rglob("*.svg"))
    if not exported:
        raise SystemExit("no exported shape")
    exp = exported[0].read_text(encoding="utf-8")
    has_image = "<image" in exp.lower()
    npaths = len(re.findall(r"<path", exp, re.I))
    print(f"exported shape image={has_image} paths={npaths}")
    if has_image:
        raise SystemExit("FAIL: still raster")
    if npaths < 20:
        raise SystemExit(f"FAIL: too few paths ({npaths})")

    after = dump_exports(cand)
    (WORK / "dump_after.txt").write_text(after, encoding="utf-8")
    for name in ("9100", "9102", "9104", "9105", "9106", "[Link_oeste-objects]"):
        ok = f'exp: "{name}"' in after
        print(f"export {name}: {ok}")
        if not ok:
            raise SystemExit(f"missing export {name}")

    shutil.copy2(cand, pack)
    art = ROOT / "artifacts" / "_oeste_gfx"
    art.mkdir(parents=True, exist_ok=True)
    shutil.copy2(cand, art / "o_oeste.swf")
    print(f"OK {GFX_ID} canyon-top vector size={img.size} anchor=({AX},{AY})")


if __name__ == "__main__":
    main()
