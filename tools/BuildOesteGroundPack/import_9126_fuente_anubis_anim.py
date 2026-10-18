"""Import fuente_anubis_agua as animated object 9126 (4 frames) in o_oeste.swf.

Pattern mirrors o_motao 5040/5041:
  Bitmap+Shape per frame -> one DefineSprite timeline swapping shapes -> Export ID.
"""
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
import inject_export as ie  # noqa: E402
from inject_motao_style import (  # noqa: E402
    define_shape4_motao,
    find_link_sprite_offset,
    remove_export_chain,
)

OESTE = Path(r"C:\Users\rubez\Desktop\OESTE")
CLIENT = Path(
    r"C:\Users\rubez\AppData\Roaming\Jeifus Launchers\rufus\cliente"
    r"\resources\app\retroclient"
)
FFDEC = ROOT / "tools" / "_ffdec" / "ffdec-cli.exe"
WORK = ROOT / "artifacts" / "_oeste_inspect" / "9126_fuente_anubis"
GFX_ID = 9126
AX, AY = 71, 60  # library 568 (same 142x108)
HOLD = 3  # ticks per visual frame (like o_motao 5040)
W, H = 142, 108
STEMS = [
    "fuente_anubis_agua_frame_01_142x108",
    "fuente_anubis_agua_frame_02_142x108",
    "fuente_anubis_agua_frame_03_142x108",
    "fuente_anubis_agua_frame_04_142x108",
]


def place_object2(shape_id: int, depth: int = 1) -> bytes:
    # flags HasCharacter|HasMatrix = 0x06, depth, character, identity matrix
    body = ie.ui8(0x06) + ie.ui16(depth) + ie.ui16(shape_id) + ie.encode_matrix()
    return ie.record_header(26, len(body)) + body


def remove_object2(depth: int = 1) -> bytes:
    body = ie.ui16(depth)
    return ie.record_header(28, len(body)) + body


def show_frame() -> bytes:
    return ie.record_header(1, 0)


def define_sprite_animated(sprite_id: int, shape_ids: list[int], hold: int) -> bytes:
    """Cycle shapes: Place A, ShowFrame x hold, Remove+Place B, ..."""
    control = bytearray()
    frame_count = 0
    for i, sid in enumerate(shape_ids):
        if i == 0:
            control += place_object2(sid)
        else:
            control += remove_object2(1)
            control += place_object2(sid)
        for _ in range(hold):
            control += show_frame()
            frame_count += 1
    control += ie.record_header(0, 0)  # End
    body = ie.ui16(sprite_id) + ie.ui16(frame_count) + bytes(control)
    return ie.record_header(39, len(body)) + body


def prepare_svg(src: Path, dest: Path) -> None:
    text = src.read_text(encoding="utf-8")
    text = re.sub(r"<metadata[\s\S]*?</metadata>", "", text, flags=re.I)
    text = re.sub(
        r"<svg[^>]*>",
        '<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" '
        f'width="{W}" height="{H}" viewBox="0 0 {W} {H}">',
        text,
        count=1,
        flags=re.I,
    )
    if "<image" in text.lower():
        raise SystemExit(f"{src.name} contains <image>")
    dest.write_text(text, encoding="utf-8")


def upsert_objects_xml(gfx_id: int, ax: int, ay: int) -> None:
    xml_path = ROOT / "Library" / "XML" / "objects.xml"
    raw = xml_path.read_bytes()
    orig_len = len(raw)
    text = raw.rstrip(b"\x00").decode("utf-8")
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
            raise SystemExit(f"xml replace failed n={n}")
        text = text2
    else:
        text = text.replace("</ArrayOfPos>", entry + "</ArrayOfPos>")
    out = text.encode("utf-8")
    payload = out + b"\x00" * (orig_len - len(out)) if len(out) <= orig_len else out
    tmp = xml_path.with_suffix(".xml.tmp_9126")
    tmp.write_bytes(payload)
    tmp.replace(xml_path)


def dump_has_export(swf: Path, name: str) -> bool:
    r = subprocess.run(
        [str(FFDEC), "-dumpSWF", str(swf)], capture_output=True, text=True, check=False
    )
    out = (r.stdout or "") + (r.stderr or "")
    return f'exp: "{name}"' in out


