# Especificación del ejecutor central y del simulador de cuenta

Versión de especificación: **2.1.0** (ligada a NQC 2.0.0; la 2.1.0 agrega el blackout de noticias antes de ejecutar ET1). Cualquier cambio de semántica = cambio de versión + repetir pruebas ET1.

## 1. Barras, tiempo y ventana
- Datos: NQ, Last, M1, serie Merge back adjusted con el roll de NT8 (R1); zona horaria de NinjaTrader (Eastern) (R2); sesión y días según `DATA_MANIFEST` de ET0.
- NinjaTrader sella cada barra con su **hora de cierre**: la barra con sello 09:31 cubre 09:30:00–09:31:00. La ventana son los sellos 09:31 a 11:30 (120 barras).
- Una señal se evalúa al **cierre** de una barra completada; la entrada se simula a la **apertura de la barra siguiente** (el mismo instante del cierre).
- **Sin hora límite de entrada (D-010):** se permite cualquier entrada cuyo instante sea anterior a las 11:30:00. Toda posición abierta se cierra a las 11:30:00 ET al precio de cierre de la barra con sello 11:30. No se abren entradas con instante ≥ 11:30:00.
- Días excluidos por ET0 (INCOMPLETA o SIN_VENTANA): no se generan señales ni se abren posiciones. No se excluyen sesiones de roll.
- Una **sesión** es la ventana 09:30–11:30 de un día; los controles de D-007 se liberan al terminar esa ventana.
- **Noticias (D-027):** el blackout de ±5 minutos de `docs/NEWS_BLACKOUT.md` se aplica antes que cualquier otra regla de entrada.

## 2. Posición y conflictos
- Máximo **1 NQ** abierto en todo momento, en todo el portafolio.
- Si hay posición abierta, toda señal nueva se **descarta** (no se encola) y se registra con motivo `BUSY`.
- Una señal con instante de entrada dentro de un blackout de noticias se descarta con motivo `NEWS`.
- Señales simultáneas en la misma barra: gana la de mayor prioridad. La prioridad es un orden fijo por portafolio, guardado en `PORTFOLIO_SPEC.json`, fijado por fitness (D-016).

## 3. Salidas (D-021)
- **Stop = k_sl × ATR(periodo)**, obligatorio. **Objetivo = k_tp × ATR(periodo)**, opcional: si el genoma no lo tiene, la operación sale por stop, por el plano de las 11:30 o por una noticia.
- El periodo, k_sl y k_tp los genera la evolución (ET3). No hay stops ni objetivos en ticks fijos.
- **ATR:** el indicador ATR de NinjaTrader (suavizado de Wilder) sobre la serie M1 completa, incluidas las barras previas a las 09:30. Se calcula al cierre de la barra de señal; el stop y el objetivo se fijan en ese momento y se redondean al tick (0,25 puntos). ET1 P1 verifica que el ATR del motor coincide con el de NT8.
- Si SL y TP caen en la misma barra: **se asume SL primero**.
- Gap que salta el stop: se ejecuta al precio de apertura de la barra (peor que el stop).
- Salida forzada a las 11:30:00 como se indica en §1.
- **Salida por noticia (`NEWS_FLAT`):** a `t−5:00` de un evento, cierre a mercado al precio de cierre de la barra que termina en ese instante, con los costos habituales.
- **Convención de fills de órdenes límite (objetivo):** se determina en ET1 P1 contra NT8 nativo y se congela.

## 4. Costos (D-006) y riesgo (D-006b)
- Slippage: 2 ticks en entrada y 2 en salida (US$5/tick); comisión US$4,68 RT. Total US$24,68, aplicado **una sola vez** por operación como cargo fijo.
- Estrés: 1,5× = US$37,02.
- **No hay tope de riesgo por entrada.** Ninguna señal se descarta por el tamaño de su stop. El riesgo se controla después: capa protectora (§5), peor día ≤ US$1.000 (que implica un límite a la peor operación), DD ≤ US$2.000 y P_pass.
- Referencia nativa en Strategy Analyzer: slippage 2 ticks y comisión equivalente a US$4,68 por viaje redondo; P3 verifica que el total por operación coincide.

