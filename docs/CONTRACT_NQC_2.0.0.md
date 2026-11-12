# Contrato NQC 2.0.0 — Embudo NQ en NinjaTrader 8

**Estado:** **CONGELADO (Nivel A)** desde 2026-11-11 como NQC 2.0.0. Reemplaza a NQC 1.0.0, que quedó congelado el mismo día y fue modificado por decisión del usuario **antes de ejecutar ninguna etapa sobre datos de mercado** (sin resultados vistos, sin ensayos que sumar). Pendiente D-009 (reglas MFFU), que bloquea ET1 pero no cambia la estructura.
**Cambios respecto de 1.0.0:** ver `docs/CHANGELOG.md`.

## 0. Principios

1. **Se juzga el portafolio ejecutado**, no la suma de estrategias: 1 NQ compartido, conflictos resueltos por el ejecutor.
2. **Primero se demuestra que el instrumento de medida funciona** (ET1, ET2); después se mira el mercado (ET3+).
3. **Cada dato tiene un uso declarado.** El IS completo para construir y seleccionar; cada OOS una sola vez para confirmar.
4. **Cada evaluación cuenta como ensayo** (`docs/TRIALS_LEDGER.md`) y entra en la corrección por búsqueda múltiple.
5. **Sin evidencia ejecutada no hay aprobación.** Runbook, código que compila o ZIP entregado ≠ etapa ejecutada.
6. **Los umbrales no se mueven después de ver resultados reales** (ver §3).
7. **Arquitectura (R3):** motor propio dentro de NinjaScript, lanzado desde Strategy Analyzer, para ET3–ET8 (agilidad). Órdenes nativas de NT8 en ET1 (referencia) y ET9 (paridad final). NT8 es quien dice la verdad sobre los fills.
8. **Unidades (R5):** las condiciones del generador se expresan en múltiplos de ATR, ticks o puntos; nunca en niveles absolutos ni porcentajes de precio (la serie es back-adjusted).
9. **El riesgo por operación no se limita en la entrada (D-006b).** Se controla en las puertas del portafolio: peor día, DD máximo, P_pass y la capa protectora. El generador es libre de elegir el ancho del stop.

## 1. Datos y ventana

| Elemento | Definición |
|---|---|
| Serie | NQ, M1, Last, **Merge back adjusted** con el roll de NinjaTrader (R1). No se excluyen sesiones de roll. |
| Zona horaria | La configurada en NinjaTrader (Eastern), no la del PC (R2). ET0 la verifica con el pico de volumen de 09:30. |
| IS | 2020-01-01 → 2022-12-31. Se usa completo para construir y seleccionar (D-004b). |
| OOS1 | 2023-01-01 → 2024-12-31. Confirmación sellada. |
| OOS2 | 2025-01-01 → 2026-10-31. Confirmación sellada (exposición previa documentada). |
| Ventana (D-010) | Inicio 09:30 ET; **plano a las 11:30 ET**; sin hora límite de entrada. NT8 sella cada barra con su hora de cierre: sellos 09:31 a 11:30 = 120 barras M1 por sesión. |

## 2. Etapas

Cada etapa define: propósito · entradas · proceso · salidas · **gate** · qué hacer si falla. Los umbrales marcados **[B]** son Nivel B: valor heredado o propuesto, calibrable solo en ET2.

