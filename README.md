# RUFUS EDITOR

Editor DOFUS Retro: **mapas**, **NPC/diálogos/misiones** y **ajustes** en un solo escritorio.

## Producto

| Artefacto | Rol |
|-----------|-----|
| **`RufusAdmin.exe`** | Único ejecutable de escritorio (raíz del repo o `dist-admin`) |
| `Library\` | Assets (mapas, GFX, XML) — debe quedar junto al EXE en uso portable |
| `src\RufusMapEditor.App` | Librería UI (ya no genera EXE propio) |

Documentación de uso y arquitectura: **[docs/RUFUS_ADMIN.md](docs/RUFUS_ADMIN.md)**  
Índice completo: **[docs/README_DOCS.md](docs/README_DOCS.md)**  
Notas recientes: **[docs/SESSION_NOTES_2026-10.md](docs/SESSION_NOTES_2026-10.md)**

## Requisitos

- Windows
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (solo para compilar)

## Compilar / ejecutar

```bat
dotnet build src\RufusMapEditor.Admin\RufusMapEditor.Admin.csproj -c Debug
src\RufusMapEditor.Admin\bin\Debug\net10.0-windows\RufusAdmin.exe
```

Paquete self-contained:

```powershell
.\scripts\build-admin-dist.ps1
```

## Tests

```bat
dotnet test tests\RufusMapEditor.LegacyCompatibility.Tests
```

## Estructura

| Ruta | Contenido |
|------|-----------|
| `src/` | Código (Admin, App-as-library, AiBackend, Domain, …) |
| `Library/` | Assets del editor |
| `docs/` | Documentación técnica y de producto |
| `scripts/` | Build / publish Admin |
| `tests/` | Tests |
| `tools/` | FFDec / JRE y utilidades |

## Colaboración

1. `git clone https://github.com/WaynoxStudio/RUFUS-EDITOR.git` (rama **`main`**)
2. Abrir `RufusMapEditor.slnx` o lanzar `RufusAdmin.exe`
3. No subir secretos ni basura de build (`bin/`, `obj/`, `artifacts/`)
4. Empezar por `docs/README_DOCS.md`
