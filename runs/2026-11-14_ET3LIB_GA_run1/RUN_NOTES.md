# ET3 LibraryTest + GaTest — corrida en NT8 (2026-11-14)

Pruebas técnicas con barras sintéticas (sin mercado, sin resultados económicos). Compilado en NinjaTrader 8 desde `NQC_ET3_pack.zip` (ZIP generado con `tools/installer/build_zip.py`).

## Evidencia (líneas de consola de NT8, copiadas tal cual)
```
NQC_ET3LIB_END status=PASS ok=44 falla=0 sha=f15e4d8fe2a2 dir=...\NQC_Results\ET3LIB_20261114_161738
NQC_ET3GA_END  status=PASS ok=15 falla=0 sha=1b9da498753a dir=...\NQC_Results\ET3GA_20261114_161842
NQC_ET3GA_END  status=PASS ok=15 falla=0 sha=1b9da498753a dir=...\NQC_Results\ET3GA_20261114_161846
NQC_ET1C_END   status=PASS ok=22 falla=0 sha=b9f84a4e6a75 (varias corridas, mismo hash)
```
## Lectura
- Compila en NT8 sin errores y ambas pruebas pasan.
- Los hashes de informe coinciden con los de la verificación previa en mono con un stub de NT8 (LibraryTest `f15e4d8fe2a2`, GaTest `1b9da498753a`): mismo comportamiento en ambos entornos.
- GaTest corrido dos veces con el mismo hash (determinismo con la misma semilla).
- ET1C sigue en 22/22 con el hash de siempre (`b9f84a4e…`).

## Hashes completos (SUMMARY.txt archivados aquí)
- `ET3LIB_SUMMARY.txt`: library `NQCL-3.0.0:19e2c9d6dbffd973`, 1.243 condiciones, 44/44, report_sha256 `f15e4d8fe2a2af873e499e2387e68d4e17f3813a5dbe403f575c0724457ab0d1`.
- `ET3GA_SUMMARY.txt`: 15/15, report_sha256 `1b9da498753a7c13914a91d477d7b448cfda16ca83f131bc3eba69519d0e3e96`.

## Limitaciones
- Pruebas sintéticas: no validan nada de mercado. ET3 sobre datos reales exige ET2 aprobada.
- Biblioteca NQCL-3.0.0 (113 familias × 11 reglas = 1.243 condiciones).
