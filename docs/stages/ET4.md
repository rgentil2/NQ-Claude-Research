# ET4 — Poda barata por estrategia

**Archivo de traspaso (memoria viva de la etapa).** Lo lee el agente `et4-poda` al empezar y lo actualiza al terminar. La autoridad es `docs/CONTRACT_NQC_2.0.0.md` §2 ET4; si hay conflicto, manda el contrato.

| Campo | Valor |
|---|---|
| Estado | NO INICIADA (el estado oficial vive en el master §6) |
| Depende de | ET3 |
| Objetivo | Descartar candidatos que no resisten costos 1,5×, sub-periodos ni vecindario de parámetros. |
| Entradas | Databank de ET3. |
| Salidas | Pool podado + causa de rechazo por candidato |
| Gate | F1 costo estresado, F2 ≥7/12 trimestres, F3 ≥70% vecinos |
| Rutas propias (lectura/escritura) | ninjatrader/src/NQC_ET4*, runs/*ET4* |
| Límite de datos / reglas duras | IS solamente. |

## Pendientes y notas vigentes
- F4 (ancho del stop) es informativo, no elimina.

## Decisiones tomadas en esta etapa
(ninguna registrada con este modelo de trabajo todavía)

## Bitácora de traspasos
Formato: `AAAA-MM-DD · qué se hizo · artefactos/hash · qué queda · bloqueos`. Máximo 5 líneas por entrada.

- 2026-11-14 · Archivo creado al adoptar el modelo de agentes por etapa (`docs/WORK_MODEL.md`). Sin trabajo nuevo.
