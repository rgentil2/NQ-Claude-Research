# Modelo de trabajo — agentes por etapa

**Vigente desde 2026-11-14.** Es un cambio de *cómo se trabaja*, no del contrato: NQC 2.0.0 (Nivel A) no se toca. Motivo: en un solo chat se perdía contexto y se repetía trabajo.

## Principio
El contexto vive **en el repo**, no en el chat. Cada etapa tiene un agente con un archivo de traspaso propio; cualquier sesión nueva retoma leyendo archivos, no la conversación.

## Roles
| Rol | Quién | Hace | No hace |
|---|---|---|---|
| Orquestador | Chat principal | Decide qué etapa trabajar, invoca al agente, revisa su reporte, **es el único que edita el master**, avanza estados cuando hay log NT8 + hash | Escribir el código de una etapa |
| Agente de etapa | `.claude/agents/etN-*.md` (ET0–ET10) | Cumple el objetivo de su etapa, en sus rutas, y actualiza `docs/stages/ETN.md` | Tocar otras etapas, el master, el contrato o decisiones |
| Auditor de contrato | `auditor-contrato` | Revisa sin editar que la etapa cumple el contrato antes de cerrarla y antes de ET8 | Escribir código |
| Revisor C# | `revisor-cs` | Revisa cada `.cs` antes de entregarlo (no se puede compilar aquí) | Editar |
| Escriba | `escriba` | Actualiza RESEARCH_LOG, CHANGELOG, LEDGER y master con lo que el orquestador indique | Inventar estados |

Los subagentes no crean otros subagentes: la jerarquía es orquestador → agente de etapa. Dentro de una etapa, las tareas internas se parten como paquetes de trabajo anotados en su `docs/stages/ETN.md`.

## Ciclo de una etapa
1. El orquestador abre `docs/stages/ETN.md`, verifica que la etapa previa está APROBADA en el master §6 y define una tarea concreta.
2. Invoca al agente de la etapa con esa tarea.
3. El agente lee su archivo de traspaso y la sección del contrato (nada más), trabaja, y registra su entrada en la bitácora.
4. `revisor-cs` revisa el `.cs` nuevo. El usuario instala, compila y corre en NT8 y devuelve logs.
5. El agente ingiere logs, deja `runs/<fecha>_ETN_<id>/SUMMARY.txt` con hash.
6. `auditor-contrato` revisa. Si APRUEBA, el orquestador actualiza el master (estado, artefacto/hash) y pide al escriba el log y el changelog.

## Reglas
- **Traspasos por archivo**: lo que un agente necesita saber de otro está en `docs/stages/`, `runs/` o el contrato. No depende de memoria de conversación.
- **Una etapa no avanza sin evidencia ejecutada** (log NT8 + hash).
- **Rutas propias**: cada agente solo escribe en las suyas (ver su archivo de traspaso). `git diff` lo comprueba el auditor.
- **OOS**: solo el agente de ET8 lo toca, una vez por campaña, tras `PORTFOLIO_FROZEN.txt` y `OOS_EXPOSURE.md`.
- **Umbrales [B] y estructura**: ningún agente los cambia; se propone al orquestador y al usuario (contrato §3).
- **Anti-repetición**: antes de empezar trabajo nuevo, el agente revisa la bitácora de su etapa y los pendientes; no rehace lo ya registrado.
- **Cómo retomar** (master §10) sigue válido; se añade leer el `docs/stages/ETN.md` de la etapa en curso.

## Dónde está cada cosa
| Qué | Ruta |
|---|---|
| Definición de agentes | `.claude/agents/` |
| Memoria viva por etapa | `docs/stages/ET0.md` … `ET10.md` |
| Contrato, decisiones, plan | `docs/CONTRACT_NQC_2.0.0.md`, `docs/DECISIONS.md`, `docs/WORK_PLAN.md` |
