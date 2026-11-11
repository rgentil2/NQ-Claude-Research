# Registro de decisiones — NQ-Claude-Research

Estados: **FIRME** (decidida por el usuario) · **PROPUESTA** (valor por defecto del asistente, se confirma al llegar a la etapa) · **PENDIENTE** (falta información o decisión).
Una decisión FIRME solo cambia con versión mayor del contrato.

## Firmes

| ID | Decisión | Fecha | Origen |
|---|---|---|---|
| D-001 | Solo NQ, máximo 1 contrato simultáneo. No MNQ. | 2026-11-11 | Usuario (heredada de PF1) |
| D-002 | Ejecución en NinjaTrader 8 → Strategy Analyzer con histórico M1 de NT8. | 2026-11-11 | Usuario |
| D-003 | Sin paper-trading forward obligatorio. No autoriza operar sin ET10. | 2026-11-11 | Usuario |
| D-004 | IS 2020–2022; OOS1 2023–2024; OOS2 2025-01 → 2026-10 (más periodo de prueba mediante varios OOS). | 2026-11-11 | Usuario |
| D-004b | Construir con los 36 meses del IS, **sin examen intermedio** (se descartó la división 2020–2021 / 2022). | 2026-11-11 | Usuario |
| D-005 | Portafolio de 3–5 estrategias, ejecutor central de 1 NQ; aceptación sobre operaciones ejecutadas. | 2026-11-11 | Usuario |
| D-006 | Costos US$24,68 RT (2+2 ticks + US$4,68); estrés 1,5×; riesgo por entrada ≤ US$200 con costos. | 2026-11-11 | Usuario (heredada de PF1) |
| D-007 | Capa protectora con alcance de sesión; ningún bloqueo es permanente. | 2026-11-11 | Usuario APROBADA |
| D-008 | P_pass (arranques rodantes) como criterio adicional al perfil heredado. | 2026-11-11 | Usuario APROBADA |
| D-010 | Ventana: inicio 09:30 ET, plano a las 11:30 ET, sin hora límite de entrada. Decisión del usuario por el volumen inicial; no es regla MFFU. | 2026-11-11 | Usuario |
| D-011 | Máximo 5 campañas de búsqueda y criterio de parada global (contrato §4). | 2026-11-11 | Usuario |
| D-012 | Política de OOS tras un fallo en ET9 (contrato §4). | 2026-11-11 | Usuario APROBADA |
| D-013 | Código C# fuente en `ninjatrader/src/`; ZIP derivado con hash. Formato de entrega: ZIP con `.cs` + instalador + archivo de ruta. | 2026-11-11 | Usuario APROBADA |
| R1 | Serie NQ Merge back adjusted con el roll de NinjaTrader; no se excluyen sesiones de roll. | 2026-11-11 | Usuario |
| R2 | Zona horaria: la configurada en NinjaTrader (Eastern), no la del PC (Bogotá). | 2026-11-11 | Usuario |
| R3 | Motor propio dentro de NinjaScript lanzado desde Strategy Analyzer para ET3–ET7; nativo solo en ET1 y ET8. | 2026-11-11 | Usuario APROBADA |
| R4 | Referencia de paridad: 5–10 estrategias simples con órdenes nativas de NT8; sin Python. | 2026-11-11 | Usuario |
| R5 | Condiciones del generador en ATR, ticks o puntos; no niveles absolutos ni porcentajes. | 2026-11-11 | Usuario |

## Propuestas con valor por defecto (se confirman al llegar a la etapa)

| ID | Tema | Valor por defecto |
|---|---|---|
| D-015 | Fitness de ET3 | t de la expectativa neta por operación en el IS, mínimo 100 operaciones |
| D-016 | Prioridad entre miembros | Por fitness en el IS, no por ID |
| D-018 | Biblioteca de condiciones | Revisar la anterior (120 fórmulas, 1.320 condiciones) y proponer subconjunto coherente con 09:30–11:30, en unidades de ATR |
| D-019 | Parámetros genéticos | Población 500, 200 generaciones (~100.000 evaluaciones), cruce uniforme, mutación 5% por gen, elitismo 5%, torneo de 3; stops fijos de 8 a 35 ticks o por ATR dentro del tope de US$200; objetivos de 1R a 3R |
| D-017 | Umbrales [B] | Heredados de PF1 como punto de partida; se fijan al cerrar ET2 |
| D-020 | Edge de referencia de ET2 | +US$20 netos por operación a ~0,5 operaciones por sesión |

## Pendientes

| ID | Tema | Qué falta |
|---|---|---|
| D-009 | Reglas MFFU vigentes | Verificación del usuario en el Help Center oficial (ver `ACCOUNT_RULES_MFFU.md`). Diferida por el usuario hasta cerrar las demás confirmaciones; **bloquea ET1**. |
| D-014 | Visibilidad del repo | **Público de forma temporal** por minutos de Actions agotados. Revisar antes de subir genomas o especificaciones de portafolio. |
| — | `OOS_EXPOSURE.md` | Completar antes de ET9: qué años se miraron en cada programa previo. |
| — | Plazo y tope de intentos | Antes de ET10: fecha límite global, número máximo de intentos de evaluación y costo máximo. |

## Cómo cambiar una decisión firme

Solo con versión mayor del contrato (NQC 2.0.0), justificación escrita y registro en `CHANGELOG.md`.
