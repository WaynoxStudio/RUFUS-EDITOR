"""Build clips/gfx/g_oeste.swf and register it in clips/ground.swf."""
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

CLIENT = Path(
    r"C:\Users\rubez\AppData\Roaming\Jeifus Launchers\rufus\cliente\resources\app\retroclient"
)
PNG = ROOT / "Library" / "Images" / "backgrounds" / "631.png"
EXPORT_NAME = "631"
LINK_NAME = "[Link_oeste-ground]"
PACK_NAME = "g_oeste.swf"
IMPORT_URL = f"clips/gfx/{PACK_NAME}"
IMPORT_LOCAL_ID = 5  # g1=1, g2=2, local sprite=3, motao=4


def empty_link_sprite(sprite_id: int) -> bytes:
    body = ie.ui16(sprite_id) + ie.ui16(1) + ie.record_header(1, 0) + ie.record_header(0, 0)
    return ie.record_header(39, len(body)) + body


def build_pack(png: Path, out_path: Path) -> None:
    img = Image.open(png).convert("RGBA")
    # Backgrounds without ground XML use anchor (0,0) in the editor.
    tags = bytearray()
    tags += ie.record_header(69, 4) + b"\x00\x00\x00\x00"  # FileAttributes
    tags += ie.record_header(9, 3) + bytes([0xFF, 0xFF, 0xFF])  # white bg
    tags += build_motao_symbol(
        img,
        bitmap_id=1,
        shape_id=2,
        sprite_id=3,
        name=EXPORT_NAME,
        anchor_x=0,
        anchor_y=0,
    )
    tags += empty_link_sprite(4)
    tags += ie.export_assets(4, LINK_NAME)
    tags += ie.record_header(1, 0)  # ShowFrame
    tags += ie.record_header(0, 0)  # End

    # Minimal RECT + frame rate 12 + 1 frame (same as g_motao / ground packs)
    rect = ie.encode_rect(0, 0, 0, 0)
    body = rect + b"\x00\x0c" + ie.ui16(1) + bytes(tags)
    version = 8
    fws = b"FWS" + bytes([version]) + ie.ui32(8 + len(body)) + body
    cws = b"CWS" + bytes([version]) + ie.ui32(len(fws)) + zlib.compress(body, 9)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_bytes(cws)
    print(f"Wrote {out_path} ({len(cws)} bytes) export={EXPORT_NAME} {img.size}")


def import_assets_tag(url: str, local_id: int, name: str) -> bytes:
    body = url.encode("ascii") + b"\x00" + ie.ui16(1) + ie.ui16(local_id) + name.encode("ascii") + b"\x00"
    return ie.record_header(57, len(body)) + body


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


def patch_ground_swf(ground_path: Path) -> None:
    raw = ground_path.read_bytes()
    sig, version = raw[:3], raw[3]
    body = raw[8:]
    if sig == b"CWS":
        body = zlib.decompress(body)
    elif sig != b"FWS":
        raise SystemExit(f"bad ground.swf sig {sig!r}")

    # Already registered?
    if IMPORT_URL.encode("ascii") in body or LINK_NAME.encode("ascii") in body:
        print(f"ground.swf already references {PACK_NAME} / {LINK_NAME} — skip patch")
        return

    tags = list(iter_tags(body))
    # Insert new ImportAssets after the last existing ImportAssets (tag 57), before Sprite.
    insert_at = None
    last_import_end = None
    for off, code, length, hdr, payload in tags:
        if code == 57:
            last_import_end = off + hdr + length
            # parse url
            url = payload.split(b"\x00", 1)[0].decode("latin1")
            print(f"  existing import: {url}")
        if code == 39 and insert_at is None:
            insert_at = off
    if insert_at is None:
        insert_at = last_import_end
    if insert_at is None:
        raise SystemExit("could not find insert point in ground.swf")

    new_import = import_assets_tag(IMPORT_URL, IMPORT_LOCAL_ID, LINK_NAME)
    nbits = body[0] >> 3
    header_len = (5 + nbits * 4 + 7) // 8 + 4
    merged = body[:insert_at] + new_import + body[insert_at:]
    # keep same rect+fps header already inside merged
    fws = b"FWS" + bytes([version]) + ie.ui32(8 + len(merged)) + merged
    # ground.swf is typically uncompressed FWS in this client — preserve original sig style
    if sig == b"FWS":
        out = fws
    else:
        out = b"CWS" + bytes([version]) + ie.ui32(len(fws)) + zlib.compress(merged, 9)

    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    bak = ground_path.with_suffix(ground_path.suffix + f".bak_oeste_{stamp}")
    shutil.copy2(ground_path, bak)
    ground_path.write_bytes(out)
    print(f"Patched {ground_path} (backup {bak.name})")
    print(f"  added ImportAssets id={IMPORT_LOCAL_ID} {LINK_NAME} <- {IMPORT_URL}")


def verify_pack(path: Path) -> None:
    raw = path.read_bytes()
    body = raw[8:]
    if raw[:3] == b"CWS":
        body = zlib.decompress(body)
    names = []
    for _, code, _, _, payload in iter_tags(body):
        if code != 56:
            continue
        n = struct.unpack_from("<H", payload, 0)[0]
        p = 2
        for _ in range(n):
            p += 2
            e = payload.index(0, p)
            names.append(payload[p:e].decode("latin1"))
            p = e + 1
    print("pack exports:", names)
    assert EXPORT_NAME in names and LINK_NAME in names


def main() -> None:
    if not PNG.is_file():
        raise SystemExit(f"missing {PNG}")

    gfx_dir = CLIENT / "clips" / "gfx"
    pack_out = gfx_dir / PACK_NAME
    build_pack(PNG, pack_out)
    verify_pack(pack_out)

    # also keep a copy under repo artifacts
    repo_copy = ROOT / "artifacts" / "_oeste_gfx" / PACK_NAME
    build_pack(PNG, repo_copy)

    patch_ground_swf(CLIENT / "clips" / "ground.swf")

    # verify ground imports
    raw = (CLIENT / "clips" / "ground.swf").read_bytes()
    body = raw[8:]
    if raw[:3] == b"CWS":
        body = zlib.decompress(body)
    print("ground.swf imports:")
    for _, code, _, _, payload in iter_tags(body):
        if code != 57:
            continue
        end = payload.index(0)
        url = payload[:end].decode("latin1")
        p = end + 1
        count = struct.unpack_from("<H", payload, p)[0]
        p += 2
        for _ in range(count):
            cid = struct.unpack_from("<H", payload, p)[0]
            p += 2
            e = payload.index(0, p)
            name = payload[p:e].decode("latin1")
            p = e + 1
            print(f"  {url} -> id={cid} {name}")


if __name__ == "__main__":
    main()
