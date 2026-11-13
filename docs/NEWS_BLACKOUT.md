# Blackout de noticias (D-027)

Especificación del filtro de noticias que usa el ejecutor (EXECUTOR_SPEC 2.1.0) en todas las etapas que simulan o ejecutan operaciones (ET1 a ET9).

## Regla (confirmada por la práctica anterior del usuario, 2026-10-05)
Para cada evento del calendario, con hora `t` en hora de Nueva York (la zona de NinjaTrader):
- **Sin operación abierta ni entradas entre `t−5:00` y `t+5:00`.**
- Una posición abierta a las `t−5:00` se cierra a mercado a ese instante, al precio de cierre de la barra que termina en `t−5:00`, con los costos habituales. Motivo de salida: `NEWS_FLAT`.
- Una señal cuyo instante de entrada cae dentro del intervalo se descarta con motivo `NEWS`. Se permiten entradas con instante ≥ `t+5:00`.
- **Las barras de NQ no se eliminan.** Los indicadores (ATR y los demás) se calculan con la serie completa; el calendario solo bloquea operaciones o cierra posiciones.
- Las ventanas que se solapan se unen.
- Los eventos sin hora (todo el día, tentativos, feriados) no generan blackout.
- Aplica también en la evaluación, aunque MFFU permita noticias T1 allí: es decisión del usuario, más conservadora y consistente con la etapa fondeada.

Es la misma lógica de `PDCSmallGapParity.cs`: USD, impacto alto y medio, cierre 5 minutos antes, sin entradas de T−5 a T+5, hora de Nueva York.

## Eventos incluidos
- Forex Factory, moneda **USD**, impacto **alto (rojo) y medio (naranja)**, todos los tipos de evento que tengan una hora programada.
- Más los eventos T1 de MFFU (empleo, CPI, FOMC y actas), que son un subconjunto y sirven de control cruzado.

## Efecto real
Los T1 son a las 8:30 y 14:00 ET, fuera de la ventana. Los eventos de impacto medio y alto de Forex Factory sí caen dentro de 09:30–11:30 ET (publicaciones de las 10:00, discursos), así que este filtro **cambia resultados**. Cuánto, lo mide el manifiesto (días afectados, minutos bloqueados, eventos que fuerzan salida).

## Fuente de datos: el método del 2026-10-05 (D-028)
En octubre el usuario construyó el calendario leyendo las páginas mensuales del calendario de Forex Factory (cookie de zona horaria GMT, 24 horas), filtrando USD con impacto alto y medio y hora válida, convirtiendo GMT a hora de Nueva York con horario de verano y quitando duplicados de días de meses adyacentes. **Resultado reportado por el usuario:** 2.856 eventos de 2020 a 2024 (1.513 de impacto alto y 1.343 medio), más 5 excluidos por no tener hora. El CSV no estaba en el repo (era un artefacto de Actions); el script sí: `tools/build_forexfactory_calendar.py` de `MES-Trading-Research`.

Aquí vive adaptado en `tools/news/` (versión NEWS-1.0.0): rango 2020–2026, caché del HTML descargado, parada si el sitio bloquea (sin eludir protecciones), corrección del reconocimiento del impacto "Medium" y `NEWS_MANIFEST.txt` con controles. Se ejecuta en el PC del usuario con Python (excepción acotada a D-002: es adquisición de datos, no flujo del embudo). Lee un sitio de terceros: la decisión y el riesgo frente a sus términos de uso son del usuario.

### Plan de respaldo si el sitio bloquea
- Histórico a abril de 2025: dataset comunitario de Hugging Face (sus horas están en Asia/Tehran y hay que convertirlas).
- Hueco de abril de 2025 en adelante: un actor de Apify de pago por evento (del orden de decenas de dólares como máximo).
- Hacia adelante y en vivo: el feed público semanal de Forex Factory, guardado cada semana con `curl.exe`.

## Archivo de datos
`usd_high_medium_2020_2026.csv`, UTF-8, saltos de línea LF. Columnas: `event_utc`, `event_et`, `date_et`, `time_et` (HH:MM:SS, hora de Nueva York), `currency`, `impact` (`High` o `Medium`), `event`, `blackout_start_et`, `blackout_end_et`, `source_url`. El motor lee `date_et`, `time_et` e `impact`. Vive en `Documents/NinjaTrader 8/NQC_Data/` y **no se sube al repo** (dato de terceros); al repo van el manifiesto y el SHA256.

## Aceptación del archivo (antes de ET1 sobre datos reales)
1. SHA256 registrado; el motor lo verifica al cargar.
2. **Reproducción de 2020–2024:** el conteo debe quedar cerca de 2.856 (aviso si la diferencia pasa de 2%; Forex Factory puede haber corregido listados).
3. **Zona horaria:** empleo y CPI a las 08:30 ET y la declaración del FOMC a las 14:00 ET en ≥98% de los casos, tanto en meses de horario de verano como de invierno.
4. **Control cruzado con el calendario T1 2026 de MFFU:** ninguna fecha pasada sin evento de alto impacto a la hora esperada.
5. **Continuidad:** ningún mes con menos de la mitad de la mediana mensual de eventos.
6. Efecto sobre la ventana (resumen del manifiesto) registrado en `runs/`.

### Calendario T1 2026 de MFFU (transcrito de la imagen oficial; horas ET)
Empleo 08:30: 01-09, 02-06, 03-06, 04-03, 05-08, 06-05, 07-02, 08-07, 09-04, 10-02, 11-06, 12-04.
CPI 08:30: 01-13, 02-11, 03-11, 04-10, 05-12, 06-10, 07-14, 08-12, 09-11, 10-14, 11-10, 12-10.
FOMC 14:00: 01-28, 03-18, 04-29, 06-17, 07-29, 09-16, 12-09.
Actas del FOMC 14:00: 02-18, 04-08, 05-20, 07-08, 08-19, 10-07, 11-18, 12-30.
Nota: la imagen no muestra reunión del FOMC en octubre, aunque sí las actas de esa reunión en noviembre; puede estar incompleta.

## Efecto en otras etapas
- **ET2:** en los datasets nulos e inyectados el calendario viaja con la sesión (al permutar sesiones completas, cada sesión conserva sus eventos), para que el filtro actúe igual que en datos reales. También permite comparar con y sin filtro.
- **ET9:** la estrategia nativa de NT8 debe cargar el mismo archivo y verificar el mismo hash.
- **ET10 (en vivo):** se consulta el feed semanal con los mismos filtros. La clasificación de impacto de Forex Factory puede cambiar con el tiempo; para el backtest el archivo queda congelado.
