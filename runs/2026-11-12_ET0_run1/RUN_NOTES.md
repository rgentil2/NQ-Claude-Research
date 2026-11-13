# ET0 run1 — 2026-11-12 — APROBADA

**Script:** `NQC_ET0_DataManifest.cs` v1.0.0, SHA256 `73e937b5fb38356954e43ef7374e93a5e4aea6883ac46dd29997ba12e82b0eed`.
**Contrato:** NQC 2.0.0. El `SUMMARY.txt` imprime `NQC-1.0.0` porque el script no cambió con la 2.0.0; la sección de ET0 es idéntica en ambas versiones.

## Veredicto
G0–G5 PASS en el resumen. **G6 (reproducibilidad):** dos corridas (21:38:10 y 21:38:40 en hora del PC) con `daysSha=9bd25f767636` idéntico y los mismos conteos. Los hashes completos de `DAYS.tsv` y `MONTHS.tsv` subidos coinciden con los del resumen. MONTHS se deriva de las mismas filas por día que DAYS, así que hash idéntico de DAYS implica MONTHS idéntico.

| Gate | Valor | Umbral |
|---|---|---|
| G3 completitud IS | 99,10% (767 de 774) | ≥ 99% |
| G4 zona horaria IS | 70,54% | ≥ 70% |

Ambos pasan con márgenes pequeños. No se cambia ningún umbral.

## Observaciones (no cambian el veredicto)
1. **Zona horaria correcta.** NinjaTrader reporta Eastern Standard Time. Separando meses de horario de verano y de invierno no hay desfase de una hora: IS 73,3% frente a 66,7%; OOS2 91,8% frente a 78,4%. Un desfase llevaría el pico a 08:31 en verano. G4 es un indicador débil porque la barra de mayor volumen no siempre es la de apertura.
2. **Sesiones incompletas del IS excluidas por regla (7):** 2020-03-09, 2020-03-12 y 2020-03-16 (faltan 13 a 14 barras; plausible por pausas de mercado, no verificado con fuente), 2020-06-03 (2 barras), 2021-08-24, 2022-08-03 y 2022-08-30 (1 barra cada una). **Limitación conocida:** la exclusión saca del IS los días más volátiles de marzo de 2020; en vivo esos días existen. Se mantiene la regla (Nivel A); se considerará en ET6/ET7.
3. **Posible hueco de datos:** 2020-10-23 (viernes) no tiene barras entre 09:16 y 11:30 y no corresponde a un festivo conocido. Queda excluido como SIN_VENTANA. Conviene revisarlo en el gráfico de NT8.
4. Los otros días SIN_VENTANA coinciden con festivos o cierres anticipados (Viernes Santo, Año Nuevo, Navidad, 2025-01-09, 2022-12-26).

## Archivos
`SUMMARY.txt`, `MONTHS.tsv`. `DAYS.tsv` (1.776 filas) se conserva fuera del repo; su hash está en el resumen.
