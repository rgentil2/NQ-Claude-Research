# Blackout de noticias (D-027)

Especificación del filtro de noticias que usa el ejecutor (EXECUTOR_SPEC 2.2.0) en todas las etapas que simulan o ejecutan operaciones (ET1 a ET9).

## Regla (confirmada por la práctica anterior del usuario, 2026-10-05)
Para cada evento del calendario, con hora `t` en hora de Nueva York (la zona de NinjaTrader):
- **Sin operación abierta ni entradas entre `t−5:00` y `t+5:00`.**
- Una posición abierta cuando el cierre de una barra cae en ese intervalo se cierra con una orden a mercado enviada al cierre de esa barra (instante `t−5:00` o posterior) y ejecutada en la apertura de la siguiente, con los costos habituales. Motivo de salida: `NEWS_FLAT`.
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
Medido sobre las 1.751 sesiones válidas de ET0 (`runs/2026-11-12_NEWS_run1/RUN_NOTES.md`): toca el 45,5% de las sesiones y bloquea el 4,5% de los minutos de la ventana; hay 1.242 cierres forzados dentro de la ventana. Dominan los eventos de las 10:00 (829), 09:45 (216) y 10:30 (97).

## Fuente de datos (D-028)
El método del 2026-10-05, rehecho con `tools/news/` (NEWS-1.0.0): páginas mensuales del calendario de Forex Factory, filtro USD alto y medio con hora válida, conversión de GMT a hora de Nueva York, duplicados eliminados. **Resultado aprobado:** 3.954 eventos 2020–2026; los 2.856 de 2020–2024 coinciden exactamente con la corrida de octubre (1.513 alto, 1.343 medio). Lee un sitio de terceros: la decisión y el riesgo frente a sus términos de uso son del usuario. Plan de respaldo si el sitio bloquea: dataset comunitario de Hugging Face (horas en Asia/Tehran) más un actor de Apify de pago para el hueco, y el feed semanal de Forex Factory para ET10.

## Archivo de datos
`usd_high_medium_2020_2026.csv`, UTF-8, saltos de línea LF, SHA256 `93d837ac323c6f144786c507606b0b77f25c2fa33fba6d21bb941d2d4d9c377d`. Columnas: `event_utc`, `event_et`, `date_et`, `time_et` (HH:MM:SS, hora de Nueva York), `currency`, `impact` (`High` o `Medium`), `event`, `blackout_start_et`, `blackout_end_et`, `source_url`. El motor lee `date_et` y `time_et`. Vive en `Documents/NinjaTrader 8/NQC_Data/` (el código también acepta `NQC Data`) y **no se sube al repo** (dato de terceros).

## Aceptación del archivo — APROBADA 2026-11-12
1. SHA256 registrado; el motor lo verifica al cargar.
2. Reproducción de 2020–2024: exacta (2.856).
3. Zona horaria: empleo (83 de 83) a las 08:30 ET en verano e invierno; CPI y FOMC a su hora, salvo reuniones de emergencia de marzo de 2020.
4. Control cruzado con el calendario T1 2026 de MFFU: 38 de 39; el informe de empleo de febrero figura el 11 en Forex Factory.
5. Continuidad: ningún mes con menos de la mitad de la mediana.
6. Efecto sobre la ventana registrado en `runs/`.

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
