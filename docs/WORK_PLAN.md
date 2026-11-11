# Plan de trabajo — NQC 1.0.0

Estructura congelada; este documento solo organiza la ejecución. Estados en `NQ_PROJECT_MASTER.md`.

## Reparto de trabajo
- **Asistente:** escribe los `.cs`, los sube al repo y genera el ZIP de instalación desde el repo.
- **Usuario:** instala, compila en NinjaTrader (F5), corre en Strategy Analyzer y devuelve `SUMMARY.txt` y los TSV pedidos.
- El asistente no puede compilar NinjaScript. Cada etapa técnica tendrá ciclos de corrección con los mensajes de error que envíes.

## Ruta crítica
ET0 → (D-009) → ET1 → código de ET3–ET7 → ET2 → ET3 real → ET4 → ET5 → ET6 → ET7 → ET8 → ET9 → ET10.
ET2 es el cuello de botella: exige que todo el embudo funcione antes de tocar datos reales.

## Por etapa

| Etapa | Entregable | Parámetros clave | Gate | Depende de |
|---|---|---|---|---|
| ET0 | `NQC_ET0_DataManifest.cs` + runbook (entregado) | Ventana sellos 09:31–11:30; completitud ≥99%; zona horaria ≥70% | G0–G6 | — |
| ET1 | Motor + simulador de cuenta + estrategias nativas de referencia + pruebas P1–P6 | Costos US$24,68; capa protectora por sesión; 5–10 referencias nativas | P1–P6 al 100% | ET0, D-009 |
| ET3–ET7 (código) | Generador, poda, robustez, constructor de portafolios, módulo estadístico | Los de cada etapa | Pruebas técnicas con datos sintéticos | ET1 |
| ET2 | Datasets nulos e inyectados + informe de calibración | ≥5 nulos, ≥5 inyectados; FP ≤5%; potencia ≥70% | Congelado numérico NQC 1.1.0 | ET3–ET7 listos |
| ET3 | Campaña C1 en el IS 2020–2022 | Fitness t; ≥100 operaciones; 100.000 evaluaciones; K=2.000; máx. 5 campañas | Corrida completa + ledger sellado | ET2 |
| ET4 | Pool podado | Costo 1,5× neto > 0; ≥ 7/12 trimestres; ≥ 70% de vecinos | Reporte de rechazos | ET3 |
| ET5 | Pool robusto | ≥ 2/3 años positivos; PBO ≤ 0,40 | Pool no vacío | ET4 |
| ET6 | Hasta 5 portafolios finalistas | PF ≥1,25; Stability y Symmetry >30%; DD ≤US$2.000; ≥150 operaciones; ≥5/mes; P_pass ≥ umbral de ET2 | Perfil completo | ET5 |
| ET7 | Informe de evidencia | DSR ≥0,95; percentil nulo ≥95; MC bloque 8, 500/5.000, P95 DD ≤US$2.000 | Todo | ET6 |
| ET8 | Estrategia NT8 con órdenes nativas | ≥99% de operaciones iguales; ≤1 tick; neto ≤2% de diferencia | Paridad + congelado con hash | ET7 |
| ET9 | OOS1 y luego OOS2, una vez | Perfil heredado; peso de evidencia de `OOS_EXPOSURE.md` | Aprobado o muere | ET8 |
| ET10 | Pre-vuelo y plan de intentos | Reglas verificadas; kill-switch; tope de intentos, costo y fecha límite | Checklist completo | ET9 |

## Referencias nativas de ET1 (R4) — propuesta de 8 a 10

Estrategias triviales con órdenes nativas de NT8, solo para comparar fills operación por operación con el motor propio. Sus resultados no son hallazgos.

| ID | Qué prueba |
|---|---|
| N1 | Long a mercado al cierre de una barra fija, SL y TP fijos en ticks |
| N2 | Short, igual que N1 |
| N3 | Cruce de medias con stop por ATR y objetivo en múltiplos de R |
| N4 | Ruptura del rango de los primeros 15 minutos, stop ≤ 35 ticks, salida forzada 11:30 |
| N5 | SL y TP muy cercanos para provocar ambos en la misma barra |
| N6 | Entrada en la última barra permitida (instante 11:29) para probar el plano de las 11:30 |
| N7 | Stop ATR amplio con salida por tiempo, para cubrir gaps |
| N8 | Dos miembros con prioridad (choque de señales) |
| N9 | Orden límite como objetivo, para fijar la convención de fills de límites |
| N10 | Día de roll y días excluidos (cobertura por el rango de fechas) |

## Pendientes que bloquean
- **D-009** reglas MFFU (bloquea ET1).
- **D-018** biblioteca de condiciones (bloquea ET3).
- Antes de ET9: `OOS_EXPOSURE.md`. Antes de ET10: plazo global y tope de intentos y costo.
