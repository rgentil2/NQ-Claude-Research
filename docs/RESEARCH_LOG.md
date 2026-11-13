# Bitácora de investigación (entradas breves)

## 2026-11-11 — Creación de NQ-Claude-Research
- Motivo: auditoría del flujo PF1 del repo anterior. Hallazgos que motivan el diseño: bloqueo de DD sin liberación; pool de portafolios sin filtro de rentabilidad; gate mensual con factibilidad no comprobada; ausencia de controles nulo y positivo; código fuera del repo.
- Simulaciones genéricas (no son datos de NQ): con trades i.i.d. de ±US$150 y bloqueo permanente a –US$1.600, un portafolio sin edge a 0,25 operaciones/día se bloquea ~80% de las veces y ejecuta ~100 operaciones de mediana.

## 2026-11-11 — Contrato congelado (Nivel A) y ET0 entregado
- El usuario aprobó las decisiones de §3 del master. Ventana 09:30–11:30 ET, serie Merge back adjusted, zona horaria de NT8.
- Entregado `NQC_ET0_DataManifest.cs` v1.0.0 (SHA256 `73e937b5…b0eed`).

## 2026-11-11 — NQC 2.0.0
- Cambios del usuario antes de ejecutar nada sobre mercado: sin tope de riesgo por entrada, salidas solo por ATR con objetivo opcional (periodo, k_sl y k_tp generados), Stability y Symmetry como propiedades del portafolio, y paridad nativa después del OOS (ET8 = OOS, ET9 = paridad).

## 2026-11-12 — ET0 ejecutada y aprobada
- Dos corridas con hashes idénticos. G0–G6 PASS: completitud IS 99,10%, zona horaria Eastern sin desfase estacional. Detalle en `runs/2026-11-12_ET0_run1/RUN_NOTES.md`.
- Limitaciones: 7 sesiones incompletas del IS excluidas (3 de marzo de 2020); 2020-10-23 sin barras en la ventana, por revisar.

## 2026-11-12 — Reglas MFFU cerradas para ET1 (D-009)
- Verificado en el Help Center: Rapid EOD (consistencia 30%, 4 días) y Rapid intradía (50%, 2 días) son planes distintos. Se eligió **Rapid intradía** (D-024).
- El usuario fijó: MLL comprobado durante la sesión (D-025), día de operación = ≥1 operación (D-026), blackout T1 de ±5 minutos (D-027), reset US$0.
- El calendario T1 2026 tiene todos los eventos a las 8:30 o 14:00: ninguno cae en la ventana 09:30–11:30 ET.
- Siguiente: ET1.
