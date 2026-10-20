"""Import OESTE mesa art as object 9100 into Library + client o_oeste.swf."""
from __future__ import annotations

import shutil
import struct
import sys
import zlib
from datetime import datetime
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "artifacts" / "_inject1389"))

import inject_export as ie  # noqa: E402
from inject_motao_style import build_motao_symbol  # noqa: E402

OESTE = Path(r"C:\Users\rubez\Desktop\OESTE")
SRC_PNG = OESTE / "exec-7771d743-8dd3-4b0e-9d9d-c0ac61a57b8c.png"
SRC_SVG = OESTE / "exec-7771d743-8dd3-4b0e-9d9d-c0ac61a57b8c.svg"
CLIENT = Path(
    r"C:\Users\rubez\AppData\Roaming\Jeifus Launchers\rufus\cliente\resources\app\retroclient"
)

GFX_ID = 9100
TARGET_WIDTH = 240  # starting size; user can ask bigger/smaller
LINK_NAME = "[Link_oeste-objects]"
PACK_NAME = "o_oeste.swf"
IMPORT_URL = f"clips/gfx/{PACK_NAME}"
IMPORT_LOCAL_ID = 20  # objects.swf: motao=19, next free


def content_bbox(im: Image.Image, alpha_min: int = 10) -> tuple[int, int, int, int]:
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
            if x < minx:
                minx = x
            if y < miny:
                miny = y
            if x > maxx:
                maxx = x
            if y > maxy:
                maxy = y
    if maxx < 0:
        return 0, 0, w - 1, h - 1
    pad = 4
    return (
        max(0, minx - pad),
        max(0, miny - pad),
        min(w - 1, maxx + pad),
        min(h - 1, maxy + pad),
    )


def prepare_png() -> tuple[Path, int, int, int, int]:
    src = Image.open(SRC_PNG).convert("RGBA")
    x0, y0, x1, y1 = content_bbox(src)
    cropped = src.crop((x0, y0, x1 + 1, y1 + 1))
    tw = TARGET_WIDTH
    th = max(1, round(cropped.height * (tw / cropped.width)))
    img = cropped.resize((tw, th), Image.Resampling.LANCZOS)
    print(f"source {src.size} crop={(x1-x0+1)}x{(y1-y0+1)} -> {img.size}")

    out_lib = ROOT / "Library" / "Images" / "objects" / "Oeste" / f"{GFX_ID}.png"
    out_lib.parent.mkdir(parents=True, exist_ok=True)
    img.save(out_lib, "PNG")
    # working copy in OESTE
    out_oeste = OESTE / f"{GFX_ID}_object.png"
    img.save(out_oeste, "PNG")
    if SRC_SVG.is_file():
        shutil.copy2(SRC_SVG, out_lib.with_suffix(".svg"))
        shutil.copy2(SRC_SVG, OESTE / f"{GFX_ID}_object.svg")

    # bottom-center anchor (typical for rocks / mesa sitting on a cell)
    ax = img.width // 2
    ay = img.height - 2
    return out_lib, img.width, img.height, ax, ay


def upsert_objects_xml(ax: int, ay: int) -> None:
    xml_path = ROOT / "Library" / "XML" / "objects.xml"
    text = xml_path.read_text(encoding="utf-8")
    entry = (
        f"  <Pos>\n"
        f"    <ID>{GFX_ID}</ID>\n"
        f"    <X>{ax}</X>\n"
        f"    <Y>{ay}</Y>\n"
        f"  </Pos>\n"
    )
    marker = f"<ID>{GFX_ID}</ID>"
    if marker in text:
        # replace existing block
        import re

        text2, n = re.subn(
            rf"  <Pos>\s*<ID>{GFX_ID}</ID>\s*<X>-?\d+</X>\s*<Y>-?\d+</Y>\s*</Pos>\s*",
            entry,
            text,
            count=1,
            flags=re.MULTILINE,
        )
        if n != 1:
            raise SystemExit("failed to replace existing Pos")
        text = text2
        print(f"Updated objects.xml Pos {GFX_ID} -> ({ax},{ay})")
    else:
        close = "</ArrayOfPos>"
        if close not in text:
            raise SystemExit("objects.xml missing root close")
        text = text.replace(close, entry + close)
        print(f"Appended objects.xml Pos {GFX_ID} -> ({ax},{ay})")
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    shutil.copy2(xml_path, xml_path.with_suffix(xml_path.suffix + f".bak_{stamp}"))
    xml_path.write_text(text, encoding="utf-8")


def empty_link_sprite(sprite_id: int) -> bytes:
    body = ie.ui16(sprite_id) + ie.ui16(1) + ie.record_header(1, 0) + ie.record_header(0, 0)
    return ie.record_header(39, len(body)) + body


