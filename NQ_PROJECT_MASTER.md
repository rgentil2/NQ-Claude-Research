# NQ PROJECT MASTER — NQ-Claude-Research

**Fuente única de verdad.** Si otro documento contradice este, manda este (y se corrige el otro).
**Límite de tamaño:** este archivo no supera ~300 líneas. El detalle vive en `docs/`; los resultados, en `runs/`.
**Última actualización:** 2026-11-12 · **Contrato vigente:** NQC 2.0.0 — **CONGELADO (Nivel A)** (reemplaza a 1.0.0). Ejecutor: EXECUTOR_SPEC 2.1.0.

---

## 1. Objetivo

**Objetivo final:** superar la evaluación de fondeo **MFFU 50K** operando **NQ, máximo 1 contrato**, con un portafolio de **3–5 estrategias** gestionado por un **ejecutor central** dentro de **NinjaTrader 8**.

**Objetivo operativo de la investigación:** encontrar (o descartar con evidencia) un portafolio cuya probabilidad de pasar la evaluación, medida con el simulador de cuenta en datos no usados para diseñarlo, sea alta y estadísticamente distinguible de la suerte de la búsqueda.

**No-objetivos:** maximizar beneficio histórico; encontrar "la" estrategia perfecta; generar más genomas como fin en sí mismo.

## 2. Reglas de la cuenta (resumen)

Plan: **Rapid (intradía) 50K** (D-024). Detalle, fuentes y perfiles del simulador: [`docs/ACCOUNT_RULES_MFFU.md`](docs/ACCOUNT_RULES_MFFU.md). **D-009 cerrada para ET1.**

| Parámetro | Valor | Estado |
|---|---|---|
| Objetivo de beneficio | US$3.000 | Verificado |
| Pérdida máxima (MLL) | US$2.000; nivel EOD; **comprobación durante la sesión** (D-025) | Verificado + decisión del usuario |
| Límite de pérdida diaria | Ninguno | Verificado |
| Consistencia en evaluación | 50% (mejor día / beneficio total) | Verificado |
| Días mínimos | 2; día = al menos una operación (D-026) | Verificado + decisión del usuario |
| Contratos máximos | 5 (usamos 1) | Verificado |
| Noticias | **Blackout ±5 min sobre eventos USD de Forex Factory (impacto alto y medio) y T1 de MFFU; tiene efecto en la ventana** (D-027, `docs/NEWS_BLACKOUT.md`) | Decisión del usuario |
| Costo | US$209 pago único; reset US$0 (dato del usuario) | Capturas del usuario |
| Cuenta fondeada | MLL en tiempo real; sin consistencia | Verificado; se modela en ET10 |

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
| D-009 | Reglas MFFU verificadas; cerrada para ET1 (residuales en `ACCOUNT_RULES_MFFU.md`). |
| D-010 | Ventana: **09:30 ET** inicio, **plano a las 11:30 ET**; sin hora límite de entrada. Decisión del usuario (volumen inicial). |
| D-011 | Máximo **5** campañas de búsqueda (C1–C5). |
| D-012 | Si ET8 falla, OOS1/OOS2 dejan de ser confirmación independiente para campañas siguientes. |
| D-013 | Código C# fuente en `ninjatrader/src/`; los ZIP se generan desde el repo. |
| D-021 | **Salidas solo por ATR**: stop obligatorio, objetivo opcional; periodo del ATR, k_sl y k_tp generados por la evolución. |
| D-022 | Stability y Symmetry son propiedades **del portafolio** (restricción dura en ET6 y gate). |
| D-023 | **Paridad nativa después del OOS**: el OOS lo evalúa el motor con el portafolio ya congelado; NT8 nativo manda en el veredicto final. |
| D-024 | Plan objetivo: **Rapid (intradía) 50K** (delegado al asistente); perfil secundario Rapid EOD. |
| D-025 | MLL comprobado **durante la sesión** contra el equity abierto. |
| D-026 | Día de operación = al menos una operación tomada. |
| D-027 | **Blackout de ±5 minutos** en noticias USD de Forex Factory (impacto alto y medio, todos los tipos) más T1 de MFFU; costo de reset US$0. |
| R1 | Serie NQ **Merge back adjusted** con el roll de NinjaTrader. No se excluyen sesiones de roll. |
| R2 | Zona horaria: la configurada en NinjaTrader (Eastern), no la del PC. |
| R3 | **Motor propio dentro de NinjaScript**, lanzado desde Strategy Analyzer, para ET3–ET8. Órdenes nativas en ET1 (referencia) y ET9 (paridad). |
| R4 | Referencia de paridad en ET1: 5–10 estrategias simples con **órdenes nativas de NT8**, comparadas operación por operación. Sin Python. |
| R5 | Condiciones del generador en múltiplos de ATR, ticks o puntos; nunca niveles absolutos ni porcentajes. |

