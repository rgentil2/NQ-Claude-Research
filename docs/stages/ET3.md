# ET3 — Generación evolutiva

**Archivo de traspaso (memoria viva de la etapa).** Lo lee el agente `et3-generacion` al empezar y lo actualiza al terminar. La autoridad es `docs/CONTRACT_NQC_2.0.0.md` §2 ET3; si hay conflicto, manda el contrato.

| Campo | Valor |
|---|---|
| Estado | NO INICIADA (el estado oficial vive en el master §6) |
| Depende de | ET2 |
| Objetivo | Búsqueda evolutiva guiada por fitness (t de expectativa neta) en el IS. |
| Entradas | NQC 2.1.0; biblioteca de condiciones (D-018); rangos ATR (D-019). |
| Salidas | Databank top-K por campaña, TRIALS_LEDGER sellado, curva de fitness |
| Gate | Corrida completa + ledger con hash |
| Rutas propias (lectura/escritura) | ninjatrader/src/NQC_ET3*, runs/*ET3*, docs/TRIALS_LEDGER.md |
| Límite de datos / reglas duras | IS 2020–2022 solo; máx. 5 campañas (C1–C5), ≤100.000 evaluaciones; semillas declaradas antes. |

## Pendientes y notas vigentes
- Cada evaluación cuenta como ensayo en el ledger.

## Decisiones tomadas en esta etapa
(ninguna registrada con este modelo de trabajo todavía)

## Bitácora de traspasos
Formato: `AAAA-MM-DD · qué se hizo · artefactos/hash · qué queda · bloqueos`. Máximo 5 líneas por entrada.

- 2026-11-14 · Archivo creado al adoptar el modelo de agentes por etapa (`docs/WORK_MODEL.md`). Sin trabajo nuevo.
