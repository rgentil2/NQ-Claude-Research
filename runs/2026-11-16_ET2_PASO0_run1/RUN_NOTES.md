# ET2 paso 0 — medición de tiempos con NQC_RUN_Batch (2026-11-16)

Usuario corrió el paquete `NQC_FULL_pack2` en NT8: pruebas sintéticas y el paso 0 de ET2 (TIMING sobre un dataset N1, nunca sobre el IS original; sin economía).

## Pruebas sintéticas del pack 2 (NT8)
- `NQC_RUN_BatchTest` 20/20, `batch_digest_A` `4629d9cc…` (igual a mono).
- `NQC_RUN_FunnelTest` 33/33, `funnel_digest_A` `b98df179…` (igual a mono; runner NQCRUN-1.1.0).
- `NQC_ET7_EvidenceTest` 54/54. ET1C 22/22 (`b9f84a4e6a75`).
- Nota: el pack 2 lleva ET7 54 pruebas y runner 1.1.0; el pack 3 (runner 1.2.0, ET7 58) aún no se ha corrido en NT8.

## Paso 0 (NQC_RUN_Batch, TIMING_N1)
- Datos: 1.054.234 barras, 933 sesiones (IS 2020-01-01..2022-12-31), R2 zona 546/774 (70,54%). Biblioteca NQCL-3.1.0 `dac56e19fbc7b431`. Transformador N1 (semilla 20269001), 2.000 evaluaciones, población 200.
- **ms por evaluación de genoma: media 284,0; mediana 117,2; p90 403,9; máximo 28.726; media sin el 5% inicial 259,3.** Tiempo total 568 s.
- Memoria gestionada 2.228 MB (presupuesto de caché 2.048 MB); pico del proceso 2.531 MB.
- Extrapolación (solo ET3, media × evaluaciones; ET4–ET7 no medidos): 10.000 evaluaciones = 0,79 h; 100.000 = **7,89 h**; 30 campañas REDUCED = 23,7 h; 20 FULL = 157,8 h; **30 FULL = 236,7 h (~9,9 días)**. Con la mediana: 30 FULL = 97,6 h.
- Lectura: la cola larga (mediana 117 ms frente a media 284 y máximo 28,7 s) apunta a genomas con familias caras o a fallos de caché; hay margen de optimización. D-037 («fase completa en todo») supera el umbral de 48 h/1 semana del runbook ET2: decisión del usuario pendiente.

Archivos: `BATCH_SUMMARY.txt`, `TIMING_N1_SUMMARY.txt`, `RUN_BATCH_MANIFEST_USED.txt`, `pack2_tests/`.

## Máquina del usuario (declarada por el usuario, 2026-11-16)
Lenovo Legion 5, Intel Core i9-13900HX (24 núcleos: 8 P + 16 E; 32 hilos lógicos), 16 GB DDR5-5600, RTX 4070 (no usada por el motor). Windows, NinjaTrader 8 (.NET Framework 4.8). Implicaciones: la memoria (16 GB; pico de 2,5 GB por proceso) y la térmica en carga sostenida limitan más que el número de núcleos; los núcleos E rinden menos que los P, así que el escalado en hilos no será lineal.