Propuestas con valor por defecto y pendientes (incluye D-027b, alcance del blackout, y D-028, archivo del calendario): [`docs/DECISIONS.md`](docs/DECISIONS.md).

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
- El blackout de noticias (D-027) se registró como **aclaración de ET1 antes de ver resultados** (ver `docs/CHANGELOG.md`); el usuario puede objetarlo antes de ET1.

## 6. Estado actual

| Etapa | Estado | Artefacto / hash | Fecha |
|---|---|---|---|
| Contrato NQC 2.0.0 | **CONGELADO (Nivel A)** | `docs/CONTRACT_NQC_2.0.0.md` | 2026-11-11 |
| ET0 | **APROBADA** (G0–G6 PASS, 2 corridas con el mismo hash) | `runs/2026-11-12_ET0_run1/`; script SHA256 `73e937b5…b0eed`; DAYS `9bd25f76…`; MONTHS `79f61c6d…` | 2026-11-12 |
| ET1 | **NO INICIADA.** El motor puede desarrollarse; las corridas sobre datos reales esperan el calendario de noticias (D-028) | — | — |
| ET2–ET10 | NO INICIADAS | — | — |

Estados válidos: `NO INICIADA` → `EN DESARROLLO` → `EJECUTADA` (con log NT8 y hash) → `APROBADA` / `RECHAZADA`. **Un runbook o un código que compila no es `EJECUTADA`.** Sin log de NT8 + hash, el estado no avanza.

**Limitaciones conocidas de ET0** (detalle en `runs/2026-11-12_ET0_run1/RUN_NOTES.md`):
- 7 sesiones incompletas del IS excluidas, 3 de ellas de marzo de 2020 (los días más volátiles). En vivo esos días existen; se considerará en ET6/ET7.
- 2020-10-23 (viernes) sin barras en la ventana y sin festivo conocido: revisar si es un hueco de datos.
- Los márgenes de G3 (99,10% frente a 99%) y G4 (70,54% frente a 70%) son pequeños.

## 7. Próximas acciones

1. Usuario: conseguir el **calendario de noticias** (USD, alto y medio, 2020-01-01 a 2026-12-31, hora de Nueva York) y subirlo para validarlo (D-028, `docs/NEWS_BLACKOUT.md`). Confirmar también D-027b: "no operar" = estar fuera del mercado (cerrar a `t−5:00`).
2. Desarrollo: ET1 — motor, simulador de cuenta con perfil RAPID_INTRADAY_50K, blackout de noticias y las referencias nativas (`docs/WORK_PLAN.md`).
3. Usuario: confirmar la interpretación de ET6 y los rangos de ATR por defecto (D-019); opcional, revisar la biblioteca de condiciones (D-018) antes de ET3.
4. Usuario (opcional): revisar 2020-10-23 en el gráfico de NT8.
5. Usuario, antes de ET8: completar `docs/OOS_EXPOSURE.md`; antes de ET10: plazo global y tope de intentos.

## 8. Mapa del repo

| Ruta | Contenido |
|---|---|
| `NQ_PROJECT_MASTER.md` | Este archivo |
| `docs/CONTRACT_NQC_2.0.0.md` | Etapas, entradas, salidas, gates, cambios permitidos (vigente) |
| `docs/CONTRACT_NQC_1.0.0.md` | Aviso de reemplazo |
| `contract/NQC_RULES_2.0.0.json` | Versión legible por máquina de los parámetros (vigente) |
| `docs/DECISIONS.md` | Registro de decisiones (firmes, propuestas, pendientes) |
| `docs/WORK_PLAN.md` | Mapa de trabajo y entregables por etapa |
| `docs/ACCOUNT_RULES_MFFU.md` | Reglas de la cuenta, perfiles del simulador y verificación |
| `docs/NEWS_BLACKOUT.md` | Regla del blackout de noticias, archivo de datos y aceptación |
| `docs/EXECUTOR_SPEC.md` | Semántica del ejecutor (2.1.0), costos, capa protectora, simulador de cuenta, auditoría |
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
- Repo público de forma temporal (D-014: minutos de Actions agotados). No subir genomas ni especificaciones de portafolio sin revisar visibilidad. Los datos de terceros (calendario de noticias) no se suben: solo su manifiesto y hash.

## 10. Cómo retomar (para cualquier sesión nueva)

Leer en este orden y **solo** esto: este master → `docs/CONTRACT_NQC_2.0.0.md` → `docs/DECISIONS.md` → el último `SUMMARY.txt` en `runs/`. No leer logs completos salvo que haga falta para una pregunta concreta.
