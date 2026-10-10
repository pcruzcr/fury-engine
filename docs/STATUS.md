# Estado del proyecto

> Documento vivo y **fuente de verdad del estado**. Si otro documento lo contradice, este manda (y el otro tiene un error que
> hay que corregir). Los tests `DocumentationTests` comprueban que las rutas y las opciones de línea de comandos que se citan en
> la documentación existen de verdad. Última revisión: 2026-10-09, versión **0.1.0-alpha1**.

## 1. Qué es esto

Dos cosas en un repositorio:

- **Fury Engine**: un motor/framework de juegos de lucha sobre Godot 4.7.2 (C# / .NET 9): simulación determinista de punto fijo,
  rollback, repeticiones, herramientas y un pipeline de contenido y arte como datos.
- **Dual Fury**: el juego que define el GDD v8.0 (`gdd/`). Hoy es **una demo técnica jugable** construida sobre el motor: dos
  luchadores (Glen y Kira) con frame data propia, no con la del GDD. **El ruleset de Dual Fury (Rage / Resonancia / Parry / Command
  Normals de 6 botones) todavía no está en el motor**: ver `docs/GDD-TRACEABILITY.md`.

## 2. Situación por capas

| Capa | Estado | Notas |
|---|---|---|
| Simulación determinista (Fp Q16.16, SoA, pipeline de 9 fases, módulos) | **Hecho y verificado** | Hashes idénticos en Windows y Linux; Native AOT correcto; 0 alocaciones por frame |
| Rollback y netcode (UDP autenticado, relay, desync, rondas continuas) | **Hecho; verificado en local** | Nunca probado en Internet real, ni con humanos |
| Repeticiones (FRY4) | **Hecho y verificado** | Verificación por resimulación |
| Herramientas (frame data, stages, visor, baker, validador) | **Hecho** | Solo en builds de debug (menú); validador también en CI |
| Pipeline de arte, audio, VFX, UI, idiomas como datos | **Hecho y verificado con plantillas** | `docs/ART_PIPELINE.md`. No hay arte ni audio finales |
| Cáscara del juego (menús, selección, pausa, controles, entrenamiento, online) | **Hecho** | Alpha jugable en Windows |
| **Ruleset del GDD (Dual Fury)** | **Falta** | Parry, Resonancia activa, Rage por EX, Clash, Counter Hit, 6 botones, escalado del GDD |
| Contenido del GDD (16 personajes, 13 stages, modos, historia) | **Falta** | 2 personajes con datos propios, 1 stage |
| Plataformas | **Windows**; Linux/macOS con presets sin probar como juego exportado; consolas no | Ver ADR-011 |
| Producción (QA humano, playtests, economía, cuentas, tienda) | **Falta** | Cero partidas jugadas por personas ajenas al desarrollo |

## 3. Lo que está verificado, y cómo

| Comprobación | Resultado | Cómo repetirla |
|---|---|---|
| Build sin avisos (el núcleo trata los avisos como errores) | OK | `dotnet build FuryEngine.sln -c Release` |
| Tests | **327** (núcleo, red, relay, sim, replay, seguridad, coherencia de la documentación) + **66** (authoring: manifiestos, packs, idiomas, stages) | `dotnet test FuryEngine.sln -c Release` |
| Certificación determinista contra hashes versionados | OK; 5,8 µs por frame (Windows) / 18 µs (Linux) | `dotnet run --project cert -c Release` |
| Mismos hashes en Windows y Linux (Ubuntu 24.04) | OK | `cert` en ambos |
| Native AOT (gcc) | OK, mismo resultado | job `aot-analysis` de CI (reproducido en Linux) |
| Smoke de Godot: todas las escenas, plantillas en 2D/2.5D/3D, validador de contenido | OK | `tools/godot-smoke.sh` |
| Partida online real de 3 rondas entre dos procesos | OK: directa y por relay, Windows↔Windows, Linux↔Linux, **Windows↔Linux** | `tools/godot-online-smoke.sh` (`RELAY=1` para el relay) |
| Export de Windows arranca, lee `i18n`, dibuja; empaqueta `.stage` y deja fuera las plantillas `_*` | OK (el `.stage` se probó con un stage temporal en `res://stages`; `.fmt2` usa el mismo filtro pero ningún paquete lo ha ejercitado todavía) | `tools/package-alpha.sh` + `--fury-report` |
| Paquete alpha (zip con juego, relay Windows/Linux y guía) | `build/DualFury-alpha-alpha1-windows.zip` | `tools/package-alpha.sh <etiqueta>` |

**Límites de esa evidencia (importante):**

- Windows↔Linux se probó en un solo PC (el Linux es WSL1 y comparte la red de Windows): demuestra el cálculo idéntico y el
  protocolo, no la latencia, los NAT ni la pérdida de paquetes reales (esos se simulan en tests con jitter y pérdida).
- No hay verificación en ARM (Apple Silicon, Android, Switch), que el GDD marca como riesgo crítico.
- macOS solo existe en la matriz de CI, que no se ha ejecutado en un runner real; el job de Godot en CI es `continue-on-error`.
- Ninguna persona ajena al desarrollo ha jugado el juego: la sensación de los controles y el equilibrio están sin validar.

## 4. Mapa de la documentación

| Documento | Para qué sirve | ¿Normativo? |
|---|---|---|
| `gdd/` (GDD v8.0 y anteriores) | Definición del producto terminado | **Sí** (v8.0 manda) |
| `docs/STATUS.md` | Estado real (este documento) | Sí |
| `docs/GDD-TRACEABILITY.md` | Cada requisito del GDD v8.0 contra el código: hecho / parcial / falta / desviación | Sí |
| `docs/ROADMAP.md` | Qué falta para terminar el producto, en orden, con decisiones pendientes | Sí |
| `docs/adr/` | Decisiones de arquitectura (con notas de actualización donde la realidad superó al ADR) | Sí |
| `docs/API.md` | Resumen de las APIs públicas | Referencia |
| `docs/ART_PIPELINE.md` | Guía de arte y contenido | Sí |
| `docs/ALPHA_TESTER_GUIDE.txt` | Guía que viaja dentro del zip del alpha | Sí |
| `docs/archive/legacy-v0.2-design/` | Diseño detallado del proyecto anterior (GDD v8.0 desglosado, decisiones del director OPEN-001…) | **Referencia para portar el ruleset** |
| `archive-reference/legacy-core-v0.2/` | Código y tests del núcleo anterior que sí implementaba el ruleset del GDD | **Referencia, no se compila** |
| `docs/archive/dev-plan-2026-09/` | Planes históricos del motor (afirmaciones no verificadas) | No: histórico |

## 5. Tamaño

≈18 mil líneas de C# en `src/` (núcleo ≈10,4 mil, juego Godot ≈7,8 mil), ≈6,7 mil en `tests/`, 11 ADRs.
