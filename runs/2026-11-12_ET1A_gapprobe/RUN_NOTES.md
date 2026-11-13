# ET1A sonda de saltos — 2026-11-12 — EJECUTADA

Script `NQC_ET1A_GapProbe.cs` ET1A-GAP-1.0.0. 46 operaciones puntuales del IS en las que la barra siguiente a la de entrada abre más allá del stop (22 largos y 24 cortos, k = 0,3 a 1,0 de ATR(14)). `trades_sha256` `e2aab0565b45ea228ecd38fedcf5ea38031d647a7291f9a47d299182f41475ca`.

## Resultado
- **Las 46 salidas ocurrieron al precio de apertura de la barra que abre más allá del stop; ninguna al precio del stop.** NT8 modela el salto: el stop sale peor que su precio.
- Entrada, stop y barra de salida coinciden con la predicción en 46 de 46. Todas salieron como `SL`.
- Cuánto peor que el stop: mínimo 0,25 puntos, mediana 1,0, media 1,6 (largos 2,0; cortos 1,2), máximo 16,0 puntos. Equivale a una media de ~US$32 por salto y un máximo de US$320 con 1 NQ.
- La réplica con la regla "apertura" sigue dando 100% en las nueve referencias (en ellas no hay saltos).

## Regla fijada
Si la barra abre más allá del stop, la salida es al precio de apertura de esa barra. Si solo lo toca, sale al precio del stop. Aplica a largos y cortos. Queda registrada en EXECUTOR_SPEC 2.3.0.

## Lectura
En la ventana los saltos son raros (3.783 salidas por stop en las nueve referencias sin ninguno), pero cuando ocurren cuestan más que el stop. Con stops ajustados y volatilidad alta pueden pesar; el costo fijo de US$24,68 no los cubre por completo.
