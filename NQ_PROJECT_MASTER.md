# NQ PROJECT MASTER — NQ-Claude-Research

**Fuente única de verdad.** Si otro documento contradice este, manda este (y se corrige el otro).
**Límite de tamaño:** este archivo no supera ~300 líneas. El detalle vive en `docs/`; los resultados, en `runs/`.
**Última actualización:** 2026-11-11 · **Contrato vigente:** NQC 1.0.0 (estructura) — ver estado de congelamiento abajo.

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

Detalle, fuentes y checklist de verificación: [`docs/ACCOUNT_RULES_MFFU.md`](docs/ACCOUNT_RULES_MFFU.md).
**Ningún parámetro de cuenta se codifica fijo:** el simulador los lee de configuración versionada.

## 3. Decisiones firmes del usuario (no se reabren sin nueva versión mayor)

- **D-001** Solo NQ, máximo 1 contrato simultáneo. No MNQ.
- **D-002** Todo se ejecuta en NinjaTrader 8 → Strategy Analyzer, con el histórico M1 de NT8. Sin Python/PowerShell en el flujo del usuario.
- **D-003** Sin paper-trading forward obligatorio.
- **D-004** IS 2020–2022 · OOS1 2023–2024 · OOS2 2025-01 a 2026-10.
- **D-005** Portafolio de 3–5 estrategias con ejecutor central de 1 NQ; las métricas de aceptación se calculan sobre las operaciones realmente ejecutadas.
- **D-006** Costos: 2 ticks de slippage en entrada + 2 en salida + US$4,68 de comisión RT = **US$24,68** por operación; estrés 1,5× = US$37,02. Riesgo planificado por entrada ≤ US$200 incluyendo costos.

Lista completa (incluye propuestas pendientes): [`docs/DECISIONS.md`](docs/DECISIONS.md).

## 4. Las etapas (ET0–ET10)

Estructura **congelada** en NQC 1.0.0. Detalle completo: [`docs/CONTRACT_NQC_1.0.0.md`](docs/CONTRACT_NQC_1.0.0.md).

| Etapa | Nombre | Datos que toca | Pregunta que responde |
|---|---|---|---|
| ET0 | Datos y entorno | IS + OOS (solo integridad, sin economía) | ¿Los datos de NT8 son completos y reproducibles? |
| ET1 | Motor, simulador de cuenta y paridad | Escenarios sintéticos + IS sin mirar economía | ¿El motor ejecuta y contabiliza correctamente? |
| ET2 | Calibración ciega | Solo datos nulos y con edge inyectado | ¿El embudo detecta un edge real y rechaza el ruido? ¿Son factibles los umbrales? |
| ET3 | Generación evolutiva | BUILD 2020–2021 | ¿Qué estrategias candidatas produce la búsqueda guiada? |
| ET4 | Poda barata por estrategia | BUILD | ¿Cuáles resisten costos, cambios de parámetro y sub-periodos? |
| ET5 | Validación interna | VAL 2022 (uso único) | ¿Cuáles sobreviven fuera del periodo de construcción? |
| ET6 | Portafolios con ejecutor en el bucle | IS completo | ¿Existe una combinación que cumpla el perfil con 1 NQ compartido? |
| ET7 | Evidencia estadística | IS + resultados de ET2 | ¿El resultado es distinguible de la suerte de la búsqueda? |
| ET8 | Paridad nativa NT8 | IS | ¿Las órdenes nativas de NT8 reproducen el simulador? |
| ET9 | OOS sellado | OOS1 y luego OOS2, una vez | ¿El portafolio congelado funciona en datos no usados? |
| ET10 | Habilitación y evaluación MFFU | En vivo | ¿Se puede operar con seguridad y pasar la cuenta? |

## 5. Congelamiento en dos niveles

- **Nivel A — Estructura (congelada al aprobar NQC 1.0.0):** objetivo, etapas y su orden, qué datos toca cada una, D-001 a D-006, política de OOS, regla de no ver resultados reales antes de ET2.
- **Nivel B — Umbrales numéricos:** los valores heredados de PF1 son el **punto de partida**. Solo pueden ajustarse **dentro de ET2**, con evidencia de datos nulos e inyectados, **antes** de correr ET3 sobre datos reales. Al cerrar ET2 se congelan en NQC 1.1.0 y **no se vuelven a tocar**.
- Después de ver resultados reales, cualquier cambio de umbral exige versión mayor (NQC 2.0.0), justificación escrita y suma de los ensayos previos al conteo de pruebas.

