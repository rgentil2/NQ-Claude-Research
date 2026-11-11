# Contrato NQC 1.0.0 — Embudo NQ en NinjaTrader 8

**Estado:** PROPUESTO — se congela (Nivel A) cuando el usuario apruebe en `docs/DECISIONS.md`.
**Fecha:** 2026-11-11. **Reemplaza a:** PF1 v1.0.0 del repo `MES-Trading-Research` (archivo histórico, no se modifica).

## 0. Principios

1. **Se juzga el portafolio ejecutado**, no la suma de estrategias: 1 NQ compartido, conflictos resueltos por el ejecutor.
2. **Primero se demuestra que el instrumento de medida funciona** (ET1, ET2); después se mira el mercado (ET3+).
3. **Cada dato tiene un solo uso declarado.** BUILD para construir, VAL una vez para validar, OOS una vez para confirmar.
4. **Cada evaluación cuenta como ensayo** (`docs/TRIALS_LEDGER.md`) y entra en la corrección por búsqueda múltiple.
5. **Sin evidencia ejecutada no hay aprobación.** Runbook, código que compila o ZIP entregado ≠ etapa ejecutada.
6. **Los umbrales no se mueven después de ver resultados reales** (ver §3).

## 1. Particiones de datos

| Bloque | Fechas | Uso | Etapas |
|---|---|---|---|
| BUILD | 2020-01-01 → 2021-12-31 | Generación y poda | ET3, ET4 |
| VAL | 2022-01-01 → 2022-12-31 | Validación interna, **una evaluación por candidato** | ET5 |
| IS completo | 2020-01-01 → 2022-12-31 | Dinámica de portafolio, paridad | ET6, ET7, ET8 |
| OOS1 | 2023-01-01 → 2024-12-31 | Confirmación sellada | ET9 |
| OOS2 | 2025-01-01 → 2026-10-31 | Confirmación sellada (exposición previa documentada) | ET9 |

La división BUILD/VAL dentro del IS es la decisión **D-004b (propuesta)**; IS/OOS1/OOS2 es firme (D-004).

## 2. Etapas

Cada etapa define: propósito · entradas · proceso · salidas · **gate** · qué hacer si falla. Los umbrales marcados **[B]** son Nivel B: valor heredado o propuesto, calibrable solo en ET2.

### ET0 — Datos y entorno
- **Propósito:** garantizar que los datos M1 de NQ en NT8 son completos, reproducibles y con política de roll explícita.
- **Entradas:** histórico M1 NQ en NT8 (2020-01 → 2026-10), configuración de sesión (CME US Index Futures ETH, zona ET).
- **Proceso:** NinjaScript `NQC_ET0_DataManifest`: cuenta barras por sesión, detecta huecos, aplica regla objetiva de exclusión de días, registra política de contrato continuo/roll y versión de NT8. **No calcula ninguna métrica económica.** Para OOS solo verifica integridad (conteos y huecos).
- **Salidas:** `DATA_MANIFEST.txt` (conteos por mes y bloque, días excluidos con motivo, política de roll, versión NT8) + SHA256.
- **Gate:** ≥99% de las sesiones del IS completas en la ventana operativa [B]; exclusiones solo por regla escrita, listadas día a día; dos corridas consecutivas producen el mismo hash.
- **Si falla:** corregir datos o regla de exclusión; no avanzar.

### ET1 — Motor, simulador de cuenta y paridad
- **Propósito:** tener un motor cuya ejecución, contabilidad y simulación de cuenta estén demostradas **antes** de mirar resultados económicos.
- **Componentes:** ejecutor central de 1 NQ, modelo de costos, capa protectora, simulador de cuenta MFFU, salidas de auditoría. Semántica completa en `docs/EXECUTOR_SPEC.md`.
- **Pruebas obligatorias:**
  - **P1 Paridad de un miembro:** con capa protectora desactivada, el ejecutor reproduce trade por trade un motor de referencia escrito de forma independiente sobre las mismas barras (≥200 genomas de muestra estratificada). Tolerancia: 0 diferencias en barra de entrada/salida y dirección; precio ≤ 0 ticks salvo casos documentados.
  - **P2 Casos límite:** SL y TP en la misma barra, gap adverso, señal en la última barra de la ventana, cruce de sesión, señales simultáneas de varios miembros, día excluido.
  - **P3 Costos:** fricción aplicada exactamente una vez por operación; estrés 1,5× correcto.
  - **P4 Capa protectora:** cada evento (pausa, cierre, guardia de DD) se dispara y se libera según la semántica escrita, en escenarios construidos a mano.
  - **P5 Simulador de cuenta:** ≥10 trayectorias de equity sintéticas con resultado conocido (pasa, falla por MLL, falla por consistencia, días mínimos) dan el resultado esperado.
  - **P6 Determinismo:** misma configuración y semilla → mismo hash de salida.
