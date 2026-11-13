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

Interpretación del asistente, por confirmar: "no operarlas" se entiende como **estar fuera del mercado**, no solo no abrir entradas. Si el usuario prefiere solo bloquear entradas y dejar correr las posiciones abiertas, se cambia a `NEWS_BLOCK_ONLY` antes de ET1.

## Eventos incluidos
- Forex Factory, moneda **USD**, impacto **alto (rojo) y medio (naranja)**, **todos los tipos de evento** (crecimiento, inflación, empleo, banco central, bonos, vivienda, encuestas de consumidores y de negocios, discursos, varios).
- Más los eventos T1 de MFFU (empleo, CPI, FOMC y actas). Son un subconjunto del anterior, pero se contrastan como control.

## Efecto real
Los T1 son a las 8:30 y 14:00 ET, fuera de la ventana. Los eventos de impacto medio y alto de Forex Factory sí caen dentro de 09:30–11:30 ET (por ejemplo, publicaciones de las 10:00 y discursos), así que este filtro **sí cambia resultados**: recorta tiempo operable y fuerza salidas. Cuánto, se mide en cuanto exista el archivo (`NEWS_MANIFEST`).

## Archivo de datos
`NQC_NewsCalendar_USD_HM.csv`, UTF-8, una fila por evento, columnas: `event_id`, `date_et` (YYYY-MM-DD), `time_et` (HH:MM, 24 horas, hora de Nueva York), `currency`, `impact` (`high` o `medium`), `title`. Rango 2020-01-01 a 2026-12-31. Vive en `Documents/NinjaTrader 8/NQC_Data/` y **no se sube al repo** (dato de terceros); al repo van el manifiesto y el SHA256.

### Fuentes posibles (elige el usuario; revisar términos de uso)
- Un actor de Apify que lee Forex Factory y exporta CSV con zona horaria `America/New_York`, USD, impacto alto y medio, de 2020-01-01 a 2026-12-31.
- El dataset comunitario "Forex Factory Calendar (2007–2025)" de Hugging Face, que llega hasta 2025-04-07 y habría que completar y reexpresar en hora de Nueva York.
- Forex Factory no ofrece exportación oficial de histórico; ambas vías son datos de terceros obtenidos de su sitio.

## Aceptación del archivo (`NEWS_MANIFEST`, antes de ET1 sobre datos reales)
1. SHA256 registrado y el motor lo verifica al cargar.
2. Conteos por año, por impacto y por hora; ninguna fila con zona horaria ambigua.
3. **Zona horaria:** el empleo y el CPI aparecen a las 08:30 y el FOMC a las 14:00 tanto en meses de horario de verano como de invierno.
4. **Control cruzado con el calendario T1 2026 de MFFU** (abajo): todas esas fechas y horas existen como alto impacto USD.
5. Porcentaje de eventos sin hora y días con eventos dentro de 09:30–11:30 (resumen).

### Calendario T1 2026 de MFFU (transcrito de la imagen oficial; horas ET)
Empleo 08:30: 01-09, 02-06, 03-06, 04-03, 05-08, 06-05, 07-02, 08-07, 09-04, 10-02, 11-06, 12-04.
CPI 08:30: 01-13, 02-11, 03-11, 04-10, 05-12, 06-10, 07-14, 08-12, 09-11, 10-14, 11-10, 12-10.
FOMC 14:00: 01-28, 03-18, 04-29, 06-17, 07-29, 09-16, 12-09.
Actas del FOMC 14:00: 02-18, 04-08, 05-20, 07-08, 08-19, 10-07, 11-18, 12-30.
Nota: la imagen no muestra reunión del FOMC en octubre, aunque sí las actas de esa reunión en noviembre; puede estar incompleta.

## Efecto en otras etapas
- **ET2:** en los datasets nulos e inyectados el calendario viaja con la sesión (al permutar sesiones completas, cada sesión conserva sus eventos), para que el filtro actúe igual que en datos reales.
- **ET9:** la estrategia nativa de NT8 debe cargar el mismo archivo y verificar el mismo hash.
- **ET10 (en vivo):** se consulta el calendario semanal con los mismos filtros. La clasificación de impacto de Forex Factory puede cambiar con el tiempo; para el backtest el archivo queda congelado.
