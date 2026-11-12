# NQ PROJECT MASTER — NQ-Claude-Research

**Fuente única de verdad.** Si otro documento contradice este, manda este (y se corrige el otro).
**Límite de tamaño:** este archivo no supera ~300 líneas. El detalle vive en `docs/`; los resultados, en `runs/`.
**Última actualización:** 2026-11-11 · **Contrato vigente:** NQC 2.0.0 — **CONGELADO (Nivel A)** (reemplaza a 1.0.0).

---

## 1. Objetivo

**Objetivo final:** superar la evaluación de fondeo **MFFU 50K** operando **NQ, máximo 1 contrato**, con un portafolio de **3–5 estrategias** gestionado por un **ejecutor central** dentro de **NinjaTrader 8**.

**Objetivo operativo de la investigación:** encontrar (o descartar con evidencia) un portafolio cuya probabilidad de pasar la evaluación, medida con el simulador de cuenta en datos no usados para diseñarlo, sea alta y estadísticamente distinguible de la suerte de la búsqueda.

**No-objetivos:** maximizar beneficio histórico; encontrar "la" estrategia perfecta; generar más genomas como fin en sí mismo.

## 2. Reglas de la cuenta (resumen)

| Parámetro | Valor de trabajo | Estado |
|---|---|---|
| Objetivo de beneficio | US$3.000 | Coinciden las fuentes consultadas |
| Pérdida máxima (MLL) | US$2.000, trailing EOD | Coinciden; mecánica exacta de lock **por verificar** |
| Límite de pérdida diaria | Ninguno | Coinciden las fuentes consultadas |
| Días mínimos | 4 (Rapid EOD) / 2 (otros planes) | **Discrepan** — verificar |
| Regla de consistencia | 30% (Rapid EOD) | **Discrepan** — verificar |
| Contratos máximos | 3 o 5 mini según fuente | Irrelevante (usamos 1 NQ), verificar igual |

Detalle, fuentes y checklist de verificación: [`docs/ACCOUNT_RULES_MFFU.md`](docs/ACCOUNT_RULES_MFFU.md). **Verificación pendiente (D-009): el usuario la hará después de cerrar las demás confirmaciones y antes de ET1.**
**Ningún parámetro de cuenta se codifica fijo:** el simulador los lee de configuración versionada.

## 3. Decisiones firmes (no se reabren sin nueva versión mayor)

| ID | Decisión |
|---|---|
| D-001 | Solo NQ, máximo 1 contrato simultáneo. No MNQ. |
| D-002 | Todo en NinjaTrader 8 → Strategy Analyzer, con el histórico M1 de NT8. Sin Python/PowerShell en el flujo del usuario. |
| D-003 | Sin paper-trading forward obligatorio. |
| D-004 | IS 2020–2022 · OOS1 2023–2024 · OOS2 2025-01 a 2026-10. |
| D-004b | Se construye con los 36 meses del IS, sin examen intermedio. |
| D-005 | Portafolio de 3–5 estrategias, ejecutor central de 1 NQ; aceptación sobre operaciones ejecutadas. |
| D-006 | Costos US$24,68 RT (2+2 ticks + US$4,68); estrés 1,5×. |
| D-006b | **Sin tope de riesgo por entrada.** El riesgo se controla en las puertas del portafolio y en la capa protectora. |
| D-007 | Capa protectora con alcance de sesión: ningún bloqueo es permanente. |
| D-008 | P_pass (arranques rodantes en el simulador de cuenta) como criterio adicional. |
| D-010 | Ventana: **09:30 ET** inicio, **plano a las 11:30 ET**; sin hora límite de entrada. Decisión del usuario (volumen inicial). |
| D-011 | Máximo **5** campañas de búsqueda (C1–C5). |
| D-012 | Si ET8 falla, OOS1/OOS2 dejan de ser confirmación independiente para campañas siguientes. |
| D-013 | Código C# fuente en `ninjatrader/src/`; los ZIP se generan desde el repo. |
| D-021 | **Salidas solo por ATR**: stop obligatorio, objetivo opcional; periodo del ATR, k_sl y k_tp generados por la evolución. |
| D-022 | Stability y Symmetry son propiedades **del portafolio** (restricción dura en ET6 y gate). |
| D-023 | **Paridad nativa después del OOS**: el OOS lo evalúa el motor con el portafolio ya congelado; NT8 nativo manda en el veredicto final. |
| R1 | Serie NQ **Merge back adjusted** con el roll de NinjaTrader. No se excluyen sesiones de roll. |
| R2 | Zona horaria: la configurada en NinjaTrader (Eastern), no la del PC. ET0 la verifica con el pico de volumen de 09:30. |
| R3 | **Motor propio dentro de NinjaScript**, lanzado desde Strategy Analyzer, para ET3–ET8. Órdenes nativas en ET1 (referencia) y ET9 (paridad). |
| R4 | Referencia de paridad en ET1: 5–10 estrategias simples con **órdenes nativas de NT8**, comparadas operación por operación. Sin Python. |
| R5 | Condiciones del generador en **múltiplos de ATR, ticks o puntos**; nunca niveles absolutos ni porcentajes de precio. |

