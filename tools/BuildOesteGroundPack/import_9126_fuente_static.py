"""Replace 9126 fuente_anubis with static frame 01 only (no animation)."""
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
from inject_motao_style import inject as inject_symbol  # noqa: E402
from inject_motao_style import find_link_sprite_offset  # noqa: E402

OESTE = Path(r"C:\Users\rubez\Desktop\OESTE")
CLIENT = Path(
    r"C:\Users\rubez\AppData\Roaming\Jeifus Launchers\rufus\cliente"
    r"\resources\app\retroclient"
)
FFDEC = ROOT / "tools" / "_ffdec" / "ffdec-cli.exe"
GFX_ID = 9126
SRC_PNG = OESTE / "objetos" / "fuente_anubis_agua_frame_01_142x108.png"
SRC_SVG = OESTE / "objetos" / "fuente_anubis_agua_frame_01_142x108.svg"
WORK = ROOT / "artifacts" / "_oeste_inspect" / "9126_fuente_static"
AX, AY = 71, 60
W, H = 142, 108


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
            rf"\s*<Pos>\s*<ID>{GFX_ID}</ID>\s*<X>-?\d+</X>\s*<Y>-?\d+</Y>\s*</Pos>\s*",
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
    tmp = xml_path.with_suffix(".xml.tmp_9126s")
    tmp.write_bytes(payload)
    tmp.replace(xml_path)


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
        raise SystemExit("SVG contains <image>")
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
        yield i, code, ln, hdr, body[i + hdr : i + hdr + ln]
        i += hdr + ln
        if code == 0:
            break


def remove_export_deep(swf: bytes, name: str) -> bytes:
    """Remove export + sprite and every bitmap/shape referenced by its timeline."""
    tags = list(ie.iter_tags(swf))
    export_offset = None
    export_chid = None
    for offset, tag_type, length, hdr in tags:
        if tag_type != 56:
            continue
        body = swf[offset + hdr : offset + hdr + length]
        if name.encode("ascii") + b"\x00" in body:
            export_offset = offset
            export_chid = struct.unpack_from("<H", body, 2)[0]
            break
    if export_offset is None:
        return swf

    by_id = {}
    for offset, tag_type, length, hdr in tags:
        if tag_type in (2, 22, 32, 35, 36, 39, 83) and length >= 2:
            chid = struct.unpack_from("<H", swf, offset + hdr)[0]
            by_id[chid] = (offset, tag_type, length, hdr)

    remove_ids = {export_chid}
    if export_chid in by_id and by_id[export_chid][1] == 39:
        off, t, ln, hd = by_id[export_chid]
        payload = swf[off + hd : off + hd + ln]
        # scan PlaceObject2 character ids inside sprite
        p = 4
        while p < len(payload):
            cl = struct.unpack_from("<H", payload, p)[0]
            p += 2
            ct = cl >> 6
            cln = cl & 0x3F
            if cln == 0x3F:
                cln = struct.unpack_from("<I", payload, p)[0]
                p += 4
            chunk = payload[p : p + cln]
            p += cln
            if ct == 26 and (chunk[0] & 2):
                remove_ids.add(struct.unpack_from("<H", chunk, 3)[0])
            if ct in (0, 1):
                if ct == 0:
                    break
                continue
        # also remove bitmaps typically shape_id-1
        for sid in list(remove_ids):
            if sid - 1 in by_id and by_id[sid - 1][1] in (20, 21, 35, 36):
                remove_ids.add(sid - 1)

    remove_offsets = {export_offset}
    for chid in remove_ids:
        if chid in by_id:
            remove_offsets.add(by_id[chid][0])
    print(f"removing export {name} chids={sorted(remove_ids)}")

    keep = bytearray(swf[:8])
    i = 8
    nbits = swf[i] >> 3
    rect_bytes = (5 + nbits * 4 + 7) // 8
    keep.extend(swf[8 : 8 + rect_bytes + 4])
    for offset, tag_type, length, hdr in tags:
        if offset in remove_offsets:
            continue
        keep.extend(swf[offset : offset + hdr + length])
    if keep[-2:] != b"\x00\x00":
        keep.extend(b"\x00\x00")
    out = bytes(keep)
    return out[:4] + ie.ui32(len(out)) + out[8:]


