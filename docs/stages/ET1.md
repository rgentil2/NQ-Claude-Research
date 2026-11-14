# ET1 — Motor, simulador de cuenta y paridad

**Archivo de traspaso (memoria viva de la etapa).** Lo lee el agente `et1-motor-cuenta` al empezar y lo actualiza al terminar. La autoridad es `docs/CONTRACT_NQC_2.0.0.md` §2 ET1; si hay conflicto, manda el contrato.

| Campo | Valor |
|---|---|
| Estado | APROBADA (el estado oficial vive en el master §6) |
| Depende de | ET0 |
| Objetivo | Motor con ejecución, contabilidad y simulador de cuenta demostrados antes de mirar economía. |
| Entradas | ET0; EXECUTOR_SPEC 2.3.0; reglas MFFU. |
| Salidas | Código en ninjatrader/src, informes de paridad, acta |
| Gate | P1–P6 al 100% (cerrada) |
| Rutas propias (lectura/escritura) | ninjatrader/src/NQC_ET1*, tools/lab/, docs/EXECUTOR_SPEC.md, docs/ET1_* |
| Límite de datos / reglas duras | IS solo técnico, sin mirar PF/beneficio. |

## Pendientes y notas vigentes
- Tarea viva: construir el motor genérico + ejecutor de portafolio para ET2/ET3.
- Antes de usarlo debe reproducir N1–N9 (8.110 operaciones), los 46 saltos y los 22 chequeos de ET1C (acta ET1 §6).
- Pendiente D-018 (biblioteca de condiciones) y D-019 (rangos de ATR).

## Decisiones tomadas en esta etapa
(ninguna registrada con este modelo de trabajo todavía)

## Bitácora de traspasos
Formato: `AAAA-MM-DD · qué se hizo · artefactos/hash · qué queda · bloqueos`. Máximo 5 líneas por entrada.

- 2026-11-14 · Archivo creado al adoptar el modelo de agentes por etapa (`docs/WORK_MODEL.md`). Sin trabajo nuevo.