Propuestas con valor por defecto y pendientes: [`docs/DECISIONS.md`](docs/DECISIONS.md).

## 4. Las etapas (ET0–ET10)

Estructura **congelada** en NQC 2.0.0. Detalle completo: [`docs/CONTRACT_NQC_2.0.0.md`](docs/CONTRACT_NQC_2.0.0.md). Plan de trabajo: [`docs/WORK_PLAN.md`](docs/WORK_PLAN.md).

| Etapa | Nombre | Datos que toca | Pregunta que responde |
|---|---|---|---|
| ET0 | Datos y entorno | IS + OOS (solo integridad, sin economía) | ¿Los datos de NT8 son completos, en la zona horaria correcta y reproducibles? |
| ET1 | Motor, simulador de cuenta y paridad | Escenarios sintéticos + IS sin mirar economía | ¿El motor reproduce a NT8 y contabiliza correctamente? |
| ET2 | Calibración ciega | Solo datos nulos y con edge inyectado | ¿El embudo detecta un edge real y rechaza el ruido? ¿Son factibles los umbrales? |
| ET3 | Generación evolutiva | IS 2020–2022 | ¿Qué estrategias (entrada, ATR, stop y objetivo opcional) produce la búsqueda guiada? |
| ET4 | Poda barata por estrategia | IS | ¿Cuáles resisten costos, cambios de parámetro y sub-periodos? |
| ET5 | Robustez interna por bloques | IS (trimestres y años) | ¿Cuáles son estables entre bloques y cuánto sobreajusta el procedimiento (PBO)? |
| ET6 | Portafolios con ejecutor en el bucle | IS | ¿Existe una combinación con Stability y Symmetry que cumpla el perfil con 1 NQ compartido? |
| ET7 | Evidencia estadística y congelado | IS + resultados de ET2 | ¿El resultado es distinguible de la suerte de la búsqueda? Congela el portafolio. |
| ET8 | OOS sellado | OOS1 y luego OOS2, una vez | ¿El portafolio congelado funciona en datos no usados? |
| ET9 | Paridad nativa NT8 (después del OOS) | IS + OOS1 + OOS2 | ¿NT8 con órdenes nativas reproduce al motor? NT8 manda en el veredicto final. |
| ET10 | Habilitación y evaluación MFFU | En vivo | ¿Se puede operar con seguridad y pasar la cuenta? |

## 5. Congelamiento en dos niveles

- **Nivel A — Estructura (CONGELADA desde 2026-11-11, NQC 2.0.0):** objetivo, etapas y su orden, qué datos toca cada una, decisiones firmes de §3, política de OOS, regla de no ver resultados reales antes de ET2.
- **Nivel B — Umbrales numéricos:** los valores heredados de PF1 son el **punto de partida**. Solo pueden ajustarse **dentro de ET2**, con evidencia de datos nulos e inyectados, **antes** de correr ET3 sobre datos reales. Al cerrar ET2 se congelan en NQC 2.1.0 y **no se vuelven a tocar**.
- Después de ver resultados reales, cualquier cambio exige versión mayor (NQC 3.0.0), justificación escrita y suma de los ensayos previos al conteo de pruebas.
- Consecuencia asumida de D-004b: sin examen intermedio, la selección usa todo el IS; ET5 (bloques + PBO) y ET7 compensan y el peso de la confirmación recae en ET8.
- Consecuencia asumida de D-023: si el motor tuviera un error, el OOS se podría consumir con una medición incorrecta; lo mitiga ET1 (referencias nativas) y la regla de que NT8 manda en ET9.

## 6. Estado actual

| Etapa | Estado | Artefacto / hash | Fecha |
|---|---|---|---|
| Contrato NQC 2.0.0 | **CONGELADO (Nivel A)** | `docs/CONTRACT_NQC_2.0.0.md` | 2026-11-11 |
| ET0 | **EN DESARROLLO** — script v1.0.0 entregado, falta ejecutarlo | `ninjatrader/src/NQC_ET0_DataManifest.cs` SHA256 `73e937b5fb38356954e43ef7374e93a5e4aea6883ac46dd29997ba12e82b0eed` | 2026-11-11 |
| ET1 | NO INICIADA | — | — |
| ET2–ET10 | NO INICIADAS | — | — |

