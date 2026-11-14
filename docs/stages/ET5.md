# ET5 — Robustez interna por bloques

**Archivo de traspaso (memoria viva de la etapa).** Lo lee el agente `et5-robustez` al empezar y lo actualiza al terminar. La autoridad es `docs/CONTRACT_NQC_2.0.0.md` §2 ET5; si hay conflicto, manda el contrato.

| Campo | Valor |
|---|---|
| Estado | NO INICIADA (el estado oficial vive en el master §6) |
| Depende de | ET4 |
| Objetivo | Estabilidad entre años y medición del sobreajuste del procedimiento (PBO). |
| Entradas | Pool podado de ET4. |
| Salidas | Pool robusto + PBO |
| Gate | ≥2/3 años positivos; PBO ≤0,40 |
| Rutas propias (lectura/escritura) | ninjatrader/src/NQC_ET5*, runs/*ET5* |
| Límite de datos / reglas duras | IS solamente; pool vacío cierra la campaña. |

## Pendientes y notas vigentes
- Compensa la ausencia de examen intermedio (D-004b).

## Decisiones tomadas en esta etapa
(ninguna registrada con este modelo de trabajo todavía)

## Bitácora de traspasos
Formato: `AAAA-MM-DD · qué se hizo · artefactos/hash · qué queda · bloqueos`. Máximo 5 líneas por entrada.

- 2026-11-14 · Archivo creado al adoptar el modelo de agentes por etapa (`docs/WORK_MODEL.md`). Sin trabajo nuevo.
