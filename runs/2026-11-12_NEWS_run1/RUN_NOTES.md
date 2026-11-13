# Calendario de noticias run1 — 2026-11-12 — APROBADO

**Archivo:** `usd_high_medium_2020_2026.csv`, SHA256 `93d837ac323c6f144786c507606b0b77f25c2fa33fba6d21bb941d2d4d9c377d`, 3.954 eventos (2.117 High, 1.837 Medium). Generado por `tools/news/` v NEWS-1.0.0 en el PC del usuario. El CSV no está en el repo (dato de terceros).

## Veredicto frente a la aceptación de `docs/NEWS_BLACKOUT.md`
1. **SHA256 registrado:** sí.
2. **Reproducción de 2020–2024: exacta.** 2.856 eventos, 1.513 High y 1.343 Medium, idéntico a la corrida del 2026-10-05 que reportó el usuario.
3. **Zona horaria: correcta.** El informe de empleo ("Non-Farm Employment Change" exacto) sale a las 08:30 ET en 83 de 83 casos, 48 en horario de verano y 35 en invierno. El CPI m/m y el CPI subyacente salen a las 08:30. La métrica `non_farm_at_0830` del manifiesto marca 50,92% porque **NEWS-1.0.0 cuenta también el "ADP Non-Farm Employment Change" (08:15 ET)**; no es un error de datos. Por corregir en la próxima versión del script.
4. **Control cruzado T1 2026 de MFFU:** 38 de 39 presentes. Falta el informe de empleo del **2026-02-06**: en Forex Factory figura el **2026-02-11 a las 08:30**, es decir, reprogramado. El calendario estático de MFFU lo muestra el 02-06. No se verificó la causa.
5. **Continuidad:** ningún mes con menos de la mitad de la mediana mensual. Los meses de noviembre y diciembre de 2026 son futuros y solo tienen eventos ya publicados (30 y 33).
6. **Efecto sobre la ventana:** abajo.

## Excepciones reales que no son errores
- Declaración del FOMC fuera de las 14:00: 2020-03-03 a las 10:00 y 2020-03-15 a las 17:00 (reuniones de emergencia).
- "CPI y/y" a las 07:30 ET el 2021-03-10 y el 2022-03-10: rareza de la fuente; ese mismo día el CPI m/m y el subyacente salen a las 08:30. Queda fuera de la ventana.
- 28 eventos en fines de semana (discursos, la reunión del 15 de marzo de 2020): sin efecto, no se opera.
- 17 eventos sin hora quedan fuera del calendario.

## Efecto del blackout sobre las sesiones válidas de ET0 (1.751 sesiones completas)
| Bloque | Sesiones | Con algún bloqueo | % de minutos de la ventana bloqueados |
|---|---|---|---|
| IS 2020–2022 | 767 | 46,2% | 4,56% |
| OOS1 2023–2024 | 513 | 48,3% | 5,00% |
| OOS2 2025–2026 | 471 | 41,4% | 3,91% |
| Total | 1.751 | 45,5% | 4,51% |

- Eventos que fuerzan cierre dentro de la ventana (`t−5:00` entre 09:30 y 11:30): 1.242.
- Sesiones con 0, 1, 2, 3 y 4 franjas bloqueadas: 954, 450, 259, 64 y 24.
- Horas dominantes dentro de la ventana: **10:00 (829 eventos)**, 09:45 (216), 10:30 (97), 11:00 (44) y 09:59 (40). Los más frecuentes: petróleo (106), ISM manufacturero y de servicios (84 cada uno), confianza del consumidor CB (83), sentimiento UoM, PMI flash, JOLTS y ventas pendientes de vivienda.
- Lectura: el blackout toca casi la mitad de las sesiones pero recorta solo ~4,5% de los minutos. La franja de las 09:55 a las 10:05 queda bloqueada en cerca de la mitad de los días, así que en esas sesiones el tramo 09:30–09:55 funciona casi como una ventana aparte.

## Notas
- La ccifra de 2026 es menor por los meses futuros incompletos; no afecta al IS ni al OOS1.
- Evento de impacto medio "Crude Oil Inventories" (EIA, miércoles 10:30) entra por el filtro de Forex Factory aunque MFFU lo trate como T1 solo para operadores de energía.
