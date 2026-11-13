@echo off
setlocal
cd /d "%~dp0"
where py >nul 2>nul
if errorlevel 1 goto :NOPY
py -3 -m pip install --quiet requests beautifulsoup4 tzdata
if errorlevel 1 goto :PIPFAIL
py -3 build_forexfactory_calendar.py --start-year 2020 --end-year 2026 --out-dir NQC_News_Out
echo.
echo Listo. Revisa NQC_News_Out\NEWS_MANIFEST.txt y envia ese archivo y el CSV de la misma carpeta.
pause
exit /b 0

:NOPY
echo No encontre Python. Instala Python 3.11 o superior desde python.org y vuelve a ejecutar.
pause
exit /b 1

:PIPFAIL
echo No se pudieron instalar las dependencias. Revisa la conexion y vuelve a ejecutar.
pause
exit /b 2