## 6. Estado actual

| Etapa | Estado | Artefacto / hash | Fecha |
|---|---|---|---|
| Contrato NQC 1.0.0 | **PROPUESTO — pendiente de aprobación del usuario** | `docs/CONTRACT_NQC_1.0.0.md` | 2026-11-11 |
| ET0 | NO INICIADA | — | — |
| ET1 | NO INICIADA | — | — |
| ET2–ET10 | NO INICIADAS | — | — |

Estados válidos: `NO INICIADA` → `EN DESARROLLO` → `EJECUTADA` (con log NT8 y hash) → `APROBADA` / `RECHAZADA`. **Un runbook o un código que compila no es `EJECUTADA`.** Sin log de NT8 + hash, el estado no avanza.

## 7. Próximas acciones

1. Usuario: aprobar o ajustar las decisiones pendientes de `docs/DECISIONS.md` (sección "Pendientes").
2. Usuario: verificar reglas MFFU en el Help Center oficial y registrar fecha y URL en `docs/ACCOUNT_RULES_MFFU.md`.
3. Usuario: decidir visibilidad del repo (D-014).
4. Desarrollo: ET0 (manifiesto de datos en NinjaScript) y especificación ejecutable de ET1.

## 8. Mapa del repo

| Ruta | Contenido |
|---|---|
| `NQ_PROJECT_MASTER.md` | Este archivo |
| `docs/CONTRACT_NQC_1.0.0.md` | Etapas, entradas, salidas, gates, cambios permitidos |
| `contract/NQC_RULES_1.0.0.json` | Versión legible por máquina de los parámetros |
| `docs/DECISIONS.md` | Registro de decisiones (firmes, propuestas, pendientes) |
| `docs/ACCOUNT_RULES_MFFU.md` | Reglas de la cuenta y verificación |
| `docs/EXECUTOR_SPEC.md` | Semántica del ejecutor, costos, capa protectora, simulador de cuenta, auditoría |
| `docs/STATISTICS_PLAN.md` | Conteo de ensayos, nulos, control positivo, DSR, PBO |
| `docs/OOS_EXPOSURE.md` | Exposición previa de los bloques OOS |
| `docs/LESSONS_FROM_PREVIOUS_REPOS.md` | Qué se hereda y qué se evita |
| `docs/TRIALS_LEDGER.md` | Registro acumulado de ensayos (formato) |
| `docs/CHANGELOG.md` · `docs/RESEARCH_LOG.md` | Cambios de contrato · bitácora breve |
| `ninjatrader/src/` | **Código C# fuente versionado** (única fuente válida) |
| `runs/` | `SUMMARY.txt` y manifiestos con SHA256 de cada corrida NT8 |

## 9. Reglas de trabajo

- El código NinjaScript vive en `ninjatrader/src/`. **No se aceptan entregas solo por ZIP.** El ZIP de instalación se genera desde el repo y su hash se registra.
- Toda corrida NT8 deja en `runs/<fecha>_<etapa>_<id>/` un `SUMMARY.txt` con: versión de contrato, hash del `.cs`, parámetros, instrumento, rango, conteos y resultado del gate.
- Nombres reservados: etapas `ET0`–`ET10`, versiones `NQC x.y.z`, campañas `C1`, `C2`, `C3`. **No reutilizar** E1–E9, PF1, V19–V25 ni S0.xx del repo anterior.
- `docs/RESEARCH_LOG.md`: máximo ~15 líneas por entrada. Si crece más de ~150 KB, se resume por mes.
- Ningún resultado de desarrollo se presenta como validado. Ninguna etapa no ejecutada aparece como aprobada.

## 10. Cómo retomar (para cualquier sesión nueva)

Leer en este orden y **solo** esto: este master → `docs/CONTRACT_NQC_1.0.0.md` → `docs/DECISIONS.md` → el último `SUMMARY.txt` en `runs/`. No leer logs completos salvo que haga falta para una pregunta concreta.
