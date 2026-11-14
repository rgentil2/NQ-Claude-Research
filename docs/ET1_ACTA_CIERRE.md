# ET1 — Acta de cierre (PROPUESTA, sin firmar)

**Contrato:** NQC 2.0.0 (Nivel A congelado) · **Ejecutor:** EXECUTOR_SPEC 2.3.0 · **Fecha del borrador:** 2026-11-14
**Estado:** `EJECUTADA` — **aprobación condicionada firmada el 2026-11-14** (§8). Pasa a `APROBADA` cuando se ejecute C1 (ZIP `NQC_ET1B2_GapParity` entregado, pendiente de correr) y se complete C2 (hecho; ver §5).
**Gate del contrato:** P1–P6 al 100%. Esta acta no relaja el gate: marca con claridad qué está demostrado y qué no.

---

## 1. Veredicto propuesto

- **P3, P5 y P6: cumplidos con evidencia ejecutada.**
- **P1: cumplido en 8 de los 9 casos exigidos.** El caso *gap adverso* está demostrado en NT8 (46/46) pero **no contra el motor C#**: las 8.110 operaciones de la paridad no contienen ningún salto. Es el único punto que impide declarar P1 al 100% (C1).
- **P2 y P4: cumplidos con observaciones menores** (§4) que el usuario puede aceptar o pedir que se cierren con 2–3 chequeos.
- No se miró economía de ninguna estrategia (§3).

## 2. Resultado por prueba

| Prueba | Evidencia ejecutada | Resultado | Observación |
|---|---|---|---|
| **P1** Paridad contra NT8 nativo | 9 referencias nativas N1–N9 (`runs/2026-11-12_ET1A_native_run1`), réplica fuera de línea (8.110/8.110, `ET1A_replication`) y paridad del motor propio contra los libros nativos (`runs/2026-11-13_ET1B_run1`): **8.110 de 8.110 operaciones iguales** en dirección, entrada, stop, nombre de salida, hora y precio; 0 operaciones de más o de menos en cada N. ATR (5/14/20/50) y EMA (5/13) propios vs NT8 en 1.054.234 barras: diferencia máxima 0 | **PASS con reserva** | Casos del contrato cubiertos: largo (N1) y corto (N2); ATR con periodos y múltiplos distintos (N1–N9); objetivo por ATR (N1, N5, N8, N9); sin objetivo (N3, N4, N7); SL y TP en la misma barra (N5); salida forzada 11:30 (N6); entrada en el último instante (N6); dos miembros con prioridad (N8: 766 de A + 441 de B). **Gap adverso: solo en NT8** (sonda `ET1A_gapprobe`, 46/46 salidas a la apertura de la barra, regla en EXECUTOR_SPEC 2.3.0). Convención de límites fijada: el objetivo exige atravesar el precio, el stop se activa al tocarlo |
| **P2** Casos límite del motor | Cubiertos por B1 (así lo declara el master) | **PASS con observación** | Señal en la última barra: N6 (762/762 por tiempo). Señales simultáneas: N8. Día excluido: 25 días excluidos del IS y calendario `OK 3954 eventos`, sin diferencias de operaciones. **Cruce de sesión: sin caso explícito**; queda cubierto por diseño (posición plana a las 11:30, sin hora límite de entrada) |
| **P3** Costos | `runs/2026-11-13_ET1C_run1/SELFTEST_REPORT.txt`: costo RT US$24,68; estrés 1,5× = US$37,02 | **PASS** | Por la aclaración de 2026-11-12, la paridad se mide en bruto (slippage 0) y el costo fijo se aplica después. Los 2 chequeos prueban las constantes; que la fricción se aplique **exactamente una vez** por operación no tiene chequeo propio |
| **P4** Capa protectora | 5 chequeos: `prot_pause`, `prot_close`, `prot_none`, `guard_start`, `guard_mid` (todos OK) | **PASS con observación** | El reporte no muestra de forma explícita la **liberación** de la protección en la sesión siguiente (D-007: ningún bloqueo es permanente) |
| **P5** Simulador de cuenta | 14 trayectorias sintéticas con resultado conocido, todas OK (pasa; falla por MLL exacto y por exceso; consistencia; días mínimos; trailing y lock; día sin operación no cuenta; perfil EOD) | **PASS** | Supera el mínimo de 10. Referencia Python independiente: 17 pruebas OK (`7fa15c2`) |
| **P6** Determinismo | `P_pass=0.1931 n=378` idéntico; mismo hash de informe `b9f84a4e…` en **12 instancias** del Analyzer y en compilación Mono fuera de NT8 | **PASS** | Idéntico entre runtimes |

## 3. Cumplimiento de la regla de ET1

> «No se reportan ni se miran PF, beneficio o DD de estrategias de investigación.»

- Las referencias N1–N9 son triviales; sus informes registran conteos de salidas (SL/TP/TIME/NEWS), no resultados económicos.
- La sonda de saltos reporta el costo del salto frente al stop (media ~US$32, máximo US$320 con 1 NQ). Es una propiedad del modelo de fills de NT8, no el desempeño de una estrategia.
- ET1 corrió solo sobre el IS (2020-01-01 a 2022-12-30). **No se tocó OOS.** Registrar: sin exposición nueva a OOS por ET1.

