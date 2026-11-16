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
- 2026-11-14 · Correcciones de revision estatica: zona Eastern exigida y registrada (aborta si no), instName copiado en DataLoaded, estado PARCIAL, NEWS evaluado antes de OUT_WINDOW (no altera trades ni R5), hash sin nombre de carpeta, NextInt valida n, MAE/MFE en dos lineas. Sin compilar · ZIP `dist/NQC_ET1_Regression_pack.zip` · Core fb4ca71f…8208, Regression e285352f…373a, ET1C aebe6fb1 sin cambios · bloquea: compilar/correr en NT8.
- 2026-11-14 · Falso positivo R2 en NT8 (aborto por zona 'Central'): Bars.TradingHours.TimeZoneInfo es la zona de la plantilla de horario, no la de Time[0]. Quitado el aborto (valor solo informativo); sustituido por chequeo empirico: pico de volumen 09:16-10:00 en barra 09:31/09:32 >= 70% de sesiones (como ET0 G4), sin volumen utilizable se reporta sin abortar. Sin compilar · Regression e285352f…373a, Core fb4ca71f sin cambios, ET1C aebe6fb1 sin cambios (ET1C ya PASS ok=22 sha b9f84a4e6a75) · bloquea: recompilar/correr en NT8.

## Notas para el orquestador (sin cambio de codigo)
- Precedencia de salida forzada TIME > NEWS > DAILY_CLOSE: el motor la aplica asi; EXECUTOR_SPEC 2.3.0 no la fija. Proponer fijarla.
- Etiqueta `NEWS` (TRADES, linea 62) vs `NEWS_FLAT` (linea 30) inconsistente en la spec.
- Redondeo bancario en empates exactos: identico a ET1B.

- Leccion (2026-11-14): la zona de la plantilla de horario (TradingHours.TimeZoneInfo) no es la zona de las barras; no usarla para R2. Verificar la zona empiricamente (pico de volumen 09:31 como ET0 G4).
- 2026-11-14 · Usuario corrió la regresión en NT8: ET1REG PASS 8.110/8.110, 46/46 (sha c7ec614192ae); ET1C 22/22 x5 con sha b9f84a4e6a75. Registro en `runs/2026-11-14_ET1REG_run1/`. Queda: WP8, y pedir `REGRESSION_REPORT.txt` para completar el registro.
- 2026-11-14 · Informe completo de ET1REG archivado en `runs/2026-11-14_ET1REG_run1/` (indicadores 0 dif., 8.110/8.110, 46/46, determinismo, R5/R6 OK; zona de barras 70,54%). Registro cerrado. Queda: WP8 y código ET3–ET7.
- 2026-11-14 · Correccion de defecto sin ver resultados (contrato §3): `NQCE_Executor.DdM2m` contaba doble el P&L cerrado de la sesion (CloseTrade ya suma a cumReal y eq=sessReal lo incluye). Ahora M2M acumulado = (cumReal - sessReal) + eq, tambien en el evento de auditoria (`ProtectEvent.DdM2m`). Alcance: DdM2m era informativo; no es gate de ET1 ni lo usan R1-R6/N1-N9; trades, TRADES y DdClosed no cambian (sinteticos R5.1-10 identicos antes/despues). Nuevo R5.11 (a mano: 874,04; con el defecto da 1698,72). Core ea6c953c…2272, Regression 616988b6…3f3e. Pendiente: el usuario repite la regresion en NT8 (cambia el hash de Core); sin ZIP.
- 2026-11-16 · Usuario corrió el paquete completo en NT8: ET1_Regression con Core corregido PASS (8.110/8.110, 46/46, hash de operaciones idéntico `760227dc…`, R5.11 OK); ET1C 22/22. Registro: `runs/2026-11-16_FULLPACK_run1/`.
