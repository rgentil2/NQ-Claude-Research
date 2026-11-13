# ET1A nativas run1 — 2026-11-12 — EJECUTADA

Script `NQC_ET1A_NativeRefs.cs` ET1A-1.0.0 (SHA256 `55259a5db9580fa06355004b6c32115ef3bff6123069cca0b70dfb12c8d47503`). Compiló y corrió las nueve referencias sobre el IS (2020-01-01 a 2022-12-30; 1.054.234 barras; calendario `OK 3954 eventos`; 25 días excluidos). Los resultados económicos no se miran.

| Ref | Operaciones | SL | TP | TIME | NEWS | SHA256 del libro (12) |
|---|---|---|---|---|---|---|
| N1 | 766 | 502 | 247 | 0 | 17 | adf51c761101 |
| N2 | 705 | 440 | 205 | 2 | 58 | 5dcf42adfb5d |
| N3 | 1.941 | 1.107 | 0 | 633 | 201 | 0c17345654a7 |
| N4 | 492 | 269 | 0 | 140 | 83 | 27be6a1d9491 |
| N5 | 766 | 407 | 359 | 0 | 0 | f3a16d4a119a |
| N6 | 762 | 0 | 0 | 762 | 0 | b294aee574f9 |
| N7 | 705 | 248 | 0 | 257 | 200 | 7241600e2386 |
| N8 | 1.207 | 613 | 551 | 0 | 43 | 6cdcb3f5f6a7 |
| N9 | 766 | 197 | 569 | 0 | 0 | 91cb343a13fd |

Consistencias internas: las 766 entradas de N1, N5, N8(A) y N9 coinciden; N8 tiene 766 de A y 441 de B; N6 sale 762 de 762 por tiempo.

## Convenciones de NT8 observadas en los libros (sin barras todavía)
1. **Fill de mercado:** una orden enviada al cierre de la barra de señal se ejecuta con el sello de la barra siguiente (señal 09:35:00 → ejecución 09:36:00), es decir, en su apertura. Se cumple en 766 de 766 entradas de N1 y en todas las demás.
2. **Stop y objetivo activos en la propia barra de entrada.** 765 de 766 operaciones de N5 salen en la barra de entrada; en N1, 199.
3. **Precio de salida del stop = precio del stop en 3.783 de 3.783 casos**; **precio del objetivo = precio del límite en 1.931 de 1.931**. Sin deslizamiento hacia peor ni mejora de precio.
4. **Salidas forzadas:** la de las 11:30 se ejecuta con sello 11:31 (762 de 762 en N6); la de las 10:30 con sello 10:31 (257 de 257 en N7); las de noticias con el sello del minuto siguiente a `t−5` (por ejemplo, evento 10:00 → 09:56).
5. Todos los precios son múltiplos de 0,25.

## Pendiente de las barras (siguiente paso, A2)
- Qué sale primero cuando stop y objetivo caben en la misma barra (N5).
- Qué pasa cuando una barra abre más allá del stop (el libro nativo nunca muestra peor precio).
- Si una orden límite se ejecuta al tocar el precio o solo al atravesarlo (N9).
- Que el precio de entrada es la apertura de la barra siguiente.
Para responderlo se exportan las barras de la ventana con los valores de ATR y EMA de NT8 y se replica el libro fuera de línea (`NQC_ET1A_BarsExport.cs`).