### ET0 — Datos y entorno
- **Propósito:** garantizar que los datos M1 de NQ en NT8 son completos, reproducibles y están en la zona horaria correcta.
- **Entradas:** histórico M1 NQ en NT8 (2020-01 → 2026-10), plantilla de horario habitual, NT8 en zona Eastern.
- **Proceso:** `NQC_ET0_DataManifest` (estrategia sin órdenes en Strategy Analyzer): cuenta barras por sesión en la ventana, detecta huecos, comprueba la zona horaria con el pico de volumen entre 09:16 y 10:00, y registra instrumento, periodo, plantilla, zonas horarias y política de empalme declarada. **No registra precios ni calcula métricas económicas.** Para OOS solo verifica integridad.
- **Salidas:** `DAYS.tsv`, `MONTHS.tsv`, `SUMMARY.txt` con SHA256 de los dos TSV.
- **Gate:** G0 cobertura 2020-01 a 2026-10; G1 instrumento NQ; G2 periodo M1; G3 ≥99% de las sesiones del IS completas (120/120 barras) [B]; G4 el volumen máximo entre 09:16 y 10:00 cae en la barra 09:31 o 09:32 en ≥70% de las sesiones del IS [B]; G5 sin barras con segundos distintos de cero; G6 dos corridas dan los mismos hashes.
- **Días excluidos:** sesiones INCOMPLETAS (menos de 120 barras) y SIN_VENTANA. La lista completa queda en `DAYS.tsv`.
- **Si falla:** corregir datos, configuración de zona horaria o regla de exclusión; no avanzar.

### ET1 — Motor, simulador de cuenta y paridad
- **Propósito:** tener un motor cuya ejecución, contabilidad y simulación de cuenta estén demostradas **antes** de mirar resultados económicos.
- **Componentes:** ejecutor central de 1 NQ, modelo de costos, capa protectora, simulador de cuenta MFFU, salidas de auditoría. Semántica completa en `docs/EXECUTOR_SPEC.md`. **No hay tope de riesgo por entrada** (D-006b).
- **Pruebas obligatorias:**
  - **P1 Paridad contra NT8 nativo (R4):** 5 a 10 estrategias simples escritas con órdenes nativas de NT8, corridas en Strategy Analyzer sobre el IS, se comparan operación por operación con el motor propio. Deben cubrir: long y short; **stop por ATR con periodo y múltiplo distintos; objetivo por ATR; operaciones sin objetivo (solo stop y salida de las 11:30)**; SL y TP en la misma barra; salida forzada a las 11:30; entrada en el último instante permitido; gap adverso; y dos miembros con prioridad. El ATR del motor debe coincidir con el indicador ATR de NT8. Tolerancia: 0 diferencias en barra de entrada, barra de salida y dirección; precio dentro de 0 ticks salvo convenciones documentadas. La convención de fills de órdenes límite se fija aquí contra NT8 y se congela.
  - **P2 Casos límite del motor:** señal en la última barra, cruce de sesión, señales simultáneas, día excluido.
  - **P3 Costos:** fricción aplicada exactamente una vez por operación; estrés 1,5× correcto; el total por operación coincide con el de NT8 configurado con slippage 2 ticks y comisión US$4,68 RT.
  - **P4 Capa protectora:** cada evento (pausa, cierre, guardia de DD) se dispara y se libera según la semántica escrita, en escenarios construidos a mano.
  - **P5 Simulador de cuenta:** ≥10 trayectorias de equity sintéticas con resultado conocido (pasa, falla por MLL, falla por consistencia, días mínimos) dan el resultado esperado.
  - **P6 Determinismo:** misma configuración y semilla → mismo hash de salida.
- **Regla:** en ET1 se puede correr sobre el IS solo para pruebas técnicas; **no se reportan ni se miran** PF, beneficio o DD de estrategias de investigación. Las estrategias de referencia son triviales y sus resultados no son hallazgos.
- **Prerrequisito:** reglas MFFU verificadas (D-009).
- **Salidas:** código en `ninjatrader/src/`, `ET1_PARITY_REPORT.txt`, hashes.
- **Gate:** P1–P6 al 100%.
- **Si falla:** corregir el motor. Ninguna etapa posterior puede ejecutarse sobre datos reales.

