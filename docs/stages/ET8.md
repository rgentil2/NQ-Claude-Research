# ET8 — OOS sellado

**Archivo de traspaso (memoria viva de la etapa).** Lo lee el agente `et8-oos` al empezar y lo actualiza al terminar. La autoridad es `docs/CONTRACT_NQC_2.0.0.md` §2 ET8; si hay conflicto, manda el contrato.

| Campo | Valor |
|---|---|
| Estado | NO INICIADA (el estado oficial vive en el master §6) |
| Depende de | ET7 |
| Objetivo | Evaluar el portafolio congelado en OOS1 y luego OOS2, una vez cada uno. |
| Entradas | PORTFOLIO_FROZEN.txt commiteado; docs/OOS_EXPOSURE.md completado por el usuario. |
| Salidas | Resultado OOS provisional |
| Gate | Gate acumulado OOS1+OOS2 (contrato §2 ET8) |
| Rutas propias (lectura/escritura) | runs/*ET8* |
| Límite de datos / reglas duras | ÚNICO agente que toca OOS. Una vez por campaña. No modifica nada tras abrir. |

## Pendientes y notas vigentes
- Bloqueado hasta que el usuario complete OOS_EXPOSURE.md.

## Decisiones tomadas en esta etapa
(ninguna registrada con este modelo de trabajo todavía)

## Bitácora de traspasos
Formato: `AAAA-MM-DD · qué se hizo · artefactos/hash · qué queda · bloqueos`. Máximo 5 líneas por entrada.

- 2026-11-14 · Archivo creado al adoptar el modelo de agentes por etapa (`docs/WORK_MODEL.md`). Sin trabajo nuevo.
