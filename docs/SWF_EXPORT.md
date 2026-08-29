# Exportación SWF / Flasm (RUFUS Map Editor)

## Objetivo

Generar un SWF de mapa compatible con DOFUS Retro a partir del `MapDocument` editable (desde biblioteca Astria o `.rufmap`), sin instalar en el cliente y sin escribir SQL.

## Pipeline Astria original (confirmado)

Fuente: `_refs/AstriaMapEditor` + instalación local  
`C:\Users\rubez\Desktop\RUFUS\Astria Map Editor 1` (**solo lectura**).

```text
Map (Astria)
  → Builder.GetMapData(map)          // MapData string
  → Flasm.Get_FlasmCode(map)         // genera temp.flm
  → flasm.exe -a temp.flm            // CWD = raíz Astria
  → modifica Flasm\blank.swf in-place
  → deja Flasm\blank.$wf (backup de la plantilla)
  → Move blank.swf → Maps\{id}\{id}_{DateMap}.swf
  → Rename blank.$wf → blank.swf
```

Referencias:

- `SWF/Flasm.vb` — `Get_FlasmCode`, `Compile`
- `MapEditor/MapEditor.vb` — `Save_SWF`
- `bin/Debug/compil.bat` — mismo flujo por lotes

Astria usa `Process` + `Thread.Sleep` para esperar `blank.$wf`. RUFUS **no** usa sleep: espera `WaitForExit` real.

### Plantilla

`Flasm/blank.swf` (~636 bytes). Flasm exige plantilla >100 bytes.

### Campos escritos en el SWF

| Campo ActionScript | Origen MapDocument |
|---|---|
| `id` | `Id` |
| `width` | `Width` |
| `height` | `Height` |
| `backgroundNum` | `BackgroundId` |
| `ambianceId` | `AmbianceId` |
| `musicId` | `MusicId` |
| `bOutdoor` | `Outdoor` (obligatorio; no se inventa) |
| `capabilities` | `Capabilities` |
| `mapData` | `Encode(Cells)` actual |

También se emite el bloque `System.security.allowDomain` como en Astria (constantes c:0–c:4).

## Pipeline RUFUS

```text
MapDocument (editable)
  → SyncMapDataString / Encode
  → FlasmScriptBuilder.Build → temp.flm
  → copiar blank.swf a carpeta temp privada
  → FlasmProcessRunner: flasm.exe -a temp.flm (WorkingDirectory = temp)
  → validar blank.swf generado (>0) + blank.$wf presente
  → FlasmSwfMetadataReader read-back (metadata + MapData)
  → comparar con documento
  → copiar atómico al destino elegido por el usuario
  → borrar temp
```

**Nunca** se escribe en `Astria\Flasm` ni en `Astria\Maps`.

### Representación intermedia

Archivo `.flm` (texto Flasm), CRLF, con:

- `movie 'blank.swf' compressed`
- pool `constants` incluyendo `mapData` + string MapData
- `push`/`setVariable` para cada campo

### Flasm

- Ejecutable: `{biblioteca}/Flasm/flasm.exe`
- `ProcessStartInfo.ArgumentList` (sin `cmd.exe`)
- Captura stdout/stderr/exit/timeout
- Rutas con espacios soportadas (CWD + ArgumentList)

## Validaciones

**Pre-export:** Map ID, Width/Height, celdas coherentes, MapData longitud/alfabeto, `Outdoor` presente, Flasm y blank.swf presentes.

**Post-export (obligatorio):** archivo >0, read-back Flasm, igualdad id/width/height/background/ambiance/music/outdoor/capabilities/mapData. Solo entonces EXPORT OK.

## Resource Gap 30001–30004

El PNG `Background:340` puede faltar para render. El SWF **solo almacena** `backgroundNum = 340`. La exportación **preserva** ese ID; no inventa otro fondo.

## Diferencias binarias Astria vs RUFUS

No se exige igualdad byte-a-byte. Equivalencia **semántica** de los campos anteriores. Tamaños SWF pueden diferir (compresión / plantilla). Informe ejemplo: `tests/artifacts/swf/10420_astria_vs_rufus.md`.

## UI

`Archivo → Exportar → SWF...` (SaveFileDialog). No marca dirty ni altera Undo/Redo. Diálogo de resultado con ruta, MapData chars, Flasm OK, Read-back OK, tiempo.

## Limitaciones (fuera de Fase 8)

- No instalación automática en cliente DOFUS
- No SQL RUFUS
- No import `.ame`
- Fight cells / geoposición / mobs / etc. no van en este SWF de mapa
