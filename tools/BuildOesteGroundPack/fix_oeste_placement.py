"""Fix g_oeste.swf size/placement and set map 6928 backgroundNum=631 in the client SWF."""
from __future__ import annotations

import shutil
import struct
import sys
import zlib
from collections import Counter
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
PNG_HD = ROOT / "Library" / "Images" / "backgrounds" / "631.png"
EXPORT_NAME = "631"
LINK_NAME = "[Link_oeste-ground]"
# Medio 15×17 full canvas is 780×442; official BGs like 630 are ~790×456.
TARGET_W, TARGET_H = 790, 456


def empty_link_sprite(sprite_id: int) -> bytes:
    body = ie.ui16(sprite_id) + ie.ui16(1) + ie.record_header(1, 0) + ie.record_header(0, 0)
    return ie.record_header(39, len(body)) + body


def build_pack(png: Path, out_path: Path) -> None:
    src = Image.open(png).convert("RGBA")
    # High-quality downscale so the FULL artwork fits the map (editor stretches; client is 1:1).
    img = src.resize((TARGET_W, TARGET_H), Image.Resampling.LANCZOS)
    print(f"resize {src.size} -> {img.size} (LANCZOS)")

    tags = bytearray()
    tags += ie.record_header(69, 4) + b"\x00\x00\x00\x00"
    tags += ie.record_header(9, 3) + bytes([0xFF, 0xFF, 0xFF])
    # Match official BG registration (~2px inset like export 630).
    tags += build_motao_symbol(
        img,
        bitmap_id=1,
        shape_id=2,
        sprite_id=3,
        name=EXPORT_NAME,
        anchor_x=2,
        anchor_y=2,
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
    print(f"Wrote {out_path} ({len(cws)} bytes)")


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


def patch_map_background(swf_path: Path, new_bg: int) -> None:
    raw = swf_path.read_bytes()
    sig, version = raw[:3], raw[3]
    body = raw[8:]
    if sig == b"CWS":
        body = zlib.decompress(body)
    elif sig != b"FWS":
        raise SystemExit(f"bad sig {sig!r}")

    # Find DoAction and replace backgroundNum push int
    tags = list(iter_tags(body))
    do_off = do_hdr = do_len = None
    do_payload = None
    for off, code, length, hdr, payload in tags:
        if code == 12:
            do_off, do_hdr, do_len, do_payload = off, hdr, length, bytearray(payload)
            break
    if do_payload is None:
        raise SystemExit("no DoAction")

    # Constant pool
    assert do_payload[0] == 0x88
    pool_len = struct.unpack_from("<H", do_payload, 1)[0]
    p = 3
    count = struct.unpack_from("<H", do_payload, p)[0]
    p += 2
    consts = []
    for _ in range(count):
        e = do_payload.index(0, p)
        consts.append(do_payload[p:e].decode("latin1", "replace"))
        p = e + 1
    bg_idx = consts.index("backgroundNum")

    # Walk actions; find Push that sets backgroundNum then int
    pos = 1 + 2 + pool_len
    patched = False
    while pos < len(do_payload):
        code = do_payload[pos]
        if code == 0:
            break
        if code < 0x80:
            pos += 1
            continue
        ln = struct.unpack_from("<H", do_payload, pos + 1)[0]
        data_start = pos + 3
        data = do_payload[data_start : data_start + ln]
        if code == 0x96:  # ActionPush
            q = 0
            # look for const8/16 == backgroundNum followed by int 0
            while q < len(data):
                t = data[q]
                q += 1
                if t == 8:
                    idx = data[q]
                    q += 1
                    if idx == bg_idx and q + 5 <= len(data) and data[q] == 7:
                        old = struct.unpack_from("<i", data, q + 1)[0]
                        struct.pack_into("<i", data, q + 1, new_bg)
                        do_payload[data_start : data_start + ln] = data
                        print(f"Patched backgroundNum {old} -> {new_bg} in {swf_path.name}")
                        patched = True
                        break
                elif t == 9:
                    idx = struct.unpack_from("<H", data, q)[0]
                    q += 2
                    if idx == bg_idx and q + 5 <= len(data) and data[q] == 7:
                        old = struct.unpack_from("<i", data, q + 1)[0]
                        struct.pack_into("<i", data, q + 1, new_bg)
                        do_payload[data_start : data_start + ln] = data
                        print(f"Patched backgroundNum {old} -> {new_bg} in {swf_path.name}")
                        patched = True
                        break
                elif t == 0:
                    q = data.index(0, q) + 1
                elif t == 1:
                    q += 4
                elif t == 4:
                    q += 1
                elif t == 5:
                    q += 1
                elif t == 6:
                    q += 8
                elif t == 7:
                    q += 4
                else:
                    break
        if patched:
            break
        pos = data_start + ln

    if not patched:
        raise SystemExit("failed to patch backgroundNum")

    # rebuild body
    new_body = body[:do_off] + ie.record_header(12, len(do_payload)) + bytes(do_payload) + body[do_off + do_hdr + do_len :]
    # fix: record_header may change size if length crosses 0x3F boundary — keep same hdr style
    # Original DoAction almost certainly used long header. Rebuild carefully:
    old_tag_total = do_hdr + do_len
    new_tag = ie.record_header(12, len(do_payload)) + bytes(do_payload)
    # If header size differs, still OK as we replace whole tag span
    new_body = body[:do_off] + new_tag + body[do_off + old_tag_total :]

    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    bak = swf_path.with_suffix(swf_path.suffix + f".bak_bg631_{stamp}")
    shutil.copy2(swf_path, bak)

    fws = b"FWS" + bytes([version]) + ie.ui32(8 + len(new_body)) + new_body
    if sig == b"FWS":
        out = fws
    else:
        out = b"CWS" + bytes([version]) + ie.ui32(len(fws)) + zlib.compress(new_body, 9)
    swf_path.write_bytes(out)
    print(f"Wrote {swf_path} (backup {bak.name})")


def decode_mapdata_grounds(path: Path) -> None:
    alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_"
    index = {c: i for i, c in enumerate(alphabet)}
    md = path.read_text(encoding="utf-8").strip()
    print(f"MapData len={len(md)} cells={len(md)//10}")
    grounds = []
    for i in range(0, len(md), 10):
        cell = md[i : i + 10]
        n = [index[c] for c in cell]
        gid = ((n[0] & 0x18) << 6) + ((n[2] & 7) << 6) + n[3]
        grounds.append(gid)
    for gid, cnt in Counter(grounds).most_common(12):
        print(f"  ground {gid}: {cnt}")


def main() -> None:
    decode_mapdata_grounds(ROOT / "Library" / "Maps" / "6928" / "6928_MapData.txt")

    pack = CLIENT / "clips" / "gfx" / "g_oeste.swf"
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    if pack.exists():
        shutil.copy2(pack, pack.with_suffix(pack.suffix + f".bak_{stamp}"))
    build_pack(PNG_HD, pack)
    build_pack(PNG_HD, ROOT / "artifacts" / "_oeste_gfx" / "g_oeste.swf")

    client_map = CLIENT / "data" / "maps" / "6928_0903231037X.swf"
    patch_map_background(client_map, 631)


if __name__ == "__main__":
    main()
