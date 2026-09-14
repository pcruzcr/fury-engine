# ADR-003: Estado dirigido por datos — tablas binarias compiladas (hot-reload)

- **Estado:** Aceptado (2026-09-14)

## Contexto

Las fichas de personaje (p. ej. los 27 movimientos de Glen y las 10 variantes del
CommandNormalResolver) no deben estar codificadas en C#: compilar para tocar un frame data
mata la iteración. Pero cargar JSON en runtime viola la estabilidad de bytes y el AOT.

## Decisión

1. Formato binario propio versionado (`FMT1`), **Little-Endian explícito** vía
   `BinaryPrimitives`, generado por el pipeline de datos (editor/tooling).
2. El parser (`Fury.Core.Data.BinaryMoveTable`) valida estructuralmente: magic, versión,
   longitud exacta esperada, bounds — cualquier desviación es `InvalidDataException`, nunca
   lectura silenciosa.
3. La FSM determina transiciones leyendo estas tablas en caliente: cambiar frame data NO
   requiere recompilar C#, solo regenerar el binario (hot-reload en tools).
4. Export JSON byte-estable solo para tooling humano; el runtime nunca parsea texto.

## Consecuencias

- Datos y código evolucionan en ciclos independientes.
- El binario es trivialmente hasheable (xxHash64) → la versión de datos viaja en replays y
  sesiones de red para rechazar mezclas de versiones.
