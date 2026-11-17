# ET2 — Runbook de calibración ciega (NT8)

**ESTADO: NO EJECUTADO.** Este documento describe lo que correrá el usuario; ningún paso tiene evidencia todavía. Contrato NQC 2.0.0; el plan y las decisiones pendientes están en `docs/stages/ET2.md`. Los nombres exactos de los parámetros de `NQC_RUN_Funnel` los fija ET3/RUN; aquí se describen por significado.

## Requisitos
- Todos los `.cs` de `ninjatrader/src/` compilan en NT8 (incluye `NQC_ET2_*.cs`, `NQC_ET3_Reservoir`, `NQC_RUN_NeffAdapter`, `NQC_RUN_Batch`; `NQC_ET2_IfaceCopy.cs` ya fue eliminado). Ver `RUN_FUNNEL_RUNBOOK.md` (sección «Lote de campañas»).
- Solo IS (2020-01 a 2022-12). **No** cargar OOS1/OOS2. No se mira ninguna economía del IS real: las campañas de ET2 corren sobre datasets transformados.
- Commit con el protocolo y las semillas (abajo) **antes** de correr. Guardar commit y hash del motor.

## Paso 0 — Prueba técnica sintética (minutos)
1. Strategy Analyzer: `NQC_ET2_DatasetTest` (sin datos relevantes), 1 corrida.
2. Devuelve: `NQC_Results/ET2DATASETTEST_*/DATASET_TEST_REPORT.txt` y `SUMMARY.txt`. Esperado: 21 PASS / 0 FALLA y los hashes de `mundo`, `N1`, `N2`, `INJ` iguales a los del reporte del desarrollo (`ET2.md`). Si difieren, parar.
3. **(v2, NQCRUN-1.3.0: paralelo)** Medir el **escalado en hilos** con `NQC_RUN_Batch` en modo TIMING, manifiesto `ET2_BATCH_MANIFEST_PASO0_TIMING_HILOS.txt` (copiar a `NQC_Results\RUN_BATCH_MANIFEST.txt`): `TIMING_HILOS` barre `threads=1,4,8,16,24` con 200 evaluaciones por valor (datos y cache en frio en cada uno) y `TIMING_REGIMEN` corre 10.000 evaluaciones con 8 hilos para medir el costo en régimen (cache caliente). El SUMMARY de cada campaña trae la tabla `hilos / pared_s / ms_pared_por_eval / speedup / eficiencia` y `resultado_identico_entre_hilos=SI` (debe ser SI; si dice NO, parar y reportar). Elegir el `threads` con mejor ms de pared que no pase de ~16 (los núcleos E y la térmica no escalan lineal) y usarlo en todo el trabajo (`threads=` global del lote). Para FULL usar `memo_budget_mb=4096` y `max_memory_mb=8192`. **Nota sobre la medición anterior**: los 284 ms (media) / 117 ms (mediana) del paso 0 original (runner 1.1.0, 2.000 evaluaciones, 1 hilo) son un promedio del ARRANQUE EN FRÍO (la cache de numeradores se va llenando); en mono sintético el costo por evaluación baja de 38 ms (primeras 1.400) a 16 ms en régimen (2 GB) y 13,5 ms (4 GB), y las primeras 18.000 evaluaciones promedian ~19 ms frente a los 38 ms del arranque (el promedio de 100.000 sería aún menor): extrapolar 284 ms × 100.000 sobreestima. Que ocurra lo mismo en NT8 con datos reales es una HIPÓTESIS que `TIMING_REGIMEN` verifica.
3b. (v1, un hilo) Medir **ms por evaluación de genoma** con `NQC_RUN_Batch` en modo **TIMING**, manifiesto `ET2_BATCH_MANIFEST_PASO0_TIMING.txt` (copiar a `NQC_Results\RUN_BATCH_MANIFEST.txt`; 2.000 evaluaciones de ET3). Corre **sobre un dataset N1 (tseed 20269001, semilla propia de esta medición), nunca sobre el IS original**: no hay economía real que mirar. Estrategy Analyzer: NQ M1 Last, back adjusted, 2020-01-01..2022-12-31, estrategia `NQC_RUN_Batch`. Devuelve `NQC_Results\RUNBATCH_ET2_PASO0_TIMING\TIMING_N1\SUMMARY.txt` con `ms_por_evaluacion: media= mediana= p90= max= media_sin_el_5%_inicial=`, memoria (`pico_working_set_mb`) y la extrapolación de horas **solo de ET3**. Copiar a `runs/<fecha>_ET2_PASO0/` con hash.
4. **Decisión con el tiempo medido** (la toma el usuario; ET2 solo presenta la tabla). Sea `c` = `media_sin_el_5%_inicial` (ms/evaluación). Horas estimadas de ET3 = `c × 100.000 × 30 / 3.600.000` (30 campañas FULL, un hilo; ≈ `c × 0,833` h). ET4–ET7 **no** están medidos y suman aprox. un 20–40% por campaña (la poda de ET4 hace ~64.000 evaluaciones en FULL): se informa como rango, no como cifra.

