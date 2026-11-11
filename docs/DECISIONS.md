# Registro de decisiones — NQ-Claude-Research

Estados: **FIRME** (decidida por el usuario) · **PROPUESTA** (recomendada, requiere aprobación) · **PENDIENTE** (falta información o decisión).
Una decisión FIRME solo cambia con versión mayor del contrato.

## Firmes

| ID | Decisión | Origen |
|---|---|---|
| D-001 | Solo NQ, máximo 1 contrato simultáneo. No MNQ. | Usuario (heredada de PF1) |
| D-002 | Ejecución exclusiva en NinjaTrader 8 → Strategy Analyzer con histórico M1 de NT8. | Usuario |
| D-003 | Sin paper-trading forward obligatorio. No autoriza operar sin ET10. | Usuario |
| D-004 | IS 2020–2022 · OOS1 2023–2024 · OOS2 2025-01 → 2026-10. | Usuario |
| D-005 | Portafolio de 3–5 estrategias, ejecutor central de 1 NQ; aceptación sobre operaciones ejecutadas. | Usuario |
| D-006 | Costos US$24,68 RT (2+2 ticks + US$4,68); estrés 1,5×; riesgo por entrada ≤ US$200 con costos. | Usuario (heredada de PF1) |

## Propuestas (aprobar, ajustar o rechazar)

| ID | Propuesta | Por qué |
|---|---|---|
| D-004b | Dividir el IS: BUILD 2020–2021 para generar y podar; VAL 2022 para validar una sola vez. | PF1 seleccionaba sobre los 36 meses sin ningún filtro intermedio antes del OOS. 2022 (mercado bajista) es además un cambio de régimen respecto a 2020–2021. |
| D-007 | Capa protectora **con alcance de sesión**: la pausa (–US$500) y el cierre (–US$750) diarios terminan al cierre de la sesión; la guardia de DD bloquea entradas **solo en la sesión en curso** cuando el colchón hasta el MLL simulado es ≤ US$400 (equivale al umbral heredado de US$1.600). Ningún bloqueo es permanente. | En PF1 el bloqueo de US$1.600 no tenía regla de liberación: medido contra el pico, sin poder operar no se libera nunca (estado absorbente). Una simulación genérica mostró que eso trunca la mayoría de las trayectorias en ~100 operaciones. |
| D-008 | Añadir **P_pass** (probabilidad de pasar la evaluación con arranques rodantes en el simulador de cuenta) como criterio de aceptación, junto al perfil heredado. | Es la métrica más cercana al objetivo real. El DD continuo de 36 meses es un sustituto más estricto e indirecto. |
| D-011 | Presupuesto de 3 campañas y criterio de parada global (contrato §4). | Evitar búsqueda indefinida que acumula sesgo de selección. |
| D-012 | Política de OOS tras un fallo en ET9 (contrato §4). | El OOS solo es confirmación una vez. |
| D-013 | Código C# fuente versionado en `ninjatrader/src/` como única fuente válida; ZIP solo como derivado con hash. | En PF1 parte del código solo existía en ZIPs entregados por chat. |
| D-015 | Fitness de ET3 = t de la expectativa neta por operación en BUILD, mínimo 100 operaciones. | Premia edge por operación con muestra suficiente, no beneficio total. |
| D-016 | Prioridad entre miembros fijada por fitness en VAL, no por ID. | En PF1 la prioridad por ID ascendente era arbitraria. |

## Pendientes

| ID | Tema | Qué falta |
|---|---|---|
| D-009 | Reglas MFFU vigentes | Verificación del usuario en el Help Center oficial (ver `ACCOUNT_RULES_MFFU.md`). Las fuentes secundarias discrepan en consistencia, días mínimos y contratos. |
| D-010 | Ventana operativa | Confirmar si se mantiene la ventana de investigación previa (entradas aprox. 09:50–11:28 ET, salida forzada 11:29 ET — **verificar contra el repo anterior**) o se define otra. No es una regla MFFU según las fuentes consultadas. |
| D-014 | Visibilidad del repo | El repo es legible sin autenticación (público). Las especificaciones de estrategias son propiedad intelectual: se recomienda hacerlo privado antes de subir genomas o especificaciones de portafolio. |
| D-017 | Umbrales [B] | Se fijan al cerrar ET2 con evidencia; hasta entonces rigen los heredados de PF1 como punto de partida. |

## Cómo aprobar

Editar esta tabla moviendo la fila a "Firmes" con fecha y la palabra APROBADA, o anotar el ajuste. Cuando D-004b, D-007, D-008, D-011, D-012 y D-013 estén resueltas, el contrato pasa a **NQC 1.0.0 CONGELADO (Nivel A)**.
