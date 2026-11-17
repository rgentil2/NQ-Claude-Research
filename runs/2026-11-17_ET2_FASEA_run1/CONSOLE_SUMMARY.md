# ET2 Fase A — resultado leído del log de consola de NT8 (2026-11-17)

Lote `ET2_FASE_A`, 30 campañas REDUCED (población 200 × 50 generaciones, 8 hilos), IS completo, pack 5 (runner NQCRUN-1.3.0, sin los exports de operaciones de miembros). Total **1.259 s (21 min)**; `batch_digest=98092b3f2433`. Fuente: líneas `NQC_RUN BATCH …` pegadas por el usuario; los archivos de las campañas aún no se han recibido (carpeta >30 MB por los `.bin` de la muestra de N_eff).

| Grupo | Campañas | Cerradas en ET5 (PBO > 0,40) | Sin finalistas en ET6 | Llegan a ET7 |
|---|---|---|---|---|
| N1 nulo (A00–A09) | 10 | 4 (A00, A03, A04, A08) | 6 (A01, A02, A05, A06, A07, A09) | 0 |
| N2 nulo (A10–A19) | 10 | 7 (A11–A16, A19) | 3 (A10, A17, A18) | 0 |
| N1+INJ inyectado (A20–A29) | 10 | 7 (A20–A23, A26, A27, A29) | 3 (A24, A25, A28) | 0 |

- Falsos positivos: **0/20** nulos. Potencia: **0/10** inyectados (ninguna campaña inyectada produjo un solo finalista).
- PBO de las 30 campañas: rango 0,00–0,99; cerradas 17/30 (55% de los nulos, 70% de los inyectados). Las 13 que superaron ET5 terminaron con **0 finalistas en ET6** (nulas e inyectadas por igual).
- ET4: `pool=200` de 200 en todas (los 200 candidatos del databank pasan F1–F3 en datasets nulos).
- ET7 no se ejercitó con ninguna campaña.
- ET3: ~7.400–9.100 evaluaciones por campaña; ET4 ~2.200–5.400 ensayos nuevos de vecinos; ET6 ~510–543 ensayos (en las 13 que llegan).
- Tiempos por campaña: ~23–77 s. Memoria gestionada hasta ~8,2 GB (pico), `hilos=8`.
