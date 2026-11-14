# ET3 — Generación evolutiva

**Archivo de traspaso (memoria viva de la etapa).** Lo lee el agente `et3-generacion` al empezar y lo actualiza al terminar. La autoridad es `docs/CONTRACT_NQC_2.0.0.md` §2 ET3; si hay conflicto, manda el contrato.

| Campo | Valor |
|---|---|
| Estado | EN DESARROLLO (WP2-WP5 escritos sin compilar; WP1 bloqueado) (el estado oficial vive en el master §6) |
| Depende de | ET2 |
| Objetivo | Búsqueda evolutiva guiada por fitness (t de expectativa neta) en el IS. |
| Entradas | NQC 2.1.0; biblioteca de condiciones (D-018); rangos ATR (D-019). |
| Salidas | Databank top-K por campaña, TRIALS_LEDGER sellado, curva de fitness |
| Gate | Corrida completa + ledger con hash |
| Rutas propias (lectura/escritura) | ninjatrader/src/NQC_ET3*, runs/*ET3*, docs/TRIALS_LEDGER.md |
| Límite de datos / reglas duras | IS 2020–2022 solo; máx. 5 campañas (C1–C5), ≤100.000 evaluaciones; semillas declaradas antes. |

## Pendientes y notas vigentes
- Preguntas D-018/D-019a ya respondidas (ver Decisiones). Pendiente: entregar la biblioteca (WP1); compilar en NT8 los NQC_ET3_*; ET2 aprobada antes de cualquier corrida real.
- Cada evaluación cuenta como ensayo en el ledger.

## Decisiones tomadas en esta etapa
Registradas en docs/DECISIONS.md (2026-11-14), preguntas P1-P4 CERRADAS:
- P1/P2 = D-018: biblioteca completa anterior (120 formulas, 1.320 condiciones), umbrales en multiplos de ATR (R5).
- P3 = D-019a: periodo ATR 5-100; k_sl 0,5-6,0 paso 0,25 (23 valores); k_tp 0,5-8,0 paso 0,25 (31 valores, ver nota) o ausente.
- P4 = D-029: solo C1 por ahora; C2-C5 reservadas.
- D-019 (parametros geneticos): propuesta del agente, no objetada: poblacion 500, 200 gen., torneo 3, cruce uniforme, mutacion 5%/gen, elitismo 5%, top-K 2.000 dedup. Semilla C1 = 20261114 (DECLARADA aqui antes de correr; fijada en NQCE3_Params.Seed).
- Nota: 0,5..8,0 paso 0,25 son 31 valores (no 32 como decia mi texto previo); el codigo usa 31 + ausente = 32 niveles del gen.

### BLOQUEO WP1: la biblioteca D-018 NO esta en este repo
Solo hay menciones (LESSONS_FROM_PREVIOUS_REPOS.md l.14, DECISIONS D-018). No hay lista de formulas ni codigo en docs/, tools/ ni ninjatrader/src/. No se reconstruye de memoria. Se necesita del usuario: el archivo/lista de las 120 formulas y 1.320 condiciones (con sus umbrales/niveles y definicion exacta) del repo MES-Trading-Research, o permiso para adjuntar ese repo con add_repo. Mientras tanto la interfaz INQCE3_Library (en NQC_ET3_Genome.cs) define el enchufe.

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
- 2026-11-14 · Decisiones D-018/D-019a/D-029 registradas; biblioteca NO encontrada en el repo (WP1 bloqueado, pide archivo al usuario). Escritos sin compilar: NQC_ET3_Genome.cs (WP2 + interfaz INQCE3_Library), _Operators.cs (WP3), _Fitness.cs (WP4), _Ledger.cs (WP5 + bucle NQCE3_Campaign). Sin corridas ni resultados.
