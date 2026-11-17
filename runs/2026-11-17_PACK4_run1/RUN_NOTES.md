# PACK4 run1 — paquete NQC_FULL_pack4 en NT8 (runner NQCRUN-1.3.0, lote NQCRUNBATCH-1.1.0) — 2026-11-16/17

Máquina: Lenovo Legion 5, i9-13900HX (32 hilos lógicos), 16 GB. Datos: IS 2020-2022, 1.054.234 barras, 933 sesiones.

## Pruebas
| Prueba | Resultado en NT8 | Comentario |
|---|---|---|
| ET1_Regression (Core con `BuildDays` memorizado) | **PASS** 8.110/8.110, 46/46, `trades_hash_n1n9` `760227dc…` idéntico al anterior, 14 sintéticos OK | el cambio de Core no alteró ninguna operación |
| ET1B EngineParity | PASS (`15c0da5f…`, igual que la corrida aprobada de ET1) | |
| RunFunnelTest | PASS 37/37, `funnel_digest_A` `3fd26becc…` | igual que mono |
| RunBatchTest | PASS 20/20, `batch_digest_A` `6bfe495c…` | igual que mono |
| ET6 PortfolioTest | PASS 22/22 (`046e4b38…`, `7bb9f9b9…`) | igual que mono |
| ET7 EvidenceTest | PASS 58/58 | |
| ET4 PruneTest | ver ET4PRUNE/ | |
| **RunPerfTest** | **FALLA 23/24**: A1 «huella de 134.688 valores de familia idéntica a la del commit 4006522» da `69d2da26d275`, `perf_digest_A` `acf9faeb…` (mono: `a2cbd6fd…`) | La constante dorada se calculó en mono. Hipótesis NO confirmada: diferencia de último ulp entre runtimes (funciones trascendentes). Diagnóstico por familia y rediseño de A1 (comparar ruta optimizada vs referencia en el MISMO runtime) delegados. Hasta confirmarlo, **no se declara demostrado que la optimización conserve los valores en NT8**; sí consta que el ledger es idéntico para 1/4/8/16/24 hilos y que todos los demás digests coinciden entre NT8 y mono. |

## Medición de tiempos (TIMING_HILOS, 200 evaluaciones por valor de hilos, datos y caché fríos; TIMING_REGIMEN, 10.000 evaluaciones con 8 hilos)
| Hilos | pared total (s) | ms de pared por evaluación | speed-up | eficiencia | ms medios dentro del hilo |
|---|---|---|---|---|---|
| 1 | 7,67 | 38,3 | 1,00 | 1,00 | 35,2 |
| 4 | 2,79 | 13,9 | 2,75 | 0,69 | 43,2 |
| 8 | 2,00 | 10,0 | 3,84 | 0,48 | 54,2 |
| 16 | 1,96 | 9,8 | 3,91 | 0,24 | 100,5 |
| 24 | 1,83 | 9,1 | 4,20 | 0,17 | 138,3 |

`resultado_identico_entre_hilos=SI` (ledger `cc7eda818fa9` en todos). El escalado se satura en ~4× a partir de 8 hilos: el tiempo dentro del hilo crece con los hilos (contención de memoria/caché y núcleos E), así que **8 hilos es el punto razonable**.
Régimen (10.000 evaluaciones, 8 hilos, memo 4.096 MB): **2,65 ms/evaluación de pared** (2,35 en la segunda mitad); media en hilo 18,7 ms, mediana 11,0; pico del proceso **5,5 GB**.
Extrapolación SOLO de ET3: 100.000 evaluaciones ≈ 4–5 min; 30 campañas FULL ≈ 2,2 h (con 8 hilos y régimen). ET4–ET7 NO medidos.

## Lectura
- La medición del paso 0 anterior (284 ms/evaluación, 1 hilo) era sobre todo coste de arranque en frío con caché de 2.048 MB y `BuildDays` recalculado en cada corrida del ejecutor. Hipótesis del agente confirmada en lo gordo: 35 ms en frío con 1 hilo, ~2,6 ms de pared a régimen con 8 hilos.
- Límites de esta extrapolación: TIMING evalúa sobre un dataset N1; el coste real depende de que los genomas del GA generen señales (el ejecutor trabaja más); ET4 evalúa decenas de miles de vecinos por campaña, ET7 agrupa hasta 5.000 series (coste no medido).
- Pico de memoria 5,5 GB con 16 GB de RAM: viable dejando ≥ 6 GB libres.
