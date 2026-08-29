# Pipeline de mapas — Cliente RUFUS Retro (corrección procedencia 9B)

Ver `docs/MAP_CRYPTO.md` para la auditoría crypto.  
YACO reference removed; not a RUFUS source of truth.

## Cliente

| Rol | Ruta | Etiqueta |
|-----|------|----------|
| Cliente Desktop | `C:\Users\rubez\Desktop\RUFUS RETRO` | `CONFIRMADO CLIENTE RUFUS` |
| Motor Flash | `...\resources\app\retroclient\` | `CONFIRMADO CLIENTE RUFUS` |
| Almacén mapas | `retroclient\data\maps\{id}_{stamp}.swf` | `CONFIRMADO CLIENTE RUFUS` |
| Ejemplo | `10420_0706141524X.swf` (MapData hex ~9580) | `CONFIRMADO CLIENTE RUFUS` |
| Dataserver | `config.xml` → `http://169.58.162.70/data/` | `CONFIRMADO CLIENTE RUFUS` |
| Launcher / copias Ankama / jeifus | rutas Desktop/AppData | `CONFIRMADO CLIENTE RUFUS` (existencia local) |

## Servidor / BD RUFUS

| Tema | Etiqueta |
|------|----------|
| SRC/emulador actual de RUFUS | `DATO PENDIENTE DE CONFIRMAR` |
| Paquete de carga de mapa | `DATO PENDIENTE DE CONFIRMAR` |
| Esquema SQL mapas RUFUS | `DATO PENDIENTE DE CONFIRMAR` |
| Key/date 10420 en BD RUFUS | `DATO PENDIENTE DE CONFIRMAR` |

## Referencias externas (no verdad RUFUS)

| Material | Etiqueta |
|----------|----------|
| Astria \Decryptage\ / exports \*.sql\ date=\AME\ | \REFERENCIA ASTRIA\ |

YACO reference removed; not a RUFUS source of truth. Do not use external emulator dumps for RUFUS build, runtime, or tests.

## Flujo (estado corregido)

```text
[DATO PENDIENTE: BD/servidor RUFUS — key, date, paquete]
        │
        ▼
Cliente RUFUS carga  data\maps\{id}_{stamp}.swf     ← CONFIRMADO CLIENTE RUFUS
        │
        ▼
MapData en SWF aparece hex/cifrado (10420: 9580)   ← CONFIRMADO CLIENTE RUFUS
        │
        ▼
Descifrado con key del servidor                    ← DATO PENDIENTE / HIPÓTESIS
```

## Dos pipelines SWF

| | Export Astria / RUFUS Map Editor actual | SWF en cliente RUFUS |
|--|----------------------------------------|----------------------|
| MapData | Plano (~4790) | Hex (~9580) — `CONFIRMADO CLIENTE RUFUS` |
| Sustitución directa | **No** asumir compatibilidad | — |

## Cache / re-download

| Condición | Etiqueta |
|-----------|----------|
| Fichero ausente → posible descarga dataserver | `HIPÓTESIS` |
| Date distinto → overwrite | `HIPÓTESIS` |
| Checksum cliente | `DATO PENDIENTE DE CONFIRMAR` |
