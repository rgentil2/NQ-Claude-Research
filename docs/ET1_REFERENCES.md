# ET1 — Referencias nativas y convenciones de paridad

ET1 se hace en partes. Ninguna mira resultados económicos.

| Parte | Qué es | Estado |
|---|---|---|
| **A** | Estrategias nativas N1–N9 con órdenes de NT8 sobre el IS (`NQC_ET1A_NativeRefs.cs`) | **Ejecutada** 2026-11-12 (`runs/2026-11-12_ET1A_native_run1/`) |
| **A2** | Exportación de barras (`NQC_ET1A_BarsExport.cs`) y réplica fuera de línea | **Ejecutada**: réplica exacta de las 8.110 operaciones (`runs/2026-11-12_ET1A_replication/`) |
| **A3** | Sonda de saltos sobre el stop (`NQC_ET1A_GapProbe.cs`, 46 casos) | Entregada; falta ejecutarla |
| **B** | Motor propio, simulador de cuenta y comparador dentro de NinjaTrader | Pendiente |
| **C** | Autopruebas P2 a P6 (casos límite, costos, capa protectora, cuenta, determinismo) | Pendiente |

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

Los multiplicadores son múltiplos de ATR. El ATR es el indicador de NT8 evaluado al cierre de la barra de señal.

## Reglas comunes
- Señal al cierre de una barra; entrada con orden a mercado ejecutada en la apertura de la barra siguiente. Entradas con instante entre 09:31 y 11:29.
- **Noticias:** si el instante de cierre de la barra cae en `[t−5, t+5)` de un evento no se entra, y una posición abierta se cierra con orden a mercado enviada al cierre de esa barra (motivo `NEWS`).
- **Plano:** a las 11:30 una posición abierta se cierra igual (motivo `TIME`; también a las 10:30 en N7).
- Sin operación ni señal en los 25 días excluidos por ET0 ni después de 2022-12-31.

## Convenciones de NT8 demostradas (ver `runs/2026-11-12_ET1A_replication/RUN_NOTES.md`)
1. Entrada a mercado: precio = apertura de la barra siguiente.
2. Stop y objetivo: precio de entrada ± k × ATR de la señal, al tick más cercano, activos desde la barra de entrada.
3. **El stop se activa al tocar; el objetivo exige atravesar** el precio.
4. Con ambos posibles en la misma barra gana el extremo más cercano a la apertura; empate, el máximo.
5. Precio de salida del stop = precio del stop; del objetivo = precio del límite.
6. Salidas forzadas: ejecutadas en la apertura de la barra siguiente al cierre en que se deciden, antes de evaluar stop u objetivo.
7. ATR y EMA del motor deben igualar a los de NT8 (a verificar en la parte B).

**Pendiente:** precio de ejecución de un stop cuando la barra abre más allá (sonda A3).

## Aclaración de P3: paridad en precios brutos
NT8 no cobra slippage en las órdenes límite, así que un costo fijo de US$24,68 no puede igualarse operación por operación con NT8 configurado con slippage de 2 ticks. La paridad se mide en **precios brutos con slippage 0** y el costo fijo (D-006) se aplica después, igual en el motor y en el informe de NT8. La intención de P3 se mantiene: el costo se aplica una vez por operación y de la misma forma en ambos lados.
