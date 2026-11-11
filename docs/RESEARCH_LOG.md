# Bitácora de investigación (entradas breves)

## 2026-11-11 — Creación de NQ-Claude-Research
- Motivo: auditoría del flujo PF1 del repo anterior. Hallazgos que motivan el diseño: bloqueo de DD sin liberación; pool de portafolios sin filtro de rentabilidad; gate mensual con factibilidad no comprobada; ausencia de controles nulo y positivo; código fuera del repo.
- Simulaciones genéricas (no son datos de NQ): con trades i.i.d. de ±US$150 y bloqueo permanente a –US$1.600, un portafolio sin edge a 0,25 operaciones/día se bloquea ~80% de las veces y ejecuta ~100 operaciones de mediana.
- Estado: contrato propuesto; ninguna etapa iniciada.
