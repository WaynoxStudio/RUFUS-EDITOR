# Portable Distribution — RUFUS Map Editor

Fase **9P.3** — distribución ZIP portable para Windows x64.

## Objetivo

Carpeta `RUFUS Map Editor\` que:

- se comprime en ZIP;
- funciona en cualquier ruta Windows x64;
- no requiere .NET instalado;
- no requiere Astria Map Editor instalado;
- detecta automáticamente `.\Library\` junto al EXE.

## Build

```powershell
cd "C:\Users\rubez\Desktop\RUFUS EDITOR"
.\scripts\build-portable-dist.ps1
```

**Salidas:**

| Artefacto | Ruta |
|-----------|------|
| Carpeta | `dist\RUFUS Map Editor\` |
| ZIP | `dist\RUFUS Map Editor.zip` |
| EXE desarrollo (sin cambiar flujo) | `RufusMapEditor.exe` (raíz repo) |

**Fuente de copia Library (build-time):**

- **`{repoRoot}\Library`** — Master Library RUFUS (obligatoria)
- Si falta: el script falla con *"No se encontró la Master Library de RUFUS"*
- **No** usa Astria Map Editor 1 en build normal (Fase 9UI.2)

Esto **no** es una dependencia runtime del paquete publicado.

## Arquitectura de rutas

### Runtime (producto)

| Concepto | Resolución |
|----------|------------|
| Directorio app | `Environment.ProcessPath` → carpeta del EXE (**no** `GetCurrentDirectory`) |
| Library portable | `{ExeDir}\Library\` |
| Prioridad carga | 1) Library hermana válida → 2) `settings.json` LibraryPath → 3) mensaje + selector manual |
| Settings | `%LocalAppData%\RufusMapEditor\settings.json` |
| Autosave | `%LocalAppData%\RufusMapEditor\autosave\` |
| Export SWF temp | `%TEMP%\RufusMapEditor\swf-export\{guid}\` |
| Proyectos `.rufmap` | Ruta elegida por el usuario |

### Build-time (desarrollo)

Scripts en `scripts/` resuelven el repo con rutas relativas desde la raíz `RUFUS EDITOR`.
La instalación Astria de referencia solo se usa en tests opcionales de compatibilidad.

## Estructura de distribución

```text
RUFUS Map Editor\
├─ RufusMapEditor.exe      # self-contained single-file win-x64
├─ README.txt
├─ manifest.json           # hashes componentes críticos
└─ Library\
   ├─ Images\
   │  ├─ backgrounds\
   │  ├─ grounds\         # subcarpetas por categoría
   │  └─ objects\
   ├─ XML\
   │  ├─ grounds.xml
   │  └─ objects.xml
   ├─ Flasm\
   │  ├─ flasm.exe
   │  ├─ blank.swf
   │  └─ flasm.ini        # opcional, si existía en origen
   └─ Maps\
      └─ {id}\
         ├─ {id}.sql      # obligatorio por mapa
         └─ {id}_AME.swf o {id}.swf   # metadata SWF (opcional por mapa)
