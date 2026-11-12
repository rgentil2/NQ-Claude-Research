# Plan estadístico — NQC 2.0.0

## 1. Por qué hace falta
Con costos de US$24,68 y un PF de 1,25, la media por operación es solo ~11% del tamaño medio de operación. Para cualquier distribución de resultados:

`t ≤ 0,112 × √n` con PF = 1,25

| Operaciones (n) | t máximo |
|---|---|
| 150 | 1,37 |
| 180 | 1,50 |
| 300 | 1,94 |
| 400 | 2,24 |
| 600 | 2,74 |

Es decir: **cumplir el perfil de PF no prueba nada por sí solo**, y menos tras evaluar decenas de miles de candidatos. Bajo nulidad, el mejor Sharpe anualizado esperado en 3 años crece con el número de ensayos efectivos (≈1,5 con 100; ≈1,9 con 1.000; aproximación normal i.i.d.).

La frecuencia mensual también tiene aritmética propia: con llegadas Poisson (caso favorable), exigir ≥5 operaciones en cada uno de 36 meses requiere una media de ~10 operaciones por mes para pasar ~1 de cada 3 veces, y ~12 para pasar ~3 de cada 4. ET2 debe confirmar si ese umbral es alcanzable.

El costo fijo de US$24,68 pesa menos cuanto más ancho es el stop. Con PF objetivo 1,25 y stop = objetivo, la tasa de acierto necesaria es ~62% con un stop de 35 ticks y ~59% con uno de 80 ticks; con objetivo de 2R baja a ~43% y ~41%. Por eso D-006b eliminó el tope de riesgo por entrada.

## 2. Conteo de ensayos
- Toda evaluación de genoma (ET3), candidato (ET4–ET5) y portafolio (ET6) se registra en `TRIALS_LEDGER`.
- Con hasta 5 campañas de 100.000 evaluaciones (D-011), el conteo acumulado puede llegar a ~500.000 más las evaluaciones de portafolios; el N efectivo, no el bruto, entra al DSR.
- **N efectivo:** agrupamiento jerárquico de las series de P&L diario del IS (distancia √(0,5·(1–ρ)), corte declarado antes de correr); N_eff = número de grupos. Se reporta junto al N bruto.

## 3. Datasets nulos (ET2)
- **N1 — Permutación de sesiones completas:** reordena sesiones enteras; conserva el perfil intradía y la distribución de cada sesión; destruye la dependencia entre sesiones.
- **N2 — Inversión aleatoria de signo por sesión:** conserva volatilidad y forma; destruye el sesgo direccional.
- Ninguno es un random walk arbitrario. Limitaciones documentadas: N1 rompe tendencias multi-día; N2 conserva la autocorrelación intradía en valor absoluto.

## 4. Control positivo (ET2)
- Sobre datasets N1, se inyecta un edge conocido: una regla simple de la misma familia de condiciones, con salidas por ATR, con expectativa neta de referencia de +US$20 por operación a ~0,5 operaciones por sesión [B] (D-020).
- **Potencia** = fracción de datasets inyectados en los que el embudo completo aprueba un portafolio que contiene la regla inyectada (o una equivalente).

## 5. Métricas de evidencia (ET5 y ET7)
- **PBO (CSCV), ET5:** 12 trimestres del IS, combinaciones de mitades; reporta la probabilidad de que el mejor en entrenamiento quede bajo la mediana en prueba. Umbral [B] ≤ 0,40. Sustituye al examen intermedio descartado en D-004b.
- **DSR (Deflated Sharpe Ratio), ET7:** con N_eff, asimetría y curtosis de los retornos diarios del portafolio; umbral [B] ≥ 0,95.
- **Percentil nulo, ET7:** posición del fitness del portafolio frente a la distribución de "mejor portafolio por campaña" obtenida en ET2; umbral [B] ≥ 95.
- **Monte Carlo, ET7:** bootstrap por bloques de 8 operaciones ejecutadas (no corrige la selección, solo mide dispersión).

## 6. Supuestos y límites
- DSR asume ensayos aproximadamente independientes tras el agrupamiento; con selección en varias etapas, el N_eff es una cota aproximada.
- La selección secuencial (ET3→ET6) hace que la distribución nula correcta sea la del **embudo completo**, no la de cada etapa: por eso ET2 corre el embudo entero.
- Sin examen intermedio (D-004b), todo el IS interviene en la selección; la confirmación independiente recae en ET8 y su peso depende de `OOS_EXPOSURE.md`.
- Con la paridad nativa después del OOS (D-023), el OOS lo mide el motor propio; si el motor tuviera un error, el OOS se pudo consumir con una medición incorrecta. El veredicto final lo da NT8 nativo en ET9.
- Si ET2 entrega 0 aprobados tanto en nulo como en real, eso no basta: se comparan las distribuciones intermedias (fitness, supervivientes por etapa, frecuencia ejecutada, eventos protectores).
