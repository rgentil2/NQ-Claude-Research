# Plan de trabajo — NQC 2.0.0

Estructura congelada; este documento solo organiza la ejecución. Estados en `NQ_PROJECT_MASTER.md`.

## Reparto de trabajo
- **Asistente:** escribe los `.cs`, los sube al repo y genera el ZIP de instalación desde el repo.
- **Usuario:** instala, compila en NinjaTrader (F5), corre en Strategy Analyzer y devuelve `SUMMARY.txt` y los TSV pedidos.
- El asistente no puede compilar NinjaScript. Cada etapa técnica tendrá ciclos de corrección con los mensajes de error que envíes.

## Ruta crítica
ET0 (aprobada) → ET1 → código de ET3–ET7 → ET2 → ET3 real → ET4 → ET5 → ET6 → ET7 (congela) → ET8 (OOS) → ET9 (paridad) → ET10.
ET2 es el cuello de botella: exige que todo el embudo funcione antes de tocar datos reales.

## Por etapa

| Etapa | Entregable | Parámetros clave | Gate | Depende de |
|---|---|---|---|---|
| ET0 | `NQC_ET0_DataManifest.cs` + runbook (**aprobada** 2026-11-12) | Ventana sellos 09:31–11:30; completitud ≥99%; zona horaria ≥70% | G0–G6 | — |
| ET1 | Motor + simulador de cuenta (perfil RAPID_INTRADAY_50K) + estrategias nativas de referencia + pruebas P1–P6 | Costos US$24,68; sin tope de riesgo por entrada; capa protectora por sesión; MLL comprobado durante la sesión; día = ≥1 operación; 5–10 referencias nativas | P1–P6 al 100% | ET0 (D-009 cerrada) |
| ET3–ET8 (código) | Generador, poda, robustez, constructor de portafolios, módulo estadístico | Los de cada etapa | Pruebas técnicas con datos sintéticos | ET1 |
| ET2 | Datasets nulos e inyectados + informe de calibración | ≥5 nulos, ≥5 inyectados; FP ≤5%; potencia ≥70% | Congelado numérico NQC 2.1.0 | ET3–ET7 listos |
| ET3 | Campaña C1 en el IS 2020–2022 | Genes de salida: periodo del ATR 5–100, k_sl 0,5–6,0, k_tp 0,5–8,0 u opcional; fitness t; ≥100 operaciones; 100.000 evaluaciones; K=2.000; máx. 5 campañas | Corrida completa + ledger sellado | ET2 |
| ET4 | Pool podado | Costo 1,5× neto > 0; ≥ 7/12 trimestres; ≥ 70% de vecinos | Reporte de rechazos | ET3 |
| ET5 | Pool robusto | ≥ 2/3 años positivos; PBO ≤ 0,40 | Pool no vacío | ET4 |
| ET6 | Hasta 5 portafolios finalistas | Stability y Symmetry > 30% como restricción dura; PF ≥1,25; DD ≤US$2.000; peor día ≤US$1.000; ≥150 operaciones; ≥5/mes; P_pass ≥ umbral de ET2 | Perfil completo | ET5 |
| ET7 | Informe de evidencia y congelado del portafolio | DSR ≥0,95; percentil nulo ≥95; MC bloque 8, 500/5.000, P95 DD ≤US$2.000 | Todo + hash de congelado | ET6 |
| ET8 | OOS1 y luego OOS2, una vez, con el motor | Perfil heredado; peso de evidencia de `OOS_EXPOSURE.md` | Aprobado (provisional) o muere | ET7 |
| ET9 | Estrategia NT8 nativa generada del spec congelado; corre en IS, OOS1 y OOS2 | ≥99% de operaciones iguales; ≤1 tick; neto ≤2% de diferencia; NT8 manda | Paridad por bloque | ET8 |
| ET10 | Pre-vuelo y plan de intentos | Reglas verificadas; kill-switch; tope de intentos y fecha límite; evaluación US$209, reset US$0; cuenta fondeada con MLL en tiempo real | Checklist completo | ET9 |

## Referencias nativas de ET1 (R4) — propuesta de 8 a 10

Estrategias triviales con órdenes nativas de NT8, solo para comparar fills operación por operación con el motor propio. Sus resultados no son hallazgos.

| ID | Qué prueba |
|---|---|
| N1 | Long a mercado al cierre de una barra fija, stop y objetivo por ATR |
| N2 | Short, igual que N1, con otro periodo y otros múltiplos de ATR |
| N3 | Cruce de medias con stop por ATR y **sin objetivo** (sale por stop o a las 11:30) |
| N4 | Ruptura del rango de los primeros 15 minutos, stop por ATR ancho, salida forzada 11:30 |
| N5 | Stop y objetivo por ATR muy cercanos para provocar ambos en la misma barra |
| N6 | Entrada en el último instante permitido (11:29) para probar el plano de las 11:30 |
| N7 | Stop ATR amplio con salida por tiempo, para cubrir gaps |
| N8 | Dos miembros con prioridad (choque de señales) |
| N9 | Orden límite como objetivo, para fijar la convención de fills de límites |
| N10 | Día de roll y días excluidos (cobertura por el rango de fechas) |

## Pendientes que bloquean
- **D-018** biblioteca de condiciones y **D-019** rangos de ATR (bloquean ET3; por defecto vigentes hasta que el usuario los ajuste).
- Antes de ET8: `OOS_EXPOSURE.md`. Antes de ET10: plazo global y tope de intentos.
