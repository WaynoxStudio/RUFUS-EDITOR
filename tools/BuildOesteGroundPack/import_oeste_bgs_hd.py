"""Import 3 OESTE HD backgrounds into g_oeste.swf + Admin Library.

Keeps existing 631. Adds 632/633/634 from OESTE/bg as Lossless2 HD bitmaps
mapped into the validated visible rect 790x456 (bounds -40..15760, -40..9080).
"""
from __future__ import annotations

import re
import shutil
import struct
import sys
import zlib
from datetime import datetime
from pathlib import Path

from PIL import Image

ROOT = Path(r"C:\Users\rubez\Desktop\RUFUS EDITOR")
sys.path.insert(0, str(ROOT / "artifacts" / "_inject1389"))

import inject_export as ie  # noqa: E402
from inject_motao_style import encode_matrix_scale_translate  # noqa: E402

CLIENT = Path(
    r"C:\Users\rubez\AppData\Roaming\Jeifus Launchers\rufus\cliente\resources\app\retroclient"
)
OESTE_BG = Path(r"C:\Users\rubez\Desktop\OESTE\bg")
LIB_BG = ROOT / "Library" / "Images" / "backgrounds"
GROUNDS_XML = ROOT / "Library" / "XML" / "grounds.xml"
PACK_NAME = "g_oeste.swf"
LINK_NAME = "[Link_oeste-ground]"

# Validated visible size / registration (same as current 631 in g_oeste.swf)
VIS_W, VIS_H = 790, 456
ANCHOR_X, ANCHOR_Y = 2, 2
XMIN, YMIN = -ANCHOR_X * 20, -ANCHOR_Y * 20
XMAX, YMAX = (VIS_W - ANCHOR_X) * 20, (VIS_H - ANCHOR_Y) * 20  # 15760, 9080

# Keep 631-634 + add bg-oeste5 as 635
ITEMS = [
    (631, ROOT / "Library" / "Images" / "backgrounds" / "631.png"),
    (632, OESTE_BG / "bg-oeste1.png"),
    (633, OESTE_BG / "bg-oeste2.png"),
    (634, OESTE_BG / "bg-oeste3.png"),
    (635, OESTE_BG / "bg-oeste5.png"),
]


def empty_link_sprite(sprite_id: int) -> bytes:
    body = ie.ui16(sprite_id) + ie.ui16(1) + ie.record_header(1, 0) + ie.record_header(0, 0)
    return ie.record_header(39, len(body)) + body


def define_shape4_hd_fill(shape_id: int, bitmap_id: int, png_w: int, png_h: int) -> bytes:
    """Bitmap fill: HD pixels stretched exactly over validated shape bounds."""
    bounds = ie.encode_rect(XMIN, XMAX, YMIN, YMAX)
    scale_x = (XMAX - XMIN) / float(png_w)
    scale_y = (YMAX - YMIN) / float(png_h)

    fill = bytearray()
    fill += ie.ui8(1)
    fill += ie.ui8(0x41)  # same fill type as validated g_oeste Shape2
    fill += ie.ui16(bitmap_id)
    fill += encode_matrix_scale_translate(scale_x, scale_y, XMIN, YMIN)

    def sbits(v: int) -> int:
        return 1 if v == 0 else v.bit_length() + 1

    move_n = max(sbits(XMIN), sbits(YMIN), 1)
    sc = "000011"
    sc += ie.encode_ub(move_n, 5) + ie.encode_sb(XMIN, move_n) + ie.encode_sb(YMIN, move_n)
    sc += ie.encode_ub(1, 1)

    def straight_edge(dx: int, dy: int) -> str:
        nbits = max(sbits(dx), sbits(dy), 2)
        bits = "11" + ie.encode_ub(nbits - 2, 4)
        if dx != 0 and dy != 0:
            bits += "1" + ie.encode_sb(dx, nbits) + ie.encode_sb(dy, nbits)
        elif dx != 0:
            bits += "00" + ie.encode_sb(dx, nbits)
        else:
            bits += "01" + ie.encode_sb(dy, nbits)
        return bits

    w_tw, h_tw = XMAX - XMIN, YMAX - YMIN
    edges = (
        straight_edge(w_tw, 0)
        + straight_edge(0, h_tw)
        + straight_edge(-w_tw, 0)
        + straight_edge(0, -h_tw)
        + "000000"
    )
    body = (
        ie.ui16(shape_id)
        + bounds
        + bounds
        + ie.ui8(0)
        + bytes(fill)
        + ie.ui8(0)
        + ie.ui8(0x10)
        + ie.bits_to_bytes(sc + edges)
    )
    return ie.record_header(83, len(body)) + body