### ET2 — Calibración ciega (nulos, control positivo y factibilidad)
- **Propósito:** medir con qué frecuencia el embudo completo (ET3–ET7) aprueba algo **sin edge**, con qué frecuencia **detecta un edge conocido**, y si los umbrales [B] son alcanzables por un portafolio con edge real.
- **Entradas:** módulos ET3–ET7 ya implementados y aprobados técnicamente; datasets nulos y con edge inyectado construidos desde el IS (métodos en `docs/STATISTICS_PLAN.md`).
- **Proceso:** correr el embudo completo, en tamaño reducido declarado, sobre: (a) ≥5 datasets nulos; (b) ≥5 datasets con edge inyectado de tamaño de referencia; registrar distribuciones intermedias en cada etapa y la tasa de paso de cada gate.
- **Salidas:** `ET2_CALIBRATION_REPORT.txt`: tasa de falsos positivos por gate y global, potencia, distribución nula del mejor portafolio por campaña, factibilidad de cada umbral [B].
- **Gate:**
  - Falsos positivos (portafolio aprobado en dataset nulo) ≤ 5% por campaña [B].
  - Potencia (portafolio aprobado en dataset con edge de referencia) ≥ 70% [B].
- **Acción al cerrar:** se fijan los umbrales [B] con la evidencia de ET2 y se publica **NQC 2.1.0 (congelado numérico)**. Desde aquí **no se mueven**.
- **Si falla la potencia:** se rediseña el embudo (versión menor) y se repite ET2. **Nunca** se pasa a datos reales con un embudo que no detecta un edge conocido.

### ET3 — Generación evolutiva (IS)
- **Propósito:** búsqueda **guiada por fitness**, no aleatoria.
- **Genoma (D-021):** condiciones de entrada en unidades de ATR/ticks/puntos (R5) más tres genes de salida, todos **generados por la evolución**:
  - **Periodo del ATR** (barras M1).
  - **Stop = k_sl × ATR** (obligatorio).
  - **Objetivo = k_tp × ATR** (**opcional**: el gen puede valer "sin objetivo"; en ese caso la operación sale por stop o por el plano de las 11:30).
  - No hay stops ni objetivos en ticks fijos. No hay tope de riesgo por entrada.
  - El ATR se calcula al cierre de la barra de señal y el stop y el objetivo quedan fijos durante la operación.
- **Rangos de búsqueda por defecto (D-019, a confirmar):** periodo 5–100; k_sl 0,5–6,0 en pasos de 0,25; k_tp 0,5–8,0 en pasos de 0,25 o ausente.
- **Proceso:** algoritmo genético sobre genomas de 20 genes. Biblioteca inicial: subconjunto de la anterior coherente con la ventana (D-018). Población inicial solo exige frecuencia mínima; la evolución selecciona.
- **Fitness [B]:** estadístico t de la expectativa neta de costos por operación en el IS, con mínimo de 100 operaciones.
- **Presupuesto:** máximo **5 campañas** (C1–C5) por versión de contrato; cada campaña ≤ 100.000 evaluaciones [B]; semillas declaradas antes de correr.
- **Salidas:** databank top-K por campaña (K = 2.000 [B]), registro de todas las evaluaciones en `TRIALS_LEDGER`, curva de fitness por generación.
- **Gate (técnico):** corrida completa, ledger sellado con hash, curva de fitness registrada. No hay gate económico aquí.

### ET4 — Poda barata por estrategia (IS)
Filtros en orden de costo creciente:
- **F1 Costo estresado:** neto > 0 con costos 1,5×.
- **F2 Consistencia temporal:** neto > 0 en ≥ 7 de los 12 trimestres del IS [B].
- **F3 Estabilidad de vecindario:** al perturbar cada parámetro ±1 paso (incluidos periodo del ATR, k_sl y k_tp), ≥ 70% de los vecinos mantienen neto > 0 [B].
- **F4 Ancho del stop (informativo, no elimina):** distribución del stop en puntos y en US$.
- **Salida:** pool podado + causa de rechazo por candidato.

### ET5 — Robustez interna por bloques (IS, sin examen único)
D-004b eliminó el examen intermedio; esta etapa lo compensa con estabilidad entre bloques y medición del sobreajuste del procedimiento.
- **Por candidato [B]:** neto > 0 tras costos en ≥ 2 de los 3 años calendario del IS.
- **Del pool:** PBO (CSCV sobre los 12 trimestres del IS) ≤ 0,40 [B].
- Si el pool queda vacío, se cierra la campaña (cuenta en el presupuesto de ET3).

