# Map crypto

YACO reference removed; not a RUFUS source of truth.

Historical note: earlier investigations compared against an external emulator dump.
That tree (`_refs/YacoEmulator`), optional YACO tests, and YACO artifacts were **removed**
from this workspace. Do not restore or use them for RUFUS build, runtime, or tests.

## Status (RUFUS)

| Topic | Label |
|-------|--------|
| RUFUS production MapData crypto | `DATO PENDIENTE DE CONFIRMAR` (needs RUFUS-owned keys) |
| `LegacyMapCrypto` in editor | Generic XOR+hex for investigation; **not** confirmed RUFUS production crypto |
| Astria decrypt/export patterns | `REFERENCIA ASTRIA` |
| Client SWF MapData length (e.g. 10420) | `CONFIRMADO CLIENTE RUFUS` where validated against RUFUS client files |

## Tests

Suite productiva: `tests/RufusMapEditor.LegacyCompatibility.Tests` (math roundtrip on Astria fixtures).
YACO reference tests: **ELIMINADOS**.

See also: `docs/RUFUS_CLIENT_MAP_PIPELINE.md`.