def build_symbol(img: Image.Image, bitmap_id: int, shape_id: int, sprite_id: int, name: str) -> bytes:
    rgba = img.convert("RGBA")
    print(
        f"  export {name}: bitmap {rgba.size} -> visible {VIS_W}x{VIS_H} "
        f"scale=({(XMAX - XMIN) / rgba.width:.4f},{(YMAX - YMIN) / rgba.height:.4f})"
    )
    return (
        ie.define_bits_lossless2(bitmap_id, rgba)
        + define_shape4_hd_fill(shape_id, bitmap_id, rgba.width, rgba.height)
        + ie.define_sprite(sprite_id, shape_id)
        + ie.export_assets(sprite_id, name)
    )


def build_pack(out_path: Path) -> list[tuple[int, Path, Image.Image]]:
    tags = bytearray()
    tags += ie.record_header(69, 4) + b"\x00\x00\x00\x00"
    tags += ie.record_header(9, 3) + bytes([0xFF, 0xFF, 0xFF])

    loaded: list[tuple[int, Path, Image.Image]] = []
    next_id = 1
    for export_id, src in ITEMS:
        if not src.is_file():
            raise SystemExit(f"missing {src}")
        img = Image.open(src).convert("RGBA")
        loaded.append((export_id, src, img))
        bitmap_id = next_id
        shape_id = next_id + 1
        sprite_id = next_id + 2
        next_id += 3
        tags += build_symbol(img, bitmap_id, shape_id, sprite_id, str(export_id))

    link_id = next_id
    tags += empty_link_sprite(link_id)
    tags += ie.export_assets(link_id, LINK_NAME)
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
    return loaded


def upsert_grounds_xml(ids: list[int]) -> None:
    """Localized edit; preserve historical null-padded file size when possible."""
    raw = GROUNDS_XML.read_bytes()
    orig_len = len(raw)
    # Strip trailing NULs for text ops, then re-pad.
    text = raw.rstrip(b"\x00").decode("utf-8")
    for gid in ids:
        entry = (
            f"  <Pos>\n"
            f"    <ID>{gid}</ID>\n"
            f"    <X>{ANCHOR_X}</X>\n"
            f"    <Y>{ANCHOR_Y}</Y>\n"
            f"  </Pos>\n"
        )
        if f"<ID>{gid}</ID>" in text:
            text2, n = re.subn(
                rf"\s*<Pos>\s*<ID>{gid}</ID>\s*<X>-?\d+</X>\s*<Y>-?\d+</Y>\s*</Pos>\s*",
                "\n" + entry,
                text,
                count=1,
            )
            if n != 1:
                raise SystemExit(f"failed to update grounds.xml id {gid}")
            text = text2
            print(f"grounds.xml updated {gid} -> ({ANCHOR_X},{ANCHOR_Y})")
        else:
            close = "</ArrayOfPos>"
            if close not in text:
                raise SystemExit("grounds.xml missing close")
            text = text.replace(close, entry + close, 1)
            print(f"grounds.xml appended {gid} -> ({ANCHOR_X},{ANCHOR_Y})")
    out = text.encode("utf-8")
    payload = out if len(out) > orig_len else out + b"\x00" * (orig_len - len(out))
    tmp = GROUNDS_XML.with_suffix(".xml.tmp_oeste_bg")
    tmp.write_bytes(payload)
    tmp.replace(GROUNDS_XML)
    print(f"grounds.xml size={GROUNDS_XML.stat().st_size} (was {orig_len})")


