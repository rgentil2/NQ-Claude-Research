# ET4 — Poda barata por estrategia

**Archivo de traspaso (memoria viva de la etapa).** Lo lee el agente `et4-poda` al empezar y lo actualiza al terminar. La autoridad es `docs/CONTRACT_NQC_2.0.0.md` §2 ET4; si hay conflicto, manda el contrato.

| Campo | Valor |
|---|---|
| Estado | NO INICIADA (el estado oficial vive en el master §6) |
| Depende de | ET3 |
| Objetivo | Descartar candidatos que no resisten costos 1,5×, sub-periodos ni vecindario de parámetros. |
| Entradas | Databank de ET3. |
| Salidas | Pool podado + causa de rechazo por candidato |
| Gate | F1 costo estresado, F2 ≥7/12 trimestres, F3 ≥70% vecinos |
| Rutas propias (lectura/escritura) | ninjatrader/src/NQC_ET4*, runs/*ET4* |
| Límite de datos / reglas duras | IS solamente. |

## Pendientes y notas vigentes
- F4 (ancho del stop) es informativo, no elimina.

- DESARROLLO listo (código + prueba sintética); NO EJECUTADA en NT8 ni sobre datos reales (exige ET2 aprobada y databank de ET3 real). Pendiente: correr NQC_ET4_PruneTest en NT8 (log + hash) y, tras ET2/ET3, la poda sobre el databank.
- Para cerrar: el orquestador debe llevar al usuario las decisiones de diseño D4-1..D4-6 (abajo).

## Decisiones tomadas en esta etapa (propuestas; no cambian umbrales [B])
- D4-1 Trimestres: 12 trimestres calendario 2020Q1..2022Q4; una operación pertenece al trimestre de su DayKey de sesión; trimestre sin operaciones = neto 0 = no positivo; sin mínimo de operaciones por trimestre.
- D4-2 F1: neto total > 0 a 1,5x (37,02). Se obtiene del neto del ejecutor menos (37,02-24,68) por operación (el costo es cargo fijo posterior a la simulación); la prueba lo contrasta con una corrida completa del ejecutor a CostMult=1,5.
- D4-3 F3 vecindario: un parámetro a la vez, ±1 paso: periodo y umbral de cada ranura activa (niveles de la biblioteca, D-030), periodo ATR 5..100, k_sl índice 0..22, k_tp índice 1..31. NO se perturban id de condición, dirección ni ranuras vacías. "Sin objetivo" no tiene vecinos y no es vecino de k_tp=0,5.
- D4-4 Vecino fuera de rejilla: NO EXISTE (ni reparación ni descarte-como-fallo): no entra al denominador. Reparar (acotar) generaría copias del candidato y inflaría el cociente. Los parámetros de borde tienen un solo vecino. Criterio: aprobados*100 >= 70*existentes (entero). Vecino "mantiene neto>0" = neto total a costo 1,0x > 0 (0 operaciones = fallo); no se exige el mínimo de 100 operaciones al vecino (ET3 ya lo exigió al candidato).
- D4-5 Ledger: la evaluación del candidato no es ensayo nuevo (ya está en el ledger de ET3). Cada vecino es ensayo si su TrialId no está en el ledger de ET3 ni evaluado antes en ET4; interfaz: NQCE4_Prune(..., knownTrialIds = columna trial_id del ledger de ET3) y FreshTrials se suma al acumulado de TRIALS_LEDGER. Se evalúan todos los vecinos de un candidato que llega a F3 (sin corte dentro de F3), para registrar el cociente completo.
- D4-6 F4: |entrada-stop| por operación (misma definición que NQCE_Audit.TradesTsv): min/p25/mediana/p75/max/media en puntos y min/mediana/max/media en US$, para todos los candidatos (incluso rechazados). No elimina.

## Bitácora de traspasos
Formato: `AAAA-MM-DD · qué se hizo · artefactos/hash · qué queda · bloqueos`. Máximo 5 líneas por entrada.

- 2026-11-14 · Archivo creado al adoptar el modelo de agentes por etapa (`docs/WORK_MODEL.md`). Sin trabajo nuevo.
- 2026-11-14 · ET4 código + prueba sintética (mcs+mono, stub NT8): 19/19 PASS. NQC_ET4_Prune.cs sha256 fc3d8760..., NQC_ET4_PruneTest.cs e28bb9f7...; pool_sha256 4ce0c17e..., rechazos 4a188f53..., ledger 60d69b0f... (24 ensayos nuevos del caso de juguete). Biblioteca de juguete, NO real. Queda: ejecución en NT8, datos reales tras ET2/ET3. Sin bloqueos propios.
- 2026-11-14 · Orquestador: verificado independientemente (mcs+stub) 19/19 con los mismos hashes. Decisiones D4-1..D4-6 registradas como D-034 (propuesta con valor por defecto).
- 2026-11-16 · NT8: NQC_ET4_PruneTest 19/19, mismos hashes que mono.
