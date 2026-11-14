# Registro de decisiones — NQ-Claude-Research

Estados: **FIRME** (decidida por el usuario) · **PROPUESTA** (valor por defecto del asistente, se confirma al llegar a la etapa) · **PENDIENTE** (falta información o decisión).
Una decisión FIRME solo cambia con versión mayor del contrato.

## Firmes

| ID | Decisión | Fecha | Origen |
|---|---|---|---|
| D-001 | Solo NQ, máximo 1 contrato simultáneo. No MNQ. | 2026-11-11 | Usuario (heredada de PF1) |
| D-002 | Ejecución en NinjaTrader 8 → Strategy Analyzer con histórico M1 de NT8. Python solo para adquisición de datos (D-028), fuera del flujo del embudo. | 2026-11-11 | Usuario |
| D-003 | Sin paper-trading forward obligatorio. No autoriza operar sin ET10. | 2026-11-11 | Usuario |
| D-004 | IS 2020–2022; OOS1 2023–2024; OOS2 2025-01 → 2026-10 (más periodo de prueba mediante varios OOS). | 2026-11-11 | Usuario |
| D-004b | Construir con los 36 meses del IS, **sin examen intermedio**. | 2026-11-11 | Usuario |
| D-005 | Portafolio de 3–5 estrategias, ejecutor central de 1 NQ; aceptación sobre operaciones ejecutadas. | 2026-11-11 | Usuario |
| D-006 | Costos US$24,68 RT (2+2 ticks + US$4,68); estrés 1,5×. | 2026-11-11 | Usuario (heredada de PF1) |
| D-006b | **Sin tope de riesgo por entrada**. El riesgo se controla en las puertas del portafolio y en la capa protectora. | 2026-11-11 | Usuario (NQC 2.0.0) |
| D-007 | Capa protectora con alcance de sesión; ningún bloqueo es permanente. | 2026-11-11 | Usuario APROBADA |
| D-008 | P_pass (arranques rodantes) como criterio adicional al perfil heredado. | 2026-11-11 | Usuario APROBADA |
| D-009 | Reglas MFFU verificadas en el Help Center y pantallas del usuario; mecánicas no aclaradas fijadas por el usuario (D-025 a D-027). **CERRADA para ET1.** Residuales en `ACCOUNT_RULES_MFFU.md`. | 2026-11-12 | Usuario |
| D-010 | Ventana: inicio 09:30 ET, plano a las 11:30 ET, sin hora límite de entrada. No es regla MFFU. | 2026-11-11 | Usuario |
| D-011 | Máximo 5 campañas de búsqueda y criterio de parada global (contrato §4). | 2026-11-11 | Usuario |
| D-012 | Política de OOS tras un fallo en ET8 (contrato §4). | 2026-11-11 | Usuario APROBADA |
| D-013 | Código C# fuente en `ninjatrader/src/`; ZIP derivado con hash. Formato: ZIP con `.cs` + instalador + archivo de ruta. | 2026-11-11 | Usuario APROBADA |
| D-019a | **Rangos de búsqueda de las salidas ATR (ET3):** periodo del ATR 5–100; k_sl 0,5–6,0 (paso 0,25); k_tp 0,5–8,0 (paso 0,25) **u opcional**. | 2026-11-12 | Usuario ACEPTADA; reconfirmada 2026-11-14 |
| D-018 | **Biblioteca de condiciones (ET3): la biblioteca completa anterior (120 fórmulas, 1.320 condiciones), con umbrales en múltiplos de ATR (R5).** Riesgo asumido por el usuario: mayor espacio de búsqueda, más falsos positivos y N efectivo mayor (DSR más exigente). ET2 debe medir los falsos positivos con esta biblioteca. | 2026-11-14 | Usuario ACEPTADA |
| D-029 | **Presupuesto de campañas:** solo C1 por ahora; C2–C5 reservadas. | 2026-11-14 | Usuario ACEPTADA |
| D-018b | **Biblioteca de ET3, cumplimiento de R5 estricto (auditoría 2026-11-14):** (1) las 19 familias que miden en % o retorno de precio (209 condiciones) se **corrigen a ATR** (dividir por ATR, no por precio); (2) las 44 familias de razón adimensional (484 condiciones) deben expresarse en **ATR literal**; las que no admitan reexpresión honesta se listan al usuario, no se excluyen sin decisión; (3) las 24 familias que usan volumen **entran en el primer corte** (el runner exporta el volumen). | 2026-11-14 | Usuario ACEPTADA |
| D-030 | **Genes de condición:** el gen mueve **periodo y umbral en múltiplos de ATR** (no solo el periodo). Consecuencia asumida: espacio de búsqueda y N efectivo mayores; ET2 mide falsos positivos con esta configuración. | 2026-11-14 | Usuario ACEPTADA |
| D-031 | **Biblioteca de ET3, cierre de la auditoría R5 (2026-11-14):** (1) las 16 familias no reexpresables en ATR se **mantienen** como razones adimensionales sin escala de precio, documentadas como excepción de R5; (2) **duplicados exactos: se deja una familia por grupo**; (3) el ATR de cada condición es el **ATR de Wilder del gen de salida** (no un ATR simple propio; reemplaza la propuesta de ATR simple de n barras). Resultado: biblioteca NQCL-3.0.0 con **113 familias × 11 reglas = 1.243 condiciones** (eliminadas 9, 18, 52, 76, 81, 104, 108); excepción R5 = exactamente las 16 familias X. | 2026-11-14 | Usuario ACEPTADA |
| D-032 | **Cuatro familias dudosas de la biblioteca (32 VPT, 60 Connors RSI, 85 expansión de volatilidad log, 103 VFI; 44 condiciones): se mantienen ACTIVAS** como razones adimensionales (excepción R5, igual que las 16 de D-031). Motivo: el porcentaje solo se usa para un signo, un rango percentil o un cambio relativo, magnitudes que no dependen del nivel de precio. Reexpresar la 32 sin porcentaje la haría idéntica a la 20. Queda la excepción R5 en 20 familias. Aplicación en código (UnitClass E→X y conteos de prueba) junto al próximo paquete de ET3, para no repetir pruebas en NT8 solo por una etiqueta. | 2026-11-14 | Asistente (delegado por el usuario) ACEPTADA |
| D-019 | **Parámetros genéticos (delegado al asistente por el usuario el 2026-11-14):** población 500, 200 generaciones (100.000 evaluaciones), torneo de 3, cruce uniforme, mutación 5% por gen, elitismo 5%, **más 5% de inmigrantes aleatorios por generación** (diversidad en un espacio de ~1e41 genomas), top-K 2.000 deduplicado, ≥100 operaciones, semilla C1 = 20261114 declarada antes de correr. Ajustable solo dentro de ET2. | 2026-11-14 | Asistente (delegado) ACEPTADA |
| D-021 | Salidas solo por ATR: stop obligatorio y objetivo opcional; periodo del ATR, k_sl y k_tp generados por la evolución. | 2026-11-11 | Usuario (NQC 2.0.0) |
| D-022 | Stability y Symmetry son propiedades del portafolio (restricción dura en ET6 y gate). Cierra la duda sobre el punto de ET6: no hay requisito adicional; la paridad con NT8 es D-023. | 2026-11-12 | Usuario (NQC 2.0.0) |
| D-023 | Paridad nativa **después** del OOS: ET8 = OOS evaluado por el motor con el portafolio ya congelado (en ET7); ET9 = paridad nativa; NT8 manda en el veredicto final. | 2026-11-11 | Usuario (NQC 2.0.0) |
| D-024 | Plan objetivo: **Rapid (intradía) 50K**, elegido por el asistente por delegación del usuario. Perfil secundario Rapid EOD. Cambiar después de ET2 exige versión mayor. | 2026-11-12 | Usuario (delegada) |
| D-025 | El MLL se comprueba **durante la sesión** contra el equity abierto (M2M). | 2026-11-12 | Usuario |
| D-026 | **Día de operación** = día con al menos una operación tomada. | 2026-11-12 | Usuario |
| D-027 | **Blackout de ±5 minutos** sobre eventos **USD de Forex Factory con impacto alto y medio** más los T1 de MFFU. Costo de reset: US$0 (dato del usuario). Tiene efecto real en la ventana. | 2026-11-12 | Usuario |
| D-027b | Alcance del blackout: **fuera del mercado** (cierre a `t−5:00`, sin entradas hasta `t+5:00`) y **sin eliminar barras**. Confirmado por la práctica anterior del usuario (`PDCSmallGapParity.cs`, 2026-10-05). | 2026-11-12 | Usuario |
| D-028 | Fuente del calendario: el método del 2026-10-05 (páginas mensuales de Forex Factory), rehecho para 2020–2026 con `tools/news/`. **Archivo aprobado** (3.954 eventos, SHA256 `93d837ac…377d`). | 2026-11-12 | Usuario |
| R1 | Serie NQ Merge back adjusted con el roll de NinjaTrader; no se excluyen sesiones de roll. | 2026-11-11 | Usuario |
| R2 | Zona horaria: la configurada en NinjaTrader (Eastern), no la del PC. | 2026-11-11 | Usuario |
| R3 | Motor propio dentro de NinjaScript lanzado desde Strategy Analyzer para ET3–ET8; nativo en ET1 y ET9. | 2026-11-11 | Usuario APROBADA |
| R4 | Referencia de paridad en ET1: 5–10 estrategias simples con órdenes nativas de NT8; sin Python. | 2026-11-11 | Usuario |
| R5 | Condiciones del generador en ATR, ticks o puntos; no niveles absolutos ni porcentajes. | 2026-11-11 | Usuario |

