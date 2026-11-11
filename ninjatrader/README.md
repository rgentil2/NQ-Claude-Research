# NinjaScript — convenciones

- **Fuente única:** `ninjatrader/src/*.cs`. Todo ZIP de instalación se genera desde aquí y su SHA256 se registra en el `SUMMARY.txt` de la corrida. Los `.cs` usan saltos de línea LF; el hash es el del archivo tal como está en el repo.
- **Formato del ZIP de entrega:** `NQC_<Etapa>_<Nombre>.cs` + `INSTALAR.cmd` (copia el `.cs` a `NinjaTrader 8\bin\Custom\Strategies` y verifica su hash) + `RUTA_NT8.txt` (ruta manual de la carpeta de NinjaTrader si no se detecta sola) + `HASH_SHA256.txt` + `LEEME.txt`. Plantillas en `ninjatrader/installer/`.
- Prefijo de nombres: `NQC_ET<n>_<Nombre>` (ej.: `NQC_ET0_DataManifest`, `NQC_ET1_CentralExecutor`).
- Ningún script envía órdenes reales. Las órdenes nativas solo se usan en Strategy Analyzer (ET1 referencia, ET8).
- Ningún script lee fechas OOS salvo ET0 (integridad) y ET9 (con hash de congelado verificado). Si las fechas configuradas tocan OOS sin congelado, el script se detiene.
- Cada script escribe sus salidas en `Documents/NinjaTrader 8/NQC_Results/<corrida>/` y el usuario copia solo `SUMMARY.txt` (y los TSV pequeños que se pidan) a `runs/`.
- Las condiciones del generador usan ATR, ticks o puntos (R5).
