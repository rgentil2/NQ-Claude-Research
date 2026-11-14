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

## Limitaciones
- El usuario entregó solo las líneas de consola de ET1REG (hash truncado a 12 caracteres), no `SUMMARY.txt` ni `REGRESSION_REPORT.txt` de esa corrida. Faltan por ver los indicadores, los 10 chequeos del ejecutor, el chequeo empírico de zona y el determinismo interno. Se piden para completar el registro.
- Primer intento de ET1REG abortó por un falso positivo de R2 (zona de la plantilla de horario, 'Central Standard Time'); se corrigió (ver `docs/stages/ET1.md`).
- Archivos ET1C copiados de esta corrida: `ET1C_SUMMARY.txt`, `ET1C_SELFTEST_REPORT.txt`.
- `ET1REG_ABORT_primer_intento_151221.txt`: aborto del primer intento (falso positivo de R2), conservado como evidencia.
