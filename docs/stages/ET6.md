# ET6 — Portafolios con ejecutor en el bucle

**Archivo de traspaso (memoria viva de la etapa).** Lo lee el agente `et6-portafolio` al empezar y lo actualiza al terminar. La autoridad es `docs/CONTRACT_NQC_2.0.0.md` §2 ET6; si hay conflicto, manda el contrato.

| Campo | Valor |
|---|---|
| Estado | NO INICIADA (el estado oficial vive en el master §6) |
| Depende de | ET5 |
| Objetivo | Encontrar combinaciones de 3–5 miembros que cumplan el perfil con 1 NQ compartido. |
| Entradas | Pool robusto de ET5; ejecutor. |
| Salidas | Hasta 5 finalistas con PORTFOLIO_SPEC.json |
| Gate | Perfil completo (PF≥1,25; Stability y Symmetry >30% duras; DD≤2.000; peor día≤1.000; ≥150 ops; P_pass) |
| Rutas propias (lectura/escritura) | ninjatrader/src/NQC_ET6*, runs/*ET6* |
| Límite de datos / reglas duras | IS solamente; Stability/Symmetry solo a nivel portafolio (D-022). |

## Pendientes y notas vigentes
- Pendiente del usuario: confirmar interpretación de ET6 (D-019).

## Decisiones tomadas en esta etapa
(ninguna registrada con este modelo de trabajo todavía)

## Bitácora de traspasos
Formato: `AAAA-MM-DD · qué se hizo · artefactos/hash · qué queda · bloqueos`. Máximo 5 líneas por entrada.

- 2026-11-14 · Archivo creado al adoptar el modelo de agentes por etapa (`docs/WORK_MODEL.md`). Sin trabajo nuevo.