- **Regla:** en ET1 se puede correr sobre IS solo para pruebas técnicas; **no se reportan ni se miran** PF, beneficio o DD de estrategias.
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
- **Acción al cerrar:** se fijan los umbrales [B] con la evidencia de ET2 y se publica **NQC 1.1.0 (congelado numérico)**. Desde aquí **no se mueven**.
- **Si falla la potencia:** se rediseña el embudo (versión menor) y se repite ET2. **Nunca** se pasa a datos reales con un embudo que no detecta un edge conocido.

### ET3 — Generación evolutiva (BUILD)
- **Propósito:** búsqueda **guiada por fitness**, no aleatoria.
- **Proceso:** algoritmo genético sobre genomas de 20 genes (biblioteca inicial heredada: 120 fórmulas, 1.320 condiciones direccionales, stops fijos y ATR, deduplicación canónica). Población inicial solo exige frecuencia mínima; la evolución selecciona.
- **Fitness [B]:** estadístico t de la expectativa neta de costos por operación en BUILD, con mínimo de 100 operaciones en BUILD; riesgo por entrada ≤ US$200.
- **Presupuesto:** máximo **3 campañas** (C1–C3) por versión de contrato; cada campaña ≤ 100.000 evaluaciones [B]; semillas declaradas antes de correr.
- **Salidas:** databank top-K por campaña (K = 2.000 [B]), registro de todas las evaluaciones en `TRIALS_LEDGER`, curva de fitness por generación.
- **Gate (técnico):** corrida completa, ledger sellado con hash, curva de fitness registrada. No hay gate económico aquí.

### ET4 — Poda barata por estrategia (BUILD)
Filtros en orden de costo creciente; solo usan BUILD:
- **F1 Costo estresado:** neto > 0 con costos 1,5×.
- **F2 Consistencia temporal:** neto > 0 en ≥ 5 de 8 trimestres de BUILD [B].
- **F3 Estabilidad de vecindario:** al perturbar cada parámetro ±1 paso, ≥ 70% de los vecinos mantienen neto > 0 [B].
- **F4 Ruido del stop (informativo, no elimina):** razón stop / ATR(14, M1) en la hora de entrada.
- **Salida:** pool podado + causa de rechazo por candidato.

### ET5 — Validación interna (VAL 2022, uso único)
- Cada candidato del pool se evalúa **una sola vez** en VAL.
- **Gate por candidato [B]:** neto > 0 tras costos, ≥ 30 operaciones en VAL.
- **Gate del pool:** PBO (CSCV sobre los 12 trimestres del IS) ≤ 0,40 [B].
- Si el pool queda vacío, se cierra la campaña (cuenta en el presupuesto de ET3).

### ET6 — Portafolios con el ejecutor en el bucle (IS completo)
- **Construcción:** búsqueda greedy/beam sobre el pool validado; cada combinación de 3–5 miembros se evalúa **con el ejecutor central** (no con correlaciones de curvas independientes). La prioridad entre miembros se fija por el fitness de VAL y queda congelada en la especificación del portafolio. Cada combinación evaluada cuenta como ensayo.
- **Perfil de aceptación (heredado de PF1, todos [B]):**

| Métrica (sobre operaciones ejecutadas) | Umbral |
|---|---|
| Profit Factor neto | ≥ 1,25 |
| Stability (definición en EXECUTOR_SPEC) | > 30% |
| Symmetry (definición en EXECUTOR_SPEC) | > 30% |
| DD máximo (cerrado y mark-to-market M1) | ≤ US$2.000 |
| Peor pérdida diaria | ≤ US$1.000 |
| Operaciones IS | ≥ 150 |
| Operaciones por mes | ≥ 5 cada mes |
| Neto con costos 1,5× | > 0 |
| Probabilidad de pasar la evaluación (P_pass, arranques rodantes) | ≥ umbral fijado en ET2 |

- **Salida:** hasta 5 portafolios finalistas con `PORTFOLIO_SPEC.json` (miembros, genes, prioridad, parámetros, hashes).

