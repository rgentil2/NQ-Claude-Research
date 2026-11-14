# ET1 B2 (C1) - run1 (2026-11-14) - EJECUTADA

- Script `ninjatrader/src/NQC_ET1B2_GapParity.cs` (ET1B2-1.0.0), SHA256 `9b5839fb4d58c8fe641df581f8e32dc009ab4ace2f0824074af4e8e050878e19`. Contrato NQC 2.0.0, EXECUTOR_SPEC 2.3.0.
- NT8: `NQC_ET1B2_END status=PASS equal=46/46 gapped=46/46 sha=a22ccc697bee` (carpeta `ET1B2_20261114_141925`).
- `status=PASS` en el script exige: ATR14 propio = NT8 en todas las barras, SHA256 del libro nativo de la sonda igual al registrado, 46 de 46 operaciones iguales (dirección, entrada, stop, nombre de salida, hora, precio) y 46 de 46 con salto (salida a la apertura, distinta del precio del stop).
- Efecto: el caso *gap adverso* de P1 queda demostrado contra el motor C#. Cierra C1 del acta `docs/ET1_ACTA_CIERRE.md`.
- Pendiente de registro: `SUMMARY.txt` y `PARITY_REPORT.txt` de la corrida (hash completo del informe; solo se tiene el prefijo de 12 caracteres). Copiarlos a esta carpeta.