def main() -> None:
    if WORK.exists():
        shutil.rmtree(WORK)
    WORK.mkdir(parents=True)

    imgs: list[Image.Image] = []
    svgs: list[Path] = []
    for stem in STEMS:
        png = OESTE / "objetos" / f"{stem}.png"
        svg = OESTE / "objetos" / f"{stem}.svg"
        if not png.is_file() or not svg.is_file():
            raise SystemExit(f"missing {stem}")
        im = Image.open(png).convert("RGBA")
        if im.size != (W, H):
            raise SystemExit(f"{stem} size {im.size}")
        imgs.append(im)
        svgs.append(svg)
        print(f"frame {stem} OK")

    pack = CLIENT / "clips" / "gfx" / "o_oeste.swf"
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    shutil.copy2(pack, pack.with_suffix(pack.suffix + f".bak_9126_{stamp}"))

    swf, version, _, _ = ie.read_swf(pack)
    swf = remove_export_chain(swf, str(GFX_ID))
    base = ie.max_character_id(swf)

    # Allocate: for each frame bitmap+shape, then one sprite
    # ids: b0,s0, b1,s1, b2,s2, b3,s3, sprite
    shape_ids: list[int] = []
    tags = bytearray()
    next_id = base + 1
    for i, img in enumerate(imgs):
        bitmap_id = next_id
        shape_id = next_id + 1
        next_id += 2
        shape_ids.append(shape_id)
        tags += ie.define_bits_lossless2(bitmap_id, img)
        tags += define_shape4_motao(shape_id, bitmap_id, W, H, AX, AY)
        print(f"  frame{i+1} bitmap={bitmap_id} shape={shape_id}")

    sprite_id = next_id
    tags += define_sprite_animated(sprite_id, shape_ids, HOLD)
    tags += ie.export_assets(sprite_id, str(GFX_ID))
    print(f"  sprite={sprite_id} frames={len(shape_ids)*HOLD} hold={HOLD} shapes={shape_ids}")

    insert_at, link = find_link_sprite_offset(swf)
    print(f"insert_at={insert_at} link={link}")
    merged = swf[:insert_at] + bytes(tags) + swf[insert_at:]
    merged = b"FWS" + bytes([version]) + ie.ui32(len(merged)) + merged[8:]
    mid = WORK / "o_oeste_9126_bitmap_anim.swf"
    mid.write_bytes(
        b"CWS" + bytes([version]) + ie.ui32(len(merged)) + zlib.compress(merged[8:], 9)
    )

    # Vectorize each shape
    shapes_dir = WORK / "import_shapes"
    shapes_dir.mkdir()
    for sid, svg in zip(shape_ids, svgs):
        prepare_svg(svg, shapes_dir / f"{sid}.svg")
    cand = WORK / "o_oeste_9126_vector.swf"
    r = subprocess.run(
        [str(FFDEC), "-importShapes", str(mid), str(cand), str(shapes_dir)],
        capture_output=True,
        text=True,
    )
    print("importShapes rc", r.returncode)
    print((r.stdout or "")[-600:])
    if r.returncode != 0:
        print((r.stderr or "")[-600:])
        raise SystemExit("importShapes failed")

    # Verify each shape is vector
    for sid in shape_ids:
        exp_dir = WORK / f"export_{sid}"
        exp_dir.mkdir(exist_ok=True)
        subprocess.run(
            [
                str(FFDEC),
                "-format",
                "shape:svg",
                "-selectid",
                str(sid),
                "-export",
                "shape",
                str(exp_dir),
                str(cand),
            ],
            check=True,
            capture_output=True,
        )
        exp = next(exp_dir.rglob("*.svg")).read_text(encoding="utf-8")
        npaths = len(re.findall(r"<path", exp, re.I))
        has_img = "<image" in exp.lower()
        print(f"  verify shape {sid}: paths={npaths} image={has_img}")
        if has_img or npaths < 20:
            raise SystemExit(f"shape {sid} not good vector")

    if not dump_has_export(cand, str(GFX_ID)):
        raise SystemExit("missing export 9126")
    for must in ("9100", "9125", "[Link_oeste-objects]"):
        if not dump_has_export(cand, must):
            raise SystemExit(f"missing protected {must}")

    # Library preview = frame 01
    lib = ROOT / "Library" / "Images" / "objects" / "Oeste" / f"{GFX_ID}.png"
    lib.parent.mkdir(parents=True, exist_ok=True)
    imgs[0].save(lib, "PNG")
    shutil.copy2(svgs[0], lib.with_suffix(".svg"))
    upsert_objects_xml(GFX_ID, AX, AY)

    shutil.copy2(cand, pack)
    art = ROOT / "artifacts" / "_oeste_gfx"
    art.mkdir(parents=True, exist_ok=True)
    shutil.copy2(cand, art / "o_oeste.swf")
    print(
        f"OK {GFX_ID} fuente_anubis_agua anim "
        f"size={W}x{H} anchor=({AX},{AY}) "
        f"visual_frames=4 timeline={len(shape_ids)*HOLD} hold={HOLD}"
    )


if __name__ == "__main__":
    main()
