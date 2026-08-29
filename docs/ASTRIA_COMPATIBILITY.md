# ASTRIA Compatibility Report — RUFUS Map Editor

**Fecha:** 2026-08-21  
**Instalación de referencia (solo lectura):** `C:\Users\rubez\Desktop\RUFUS\Astria Map Editor 1`  
**Código público analizado:** [quentinrozados/AstriaMapEditor](https://github.com/quentinrozados/AstriaMapEditor) (clon local en `_refs/AstriaMapEditor`)  
**Regla:** la instalación local es la referencia de compatibilidad. El código público se usa como ayuda técnica; no se asume identidad binaria.

---

## 1. Resumen ejecutivo

La instalación local es **Astria Map Editor v2.0.0.0** (“Version privée” / Zano), WinForms + VB.NET, x86.

| Elemento | Hallazgo |
|---|---|
| Ejecutable | `Astria Map Editor.exe` — **2.0.0.0**, SHA256 `FFD62297984F92495C3C763EDD45B08C08FA2BEA7D9CAC0F29BEEDD4EFA9917D` |
| Mapas disponibles | **30** carpetas con `.sql` / `.ame` / `.swf` / PNG |
| MapData | 10 caracteres/celda; mapa 10420 = **479 celdas / 4790 chars** |
| Codec | Aislado en `RufusMapEditor.LegacyCompatibility` — **roundtrip idéntico** en los 30 mapas fixture |
| `.ame` | BinaryFormatter / NRBF (`00 01 00 00 00…`) — **no** usar BinaryFormatter en .NET moderno |
| SQL RUFUS | **DATO PENDIENTE DE CONFIRMAR** (no escribir en BD todavía) |

---

## 2. Estructura de la instalación local

```
Astria Map Editor 1/
├── Astria Map Editor.exe
├── compil.bat
├── config                          # BinaryFormatter List<MyOptions>
├── ComponentFactory.Krypton.Toolkit.dll
├── log4net.dll
├── MySql.Data.dll
├── SharpZipLib.dll
├── SwfDotNet.IO.dll
├── Flasm/
│   ├── blank.swf
│   ├── flasm.exe
│   └── flasm.ini
├── Géopositions/                   # nombre con acento en disco
│   └── None/
│       ├── None.geo
│       ├── None.png
│       └── None_Mode.png
├── Images/
│   ├── backgrounds/                # ~48 PNG (GfxID.png)
│   ├── grounds/<categoría>/        # ~549 PNG
│   └── objects/<categoría>/        # ~5151 PNG
├── Maps/
│   ├── <MapId>/                    # 30 mapas
│   └── AutoSave/                   # vacío en esta instalación
├── Musiques/                       # ~37 MP3 (id-nombre.mp3)
└── XML/
    ├── areas.xml
    ├── grounds.xml                 # ArrayOfPos (ID, X, Y) anchors
    ├── monsters.xml
    ├── objects.xml
    └── subareas.xml
```

### 2.1 Inventario de formatos

| Extensión | Cantidad (aprox.) | Uso |
|---|---|---|
| `.png` | 5809 | tiles + screenshots de mapas |
| `.swf` | 59 | mapas exportados + `Flasm/blank.swf` |
| `.ame` | 31 | proyecto serializado BinaryFormatter |
| `.sql` | 30 | script DELETE+INSERT generado por Astria |
| `.txt` | 31 | listas “GfxID utilisés” |
| `.xml` | 5 | anchors / áreas / monstruos |
| `.geo` | 1 | geoposición BinaryFormatter |
| `.mp3` | 36–37 | músicas |
| `.exe` / `.dll` | 2 + 5 | runtime Astria |

### 2.2 Contenido típico de `Maps/<id>/`

Ejemplo **10420**:

| Archivo | Rol |
|---|---|
| `10420.sql` | SQL export (tabla `maps`, columna tipografiada `heigth`) |
| `10420.swf` / `10420_AME.swf` | SWF cliente / variante AME |
| `10420_AME.ame` | documento editable serializado |
| `10420.png` / `10420_ModeCell.png` | preview / modo celdas |
| `GfxID utilisés.txt` | lista de GfxID (una línea por ID) |

Patrones de nombre `.ame` observados: `*_AME.ame` (30) y un caso `10111_.ame`.

---

## 3. Arquitectura del Astria original (público + local)

### 3.1 Stack

- **Lenguaje:** Visual Basic.NET (WinForms)
- **UI:** ComponentFactory Krypton Toolkit 4.4
- **SWF I/O:** SwfDotNet.IO 0.9 + Flasm externo
- **ZIP:** SharpZipLib
- **MySQL:** MySql.Data 6.2.2
- **Config / `.ame` / `.geo`:** `BinaryFormatter`

### 3.2 Módulos relevantes (código público)

| Área | Archivos |
|---|---|
| Codec MapData | `Maps/MapData/Builder.vb`, `Decryptage.vb`, `Patterns Dofus/Map.vb` |
| Celda / dibujo | `MapEditor/Cell.vb`, `MapEditor/Tile.vb`, `MapEditor/MapEditor.vb` |
| Fight places | `Maps/FightCell/FightCellsManager.vb` |
| SWF export | `SWF/Flasm.vb` |
| SWF import | `SWF/UnPacker.vb` (índices mágicos sobre disassembly) |
| SQL | `MySQL.vb` |
| Geoposición | `Geoposition/Geoposition.vb`, `CellGeo.vb` |
| Opciones | `Options/MyOptions.vb`, archivo `config` |
| Resources XML | `XML/XMLLoader.vb` |

### 3.3 Relación código público ↔ binario local

- Ambos anuncian **AssemblyVersion 2.0.0.0** y descripción “Version privée”.
- **No se ha demostrado** (aún) que el SHA del exe coincida con el release del repo.
- Política RUFUS: **comportamiento observado en la instalación local + fixtures locales** > asunción de paridad con GitHub.

---

## 4. MapData (crítico)

### 4.1 Geometría de celdas

Astria declara:

```vb
Cells(Height * (Width * 2 - 1) - Width)
```

En VB.NET eso crea índices `0..N` → longitud **N+1**.

```
CellCount = Height * (Width * 2 - 1) - Width + 1
```

Para 15×17 → **479** celdas → MapData **4790** caracteres. Confirmado en mapa 10420 y en los 30 `.sql` fixture.

### 4.2 Alfabeto

```
abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_
```

(64 símbolos; idéntico a `Builder.ZKARRAY` / hash de fight cells.)

### 4.3 Codificación por celda (10 valores 0..63)

Portado fielmente desde `Builder.GetCellData` / `Map.UncompressCell`:

| Índice | Contenido |
|---|---|
| 0 | Active(0x20) \| LoS(1) \| bits altos Gfx1/Gfx2/Gfx3 |
| 1 | Rotación suelo (2b) \| nivel suelo (4b) |
| 2 | Movement (3b) \| bits medios Gfx1 |
| 3 | Gfx1 bajo (6b) |
| 4 | Incline (4b) \| flip suelo \| bit alto Gfx2 |
| 5–6 | Gfx2 |
| 7 | Rotación obj1 \| flips \| IO \| bit alto Gfx3 |
| 8–9 | Gfx3 |

**MovementEnum (Astria):**

| Valor | Nombre |
|---|---|
| 0 | UNWALKABLE |
| 1 | DOOR |
| 2 | TRIGGER |
| 4 | WALKABLE |
| 5 | PADDOCK |
| 7 | PATH |

### 4.4 Cifrado opcional

`Decryptage.vb`: PrepareKey (hex→string + unescape), Checksum, DecypherData (XOR).  
Los 30 mapas fixture tienen `key` vacío → MapData en claro.  
Soporte de roundtrip cifrado: **implementación deferred** hasta tener fixtures cifrados reales.

### 4.5 Implementación RUFUS (Fase 1)

- Biblioteca: `src/RufusMapEditor.LegacyCompatibility/MapData/MapDataCodec.cs`
- Modelo: `src/RufusMapEditor.Domain/Maps/CellData.cs`
- Tests: roundtrip 10420 + **todos** los `.sql` en `tests/fixtures/maps/`

**Hito:** `MapData original → Decode → Encode → MapData idéntico` — ver sección 14.

---

## 5. Peculiaridades / bugs Astria a documentar

### 5.1 `Cell.Type` — Trigger vs TriggerCell

- **Archivo:** `MapEditor/Cell.vb`
- **Comportamiento:** al *set* de `MovementEnum.TRIGGER` se escribe `TriggerCell = True`; el *get* consulta `Trigger` (otro campo).
- **Riesgo:** un decode→encode pasando por `Cell.Type()` **puede corromper** celdas TRIGGER (se reescriben como WALKABLE=4).
- **Política RUFUS:** el codec guarda `Movement` como entero/enum crudo; **no** usa la API `Type()` de Astria.
- **Test:** roundtrip MapData (ya cubre conservación del valor 2).

### 5.2 Active flag forzado

- **Archivo:** `Builder.vb` — `If 1 Then numArray(0) = &H20`
- **Comportamiento:** siempre marca celda “activa”.
- **Riesgo:** encode Astria puede diferir de MapData raros sin bit 0x20.
- **RUFUS:** preserva el bit leído (fixtures actuales lo tienen a 1).

### 5.3 SQL dungeon / mobs tablas intercambiadas

- **Archivo:** `MySQL.vb` — `Get_SqlDungeon` / `Get_SqlGroupMobsFix`
- **Comportamiento:** DELETE/INSERT usan tablas `endfight_action` y `mobgroups_fix` de forma cruzada.
- **Riesgo:** corrupción de datos si se ejecuta SQL heredado tal cual.
- **Política:** no copiar SQL a RUFUS hasta validar esquema.

### 5.4 Tipografía columna `heigth`

- Config y SQL export usan `heigth` (sin ‘g’). Es el nombre de columna **configurado** en Astria para Ancestra-like DBs, no necesariamente el de RUFUS.

### 5.5 Import SWF por índices mágicos

- **Archivo:** `UnPacker.vb` — `Split(..., "push")(8)`, `(10)`, … y `Split(..., "'")(&H1D)` para MapData.
- **Riesgo:** frágil ante cambios de plantilla Flasm / versión SWF.
- **Plan:** parser semántico (constantes / setVariable) con tests (Fase 9).

### 5.6 Flasm + `Thread.Sleep`

- **Archivo:** `MapEditor.vb` (export) — sleeps fijos esperando `blank.$wf`.
- **RUFUS:** `Process` + wait real + timeout + captura stdout/stderr/exit code; carpeta temporal.

### 5.7 Búsqueda de tiles O(n) + arrays de 50000

- `Tile.Get_*` recorre listas; `Pos_Grounds(50000)`, `Monsters(50000)`, etc.
- **RUFUS:** `Dictionary<int, TileDefinition>` + indexación única al arranque.

---

## 6. Assets gráficos (resumen)

Ver detalle completo y cifras medidas en **§ Catálogo de recursos gráficos** (Fase 2).

| Carpeta | Naming | Extensiones aceptadas por Astria |
|---|---|---|
| `Images/backgrounds` | `{GfxID}.ext` (plano) | `.png` `.jpg` `.jpeg` `.bmp` |
| `Images/grounds/<carpeta>` | `{GfxID}.ext` | idem |
| `Images/objects/<carpeta>` | `{GfxID}.ext` | idem |

`XML/grounds.xml` / `objects.xml`: `ArrayOfPos` → `Pos/ID,X,Y` (anchors). Backgrounds **no** tienen XML propio.

---

## Catálogo de recursos gráficos

### Funcionamiento original de Astria

| Pieza | Ubicación | Comportamiento |
|---|---|---|
| Carga imágenes | `Main.vb` → `LoadImages_DoWork` / `SearchGrounds` / `SearchObjects` | Recorre carpetas; GfxID = parte del nombre antes del primer `.`; indexa en arrays `List_*(100000)` **por índice = ID** |
| Lookup | `Tile.Get_Ground/Object/Background` | Búsqueda **lineal** `For Each` sobre el array (incluye huecos) |
| UI selector | `ListView` usa `List_Grounds(SelectedItems(0).Text)` | Acceso directo por índice = ID (O(1) en ese camino) |
| Anchors | `XMLLoader.LoadAllXML` + `Tile.Pos` | `XmlSerializer` de `Tile.Pos()`; arrays redimensionados a 50000 |
| Anchor missing | `Cell.Draw_Tile` | Si `Get_*_Pos(id).ID = 0` (struct default), sintetiza centro de imagen en runtime |
| Background draw | `MapEditor.vb` | Usa **`Get_Ground_Pos(Background.ID)`** (no hay XML de backgrounds) |
| Categoría UI | nombre de carpeta padre inmediata | `Tile.Folder`; backgrounds → `""` |

**Necesario para compatibilidad:** mismos directorios, mismo parseo de GfxID, namespaces separados Background/Ground/Object, anchors X/Y (incl. negativos), no inventar metadatos.

**Limitación del editor antiguo (no copiar):** arrays de 100000/50000, búsqueda lineal `Get_*`, sobrescritura silenciosa de duplicados, Try/Catch inútil para detectar dobles, carga de bitmaps acoplada al modelo.

### Resultados del escaneo (instalación local, solo lectura)

Medidos con `AstriaGfxCatalogBuilder.Build` sobre  
`C:\Users\rubez\Desktop\RUFUS\Astria Map Editor 1` (no hardcodeados en el producto; descubiertos al indexar):

| Métrica | Valor |
|---|---|
| Backgrounds (IDs únicos) | **48** (47 `.png` + 1 `.bmp`) |
| Grounds (IDs únicos) | **549** (todos `.png`) |
| Objects (IDs únicos) | **4952** (5151 archivos `.png` − duplicados) |
| Archivos object con ID duplicado | **199** colisiones (p. ej. `Murs` vs `Murs 2`) |
| Total recursos indexados | **5549** |
| Anchors grounds.xml (únicos) | **1005** |
| Anchors objects.xml (únicos) | **6529** (6551 entradas brutas; **22** IDs duplicados en XML) |
| XML sin imagen (huérfanos) | **2033** (456 grounds + 1577 objects) |
| Imágenes sin anchor | **0** |
| Nombres de fichero no numéricos | **0** |
| Errores de indexación | **0** |
| Tiempo escaneo imágenes | ~33 ms (máquina de desarrollo) |
| Tiempo parseo XML | ~24 ms |
| Tiempo total catálogo | ~61 ms |

**Solapamiento numérico entre categorías (legítimo):** Background∩Ground=1, Background∩Object=48, Ground∩Object=545. Los namespaces **no** se mezclan.

**Carpetas grounds observadas:** Autre, Bois, Cendres, Eau, Fibres, Gros, Herbes, Lave, Nowel, Pierre, Sable, Terre.

**Carpetas objects observadas:** Arbres, Autre, Autre 2–4, Buissons, Dalles, Eau, Enclos, HDV, Interactifs, Lumières, Maison, Mobilier, Murs, Murs 2, Nowel, Pandala, Passages, Pirates, PNJ, Pratique, Prisons,cimetières, Rochers, Rochers 2, Sable, Statues, Transports, Végétation.

**Anchors negativos** (válidos): presentes en XML (grounds y objects). En recursos con imagen: 9 grounds / 104 objects con X&lt;0 o Y&lt;0.

### Formato XML de anchors

```xml
<ArrayOfPos>
  <Pos>
    <ID>481</ID>
    <X>27</X>
    <Y>14</Y>
  </Pos>
  …
</ArrayOfPos>
```

- Ficheros locales **rellenos con bytes `0x00`** hasta un tamaño fijo (grounds ≈ 128 KiB, objects ≈ 512 KiB). Astria los carga vía `ReadAllText`+`XmlDocument`; RUFUS **trunca en el primer null** antes de parsear.
- No se fuerza `(0,0)` cuando falta el anchor: `HasAnchor = false`. El centro de imagen queda para el renderer (como Astria en draw-time).

### Decisiones arquitectónicas RUFUS

| Tema | Decisión |
|---|---|
| API | `IGfxCatalog` + `TryGetBackground/Ground/Object` / `TryGet(category, id)` |
| Estructura | Tres `Dictionary<int, GfxResource>` (namespaces separados) |
| Memoria | Solo metadatos + rutas; `IGfxImageProvider` carga bytes bajo demanda |
| Duplicados misma categoría | Warning; **última ruta en orden lexicográfico** gana (equivalente a sobrescritura Astria, pero determinista) |
| Favoritos / recientes | No implementados; `Enumerate` / `EnumerateById` preparados para búsqueda futura |
| Código | `Domain/Gfx/*` + `LegacyCompatibility/Gfx/*` |

### Posible bug / peculiaridad Astria

**Archivo:** `Main.vb` (`SearchGrounds` / `SearchObjects`)  
**Método:** asignación `MyList_Objects(id) = New Tile(...)` dentro de `Try/Catch`  
**Comportamiento:** un GfxID repetido en dos carpetas **sobrescribe** la entrada anterior; el `Catch` (“présent deux fois”) **no se dispara** (asignar al mismo índice no lanza).  
**Impacto:** imagen efectiva indeterminada según orden de `GetDirectories`.  
**Decisión RUFUS:** detectar `DuplicateGfxId` (Warning) y fijar ganador determinista.  
**Test:** `Duplicate_gfx_ids_are_detected_and_last_path_wins`

### Posible bug / peculiaridad Astria

**Archivo:** `MapEditor.vb` (dibujo de background)  
**Método:** uso de `Tile.Get_Ground_Pos(Background.ID)`  
**Comportamiento:** el pivot del fondo se busca en anchors de **grounds**.  
**Impacto:** background sin entrada homónima en `grounds.xml` → Pos default (ID=0) / comportamiento frágil.  
**Decisión RUFUS:** backgrounds sin XML propio; no inventar anchors; documentar dependencia si el renderer la necesita.  
**Test:** catálogo no asigna `Anchor` a Background.

### Posible bug / peculiaridad Astria

**Archivo:** `XML/*.xml` en la instalación  
**Método:** `XMLLoader.LoadXML`  
**Comportamiento:** ficheros con **padding de null bytes**.  
**Impacto:** parsers XML estrictos modernos fallan si no se truncan.  
**Decisión RUFUS:** `GfxAnchorXmlParser` elimina padding (`XmlNullPaddingStripped` Info).  
**Test:** `Xml_null_padding_is_stripped_like_astria_shipped_files`

### Posible bug / peculiaridad Astria

**Archivo:** `Tile.vb` / `Cell.vb`  
**Método:** `Get_*_Pos` + fallback en `Draw_Tile`  
**Comportamiento:** `Pos` es `Structure`; “no encontrado” ≡ `ID=0,X=0,Y=0`, luego se sustituye por centro de imagen.  
**Impacto:** confundir “sin anchor” con anchor real `(0,0)` si existiera ID 0.  
**Decisión RUFUS:** `GfxAnchor?` / `HasAnchor`; no silenciar faltantes como `(0,0)`.  
**Test:** fixtures + install (`ImagesWithoutAnchor=0` en esta librería)

---

## Renderer isométrico

### Fórmulas Astria (fuente: `MapEditor.vb` / `Cell.vb`)

| Concepto | Fórmula / comportamiento |
|---|---|
| `SizeBaseCell` | **26** px (media celda) |
| Canvas completo | `(Width * SizeCell * 2, Height * SizeCell)` → 15×17 @26 = **780×442** |
| Crop export (`RogneImage`) | origen `(SizeCell, SizeCell/2)=(26,13)`; tamaño `(fullW-2·SizeCell, fullH-SizeCell)` → **728×416** |
| Escala tiles | `PourceOfTile = SizeCell / SizeBaseCell` (=1 en export) |
| Orden capas | Clear negro → Background → **todas** Gfx1 → **todas** Gfx2 → **todas** Gfx3 → (overlays editor) |
| Posición tile | `(Location[3].X + SizeCell - PosX, Location[2].Y - SizeCell/2 - PosY)` |
| Anchor | XML `grounds.xml`/`objects.xml`; si falta → centro imagen en draw-time |
| Flip | `RotateNoneFlipX`; ajuste PosX **solo** para Object |
| Rotación Gfx3 | siempre 0 en `Draw_Gfx3` |
| InclineSol / NivSol | **no afectan** el dibujo en Astria |
| Logo | tras crop: `logo_map` en `(W-logoW-5, H-logoH-5)` |

### Grid de celdas (`GenerateGrid`)

Dos pasadas (filas “pares” / “impares”) con IDs:

- Par: `id = i + n*(Width*2) - n`, `i=0..Width`, `n=0..Height-1`
- Impar: `id = i + n*(Width*2) + Width - n`, `i=0..Width-2`, `n=0..Height-2`

### Background

- SWF/AME: `backgroundNum` (SQL **no** lo incluye).
- Dibujo: `Get_Ground_Pos(Background.ID)` — ancla en **grounds.xml** aunque no exista imagen ground.
- Destino: rectángulo del **canvas completo** (estira el PNG de fondo).
- Ejemplo 10420: `backgroundNum=284`, anchor ground-pos `(2,2)`, archivo `Images/backgrounds/284.png`.

### Política duplicados (confirmada)

| Recurso | Método Astria | Política |
|---|---|---|
| Imágenes mismo GfxID | `List_Objects(id)=…` | **última escritura** gana (orden FS) |
| Anchors XML mismo ID | `Get_Object_Pos` / `Get_Ground_Pos` | **primera coincidencia** en el array (orden documento) |

IDs con anchors conflictivos (535, 536, 697, 698, 874, 3811, 6559, 6567): no usados en los 30 fixtures; RUFUS conserva la primera entrada y marca `AnchorAmbiguous`.

### Comparación vs golden PNG

Artefactos en `tests/artifacts/render/` (nunca dentro de Astria).

**Mapa 10420 (referencia):**

| Métrica | Valor |
|---|---|
| Dimensiones | 728×416 (idénticas) |
| Missing GFX / anchors | 0 / 0 |
| Píxeles distintos | ~46839 (~15.5%) |
| Diferencia media abs. | **~0.059** |
| Diferencia máxima canal | **7** |

#### Causa de la diferencia residual (no estructural)

Tras corregir el ancla de background, las diferencias restantes son **sub-umbral visual**:

- Golden generado con **.NET Framework 4.x + GDI+** de Astria.
- RUFUS usa **System.Drawing.Common en .NET 10** (mismo API, implementación/rasterizado distinto en escalados bilinear y blending alpha del fondo 745×436→780×442).
- No hay desfase de celdas, capas ni GfxID incorrectos detectables (mean≪1, max≤7).

Criterio de test 10420: dimensiones OK, sin recursos faltantes, `mean < 0.5` y `max ≤ 8` → **NEAR_IDENTICAL** aceptado con esta causa documentada.

### Gaps de recursos en fixtures

Mapas **30001–30004** referencian `backgroundNum=340` pero **`Images/backgrounds/340.png` no existe** en la instalación → `RESOURCE_GAP` informado (no se inventa un fondo).

### Implementación RUFUS

- Proyecto: `RufusMapEditor.Rendering` (sin WPF)
- `AstriaMapRenderer` + `IsoGeometry` + `CachedBitmapGfxProvider` + `ImageComparer`
- Metadata SWF: `FlasmSwfMetadataReader` (flasm `-d`, preferir `*_AME.swf`)
- Pipeline: SQL MapData → Decode → catálogo → render → PNG → compare

---

## 7. Formato `.ame`

- Cabecera NRBF: `00 01 00 00 00 FF FF FF FF …`
- Tipo raíz: `AstriaMapEditor.Map`
- Campos serializados observados (ASCII del header):  
  `Screenshot, ID, DateMap, BackgroundID, Musique, MusiqueName, Ambiance, BOutdoor, Capabilities, Width, Height, Key, MapData, FightPlaces, NbGroups, GroupMaxSize, X, Y, Area, SubArea, SuperArea, NextRoom, NextCell, Mobs, GroupFixe_Mobs, GroupFixe_Cell`
- `config` usa el mismo mecanismo para `List<MyOptions>`.

**RUFUS:** import `.ame` → modelo interno vía `System.Formats.Nrbf` (Fase 8). **No** escribir `.ame` salvo necesidad demostrada. Formato propio: `.rufmap` versionado (JSON u otro seguro).

---

## 8. SWF / Flasm

### 8.1 Flujo export (Astria)

1. Genera script Flasm (`Flasm.Get_FlasmCode`) embebido en `blank.swf`.
2. Constantes: `id, width, height, backgroundNum, ambianceId, musicId, bOutdoor, capabilities, mapData`.
3. Ejecuta `flasm.exe -a …` (a menudo sin esperar bien el proceso).
4. Renombra `blank.$wf` → SWF de salida; sleeps artificiales.

`compil.bat` documenta el flujo manual equivalente.

### 8.2 Import

`UnPackerSwf` decompila Actions y parsea por posición.  
**DATO PENDIENTE DE CONFIRMAR:** equivalencia exacta entre `10420.swf` y `10420_AME.swf` (tamaños distintos: 855 vs 890 bytes).

### 8.3 Política RUFUS

- No tocar la instalación original en tests.
- Export en directorio temporal; copiar resultado solo si exit code OK y fichero existe.
- Tests de equivalencia **semántica**, no binaria.

---

## 9. SQL y base de datos

### 9.1 Lo que genera Astria (perfil local `config`)

Perfil observado en `config` (valores deserializados legibles):

| Opción | Valor local |
|---|---|
| Profil | `Dystopia` (lectura parcial del blob) |
| MySQL_Host | `localhost` |
| MySQL_User | `root` |
| MySQL_Database | `ancestra_static` |
| Table maps | `maps` |
| Columnas maps | `id, date, width, heigth, mapData, key, places, monsters, capabilities, mappos, numgroup, groupmaxsize` |
| Triggers | `scripted_cells` |
| EndFight | `endfight_action` |
| Mobs | `mobgroups_fix` |
| … | houses, mountpark_data, zaaps, zaapi, npcs |

### 9.2 Pendiente RUFUS

> **DATO PENDIENTE DE CONFIRMAR: esquema actual de la base de datos RUFUS relacionado con mapas y funciones del editor.**

No implementar escrituras directas. Crear más adelante perfiles/adaptadores tras validar el esquema real. Revisar bugs SQL de Astria antes de cualquier generación.

---

## 10. Fight places

Formato: `team1|team2` donde cada celda = 2 chars del mismo alfabeto 64.  
Código: `FightCellsManager` (`cellId = hi*64 + lo`).  
**No** forma parte del MapData de 10 chars; vive en SQL `places` / campo `FightPlaces` del `.ame`.

---

## 11. Geoposición (solo documentar — Fase 11)

| Elemento | Hallazgo |
|---|---|
| Formato `.geo` | BinaryFormatter de `CellGeo()` |
| `CellGeo` | `ID`, `Location`, `MapID`, `x_pos`, `y_pos` + refs no serializadas |
| Artefactos locales | `Géopositions/None/None.geo` + PNG overview / mode |
| Triggers auto / vecinos / bordes | Lógica en `Geoposition.vb` — **análisis detallado diferido** a módulo dedicado con tests antes de uso real |

No implementar en la primera iteración del editor.

---

## 12. Dependencias locales vs RUFUS

### Conservar conceptualmente (compatibilidad)

- Codec MapData 10-char + alfabeto
- Fórmula de número de celdas
- Campos de mapa presentes en `.ame` / SWF / SQL Astria
- Anchors XML + layout de carpetas Images (con la “s” de AME)
- Fight places encoding
- Flujo Flasm sobre plantilla `blank.swf` (mejorado)
- Atajos clásicos útiles (memoria muscular)

### Mejorar

- Indexación O(1) de Gfx / anchors
- Render incremental + hit-test isométrico matemático
- Undo/Redo, multi-selección, copiar/pegar, pinceles, fill, eyedropper
- Zoom/pan modernos, capas, pestañas multi-mapa
- Guardar proyecto (`.rufmap`) ≠ compilar/exportar
- Autosave en carpeta de recuperación (sin pisar el principal)
- Import NRBF seguro de `.ame`
- Validaciones explícitas
- Sin `Thread.Sleep` ni BinaryFormatter inseguro
- SQL solo con esquema RUFUS confirmado

### No introducir sin necesidad real

- ORMs pesados, motores de juego 3D, frameworks UI ajenos a WPF, etc.

---

## 13. Arquitectura RUFUS (objetivo)

```
RufusMapEditor.sln
├── src/
│   ├── RufusMapEditor.Domain              # modelos (CellData, MapDocument, …)
│   ├── RufusMapEditor.LegacyCompatibility # MapData, SQL Astria parser, (luego AME/SWF)
│   ├── RufusMapEditor.Rendering           # (Fase 3+)
│   ├── RufusMapEditor.Infrastructure      # (archivos, Flasm, autosave)
│   └── RufusMapEditor.App                 # WPF UI (más adelante)
└── tests/
    ├── fixtures/maps/*.sql                # COPIAS — nunca escribir en Astria
    └── RufusMapEditor.LegacyCompatibility.Tests
```

Reglas de dependencia:

- Codec **sin** WPF
- UI **sin** detalles Flasm
- Renderer **sin** SQL
- Compatibilidad Astria aislada

---

## 14. Estado de hitos

### Fase 0–1 (MapData)

| Entrega | Estado |
|---|---|
| Informe `docs/ASTRIA_COMPATIBILITY.md` | Este documento |
| Codec MapData + tests roundtrip | **PASS** (30 mapas + 10420) |

### Fase 2 (Catálogo GFX)

| Entrega | Estado |
|---|---|
| Escaneo + lookup O(1) + anchors | **PASS** |
| Anchors XML: **primera coincidencia** (como `Get_*_Pos`) | **PASS** |

### Fase 3 (Renderer isométrico)

| Entrega | Estado |
|---|---|
| Geometría + capas + flips/rotaciones | **PASS** |
| Render 10420 + compare golden | **PASS** (NEAR_IDENTICAL / GDI+ doc) |
| Pipeline 30 mapas + informe | **PASS** (`tests/artifacts/render/render_report.md`) |
| Tests previos MapData/Gfx | **PASS** |
| Astria sin modificar | **OK** |

### Fase 4 (Visor WPF)

| Entrega | Estado |
|---|---|
| `RufusMapEditor.App` (WPF, net10.0-windows) | **PASS** |
| Descubrimiento: `Maps/{id}/{id}.sql` (+ metadata SWF vía Flasm) | **PASS** |
| Viewport zoom/pan + overlay hover (sin re-render) | **PASS** |
| Hit-test `IsoHitTester` (Cell ID Astria) | **PASS** |
| Publicación: `artifacts/RufusMapEditor/RufusMapEditor.exe` | **PASS** |

### Fase 5 (Edición de celdas)

| Entrega | Estado |
|---|---|
| Selección vs hover (overlays independientes) | **PASS** |
| Catálogo visual + carpetas reales + búsqueda GfxID | **PASS** |
| Pintar/borrar Ground / Layer1 / Layer2 (solo memoria) | **PASS** |
| Propiedades MapData confirmadas editables | **PASS** |
| Dirty + aviso pérdida + recargar original | **PASS** |
| Astria sin escritura | **OK** |

### Fase 6 (Undo/Redo + herramientas)

| Entrega | Estado |
|---|---|
| EditHistory por documento (capacidad 100) | **PASS** |
| Stroke paint/erase = 1 comando | **PASS** |
| Selección rectangular + multi + Ctrl+clic | **PASS** |
| Copy/Paste geometría isométrica | **PASS** |
| Cuentagotas / Replace / Aplicar a selección | **PASS** |
| Favoritos + Recientes (Category+GfxID) | **PASS** |
| Catálogo: filas virtualizadas + thumbs lazy | **PASS** |
| Dirty ↔ historial (Undo hasta clean) | **PASS** |

---

## Fase 6 — Undo/Redo y herramientas avanzadas

### Historial

- `EditHistory` + `CellBatchEditCommand` + `CellSnapshot` (todos los campos MapData).
- Capacidad por defecto: **100** comandos; se descartan los más antiguos.
- Historial **por `MapEditSession` / documento** (no global).
- `MarkClean` al cargar/recargar; `IsDirty` ⇔ profundidad ≠ clean.
- Stroke: `BeginStroke` → mutaciones → `EndStroke` = un comando.
- No-ops no se registran.

### Paste

- Offsets relativos al centro isométrico del ancla (`MapClipboard`).
- Destino: celda primaria seleccionada (o hover).
- Celdas fuera de mapa: se omiten; status indica cuántas.

### Preview pincel

- ~~Overlay ligero (rombo semitransparente + miniatura 32px).~~ **Fase 9P:** preview completo con bitmap real, anchor Astria, flip/rot de pincel, opacidad ~55%, capa overlay sin tocar `MapDocument`.
- Implementación: `GfxPlacementMath` + `GfxOverlayCache` + `MapViewport.DrawBrushPreview`.
- Cuentagotas (`I`) copia flip/rot de la capa, cambia a Pintar y muestra preview inmediato.

### Selección visual Cell vs GFX (Fase 9P)

| Concepto | Representación RUFUS | Referencia Astria |
|---|---|---|
| Celda | Rombo isométrico (amarillo selección / azul hover) | `Cell.Border` violeta + rombo selección |
| Bounds GFX colocado | Rectángulo blanco (`Pens.White`) | `Cell.SurRound` / `SurRound_Gfx1/2/3` |
| Inspector layer highlight | Rectángulo amarillo/azul según capa | — (mejora UX RUFUS) |
| Ground seleccionado | Bounds + borde inspector verde | `SurRound_Gfx1` |
| Object1 / Object2 | Bounds independientes por capa | `SurRound_Gfx2` / `SurRound_Gfx3` (rot=0) |

#### Astria `SurRound` — investigación (código `_refs/AstriaMapEditor/MapEditor/Cell.vb`)

| Pregunta | Respuesta |
|---|---|
| Método dibujo | `Cell.SurRound(G, aTile, Flip, Rotate)` → `G.DrawRectangle(Pens.White, MyRectangle)` |
| Activación | `MapEditor.PictureBox1_MouseMove` / `RefreshDraw` cuando `Outil <> CellMode` |
| Selector | `SurRound_Gfx1/2/3` en **celda hover** (todas las capas con GFX) |
| Brush | Solo capa activa (`Calque` + tipo Ground/Object) |
| Posición | `Location(3).X + SizeCell - Pos_X`, `Location(2).Y - SizeCell/2 - Pos_Y` (= `D`, `C` en RUFUS) |
| Tamaño | `(image.W × PourceOfTile, image.H × PourceOfTile)` tras rot |
| Anchor | `Get_Ground_Pos` / `Get_Object_Pos` — **sin** fallback centro (a diferencia de `Draw_Tile`) |
| Flip | `RotateNoneFlipX`; ajuste Pos_X solo si `TileType.Objet` |
| Rotación Gfx1/2 | 0–3 con resize 51.85%/192.86% en casos 1 y 3 |
| Rotación Gfx3 | Siempre 0 en `SurRound_Gfx3` |
| Bitmap | Clona imagen original para calcular bounds (no thumbnail) |

RUFUS port: `GfxPlacementMath.ComputeBounds` (misma geometría que `AstriaMapRenderer.Draw_Tile` / `SurRound`).

### Catálogo (Fase 9P)

- Índice por carpeta + ID al cargar biblioteca (`BuildCatalogIndex`).
- Debounce búsqueda GfxID 200 ms.
- Skip rebuild si filtro (capa/carpeta/búsqueda) no cambió (`_lastVisibleGfxFilterKey`).
- Inspector: clic en preview de capa → highlight bounds; botón 📍 → `LocateLayerInCatalog`.

### Atajos (teclado)

| Tecla | Acción |
|---|---|
| Ctrl+Z / Y | Undo / Redo |
| Ctrl+C / V / D | Copiar / Pegar / Duplicar |
| V / R / B / E / I | Sel / Área / Pintar / Borrar / Cuentagotas |
| Delete | Borrar capa activa de la selección |

(No interceptan cuando el foco está en un TextBox.)

Ejecutar:

```bat
dotnet test tests/RufusMapEditor.LegacyCompatibility.Tests
.\scripts\publish-release-test.ps1
```

---

## 15. Plan de implementación (recordatorio)

| Fase | Contenido |
|---|---|
| 0 | Investigación + este informe |
| 1 | Codec MapData + tests |
| 2 | Catálogo/indexación Gfx |
| 3 | Renderer isométrico |
| 4 | Abrir mapa + navegación |
| 5 | Edición básica |
| 6 | Undo/Redo + herramientas |
| 7 | Guardado `.rufmap` + autosave |
| 8 | Exportación SWF / Flasm |
| 9 | Validación cliente RUFUS Retro ← **preparada (manual pendiente)** |
| 10 | (candidato) MapData cifrado / instalar en cliente / `.ame` / SQL RUFUS |

Formato de proyecto: ver [`docs/RUFMAP_FORMAT.md`](RUFMAP_FORMAT.md).  
Export SWF: ver [`docs/SWF_EXPORT.md`](SWF_EXPORT.md).  
Cliente: ver [`docs/RUFUS_CLIENT_MAP_PIPELINE.md`](RUFUS_CLIENT_MAP_PIPELINE.md) y [`docs/RUFUS_CLIENT_VALIDATION.md`](RUFUS_CLIENT_VALIDATION.md).

## Fase 5 — Edición de celdas

### Modelo de edición

```text
UI (herramienta / catálogo / inspector)
  → MapCellEditor (mutaciones tipadas sobre CellData)
  → MapDocument.Cells (+ SyncMapDataString vía MapDataCodec)
  → AstriaMapRenderer (re-render completo aceptable en Fase 5)
```

- Todas las ediciones son **en memoria**.
- Cerrar / cambiar mapa / recargar descarta cambios (con aviso si dirty).
- La biblioteca Astria permanece **solo lectura**.

API: `LegacyCompatibility.MapData.MapCellEditor`.

### Capas

| UI | Campo CellData | Catálogo |
|---|---|---|
| GROUND | `GroundGfxId` (+ flip/rot pincel) | `GfxCategory.Ground` |
| LAYER 1 | `Object1GfxId` (+ flip/rot) | `GfxCategory.Object` |
| LAYER 2 | `Object2GfxId` (+ flip; **sin rotación**) | `GfxCategory.Object` |

Object2 no tiene rotación en MapData (Astria tampoco tiene `RotaGfx3`).

### Propiedades editables confirmadas (MapData)

| Propiedad RUFUS | Bits / campo | Rango | Notas |
|---|---|---|---|
| Movement | 3 bits raw | 0,1,2,4,5,7 | **No** usar Astria `Cell.Type()` |
| LineOfSight | bit | bool | |
| InteractiveObject | bit | bool | IO calque 2 |
| GroundLevel | nibble | 0–15 | `NivSol`; no afecta draw Astria |
| GroundSlope | nibble | 0–15 | `InclineSol`; no afecta draw Astria |
| FlipGround / FlipObject1 / FlipObject2 | bits | bool | |
| GroundRotation / Object1Rotation | 2 bits | 0–3 | |

### Propiedades pendientes / no editables aún

| Concepto Astria | Motivo | Fase |
|---|---|---|
| FightCell 1/2 | No está en MapData | 6+ |
| Trigger scripted + nombre | SQL `scripted_cells`, no MapData movement | 6+ |
| Checklist “Marchable” como flag inverso | En RUFUS se edita `Movement` raw | — |
| Preview semitransparente de pincel | Overlay deseable; no bloqueante | 6 |
| Undo/Redo / multi-select / fill | Fuera de alcance | 6 |

### Catálogo

- Categorías = nombres de carpeta reales bajo `Images/grounds` y `Images/objects` (`GfxResource.Folder`).
- No hardcodear “Arbres”, “Eau”, etc.
- Miniaturas lazy (`GfxThumbnailCache`, DecodePixelWidth≈56); lista limitada/filtrada por carpeta o GfxID.
- WrapPanel + filtro por carpeta (virtualización plena diferida; carpetas grandes se acotan).

### Herramientas Fase 5

| Herramienta | Comportamiento |
|---|---|
| Seleccionar | LMB fija `SelectedCellId` |
| Pintar | LMB / drag aplica GFX de capa activa (sin re-pintar la misma celda en un stroke) |
| Borrar | LMB / drag limpia la capa activa |

### Toolbar vertical Astria (referencia; no clonada)

| Control Astria | Función real | Código | Equivalente RUFUS | Fase |
|---|---|---|---|---|
| `BT_ModeConstruction` | Brush | `AucunToolStripMenuItem_Click` | Herramienta Pintar | 5 |
| `BT_ModeSelect` | Selector | `ToolStripButton1_Click` | Herramienta Seleccionar | 5 |
| `BT_Unwalkable` | Paint Unwalkable | `NonMarchable…_Click` / `AddCellType` | Movement=Unwalkable (inspector) | 5 (inspector) / 6 paint-mode |
| `BT_LoS` | Paint LoS | `LigneDeVue…_Click` | LoS checkbox | 5 |
| `BT_Chemin` | Paint Path | `Chemin…_Click` | Movement=Path | 5 |
| `BT_Paddock` | Paint Paddock | `CelluleEnclos…_Click` | Movement=Paddock | 5 |
| `BT_FightCell1/2` | Fight placement | FightCell handlers | — | futura |
| `BT_Calque1/2` | Object layer | `ToolStripButton3/7_Click` | LAYER 1 / LAYER 2 | 5 |
| `BT_Flip` / `BT_Rotate` | Brush flip/rot | `ChangeFlip` / `ChangeRotate` | Flip/Rot pincel | 5 |
| Affichage menu | Grid/IDs/layers | `Grille…`, `IDDesCellules…` | Ver → cuadrícula / debug | 5 parcial |
| `BT_Del_Sol/Gfx2/Gfx3` | Clear layer | `Button1/2/3_Click` | ✕ en inspector / Editar | 5 |
| `SurRound_Gfx1/2/3` | Hover GFX bounds | `Cell.SurRound` | Bounds overlay mapa | **9P** |
| `Draw_Tile` (brush preview) | Preview pincel | `MapEditor.MouseMove` | Preview overlay semitransparente | **9P** |

### Astria binario referencia (integridad)

SHA256 `Astria Map Editor.exe` (solo lectura): `FFD62297984F92495C3C763EDD45B08C08FA2BEA7D9CAC0F29BEEDD4EFA9917D`

Verificado al cierre Fase 9P — sin escrituras en `Astria Map Editor 1`.

---

## Fase 9P — Pulido visual y EXE raíz (2026-08-22)

| Entrega | Estado |
|---|---|
| `RufusMapEditor.exe` en raíz RUFUS EDITOR | Release, win-x64, self-contained, single-file |
| `artifacts/RufusMapEditor/` | Mantenido |
| Settings `%LocalAppData%` | Sin rutas hardcodeadas a artifacts/bin |
| Bounds GFX SurRound | `GfxPlacementMath` + overlay blanco |
| Preview pincel completo | Cache bitmap, anchor, flip, rot |
| Inspector → seleccionar capa | Clic preview + highlight bounds |
| Localizar en catálogo | 📍 por capa |
| Cuentagotas → preview | Copia flip/rot, auto Pintar |
| Capas toolbar | GROUND / LAYER 1 / LAYER 2 colores |
| Catálogo optimizado | Índice carpeta + debounce búsqueda |
| Tests bounds/preview math | `GfxPlacementMathTests` (8 tests) |
| YACO como fuente | **ELIMINADO** — YACO reference removed; not a RUFUS source of truth. |

---

## Fase 9P.1 — Cell MapData Code + limpieza YACO (2026-08-22)

### Cell MapData Code

Astria-style per-cell MapData block (10 chars) remains documented elsewhere in this file / inspector MAPDATA UI.

### Limpieza YACO

YACO reference removed; not a RUFUS source of truth.

- `_refs/YacoEmulator` eliminado
- `tests/RufusMapEditor.YacoReference.Tests` eliminado
- `tests/artifacts/reference_only_yaco/` eliminado

## Fase 9P.2 — Preview-to-Final Placement (2026-08-22)

### Causa del bug (confirmada)

1. **Bounds del pincel:** en modo Paint, el rectángulo blanco mostraba el GFX **ya colocado** en la celda (`TryGetCellLayerVisual`), no el **preview del pincel**. Con un GFX nuevo seleccionado, preview y bounds referían capas distintas → apariencia de desfase.
2. **WPF preview:** `Image.Stretch` por defecto (`Uniform`) podía no rellenar el rectángulo de placement; corregido a `Fill` + `NearestNeighbor` (paridad con GDI+ del renderer).
3. **Rotación 1/3 en `ComputeBounds`:** usaba dimensiones **originales** del bitmap para los factores 192.86%/51.85%; Astria `Draw_Tile` usa dimensiones **post-Rotate90/270** (width/height intercambiados). Corregido en `CalculateDrawPlacement`.

### Preview-to-Final Placement

| Aspecto | Valor |
|---------|--------|
| Fuente única | `GfxPlacementMath.CalculateDrawPlacement` / `ComputePlacementOffsets` |
| Preview | `GfxOverlayCache` → hit space (PNG recortado = `MapImage`) |
| Final | `AstriaMapRenderer.DrawTile` → canvas completo → crop export |
| Equivalencia | hit = full − `ExportCrop`; zoom/pan solo en `WorldRoot` |
| Orden transform | flip → rotate → anchor offset (como Astria `Draw_Tile`) |
| Renderer | **No** reescrito con offsets mágicos; usa la misma función de placement |

Tests: `PreviewToFinalPlacementTests`, golden renderer intacto.

### EXE raíz

```
C:\Users\rubez\Desktop\RUFUS EDITOR\RufusMapEditor.exe
```

Publicar: `.\scripts\publish-release-test.ps1` → target MSBuild `PublishReleaseTest`.

Single-file: **SÍ** (self-contained + `PublishSingleFile=true`). Sin hacks; WPF + GDI+ compatibles.

### Overlays Astria diferidos

| Overlay | Método | Fase RUFUS |
|---|---|---|
| Hover / selección / grid debug | overlays UI | 5 (hecho) |
| GFX bounds blancos (SurRound) | `Cell.SurRound_*` | **9P (hecho)** |
| Preview pincel GFX completo | `Draw_Tile` en hover (Brush) | **9P (hecho)** |
| Unwalkable X / Path / LoS blocked / Paddock colors | `DrawMode` | 6+ |
| Fight 1 rojo / Fight 2 azul | `DrawMode` | futura |
| Trigger name | `DrawMode` | futura |
| IO text | `Draw_IO` | futura |
| Cell ID labels | `Draw_ID` | debug parcial |

### Atajos

| Tecla | Acción |
|---|---|
| Ctrl+Z / Y | Undo / Redo |
| Ctrl+C / V / D | Copiar / Pegar / Duplicar |
| V / R / B / E / I | Sel / Área / Pintar / Borrar / Cuentagotas |
| Delete | Borrar capa activa de la selección |

### Diferencias RUFUS vs Astria

- Movement raw; no bug Trigger/`TriggerCell` vía `Type()`.
- Sin escritura SQL/SWF/AME.
- UI moderna (no clon visual).
- Re-render completo tras edición (latencia medida en status `Edit→vis`).

---

## 16. Referencias de código fuente (públicas)

- GitHub: https://github.com/quentinrozados/AstriaMapEditor  
- Mirror mencionado: https://github.com/ZanoQuentin/AstriaMapEditor  
- GitLab: https://gitlab.com/zouki.dev/astria-map-editor-v2  

Clon de trabajo (no es la referencia de compatibilidad binaria): `_refs/AstriaMapEditor/`.

---

## 17. Gestionnaire d'île / Monde / Géopositions (investigación 9M.1)

### CONFIRMADO — código Astria (`_refs/AstriaMapEditor`)

| Elemento | Hallazgo |
|---|---|
| UI principal | `Geoposition.vb` — formulario **Géoposition** (toolbar `BT_Geoposition`) |
| Grid | `SizeMap` Width×Height configurable al abrir (`InputSizeMap`); celdas `CellGeo[]` |
| Coordenadas mundo | Campos `x_pos`, `y_pos` por celda; origen configurable (`x_PositionTopLeft`, `y_PositionTopLeft`) |
| Persistencia île | Guardar/cargar `.geo` con `BinaryFormatter` serializando `CellGeo[]` |
| Carpeta datos | `Géopositions/<isla>/` — `.geo`, PNGs, SQL/SWF generados (instalación Astria) |
| Area / SubArea | Combo `TXT_A` / `TXT_SubA` rellenados desde `Area.Areas` y `SubArea.SubAreas` (listas en código + XML en instalación) |
| Map ID por celda | Campo `MapID` en `CellGeo` |

### CONFIRMADO — equivalencia RUFUS 9M.1

| Astria | RUFUS |
|---|---|
| Grid île + MapID + x/y | Pestaña **MUNDO**, `.rufworld`, `WorldEditorService` |
| Preview mapa | `AstriaMapRenderer` vía `WorldThumbnailCache` |
| Duplicar mapa | `MapDocumentDuplicator` + `LocalMapIdAllocator` |
| `.geo` import | `AstriaGeoImporter` (read-only) |

### PENDIENTE — no implementado en 9M.1

| Función Astria | Notas |
|---|---|
| **Générer → Géopositions des maps** | Genera salidas servidor; requiere análisis salida exacta antes de export RUFUS |
| **Configuration des maisons et enclos** | Menú generador; estructura RUFUS **PENDIENTE** |
| **Définir les monstres pour toutes les maps** | **PENDIENTE** (BD RUFUS desconocida) |
| **Préparation patch** | **PENDIENTE** — no documentado con evidencia suficiente |
| **Auto placement des triggers** | **PENDIENTE** — requiere trazado handler específico |
| **Ouvrir toutes les maps** | Astria abría múltiples editores; RUFUS: abrir seleccionado / doble clic |

### Area / SubArea

- Astria expone nombres (Amakna, Astrub, …) vía clases `Area` / `SubArea`.
- Archivos `areas.xml` / `subareas.xml` existen en instalación Astria; **no** incluidos en portable 9P.3.
- **Semántica exacta IDs ↔ selector UI ↔ BD RUFUS: DATO PENDIENTE DE CONFIRMAR.**

---

## 18. Visibilidad / Fond / Grille (equivalencia 9UI)

### CONFIRMADO — Astria

Toggles visuales en editor de mapa:

| Astria | RUFUS 9UI |
|---|---|
| Grille | Rejilla (`ShowGrid`) |
| CellID | Cell ID (`ShowCellIds`, auto-hide zoom bajo) |
| Fond | Fondo (`ShowBackgroundLayer` + picker) |
| Sol | Suelo (`ShowGroundLayer`) |
| Calque 1 | Capa 1 (`ShowObject1Layer`) |
| Calque 2 | Capa 2 (`ShowObject2Layer`) |
| Fond → Sélectionner | `BackgroundPickerWindow` (48 BG catálogo) |

**Ningún toggle modifica MapData** — solo render/overlays.

### CONFIRMADO — sin fondo

`MapDocument.BackgroundId = 0` — sin background (Astria/SWF `backgroundNum`).

---

## 19. Fase 9G — Geometría de celdas / capas / fidelidad GFX (2026-08-22)

### CONFIRMADO — geometría Astria

| Elemento | Astria | RUFUS |
|---|---|---|
| `SizeBaseCell` | 26 (`MapEditor.vb`) | `IsoGeometry.SizeBaseCell = 26` |
| Grid | `GenerateGrid()` | `IsoGeometry.BuildCellCorners` |
| Hit test | `Get_IdCell` (4 cross-products) | `IsoHitTester.HitTest` |
| Export crop | `Save_Img` / `RogneImage` | `IsoGeometry.ExportCrop` |
| Centro celda | implícito | `IsoGeometry.GetCellCenter` (midpoint A↔C) |

Grid, hover, paint target, Cell ID y debug overlay comparten `IsoHitTester.TryGetCellCornersInHitSpace` (sin fórmula paralela).

### CONFIRMADO — mapeo capas MapData

| Astria | RUFUS campo | Catálogo |
|---|---|---|
| Gfx1 | `GroundGfxId` | Ground |
| Gfx2 | `Object1GfxId` | Object |
| Gfx3 | `Object2GfxId` | Object |

### CONFIRMADO — GfxID numérico compartido entre namespaces

Ejemplo documentado: **GfxID 374** existe en Ground (`grounds/Nowel/374.png`) y Object (`objects/Végétation/374.png`) — archivos distintos. Resolución obligatoria por `GfxCategory` + ID, no por ID solo (`GfxResourceResolver`).

### CONFIRMADO — dimensiones nativas

Catálogo indexa `PixelWidth`/`PixelHeight`; inspector muestra W×H, carpeta, anchor y aviso cuando ID existe en varios namespaces.

### PENDIENTE — reproducción manual GfxID 374 en mapa usuario

Ningún fixture SQL contiene 374 en MapData. Comparación celda-a-celda Astria↔RUFUS requiere el mapa concreto usado en la prueba manual.

Documentación detallada: `docs/CELL_GFX_FIDELITY.md`.

