# FULLPACK run1 — paquete NQC_FULL_pack en NinjaTrader 8 (2026-11-16)

Usuario instaló `NQC_FULL_pack.zip` (30 .cs: ET1 corregido, ET2–ET7, runner), compiló con F5 y corrió las pruebas en Strategy Analyzer. Todas las pruebas, salvo ET1 Regression, usan datos SINTÉTICOS (sin mercado, sin resultados económicos).

## Resultados (SUMMARY.txt archivados en subcarpetas)
| Prueba | Resultado | Hash del informe |
|---|---|---|
| NQC_ET1_Regression (IS 2020–2022, Core corregido) | PASS: N1–N9 8.110/8.110, saltos 46/46, indicadores 0 dif., determinismo SI, 14 chequeos sintéticos OK (incl. R5.11 de DdM2m) | report `98303ce54280bdcf69a6b390f1d83d6291cf66e1c24db057068d765cd1c2a797`; `trades_hash_n1n9` `760227dc…78ecbf` **idéntico al anterior a la corrección** |
| NQC_ET1_Regression (rango corto, 4 sesiones) | PARCIAL (esperado: rango corto; R2 «no verificable») | `af463155…` |
| NQC_ET1C_AccountSim | PASS 22/22 en todas las corridas | `b9f84a4e6a75` |
| NQC_ET3_LibraryTest (NQCL-3.1.0) | PASS 44/44 | `4b7dde3f41ee…` |
| NQC_ET3_GaTest | PASS 15/15 | `148574836fad…` |
| NQC_ET2_DatasetTest | PASS 21/21 | N1 `c6644b2b…`, N2 `2f0ee6b8…`, INJ `0ff38c6c…` |
| NQC_ET4_PruneTest | PASS 19/19 | pool `4ce0c17e…`, rechazos `4a188f53…`, ledger `60d69b0f…`, reporte `c0272d24…` |
| NQC_ET5_RobustTest | PASS 13/13 | pool `ae1efcea…`, informe `d40518b4…` |
| NQC_ET6_PortfolioTest | PASS 22/22 | beam `046e4b38…`, spec F1 `7bb9f9b9…`, reporte `649fae45…` |
| NQC_ET7_EvidenceTest | PASS 35/35 | |
| NQC_RUN_FunnelTest | PASS 31/31 | digest `8084e84d984bc617…`, reporte `d309af0a…` |

## Lectura
- El paquete completo compila en NT8 y todas las pruebas pasan. Los hashes coinciden con los de la verificación previa en mono con un stub (ET2, ET3, ET4, ET5, ET7, RUN idénticos; ET6 coincide con mono tras la corrección de Core, `046e4b38…`/`7bb9f9b9…`): NT8 y mono se comportan igual.
- La corrección de `DdM2m` en ET1 Core (doble conteo del P&L cerrado) queda confirmada en NT8: el chequeo R5.11 pasa y las operaciones y su hash no cambian (`760227dc…`). En ET6, DD M2M del núcleo = 158,72 = valor correcto a mano.
- La regresión del motor con Core corregido: PASS en NT8 con datos reales del IS. Core `ea6c953c…`, Regression `616988b6…`.

## Limitaciones / defectos
- **NQC_RUN_Funnel se ejecuta al ser instanciada por NinjaTrader** (p. ej. al listar estrategias) y entra a `RunAll` en `State.Terminated` sin haber cargado datos: imprime `NQC_RUN_ERROR RUN: sin barras` y deja un `RUN_ERROR_<fecha>.txt` en `NQC_Results` (23 archivos en esta sesión; ejemplo guardado aquí). Inofensivo, pero es ruido y una fuente de riesgo si algún día se instanciara con barras sin quererlo. Corrección delegada: ejecutar `RunAll` solo si la estrategia llegó a `State.DataLoaded` con barras.
- Todo es sintético salvo ET1 Regression: no valida nada del mercado. ET2 no se ha ejecutado.
- Los TSV de trades del motor (ENGINE_N*_TRADES.tsv) no se archivan (~1 MB); hay hashes en `runs/2026-11-14_ET1REG_run1/`.