## Propuestas con valor por defecto (se confirman al llegar a la etapa)

| ID | Tema | Valor por defecto |
|---|---|---|
| D-015 | Fitness de ET3 | t de la expectativa neta por operación en el IS, mínimo 100 operaciones |
| D-016 | Prioridad entre miembros | Por fitness en el IS, no por ID |
| D-034 | Método de la poda de ET4 (por defecto vigente, objetable por el usuario) | 12 trimestres calendario 2020Q1–2022Q4; trimestre sin operaciones = neto 0 = no positivo. **F1:** neto total > 0 a costo 1,5× (37,02). **F3:** un parámetro a la vez, ±1 paso (periodo y umbral de cada ranura activa, periodo ATR 5–100, k_sl, k_tp); no se perturban id de condición, dirección ni ranuras vacías; «sin objetivo» no tiene vecinos; **un vecino fuera de rejilla no existe** (no se repara ni cuenta como fallo; no entra al denominador); un vecino «mantiene» si su neto total a costo 1,0× > 0 (0 operaciones = fallo, sin exigir el mínimo de 100); criterio entero aprobados×100 ≥ 70×existentes. **Ensayos:** cada vecino cuenta como ensayo nuevo solo si su TrialId no está ya en el ledger de ET3 ni se evaluó antes (se estiman 30–60 mil ensayos adicionales en el acumulado, lo que endurece el DSR). **F4** informativo: |entrada−stop| por operación. | 2026-11-14 | Propuesta con valor por defecto |
| D-035 | Interpretación de ET6 (por defecto vigente, objetable por el usuario) | **Prioridad** = fitness IS descendente, congelada en el spec (D-016). **Beam:** MaxPool = 30 mejores por fitness, ancho 10, profundidad 5 (~1.230 evaluaciones), determinista (semilla solo en desempates por hash). **Stability y Symmetry duras** (> 30, estricto) para toda combinación evaluada de ≥ 3 miembros: se descarta y no se expande; los parciales de 1–2 miembros no se filtran (un solo lado da Symmetry 0). Puntuación del beam: menos puertas falladas, luego t de la expectativa neta por operación. Corrida a 1,5× y P_pass solo si las demás puertas pasan. **Finalistas** (≤ 5, de las aceptadas con 3–5 miembros): por t, luego P_pass, menos miembros, hash; diversidad voraz (Jaccard de miembros ≤ 0,5 y huella distinta). **Ensayos:** cada combinación única evaluada cuenta (también las de 1–2 miembros, conservador para el DSR); las corridas a 1,5× no son ensayos aparte. Mes sin operaciones = 0; peor día = −min(neto diario); P_pass NaN (umbral aún no fijado por ET2) = falla cerrada. | 2026-11-14 | Propuesta con valor por defecto |
| D-033 | Método del PBO en ET5 (por defecto vigente, objetable por el usuario) | Trimestres 0–11 = 2020Q1–2022Q4; filtro exige neto anual > 0 en ≥ 2 de 3 años. Ranking del PBO por **P&L neto total** (un trimestre sin operaciones aporta 0). CSCV con las 924 particiones, mejor en entrenamiento = argmax (empate: menor índice); omega = rango medio en prueba / (N+1); lambda = ln(omega/(1−omega)); **PBO = fracción de particiones con lambda ≤ 0** (omega = 0,5 cuenta como sobreajuste). El PBO del gate se calcula sobre el pool que pasa el filtro de años; el del pool completo de ET4 es solo informativo. Si quedan **menos de 2 candidatos**, la campaña se cierra (PBO no evaluable; falla cerrada). Si ET4 entrega series diarias, se extiende la interfaz. |
| D-017 | Umbrales [B] | Heredados de PF1 como punto de partida; se fijan al cerrar ET2 |
| D-020 | Edge de referencia de ET2 | +US$20 netos por operación a ~0,5 operaciones por sesión |

## Pendientes

| ID | Tema | Qué falta |
|---|---|---|
| — | ET1 parte A | El usuario instala `NQC_ET1A_NativeRefs.cs`, corre N1–N9 en Strategy Analyzer sobre el IS y envía las 9 líneas de salida, `SUMMARY.txt` y `TRADES_NATIVE.tsv`. Ver `docs/ET1_REFERENCES.md`. |
| D-014 | Visibilidad del repo | **Público de forma temporal** por minutos de Actions agotados. Revisar antes de subir genomas o especificaciones de portafolio. |
| — | `OOS_EXPOSURE.md` | Completar antes de ET8: qué años se miraron en cada programa previo. |
| — | Plazo y tope de intentos | Antes de ET10: fecha límite global y número máximo de intentos. Costo de la evaluación: US$209; reset: US$0 (dato del usuario). |
| — | Residuales de reglas MFFU | Ver `ACCOUNT_RULES_MFFU.md` (lock del MLL, regla de 2 minutos para noticias no T1, etiqueta EST del calendario T1). |

## Cómo cambiar una decisión firme

Solo con versión mayor del contrato, justificación escrita y registro en `CHANGELOG.md`.