```

## Tabla de recursos

| Recurso | Ruta original (referencia dev) | Función | Necesario runtime | Incluido en ZIP | Motivo |
|---------|-------------------------------|---------|-------------------|-----------------|--------|
| EXE RUFUS | build publish | Editor | Sí | Sí | Producto |
| `Images/backgrounds` | Astria `Images/backgrounds` | Render catálogo BG | Sí (render) | Sí | GFX backgrounds |
| `Images/grounds` | Astria `Images/grounds` | Render/edición ground | Sí | Sí | GFX ground |
| `Images/objects` | Astria `Images/objects` | Render/edición objects | Sí | Sí | GFX objects |
| `XML/grounds.xml` | Astria `XML/grounds.xml` | Anchors ground | Sí | Sí | Placement |
| `XML/objects.xml` | Astria `XML/objects.xml` | Anchors object | Sí | Sí | Placement |
| `Flasm/flasm.exe` | Astria `Flasm/flasm.exe` | Export SWF AME | Solo export | Sí | Pipeline SWF |
| `Flasm/blank.swf` | Astria `Flasm/blank.swf` | Plantilla SWF | Solo export | Sí | Pipeline SWF |
| `Maps/{id}/{id}.sql` | Astria `Maps/` | MapData + metadatos SQL | No arranque* | Sí | Contenido inicial 30 mapas |
| `Maps/{id}/*.swf` | Astria map folders | Metadata Outdoor/BG | Opcional | Sí (swf/ame) | Paridad metadata |
| `Astria Map Editor.exe` | Astria root | — | No | **NO** | No usado por RUFUS |
| DLL Astria | Astria root | — | No | **NO** | No usadas |
| `Musiques/` | Astria | — | No | **NO** | No leído |
| `Géopositions/` | Astria | — | No | **NO** | Fase futura |
| `XML/areas.xml` etc. | Astria XML | — | No | **NO** | No parseado |
| `.ame` map files | Map folders | — | No | **NO** | No leído |
| Map preview PNG | Map folders | — | No | **NO** | No leído |
| `_refs/YacoEmulator` | — | Eliminado | No | **NO** | YACO reference removed; not a RUFUS source of truth. |
| Settings personales | LocalAppData | UX usuario | No | **NO** | Por usuario |
| Autosaves personales | LocalAppData | Recovery | No | **NO** | Por usuario |

\* El editor **arranca** con `Maps\` vacío (aviso), pero sin mapas no hay contenido editable.

## Validación Library

Clase: `PortableLibraryValidator` (`RufusMapEditor.LegacyCompatibility.Portable`)

| Comprobación | Bloquea editor | Bloquea export SWF |
|--------------|----------------|-------------------|
| `Maps\` existe | Sí | — |
| `Images\{backgrounds,grounds,objects}` | Sí | — |
| `XML\grounds.xml`, `objects.xml` | Sí | — |
| `Flasm\flasm.exe` + `blank.swf` | No (aviso) | Sí |

## Auto-detección

`MainViewModel.TryLoadSavedLibrary()`:

1. `PortableLibraryPaths.GetSiblingLibraryPath()` + validación
2. Fallback `settings.json` → `LibraryPath`
3. Mensaje: *«No se encontró la biblioteca de RUFUS Map Editor»* + botón Biblioteca...

## Informe rutas absolutas (runtime productivo)

Búsqueda en `src/**/*.cs` (excl. `obj/`):

| Patrón | En runtime productivo |
|--------|----------------------|
| `C:\Users\rubez` | **NO** |
| `Desktop\RUFUS` | **NO** |
| `Astria Map Editor 1` | **NO** |
| `GetCurrentDirectory()` para Library | **NO** |

Referencias en **tests** y **scripts** usan rutas de desarrollo o `ASTRIA_MAP_EDITOR_ROOT` — aceptable.

## Smoke test portable

1. `.\scripts\build-portable-dist.ps1`
2. Descomprimir ZIP en `%TEMP%\RUFUS Portable Test\`
3. Ejecutar `RufusMapEditor.exe`
4. Verificar auto-carga Library, mapa 10420, edición, `.rufmap`, Export SWF
5. Sin acceso a carpeta Astria original

## Limitaciones actuales

- Sin MSI / Setup / asociación `.rufmap`
- Sin servidor RUFUS / BD / crypto cliente
- Mapas incluidos = **contenido inicial**, no dependencia estructural
- Background `340.png` ausente en origen → warnings conocidos en mapas 3000x

## Tests automáticos

`tests/.../Portable/PortableLibraryTests.cs`:

- exe dir ≠ cwd
- validación layout mínimo
- rutas con espacios
- Flasm opcional

Suite activa debe permanecer en verde (114+ tests).

---

## Fase 9P.3 — resumen

Ver documento completo: [`PORTABLE_DISTRIBUTION.md`](PORTABLE_DISTRIBUTION.md).

Build: `.\scripts\build-portable-dist.ps1` → `dist\RUFUS Map Editor\` + ZIP.

Auto-detección: `{ExeDir}\Library\` vía `PortableLibraryPaths` + `PortableLibraryValidator`.
