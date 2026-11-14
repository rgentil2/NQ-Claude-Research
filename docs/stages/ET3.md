# ET3 — Generación evolutiva

**Archivo de traspaso (memoria viva de la etapa).** Lo lee el agente `et3-generacion` al empezar y lo actualiza al terminar. La autoridad es `docs/CONTRACT_NQC_2.0.0.md` §2 ET3; si hay conflicto, manda el contrato.

| Campo | Valor |
|---|---|
| Estado | EN DESARROLLO (WP1-WP5 escritos sin compilar; pendiente compilar y correr NQC_ET3_LibraryTest en NT8) (el estado oficial vive en el master §6) |
| Depende de | ET2 |
| Objetivo | Búsqueda evolutiva guiada por fitness (t de expectativa neta) en el IS. |
| Entradas | NQC 2.1.0; biblioteca de condiciones (D-018); rangos ATR (D-019). |
| Salidas | Databank top-K por campaña, TRIALS_LEDGER sellado, curva de fitness |
| Gate | Corrida completa + ledger con hash |
| Rutas propias (lectura/escritura) | ninjatrader/src/NQC_ET3*, runs/*ET3*, docs/TRIALS_LEDGER.md |
| Límite de datos / reglas duras | IS 2020–2022 solo; máx. 5 campañas (C1–C5), ≤100.000 evaluaciones; semillas declaradas antes. |

## Pendientes y notas vigentes
- Preguntas D-018/D-019a ya respondidas (ver Decisiones). Pendiente: compilar en NT8 el paquete dist/NQC_ET3_pack.zip y correr NQC_ET3_LibraryTest (evidencia log+hash); decisiones del usuario sobre la lista R5 (abajo); ET2 aprobada antes de cualquier corrida real.
- El runner debe pasar el VOLUMEN a NQCE3_Library.Bind(bars, volumen): NQCE_Bars (ET1 Core) no lo guarda. Sin volumen, 24 familias (264 condiciones) no dan senal.
- Cada evaluación cuenta como ensayo en el ledger.

## Decisiones tomadas en esta etapa
Registradas en docs/DECISIONS.md (2026-11-14), preguntas P1-P4 CERRADAS:
- P1/P2 = D-018: biblioteca completa anterior (120 formulas, 1.320 condiciones), umbrales en multiplos de ATR (R5).
- P3 = D-019a: periodo ATR 5-100; k_sl 0,5-6,0 paso 0,25 (23 valores); k_tp 0,5-8,0 paso 0,25 (31 valores, ver nota) o ausente.
- P4 = D-029: solo C1 por ahora; C2-C5 reservadas.
- D-019 (parametros geneticos): propuesta del agente, no objetada: poblacion 500, 200 gen., torneo 3, cruce uniforme, mutacion 5%/gen, elitismo 5%, top-K 2.000 dedup. Semilla C1 = 20261114 (DECLARADA aqui antes de correr; fijada en NQCE3_Params.Seed).
- Nota: 0,5..8,0 paso 0,25 son 31 valores (no 32 como decia mi texto previo); el codigo usa 31 + ausente = 32 niveles del gen.

### WP1 HECHO (sin compilar): biblioteca D-018 portada
Fuente: repo historico en solo lectura, commit 80ac30b (8 archivos, SHA256 en la cabecera de NQC_ET3_Library.cs). Archivos: NQC_ET3_Library.cs (NQCE3_Library : INQCE3_Library, NQCE3_Cond), NQC_ET3_LibCore.cs (serie, memoria, catalogo, auditoria, 11 reglas), NQC_ET3_LibMath.cs, NQC_ET3_LibFeatures.cs (120 familias, port mecanico), NQC_ET3_LibraryTest.cs. condId 1..1320 = tipo+1 (familia = tipo/11, regla = tipo%11). Parametro genetico = periodo n 4..30 (27 niveles), NO el umbral. Cambio en mi interfaz: Create(condId, nivel, atrPeriod, dir).

### Auditoria WP1 contra el contrato (NO decidida por mi; para el orquestador y el usuario)
**(a) R5.** Clasificacion por familia (codificada en NQCE3_Catalog.UnitClass; las reglas 3-8 usan umbrales fijos 0,15/0,35/0,60 en unidades de la familia):
- A (53 fam = 583 cond): multiplos de ATR. Cumplen.
- B (44 fam = 484 cond): razon adimensional acotada o autonormalizada (RSI, estocastico, CMF, Aroon...), sin nivel ni porcentaje de precio. Cumplen en espiritu; el umbral es una fraccion, no un multiplo de ATR literal (decision: aceptar o no).
- E (4 fam = 44 cond) DUDOSAS: 32 (VPT: usa retorno porcentual pero multiplica por Close; equivale a delta/ATR), 60 (Connors: ROC solo para el rango percentil), 85 (cambio relativo de volatilidad log), 103 (VFI: porcentaje solo para el signo/corte).
- C (17 fam = 187 cond) INCUMPLEN (unidades de porcentaje/retorno de precio; con umbrales fijos 0,15-0,60 casi nunca disparan, las reglas 0,1,2,9,10 por signo si): 63 PPO, 65 KST, 66 KST derivada, 67 TRIX aceleracion, 75 Coppock, 83 Bollinger bandwidth/precio, 86 ATR/precio, 87 Parkinson, 88 Garman-Klass, 89 Rogers-Satchell, 90 Yang-Zhang, 92 Ulcer, 100 volumen relativo x ROC, 101 NVI, 102 PVI, 104 flujo volumen x ROC, 105 PVT suavizado. Correccion propuesta: dividir por ATR en vez de por precio (p. ej. 63 -> MACD/ATR, duplicado de la 8; 65/66/75 -> cambios de precio/ATR; 83 -> 4*Std/ATR; 86 -> variacion de ATR en ATR; 87-90 -> estimador en puntos/ATR). Alternativa: excluir las 17 (187 cond, quedan 1.133).
- D (2 fam = 22 cond) INCUMPLEN: 21 EOM (producto de dos razones/ATR, escala ATR^2), 118 Polarized fractal (constante 1 punto por barra mezclada con n barras). Correccion propuesta: normalizar por ATR (21: un solo factor ATR; 118: delta/ATR) o excluir.
- Total: cumplen A+B = 1.067 (97 fam); dudosas 44; incumplen 209 (19 fam). Ademas, el umbral fijo 0,15/0,35/0,60 no es un parametro: D-018/D-019 pedia "umbral discretizado en ATR"; si se quiere evolucionar el umbral hay que cambiarlo (decision pendiente).
**(b) Futuro.** Revisadas las 120 formulas: ninguna lee indices > at (DPO usa at-lag; Donchian previo usa at-1; Ichimoku sin desplazamiento; Supertrend/Chande-Kroll secuenciales con barras previas). La prueba L4 lo verifica por truncado y por reemplazo del futuro (pendiente de correr en NT8).
**(c) Volumen / datos.** 24 familias dependen de volumen: 10, 11, 13, 17-21, 32, 64, 99-112 (264 cond). NT8 M1 lo entrega, pero NQCE_Bars no lo guarda: hay que pasarlo a Bind (cambio en el runner, no en ET1 Core). Ninguna usa bid/ask ni volumen por precio (los "delta"/flujo son proxies por vela). Las 24 son baja prioridad si el usuario prefiere excluirlas del primer corte (no decido).
**(d) Ventana y calentamiento.** La referencia exigia 3n+22 barras y recortaba la recursion al inicio de la lista, pero familias 31 (MACD hist) y 69 (STC) miran hasta 7n+6 barras atras (n=30: 216). Aqui RequiredBars = 7n+10 y, por defecto (SameDay), todas deben ser del mismo dia calendario de la barra de senal (si no, sin senal). Con la serie completa de NT8 (ETH, desde 00:00) siempre se cumple a las 09:31 (n=30 pide 220 barras): se usan barras de pre-mercado, de poco volumen. Si el runner pasa solo la ventana 09:29-11:32 (124 barras), solo n<=16 puede evaluar a partir de las 11:30 y n=30 nunca. Modo AnyHistory disponible. El ATR interno es simple de n barras, distinto del gen ATR de salida (Wilder).
**Otros.** Formulas log (87-90, 85, 92) dan NaN con precio <= 0 (back-adjusted lo permite en teoria): se tratan como sin senal.

### Formato de genoma propuesto (20 genes, sobre NQCE_Member)
Genes 1–16: pares (familia/condición de la biblioteca, parámetro discretizado en unidades de ATR) de entrada, con ranura vacía permitida; se combinan con AND (INQCE_Condition). Gen 17: dirección (+1/-1). Gen 18: periodo ATR. Gen 19: k_sl. Gen 20: k_tp (o ausente). MaxPerDay=1 y TimeExitStamp=-1 por defecto (no evolucionan). Serialización: GenomeText() existente + hash SHA256 (NQCE_Const.Sha256Text) como id de ensayo en el ledger.

### Plan de paquetes de trabajo (WP) de ET3
- WP1 Biblioteca de condiciones D-018: HECHO sin compilar (ver arriba); pruebas L1-L7 en NQC_ET3_LibraryTest, falta correrlas en NT8.
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
- 2026-11-14 · WP1: biblioteca D-018 portada (1.320 cond., NQC_ET3_Lib*.cs + LibraryTest) y auditada; R5: 1.067 cumplen, 44 dudosas, 209 incumplen (lista arriba, sin decidir). Create() ahora lleva dir. ZIP dist/NQC_ET3_pack.zip (SHA 6ab5096c...). Sin compilar ni correr. Pide: decision usuario sobre C/D/E y sobre el umbral fijo; runner debe pasar volumen.