## 5. Capa protectora (D-007, aprobada)
| Control | Disparo | Efecto | Liberación |
|---|---|---|---|
| Pausa diaria | P&L de la sesión ≤ –US$500 | No hay nuevas entradas | Fin de la sesión |
| Cierre diario | P&L de la sesión ≤ –US$750 | Cierra la posición abierta y bloquea entradas | Fin de la sesión |
| Guardia de DD | Colchón hasta el MLL simulado ≤ US$400 | No hay nuevas entradas | Fin de la sesión; se reevalúa en la siguiente |

**Ningún bloqueo es permanente.** Para diagnóstico existe el modo `LOCK_MODE=PERMANENT_DIAGNOSTIC`, que nunca se usa para aceptar un portafolio. Estos controles actúan después de una pérdida: una sola operación con stop ancho puede superar los umbrales antes de que se disparen, y eso lo miden las puertas de ET6 y el simulador de cuenta.

## 6. Contabilidad
- **DD cerrado:** pico a valle de la equity de operaciones cerradas.
- **DD M2M M1:** pico a valle de la equity marcada al cierre de cada barra M1, con posición abierta incluida.
- **Saldo EOD:** saldo al cierre de cada sesión (para el simulador de cuenta).
- El proxy MAE/MFE sin cronología **no** se usa para disparar controles; si se reporta, va etiquetado como proxy.

## 7. Métricas
- **Stability:** 100 × R² de la regresión OLS de la equity neta diaria (incluye días válidos sin operaciones) contra el índice de día; 0 si la pendiente no es positiva. Se calcula sobre el **portafolio ejecutado** (D-022).
- **Symmetry:** si el neto Long > 0 y el neto Short > 0: 100 × min/max; si no, 0. Se calcula sobre el **portafolio ejecutado** (D-022).
- **P_pass (D-008):** para cada sesión de inicio posible, se simula la cuenta desde saldo cero; resultado `PASS` si alcanza el objetivo cumpliendo días mínimos y consistencia antes de tocar el MLL; `FAIL` si toca el MLL; `CENSORED` si se acaban los datos. P_pass = PASS / (PASS + FAIL). Se reportan también los censurados y la duración media. Los arranques se solapan: no tratarlos como independientes.

## 8. Simulador de cuenta MFFU
Parámetros leídos de configuración con el perfil elegido (D-024; valores y perfiles en `ACCOUNT_RULES_MFFU.md`): objetivo, MLL, tipo de trailing, lock, días mínimos, consistencia, límite diario, comprobación del MLL durante la sesión (D-025) y día de operación = al menos una operación (D-026). El simulador recibe la secuencia de operaciones ejecutadas y la equity por barra; no genera operaciones.

## 9. Salidas de auditoría (obligatorias en toda corrida)
- `TRADES.tsv`: portafolio, miembro, barra de señal, entrada, salida, dirección, ATR, k_sl, k_tp (o vacío), stop en puntos y en US$, motivo de salida (`SL`, `TP`, `TIME_1130`, `NEWS_FLAT`, `DAILY_CLOSE`), MAE, MFE, neto.
- `SIGNALS_WATERFALL.tsv`: por portafolio, mes y miembro: señales brutas, fuera de ventana, `NEWS`, `BUSY`, pausa diaria, cierre diario, guardia DD, día excluido, ejecutadas.
- `PROTECT_EVENTS.tsv`: tipo, timestamp, P&L de sesión, saldo, pico, DD cerrado, DD M2M, colchón al MLL, hora de liberación.
- `ACCOUNT_SIM.tsv`: por arranque: fecha, resultado, días, mejor día, razón de fallo.
- `SUMMARY.txt`: versión de contrato y de esta especificación, hash del `.cs`, hash del calendario de noticias, parámetros, rango, conteos, resultado del gate.

## 10. Unidades de las condiciones (R5)
Toda condición del generador se expresa en múltiplos de ATR, ticks o puntos relativos a un nivel calculado en la propia sesión o en datos previos. No se usan niveles absolutos ni porcentajes de precio, porque la serie back-adjusted desplaza los precios antiguos.

## 11. Noticias
Regla, datos y aceptación del calendario: `docs/NEWS_BLACKOUT.md`. El motor verifica el SHA256 del archivo al cargar y lo escribe en el `SUMMARY.txt`.
