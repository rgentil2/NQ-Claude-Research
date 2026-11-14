# ET7 — Evidencia estadística y congelado

**Archivo de traspaso (memoria viva de la etapa).** Lo lee el agente `et7-evidencia` al empezar y lo actualiza al terminar. La autoridad es `docs/CONTRACT_NQC_2.0.0.md` §2 ET7; si hay conflicto, manda el contrato.

| Campo | Valor |
|---|---|
| Estado | NO INICIADA (el estado oficial vive en el master §6) |
| Depende de | ET6, ET2 |
| Objetivo | Demostrar que el resultado es distinguible de la suerte de la búsqueda y congelar el portafolio. |
| Entradas | Finalistas de ET6; TRIALS_LEDGER; distribución nula de ET2. |
| Salidas | ET7_EVIDENCE_REPORT.txt, PORTFOLIO_FROZEN.txt |
| Gate | DSR ≥0,95; percentil nulo ≥95; MC P95 DD ≤2.000 |
| Rutas propias (lectura/escritura) | tools/lab/et7/, runs/*ET7*, docs/STATISTICS_PLAN.md |
| Límite de datos / reglas duras | IS + ET2. Congela hash de spec + motor + commit ANTES de abrir OOS. |

## Pendientes y notas vigentes
- (ninguna)

## Decisiones tomadas en esta etapa
(ninguna registrada con este modelo de trabajo todavía)

## Bitácora de traspasos
Formato: `AAAA-MM-DD · qué se hizo · artefactos/hash · qué queda · bloqueos`. Máximo 5 líneas por entrada.

- 2026-11-14 · Archivo creado al adoptar el modelo de agentes por etapa (`docs/WORK_MODEL.md`). Sin trabajo nuevo.
