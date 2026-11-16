# ET7 — Evidencia estadística y congelado

**Archivo de traspaso (memoria viva de la etapa).** Lo lee el agente `et7-evidencia` al empezar y lo actualiza al terminar. La autoridad es `docs/CONTRACT_NQC_2.0.0.md` §2 ET7; si hay conflicto, manda el contrato.

| Campo | Valor |
|---|---|
| Estado | NO INICIADA (el estado oficial vive en el master §6; propuesto: EN DESARROLLO, solo sintético) |
| Depende de | ET6, ET2 |
| Objetivo | Demostrar que el resultado es distinguible de la suerte de la búsqueda y congelar el portafolio. |
| Entradas | Finalistas de ET6; TRIALS_LEDGER; distribución nula de ET2. |
| Salidas | ET7_EVIDENCE_REPORT.txt, PORTFOLIO_FROZEN.txt |
| Gate | DSR ≥0,95; percentil nulo ≥95; MC P95 DD ≤2.000 |
| Rutas propias (lectura/escritura) | tools/lab/et7/, runs/*ET7*, docs/STATISTICS_PLAN.md |
| Límite de datos / reglas duras | IS + ET2. Congela hash de spec + motor + commit ANTES de abrir OOS. |

## Pendientes y notas vigentes
- Código y prueba sintética listos (`ninjatrader/src/NQC_ET7_*.cs`). ET7 REAL bloqueada: exige ET2 aprobada (vector nulo, umbral de P_pass) y finalistas reales de ET6. Estado propuesto: EN DESARROLLO (código compilado + prueba sintética; NO ejecutada con datos).
- Pendiente: ET6 debe exportar la serie de P&L diario (IS) de cada ensayo evaluado y de cada finalista, y la lista de operaciones ejecutadas (sesión, neto, mínimo abierto); ET2 debe entregar el vector «mejor portafolio por campaña» con la MISMA métrica de fitness que se pasa a ET7.
- D-038 (cobertura del N_eff): implementada la politica `neff_method=SAMPLE_CHAO1` (`NQCE7_Neff.EstimateFromSample`, `NQC_ET7_Neff.cs`) lista para la MUESTRA ALEATORIA UNIFORME que entregara ET3; PENDIENTE: ET2 declara cobertura Good-Turing minima, limite del IC y tamano de muestra ANTES de correr y mide con los nulos si el DSR es alcanzable (Chao1 es limite inferior: ver limitaciones en la cabecera de NQC_ET7_Neff.cs). Solo sintetico; NO ejecutada con datos.
- Preguntas abiertas: ver «Decisiones» (corte del N_eff, cobertura de series, métrica de fitness del percentil).

## Decisiones tomadas en esta etapa
Recomendaciones conservadoras (INTERPRETACIÓN; el orquestador/usuario confirma):
1. **N bruto del ledger acumulado** = ET3 (100.000 por campaña, todas las evaluaciones de genoma, también las descartadas) + ET4 (30–60 mil vecinos, D-034: cada vecino evaluado es un ensayo) + ET6 (D-035: cada combinación única evaluada, parciales incluidos, sin recontar la corrida a 1,5×). Se suman sin descuento; el bruto se reporta siempre.
2. **N_eff**: agrupamiento jerárquico, linkage PROMEDIO, distancia √(0,5(1−ρ)), corte por defecto 0,40 (ρ ≥ 0,68) declarado en `ET7_CONFIG.txt` ANTES de correr y sellado por `config_sha256` en el informe. Los vecinos de ET4/ET6 (muy correlacionados con su padre) se funden con él: eso es el tratamiento de la correlación; un corte menor da más N_eff (DSR más duro). Series con varianza cero = grupo propio.
3. **Cobertura**: no se guardan series de los 100.000 ensayos. Si solo una fracción c del N bruto tiene serie, N_eff = grupos / c (conservador); c < `neff_coverage_min` (defecto 1,0) falla cerrado. Más de `neff_max_series` (4.000, matriz O(n²)) falla cerrado: reducir antes (p. ej. series de los supervivientes de ET3 más representantes).
4. **Retorno diario del portafolio** = P&L neto de la sesión en US$ (días sin operación = 0, todas las sesiones no excluidas del IS). SR diario; anualizado solo informativo ×√252. El DSR usa el SR diario (no anualizado), asimetría y curtosis no exceso, T días.
5. **V del SR** = varianza entre grupos del SR diario (representante: serie de menor índice); con <2 grupos V=0 y SR0=0.
6. **Percentil nulo** = 100·#{nulo < x}/n (empates no suman); exige n ≥ `null_min_n` (20). Fitness del portafolio y del nulo deben ser la misma métrica.
7. **Monte Carlo**: bloques circulares de 8 operaciones, la posición i conserva la sesión de la operación original i; DD por operación incluyendo el mínimo abierto; P95 = cuantil ceil(0,95·K); P_pass bootstrap = media de `NQC_AccountSim.Rolling` por trayectoria; solo se reporta salvo que `ppass_boot_min` esté fijado. Semilla defecto 20261114.
8. **Congelado**: hash de bytes (sin normalizar CRLF/LF: fijar eol=lf); ET8 debe llamar `NQCE7_Freeze.Verify` con commit actual (`git rev-parse HEAD`), spec y TODOS los .cs del motor; cualquier discrepancia, archivo de más/de menos o sello roto = no abrir el OOS.

## Bitácora de traspasos
Formato: `AAAA-MM-DD · qué se hizo · artefactos/hash · qué queda · bloqueos`. Máximo 5 líneas por entrada.

- 2026-11-14 · Archivo creado al adoptar el modelo de agentes por etapa (`docs/WORK_MODEL.md`). Sin trabajo nuevo.
- 2026-11-14 · Implementado Stats/MonteCarlo/Freeze/Evidence + EvidenceTest (35 PASS/0 FALLA, referencias en Python). report_sha256 prueba=4eca95fe38be59ea5b49e606c5dd4661552dbce66a578aa7819b1236db0fbf52 (determinista 2 corridas). Sin datos de mercado. Queda: ET7 real tras ET2+ET6 · bloqueo: series/trades de ET6 y vector nulo de ET2.
- 2026-11-14 · Orquestador: verificado independientemente (mcs+stub) 35/35. Decisiones registradas como D-036. Respuestas a las preguntas abiertas: corte 0,40 y N_eff=grupos/cobertura aceptados como defecto (se confirman al cerrar ET2); métrica común del percentil nulo = t de la expectativa neta por operación; P_pass bootstrap solo se reporta salvo que ET2 fije ppass_boot_min. Pedido a ET6/ET2: exportar serie de P&L diario y lista de operaciones por finalista; ET2 debe entregar el vector nulo con esa métrica.
- 2026-11-16 · NT8: NQC_ET7_EvidenceTest 35/35.
- 2026-11-16 · D-038: NQC_ET7_Neff.cs (Chao1 sesgo-corregido + IC95 log + Good-Turing), politica SAMPLE_CHAO1 en Evidence (claves neff_method/neff_bound/neff_sample_*), 18 casos nuevos: 54 PASS/0 FALLA en mcs+mono, referencia Python ninjatrader/runbooks/ET7_NEFF_REF.py. Cobertura IC95: 42/50 (G=200, abundancia minima 45) y 10/50 (G=3000, casi todo raro: Chao1 subestima). COVERED intacto (hash 4eca95fe reproducido). Queda: NT8 + datos reales de ET3/ET2 · bloqueo: muestra de ET3 y calibracion ET2.
- 2026-11-16 · Orquestador: estimador N_eff por muestra (Chao1) verificado independientemente (mcs+stub) 54/54, modo COVERED con el mismo hash anterior. Registrado en D-038 con su limitación (límite inferior; IC cubre 84% con grupos grandes y 20% con 3.000 grupos raros): la salvaguarda real es la medición de FP de ET2.
