# Bitácora de investigación (entradas breves)

## 2026-11-11 — Creación de NQ-Claude-Research
- Motivo: auditoría del flujo PF1 del repo anterior. Hallazgos que motivan el diseño: bloqueo de DD sin liberación; pool de portafolios sin filtro de rentabilidad; gate mensual con factibilidad no comprobada; ausencia de controles nulo y positivo; código fuera del repo.
- Simulaciones genéricas (no son datos de NQ): con trades i.i.d. de ±US$150 y bloqueo permanente a –US$1.600, un portafolio sin edge a 0,25 operaciones/día se bloquea ~80% de las veces y ejecuta ~100 operaciones de mediana.
- Estado: contrato propuesto; ninguna etapa iniciada.

## 2026-11-11 — Contrato congelado (Nivel A) y ET0 entregado
- El usuario aprobó las decisiones de §3 del master. Ventana 09:30–11:30 ET, serie Merge back adjusted, zona horaria de NT8.
- Entregado `NQC_ET0_DataManifest.cs` v1.0.0 (SHA256 `73e937b5…b0eed`). Falta ejecutarlo en Strategy Analyzer y comparar hashes de dos corridas.
- Sin resultados de mercado hasta ahora.