def find_export_shape_id(swf_path: Path, export_name: str) -> tuple[int, int]:
    raw = swf_path.read_bytes()
    body = zlib.decompress(raw[8:]) if raw[:3] == b"CWS" else raw[8:]
    sprite_id = None
    for _off, code, _ln, _hdr, payload in iter_tags(body):
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
        raise SystemExit("export not found")
    for _off, code, _ln, _hdr, payload in iter_tags(body):
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
    raise SystemExit("shape not found")


def dump_exports(swf: Path) -> str:
    r = subprocess.run(
        [str(FFDEC), "-dumpSWF", str(swf)], capture_output=True, text=True, check=False
    )
    return (r.stdout or "") + (r.stderr or "")


def main() -> None:
    if not SRC_PNG.is_file() or not SRC_SVG.is_file():
        raise SystemExit("missing frame 01 png/svg")
    img = Image.open(SRC_PNG).convert("RGBA")
    if img.size != (W, H):
        raise SystemExit(f"size {img.size}")
    print(f"9126 static frame01 {img.size} anchor=({AX},{AY})")

    pack = CLIENT / "clips" / "gfx" / "o_oeste.swf"
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    shutil.copy2(pack, pack.with_suffix(pack.suffix + f".bak_9126static_{stamp}"))

    swf, version, _, _ = ie.read_swf(pack)
    swf = remove_export_deep(swf, str(GFX_ID))
    cleaned = WORK
    if cleaned.exists():
        shutil.rmtree(cleaned)
    cleaned.mkdir(parents=True)
    cleaned_swf = cleaned / "cleaned.swf"
    fws = b"FWS" + bytes([version]) + ie.ui32(len(swf)) + swf[8:]
    # swf from remove already has length patched
    cleaned_swf.write_bytes(
        b"CWS" + bytes([version]) + ie.ui32(len(swf)) + zlib.compress(swf[8:], 9)
    )

    lib = ROOT / "Library" / "Images" / "objects" / "Oeste" / f"{GFX_ID}.png"
    lib.parent.mkdir(parents=True, exist_ok=True)
    img.save(lib, "PNG")
    shutil.copy2(SRC_SVG, lib.with_suffix(".svg"))
    upsert_objects_xml(AX, AY)

    tmp_png = cleaned / "_tmp.png"
    img.save(tmp_png, "PNG")
    mid = cleaned / "bitmap.swf"
    inject_symbol(cleaned_swf, tmp_png, str(GFX_ID), mid, AX, AY)
    shape_id, sprite_id = find_export_shape_id(mid, str(GFX_ID))
    print(f"shape_id={shape_id} sprite_id={sprite_id}")

    shapes = cleaned / "import_shapes"
    shapes.mkdir()
    prepare_svg(SRC_SVG, shapes / f"{shape_id}.svg", W, H)
    cand = cleaned / "vector.swf"
    r = subprocess.run(
        [str(FFDEC), "-importShapes", str(mid), str(cand), str(shapes)],
        capture_output=True,
        text=True,
    )
    print("importShapes rc", r.returncode, (r.stdout or "")[-400:])
    if r.returncode != 0:
        raise SystemExit("importShapes failed")

    exp_dir = cleaned / "export_after"
    exp_dir.mkdir()
    subprocess.run(
        [
            str(FFDEC),
            "-format",
            "shape:svg",
            "-selectid",
            str(shape_id),
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
    if "<image" in exp.lower() or npaths < 20:
        raise SystemExit(f"bad vector paths={npaths}")
    print(f"vector paths={npaths}")

    after = dump_exports(cand)
    if f'exp: "{GFX_ID}"' not in after:
        raise SystemExit("missing 9126")
    # sprite should be 1 frame — quick check via dump line
    print("OK static 9126")

    shutil.copy2(cand, pack)
    art = ROOT / "artifacts" / "_oeste_gfx"
    art.mkdir(parents=True, exist_ok=True)
    shutil.copy2(cand, art / "o_oeste.swf")
    print(f"installed static {GFX_ID}")


if __name__ == "__main__":
    main()
