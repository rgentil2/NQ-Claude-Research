# ET5 — Robustez interna por bloques

**Archivo de traspaso (memoria viva de la etapa).** Lo lee el agente `et5-robustez` al empezar y lo actualiza al terminar. La autoridad es `docs/CONTRACT_NQC_2.0.0.md` §2 ET5; si hay conflicto, manda el contrato.

| Campo | Valor |
|---|---|
| Estado | NO INICIADA (el estado oficial vive en el master §6) |
| Depende de | ET4 |
| Objetivo | Estabilidad entre años y medición del sobreajuste del procedimiento (PBO). |
| Entradas | Pool podado de ET4. |
| Salidas | Pool robusto + PBO |
| Gate | ≥2/3 años positivos; PBO ≤0,40 |
| Rutas propias (lectura/escritura) | ninjatrader/src/NQC_ET5*, runs/*ET5* |
| Límite de datos / reglas duras | IS solamente; pool vacío cierra la campaña. |

## Pendientes y notas vigentes
- Codigo y prueba sintetica listos; ET5 sobre datos reales exige ET2 aprobada y el pool de ET4 (matriz candidatos x 12 trimestres del ledger). Sin evidencia NT8 todavia.
- Compensa la ausencia de examen intermedio (D-004b).

## Decisiones tomadas en esta etapa
Implementadas en `ninjatrader/src/NQC_ET5_Robust.cs` (propuestas, a confirmar):
- D5-1 Trimestres 0..11 = 2020Q1..2022Q4; anio = 4 trimestres; neto anual = suma. Filtro: neto anual > 0 estricto en >=2 de 3.
- D5-2 Metrica de ranking del PBO: P&L neto total del conjunto de trimestres (aditiva). Trimestre sin operaciones = 0. Expectativa (neto/operacion) disponible como alternativa, no por defecto; Sharpe diario descartado (requiere series diarias; la interfaz es trimestral).
- D5-3 CSCV: las 924 particiones, orden lexicografico; n* = argmax en entrenamiento (empate: menor indice); omega = rango medio en prueba/(N+1); lambda = ln(omega/(1-omega)); PBO = fraccion con lambda<=0. omega=0,5 (incl. empate total, candidatos identicos) cuenta como sobreajuste (conservador).
- D5-4 PBO del gate se calcula sobre el pool tras el filtro de anios; PBO del pool completo de ET4 se reporta como informativo.
- D5-5 Ensayos: la matriz sale del ledger; ET5 no agrega ensayos ni deduplica filas.
- D5-6 N<2 tras filtro: PBO no evaluable -> falla cerrada y cierra campania (PREGUNTA ABIERTA).
- Calculo a mano R4: perfiles espejo A=(+1x6,-1x6), B=-A; k=|S∩primeros6|; k!=3 (524 particiones) n* queda ultimo en prueba (omega=1/3); k=3 (400) empate total omega=0,5 -> PBO=1.

## Bitácora de traspasos
Formato: `AAAA-MM-DD · qué se hizo · artefactos/hash · qué queda · bloqueos`. Máximo 5 líneas por entrada.

- 2026-11-14 · Archivo creado al adoptar el modelo de agentes por etapa (`docs/WORK_MODEL.md`). Sin trabajo nuevo.
- 2026-11-14 · NQC_ET5_Robust.cs (filtro anios + PBO/CSCV) y NQC_ET5_RobustTest.cs: 13/13 PASS con mcs+stub (sintetico, NO NT8). Hash pool R9 ae1efcea..., informe d40518b4... · falta: corrida en NT8, datos reales tras ET2/ET4 · pregunta abierta D5-6.
