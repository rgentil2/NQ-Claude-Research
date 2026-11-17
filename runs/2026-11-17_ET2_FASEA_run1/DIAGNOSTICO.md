# ET2 Fase A — diagnóstico (2026-11-17)

Fuente: `FASEA_LIGERO.zip` del usuario (SUMMARY, databank, rechazos de ET4/ET6, informes de ET5/ET7 de las 30 campañas) procesado con `NQC_ET2_ReportBuilder` (informe `ET2_CALIBRATION_REPORT.txt`, `report_sha256 860e393e…`, veredicto mecánico: GATE NO CUMPLIDO; no hay vector nulo de ET7 porque ninguna campaña llegó a ET7).

## Resultado
Falsos positivos 0/20 (IC95 [0%; 16,8%]); potencia 0/10 (IC95 [0%; 30,8%]). ET5 cierra 17/30 por PBO > 0,40; las otras 13 terminan sin finalistas en ET6.

## Hallazgos (calculados de los archivos; interpretación marcada como tal)
1. **El edge inyectado es indetectable por diseño.** `t_pair` = 0,71 (343 operaciones, +US$20 netos, 0,447/sesión); el mejor genoma de ET3 en datasets NULOS tiene t mediana ≈ 6,0 (rango 4,5–13,7). Un edge de t 0,7 no puede emerger de una búsqueda que ya encuentra t ≈ 6 por azar. (D-020/§5(d) de ET2.)
2. **La aptitud (t de la expectativa) se sobreajusta masivamente en TODOS los datasets**, nulos incluidos. Los 200 candidatos del databank de cada campaña tienen mean_net mediana US$149 (N1) / 293 (N2) / 151 (INJ) por operación tras costos; ET4 deja pasar 200/200 en las 30 campañas (F1, F2, F3). Interpretación (hipótesis): el GA explota rarezas del conjunto de entrenamiento (8 condiciones en AND, stops de ~5 ATR con objetivos de ~2,75 ATR: estrategias de alta tasa de acierto y pérdida rara). Quien frena esto son ET5 (PBO) y ET7: FP 0/20 indica que las capas defensivas funcionan.
3. **Los candidatos se agrupan en el mínimo de operaciones.** trades: mín 100, P5 102, mediana 171, 46% < 150, 19% < 110. ET3 exige ≥ 100 y ET6 exige ≥ 150 y ≥ 5 en cada mes: inconsistentes entre sí.
4. **ET6 descarta TODAS las combinaciones de 3 por Symmetry** (3.084 de 3.084 descartes duros; también `ops_por_mes` 83%, `peor_dia` 59%, `DD_M2M` 51%, `operaciones` 49%). El 80% del databank (4.801/6.000) y el 80% del pool de ET6 (721/900) son cortos (`dir=-1`): combinaciones de miembros de la misma dirección tienen Symmetry 0.
5. Efecto sobre ET7: no se ejercitó con datos (0 campañas llegan).

## Limitación de interpretación
Los datasets «nulos» N1/N2 conservan la estructura intradía de las sesiones reales (permutar sesiones o invertir el signo de cada una no destruye patrones dentro de la sesión): no son nulos para efectos intradía. Si el IS real tuviera algún patrón intradía, los nulos lo conservarían. Es un límite documentado del diseño (ET2.md §2 y STATISTICS_PLAN §3), no se ha demostrado aquí que exista tal patrón.

## Pendiente
Decisión del usuario sobre cómo rediseñar el embudo (versión menor, NQC 2.0.x) y repetir la Fase A (21 min). Opciones y recomendación en la conversación del 2026-11-17.
