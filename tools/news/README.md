# Herramienta del calendario de noticias

Genera `usd_high_medium_2020_2026.csv` (USD, impacto alto y medio, hora de Nueva York) y `NEWS_MANIFEST.txt` a partir de las páginas mensuales del calendario de Forex Factory. Es el método que se usó el 2026-10-05 en `MES-Trading-Research` (2.856 eventos de 2020 a 2024, según el usuario), con dos cambios: rango 2020–2026 y un manifiesto con controles.

**Excepción acotada a D-002:** esta herramienta usa Python porque es adquisición de datos, no parte del flujo del embudo en NinjaTrader.

## Cómo ejecutarla (Windows)
1. Instala Python 3.11 o superior desde python.org si no lo tienes.
2. Descarga la carpeta `tools/news/` y haz doble clic en `RUN_NEWS_BUILD.cmd`. Tarda unos minutos (84 meses con una pausa de 2,1 s).
3. Al terminar, en `NQC_News_Out/` quedan el CSV, `NEWS_MANIFEST.txt`, el JSON de eventos sin hora y el HTML descargado (`raw_html/`).
4. Envía el CSV y `NEWS_MANIFEST.txt`. El asistente los valida (`docs/NEWS_BLACKOUT.md`).

## Si el sitio bloquea
El script se detiene con `DETENIDO: HTTP 403` y conserva los meses ya bajados; no intenta eludir protecciones. En ese caso se usa el plan de respaldo de `docs/NEWS_BLACKOUT.md`.

## Qué no se sube al repo
El CSV y el HTML son datos de terceros: solo se versionan el manifiesto y el SHA256.

## Aviso
Lee un sitio de terceros; revisa sus términos de uso. La decisión de usarlo es del usuario.