### ET6 — Portafolios con el ejecutor en el bucle (IS)
- **Construcción:** búsqueda greedy/beam sobre el pool robusto; cada combinación de 3–5 miembros se evalúa **con el ejecutor central** (no con correlaciones de curvas independientes). La prioridad entre miembros se fija por fitness (D-016) y queda congelada en la especificación del portafolio. Cada combinación evaluada cuenta como ensayo.
- **Stability y Symmetry son propiedades del portafolio (D-022):** se calculan sobre las operaciones ejecutadas del portafolio. Durante la construcción son **restricciones duras** (una combinación que no las cumple se descarta) y además son gates de aceptación. Las estrategias individuales de ET3–ET5 **no** se filtran por Stability ni por Symmetry.
- **Perfil de aceptación (heredado de PF1, todos [B]):**

| Métrica (sobre operaciones ejecutadas) | Umbral |
|---|---|
| Profit Factor neto | ≥ 1,25 |
| Stability (definición en EXECUTOR_SPEC) | > 30% |
| Symmetry (definición en EXECUTOR_SPEC) | > 30% |
| DD máximo (cerrado y mark-to-market M1) | ≤ US$2.000 |
| Peor pérdida diaria (implica un límite a la peor operación) | ≤ US$1.000 |
| Operaciones IS | ≥ 150 |
| Operaciones por mes | ≥ 5 cada mes |
| Neto con costos 1,5× | > 0 |
| Probabilidad de pasar la evaluación (P_pass, arranques rodantes) | ≥ umbral fijado en ET2 |

- **Salida:** hasta 5 portafolios finalistas con `PORTFOLIO_SPEC.json` (miembros, genes, prioridad, parámetros, hashes).

### ET7 — Evidencia estadística y congelado del portafolio
- **DSR** (Deflated Sharpe) con N efectivo estimado del `TRIALS_LEDGER` acumulado: ≥ 0,95 [B].
- **Percentil frente a la distribución nula de ET2** del mejor portafolio por campaña: ≥ 95 [B].
- **Monte Carlo** (bootstrap por bloques de 8 operaciones, 500 exploratorias / 5.000 confirmatorias, semilla fija): P95 del DD ≤ US$2.000; P_pass bootstrap reportada.
- **Congelado (D-023):** al aprobar, el portafolio queda congelado: hash de `PORTFOLIO_SPEC.json` + hash del motor (`.cs`) + commit, registrados **antes de abrir el OOS**.
- **Salida:** `ET7_EVIDENCE_REPORT.txt` y `PORTFOLIO_FROZEN.txt`. Solo los portafolios que pasan llegan a ET8.

### ET8 — OOS sellado (una vez, en orden)
- **Quién evalúa (D-023):** el motor propio, con el portafolio ya construido y congelado. No se modifica nada.
- **Antes de abrir:** `PORTFOLIO_FROZEN.txt` commiteado y `docs/OOS_EXPOSURE.md` actualizado.
- Se corre OOS1 y luego OOS2, cada uno una sola vez, sin cambiar nada entre ellos.
- **Gate (sobre OOS1+OOS2 acumulado, heredado de PF1 [B]):** PF neto ≥ 1,25; Stability > 30%; Symmetry > 30%; DD continuo sin reinicio anual ≤ US$2.000; peor pérdida diaria ≤ US$1.000; neto > 0 en cada bloque; P_pass ≥ umbral de ET2.
- **Interpretación:** el resultado se reporta junto con el peso de evidencia asignado en `OOS_EXPOSURE.md`. Un bloque con exposición previa no se presenta como independiente.
- **Veredicto:** provisional hasta ET9. El veredicto final lo da NinjaTrader nativo (ver ET9).
- **Si falla:** el portafolio muere. **No se ajusta.** Una nueva campaña ya no puede usar OOS1/OOS2 como confirmación independiente (ver §4).