## 4. Artefactos y hashes

| Artefacto | SHA256 |
|---|---|
| `NQC_ET1A_NativeRefs.cs` (ET1A-1.0.0) | `55259a5db9580fa06355004b6c32115ef3bff6123069cca0b70dfb12c8d47503` |
| Sonda de saltos, `trades_sha256` | `e2aab0565b45ea228ecd38fedcf5ea38031d647a7291f9a47d299182f41475ca` |
| ET1B `PARITY_REPORT` original | `15c0da5fbb817a1c401a3d822df28dfc8e151539cca6f094d48b4f13cc1754d1` |
| `NQC_ET1B_EngineParity.cs` (ET1B-1.0.0) | `fe4a058f4df1a588173945435c3af7e15fff8eb7a7bf833789c6cc166972b40a` (archivo del repo; un solo commit, `968b498`, anterior a la corrida) |
| `NQC_ET1B2_GapParity.cs` (ET1B2-1.0.0, cierre C1) | `9b5839fb4d58c8fe641df581f8e32dc009ab4ace2f0824074af4e8e050878e19` |
| `NQC_ET1C_AccountSim.cs` (ET1C-1.0.0) | `aebe6fb1d3c9eb670c9f9ffe4455143b6638cfed4bcc69fd9ad517f0da5b061e` |
| ET1C informe | `b9f84a4e6a7509b4fc774bd460b1c7345d4323171a763135d63d89d2c2ce8aa1` |
| Calendario de noticias (3.954 eventos) | `93d837ac…377d` (completo en `runs/2026-11-12_NEWS_run1/`) |

## 5. Cierres antes de marcar `APROBADA`

| ID | Qué | Tipo | Esfuerzo |
|---|---|---|---|
| **C1** | **B2 — gap adverso contra el motor.** Correr el motor C# sobre los 46 casos de la sonda y exigir 46/46 con salida a la apertura de la barra. Cierra P1 al 100% | **Bloqueante** del gate | Una corrida corta en Strategy Analyzer |
| **C2** | Registrar el SHA256 de `NQC_ET1B_EngineParity.cs` y completar la trazabilidad de B1 y C con los campos que exige el master §9. Los `SUMMARY.txt` ya ejecutados no se reescriben (son los de la corrida): el hash y los campos van en sus `RUN_NOTES.md`. **Hecho en el commit de esta acta.** Queda anotado que el `PARITY_REPORT` de B1 en el repo es una versión abreviada (el SHA256 es el del original) | Documental | Hecho |
| **C3** | Decisión del usuario sobre las observaciones: aceptar, o pedir 3 chequeos extra en ET1C — (a) fricción aplicada una vez por operación, (b) liberación de la protección en la sesión siguiente, (c) caso de cruce de sesión | Decisión | Opcional; barato |

## 6. Condición de arranque para ET2 (regresión)

La paridad prueba el motor de ET1B, no código que todavía no existe. Cuando se construya el motor genérico y el ejecutor de portafolio de ET3–ET7:

1. Debe reproducir **N1–N9 (8.110/8.110)**, los **46 saltos** y los **22 chequeos de ET1C** antes de usarse en ET2.
2. Si reutiliza el núcleo de ET1B sin cambios, basta registrar el hash del núcleo; si lo reescribe, repite la paridad completa.
3. Cualquier cambio posterior de semántica de fills pasa por el changelog y repite P1.

Esto va en el plan de trabajo como prueba de regresión, no como etapa nueva (no cambia el Nivel A).

## 7. Convenciones de NT8 que quedan fijadas en EXECUTOR_SPEC 2.3.0

- Entrada a mercado enviada al cierre de la barra de señal: precio = apertura de la barra siguiente (8.110/8.110).
- Stop y objetivo = entrada ± k × ATR de la señal, al tick más cercano; activos desde la barra de entrada.
- Stop: se activa al tocar. Objetivo (límite): exige atravesar.
- Stop y objetivo en la misma barra: gana el extremo más cercano a la apertura; en empate, el máximo primero.
- Salto: si la barra abre más allá del stop, sale a la apertura; si solo lo toca, al precio del stop.
- Salidas forzadas (11:30, noticias): orden a mercado al cierre de la barra, ejecutada en la apertura de la siguiente, antes de evaluar stop u objetivo.
- Al cerrarse una posición dentro de una barra, al cierre de esa barra puede entrar otra señal.

## 8. Firma

- [x] Apruebo ET1 con C1 ejecutada y C2 completada. C3: ☐ acepto las observaciones ☐ pido los 3 chequeos.
- [ ] No apruebo; motivo: ______________________

Al firmar: el master pasa ET1 a `APROBADA`, se corrige su cabecera (dice EXECUTOR_SPEC 2.1.0; la vigente es 2.3.0) y se registra la entrada en `RESEARCH_LOG.md`.

Usuario: Rodrigo (aprobación dada por comentario en el documento)  Fecha: 2026-11-14

**Nota de registro:** la aprobación es condicionada. ET1 pasa a `APROBADA` en el master solo cuando C1 esté ejecutada (46/46). C2 completada. C3 sin elegir todavía.