| c (ms/eval) | h, 30 FULL | h, 20 FULL (Fase B) | h, 30 REDUCED (Fase A) | Qué hacer |
|---|---|---|---|---|
| 10 | 8,3 | 5,6 | 0,8 | Todo FULL en un hilo (30 campañas, D-037). |
| 30 | 25 | 17 | 2,5 | Todo FULL, ~1 día. |
| 50 | 42 | 28 | 4,2 | Todo FULL, ~2 días; aceptable (< ~48 h). |
| 60 | 50 | 33 | 5 | Límite: ~48 h. Preferir paralelizar (opción 1). |
| 120 | 100 | 67 | 10 | Supera 48 h: paralelizar o reducir (abajo). |
| 200 | 167 | 111 | 17 | Supera ~1 semana: paralelizar **y** reducir. |

**Tabla con la medición real (NT8, i9-13900HX, 1 hilo, runner 1.1.0) y el paralelismo (solo ET3; horas = c × 0,833 × campañas/30 ÷ speed-up).** El speed-up S es una **suposición ilustrativa** hasta medirlo con `TIMING_HILOS` (en mono con 4 núcleos se midió 1,8× con 2 hilos y 3,0× con 4 hilos, eficiencia 0,90 y 0,75; en el i9 los 16 núcleos E rinden menos que los 8 P y la térmica limita en cargas de días):

| c (ms/eval, 1 hilo) | 30 FULL, S=1 | S=4 | S=8 | S=12 | 20 FULL, S=8 | 30 REDUCED, S=8 |
|---|---|---|---|---|---|---|
| 284 (media medida, arranque en frío) | 236,7 h | 59 h | 29,6 h | 19,7 h | 19,7 h | 3,0 h |
| 117 (mediana medida) | 97,6 h | 24 h | 12,2 h | 8,1 h | 8,1 h | 1,2 h |
| ~120 (hipótesis de régimen: 284 × 0,42, a verificar con TIMING_REGIMEN) | 100 h | 25 h | 12,5 h | 8,3 h | 8,3 h | 1,3 h |

ET4–ET7 suman un 20–40% más y no están medidos. Con S=8 y la media del arranque en frío (peor caso) ET3 FULL en 30 campañas son ~30 h (~1,2 días); con régimen ~12 h: **ya no supera el umbral de 48 h** si el escalado real es ≥ 5×. Decidir con la tabla de `TIMING_HILOS`.

Si la estimación (con el 20–40% extra de ET4–ET7) **supera ~48 h**, o **~1 semana** en un solo hilo, ET2 **no** decide por su cuenta: presenta al usuario estas opciones con sus costos:
1. **Paralelizar con varias copias de NT8** (o varias instancias/ventanas del Strategy Analyzer, una por máquina o proceso): partir el manifiesto en lotes disjuntos (p. ej. 3–4 lotes de 7–10 campañas, con `batch=` y `out_root` distintos; las semillas ya están fijadas por campaña, así que el resultado es idéntico a correr en un hilo). Límite: la RAM (`memo_budget_mb` ~2.048 por proceso + ~31 MB de reservorio) y los núcleos; el `SUMMARY` de cada lote se consolida a mano en el informe.
2. **Reducir** (declarado, sesgo documentado): (a) FULL solo en los 20 nulos y REDUCED en los 10 inyectados (la potencia se mide a tamaño reducido, **optimista** para detectar el edge porque hay menos pruebas que deflactar); (b) Fase B con menos nulos o `size=CUSTOM`; (c) vector nulo desde la Fase A, declarado **sesgado a la baja** (ET2.md §4).
3. **Aceptar el tiempo** y repartirlo en sesiones (el lote es reanudable: `resume=1`, se repite con el mismo manifiesto).
Hasta esa decisión **no** se corren los manifiestos FULL (Fase B ni inyectados).

