# ET1 C - run1 (2026-11-13)
- Script ninjatrader/src/NQC_ET1C_AccountSim.cs (ET1C-1.0.0), SHA256 aebe6fb1d3c9eb670c9f9ffe4455143b6638cfed4bcc69fd9ad517f0da5b061e.
- NT8: NQC_ET1C_END status=PASS ok=22 falla=0 sha=b9f84a4e6a75 (12 instancias del Analyzer, todas con el mismo hash).
- El mismo hash de informe (b9f84a4e...) sale de la compilacion Mono fuera de NT8: el resultado es identico entre runtimes (P6).
- Cubre P3 (costos), P4 (capa protectora con guardia de DD), P5 (14 trayectorias), P6 (determinismo). P2 queda cubierto por la paridad B1 (8,110/8,110).
- Pendiente de ET1: cierre formal y aprobacion; luego ET2 (calibracion ciega).
- Trazabilidad (master §9, añadida 2026-11-14): contrato NQC 2.0.0; EXECUTOR_SPEC 2.3.0; pruebas sobre escenarios sintéticos (sin instrumento ni rango de datos de mercado). El `SUMMARY.txt` es el que escribió la corrida (3 líneas) y no se reescribe.
