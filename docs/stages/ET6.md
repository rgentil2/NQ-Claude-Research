# ET6 — Portafolios con ejecutor en el bucle

**Archivo de traspaso (memoria viva de la etapa).** Lo lee el agente `et6-portafolio` al empezar y lo actualiza al terminar. La autoridad es `docs/CONTRACT_NQC_2.0.0.md` §2 ET6; si hay conflicto, manda el contrato.

| Campo | Valor |
|---|---|
| Estado | CÓDIGO LISTO, SIN EJECUCIÓN REAL (propuesto: DESARROLLO) (el estado oficial vive en el master §6) |
| Depende de | ET5 |
| Objetivo | Encontrar combinaciones de 3–5 miembros que cumplan el perfil con 1 NQ compartido. |
| Entradas | Pool robusto de ET5; ejecutor. |
| Salidas | Hasta 5 finalistas con PORTFOLIO_SPEC.json |
| Gate | Perfil completo (PF≥1,25; Stability y Symmetry >30% duras; DD≤2.000; peor día≤1.000; ≥150 ops; P_pass) |
| Rutas propias (lectura/escritura) | ninjatrader/src/NQC_ET6*, runs/*ET6* |
| Límite de datos / reglas duras | IS solamente; Stability/Symmetry solo a nivel portafolio (D-022). |

## Pendientes y notas vigentes
- Pendiente del usuario: confirmar interpretación de ET6 (D-019). Interpretación adoptada: ver "Decisiones" (D6-1..D6-11).
- Código listo (NO ejecutado sobre datos reales): `ninjatrader/src/NQC_ET6_Portfolio.cs` + prueba sintética `NQC_ET6_PortfolioTest.cs` (22/22 con mcs+mono). ET6 real exige ET2 aprobada (umbral P_pass) y el pool real de ET5.
- Pendiente: umbral PpassMin (lo fija ET2; NaN = la puerta falla cerrada); EngineHash/LibraryHash los pasa el runner; runner NT8 (no escrito); el IS real debe entregar M1 completas desde 00:00 y volumen alineado (Bind de la biblioteca).
- Defecto en ET1 (a corregir por su dueño): NQCE_Executor.DdM2m cuenta doble el P&L cerrado de la sesión (cumReal ya incluye el cierre y eq=sessReal también). ET6 usa su propio DD M2M por hook.

## Decisiones tomadas en esta etapa
Interpretación de ET6 propuesta (a confirmar por el usuario):
- D6-1 Prioridad = orden por fitness IS descendente del pool (D-016), congelada en el spec.
- D6-2 Beam acotado: MaxPool=30 mejores por fitness, ancho 10, profundidad 5; nivel 1 = todos los solos, luego cada estado se extiende con cualquier candidato (~K+4·W·K evaluaciones).
- D6-3 Stability>30 y Symmetry>30 duras para toda combinación evaluada de >=3 miembros (descartada, no se expande); los parciales de 1-2 miembros no se filtran (Symmetry de un solo lado = 0). Lectura alternativa: aplicar solo a la salida.
- D6-4 Puntuación del beam: menos puertas falladas, luego t de la expectativa neta por operación del portafolio; desempate hash(semilla|clave); dedupe por huella de operaciones.
- D6-5 Corrida a 1,5x y P_pass solo si las demás puertas pasan ("NE" si no).
- D6-6 Finalistas (<=5): entre las aceptadas, por t, P_pass, menos miembros, hash; voraz con diversidad (Jaccard de miembros <=0,5 y huella distinta).
- D6-7 Ensayo = combinación única evaluada con el ejecutor (también 1-2 miembros); desglose por tamaño; ledger acumulado = previo + corrida.
- D6-8 P_pass: NQC_AccountSim.Rolling, RAPID_INTRADAY_50K, censurados fuera; PpassMin parámetro, NaN falla cerrada.
- D6-9 Mes sin operaciones = 0; peor día = -min(DailyNet). D6-10 IS solo (excepción fuera de 2020-2022). D6-11 DD M2M propio (defecto ET1).

## Bitácora de traspasos
Formato: `AAAA-MM-DD · qué se hizo · artefactos/hash · qué queda · bloqueos`. Máximo 5 líneas por entrada.

- 2026-11-14 · Archivo creado al adoptar el modelo de agentes por etapa (`docs/WORK_MODEL.md`). Sin trabajo nuevo.
- 2026-11-14 · ET6 código + prueba sintética 22/22 (mcs+mono): beam, perfil completo, spec JSON versionado, TSV de rechazos/ensayos. Sin datos reales (falta ET2 y pool de ET5).
  Hashes: Portfolio.cs 5485eda511ff...d9413 · PortfolioTest.cs a9dc27006e42...c1418 · resumen beam ea09d3fdfc92...ee02d · spec F1 dd6d61706c41...dea3b (prueba, no resultado de mercado).
  Queda: ET2 (umbral P_pass), pool real, runner NT8, corregir DdM2m de ET1. Preguntas: interpretación D6-3, MaxPool/ancho, tratar parciales como ensayos.
