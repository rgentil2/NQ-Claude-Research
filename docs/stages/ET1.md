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
- Tarea viva: construir el motor genérico + ejecutor de portafolio para ET2/ET3. **Paquetes (2026-11-14):**
  - WP1 núcleo de datos/indicadores/noticias/RNG (`NQC_ET1_Core.cs`): ESCRITO, sin compilar.
  - WP2 genoma de entradas + genes de salida ATR (condiciones de referencia; la biblioteca D-018 sigue pendiente): ESCRITO.
  - WP3 ejecutor central de 1 NQ con prioridad, costos y salidas §3: ESCRITO.
  - WP4 capa protectora + hooks de cuenta (cuenta continua, `NQC_AccountSim` reutilizado): ESCRITO.
  - WP5 auditoría §9 (TRADES, WATERFALL, PROTECT_EVENTS, ACCOUNT_SIM) y métricas Stability/Symmetry/DD: ESCRITO (SUMMARY.txt lo arma el runner, WP8).
  - WP6 determinismo con semilla (`NQCE_Rng`): ESCRITO.
  - WP7 arnés de regresión (`NQC_ET1_Regression.cs`, runbook `ninjatrader/runbooks/ET1_REGRESSION_RUNBOOK.md`): ESCRITO, NO EJECUTADO.
  - WP8 runner de portafolio en NT8 (lee `PORTFOLIO_SPEC`, escribe las salidas): PENDIENTE (necesita formato de genoma de ET3 y D-018).
  - WP9 compilar y ejecutar la regresión en NT8 (usuario): PENDIENTE.
- Antes de usarlo debe reproducir N1–N9 (8.110 operaciones), los 46 saltos y los 22 chequeos de ET1C (acta ET1 §6).
- Pendiente D-018 (biblioteca de condiciones) y D-019 (rangos de ATR).

## Decisiones tomadas en esta etapa
Aceptadas por el usuario el 2026-11-14 (interpretaciones del motor que EXECUTOR_SPEC 2.3.0 no fijaba):
1. Cierre diario de -US$750: se dispara con la marca M2M al cierre de barra y sale como salida forzada (ejecucion en la apertura siguiente).
2. Guardia de DD: cuenta continua desde la primera sesion, con reinicio tras PASS o FAIL.
3. MAE/MFE como proxy (extremos de barra sin cronologia): columnas `mae_proxy_usd` y `mfe_proxy_usd`.
Regla del usuario: toda entrega para instalar en NT8 se da como ZIP (generado con `tools/installer/build_zip.py`).

## Bitácora de traspasos
Formato: `AAAA-MM-DD · qué se hizo · artefactos/hash · qué queda · bloqueos`. Máximo 5 líneas por entrada.

- 2026-11-14 · Archivo creado al adoptar el modelo de agentes por etapa (`docs/WORK_MODEL.md`). Sin trabajo nuevo.
- 2026-11-14 · WP1–WP7 escritos: `NQC_ET1_Core.cs` (motor genérico puro) + `NQC_ET1_Regression.cs` + runbook; sin compilar ni ejecutar (no hay compilador aquí) · hashes en el commit · queda WP8/WP9 · bloquea: compilar/correr en NT8 (usuario).
