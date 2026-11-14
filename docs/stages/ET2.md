# ET2 — Calibración ciega

**Archivo de traspaso (memoria viva de la etapa).** Lo lee el agente `et2-calibracion` al empezar y lo actualiza al terminar. La autoridad es `docs/CONTRACT_NQC_2.0.0.md` §2 ET2; si hay conflicto, manda el contrato.

| Campo | Valor |
|---|---|
| Estado | NO INICIADA (el estado oficial vive en el master §6) |
| Depende de | ET3–ET7 (código) |
| Objetivo | Medir falsos positivos y potencia del embudo completo y fijar umbrales [B] en NQC 2.1.0. |
| Entradas | Módulos ET3–ET7 aprobados técnicamente; datasets nulos e inyectados (STATISTICS_PLAN). |
| Salidas | ET2_CALIBRATION_REPORT.txt; NQC 2.1.0 |
| Gate | FP ≤5% por campaña; potencia ≥70% |
| Rutas propias (lectura/escritura) | tools/lab/et2/, runs/*ET2*, docs/STATISTICS_PLAN.md |
| Límite de datos / reglas duras | Solo datos nulos e inyectados; nunca datos reales del IS como resultado. |

## Pendientes y notas vigentes
- Es el cuello de botella: exige el embudo completo implementado.
- Al cerrar, los umbrales [B] se congelan y ya no se mueven.
- Si falla la potencia: rediseñar embudo (versión menor) y repetir.

## Decisiones tomadas en esta etapa
(ninguna registrada con este modelo de trabajo todavía)

## Bitácora de traspasos
Formato: `AAAA-MM-DD · qué se hizo · artefactos/hash · qué queda · bloqueos`. Máximo 5 líneas por entrada.

- 2026-11-14 · Archivo creado al adoptar el modelo de agentes por etapa (`docs/WORK_MODEL.md`). Sin trabajo nuevo.
