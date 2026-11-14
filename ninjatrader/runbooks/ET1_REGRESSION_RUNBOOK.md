# Regresión obligatoria del motor genérico (acta ET1 §6) — runbook

**Estado: NO EJECUTADO.** Código escrito sin compilar. Sin log de NT8 + hash, el motor genérico no se puede usar en ET2 y ningún estado avanza.

## Qué se prueba
Archivos: `NQC_ET1_Core.cs` (biblioteca pura, WP1–WP5) y `NQC_ET1_Regression.cs` (estrategia sin órdenes, WP7). Dependen de `NQC_ET1C_AccountSim.cs` (clases `NQC_AccountSim`, `NQC_AccountProfile`, `NQC_SessionRow`), que **debe estar instalado sin cambios**.

| Prueba | Exigencia | Dónde |
|---|---|---|
| R1 indicadores | ATR 5/14/20/50 y EMA 5/13 del motor = NT8 en todas las barras (0 barras con diferencia >1e-7) | `NQC_ET1_Regression` |
| R2 N1–N9 | **8.110 de 8.110** operaciones iguales (dirección, entrada, stop, nombre de salida, hora, precio), 0 de más/menos, con el motor **genérico** y capa protectora apagada | `NQC_ET1_Regression` |
| R3 saltos | **46 de 46** iguales y **46 de 46** con salto; `native_sha_ok=SI` (`e2aab056…475ca`) | `NQC_ET1_Regression` |
| R4 determinismo | dos corridas completas, mismo hash de TRADES de N1–N9 | `NQC_ET1_Regression` |
| R5 protectora/costos | 10 chequeos sintéticos del ejecutor (cierre diario, pausa, guardia DD, liberación al día siguiente, fricción una vez, estrés 1,5×) | `NQC_ET1_Regression` |
| R6 cuenta continua | tracker de la guardia = `RunFrom` encadenado (400 sesiones, semilla 7) | `NQC_ET1_Regression` |
| ET1C | **22 chequeos** (`checks_ok=22 checks_falla=0`, `status: PASS`, hash de informe `b9f84a4e…` como en ET1) | `NQC_ET1C_AccountSim` sin cambios |

## Pasos
1. Verifica que `NQC_ET1C_AccountSim.cs` sigue con SHA256 `aebe6fb1d3c9eb670c9f9ffe4455143b6638cfed4bcc69fd9ad517f0da5b061e` (sin cambios). Instala junto con `NQC_ET1_Core.cs` y `NQC_ET1_Regression.cs` (formato de ZIP/INSTALAR.cmd en `ninjatrader/README.md`). ZIP listo: `dist/NQC_ET1_Regression_pack.zip` (ignorado por git; se regenera con `tools/installer/build_zip.py`). SHA256 de los archivos nuevos: `NQC_ET1_Core.cs` = `fb4ca71f6cacb7eb28843f9e0fa2a6b09e3a8bfa5fc55097b30bcf10d9408208`; `NQC_ET1_Regression.cs` = `e285352f6c69664d53cca501615d52007136a2729588fc1523c03e7e0c9d373a`. NT8 debe estar en Eastern: el arnes lo comprueba empiricamente (pico de volumen 09:16-10:00 en barra 09:31/09:32 en >= 70% de las sesiones, como ET0 G4) y si no se cumple aborta con `ET1REG_ABORT_*.txt` (revisar Tools > Options > General > Time zone); la zona de la plantilla de horario se registra solo como dato informativo; una corrida con ultimo Time lejos de 2022-12-31 sale `PARCIAL`, no `FALLA`.
2. NinjaScript Editor → **F5**. Si hay errores de compilación, **envíalos tal cual**: no se corrige a ciegas.
3. Requisitos previos ya existentes: `Documents\NinjaTrader 8\NQC_Results\ET1A_N1_…` a `ET1A_N9_…` (libros nativos), `ET1A_GAP_…` y `NQC_Data\usd_high_medium_2020_2026.csv` (SHA `93d837ac…377d`).
4. **Strategy Analyzer** → `NQC_ET1_Regression`: NQ, Minute 1, Last, Merge back adjusted, **From 1/1/2020 To 12/31/2022**, plantilla habitual (igual que ET1B). Sin parámetros.
5. Output: `NQC_ET1REG_END status=… n1n9=8110/8110 gaps=46/46 sha=… dir=…`.
6. Corre `NQC_ET1C_AccountSim` (rango corto, p.ej. 2022-01-03 a 2022-01-07): `NQC_ET1C_END status=PASS ok=22 falla=0`.
7. Copia a `runs/<fecha>_ET1REG_run1/` solo `SUMMARY.txt` y `REGRESSION_REPORT.txt` (más `SUMMARY.txt` y `SELFTEST_REPORT.txt` de ET1C en `…_ET1C_regresion/`). No copies los `ENGINE_*.tsv`.

## Gate (todo o nada)
`status=PASS` en ET1REG **y** `status=PASS ok=22` en ET1C. Cualquier diferencia = el motor genérico no se usa; se corrige el motor y se repite completo. No se miran PF/beneficio/DD de N1–N9 (los TSV contienen `net_usd` de referencias triviales; no son hallazgos).

## Interpretaciones del motor que el master debe confirmar (no están en EXECUTOR_SPEC 2.3.0)
- Cierre diario (−US$750): se dispara con la marca M2M al cierre de barra y sale como **salida forzada** (orden a mercado al cierre, ejecución en la apertura siguiente), no al precio exacto de −750 como en el modelo de `account_sim.py`.
- Guardia de DD: usa una cuenta continua desde la primera sesión, con reinicio tras PASS/FAIL (D-007 no define el arranque).
- Costo abierto: la posición abierta se marca con el costo RT ya descontado.
