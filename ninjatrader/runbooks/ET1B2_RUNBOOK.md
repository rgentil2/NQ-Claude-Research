# ET1 B2 — paridad del motor en saltos sobre el stop (cierre C1): instrucciones de ejecución

Script: `ninjatrader/src/NQC_ET1B2_GapParity.cs` (ET1B2-1.0.0, SHA256 `9b5839fb4d58c8fe641df581f8e32dc009ab4ace2f0824074af4e8e050878e19`).
Qué hace: sin órdenes. Simula con el motor propio las 46 operaciones de la sonda de saltos de ET1A y las compara una por una con el libro nativo de NT8 (`ET1A_GAP_*\TRADES_NATIVE.tsv`, SHA256 registrado `e2aab056…475ca`).

## Antes de empezar
- La carpeta `Documents\NinjaTrader 8\NQC_Results\ET1A_GAP_<fecha>` de la sonda ya existe (no se borra).
- NT8 en zona Eastern, serie NQ **Merge back adjusted**, igual que en ET1B.

## Pasos
1. Descomprime el ZIP y ejecuta `INSTALAR.cmd` (`OK. Instalado en: ...`).
2. NinjaScript Editor → **F5** (*Compile successful*).
3. **Strategy Analyzer** → `NQC_ET1B2_GapParity`. NQ, **Minute 1**, **Last**, **From 1/1/2020 To 12/31/2022** (el mismo rango que ET1B, para que el ATR de Wilder arranque igual), plantilla habitual. Sin parámetros.
4. Output: `NQC_ET1B2_END status=... equal=../46 gapped=../46 sha=... dir=...`.
5. Abre `NQC_Results\ET1B2_<fecha_hora>\` y envía la línea, `SUMMARY.txt` y `PARITY_REPORT.txt`. Copia a `runs/<fecha>_ET1B2_run1/` solo `SUMMARY.txt` y `PARITY_REPORT.txt`.

## Gate
`status=PASS` exige: ATR14 propio = NT8 en todas las barras; `native_sha_ok=SI`; 46 de 46 operaciones iguales en dirección, entrada, stop, nombre de salida, hora y precio; y **46 de 46 con salto** (salida a la apertura de la barra, distinta del precio del stop). Si hay diferencias, el informe muestra hasta 5.

## Qué cierra
C1 del acta `docs/ET1_ACTA_CIERRE.md`: el caso *gap adverso* de P1 demostrado contra el motor, no solo contra NT8.
