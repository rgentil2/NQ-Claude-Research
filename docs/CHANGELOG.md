# Changelog del contrato

## NQC 2.0.0 — aclaración de ET1 — 2026-11-12
El usuario decidió un blackout de ±5 minutos sobre noticias USD de Forex Factory (impacto alto y medio, todos los tipos) más los T1 de MFFU (D-027). Como el contrato no menciona noticias, esto se trata como **aclaración de ET1 antes de ver resultados**, no como cambio de estructura:
- Cambia el ejecutor: `EXECUTOR_SPEC` pasa a 2.1.0 (motivos `NEWS` y `NEWS_FLAT`, hash del calendario en el resumen).
- Se agrega `docs/NEWS_BLACKOUT.md` con la regla, el formato del archivo y la aceptación.
- ET1 consume un archivo de datos nuevo (calendario). No cambian las etapas, los datos que toca cada una, su orden ni los umbrales.
- Efecto real: a diferencia de los T1 (8:30 y 14:00), los eventos de impacto medio y alto de Forex Factory caen dentro de la ventana 09:30–11:30 ET.
- Si se considerara cambio de estructura, correspondría NQC 3.0.0; se anota aquí para que el usuario pueda objetarlo antes de ET1.

## NQC 2.0.0 — 2026-11-11 — CONGELADO (Nivel A)
Cambios de estructura pedidos por el usuario sobre NQC 1.0.0. **Ninguna etapa se había ejecutado sobre datos de mercado**, así que no hay resultados vistos ni ensayos que sumar.
- **D-006b — sin tope de riesgo por entrada.** Se elimina "riesgo ≤ US$200, o sea stop ≤ 35 ticks" porque ahogaba al NQ. El riesgo por operación se controla en las puertas del portafolio (peor día ≤ US$1.000, DD ≤ US$2.000, P_pass) y en la capa protectora. Los costos (US$24,68) no cambian. Se elimina el motivo `RISK` de la cascada de señales.
- **D-021 — salidas por ATR generadas por la evolución (ET3).** Solo stop por ATR (obligatorio) y objetivo por ATR (opcional). El periodo del ATR, k_sl y k_tp son genes. Se eliminan los stops fijos en ticks.
- **D-022 — Stability y Symmetry como propiedades del portafolio (ET6).** Restricciones duras durante la construcción y gates de aceptación; no se aplican a estrategias individuales.
- **D-023 — paridad nativa después del OOS.** ET8 y ET9 intercambian su lugar: ET8 pasa a ser OOS sellado (evaluado por el motor con el portafolio ya congelado) y ET9 pasa a ser paridad nativa sobre IS, OOS1 y OOS2. El congelado del portafolio pasa a ET7. NT8 nativo manda para el veredicto final del OOS.
- Versión numeral: el congelado numérico tras ET2 pasa a llamarse NQC 2.1.0; el siguiente cambio mayor sería NQC 3.0.0.
- Interpretación pendiente de confirmar por el usuario: la frase "la paridad" al final del punto de ET6 se entendió como parte del punto de ET8/ET9.

## NQC 1.0.0 — 2026-11-11 — REEMPLAZADO
- Creación del contrato con etapas ET0–ET10, congelamiento en dos niveles, política de OOS y campañas.
- Decisiones del usuario incorporadas antes del congelado: D-004b (36 meses, sin examen intermedio; ET5 pasa a "Robustez interna por bloques"), D-007, D-008, D-010 (09:30–11:30 ET, plano 11:30, sin hora límite de entrada), D-011 (5 campañas), D-012, D-013, R1 (Merge back adjusted, roll de NT8), R2 (zona horaria de NT8), R3 (motor propio en NinjaScript; nativo en ET1 y ET8), R4 (referencia nativa sin Python), R5 (condiciones en ATR/ticks/puntos).
- Umbrales de Nivel B ajustados al nuevo reparto de datos: ET4 trimestres ≥ 7 de 12; ET5 años positivos ≥ 2 de 3; ET0 coincidencia de zona horaria ≥ 70%.

## NQC 1.0.0 — 2026-11-11 — PROPUESTO (versión inicial)
- Creación del repo y del contrato.
