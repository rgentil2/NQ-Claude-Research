# Especificación del ejecutor central y del simulador de cuenta

Versión de especificación: 1.0.0 (ligada a NQC 1.0.0). Cualquier cambio de semántica = cambio de versión + repetir pruebas ET1.

## 1. Barras y tiempo
- Datos: NQ, Last, M1, sesión CME US Index Futures ETH, zona ET, según `DATA_MANIFEST` de ET0.
- Una señal se evalúa al **cierre** de una barra completada; la entrada se simula a la **apertura de la barra siguiente**.
- Ventana operativa: pendiente D-010. Toda posición abierta se cierra a la hora de salida forzada definida en la ventana.
- Días excluidos por ET0: no se generan señales ni se abren posiciones.

## 2. Posición y conflictos
- Máximo **1 NQ** abierto en todo momento, en todo el portafolio.
- Si hay posición abierta, toda señal nueva se **descarta** (no se encola) y se registra con motivo `BUSY`.
- Señales simultáneas en la misma barra: gana la de mayor prioridad. La prioridad es un orden fijo por portafolio, guardado en `PORTFOLIO_SPEC.json` (propuesta D-016: por fitness en VAL).

## 3. Salidas
- Stop fijo o ATR según genoma; el ATR se calcula con datos disponibles al cierre de la barra de señal.
- Si SL y TP caen en la misma barra: **se asume SL primero**.
- Gap que salta el stop: se ejecuta al precio de apertura de la barra (peor que el stop).
- Salida por tiempo según genoma o ventana.

## 4. Costos y riesgo
- Slippage: 2 ticks en entrada y 2 en salida (US$5/tick); comisión US$4,68 RT. Total US$24,68, aplicado **una sola vez** por operación.
- Estrés: 1,5× = US$37,02.
- Riesgo planificado: `stopTicks × 5 + 24,68 ≤ 200` → stop máximo 35 ticks (8,75 puntos). Si no se cumple, la señal se descarta con motivo `RISK`.

## 5. Capa protectora (propuesta D-007)
| Control | Disparo | Efecto | Liberación |
|---|---|---|---|
| Pausa diaria | P&L de la sesión ≤ –US$500 | No hay nuevas entradas | Fin de la sesión |
| Cierre diario | P&L de la sesión ≤ –US$750 | Cierra la posición abierta y bloquea entradas | Fin de la sesión |
| Guardia de DD | Colchón hasta el MLL simulado ≤ US$400 | No hay nuevas entradas | Fin de la sesión; se reevalúa en la siguiente |

**Ningún bloqueo es permanente.** Para diagnóstico existe el modo `LOCK_MODE=PERMANENT_DIAGNOSTIC`, que nunca se usa para aceptar un portafolio.

## 6. Contabilidad
- **DD cerrado:** pico a valle de la equity de operaciones cerradas.
- **DD M2M M1:** pico a valle de la equity marcada al cierre de cada barra M1, con posición abierta incluida.
- **Saldo EOD:** saldo al cierre de cada sesión (para el simulador de cuenta).
- El proxy MAE/MFE sin cronología **no** se usa para disparar controles; si se reporta, va etiquetado como proxy.

## 7. Métricas
- **Stability:** 100 × R² de la regresión OLS de la equity neta diaria (incluye días válidos sin operaciones) contra el índice de día; 0 si la pendiente no es positiva.
- **Symmetry:** si el neto Long > 0 y el neto Short > 0: 100 × min/max; si no, 0.
- **P_pass:** para cada sesión de inicio posible, se simula la cuenta desde saldo cero; resultado `PASS` si alcanza el objetivo cumpliendo días mínimos y consistencia antes de tocar el MLL; `FAIL` si toca el MLL; `CENSORED` si se acaban los datos. P_pass = PASS / (PASS + FAIL). Se reportan también los censurados y la duración media. Los arranques se solapan: no tratarlos como independientes.

## 8. Simulador de cuenta MFFU
Parámetros leídos de configuración (valores en `ACCOUNT_RULES_MFFU.md`): objetivo, MLL, tipo de trailing, lock, días mínimos, consistencia, límite diario. El simulador recibe la secuencia de operaciones ejecutadas y la equity por barra; no genera operaciones.

## 9. Salidas de auditoría (obligatorias en toda corrida)
- `TRADES.tsv`: portafolio, miembro, barra de señal, entrada, salida, dirección, stop en ticks, riesgo US$, motivo de salida, MAE, MFE, neto.
- `SIGNALS_WATERFALL.tsv`: por portafolio, mes y miembro: señales brutas, fuera de ventana, `BUSY`, `RISK`, pausa diaria, cierre diario, guardia DD, día excluido, ejecutadas.
- `PROTECT_EVENTS.tsv`: tipo, timestamp, P&L de sesión, saldo, pico, DD cerrado, DD M2M, colchón al MLL, hora de liberación.
- `ACCOUNT_SIM.tsv`: por arranque: fecha, resultado, días, mejor día, razón de fallo.
- `SUMMARY.txt`: versión de contrato y de esta especificación, hash del `.cs`, parámetros, rango, conteos, resultado del gate.
