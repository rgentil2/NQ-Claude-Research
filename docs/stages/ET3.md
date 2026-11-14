# ET3 — Generación evolutiva

**Archivo de traspaso (memoria viva de la etapa).** Lo lee el agente `et3-generacion` al empezar y lo actualiza al terminar. La autoridad es `docs/CONTRACT_NQC_2.0.0.md` §2 ET3; si hay conflicto, manda el contrato.

| Campo | Valor |
|---|---|
| Estado | NO INICIADA (el estado oficial vive en el master §6) |
| Depende de | ET2 |
| Objetivo | Búsqueda evolutiva guiada por fitness (t de expectativa neta) en el IS. |
| Entradas | NQC 2.1.0; biblioteca de condiciones (D-018); rangos ATR (D-019). |
| Salidas | Databank top-K por campaña, TRIALS_LEDGER sellado, curva de fitness |
| Gate | Corrida completa + ledger con hash |
| Rutas propias (lectura/escritura) | ninjatrader/src/NQC_ET3*, runs/*ET3*, docs/TRIALS_LEDGER.md |
| Límite de datos / reglas duras | IS 2020–2022 solo; máx. 5 campañas (C1–C5), ≤100.000 evaluaciones; semillas declaradas antes. |

## Pendientes y notas vigentes
- **AL EMPEZAR ET3: el agente debe preguntar al usuario** por D-018 (biblioteca de condiciones) y D-019 (rangos de ATR) antes de escribir código. Instrucción del usuario, 2026-11-14.
- Cada evaluación cuenta como ensayo en el ledger.

## Decisiones tomadas en esta etapa
**Ninguna del usuario todavía.** AskUserQuestion no está disponible para este agente (2026-11-14); las preguntas quedan redactadas abajo para que el orquestador las plantee. No se escribe código hasta tener respuesta.

Nota de numeración: DECISIONS.md tiene D-019a (rangos ATR, ACEPTADA por el usuario 2026-11-12) y D-019 (parámetros genéticos, pendiente). La instrucción del usuario pide reconfirmar rangos ATR; se pregunta igualmente.

### Preguntas pendientes para el usuario (recomendada primero)
- **P1 (D-018) Biblioteca de condiciones.** (a) Recomendada: núcleo coherente con 09:30–11:30, ~40–60 fórmulas / ~400–600 condiciones: rango de apertura (cierre vs máx/mín del rango 09:30–09:45), distancia a VWAP de sesión, EMA cruces/pendiente, momentum de N barras, tamaño de barra y rango reciente, volumen relativo, sello horario. Menos ensayos por espacio, menos riesgo de sobreajuste. (b) Biblioteca completa anterior (120 fórmulas, 1.320 condiciones): más cobertura, espacio mucho mayor, más falsos positivos con 100.000 evaluaciones. (c) Mínimo (~15 fórmulas, solo apertura/EMA/momentum): muy restringido, riesgo de no encontrar nada.
- **P2 (D-018) Unidades.** (a) Recomendada: todo umbral en múltiplos de ATR (distancias, rangos, momentum), tick/puntos solo para filtros de precio absoluto (R5). (b) Mezcla libre ATR/ticks/puntos por condición: más genes de unidad, más ensayos efectivos, más sobreajuste. (c) Solo puntos fijos: no escala con volatilidad; no recomendada.
- **P3 (D-019) Rangos ATR.** (a) Recomendada: los ya aceptados: periodo 5–100 (entero), k_sl 0,5–6,0 paso 0,25 (23 valores), k_tp 0,5–8,0 paso 0,25 o ausente (32 valores + ausente). (b) Rango reducido (periodo 5–50, k_sl 0,5–4, k_tp 0,5–6): menos combinaciones, menor riesgo de sobreajuste, pero puede excluir salidas válidas. (c) Paso 0,5 en k: espacio de salidas ~4 veces menor.
- **P4 Presupuesto de campañas.** (a) Recomendada: usar solo C1 (100.000 evaluaciones) y reservar C2–C5 para rediseño si falla; (b) planificar 2 campañas con semillas distintas para medir estabilidad de la búsqueda (200.000 ensayos en total, penaliza más el ajuste por múltiples pruebas); (c) planificar las 5 desde ahora: no recomendado, cada campaña adicional encarece la corrección por ensayos.

### Recomendaciones propias (no son preguntas)
Población 500, 200 generaciones (=100.000 evaluaciones, D-019 de DECISIONS), torneo de 3, cruce uniforme, mutación 5% por gen, elitismo 5%; genoma de 20 genes; población inicial con frecuencia mínima de señal; semillas declaradas en este archivo antes de correr (propuesta: C1 = 20261114 y derivadas por generación vía NQCE_Rng); fitness t con mínimo 100 operaciones, penalización a 0 por debajo; top-K 2.000 con deduplicación por huella de operaciones.

### Formato de genoma propuesto (20 genes, sobre NQCE_Member)
Genes 1–16: pares (familia/condición de la biblioteca, parámetro discretizado en unidades de ATR) de entrada, con ranura vacía permitida; se combinan con AND (INQCE_Condition). Gen 17: dirección (+1/-1). Gen 18: periodo ATR. Gen 19: k_sl. Gen 20: k_tp (o ausente). MaxPerDay=1 y TimeExitStamp=-1 por defecto (no evolucionan). Serialización: GenomeText() existente + hash SHA256 (NQCE_Const.Sha256Text) como id de ensayo en el ledger.

### Plan de paquetes de trabajo (WP) de ET3
- WP1 Biblioteca de condiciones D-018 (implementan INQCE_Condition, tests contra la referencia de ET1). Bloqueado por P1/P2.
- WP2 Codificación del genoma (20 genes, límites D-019a, reparación de valores inválidos).
- WP3 Operadores GA (inicialización con frecuencia mínima, torneo, cruce uniforme, mutación, elitismo) sobre NQCE_Rng con semilla.
- WP4 Fitness: evaluación con NQCE_Executor sobre IS 2020–2022, t de expectativa neta, mín. 100 operaciones.
- WP5 Ledger: cada evaluación a docs/TRIALS_LEDGER.md/TSV con hash; databank top-K; curva de fitness por generación.
- WP6 Runner NT8 (Strategy Analyzer) y runbook; pruebas con datos sintéticos (sin resultados como validados).
- WP7 Campaña C1 en NT8 con evidencia (log + hash) y SUMMARY.txt en runs/<fecha>_ET3_C1/. Depende de ET2 aprobada técnicamente.

## Bitácora de traspasos
Formato: `AAAA-MM-DD · qué se hizo · artefactos/hash · qué queda · bloqueos`. Máximo 5 líneas por entrada.

- 2026-11-14 · Archivo creado al adoptar el modelo de agentes por etapa (`docs/WORK_MODEL.md`). Sin trabajo nuevo.
- 2026-11-14 · Arranque: lectura de contrato/ET1 Core; AskUserQuestion no disponible, 4 preguntas (D-018, D-019, presupuesto) redactadas, WP1–WP7 y genoma propuestos. Sin código. Bloqueo: respuestas del usuario. Pide al orquestador registrar D-018/D-019.
