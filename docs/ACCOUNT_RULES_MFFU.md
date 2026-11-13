# Reglas de la cuenta MFFU 50K — estado de verificación

**Estado: PARCIALMENTE VERIFICADO en fuente primaria (2026-11-12).** Los parámetros principales se confirmaron en el Help Center oficial de MFFU; quedan puntos de mecánica por confirmar (abajo). El simulador de cuenta (ET1) lee estos valores desde configuración; **no se codifican fijos**.

## Verificado en el Help Center oficial

Fuentes: `help.myfundedfutures.com/en/articles/16158363` ("Rapid EOD 50k – A Comprehensive Look", fechado 2026-08-24) y `help.myfundedfutures.com/en/articles/8348565` ("End of Day (EOD) Drawdown Explained").

### Evaluación, Rapid EOD 50K
| Parámetro | Valor |
|---|---|
| Objetivo de beneficio | US$3.000 |
| Pérdida máxima (MLL) | US$2.000, EOD |
| Límite de pérdida diaria | Ninguno |
| Contratos máximos | 3 mini / 30 micro |
| Consistencia | 30% (solo en evaluación) |
| Días mínimos de operación | 4 |
| Noticias T1 | Permitidas en evaluación |
| Regla de inactividad | 7 días |
| Cuota de activación | Ninguna |
| Lock del trailing EOD | Saldo inicial + US$100 (artículo 8348565; confirmar que aplica a este plan) |

### Sim-funded, Rapid EOD 50K
| Parámetro | Valor |
|---|---|
| Balance inicial | US$0 |
| MLL | US$2.000, trailing EOD: sube solo con cada cierre de sesión en máximo nuevo, no se mueve intradía, nunca baja |
| Lock | Se fija cuando el MLL llega a US$100; desde ahí hay que mantener al menos US$100 en la cuenta |
| Límite de pérdida diaria | Ninguno |
| Contratos máximos | 3 mini / 30 micro |
| Noticias T1 | **No permitidas** |
| Cuentas fondeadas | Máx. 3 |
| Pagos | Buffer de US$2.100 para el primer pago; luego US$500 de beneficio neto por pago; mínimo US$500; diarios; 90/10 |

## Por confirmar (D-009 sigue abierta)

- [ ] ¿El límite se evalúa **solo al cierre** o también **intradía** contra el equity abierto? El artículo dice que el MLL no se mueve intradía, pero no dice cuándo se comprueba la violación. Define si el simulador usa DD cerrado o mark-to-market.
- [ ] Fórmula exacta de la **regla de consistencia del 30%** (qué se compara con qué).
- [ ] Qué cuenta como **día de operación** para los 4 días mínimos.
- [ ] Definición de **noticias T1**, lista de eventos y restricción exacta en sim-funded. La ventana 09:30–11:30 ET incluye publicaciones de las 10:00.
- [ ] Vigencia del plan Rapid EOD (descrito por fuentes secundarias como plan por tiempo limitado, cuota de US$157 y 365 días de acceso), costo de un reset y condiciones de reinicio.
- [ ] Lock del MLL en la evaluación de este plan específico (saldo inicial + US$100).

Al completar: estado → VERIFICADO, fecha, y entrada en `CHANGELOG.md`. Cualquier diferencia con los valores de trabajo se aplica a la configuración del simulador **antes de ET2**.