### ET9 — Paridad nativa en NinjaTrader (después del OOS)
- **Propósito:** comprobar que NinjaTrader, con órdenes nativas, reproduce lo que midió el motor propio, usando el portafolio ya congelado y ya evaluado en el OOS.
- **Proceso:** el portafolio congelado se implementa como estrategia NT8 con órdenes nativas, **generada a partir de `PORTFOLIO_SPEC.json` sin cambiar el spec**, y corre en Strategy Analyzer sobre el IS, OOS1 y OOS2. Se compara con el motor bloque por bloque.
- **Cuándo corre:** siempre para los portafolios que pasan ET8. Para los que no pasan es opcional y solo sirve para descartar un error del motor.
- **Gate [B]:** ≥ 99% de las operaciones con la misma barra de entrada y dirección que el motor; diferencia media de precio ≤ 1 tick; diferencia de neto total ≤ 2%, en cada bloque.
- **Regla de veredicto (D-023):** **NT8 nativo manda.** Si la paridad falla, los gates de ET8 se recalculan con los resultados nativos y ese veredicto es el definitivo, aunque cambie el de ET8. Una corrección de un error del motor está permitida solo si el `PORTFOLIO_SPEC.json` no cambia, y se documenta en el changelog. Nunca se modifican los miembros, los genes ni la prioridad.
- **Riesgo asumido:** si el motor tenía un error, el OOS se pudo consumir con una medición incorrecta. Lo mitiga ET1 (P1 compara motor y NT8 con estrategias de referencia antes de cualquier búsqueda) y la regla anterior.

### ET10 — Habilitación y evaluación MFFU
- **Prerrequisitos:** ET8 y ET9 aprobadas.
- **Pre-vuelo:** reglas MFFU verificadas en fuente primaria con fecha; guardas de contratos máximos; calendario de roll; comportamiento ante desconexión; kill-switch manual; registro de operaciones; plano a las 11:30 ET garantizado.
- **Plan de intentos (pre-registrado antes del primer intento):** número máximo de intentos/resets, costo máximo y fecha límite global.
- **Monitoreo:** comparación de resultados en vivo contra la distribución esperada (MC de ET7); alarma si cae por debajo del P5.
- **Opcional (D-003 deja esto a decisión del usuario):** registro automático de señales sin órdenes a partir de noviembre 2026, como única fuente de datos realmente nuevos.

## 3. Control de cambios

| Tipo de cambio | Cuándo se permite | Requisito |
|---|---|---|
| Corrección de bug sin mirar resultados | Siempre | Repetir pruebas ET1 afectadas; nota en CHANGELOG |
| Umbral [B] | Solo dentro de ET2 | Evidencia de calibración; se congela en NQC 2.1.0 |
| Umbral [B] después de ET2 | Nunca dentro de la versión | Versión mayor NQC 3.0.0, justificación, ensayos previos se suman |
| Estructura (Nivel A) | Nunca dentro de la versión | Versión mayor + aprobación explícita del usuario |
| Decisión firme (D-001…, R1…) | Solo por el usuario | Versión mayor |

**Regla del primer vistazo:** ver el resultado de una etapa no puede cambiar las reglas de esa etapa ni de las anteriores dentro de la misma versión.

## 4. Política de OOS y campañas

- Cada campaña tiene derecho a **un** paso por ET8.
- Si una campaña falla en ET8 (con el veredicto final de ET9), las siguientes campañas solo pueden usar como confirmación independiente **datos posteriores a 2026-10-31** (acumulados desde la fecha de congelado); OOS1/OOS2 pasan a ser evidencia con peso reducido, declarado en `OOS_EXPOSURE.md`.
- **Criterio de parada global [B]:** si las 5 campañas terminan sin portafolio aprobado en ET8/ET9, se declara "sin edge encontrado con esta arquitectura" y cualquier continuación requiere NQC 3.0.0 con una hipótesis distinta, no más búsqueda con la misma.
- Cada campaña adicional aumenta el número de ensayos acumulados (hasta ~500.000 con 5 campañas de 100.000) y endurece, por construcción, el DSR de ET7.