## Paso 1 — Pre-chequeo de potencia (3 corridas)
1. Generar un N1+INJ (semilla 20264001) y un N1 nulo (20262001). Devuelve por dataset: hash, `ParamsText` (incluye `a`, `measured_net`, `t_pair`, tasa por sesión, `ok`).
2. Aceptación del dataset inyectado: `ok=1`, convergencia del punto fijo, neto medido 20 ± 2, tasa 0,3–0,8 por sesión, ATR(14) medio ±1%. Si `t_pair` es bajo (< ~3) se **avisa al usuario** antes de gastar cómputo (riesgo de potencia baja por diseño, `ET2.md` §6).

## Paso 2 — Fase A: 30 campañas REDUCED (ET3→ET7 completo) con `NQC_RUN_Batch`
Manifiesto: `ninjatrader/runbooks/ET2_BATCH_MANIFEST_FASE_A.txt` → `NQC_Results\RUN_BATCH_MANIFEST.txt` (completar `commit=` con `git rev-parse HEAD` antes de correr; commit del protocolo ya hecho). Un solo lote (`batch=ET2_FASE_A`, `resume=1`), los datos se cargan una vez y las campañas corren en secuencia; si falla una, se registra ERROR y el lote sigue. Salida: `NQC_Results\RUNBATCH_ET2_FASE_A\` (`SUMMARY.txt` del lote, `BATCH_LOG.txt`, y por campaña `<id>\SUMMARY.txt`, `BATCH_ENTRY.txt`, `CKPT_*`, `ET3..ET7\`). Copiar a `runs/<fecha>_ET2_FASE_A/` con hash.
Parámetros declarados (no cambiar durante la fase):
| Elemento | Valor |
|---|---|
| Datasets nulos | N1 tseed 20262001–20262010 (A00–A09); N2 tseed 20263001–20263010 (A10–A19) |
| Datasets inyectados | N1+INJ (`N1INJ`) tseed 20264001–20264010 (A20–A29); `target=20`, `tol=2`, costo 24,68 |
| ET3 (`size=REDUCED`) | población 200 × 50 gen., MaxEvals 10.000, Top-K 200, torneo 3, mutación 5%, élite 5%, inmigrantes 5% |
| Semilla GA | 20271000 + índice (0–29) |
| ET4–ET7 | reglas D-033..D-036 sin cambios; MC 500 exploratorias; `et6.ppass_min=` vacío (falla cerrada hasta que ET2 lo fije); `et7.neff_cut=0.40` |
| N_eff (D-038) | `neff_sample=5000`, `et7.neff_method=SAMPLE_CHAO1`, `et7.neff_bound=UPPER` (ver «N_eff por muestra») |
Evidencia exigida: log de NT8 + hash del `SUMMARY.txt` del lote y de cada campaña. Aceptación del N1+INJ: `inj_ok=1` en la columna `nota` del SUMMARY del lote; si `inj_ok=0` la campaña corre pero su dataset no cumple el Paso 1.

## Paso 3 — Fase B y FULL de inyectados (solo tras el Paso 0.4)
**3a. Fase B** (`ninjatrader/runbooks/ET2_BATCH_MANIFEST_FASE_B.txt`, `batch=ET2_FASE_B`): los mismos 20 datasets nulos (N1 20262001–20262010, N2 20263001–20263010) con `size=FULL` (población 500 × 200, 100.000 evaluaciones, Top-K 2.000), semillas GA **20272000 + índice 0–19** (distintas de la Fase A). Produce el vector del «mejor portafolio por campaña» al tamaño de búsqueda real para el percentil nulo de ET7. Se corre solo si la Fase A pasó y el costo fue aceptado.
**3b. FULL de las 10 inyectadas** (`ninjatrader/runbooks/ET2_BATCH_MANIFEST_FULL_INYECTADOS.txt`, `batch=ET2_FULL_INYECTADOS`): **D-037 pide FULL en las 30 campañas**, incluidas las inyectadas (la potencia solo es válida al mismo tamaño de búsqueda/deflación que las reales). N1+INJ 20264001–20264010, semillas GA 20272020–20272029. Se corre **solo tras medir el tiempo del Paso 0** y con la decisión del usuario sobre paralelizar/reducir. Si se omite, la potencia de la Fase A queda declarada como **optimista** (menos pruebas que deflactar).
Cada lote en `runs/<fecha>_ET2_<id>/` con SUMMARY y hash. Los lotes FULL son reanudables; no alterar el manifiesto entre sesiones (el hash de entrada recalcularía la campaña).

## N_eff por muestra (D-038): uso y qué se mide
- Configuración: `neff_sample=5000` (global del lote; reservorio uniforme con semilla = semilla del GA XOR 0x5DEECE66D, no altera el GA) y `et7.neff_method=SAMPLE_CHAO1`, `et7.neff_bound=UPPER` (obligatorio con SAMPLE_CHAO1), `et7.neff_sample_cov_min=0.90`, `et7.neff_sample_min_n=1000` (defectos a confirmar). Con `COVERED` (D-036) ET7 falla cerrado por cobertura ≈ 0,01.
- **Estado de la integración:** el runner deja `ET7\ET7_NEFF_SAMPLE_ESTIMATE.txt` (N_poblacion, N_muestra, grupos observados, f1, f2, N_eff Chao1, IC95, cobertura Good–Turing), pero su veredicto aún calcula con el N_eff de D-036 hasta que el orquestador integre `NQCE7_Evidence` con la muestra. **Antes de correr la Fase A hay que confirmar con el orquestador que el veredicto usa SAMPLE_CHAO1**; si no, el DSR de la calibración no es el que se usará con datos reales.
- Qué se mide en los nulos para validarlo: (1) **FP**: portafolios nulos aprobados con ese N_eff (si Chao1 subestima el N_eff, el DSR es optimista y aparecen FP; es la salvaguarda real, D-038); (2) **DSR alcanzable**: DSR del par inyectado y del mejor nulo con N_eff = punto / inferior / superior del IC (¿puede un edge de +US$20 superar 0,95 con el N_eff superior?); (3) **IC y cobertura del N_eff**: f1, f2, cobertura Good–Turing por campaña, y su comparación con el N_eff de COVERED sobre el top-K, y la estabilidad entre campañas del mismo tipo; (4) conteo de campañas que **fallan cerrado** por muestra insuficiente (`neff_sample_n`, `neff_sample_flat`). Todo va al informe (`docs/stages/ET2.md`, §10).

## Paso 4 — Informe
**Generador (desarrollo, probado solo con carpetas sintéticas; NO ejecutado sobre un lote real):** `NQC_ET2_Report` (estrategia NT8 sin órdenes; config en `NQC_Results\ET2_REPORT_CONFIG.txt` con `batch_dir=NQC_Results\RUNBATCH_ET2_FASE_A`, `label=`, `commit=`) o, fuera de NT8, `mcs -out:et2report.exe ninjatrader/src/NQC_ET2_Calibration.cs ninjatrader/src/NQC_ET2_ReportBuilder.cs ninjatrader/runbooks/ET2_REPORT_CLI.cs` y `mono et2report.exe <carpeta_del_lote> ET2_CALIBRATION_REPORT.txt label=FASE_A commit=<sha>`. Lee la carpeta del lote tal como la deja `NQC_RUN_Batch`, calcula FP/potencia con Clopper–Pearson, embudo por puerta, vector nulo, barrido de umbrales, N_eff y un veredicto mecánico, y escribe el informe con su sha256 y el de cada archivo leído (`ET2_REPORT_INPUTS.txt`). Prueba: `NQC_ET2_ReportTest` (29 comprobaciones). **Limitaciones declaradas:** (1) en la Fase A el runner no tiene vector nulo, el percentil de ET7 falla cerrado y el informe lo **recalcula** leave-one-out (n≥19); (2) la potencia con regla equivalente (D-037) solo es evaluable si el runner exporta `ET6\ET6_MEMBER_TRADES.tsv` (`trial_id`,`day`,`dir` por operación ejecutada de cada miembro de los finalistas) e `INJ_PAIR_TRADES.tsv` (`day`,`dir` del par inyectado); sin ellos da la cota superior y el veredicto queda INDETERMINADO.
`ET2_CALIBRATION_REPORT.txt` lo arma ET2 con los SUMMARY; su contenido exacto y los archivos/claves que consume están definidos en `docs/stages/ET2.md` §10. El usuario aprueba; el orquestador publica NQC 2.1.0.

## Qué NO hacer
No cambiar umbrales entre campañas; no reutilizar datasets vistos para afinar tras un rediseño (semillas nuevas); no correr datos reales del IS como resultado; no marcar la etapa como ejecutada sin logs y hashes.
