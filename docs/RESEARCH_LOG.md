# Bitácora de investigación (entradas breves)

## 2026-11-11 — Creación de NQ-Claude-Research
- Motivo: auditoría del flujo PF1 del repo anterior. Hallazgos que motivan el diseño: bloqueo de DD sin liberación; pool de portafolios sin filtro de rentabilidad; gate mensual con factibilidad no comprobada; ausencia de controles nulo y positivo; código fuera del repo.
- Simulaciones genéricas (no son datos de NQ): con trades i.i.d. de ±US$150 y bloqueo permanente a –US$1.600, un portafolio sin edge a 0,25 operaciones/día se bloquea ~80% de las veces y ejecuta ~100 operaciones de mediana.
- Estado: contrato propuesto; ninguna etapa iniciada.

## 2026-11-11 — Contrato congelado (Nivel A) y ET0 entregado
- El usuario aprobó las decisiones de §3 del master. Ventana 09:30–11:30 ET, serie Merge back adjusted, zona horaria de NT8.
- Entregado `NQC_ET0_DataManifest.cs` v1.0.0 (SHA256 `73e937b5…b0eed`).

## 2026-11-11 — NQC 2.0.0
- Cambios del usuario antes de ejecutar nada sobre mercado: sin tope de riesgo por entrada (el stop de 35 ticks ahogaba al NQ), salidas solo por ATR con stop obligatorio y objetivo opcional (periodo, k_sl y k_tp generados), Stability y Symmetry como propiedades del portafolio, y paridad nativa después del OOS (ET8 = OOS, ET9 = paridad).
- Sin resultados de mercado ni ensayos que sumar.

## 2026-11-12 — ET0 ejecutada y aprobada
- Dos corridas en Strategy Analyzer con hashes idénticos. G0–G6 PASS: completitud IS 99,10%, zona horaria Eastern verificada sin desfase estacional. Detalle en `runs/2026-11-12_ET0_run1/RUN_NOTES.md`.
- Limitaciones anotadas: 7 sesiones incompletas del IS excluidas (3 de marzo de 2020); 2020-10-23 sin barras en la ventana, por revisar. Los márgenes de G3 y G4 son pequeños.
- Siguiente: verificar reglas MFFU (D-009) y empezar ET1.
