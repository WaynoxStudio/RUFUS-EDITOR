# RUFUS Map Editor

Editor moderno de mapas DOFUS Retro.

## Requisitos

- Windows
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Compilar / ejecutar

```bat
dotnet build RufusMapEditor.slnx
dotnet run --project src/RufusMapEditor.App
```

## Tests

```bat
dotnet test tests/RufusMapEditor.LegacyCompatibility.Tests
```

## Estructura

| Ruta | Contenido |
|------|-----------|
| `src/` | Código fuente (App, Admin, AiBackend, librerías) |
| `tests/` | Tests y fixtures de compatibilidad |
| `Library/` | Assets del editor (imágenes, mapas, XML, visuals) |
| `docs/` | Documentación técnica |
| `tools/` | Utilidades de desarrollo |
| `scripts/` | Scripts auxiliares |

## Colaboración

1. Clonar: `git clone https://github.com/WaynoxStudio/RUFUS-EDITOR.git`
2. Abrir la solución `RufusMapEditor.slnx` en Visual Studio / Rider / VS Code
3. No subir builds (`dist/`, `bin/`, `obj/`) ni secretos (`.env`, claves API)
4. Ver `docs/` para compatibilidad Astria y licenciamiento
