# RUFUS Map Editor — UI Theme (Fase 9UI)

## Temas

Tres modos en **Ver → Apariencia**:

| Modo | Comportamiento |
|---|---|
| **Sistema** | Lee `HKCU\...\Themes\Personalize\AppsUseLightTheme` (Windows 10+) |
| **Claro** | Paleta `Themes/ColorsLight.xaml` |
| **Oscuro** | Paleta `Themes/ColorsDark.xaml` |

Persistencia: `%LocalAppData%\RufusMapEditor\settings.json` (`Theme`, `UiScale`). No incluido en ZIP portable.

Cambio en caliente: `ThemeService.SetPreference` / `SetUiScale` reemplaza merged dictionaries sin reiniciar.

## Recursos semánticos

Definidos en `ColorsDark.xaml` / `ColorsLight.xaml`:

`WindowBackground`, `CanvasBackground`, `PanelBackground`, `SurfaceBackground`, `ElevatedSurface`, `Border`, `Divider`, `TextPrimary`, `TextSecondary`, `TextDisabled`, `Accent*`, `Selection*`, `HoverBackground`, `Input*`, `Warning`, `Error`, `Success`, `AccentGround/Layer1/Layer2`, overlays `OverlayGrid`, `OverlayCellId`, `OverlayPaintTargetFill`, `OverlayPaintTargetStroke`.

Estilos compartidos: `Themes/Controls.xaml`.

### Contraste disabled (9UI.1)

`TextDisabled` debe ser legible sobre `SurfaceBackground` / menús:

| Tema | TextSecondary | TextMuted | TextDisabled |
|---|---|---|---|
| Claro | `#555555` | `#777777` | `#9A9A9A` |
| Oscuro | `#AAAAAA` | `#888888` | `#666666` |

### Migración hardcoded (9UI.2)

MainWindow, ventanas secundarias y MUNDO migrados a `DynamicResource`. Overlays de mapa (MapViewport) mantienen colores funcionales.

## Visibilidad (solo UI)

Panel en toolbar MAPA + menú Ver. Opciones:

- Fondo, Suelo, Capa 1, Capa 2 → `MapRenderOptions` (no altera MapData)
- Rejilla, Cell ID → overlays en `MapViewport`

**Solo capa** / **Restaurar visibilidad** — snapshot en memoria de sesión.

Persistencia global en `AppSettings.MapViewVisibility`.

Cell ID se oculta automáticamente si zoom &lt; 35% (`ShowCellIdsEffective`) aunque el toggle siga activo.

## Selector de fondo

`BackgroundPickerWindow` — galería de 48 backgrounds del catálogo portable. `BackgroundId = 0` = sin fondo (confirmado en `MapDocument`).

Undo: `MapMetadataEditCommand`.

## Música

**NO implementado** selector visual — `Musiques` excluido de portable 9P.3. DATO PENDIENTE Fase posterior.

## Renderer

El tema **no** modifica pixels del mapa. Golden tests deben permanecer idénticos.

## Escala UI

90% / 100% / 110% / 125% vía `FontSize*` resources × `UiScale`. No afecta zoom del mapa ni render export.

## Limitaciones

- `MessageBox` nativo de Windows no sigue tema oscuro del app.
- Algunos colores de acento de capa en toolbar usan recursos semánticos `AccentGround` etc.
