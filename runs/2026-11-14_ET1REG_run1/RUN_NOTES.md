# ET1REG run1 — regresión del motor genérico (2026-11-14)

Corrida del usuario en NT8 (NQ, M1, Last, Merge back adjusted, 2020-01-01 a 2022-12-31).

## Evidencia (líneas de consola de NT8, copiadas tal cual)
```
NQC_ET1REG_END status=PASS n1n9=8110/8110 gaps=46/46 sha=c7ec614192ae dir=...\NQC_Results\ET1REG_20261114_151605
NQC_ET1C_END status=PASS ok=22 falla=0 sha=b9f84a4e6a75 dir=...\NQC_Results\ET1C_20261114_151606
```
ET1C se corrió 4 veces más el mismo día (15:12, 15:15, 15:15, 15:16): `ok=22 falla=0`, `sha=b9f84a4e6a75` en todas (determinismo).

## Código corrido (SHA256)
- NQC_ET1_Core.cs `fb4ca71f6cacb7eb28843f9e0fa2a6b09e3a8bfa5fc55097b30bcf10d9408208`
- NQC_ET1_Regression.cs `e285352f6c69664d53cca501615d52007136a2729588fc1523c03e7e0c9d373a`
- NQC_ET1C_AccountSim.cs `aebe6fb1d3c9eb670c9f9ffe4455143b6638cfed4bcc69fd9ad517f0da5b061e`

## Resultado
Regresión PASS: N1–N9 8.110/8.110 operaciones iguales, 46/46 saltos, ET1C 22/22. Hash de ET1C idéntico al de la corrida aprobada de ET1 (`b9f84a4e…`).

## Detalle del informe (REGRESSION_REPORT.txt / SUMMARY.txt de la corrida de las 15:16)
- `report_sha256` completo: `c7ec614192ae4ec282e9f0d106e37968131697075039694c7a15d698dc2f79f5`.
- Barras: 1.054.234 (2020-01-01 18:01 a 2022-12-30 16:59). Calendario: 3.954 eventos, SHA `93d837ac…377d` (igual al aprobado).
- R1 indicadores: ATR 5/14/20/50 y EMA 5/13, diferencia máxima 0 en todas las barras.
- R2 paridad: N1 766, N2 705, N3 1.941, N4 492, N5 766, N6 762, N7 705, N8 1.207, N9 766; total 8.110/8.110, 0 solo-nativo, 0 solo-motor.
- R3 saltos: libro nativo SHA `e2aab056…75ca` verificado; 46/46 iguales, 46/46 con salto.
- R4 determinismo: dos corridas, hash de trades `760227dc…78ecbf` idéntico.
- R5 capa protectora y costos (sintéticos): 10/10 OK. R6 cuenta continua: OK. 11 chequeos sintéticos OK, 0 fallas.
- R2 zona de barras (empírico): 546/774 sesiones = **70,54%** (exigido ≥ 70%). Mismo margen estrecho que ET0 G4; la zona de la plantilla de horario figura como 'Central Standard Time' solo como dato informativo.
- Archivos guardados aquí: `REGRESSION_REPORT.txt`, `SUMMARY.txt`, `ENGINE_GAP.tsv`, `ENGINE_TRADES_SHA256.txt` (hashes de los 9 TSV de trades; los TSV completos, ~1 MB, no se suben).

## Limitaciones
- Primer intento de ET1REG abortó por un falso positivo de R2 (zona de la plantilla de horario, 'Central Standard Time'); se corrigió (ver `docs/stages/ET1.md`).
- El margen del chequeo empírico de zona (70,54% frente a 70%) es pequeño; es el mismo dato de ET0 G4, no una nueva debilidad.
- Archivos ET1C copiados de esta corrida: `ET1C_SUMMARY.txt`, `ET1C_SELFTEST_REPORT.txt`.
- `ET1REG_ABORT_primer_intento_151221.txt`: aborto del primer intento, conservado como evidencia.
