# ET1 — Referencias nativas y convenciones de paridad

ET1 se hace en tres partes. Ninguna mira resultados económicos.

| Parte | Qué es | Estado |
|---|---|---|
| **A** | Estrategias nativas N1–N9 con órdenes de NT8 sobre el IS: el "libro de operaciones" que NT8 considera verdadero | Script entregado (`NQC_ET1A_NativeRefs.cs`, ET1A-1.0.0) |
| **B** | Motor propio, simulador de cuenta MFFU y comparador contra las operaciones nativas | Pendiente de A |
| **C** | Autopruebas P2 a P6 (casos límite, costos, capa protectora, cuenta, determinismo) | Pendiente de B |

## Configuración en Strategy Analyzer (obligatoria)
NQ, Minute 1, Last, Merge back adjusted; **2020-01-01 a 2022-12-31** (las barras posteriores se ignoran); **Slippage 0 y comisión ninguna**; sin optimizar. El archivo `usd_high_medium_2020_2026.csv` debe estar en `Documents/NinjaTrader 8/NQC_Data/` o `NQC Data/`; el script verifica su SHA256 y no opera si falta o es distinto.

## Las referencias

| Ref | Señal | ATR | Stop | Objetivo | Otros |
|---|---|---|---|---|---|
| N1 | Long a las 09:35 | 14 | 1,5 | 3,0 | 1 por día |
| N2 | Short a las 09:40 | 20 | 2,0 | 4,0 | 1 por día |
| N3 | Cruce EMA(5) sobre EMA(13), long | 14 | 2,0 | ninguno | Varias por día |
| N4 | Primer cierre sobre el máximo de 09:30–09:45, long | 14 | 2,0 | ninguno | 1 por día |
| N5 | Long a las 09:35 | 5 | 0,3 | 0,3 | Provoca stop y objetivo en la misma barra |
| N6 | Long a las 11:29 | 14 | 3,0 | ninguno | Prueba el plano de las 11:30 |
| N7 | Long a las 09:40 | 50 | 6,0 | ninguno | Salida por hora a las 10:30 |
| N8 | A: long 09:35 (1,5 / 3,0). B: short 09:35 y 09:38 (2,5 / 1,0) | 14 | según miembro | según miembro | Prioridad A sobre B; B descartada si hay posición |
| N9 | Long a las 09:35 | 14 | 1,5 | 0,5 | Fija la convención de órdenes límite |

Los multiplicadores son múltiplos de ATR. El ATR es el indicador de NT8 evaluado al cierre de la barra de señal. Stop y objetivo se calculan sobre el **precio de entrada ejecutado** y se redondean al tick.

## Reglas comunes
- Señal al cierre de una barra; entrada con orden a mercado, que NT8 ejecuta en la apertura de la barra siguiente. Entradas con instante entre 09:31 y 11:29.
- **Noticias:** si el instante de cierre de la barra cae en `[t−5, t+5)` de un evento del calendario no se entra, y una posición abierta se cierra a mercado con motivo `NEWS`.
- **Plano:** a las 11:30 una posición abierta se cierra a mercado con motivo `TIME` (también a las 10:30 en N7).
- Sin operación ni señal en los 25 días excluidos por ET0 (incluidos en el código) ni después de 2022-12-31.
- Nombres de las órdenes de salida: `SL`, `TP`, `TIME`, `NEWS`.

## Convenciones que NT8 debe responder (se fijan con estos datos)
1. Precio y barra de ejecución de la entrada a mercado.
2. Si stop y objetivo ya están activos dentro de la propia barra de entrada.
3. Qué sale primero cuando stop y objetivo caben en la misma barra (N5).
4. Si una orden límite se ejecuta al tocar el precio o solo al atravesarlo (N9).
5. Precio y barra de las salidas a mercado forzadas (11:30, 10:30, noticias).
6. Si el ATR del motor coincide con el de NT8.

## Aclaración de P3: paridad en precios brutos
El contrato dice que el costo total por operación debe coincidir con NT8 configurado con slippage de 2 ticks y comisión. NT8 no cobra slippage en las órdenes límite, así que esa comparación no puede cumplirse operación por operación. La paridad se mide en **precios brutos con slippage 0** y el costo fijo de US$24,68 (D-006) se aplica después, igual en el motor y en el informe de NT8. La intención de P3 se mantiene: el costo se aplica una vez por operación y de la misma forma en ambos lados.

## Qué enviar
Para cada ref (N1–N9): la línea `NQC_ET1A_END` de la ventana Output, `SUMMARY.txt` y `TRADES_NATIVE.tsv`. No se interpretan los resultados económicos.
