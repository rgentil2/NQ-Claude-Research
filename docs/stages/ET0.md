# ET0 — Datos y entorno

**Archivo de traspaso (memoria viva de la etapa).** Lo lee el agente `et0-datos` al empezar y lo actualiza al terminar. La autoridad es `docs/CONTRACT_NQC_2.0.0.md` §2 ET0; si hay conflicto, manda el contrato.

| Campo | Valor |
|---|---|
| Estado | APROBADA (el estado oficial vive en el master §6) |
| Depende de | — |
| Objetivo | Garantizar que los datos M1 de NQ en NT8 son completos, reproducibles y están en Eastern. |
| Entradas | Histórico M1 NQ en NT8; plantilla horaria. |
| Salidas | DAYS.tsv, MONTHS.tsv, SUMMARY.txt con SHA256 |
| Gate | G0–G6 (ver contrato §2 ET0) |
| Rutas propias (lectura/escritura) | ninjatrader/src/NQC_ET0_*, tools/news/, runs/*ET0*, runs/*NEWS* |
| Límite de datos / reglas duras | Solo integridad; nunca economía. |

## Pendientes y notas vigentes
- Estado cerrado: solo se reabre para corregir datos o el calendario de noticias.
- Pendiente opcional: revisar 2020-10-23; corregir conteo de ADP en NEWS-1.0.0.

## Decisiones tomadas en esta etapa
(ninguna registrada con este modelo de trabajo todavía)

## Bitácora de traspasos
Formato: `AAAA-MM-DD · qué se hizo · artefactos/hash · qué queda · bloqueos`. Máximo 5 líneas por entrada.

- 2026-11-14 · Archivo creado al adoptar el modelo de agentes por etapa (`docs/WORK_MODEL.md`). Sin trabajo nuevo.
