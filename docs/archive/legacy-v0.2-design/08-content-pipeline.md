# 08 — Content pipeline (canónico: código + datos, no documentos sueltos)

> Creado en auditoría GATE-G4: el prompt maestro lo lista como autoritativo;
> no existía como archivo. Esta es la descripción real del pipeline.

## Dónde viven los datos
`src/Fighter.Core/GlenData.cs` y `KiraData.cs`: builders que producen
`FighterData` (stats + `List<MoveData>` + motion map + chains + gimmick).
Formato: C# compilado (no JSON/binary/SO). Sin archivos de datos sueltos.

## Validación (puerta obligatoria)
`DataValidator.ValidateFighter` corre dentro de cada `Build()`: ids únicos,
frames > 0, botones en normales, niveles en throws, flags compatibles,
costos EX en rango, cadenas contra ids existentes, motion-map contra ids,
AirOK solo normales neutrales, stats positivos, zonas del stage.
Datos inválidos → excepción ruidosa. Probado en `DataValidationTests`.

## Exportación a herramientas
`FrameDataExport.ToJson`: JSON byte-estable generado DESDE las estructuras
del runtime (misma fuente que la sim, §49). HUD/training consumen runtime,
nunca copias manuales.

## Template de personaje (proceso, GDD Apéndice B)
1. Frame data en tabla (startup/onBlock/daño GDD + [CAL] documentados).
2. Transcribir a `XData.Build` (consts de ids estables).
3. Chains desde rutas GDD + motion map.
4. `ValidateFighter` verde + suites de gimmick + rutas ejecutables.
5. Placeholder explícito `[NOMBRE-CAL]` si el GDD no da números (OPEN-008);
   prohibido presentarlos como canon.

## GimmickData
`Gimmick` (id cerrado) + `GimmickState` blob versionado en snapshot schema 5.
Reglas §13: schema, snapshot, hash, replay, rollback, reset, round, KO, tests.

## Estado
Glen: datos GDD reales + [CAL] (27 movimientos). Kira: mecánica real + números
provisionales (OPEN-008). Añadir un 3er personaje con gimmick conocido no toca
reglas Core (precedente: Kira reutilizó gimmick/energía existentes tras añadir
su hook documentado); gimmicks inéditos pueden requerir hooks nuevos (P1-01).
