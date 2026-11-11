# Lecciones de los repos anteriores

## Lo que se hereda (funcionó)
- **Contrato versionado con reglas legibles por máquina** y validación en CI (PF1).
- **OOS sellado** y política explícita de no reoptimizar.
- **Preregistro y corrección por múltiples comparaciones** (series T/H): disciplina estadística correcta.
- **Ledger con SHA256** de genomas y de barras utilizables; identidad canónica sin duplicados.
- **Ejecución nativa en Strategy Analyzer** con salidas TSV automáticas (sin exportar datos).
- **Ejecutor central de 1 NQ** como objeto de evaluación (no sumar curvas individuales).
- **Modelo de costos** (2+2 ticks + comisión) y estrés 1,5×.
- **Definiciones explícitas** de Stability y Symmetry.
- **Exclusión de días por regla** y conteo de barras válidas.
- **Precedente de paridad exacta** entre motores sobre los mismos datos.
- **Biblioteca de condiciones** (120 fórmulas, 1.320 condiciones) como punto de partida del generador.

## Lo que se evita (falló o generó deuda)
| Problema observado | Cómo lo evita NQC |
|---|---|
| Documentos maestros de 400+ KB y estados desincronizados (README/master decían "pendiente" con E5 ya ejecutado) | Master ≤ ~300 líneas; estado solo avanza con log NT8 + hash |
| Código NinjaScript entregado solo como ZIP por chat | `ninjatrader/src/` es la única fuente (D-013) |
| Bloqueo de DD sin regla de liberación (estado absorbente) | Capa protectora con alcance de sesión (D-007) |
| E2 sin criterio de rentabilidad y E3 muestreando del pool | Evolución guiada por fitness (ET3) y poda (ET4) antes de armar portafolios |
| Complementariedad medida con correlación mensual de curvas independientes (favorece estrategias escasas) | Ejecutor en el bucle de construcción (ET6) |
| Prioridad entre señales por ID ascendente | Prioridad por fitness en VAL (D-016) |
| Umbrales no calibrados (≥5/mes en 36 meses resultó casi inalcanzable para el pool) | Factibilidad de umbrales medida en ET2 antes de usar datos reales |
| Sin control nulo ni control positivo | ET2 obligatorio |
| Exposición del OOS reconocida en una línea | `OOS_EXPOSURE.md` con peso de evidencia |
| Proliferación de versiones (V1–V25, S0.xx, PF1) con nombres que chocaban | Nombres reservados ET0–ET10, NQC x.y.z, C1–C3 |
| Benchmarks de velocidad sobre datos sintéticos usados como progreso | Solo cuentan corridas NT8 con gate y hash |
| Repo público con especificaciones de estrategias | D-014 |

## Resultados previos: cómo se tratan
- Ninguna estrategia anterior (T05, T186, T215, H-series, PF1_NT8_PRE_3_042) se considera validada. Pueden inspirar condiciones de la biblioteca; si se reutilizan, entran como un genoma más y cuentan como ensayo.
- Resultado de referencia de PF1 (reportado por el usuario, no versionado en el repo anterior): 300 portafolios, 0 aprobados, máximo 109 operaciones, 9 con neto positivo, 4 con PF ≥ 1,25. Es compatible con ausencia de edge más costos y con truncamiento por el bloqueo de DD; no discrimina entre ambos.
