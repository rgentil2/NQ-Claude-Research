# ET2 — Runbook de calibración ciega (NT8)

**ESTADO: NO EJECUTADO.** Este documento describe lo que correrá el usuario; ningún paso tiene evidencia todavía. Contrato NQC 2.0.0; el plan y las decisiones pendientes están en `docs/stages/ET2.md`. Los nombres exactos de los parámetros de `NQC_RUN_Funnel` los fija ET3/RUN; aquí se describen por significado.

## Requisitos
- Todos los `.cs` de `ninjatrader/src/` compilan en NT8 (incluye `NQC_ET2_*.cs`; borrar `NQC_ET2_IfaceCopy.cs` si `NQC_RUN` ya declara `NQCRUN_IDatasetTransform`).
- Solo IS (2020-01 a 2022-12). **No** cargar OOS1/OOS2. No se mira ninguna economía del IS real: las campañas de ET2 corren sobre datasets transformados.
- Commit con el protocolo y las semillas (abajo) **antes** de correr. Guardar commit y hash del motor.

## Paso 0 — Prueba técnica sintética (minutos)
1. Strategy Analyzer: `NQC_ET2_DatasetTest` (sin datos relevantes), 1 corrida.
2. Devuelve: `NQC_Results/ET2DATASETTEST_*/DATASET_TEST_REPORT.txt` y `SUMMARY.txt`. Esperado: 21 PASS / 0 FALLA y los hashes de `mundo`, `N1`, `N2`, `INJ` iguales a los del reporte del desarrollo (`ET2.md`). Si difieren, parar.
3. Medir **ms por evaluación de genoma** en el IS real con el runner (una campaña de 2.000 evaluaciones sobre el IS original **solo para tiempos**; no se guarda ni se mira su economía). Con eso se actualiza `NQCET2_CostModel` y se decide Fase B.

## Paso 1 — Pre-chequeo de potencia (3 corridas)
1. Generar un N1+INJ (semilla 20264001) y un N1 nulo (20262001). Devuelve por dataset: hash, `ParamsText` (incluye `a`, `measured_net`, `t_pair`, tasa por sesión, `ok`).
2. Aceptación del dataset inyectado: `ok=1`, convergencia del punto fijo, neto medido 20 ± 2, tasa 0,3–0,8 por sesión, ATR(14) medio ±1%. Si `t_pair` es bajo (< ~3) se **avisa al usuario** antes de gastar cómputo (riesgo de potencia baja por diseño, `ET2.md` §6).

## Paso 2 — Fase A: 30 campañas reducidas (ET3→ET7 completo)
Parámetros declarados (no cambiar durante la fase):
| Elemento | Valor |
|---|---|
| Datasets nulos | N1 semillas 20262001–20262010; N2 semillas 20263001–20263010 |
| Datasets inyectados | N1+INJ semillas 20264001–20264010; objetivo +US$20 netos/operación, tolerancia ±2, costo 24,68 |
| ET3 | población 200, 50 generaciones, MaxEvals 10.000, torneo 3, mutación 5%, élite 5%, inmigrantes 5%, Top-K 200, ≥100 operaciones |
| Semilla GA | 20271000 + índice de campaña (0–29) |
| ET4–ET7 | reglas D-033..D-036 sin cambios; MC 500 exploratorias (5.000 solo si pasa todo); corte N_eff 0,40 |
| Ejecutor | costo 24,68, noticias D-027, capa protectora según el embudo |

Cada campaña devuelve (carpeta `runs/<fecha>_ET2_<id>/`): `SUMMARY.txt` (hash del dataset, `ParamsText`, semillas, commit, hash del motor), `TRIALS_LEDGER` sellado, tasa de paso por gate, distribución del fitness por etapa, supervivientes por etapa, cascada del ejecutor, eventos protectores, mejor portafolio (t, P_pass, PpassBoot, ¿contiene la regla inyectada o equivalente?). **Log de NT8 + hash** por campaña son la evidencia exigida.

## Paso 3 — Fase B (solo si A pasó y el costo es aceptable): 20 campañas nulas a tamaño completo
Mismos datasets nulos, ET3 población 500 × 200 generaciones (100.000 evaluaciones), Top-K 2.000, resto idéntico. Devuelve el vector del «mejor portafolio por campaña» (t de la expectativa neta por operación en el IS, D-036) para el percentil de ET7.

## Paso 4 — Informe
`ET2_CALIBRATION_REPORT.txt` (lo arma ET2 con los SUMMARY): FP por gate y global con Clopper–Pearson, potencia con Clopper–Pearson, distribución nula, tabla de factibilidad de cada umbral [B], curva FP/potencia de P_pass y `ppass_boot_min`, conteo de campañas por caso del vector nulo. El usuario aprueba; el orquestador publica NQC 2.1.0.

## Qué NO hacer
No cambiar umbrales entre campañas; no reutilizar datasets vistos para afinar tras un rediseño (semillas nuevas); no correr datos reales del IS como resultado; no marcar la etapa como ejecutada sin logs y hashes.
