"""Import 3 suelo_camino (ref770) as objects 9123/9124/9125 into o_oeste.swf."""
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
WORK = ROOT / "artifacts" / "_oeste_inspect" / "9123_9125_suelos"
AX, AY = 99, 51  # library object 770
ITEMS = [
    (9123, "suelo_camino_arena_cubierto_ref770_200x110"),
    (9124, "suelo_camino_arena_roto_ref770_200x110"),
    (9125, "suelo_camino_arena_visible_ref770_200x110"),
]
MUST = [
    "9100",
    "9102",
    "9104",
    "9105",
    "9106",
    "9107",
    "9108",
    "9111",
    "9112",
    "9113",
    "9114",
    "9115",
    "9116",
    "9117",
    "9118",
    "9119",
    "9120",
    "9121",
    "9122",
    "9123",
    "9124",
    "9125",
    "[Link_oeste-objects]",
]


def upsert_objects_xml_many(entries: list[tuple[int, int, int]]) -> None:
    xml_path = ROOT / "Library" / "XML" / "objects.xml"
    raw = xml_path.read_bytes()
    orig_len = len(raw)
    text = raw.rstrip(b"\x00").decode("utf-8")
    for gfx_id, ax, ay in entries:
        entry = (
            f"  <Pos>\n"
            f"    <ID>{gfx_id}</ID>\n"
            f"    <X>{ax}</X>\n"
            f"    <Y>{ay}</Y>\n"
            f"  </Pos>\n"
        )
        if f"<ID>{gfx_id}</ID>" in text:
            text2, n = re.subn(
                rf"\s*<Pos>\s*<ID>{gfx_id}</ID>\s*<X>-?\d+</X>\s*<Y>-?\d+</Y>\s*</Pos>\s*",
                "\n" + entry,
                text,
                count=1,
            )
            if n != 1:
                raise SystemExit(f"objects.xml replace failed {gfx_id} n={n}")
            text = text2
        else:
            text = text.replace("</ArrayOfPos>", entry + "</ArrayOfPos>")
    out = text.encode("utf-8")
    payload = out + b"\x00" * (orig_len - len(out)) if len(out) <= orig_len else out
    tmp = xml_path.with_suffix(".xml.tmp_suelos")
    tmp.write_bytes(payload)
    tmp.replace(xml_path)
    print(f"objects.xml updated {[e[0] for e in entries]} size={xml_path.stat().st_size}")


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
        raise SystemExit(f"{src.name} has <image>")
    dest.write_text(text, encoding="utf-8")


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


def find_export_shape_id(swf_path: Path, export_name: str) -> int:
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
                return struct.unpack_from("<H", chunk, 3)[0]
            if t in (0, 1):
                break
    raise SystemExit("shape not found")


def dump_exports(swf: Path) -> str:
    r = subprocess.run(
        [str(FFDEC), "-dumpSWF", str(swf)], capture_output=True, text=True, check=False
    )
    out = (r.stdout or "") + (r.stderr or "")
    return "\n".join(ln for ln in out.splitlines() if "ExportAssets" in ln or "exp:" in ln)


def import_one(pack: Path, gfx_id: int, stem: str) -> Path:
    src_png = OESTE / "bg" / f"{stem}.png"
    src_svg = OESTE / "bg" / f"{stem}.svg"
    if not src_png.is_file() or not src_svg.is_file():
        raise SystemExit(f"missing {stem}")
    img = Image.open(src_png).convert("RGBA")
    w, h = img.size
    if img.size != (200, 110):
        print(f"WARN size {img.size}")
    print(f"{stem} {img.size} -> {gfx_id} anchor=({AX},{AY})")

    lib = ROOT / "Library" / "Images" / "objects" / "Oeste" / f"{gfx_id}.png"
    lib.parent.mkdir(parents=True, exist_ok=True)
    img.save(lib, "PNG")
    shutil.copy2(src_svg, lib.with_suffix(".svg"))

    item_work = WORK / str(gfx_id)
    if item_work.exists():
        shutil.rmtree(item_work)
    item_work.mkdir(parents=True)
    tmp = item_work / f"_tmp_{gfx_id}.png"
    img.save(tmp, "PNG")
    mid = item_work / f"bitmap.swf"
    inject_symbol(pack, tmp, str(gfx_id), mid, AX, AY)
    shape_id = find_export_shape_id(mid, str(gfx_id))
    shapes = item_work / "import_shapes"
    shapes.mkdir()
    prepare_svg(src_svg, shapes / f"{shape_id}.svg", w, h)
    cand = item_work / "vector.swf"
    r = subprocess.run(
        [str(FFDEC), "-importShapes", str(mid), str(cand), str(shapes)],
        capture_output=True,
        text=True,
    )
    if r.returncode != 0:
        raise SystemExit(f"importShapes failed: {(r.stderr or r.stdout)[-500:]}")
    export_dir = item_work / "export_after"
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
    exp = next(export_dir.rglob("*.svg")).read_text(encoding="utf-8")
    if "<image" in exp.lower():
        raise SystemExit(f"{gfx_id} still raster")
    npaths = len(re.findall(r"<path", exp, re.I))
    print(f"  shape={shape_id} paths={npaths}")
    if npaths < 10:
        raise SystemExit(f"too few paths {npaths}")
    return cand


def main() -> None:
    pack = CLIENT / "clips" / "gfx" / "o_oeste.swf"
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    shutil.copy2(pack, pack.with_suffix(pack.suffix + f".bak_suelos_{stamp}"))
    if WORK.exists():
        shutil.rmtree(WORK)
    WORK.mkdir(parents=True)

    current = pack
    last = None
    for gid, stem in ITEMS:
        last = import_one(current, gid, stem)
        current = last

    upsert_objects_xml_many([(gid, AX, AY) for gid, _ in ITEMS])
    after = dump_exports(last)
    for name in MUST:
        if f'exp: "{name}"' not in after:
            raise SystemExit(f"missing export {name}")
        print(f"export {name}: True")

    shutil.copy2(last, pack)
    art = ROOT / "artifacts" / "_oeste_gfx"
    art.mkdir(parents=True, exist_ok=True)
    shutil.copy2(last, art / "o_oeste.swf")
    print("OK suelos 9123/9124/9125 installed as objects (ref770)")


if __name__ == "__main__":
    main()
