# RUFUS ADMIN — producto de escritorio

Único ejecutable de escritorio del repo: **`RufusAdmin.exe`**.

Incluye en un solo shell:

| Sección | Qué hace |
|---------|----------|
| **Mapas** | Editor de mapas / mundo (`MapsEditorView`) |
| **NPC y diálogos** | Contenido: NPC, diálogos, misiones (`ContentWorkspaceView`) |
| **Ajustes** | Backend Admin, tema, caches, rutas |

No existe ya un `RufusMapEditor.exe` de producto. El proyecto `src/RufusMapEditor.App` es una **librería WPF** (`RufusMapEditor.dll`) hospedada por Admin.

## Cómo abrirlo

1. **Raíz del repo (doble clic):** `RufusAdmin.exe` (publish self-contained junto a `Library\`).
2. **Debug (desarrollo):**  
   `src\RufusMapEditor.Admin\bin\Debug\net10.0-windows\RufusAdmin.exe`
3. **Script de paquete:** `scripts\build-admin-dist.ps1` → `dist-admin\RUFUS ADMIN\`

```powershell
dotnet build src\RufusMapEditor.Admin\RufusMapEditor.Admin.csproj -c Debug
# o republish a la raíz:
dotnet publish src\RufusMapEditor.Admin\RufusMapEditor.Admin.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .
```

## Biblioteca (`Library\`)

- Ruta guardada en `%LocalAppData%\RufusMapEditor\settings.json` → `LibraryPath`.
- Si hay `.\Library` **junto al EXE**, tiene prioridad (modo portable).
- Mapas se descubren en `Library\Maps\{id}\` si hay `{id}.rufmap`, `{id}.sql` o SWF AME.

### Si la lista de mapas muestra muy pocos

El filtro de la lista de mapas **persiste** en settings (`MapListFilter.SearchText`).  
Ejemplo: `SearchText = "30021"` → solo aparece el mapa 30021 aunque `Library\Maps` tenga cientos.

Vaciar el buscador de mapas en la UI, o limpiar `MapListFilter` en `settings.json`.

## Arquitectura (resumen)

```
RufusAdmin.exe  (WinExe)
  └── referencia  RufusMapEditor.dll  (Library: Maps + Content UI)
        ├── Domain / Rendering / LegacyCompatibility
        └── lee Library\ (imágenes, XML, Maps, Visuals)
```

AiBackend es un **servicio web aparte** (no es el editor de escritorio).

## Qué no tocar al “limpiar” el repo

Conservar: `src\`, `Library\`, `scripts\`, `tests\`, `tools\`, `docs\`, solución.

Regenerable / basura de build: `artifacts\`, `dist\`, `dist-admin\`, `bin\`, `obj\`, `publish\`.

## Documentación relacionada

- [RUFUS_LIBRARY.md](RUFUS_LIBRARY.md) — estructura Library  
- [WORLD_EDITOR.md](WORLD_EDITOR.md) — vista mundo  
- [LANG_MAPS_AND_COORDS.md](LANG_MAPS_AND_COORDS.md) — maps_es / coords cliente  
- [DATABASE_PUBLISH.md](DATABASE_PUBLISH.md) — publicar mapas a BD  
- Índice: [README_DOCS.md](README_DOCS.md)
