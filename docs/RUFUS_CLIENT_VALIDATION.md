# Validación cliente RUFUS Retro — Fase 9

Estado: **PREPARADA PARA VALIDACIÓN MANUAL** — ningún archivo del cliente ha sido modificado.

Fecha investigación: 2026-08-22.

## Resumen ejecutivo

Se localizó el cliente y el almacén real de SWF de mapas.  
El export actual de RUFUS Map Editor es **compatible con el formato Astria/Flasm en claro**, pero el SWF **instalado en el cliente RUFUS para 10420 es distinto** (MapData cifrado + metadatos distintos + flags extra).

Por tanto: **no se ha sustituido nada**. Se requiere autorización explícita y, probablemente, un paso adicional (cifrado / date stamp / acuerdo con servidor) antes de una prueba segura.

## Cliente

| Ítem | Valor |
|------|--------|
| Ruta recomendada para pruebas | `C:\Users\rubez\Desktop\RUFUS RETRO` |
| Exe | `Dofus Retro.exe` |
| Maps | `resources\app\retroclient\data\maps\` |
| Dataserver | `http://169.58.162.70/data/` (`config.xml`) |
| Copias espejo (mismo hash 10420) | Ankama Retro, jeifus-client |

Detalle: [`RUFUS_CLIENT_MAP_PIPELINE.md`](RUFUS_CLIENT_MAP_PIPELINE.md).

## Map ID de prueba recomendado

**10420** — conocido en Astria, fixtures, export RUFUS y presente en el cliente.

**Advertencia:** el SWF del cliente para 10420 **no** coincide con el export RUFUS sin cambios.

## Hashes registrados (solo lectura)

| Archivo | Bytes | SHA256 |
|---------|------:|--------|
| Cliente `...\data\maps\10420_0706141524X.swf` | 3847 | `D0FAB95D102937008755ABC78009543CAB7BD83F3FB292693094E768E39D18EB` |
| Astria `Maps\10420\10420_AME.swf` | 890 | `767685A280BF04E63DAA42058ECCE3FB7F7F487DCF60491DCDD007BFBA547F6E` |
| RUFUS export `tests\artifacts\swf\10420_rufus.swf` | 890 | `767685A280BF04E63DAA42058ECCE3FB7F7F487DCF60491DCDD007BFBA547F6E` |
| Astria `Flasm\blank.swf` (plantilla) | 636 | `C6DA223034DEC8E58669C9E8B43F958B8B2875A242B6E84C5D98978FE379B3EB` |

Astria Map Editor.exe permanece **INTACTO** (no tocado en esta fase).

## Procedimiento de backup (cuando se autorice)

1. Confirmar carpeta cliente activa (Desktop / Ankama / jeifus).
2. Destino exacto:  
   `{cliente}\resources\app\retroclient\data\maps\10420_0706141524X.swf`
3. Calcular SHA256 actual (debe ser `D0FAB95D…` si no ha cambiado).
4. Copiar a backup **fuera** del cliente, p.ej.:  
   `RUFUS EDITOR\tests\artifacts\client-backup\10420_0706141524X.swf.bak`  
   + fichero `.sha256.txt` con hash y fecha.
5. Solo entonces sustituir (atómico: temp → replace).
6. Restauración: copiar backup → destino; verificar hash = original.

**NO ejecutado aún.**

## Caché

- Mínimo a considerar: el propio fichero en `data\maps\`.
- Pepper `AssetCache` y Chromium `Cache` existen; **no** confirmado que almacenen el SWF de mapa.
- Riesgo: **re-descarga** desde `http://169.58.162.70/data/` sobrescribiendo el SWF de prueba (**HIPÓTESIS**).

## Servidor / BD

| Pregunta | Respuesta |
|----------|-----------|
| ¿Hace falta BD para *solo* probar gráficos de un Map ID ya existente? | **PENDIENTE** — si el servidor manda key y espera MapData cifrado, sustituir SWF en claro puede fallar o verse “vacío/roto” sin tocar BD |
| ¿Crear Map ID nuevo? | **NO en Fase 9** — requiere esquema RUFUS (**DATO PENDIENTE**) |
| SQL RUFUS | **NO inventado / NO escrito** |

## Resultado de validación in-game

**No realizado** (pendiente autorización + decisión sobre cifrado).

Checklist previsto cuando se autorice una estrategia segura:

- [ ] Backup + hash
- [ ] Sustitución controlada
- [ ] Entrar al mapa 10420
- [ ] Render / movimiento / LoS / interactivos / transiciones / sin crash
- [ ] Restaurar + verificar hash

## Cuestiones pendientes

1. Confirmación de qué carpeta cliente usa el launcher en gameplay real.
2. Paquete servidor: campos `date`, `key`, mapData.
3. Cómo producir SWF cifrado compatible con RUFUS (export Fase 8 hoy = claro).
4. Si el dataserver reescribe ficheros locales.
5. Significado exacto de `canAggro` / `canUseInventory` / etc. en el SWF cliente.
6. Esquema tabla(s) mapas RUFUS (Navicat) — **DATO PENDIENTE DE CONFIRMAR**.

## Astria

READ ONLY. Ningún archivo escrito en la instalación Astria.
