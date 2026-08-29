# Colaboración — RUFUS Maps

Guía práctica para trabajar en el módulo Maps en paralelo con el resto del proyecto,
sin separar repositorios ni duplicar el editor.

## Quién hace qué

| Persona | Enfoque |
|---------|---------|
| **Ruben** | Todo el proyecto: NPC, diálogos, misiones, IA, Licensing, ADMIN, backend, contenido, etc. |
| **Colaborador** | Principalmente Maps: interfaz, UX, paneles, ventanas, herramientas del editor, arreglos del módulo |

No es una limitación de acceso: ambos pueden ver y tocar el código completo.
Es una estrategia para **minimizar conflictos Git**.

## Arquitectura USER / ADMIN (compartida)

```
MapsEditorView
      ↓
   USER (RufusMapEditor.App)

MapsEditorView
      ↓
   ADMIN (RufusMapEditor.Admin hospeda la misma vista)
```

La implementación es **una sola**. No crear una variante Maps exclusiva para ADMIN.

Flujo de cambios:

1. Cambiar Maps en `src/`
2. Merge a `main`
3. Rebuild USER y ADMIN
4. El cambio aparece en ambos

## Dónde trabajar

Trabaja sobre **`src/`**.

**No edites** outputs de compilación:

- `dist/`
- `dist-admin/`

Esos se regeneran con los scripts de build.

### Núcleo Maps (confirmado)

- `src/RufusMapEditor.App/MapsEditorView.xaml`
- `src/RufusMapEditor.App/MapsEditorView.xaml.cs`

### Controles Maps

- `src/RufusMapEditor.App/Controls/FloatingMapWindow.xaml`
- `src/RufusMapEditor.App/Controls/FloatingMapWindow.xaml.cs`
- `src/RufusMapEditor.App/Controls/MapViewport.xaml`
- `src/RufusMapEditor.App/Controls/MapViewport.xaml.cs`
- `src/RufusMapEditor.App/Controls/WorldViewport.xaml`
- `src/RufusMapEditor.App/Controls/WorldViewport.xaml.cs`

### ViewModels / documentos Maps

- `src/RufusMapEditor.App/ViewModels/MainViewModel.cs` *(grande; tocar con cuidado)*
- `src/RufusMapEditor.App/ViewModels/WorldViewModel.cs`
- `src/RufusMapEditor.App/ViewModels/OpenMapDocument.cs`
- `src/RufusMapEditor.App/ViewModels/GfxCatalogVms.cs`
- `src/RufusMapEditor.App/ViewModels/MapPublishQueueViewModel.cs`
- `src/RufusMapEditor.App/ViewModels/MapMonstersEditorViewModel.cs`
- `src/RufusMapEditor.App/ViewModels/MapFixedMobsEditorViewModel.cs`

### Ventanas / servicios típicos de Maps

- `src/RufusMapEditor.App/MapPickerWindow.xaml` (+ `.xaml.cs`)
- `src/RufusMapEditor.App/MapPublishQueueWindow.xaml` (+ `.xaml.cs`)
- `src/RufusMapEditor.App/MapPublishQueueEditWindow.xaml` (+ `.xaml.cs`)
- `src/RufusMapEditor.App/BackgroundPickerWindow.xaml` (+ `.xaml.cs`)
- `src/RufusMapEditor.App/ReplaceGfxWindow.xaml` (+ `.xaml.cs`)
- `src/RufusMapEditor.App/WorldCoordInputWindow.xaml` (+ `.xaml.cs`)
- `src/RufusMapEditor.App/WorldGridSizeWindow.xaml` (+ `.xaml.cs`)
- `src/RufusMapEditor.App/MapIdInputWindow.xaml` (+ `.xaml.cs`)
- `src/RufusMapEditor.App/ExportSwfResultWindow.xaml` (+ `.xaml.cs`)
- `src/RufusMapEditor.App/Services/MapEditSession.cs`
- `src/RufusMapEditor.App/Services/MultiMapEditService.cs`
- `src/RufusMapEditor.App/Services/MapPreviewCache.cs`
- `src/RufusMapEditor.App/Services/WorldThumbnailCache.cs`
- `src/RufusMapEditor.App/Services/EditorEnums.cs`

ADMIN solo **hospeda** Maps (no reimplementa):

- `src/RufusMapEditor.Admin/MainWindow.xaml.cs` (crea `MapsEditorView`)
- `src/RufusMapEditor.Admin/Navigation/AdminNavNotes.cs`

## Libertad del colaborador

Puedes modificar el módulo Maps con bastante libertad:

- rediseñar interfaz y UX
- reorganizar paneles y ventanas
- mover controles / simplificar acciones
- corregir problemas visuales y de navegación
- mejorar herramientas de mapas
- tocar code-behind y ViewModels de Maps si hace falta

Intenta preservar:

- funcionalidades existentes
- bindings y comandos
- compatibilidad USER / ADMIN
- publicación, carga/guardado
- renderer y herramientas existentes

Si cambias lógica funcional, hazlo de forma explícita y **documentalo en el PR**.

