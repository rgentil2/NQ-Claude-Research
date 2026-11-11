# Reglas de la cuenta MFFU 50K — estado de verificación

**Estado: NO VERIFICADO en fuente primaria.** Valores de trabajo tomados de fuentes secundarias consultadas el 2026-11-11. El simulador de cuenta (ET1) lee estos valores desde configuración; **no se codifican fijos**.

## Valores de trabajo (plan Rapid EOD 50K)

| Parámetro | Valor de trabajo | Acuerdo entre fuentes |
|---|---|---|
| Objetivo de beneficio | US$3.000 | Sí |
| Pérdida máxima (MLL) | US$2.000, trailing al cierre (EOD) en evaluación y sim-funded | Sí en lo principal |
| Lock del MLL | Se fija en saldo inicial + US$100 al alcanzarlo (según una fuente, en sim-funded) | Verificar |
| Límite de pérdida diaria | Ninguno | Sí |
| Días mínimos de evaluación | 4 | Discrepa (otras fuentes, para otros planes: 2) |
| Regla de consistencia en evaluación | 30% | Discrepa (otras fuentes: 50% u otra cifra según plan) |
| Contratos máximos | 3 mini / 30 micro | Discrepa (otras: 5 mini) — irrelevante con 1 NQ |
| Disponibilidad del plan | Descrito como plan por tiempo limitado | Verificar que siga disponible |

## Fuentes secundarias consultadas (2026-11-11)

- Finance Magnates, 2026-08-06: Rapid EOD con objetivo US$3.000, MLL US$2.000, sin límite diario, trailing EOD en evaluación y sim-funded; 30% de consistencia y 4 días mínimos en su tabla comparativa.
- PropTradingVibes, guía Rapid EOD (actualizada 2026-08-06): plan por tiempo limitado, 30% de consistencia, 4 días mínimos, lock del piso en US$100, 3 mini / 30 micro.
- Otras guías de terceros sobre los planes Rapid/Pro/Builder muestran cifras distintas de consistencia y días mínimos: no usar sin verificar.

## Checklist de verificación (usuario)

- [ ] URL del Help Center oficial y fecha de consulta.
- [ ] Definición exacta del trailing EOD: ¿se recalcula solo al cierre? ¿la violación se evalúa solo al cierre o también intradía?
- [ ] Definición exacta de la regla de consistencia (¿mejor día ≤ X% del beneficio total?) y cómo se cuenta un día.
- [ ] Días mínimos y qué cuenta como día operado.
- [ ] Regla de inactividad, si existe.
- [ ] Reglas de noticias y horarios, si existen.
- [ ] Cambios entre evaluación y sim-funded.

Al completar: estado → VERIFICADO, fecha, y entrada en `CHANGELOG.md`. Cualquier diferencia con los valores de trabajo se aplica a la configuración del simulador **antes de ET2**.
