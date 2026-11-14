# ET1 C - referencia del simulador (2026-11-13)
17 pruebas sinteticas con resultado conocido (PASS, consistencia, dias minimos, MLL intradia exacto y +1 tick, trailing, lock +100, perfil EOD, capa protectora) y determinismo: todas OK.
Pendiente: portar a NinjaScript (NQC_ET1C_AccountSim.cs), compilar con mcs+stubs, y correr P2-P6 dentro de NT8. Nota: el guardia de DD (colchon US$400) aun no esta en protect(); se agrega en el port.
