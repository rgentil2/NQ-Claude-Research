# Reglas de la cuenta MFFU 50K — estado de verificación

**Estado (2026-11-12): VERIFICADO en fuente primaria en lo principal.** Quedan puntos de mecánica por confirmar (al final). El simulador de cuenta (ET1) lee estos valores desde configuración con **dos perfiles**; **no se codifican fijos**.

## Dos planes distintos

MFFU llama **Rapid (intradía)** al plan estándar y **Rapid EOD** a otro. No son lo mismo.

| Parámetro | Rapid (intradía) 50K | Rapid EOD 50K |
|---|---|---|
| Fuente | Capturas del usuario de `myfundedfutures.com/plans/rapid` y del checkout (2026-11-12) | Help Center artículo 16158363 (2026-08-24) |
| Precio | US$209, pago único, no se renueva | US$157 según un tercero, **sin verificar**; plan descrito como por tiempo limitado |
| **Evaluación** | | |
| Objetivo | US$3.000 | US$3.000 |
| MLL | US$2.000, modo EOD | US$2.000, EOD |
| Límite diario | Ninguno | Ninguno |
| Contratos máximos | 5 (micro 10:1) | 3 mini / 30 micro |
| Consistencia | **50%** | **30%** |
| Días mínimos | **2** | **4** |
| Inactividad | 7 días calendario | 7 días |
| **Cuenta fondeada (sim)** | | |
| MLL | US$2.000, **tiempo real (intradía)** | US$2.000, **EOD trailing**, lock en US$100 |
| Consistencia | Ninguna | Ninguna |
| Pagos | Diarios; mínimo US$500; 90%; buffer US$2.100 | Diarios; mínimo US$500; 90/10; buffer US$2.100 |
| Noticias T1 | No permitidas en sim-funded | No permitidas en sim-funded |

La página de comparación de MFFU muestra "Days to pass: 2 days" y la tabla de día de revisión es del 2026-05-18. Las capturas del usuario no muestran el plan Rapid EOD: confirmar que sigue a la venta.

## Regla de consistencia (Help Center, artículo 11994562)
- Es 30% en Rapid EOD y 50% en Rapid intradía y Pro.
- Significa que el beneficio de ningún día debe superar ese porcentaje del beneficio total de la evaluación.
- **Excederla no viola la cuenta:** hay que operar más días hasta cumplirla.
- Simulador: la evaluación se pasa cuando el beneficio total ≥ objetivo, los días mínimos se cumplen y (mejor día / beneficio total) ≤ umbral; si no, sigue.

## Noticias (Help Center, artículo 8230009, 2026-08-31)
- **T1 para todos:** reuniones del FOMC, actas del FOMC, informe de empleo y CPI. EIA solo para operadores de energía; reportes agrícolas para agrícolas.
- **Restringidas en T1:** Rapid sim-funded y Pro sim-funded. **Permitidas en T1:** todas las evaluaciones y los planes Builder.
- Regla en cuentas restringidas: ninguna posición ni orden (incluidas límite) abierta desde 2 minutos antes hasta 2 minutos después del dato. Ejemplo oficial: noticia a las 8:30 → cerrar a las 8:28:00 o antes y reabrir desde las 8:32:00.
- Prohibido en todo caso: estrategias de straddle o strangle sobre la noticia y disfrazar operaciones de noticia.
- Impacto: los T1 usuales (CPI y empleo a las 8:30 ET; FOMC a las 14:00 ET) quedan fuera de la ventana 09:30–11:30 ET. Verificar contra el calendario 2026 del artículo.
- **Ambigüedad:** el artículo dice que los "protocolos estándar" (2 minutos sin posiciones ni órdenes) aplican a todas las publicaciones, y luego dice que se puede operar otras publicaciones si encajan con la estrategia. Hay publicaciones no T1 a las 10:00 ET dentro de la ventana. Pedir aclaración a soporte.
- El simulador incluye una opción de **blackout de noticias** (apagada en evaluación; encendida para sim-funded en ET10).

## Por confirmar (D-009 sigue abierta para estos puntos)
- [ ] ¿La violación del MLL se comprueba **solo al cierre** o también **intradía** contra el equity abierto? Hasta confirmarlo, el simulador usa el modo conservador (intradía sobre equity) y reporta también el modo EOD.
- [ ] Qué cuenta como **día de operación** para los días mínimos.
- [ ] **Plan objetivo:** Rapid (intradía) o Rapid EOD, y disponibilidad del segundo.
- [ ] Costo de un **reset** y condiciones de reinicio.
- [ ] Aclaración de la regla de 2 minutos para publicaciones que no son T1.
- [ ] Artículo "Permitted Times to Trade" (no revisado).

Al completar: estado → VERIFICADO COMPLETO, fecha, y entrada en `CHANGELOG.md`. Cualquier diferencia con los valores de trabajo se aplica a la configuración del simulador **antes de ET2**.
