# Publicación en base de datos (FASE 10A UPDATE / 10B INSERT)

## Guardar ≠ Publicar

| Acción | Qué hace | Qué no hace |
|--------|----------|-------------|
| **Guardar** (Ctrl+S / Official Save) | Escribe `Library\Maps\<MapId>\` (`.rufmap`, `.png`, `_MapData.txt`, `_AME.swf` opcional) | No toca MySQL |
| **Publicar en BD** | `UPDATE` de una fila existente o `INSERT` confirmado de un mapa nuevo | No genera SWF cliente ni altera schema |

La publicación exige un Official Save previo. Si el guardado falla, no se publica.

## Configuración

- Host, Puerto, Usuario, Contraseña, Base (`estaticos`), Tabla (`mapas`)
- Valores opcionales para mapas nuevos: solo se usan si una columna secundaria es `NOT NULL` y no tiene `DEFAULT`
- Contraseña: DPAPI (`PasswordProtectedBase64`), nunca en logs ni reportes
- Probar conexión: `SELECT 1` de solo lectura

## Mapeo editor → columnas

| Documento | Columna BD |
|-----------|------------|
| MapId | id |
| DateMap (Revisión) | fecha |
| Width | ancho |
| Height | alto |
| BackgroundId | bgID |
| MusicId | musicID |
| AmbianceId | ambienteID |
| Outdoor | outDoor |
| Capabilities | capabilities |
| FightPlaces | posPelea |
| MapData (canónico en memoria) | mapData |
| WorldX | X |
| WorldY | Y |

## Revisión (`fecha`)

- UI: campo **Revisión** (no “Fecha”)
- Si `fecha` es numérica: nueva = actual + 1
- Si es texto (p.ej. `VULKANIA`): no se auto-incrementa; se pide revisión manual válida
- En CREATE: revisión inicial exacta `0`; no se incrementa durante la creación

## X / Y

Persistidos en `.rufmap` (`worldX` / `worldY`) junto con `worldCoordinatesSet`.
Aceptan negativos (ej. Map 30010 → X=-47, Y=33). CREATE exige que X/Y se hayan definido explícitamente.

## Campos preservados (no entran en UPDATE)

`key`, `mobs`, `subArea`, `maxGrupoMobs`, `maxMobsPorGrupo`, `minNivelGrupoMob`, `maxNivelGrupoMob`, `maxMercantes`, `maxPeleas`, `minMobsPorGrupo`

## Seguridad

- UPDATE: `UPDATE … WHERE id = @id`
- CREATE: `BEGIN → SELECT exists → INSERT → affected_rows==1 → SELECT verify → COMMIT`; cualquier fallo hace `ROLLBACK`
- Consultas y valores parametrizados; identificadores validados y delimitados
- Si `affected_rows > 1`: ROLLBACK + error crítico
- Nunca DROP / ALTER / TRUNCATE / DELETE / REPLACE
- Backup JSON local de la fila previa (sin password) bajo `%LocalAppData%\RufusMapEditor\db-backups\`
- CREATE guarda antes un JSON de intención con MapData completo y escribe `database-publish.log` (timestamp, MapId, CREATE/UPDATE, resultado)
- Tests: solo `InMemoryMapasRepository` / mocks — nunca BD de producción

## Creación segura (10B)

Antes de ofrecer **Crear mapa**, la aplicación lee `INFORMATION_SCHEMA.COLUMNS` en modo lectura:
`COLUMN_NAME`, `DATA_TYPE`, `COLUMN_TYPE`, `IS_NULLABLE`, `COLUMN_DEFAULT`,
`CHARACTER_MAXIMUM_LENGTH`, `NUMERIC_PRECISION`, `NUMERIC_SCALE`, `COLUMN_KEY`,
`EXTRA` y `ORDINAL_POSITION`.

Solo se escriben desde el editor:
`id`, `fecha`, `ancho`, `alto`, `bgID`, `musicID`, `ambienteID`, `outDoor`,
`capabilities`, `posPelea`, `mapData`, `X`, `Y`.

Para cada campo preservado (`key`, `mobs`, `subArea`, `maxGrupoMobs`,
`maxMobsPorGrupo`, `minNivelGrupoMob`, `maxNivelGrupoMob`, `maxMercantes`,
`maxPeleas`, `minMobsPorGrupo`) se aplica esta política, sin inventar valores:

1. omitir la columna si MySQL declara `DEFAULT`;
2. enviar `NULL` si es nullable;
3. exigir un valor en **Configuración BD → Valores para mapas nuevos**.

El flujo es Official Save → introspección/plan → confirmación CREATE → transacción INSERT
→ revisión local `0` → segundo Official Save. Si el primer guardado falla, no se ejecuta INSERT.

## Fases

- **10A:** UPDATE de mapas existentes, con revisión numérica incremental
- **10B:** INSERT seguro de mapas nuevos, schema-driven y con confirmación explícita
