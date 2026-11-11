# NinjaScript — convenciones

- **Fuente única:** `ninjatrader/src/*.cs`. Todo ZIP de instalación se genera desde aquí y su SHA256 se registra en el `SUMMARY.txt` de la corrida.
- Prefijo de nombres: `NQC_ET<n>_<Nombre>` (ej.: `NQC_ET0_DataManifest`, `NQC_ET1_CentralExecutor`).
- Ningún script envía órdenes reales. Las órdenes nativas solo se usan en Strategy Analyzer (ET8).
- Ningún script lee fechas OOS salvo ET0 (integridad) y ET9 (con hash de congelado verificado). Si las fechas configuradas tocan OOS sin congelado, el script se detiene.
- Cada script escribe sus salidas en `Documents/NinjaTrader 8/NQC_Results/<corrida>/` y el usuario copia solo `SUMMARY.txt` (y los TSV pequeños que se pidan) a `runs/`.
