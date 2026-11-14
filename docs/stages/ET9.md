# ET9 — Paridad nativa NT8

**Archivo de traspaso (memoria viva de la etapa).** Lo lee el agente `et9-paridad-nativa` al empezar y lo actualiza al terminar. La autoridad es `docs/CONTRACT_NQC_2.0.0.md` §2 ET9; si hay conflicto, manda el contrato.

| Campo | Valor |
|---|---|
| Estado | NO INICIADA (el estado oficial vive en el master §6) |
| Depende de | ET8 |
| Objetivo | NT8 con órdenes nativas reproduce al motor; NT8 manda en el veredicto final. |
| Entradas | PORTFOLIO_SPEC.json congelado; resultado ET8. |
| Salidas | Estrategia nativa generada del spec; informe de paridad por bloque |
| Gate | ≥99% ops iguales; ≤1 tick; neto ≤2% |
| Rutas propias (lectura/escritura) | ninjatrader/src/NQC_ET9*, runs/*ET9* |
| Límite de datos / reglas duras | Nunca cambia miembros, genes ni prioridad. |

## Pendientes y notas vigentes
- (ninguna)

## Decisiones tomadas en esta etapa
(ninguna registrada con este modelo de trabajo todavía)

## Bitácora de traspasos
Formato: `AAAA-MM-DD · qué se hizo · artefactos/hash · qué queda · bloqueos`. Máximo 5 líneas por entrada.

- 2026-11-14 · Archivo creado al adoptar el modelo de agentes por etapa (`docs/WORK_MODEL.md`). Sin trabajo nuevo.
