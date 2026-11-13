@echo off
setlocal
cd /d "%~dp0"
set "PY="
py -3 -c "import sys" >nul 2>nul
if not errorlevel 1 set "PY=py -3"
if not defined PY python -c "import sys" >nul 2>nul
if not defined PY if not errorlevel 1 set "PY=python"
if not defined PY goto :NOPY
echo Usando: %PY%
%PY% -m pip install --quiet requests beautifulsoup4 tzdata
if errorlevel 1 goto :PIPFAIL
%PY% build_forexfactory_calendar.py --start-year 2020 --end-year 2026 --out-dir NQC_News_Out
echo.
echo Listo. Revisa NQC_News_Out\NEWS_MANIFEST.txt y envia ese archivo y el CSV de la misma carpeta.
pause
exit /b 0

:NOPY
echo No pude ejecutar Python desde esta ventana.
echo Si esta instalado, prueba una de estas dos cosas:
echo 1. Reinstalarlo marcando la casilla Add python.exe to PATH.
echo 2. Desactivar los alias de la Tienda de Windows: Configuracion, Aplicaciones, Configuracion avanzada de aplicaciones, Alias de ejecucion de aplicaciones, y apagar los de python.
echo Luego vuelve a ejecutar este archivo.
pause
exit /b 1

:PIPFAIL
echo No se pudieron instalar las dependencias. Revisa la conexion y vuelve a ejecutar.
pause
exit /b 2