def verify_pack(path: Path, expected: list[str]) -> None:
    raw = path.read_bytes()
    body = raw[8:]
    if raw[:3] == b"CWS":
        body = zlib.decompress(body)

    def iter_tags(b: bytes):
        nbits = b[0] >> 3
        i = (5 + nbits * 4 + 7) // 8 + 4
        while i < len(b):
            code_len = struct.unpack_from("<H", b, i)[0]
            code = code_len >> 6
            length = code_len & 0x3F
            hdr = 2
            if length == 0x3F:
                length = struct.unpack_from("<I", b, i + 2)[0]
                hdr = 6
            yield code, b[i + hdr : i + hdr + length]
            i += hdr + length
            if code == 0:
                break

    exports: list[str] = []
    bitmaps: list[tuple[int, int, int]] = []
    shapes: list[tuple[int, int, int, int, int]] = []
    for code, payload in iter_tags(body):
        if code == 36:
            cid, fmt, w, h = struct.unpack_from("<HBHH", payload, 0)
            bitmaps.append((cid, w, h))
        if code == 83:
            cid = struct.unpack_from("<H", payload, 0)[0]
            bb = payload[2:]
            nb = bb[0] >> 3

            def read_sb(bitpos: int, n: int):
                v = 0
                for i in range(n):
                    byte = bb[(bitpos + i) // 8]
                    bit = 7 - ((bitpos + i) % 8)
                    v = (v << 1) | ((byte >> bit) & 1)
                if n and v & (1 << (n - 1)):
                    v -= 1 << n
                return v, bitpos + n

            bp = 5
            xmin, bp = read_sb(bp, nb)
            xmax, bp = read_sb(bp, nb)
            ymin, bp = read_sb(bp, nb)
            ymax, bp = read_sb(bp, nb)
            shapes.append((cid, xmin, xmax, ymin, ymax))
        if code == 56:
            n = struct.unpack_from("<H", payload, 0)[0]
            p = 2
            for _ in range(n):
                p += 2
                e = payload.index(0, p)
                exports.append(payload[p:e].decode("latin1"))
                p = e + 1

    print("exports:", exports)
    print("bitmaps:", bitmaps)
    for cid, xmin, xmax, ymin, ymax in shapes:
        print(
            f"shape {cid}: twips X={xmin}..{xmax} Y={ymin}..{ymax} "
            f"vis={(xmax - xmin) / 20:.0f}x{(ymax - ymin) / 20:.0f}"
        )
        if (xmin, xmax, ymin, ymax) != (XMIN, XMAX, YMIN, YMAX):
            raise SystemExit(f"shape {cid} bounds mismatch")
    for name in expected:
        if name not in exports:
            raise SystemExit(f"missing export {name}")
    if LINK_NAME not in exports:
        raise SystemExit("missing link export")
    expected_n = len(ITEMS)
    if len(bitmaps) != expected_n:
        raise SystemExit(f"expected {expected_n} bitmaps, got {len(bitmaps)}")
    for cid, w, h in bitmaps:
        if w < 1000 or h < 900:
            raise SystemExit(f"bitmap {cid} not HD: {w}x{h}")
    print("VERIFY OK")


def main() -> None:
    work = ROOT / "artifacts" / "_oeste_inspect" / "g_oeste_multi_hd"
    work.mkdir(parents=True, exist_ok=True)
    cand = work / "g_oeste_candidate.swf"

    print("Building candidate…")
    loaded = build_pack(cand)
    verify_pack(
        cand,
        [str(i) for i, _, _ in loaded],
    )

    # Library previews (full HD PNGs)
    LIB_BG.mkdir(parents=True, exist_ok=True)
    for export_id, src, img in loaded:
        dest = LIB_BG / f"{export_id}.png"
        if export_id == 631 and dest.resolve() == src.resolve():
            print(f"library {dest.name} already source")
            continue
        img.save(dest, "PNG")
        print(f"library wrote {dest} ({img.size})")

    upsert_grounds_xml([i for i, _, _ in loaded])

    # Install to client with backup
    live = CLIENT / "clips" / "gfx" / PACK_NAME
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    bak = live.with_suffix(live.suffix + f".bak_multi_{stamp}")
    shutil.copy2(live, bak)
    shutil.copy2(cand, live)
    art = ROOT / "artifacts" / "_oeste_gfx"
    art.mkdir(parents=True, exist_ok=True)
    shutil.copy2(cand, art / PACK_NAME)
    print(f"installed {live} (backup {bak.name})")
    print("Done. Close and reopen client to test.")


if __name__ == "__main__":
    main()
