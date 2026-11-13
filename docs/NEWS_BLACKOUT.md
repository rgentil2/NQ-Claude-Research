# Blackout de noticias (D-027)

Especificación del filtro de noticias que usa el ejecutor (EXECUTOR_SPEC 2.1.0) en todas las etapas que simulan o ejecutan operaciones (ET1 a ET9).

## Regla
Para cada evento del calendario, con hora `t` en hora de Nueva York (la zona de NinjaTrader):
- **Sin operación abierta ni entradas entre `t−5:00` y `t+5:00`.**
- Una posición abierta a las `t−5:00` se cierra a mercado a ese instante, al precio de cierre de la barra que termina en `t−5:00`, con los costos habituales. Motivo de salida: `NEWS_FLAT`.
- Una señal cuyo instante de entrada cae dentro del intervalo se descarta con motivo `NEWS`. Se permiten entradas con instante ≥ `t+5:00`.
- Las ventanas que se solapan se unen.
- Los eventos sin hora (todo el día, tentativos, feriados) no generan blackout.
- Aplica también en la evaluación, aunque MFFU permita noticias T1 allí: es decisión del usuario, más conservadora y consistente con la etapa fondeada.

Interpretación del asistente, por confirmar (D-027b): "no operarlas" se entiende como **estar fuera del mercado**, no solo no abrir entradas. Si el usuario prefiere solo bloquear entradas y dejar correr las posiciones abiertas, se cambia a `NEWS_BLOCK_ONLY` antes de ET1.

## Eventos incluidos
- Forex Factory, moneda **USD**, impacto **alto (rojo) y medio (naranja)**, **todos los tipos de evento** (crecimiento, inflación, empleo, banco central, bonos, vivienda, encuestas de consumidores y de negocios, discursos, varios).
- Más los eventos T1 de MFFU (empleo, CPI, FOMC y actas). Son un subconjunto del anterior, pero se contrastan como control.

## Efecto real
Los T1 son a las 8:30 y 14:00 ET, fuera de la ventana. Los eventos de impacto medio y alto de Forex Factory sí caen dentro de 09:30–11:30 ET (por ejemplo, publicaciones de las 10:00 y discursos), así que este filtro **sí cambia resultados**: recorta tiempo operable y fuerza salidas. Cuánto, se mide en cuanto exista el archivo (`NEWS_MANIFEST`).

## Archivo de datos
`NQC_NewsCalendar_USD_HM.csv`, UTF-8, una fila por evento, columnas: `event_id`, `date_et` (YYYY-MM-DD), `time_et` (HH:MM, 24 horas, hora de Nueva York), `currency`, `impact` (`high` o `medium`), `title`. Rango 2020-01-01 a 2026-12-31. Vive en `Documents/NinjaTrader 8/NQC_Data/` y **no se sube al repo** (dato de terceros); al repo van el manifiesto y el SHA256.

## Fuente de datos: plan en tres capas (D-028)
Hallazgo (2026-11-12): **no existe una vía gratuita, automática y oficial para bajar el histórico de Forex Factory.** Sí hay tres piezas que cubren el calendario completo:

| Capa | Periodo | Fuente | Costo | Nota |
|---|---|---|---|---|
| 1. Histórico | 2020-01-01 → 2025-04-07 | Dataset comunitario de Hugging Face (`Ehsanrs2/Forex_Factory_Calendar` y copias) | Gratis | Sus horas están en **Asia/Tehran**: hay que convertir a hora de Nueva York (Irán tuvo horario de verano hasta 2022). La conversión se valida con empleo y CPI a las 08:30 ET. Licencia MIT declarada, con aviso de respetar los términos de Forex Factory y uso no comercial. |
| 2. Hueco | 2025-04-08 → 2026-12-31 | Sin fuente gratuita verificada | Un actor de Apify de pago por evento (el de `pintostudio` cobra desde US$10 por 1.000 resultados; ~700 a 1.400 eventos USD alto y medio en este tramo → del orden de US$7 a US$15, estimación del asistente), o extracción propia del sitio (riesgo frente a sus términos; no se recomienda eludir bloqueos) | Debe pedirse con zona horaria `America/New_York`, USD, impacto alto y medio, rango personalizado. |
| 3. Adelante y en vivo | Desde hoy | Feed público semanal de Forex Factory (semana en curso, sin clave) | Gratis y automático | Una tarea programada de Windows con `curl.exe` guarda cada semana el archivo con fecha; crea nuestro archivo "point-in-time" y alimenta ET10. No lo pude comprobar desde el entorno del asistente. |

El actor de Apify también puede cubrir las tres capas por completo, pero a mayor costo. Ninguna capa es dato oficial de Forex Factory.

## Aceptación del archivo (`NEWS_MANIFEST`, antes de ET1 sobre datos reales)
1. SHA256 registrado y el motor lo verifica al cargar.
2. Conteos por año, por impacto y por hora; ninguna fila con zona horaria ambigua.
3. **Zona horaria:** el empleo y el CPI aparecen a las 08:30 y el FOMC a las 14:00 tanto en meses de horario de verano como de invierno, en todos los años (en particular en 2020–2022 por el cambio de horario de Irán).
4. **Control cruzado con el calendario T1 2026 de MFFU** (abajo): todas esas fechas y horas existen como alto impacto USD.
5. **Continuidad en las costuras** entre capas (2025-04-07/08 y la fecha de inicio del feed): conteo mensual de eventos comparable.
6. Porcentaje de eventos sin hora y días con eventos dentro de 09:30–11:30 (resumen).

### Calendario T1 2026 de MFFU (transcrito de la imagen oficial; horas ET)
Empleo 08:30: 01-09, 02-06, 03-06, 04-03, 05-08, 06-05, 07-02, 08-07, 09-04, 10-02, 11-06, 12-04.
CPI 08:30: 01-13, 02-11, 03-11, 04-10, 05-12, 06-10, 07-14, 08-12, 09-11, 10-14, 11-10, 12-10.
FOMC 14:00: 01-28, 03-18, 04-29, 06-17, 07-29, 09-16, 12-09.
Actas del FOMC 14:00: 02-18, 04-08, 05-20, 07-08, 08-19, 10-07, 11-18, 12-30.
Nota: la imagen no muestra reunión del FOMC en octubre, aunque sí las actas de esa reunión en noviembre; puede estar incompleta.

## Efecto en otras etapas
- **ET2:** en los datasets nulos e inyectados el calendario viaja con la sesión (al permutar sesiones completas, cada sesión conserva sus eventos), para que el filtro actúe igual que en datos reales.
- **ET9:** la estrategia nativa de NT8 debe cargar el mismo archivo y verificar el mismo hash.
- **ET10 (en vivo):** se consulta el feed semanal con los mismos filtros. La clasificación de impacto de Forex Factory puede cambiar con el tiempo; para el backtest el archivo queda congelado.