## Archivos compartidos — avisar antes de modificar

Si necesitas tocar estos archivos globales, hazlo en un **commit separado** y explícalo en el PR:

- `src/RufusMapEditor.App/App.xaml`
- `src/RufusMapEditor.App/App.xaml.cs`
- `src/RufusMapEditor.App/MainWindow.xaml`
- `src/RufusMapEditor.App/MainWindow.xaml.cs`
- `src/RufusMapEditor.App/Themes/ColorsDark.xaml`
- `src/RufusMapEditor.App/Themes/ColorsLight.xaml`
- `src/RufusMapEditor.App/Themes/Controls.xaml`
- `src/RufusMapEditor.App/Themes/ToolIcons.xaml`
- `src/RufusMapEditor.App/Services/ThemeService.cs`
- `src/RufusMapEditor.App/Services/ThemePreference.cs`
- `src/RufusMapEditor.App/RufusMapEditor.App.csproj`
- `src/RufusMapEditor.Admin/App.xaml` / `App.xaml.cs`
- `src/RufusMapEditor.Admin/RufusMapEditor.Admin.csproj`
- `src/RufusMapEditor.Admin/MainWindow.xaml.cs` (host de Maps)
- `RufusMapEditor.slnx`
- `scripts/build-portable-dist.ps1`
- `scripts/build-admin-dist.ps1`
- `scripts/publish-release-test.ps1`

Evita refactors globales fuera de Maps mientras Ruben trabaja en Content/NPC, misiones, IA, Licensing, ADMIN shell, backend, etc.

## Setup en el PC del colaborador

```bat
git clone https://github.com/WaynoxStudio/RUFUS-EDITOR.git
cd RUFUS-EDITOR
git fetch origin
git switch feature/maps-ui
```

### Compilar / ejecutar Maps (USER)

```bat
dotnet build RufusMapEditor.slnx
dotnet run --project src/RufusMapEditor.App
```

### Build ADMIN (Maps compartido)

```bat
dotnet build src/RufusMapEditor.Admin/RufusMapEditor.Admin.csproj
dotnet run --project src/RufusMapEditor.Admin
```

Opcional dist ADMIN:

```bat
powershell -File scripts/build-admin-dist.ps1
```

### Tests antes de abrir PR

No hay suite dedicada solo a UI Maps. Como mínimo:

```bat
dotnet build RufusMapEditor.slnx
dotnet test tests/RufusMapEditor.LegacyCompatibility.Tests
```

Si tocaste algo que afecta ADMIN, también compila ADMIN (comandos de arriba).

## Sincronización con main

Antes de una nueva tanda de trabajo:

```bat
git fetch origin
git switch feature/maps-ui
git merge origin/main
```

Preferimos **merge explícito** (no rebase forzado sobre una rama compartida).
Si hay conflictos: resuélvelos antes de seguir desarrollando.

Después de que un PR Maps entre en `main`, vuelve a sincronizar con los mismos comandos.

## Commits pequeños

Ejemplos:

- `MAPS: reorganiza inspector lateral`
- `MAPS: mejora catálogo GFX`
- `MAPS: corrige layout de ventanas flotantes`
- `MAPS: simplifica herramientas de celdas`
- `SHARED: ajusta recurso visual usado por Maps`

Evita un único commit enorme con semanas de cambios.

## Pull Requests

Flujo:

1. Trabajar en `feature/maps-ui`
2. `git push origin feature/maps-ui`
3. Abrir Pull Request → `main`
4. Revisión / tests / merge

**No hagas push directo a `main`** durante esta colaboración (regla de trabajo).

Preferir PRs por bloques, por ejemplo:

1. Layout general Maps  
2. Inspector / herramientas  
3. Catálogo GFX  
4. Ventanas del mapa  
5. Correcciones UX  

No hace falta terminar todo Maps para integrar.

## Library, dist y configuración local

| Ruta | Tratamiento |
|------|-------------|
| `Library/Maps`, `Visuals`, `XML`, `Flasm`, `Images`, … | Assets reales versionados (no borrar del repo) |
| `Library/cache/` | Caché PNG regenerable — **ignorada por Git** |
| `dist/`, `dist-admin/` | Outputs de build — no editar ni commitear |
| `bin/`, `obj/`, `artifacts/` | Ignorados |

Los clips / previews locales pueden necesitar configuración aparte (`ClipsRootPath`, Library en disco).
No subas cientos de MB de assets regenerables salvo decisión explícita.

**No commitees** configuración privada:

- BD / SFTP / passwords
- `ClipsRootPath` personal
- SessionToken, secretos API, `rufus-ai.env`
- SQLite de licencias

Cada persona configura su entorno local fuera de Git.

## Resumen

- Un solo repo, un solo `MapsEditorView`, compartido USER + ADMIN.
- Colaborador: rama `feature/maps-ui` → PR a `main`.
- Ruben: sigue en su trabajo habitual en `main` (u otras ramas).
- Código fuente en `src/`; nunca editar `dist/` / `dist-admin/` a mano.