def build_pack(png: Path, ax: int, ay: int, out_path: Path) -> None:
    img = Image.open(png).convert("RGBA")
    tags = bytearray()
    tags += ie.record_header(69, 4) + b"\x00\x00\x00\x00"
    tags += ie.record_header(9, 3) + bytes([0xFF, 0xFF, 0xFF])
    tags += build_motao_symbol(
        img,
        bitmap_id=1,
        shape_id=2,
        sprite_id=3,
        name=str(GFX_ID),
        anchor_x=ax,
        anchor_y=ay,
    )
    tags += empty_link_sprite(4)
    tags += ie.export_assets(4, LINK_NAME)
    tags += ie.record_header(1, 0)
    tags += ie.record_header(0, 0)

    rect = ie.encode_rect(0, 0, 0, 0)
    body = rect + b"\x00\x0c" + ie.ui16(1) + bytes(tags)
    version = 8
    fws = b"FWS" + bytes([version]) + ie.ui32(8 + len(body)) + body
    cws = b"CWS" + bytes([version]) + ie.ui32(len(fws)) + zlib.compress(body, 9)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_bytes(cws)
    print(f"Wrote {out_path} ({len(cws)} bytes) export={GFX_ID} anchor=({ax},{ay})")


def iter_tags(body: bytes):
    nbits = body[0] >> 3
    i = (5 + nbits * 4 + 7) // 8 + 4
    while i < len(body):
        code_len = struct.unpack_from("<H", body, i)[0]
        code = code_len >> 6
        length = code_len & 0x3F
        hdr = 2
        if length == 0x3F:
            length = struct.unpack_from("<I", body, i + 2)[0]
            hdr = 6
        yield i, code, length, hdr, body[i + hdr : i + hdr + length]
        i += hdr + length
        if code == 0:
            break


def import_assets_tag(url: str, local_id: int, name: str) -> bytes:
    body = url.encode("ascii") + b"\x00" + ie.ui16(1) + ie.ui16(local_id) + name.encode("ascii") + b"\x00"
    return ie.record_header(57, len(body)) + body


def patch_objects_swf(objects_path: Path) -> None:
    raw = objects_path.read_bytes()
    sig, version = raw[:3], raw[3]
    body = raw[8:]
    if sig == b"CWS":
        body = zlib.decompress(body)
    elif sig != b"FWS":
        raise SystemExit(f"bad objects.swf sig {sig!r}")

    if IMPORT_URL.encode("ascii") in body or LINK_NAME.encode("ascii") in body:
        print(f"objects.swf already references {PACK_NAME} — skip patch")
        return

    tags = list(iter_tags(body))
    insert_at = None
    last_import_end = None
    for off, code, length, hdr, payload in tags:
        if code == 57:
            last_import_end = off + hdr + length
            url = payload.split(b"\x00", 1)[0].decode("latin1")
            print(f"  existing import: {url}")
        if code == 39 and insert_at is None:
            insert_at = off
    if insert_at is None:
        insert_at = last_import_end
    if insert_at is None:
        raise SystemExit("no insert point in objects.swf")

    new_import = import_assets_tag(IMPORT_URL, IMPORT_LOCAL_ID, LINK_NAME)
    merged = body[:insert_at] + new_import + body[insert_at:]
    fws = b"FWS" + bytes([version]) + ie.ui32(8 + len(merged)) + merged
    if sig == b"FWS":
        out = fws
    else:
        out = b"CWS" + bytes([version]) + ie.ui32(len(fws)) + zlib.compress(merged, 9)

    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    bak = objects_path.with_suffix(objects_path.suffix + f".bak_oeste_{stamp}")
    shutil.copy2(objects_path, bak)
    objects_path.write_bytes(out)
    print(f"Patched {objects_path.name} (backup {bak.name})")
    print(f"  ImportAssets id={IMPORT_LOCAL_ID} {LINK_NAME} <- {IMPORT_URL}")


def main() -> None:
    if not SRC_PNG.is_file():
        raise SystemExit(f"missing {SRC_PNG}")

    png, w, h, ax, ay = prepare_png()
    upsert_objects_xml(ax, ay)

    pack = CLIENT / "clips" / "gfx" / PACK_NAME
    build_pack(png, ax, ay, pack)
    build_pack(png, ax, ay, ROOT / "artifacts" / "_oeste_gfx" / PACK_NAME)
    patch_objects_swf(CLIENT / "clips" / "objects.swf")

    print()
    print(f"OK object {GFX_ID}  size={w}x{h}px  anchor=({ax},{ay})")
    print(f"Library: {png}")
    print("Reinicia Admin / cliente para verlo. Dime si lo quieres más grande o más pequeño.")


if __name__ == "__main__":
    main()
