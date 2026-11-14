@echo off
setlocal EnableExtensions EnableDelayedExpansion
set "HERE=%~dp0"
set "NT8="

rem 1) Ruta manual (RUTA_NT8.txt, ultima linea que no sea comentario)
if exist "%HERE%RUTA_NT8.txt" (
  for /f "usebackq eol=# tokens=* delims=" %%A in ("%HERE%RUTA_NT8.txt") do (
    if not "%%A"=="" set "NT8=%%A"
  )
)

rem 2) Carpeta Documentos real segun Windows (cubre OneDrive e idioma)
set "DOCS="
for /f "tokens=2,*" %%A in ('reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders" /v Personal 2^>nul') do set "DOCS=%%B"
if defined DOCS call set "DOCS=%DOCS%"
if not defined NT8 if defined DOCS if exist "%DOCS%\NinjaTrader 8\bin\Custom\Strategies" set "NT8=%DOCS%\NinjaTrader 8"

rem 3) Rutas habituales en ingles y espanol
if not defined NT8 if exist "%USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\Strategies" set "NT8=%USERPROFILE%\Documents\NinjaTrader 8"
if not defined NT8 if exist "%USERPROFILE%\Documentos\NinjaTrader 8\bin\Custom\Strategies" set "NT8=%USERPROFILE%\Documentos\NinjaTrader 8"
if not defined NT8 if defined OneDrive if exist "%OneDrive%\Documents\NinjaTrader 8\bin\Custom\Strategies" set "NT8=%OneDrive%\Documents\NinjaTrader 8"
if not defined NT8 if defined OneDrive if exist "%OneDrive%\Documentos\NinjaTrader 8\bin\Custom\Strategies" set "NT8=%OneDrive%\Documentos\NinjaTrader 8"

if not defined NT8 goto :NOFOLDER

set "DEST=%NT8%\bin\Custom\Strategies"
if not exist "%DEST%" if exist "%NT8%\Strategies" set "DEST=%NT8%\Strategies"
if not exist "%DEST%" goto :NOFOLDER

rem Paso 1: verificar TODOS los hashes antes de copiar nada.
rem HASH_SHA256.txt: una linea por archivo, "<sha256>  <archivo.cs>"
set "BAD=0"
for /f "usebackq tokens=1,2" %%H in ("%HERE%HASH_SHA256.txt") do call :CHECK "%%I" %%H
if not "%BAD%"=="0" goto :BADHASH

rem Paso 2: copiar (con copia .bak de lo que existia).
set "COUNT=0"
for /f "usebackq tokens=1,2" %%H in ("%HERE%HASH_SHA256.txt") do call :INSTALL "%%I"
if not "%BAD%"=="0" goto :COPYFAIL

echo.
echo OK. Instalados %COUNT% archivos en:
echo   %DEST%
echo.
echo SIGUIENTE PASO: en NinjaTrader abre el NinjaScript Editor y presiona F5 para compilar.
echo Debe decir "Compile successful". Si hay errores, copialos y envialos tal cual.
echo.
pause
exit /b 0

:CHECK
set "ACT="
for /f "skip=1 tokens=*" %%X in ('certutil -hashfile "%HERE%%~1" SHA256') do if not defined ACT set "ACT=%%X"
set "ACT=%ACT: =%"
if /i not "%ACT%"=="%2" (
  echo ERROR de hash en %~1
  echo   esperado: %2
  echo   actual  : %ACT%
  set "BAD=1"
)
exit /b 0

:INSTALL
if exist "%DEST%\%~1" copy /y "%DEST%\%~1" "%DEST%\%~1.bak" >nul
copy /y "%HERE%%~1" "%DEST%\%~1" >nul
if errorlevel 1 (set "BAD=1") else (set /a COUNT+=1)
exit /b 0

:NOFOLDER
echo.
echo No encontre la carpeta Strategies de NinjaTrader 8.
if defined NT8 echo Ruta probada: %NT8%
echo Abre RUTA_NT8.txt, escribe en una linea la ruta de "NinjaTrader 8" (la carpeta que contiene "bin\Custom")
echo y vuelve a ejecutar este instalador.
echo.
pause
exit /b 1

:BADHASH
echo.
echo No se instalo nada: algun hash no coincide con HASH_SHA256.txt.
echo.
pause
exit /b 2

:COPYFAIL
echo.
echo ERROR: no se pudo copiar algun archivo. Cierra el NinjaScript Editor y reintenta.
echo.
pause
exit /b 3
