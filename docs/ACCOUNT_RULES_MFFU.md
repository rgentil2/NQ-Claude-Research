# Reglas de la cuenta MFFU 50K — estado de verificación y perfiles del simulador

**Estado (2026-11-12): D-009 CERRADA para ET1.** Los parámetros principales están verificados en el Help Center oficial y en las pantallas del usuario; las mecánicas que el sitio no aclara las fijó el usuario (D-025 a D-027). Quedan puntos residuales al final. El simulador de cuenta lee estos valores desde configuración con **dos perfiles**; no se codifican fijos.

## Plan elegido (D-024): Rapid (intradía) 50K
Por delegación del usuario ("el plan que consideres"). Motivos: es el que aparece a la venta en su checkout (US$209, pago único); su evaluación es más holgada (consistencia 50% y 2 días mínimos frente a 30% y 4 de Rapid EOD), lo que sube la probabilidad de pasar; Rapid EOD se describe como plan por tiempo limitado y no aparece en las capturas. **Costo:** la cuenta fondeada de este plan usa MLL en tiempo real (más estricto que EOD); eso se modela en ET10 y no afecta la evaluación. El perfil Rapid EOD queda disponible como secundario. Cambiar de plan después de ET2 exige versión mayor porque cambia P_pass.

## Los dos planes

| Parámetro | Rapid (intradía) 50K | Rapid EOD 50K |
|---|---|---|
| Fuente | Capturas del usuario de `myfundedfutures.com/plans/rapid` y del checkout (2026-11-12) | Help Center artículo 16158363 (2026-08-24) |
| Precio | US$209, pago único, no se renueva | US$157 según un tercero, sin verificar |
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

## Regla de consistencia (Help Center, artículo 11994562)
Ningún día debe superar el porcentaje (50% o 30%) del beneficio total de la evaluación. Excederla no viola la cuenta: hay que operar más días hasta cumplirla. Simulador: la evaluación se pasa cuando beneficio total ≥ objetivo, se cumplen los días mínimos y (mejor día / beneficio total) ≤ umbral; si no, sigue.

## Noticias (Help Center, artículo 8230009, 2026-08-31, y calendario T1 2026)
- T1 para todos: reuniones y actas del FOMC, informe de empleo y CPI. EIA solo para energía; reportes agrícolas para agrícolas.
- Restringidas en T1: Rapid sim-funded y Pro sim-funded. Permitidas: todas las evaluaciones y Builder.
- Regla en cuentas restringidas: sin posiciones ni órdenes desde 2 minutos antes hasta 2 minutos después del dato.
- **Calendario T1 2026 (imagen oficial):** todos los eventos son a las 8:30 AM (empleo, CPI) o a las 2:00 PM (FOMC y actas). Ninguno cae en la ventana 09:30–11:30 ET.
- **Decisión D-027 (actualizada):** el usuario decidió un blackout de ±5 minutos sobre los eventos **USD de Forex Factory con impacto alto y medio, de todos los tipos**, más los T1 de MFFU. Eso **sí afecta la ventana** (publicaciones de las 10:00, discursos, etc.). Regla, archivo de datos y aceptación: [`NEWS_BLACKOUT.md`](NEWS_BLACKOUT.md).
- La etiqueta del calendario T1 de MFFU dice EST. Se interpreta como hora local de Nueva York. El archivo de Forex Factory se pide ya en hora de Nueva York, y su aceptación comprueba que empleo y CPI aparecen a las 08:30 todo el año.
- El calendario T1 2026 no muestra una reunión del FOMC en octubre aunque sí las actas de esa reunión en noviembre: puede estar incompleto.

## Decisiones del usuario (2026-11-12)
- **D-024:** plan Rapid (intradía), delegado al asistente.
- **D-025:** el límite de pérdida máxima se comprueba **durante la sesión**, contra el equity abierto (mark-to-market M1). El nivel del MLL sube solo con los cierres de sesión. Se reporta también el modo "solo al cierre" como diagnóstico.
- **D-026:** un **día de operación** es un día con al menos una operación tomada.
- **D-027:** blackout de noticias de ±5 minutos (arriba).
- **Costo de reset: US$0** (dato del usuario; no verificado por el asistente en el Help Center).

## Perfil del simulador: RAPID_INTRADAY_50K (por defecto)
- Saldo inicial relativo 0; objetivo +US$3.000; MLL US$2.000; el nivel empieza en –US$2.000 y sube con el mayor saldo de cierre; lock cuando el nivel llega a +US$100 (artículo 8348565, por confirmar para este plan).
- Violación: equity abierto (M2M) ≤ nivel en cualquier momento de la sesión (D-025).
- Consistencia 50%; días mínimos 2 (D-026); sin límite diario; contratos 1 NQ (máximo del plan 5).
- Blackout de noticias ±5 min (D-027), con el calendario de `NEWS_BLACKOUT.md`. Inactividad de 7 días: no aplica en backtest.
- **Cuenta fondeada (ET10):** MLL en tiempo real, sin consistencia, noticias T1 restringidas.

## Perfil secundario: RAPID_EOD_50K
Consistencia 30%, días mínimos 4, contratos máximos 3 mini; fondeada con EOD trailing y lock en US$100. Mismas reglas de D-025 a D-027.

## Residuales (no bloquean ET1)
- [ ] Archivo del calendario de noticias (D-028): bloquea las corridas de ET1 sobre datos reales.
- [ ] Aclaración de soporte sobre la regla de 2 minutos para publicaciones no T1 (hoy cubierta por el blackout de ±5 minutos).
- [ ] Costo de reset US$0: guardar la fuente.
- [ ] Lock del MLL en la evaluación de Rapid intradía (saldo inicial + US$100).
- [ ] Artículo "Permitted Times to Trade" (no revisado).
