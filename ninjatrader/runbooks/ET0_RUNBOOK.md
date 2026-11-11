# ET0 — Datos y entorno: instrucciones de ejecución

Script: `ninjatrader/src/NQC_ET0_DataManifest.cs` (v1.0.0, SHA256 `73e937b5fb38356954e43ef7374e93a5e4aea6883ac46dd29997ba12e82b0eed`).

## Antes de empezar
- NinjaTrader 8 con **zona horaria Eastern** (Tools → Options → General → Time zone).
- Política de empalme **Merge back adjusted** (en las opciones globales o en la serie de datos).
- Historial M1 de NQ descargado de 2020-01 a 2026-10.

## Pasos
1. Descomprime el ZIP y ejecuta `INSTALAR.cmd`. Debe decir `OK. Instalado en: ...`. Si no encuentra la carpeta, escribe la ruta de `NinjaTrader 8` en `RUTA_NT8.txt` y reintenta.
2. En NinjaTrader: **New → NinjaScript Editor** y presiona **F5**. Debe decir *Compile successful*. Si hay errores, cópialos tal cual.
3. **Strategy Analyzer** → estrategia `NQC_ET0_DataManifest`. Instrumento: NQ (el contrato que usas), **Minute 1**, tipo **Last**, **From 1/1/2020 To 10/31/2026**, plantilla de horario la habitual. No hay parámetros que cambiar. Ejecuta (Run).
4. Al terminar, la ventana **Output** muestra una línea `NQC_ET0_END status=... dir=...`.
5. Abre la carpeta `dir` (`Documents\NinjaTrader 8\NQC_Results\ET0_<fecha_hora>\`): `SUMMARY.txt`, `MONTHS.tsv`, `DAYS.tsv`.
6. **Repite la corrida una segunda vez** y comprueba que `DAYS_SHA256` y `MONTHS_SHA256` son idénticos (gate G6).
7. Envía la línea `NQC_ET0_END`, `SUMMARY.txt` y `MONTHS.tsv`. Copia a `runs/<fecha>_ET0_run1/` solo `SUMMARY.txt` y `MONTHS.tsv`.

## Cómo leer el resultado
- `status=PASS_PENDIENTE_REPRODUCIBILIDAD` significa que pasaron G0–G5; G6 se confirma con la segunda corrida.
- **G4 (zona horaria):** si falla, mira `MONTHS.tsv`. Si los meses de horario de verano (marzo a noviembre) tienen `tzHit` mucho menor que los de invierno, NinjaTrader está en una zona sin horario de verano (por ejemplo Bogotá) y hay que corregirla antes de seguir.
- `tz_NinjaTrader` puede salir `N/D` si NT8 no expone esa propiedad; en ese caso G4 es la comprobación válida.
- La política de empalme no se puede leer desde la estrategia: queda **declarada** en el resumen. Confirma tú que está en Merge back adjusted.
- Las sesiones `INCOMPLETA` y `SIN_VENTANA` quedan listadas en `DAYS.tsv` y se excluyen del resto del embudo.