### ET7 — Evidencia estadística
- **DSR** (Deflated Sharpe) con N efectivo estimado del `TRIALS_LEDGER` acumulado: ≥ 0,95 [B].
- **Percentil frente a la distribución nula de ET2** del mejor portafolio por campaña: ≥ 95 [B].
- **Monte Carlo** (bootstrap por bloques de 8 operaciones, 500 exploratorias / 5.000 confirmatorias, semilla fija): P95 del DD ≤ US$2.000; P_pass bootstrap reportada.
- **Salida:** `ET7_EVIDENCE_REPORT.txt`. Solo los portafolios que pasan llegan a ET8.

### ET8 — Paridad nativa en NinjaTrader
- El portafolio congelado corre como estrategia NT8 con **órdenes nativas** en Strategy Analyzer sobre el IS.
- **Gate [B]:** ≥ 99% de las operaciones con la misma barra de entrada y dirección que el simulador; diferencia media de precio ≤ 1 tick; diferencia de neto total ≤ 2%.
- Al aprobar: **congelado de portafolio** — hash de `PORTFOLIO_SPEC.json` + hash del `.cs` + commit, registrados antes de abrir OOS.

### ET9 — OOS sellado (una vez, en orden)
- **Antes de abrir:** commit con el hash de congelado y `docs/OOS_EXPOSURE.md` actualizado.
- Se corre OOS1 y luego OOS2, cada uno una sola vez, sin cambiar nada entre ellos.
- **Gate (sobre OOS1+OOS2 acumulado, heredado de PF1 [B]):** PF neto ≥ 1,25; Stability > 30%; Symmetry > 30%; DD continuo sin reinicio anual ≤ US$2.000; peor pérdida diaria ≤ US$1.000; neto > 0 en cada bloque; P_pass ≥ umbral de ET2.
- **Interpretación:** el resultado se reporta junto con el peso de evidencia asignado en `OOS_EXPOSURE.md`. Un bloque con exposición previa no se presenta como independiente.
- **Si falla:** el portafolio muere. **No se ajusta.** Una nueva campaña ya no puede usar OOS1/OOS2 como confirmación independiente (ver §4).

### ET10 — Habilitación y evaluación MFFU
- **Pre-vuelo:** reglas MFFU verificadas en fuente primaria con fecha; guardas de contratos máximos; calendario de roll; comportamiento ante desconexión; kill-switch manual; registro de operaciones.
- **Plan de intentos (pre-registrado antes del primer intento):** número máximo de intentos/resets y condición de parada.
- **Monitoreo:** comparación de resultados en vivo contra la distribución esperada (MC de ET7); alarma si cae por debajo del P5.
- **Opcional (D-003 deja esto a decisión del usuario):** registro automático de señales sin órdenes a partir de noviembre 2026, como única fuente de datos realmente nuevos.

## 3. Control de cambios

| Tipo de cambio | Cuándo se permite | Requisito |
|---|---|---|
| Corrección de bug sin mirar resultados | Siempre | Repetir pruebas ET1 afectadas; nota en CHANGELOG |
| Umbral [B] | Solo dentro de ET2 | Evidencia de calibración; se congela en NQC 1.1.0 |
| Umbral [B] después de ET2 | Nunca dentro de la versión | Versión mayor NQC 2.0.0, justificación, ensayos previos se suman |
| Estructura (Nivel A) | Nunca dentro de la versión | Versión mayor + aprobación explícita del usuario |
| Decisión del usuario D-001…D-006 | Solo por el usuario | Versión mayor |

**Regla del primer vistazo:** ver el resultado de una etapa no puede cambiar las reglas de esa etapa ni de las anteriores dentro de la misma versión.

## 4. Política de OOS y campañas

- Cada campaña tiene derecho a **un** paso por ET9.
- Si una campaña falla en ET9, las siguientes campañas solo pueden usar como confirmación independiente **datos posteriores a 2026-10-31** (acumulados desde la fecha de congelado); OOS1/OOS2 pasan a ser evidencia con peso reducido, declarado en `OOS_EXPOSURE.md`.
- **Criterio de parada global [B]:** si las 3 campañas terminan sin portafolio aprobado en ET9, se declara "sin edge encontrado con esta arquitectura" y cualquier continuación requiere NQC 2.0.0 con una hipótesis distinta, no más búsqueda con la misma.
