# LANG maps_es y coordenadas de mundo

## Tablas vs SWF

| Dónde | Qué guarda |
|-------|------------|
| BD `mapas` (X, Y) | Coordenadas de juego / mundo del editor |
| BD `mapas.subArea` | A menudo `0` en datos RUFUS; **no** es el `sa` del cliente |
| `maps_es_N.swf` → `MA.m[id]` | `x`, `y`, **`sa`**, **`ep`** para el cliente Flash |

Publicar coords desde Admin (“Subir coords”) actualiza **BD X/Y**. El LANG (`maps_es`) se publica por el pipeline SFTP aparte.

## Dual-layer `sa` / `ep` (histórico Retro)

En `maps_es` clásico, muchos mapas (p. ej. Vulkania) tenían **dos** bloques `MA.m[id]` con las **mismas** coords:

- `sa=30`, `ep=2`
- `sa=612`, `ep=18`

El mutator antiguo **colapsaba** a una sola entrada al actualizar. Eso puede desalinear minimapa vs mapa grande si cada UI filtra por distinto `ep`.

### Código (FASE batch)

- `LangMapsMutator.ReplaceVariants` — borra todas las entradas de un MapId e inserta N variantes `(sa,ep)` en el mismo `(x,y)`.
- `LangMapsSwfService.GenerateBatch` — permite varias filas del mismo MapId si coords coinciden y `(sa,ep)` difieren; opcional `RemoveMapIds`.
- `LangRemotePublishService.PublishBatch` — mismo bump `N → N+1` + `versions_es` `maps,es`.

Validación: cada variante pedida debe existir; no se permiten coords divergentes entre variantes del mismo id.

### Fantasmas

Entradas `MA.m[id]` en coords de una isla **sin** fila correspondiente en BD (ej. histórico `30012` sobre celda de otro mapa) distorsionan la silueta. Quitarlas con `RemoveMapIds` en el mismo publish.

## Convención Y (Dofus)

Y más negativo = más al **norte**. En Vulkania, fila `Y=-38` es el borde norte de la isla (p. ej. `30017` punta NO).

## Cliente

Tras publicar `maps_es_N+1` y bumpear `versions_es`, el cliente debe descargar la nueva versión (limpiar cache lang si sigue viendo datos viejos).

## Vulkania (caso 2026-10)

- BD e isla sincronizadas a bbox aprox. `X[-44..-38]`, `Y[-38..-33]`.
- Publish `1288 → 1289`: dual-layer en 40 mapas Vulkania + eliminación de fantasma `30012`.
- Remoto: `maps,es,1289` (comprobar versión activa en `versions_es.txt` del servidor).
