@echo off
setlocal EnableExtensions EnableDelayedExpansion
set "HERE=%~dp0"
set "CS=NQC_ET0_DataManifest.cs"
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

rem Verificar hash del .cs contra HASH_SHA256.txt
set "EXPECTED="
set "ACTUAL="
for /f "tokens=1" %%H in ('type "%HERE%HASH_SHA256.txt"') do if not defined EXPECTED set "EXPECTED=%%H"
for /f "skip=1 tokens=*" %%H in ('certutil -hashfile "%HERE%%CS%" SHA256') do if not defined ACTUAL set "ACTUAL=%%H"
set "ACTUAL=!ACTUAL: =!"
if /i not "!ACTUAL!"=="!EXPECTED!" goto :BADHASH

if exist "%DEST%\%CS%" copy /y "%DEST%\%CS%" "%DEST%\%CS%.bak" >nul
copy /y "%HERE%%CS%" "%DEST%\%CS%" >nul
if errorlevel 1 goto :COPYFAIL

echo.
echo OK. Instalado en:
echo   %DEST%\%CS%
echo   SHA256: !ACTUAL!
echo.
echo SIGUIENTE PASO: en NinjaTrader abre el NinjaScript Editor y presiona F5 para compilar.
echo Debe decir "Compile successful". Si hay errores, copialos y envialos.
echo.
pause
exit /b 0

:NOFOLDER
echo.
echo No encontre la carpeta Strategies de NinjaTrader 8.
if defined NT8 echo Ruta probada: !NT8!
echo Abre RUTA_NT8.txt, escribe en una linea la ruta de "NinjaTrader 8" (la carpeta que contiene "bin\Custom")
echo y vuelve a ejecutar este instalador.
echo.
pause
exit /b 1

:BADHASH
echo.
echo ERROR: el hash del .cs no coincide con HASH_SHA256.txt. No se instalo nada.
echo   esperado: !EXPECTED!
echo   actual  : !ACTUAL!
echo.
pause
exit /b 2

:COPYFAIL
echo.
echo ERROR: no se pudo copiar el archivo. Cierra el NinjaScript Editor y reintenta.
echo.
pause
exit /b 3
