# RUFUS Map Package & Official Save

## Dos acciones distintas

| Acción | Destino | Contenido |
|---|---|---|
| **Guardar / Ctrl+S** (Official Map Save) | `Library\Maps\<MapId>\` | CORE: `.rufmap` + `.png` + `_MapData.txt` (+ `_AME.swf` opcional) |
| **Archivo → Exportar → Paquete de mapa...** | carpeta elegida por el usuario | diagnóstico completo (MapData TXT, ModeCell, GfxList, manifest, Legacy SWF) |
| **Publicar** | futuro | BD / cliente RUFUS |

Autosave sigue en `%LocalAppData%\RufusMapEditor\...` — **recovery-only**, no toca `Library\Maps` ni genera `_MapData.txt`.

## Official Map Save (Fase 9S.3)

```
<Library>\Maps\<MapId>\
├─ <MapId>.rufmap          # editable completo (MapData + FightPlaces + metadata)
├─ <MapId>.png              # render limpio, crop export (15×17 → 728×416)
├─ <MapId>_MapData.txt      # MapData plano; UTF-8 sin BOM; sin cabeceras ni saltos de línea
└─ <MapId>_AME.swf         # opcional (Flasm + Outdoor)
```

### Roles

| Artefacto | Rol |
|---|---|
| `.rufmap` | Formato editable RUFUS completo |
| `_MapData.txt` | Export limpio del MapData (copia directa) |
| `.png` | Render limpio (sin rejilla / overlays / IDs) |
| `_AME.swf` | Export legacy opcional |

### Reglas

- Misma carpeta por MapId (sin `_v2` / `_new` / `_MapData_2.txt`).
- Cada Guardar **reconstruye** la carpeta (reemplazo atómico vía staging).
- MapData del TXT = MapData canónico del documento (misma fuente que `.rufmap` / AME SWF).
- Longitud esperada: `CellCount * 10` (p. ej. 15×17 → 4790).
- Archivos ajenos / ModeCell / manifest / GfxList / SQL / AME **no sobreviven**.
- Si el SWF AME no puede generarse: CORE OK + warning; el SWF viejo **no** se conserva.
- FightPlaces **no** van al TXT (solo en `.rufmap`).

### No incluido en Ctrl+S

- `_ModeCell.png`
- `GfxID utilizados.txt`
- `manifest.txt`
- SQL producción / legacy
- `.ame`
- SWF cliente RUFUS

## Export Package (Fase 9S.1 + 9S.3)

Sigue disponible para diagnóstico externo:

```
<Parent>\<MapId>\
├─ <MapId>.rufmap
├─ <MapId>.png
├─ <MapId>_MapData.txt
├─ <MapId>_ModeCell.png
├─ GfxID utilizados.txt
├─ manifest.txt
└─ Legacy\<MapId>_AME.swf
```

El `_MapData.txt` del paquete externo reutiliza el mismo escritor que Official Save (`MapDataPlainText`).

## Implementación

- Paths: `LibraryMapPaths`
- MapData TXT: `MapDataPlainText`
- Official save: `OfficialMapSave`
- Export diagnóstico: `MapPackageBuilder`