Estados válidos: `NO INICIADA` → `EN DESARROLLO` → `EJECUTADA` (con log NT8 y hash) → `APROBADA` / `RECHAZADA`. **Un runbook o un código que compila no es `EJECUTADA`.** Sin log de NT8 + hash, el estado no avanza.

## 7. Próximas acciones

1. Usuario: instalar y correr ET0 dos veces (`ninjatrader/runbooks/ET0_RUNBOOK.md`) y enviar `SUMMARY.txt` y `MONTHS.tsv`.
2. Usuario: verificar reglas MFFU (D-009) antes de ET1.
3. Usuario: confirmar la interpretación de ET6 anotada en el changelog y los rangos de ATR por defecto (D-019).
4. Desarrollo: especificación ejecutable de ET1 y las 5–10 estrategias nativas de referencia (`docs/WORK_PLAN.md`).
5. Usuario, antes de ET8: completar `docs/OOS_EXPOSURE.md`; antes de ET10: plazo global y tope de intentos y costo.

## 8. Mapa del repo

| Ruta | Contenido |
|---|---|
| `NQ_PROJECT_MASTER.md` | Este archivo |
| `docs/CONTRACT_NQC_2.0.0.md` | Etapas, entradas, salidas, gates, cambios permitidos (vigente) |
| `docs/CONTRACT_NQC_1.0.0.md` | Aviso de reemplazo |
| `contract/NQC_RULES_2.0.0.json` | Versión legible por máquina de los parámetros (vigente) |
| `docs/DECISIONS.md` | Registro de decisiones (firmes, propuestas, pendientes) |
| `docs/WORK_PLAN.md` | Mapa de trabajo y entregables por etapa |
| `docs/ACCOUNT_RULES_MFFU.md` | Reglas de la cuenta y verificación |
| `docs/EXECUTOR_SPEC.md` | Semántica del ejecutor, costos, capa protectora, simulador de cuenta, auditoría |
| `docs/STATISTICS_PLAN.md` | Conteo de ensayos, nulos, control positivo, DSR, PBO |
| `docs/OOS_EXPOSURE.md` | Exposición previa de los bloques OOS |
| `docs/LESSONS_FROM_PREVIOUS_REPOS.md` | Qué se hereda y qué se evita |
| `docs/TRIALS_LEDGER.md` | Registro acumulado de ensayos (formato) |
| `docs/CHANGELOG.md` · `docs/RESEARCH_LOG.md` | Cambios de contrato · bitácora breve |
| `ninjatrader/src/` | **Código C# fuente versionado** (única fuente válida) |
| `ninjatrader/installer/` | Plantillas del instalador (`INSTALAR.cmd`, `RUTA_NT8.txt`) |
| `ninjatrader/runbooks/` | Instrucciones de ejecución por etapa |
| `runs/` | `SUMMARY.txt` y manifiestos con SHA256 de cada corrida NT8 |

## 9. Reglas de trabajo

- El código NinjaScript vive en `ninjatrader/src/`. **No se aceptan entregas solo por ZIP.** El ZIP (`.cs` + `INSTALAR.cmd` + `RUTA_NT8.txt` + `HASH_SHA256.txt` + `LEEME.txt`) se genera desde el repo y su hash se registra.
- Toda corrida NT8 deja en `runs/<fecha>_<etapa>_<id>/` un `SUMMARY.txt` con: versión de contrato, hash del `.cs`, parámetros, instrumento, rango, conteos y resultado del gate.
- Nombres reservados: etapas `ET0`–`ET10`, versiones `NQC x.y.z`, campañas `C1`–`C5`. **No reutilizar** E1–E9, PF1, V19–V25 ni S0.xx del repo anterior.
- `docs/RESEARCH_LOG.md`: máximo ~15 líneas por entrada. Si crece más de ~150 KB, se resume por mes.
- Ningún resultado de desarrollo se presenta como validado. Ninguna etapa no ejecutada aparece como aprobada.
- Repo público de forma temporal (D-014: minutos de Actions agotados). No subir genomas ni especificaciones de portafolio sin revisar visibilidad.

## 10. Cómo retomar (para cualquier sesión nueva)

Leer en este orden y **solo** esto: este master → `docs/CONTRACT_NQC_2.0.0.md` → `docs/DECISIONS.md` → el último `SUMMARY.txt` en `runs/`. No leer logs completos salvo que haga falta para una pregunta concreta.
