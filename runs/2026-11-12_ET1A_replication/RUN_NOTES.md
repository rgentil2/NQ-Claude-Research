# ET1A — réplica fuera de línea de las referencias nativas — 2026-11-12/13

**Entradas:** los libros `TRADES_NATIVE.tsv` de N1–N9 (8.110 operaciones) y `BARS_WINDOW.csv` (95.920 barras M1 de 09:29 a 11:32 de 774 días del IS, con ATR y EMA calculados por NT8; SUMMARY en `runs/` del usuario). **Herramienta:** `tools/lab/replicate_native_refs.py`.

## Resultado
La réplica reproduce **las 8.110 operaciones de las nueve referencias, sin una sola diferencia**, en dirección, precio de entrada, stop, nombre de salida, hora de salida y precio de salida. También coinciden el número de operaciones y cada señal: ni una operación de más ni de menos. Antes de la réplica se comprobó que el precio de entrada es la apertura de la barra siguiente (766 de 766), que el ATR exportado coincide con el de la señal (766 de 766) y que stop y objetivo siguen la fórmula del tick (100%).

## Reglas de NT8 que quedaron demostradas
| Regla | Evidencia |
|---|---|
| Entrada a mercado enviada al cierre de la barra: precio = apertura de la barra siguiente | 8.110 de 8.110 |
| Stop y objetivo = precio de entrada ± k × ATR de la señal, redondeado al tick más cercano; activos desde la barra de entrada | 100% |
| El **stop se activa al tocar** el precio (mínimo ≤ stop en largos) | 148 stops con mínimo exactamente igual al stop |
| El **objetivo (orden límite) exige atravesar** el precio (máximo > objetivo); tocarlo no basta | N9 solo da 100% con esta regla (98,4% con "tocar") |
| Si caben stop y objetivo en la misma barra gana el **extremo más cercano a la apertura**; en empate, el máximo primero | N5: 99,1% con empate al mínimo, 100% con empate al máximo (7 empates exactos) |
| Precio de salida del stop = precio del stop; del objetivo = precio del límite | 3.783 y 1.931 casos |
| Salidas forzadas (11:30, 10:30, noticias): orden a mercado al cierre de la barra, ejecutada en la apertura de la siguiente, antes de evaluar stop u objetivo | 100% de N2, N3, N4, N6, N7, N8 |
| Al cerrarse una posición dentro de una barra, al cierre de esa barra puede entrar otra señal | N3 y N8 |
| ATR y EMA son los de NT8; el cruce usa el valor actual y el anterior | N3 y todas las referencias con ATR |

## Lo que estos datos NO probaron
En las 3.783 salidas por stop ninguna barra abrió más allá del stop, así que **no se sabe qué precio da NT8 cuando hay un salto sobre el stop**. Se construyó una sonda de 46 casos reales del IS en los que la barra siguiente a la de entrada abre más allá del stop (22 largos y 24 cortos, k de 0,3 a 1,0). Predicción de la réplica: con la regla "precio del stop" y con la regla "apertura de la barra" los precios difieren en los 46 casos, así que la sonda decide.

## Aplicación al ejecutor
EXECUTOR_SPEC pasa a 2.2.0 con estas reglas. La regla de salto queda pendiente de la sonda.
