# RUFUS Master Library

**Fase 9UI.2** — biblioteca operativa de RUFUS Map Editor.

## Ubicación

```
C:\Users\rubez\Desktop\RUFUS EDITOR\Library\
```

Layout:

```
Library\
├─ Images\
│  ├─ backgrounds\   (48)
│  ├─ grounds\       (549 archivos / 549 IDs)
│  └─ objects\       (5151 archivos / 4952 IDs únicos)
├─ XML\
│  ├─ grounds.xml
│  └─ objects.xml
├─ Flasm\
│  ├─ flasm.exe
│  └─ blank.swf
└─ Maps\             (30 carpetas legacy `{id}/{id}.sql` + SWF;
                     Official Save escribe `{id}.rufmap` + `{id}.png` + `{id}_MapData.txt` + opcional `{id}_AME.swf`)
```

## Official Map Save (Fase 9S.3)

`Ctrl+S` / **Guardar** reconstruye de forma atómica:

```
Library\Maps\<MapId>\
├─ <MapId>.rufmap
├─ <MapId>.png
├─ <MapId>_MapData.txt   # MapData plano; UTF-8 sin BOM; sin cabeceras ni saltos de línea. Pensado para copia directa.
└─ <MapId>_AME.swf   (opcional)
```

- `.rufmap` = editable completo (MapData + FightPlaces + metadata).
- `_MapData.txt` = MapData puro (misma cadena canónica que el documento en memoria).
- Discovery acepta `{id}.rufmap` y/o `{id}.sql` (un MapId, sin duplicados).
- Carga: preferir `.rufmap` si existe; si no, SQL legacy.
- Autosave **no** escribe aquí ni genera `_MapData.txt`.
- Exportar paquete (diagnóstico) es acción aparte — ver `docs/MAP_PACKAGE.md`.

## Pipeline

```
RUFUS EDITOR\Library  (master)
        ↓
RufusMapEditor.exe  (detecta .\Library hermana)
        ↓
scripts\build-portable-dist.ps1
        ↓
dist\RUFUS Map Editor\Library
        ↓
dist\RUFUS Map Editor.zip
```

## Resolución en runtime

1. `{ExeDir}\Library` — prioridad máxima (portable / desarrollo)
2. `settings.json` → `LibraryPath` — fallback manual
3. Selector **Archivo → Seleccionar biblioteca...**

Al cargar la Library hermana válida, `LibraryPath` en settings se actualiza a esa ruta.

## Astria

`C:\Users\rubez\Desktop\RUFUS\Astria Map Editor 1` queda **REFERENCE ONLY**.

- No se usa en build normal (`build-portable-dist.ps1` falla si falta master Library).
- No es dependencia runtime del producto.
- Tests de compatibilidad legacy pueden referenciarla explícitamente vía `RufusTestPaths.AstriaReferenceRoot`.

## Migración inicial (9UI.2)

Copia one-shot desde `dist\RUFUS Map Editor\Library` (validada) — no desde Astria en build normal.

## Añadir recursos RUFUS

1. Colocar PNG en la carpeta de categoría correcta bajo `Images\grounds` u `Images\objects`.
2. Actualizar `XML\grounds.xml` u `XML\objects.xml` con ancla si aplica.
3. Reconstruir portable si se distribuye.
4. No eliminar duplicados legacy conocidos sin análisis de compatibilidad.

## Validación

`PortableLibraryValidator.Validate(libraryRoot)` — requisitos mínimos para editor + export SWF.

Tests: `MasterLibraryTests.cs` — existencia, conteos, GfxID 374 namespace.
