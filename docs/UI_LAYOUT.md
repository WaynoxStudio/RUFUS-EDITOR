# RUFUS Map Editor — UI Layout (Fase 9UI.2 + 9UI.3)

## Menú Archivo (mapa)

- **Guardar** (Ctrl+S) / **Guardar como…** — solo `.rufmap`
- **Exportar → SWF…** — SWF Astria/AME suelto
- **Exportar → Paquete de mapa…** — carpeta `<MapId>\` (PNG, ModeCell, Gfx list, manifest, Legacy SWF opcional). Ver `docs/MAP_PACKAGE.md`. No cambia Ctrl+S ni autosave.

## Estructura MainWindow

Grid principal 3 filas: workspace (`*`), splitter, catálogo (`220px` default).

Workspace 5 columnas: Mapas | splitter | TabControl MAPA/MUNDO | splitter | Inspector.

Catálogo 4 columnas: Categorías (200) | splitter | GFX grid | Pincel (155).

## Barra vertical MAPA (9UI.3)

Grupos: **HERRAM.** · **GFX (C1/C2)** · **CELDA** · **COMB. (E1/E2)**

Ocultable con **Ver → Paneles → Barra de herramientas** (comparte flag con toolbar horizontal).

## Toolbar horizontal MAPA

Undo/Redo · Flip/Rot · Aplicar/Reemplazar · Visibilidad capas · Rejilla/Cell ID/Límite.

## Ver → Paneles

Checkboxes: Mapas, Inspector, Catálogo, Categorías, Pincel, Barra herramientas, Barra estado.

Toggle vía commands; persistencia en `settings.json` → `UiLayout`.

## Restaurar diseño predeterminado

**Ver → Restaurar diseño predeterminado** — restaura anchos default, muestra paneles, expande catálogo.

## Persistencia layout (`UiLayoutSettings`)

| Propiedad | Default |
|---|---|
| `LeftPanelWidth` | 160 |
| `RightPanelWidth` | 280 |
| `CatalogHeight` | 220 |
| `CatalogCollapsed` | false |
| `Show*Panel` | true |

Guardado en `%LocalAppData%\RufusMapEditor\settings.json`. Valores clampeados al cargar.

## Catálogo colapsable

Botón **▾/▴** en header Categorías + tooltip Ocultar/Mostrar catálogo. Alternativa: Ver → Paneles → Catálogo.

## Capas y catálogo

`PaintLayer` controla namespace del catálogo:

- SUELO → Ground → raíz TreeView "Suelos"
- CAPA 1/2 → Object → raíz "Objetos"

Cambio Ground↔Object limpia Gfx seleccionado. Object1↔Object2 conserva selección si mismo namespace.
