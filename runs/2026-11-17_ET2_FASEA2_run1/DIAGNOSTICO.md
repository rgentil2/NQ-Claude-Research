# ET2 Fase A2 — diagnóstico (2026-11-17)

Lote `ET2_FASE_A2` (35 campañas REDUCED: 20 nulos N1/N2 + 15 inyectados a +20/+60/+100), pack 6 (D-039 + D-040), 8 hilos, 1.177 s. Informe mecánico: `ET2_CALIBRATION_REPORT.txt` (`report_sha256 fe25efd0…`, veredicto GATE NO CUMPLIDO). Archivos: `FASEA2_LIGERO.zip` del usuario.

## Resultado
**35 de 35 campañas se cierran en ET5 (PBO > 0,40); ninguna llega a ET6.** (Fase A: 17 de 30.) FP 0/20 y potencia 0/5, 0/5, 0/5 por nivel de edge (+20/+60/+100) son consecuencia de que el embudo se corta antes de ET6, no de una medición de detección.

## Hallazgos (calculados de los archivos; `analisis_pbo.py`, `analisis_pbo.tsv`)
1. **El PBO calculado sobre el pool de ganadores está sesgado por construcción.** Los 200 candidatos de cada campaña se eligieron por aptitud sobre TODA la muestra (36 meses). Condicionado a un total similar, el resultado de una mitad correlaciona negativamente con el de la otra: correlación media entre candidatos de la mitad A y la mitad B = **−0,28** (rango −0,58 a +0,06). Con ese sesgo, el «mejor en entrenamiento» tiende a quedar bajo la mediana en prueba, y el PBO sale > 0,5. PBO de las 35 campañas: mín 0,43, **mediana 0,78**, máx 0,99. Un PBO recalculado aparte con la matriz `q_net` reproduce el de ET5 (p. ej. 0,891 frente a 0,889; 0,835 frente a 0,836): la implementación de ET5 es correcta; el problema es el conjunto sobre el que se aplica. El PBO de Bailey et al. se define sobre el conjunto de TODAS las configuraciones probadas, no sobre un top-K seleccionado con los mismos datos.
2. **Los datasets «nulos» no son nulos para efectos intradía.** N1 (permutar sesiones) y N2 (invertir el signo de cada sesión) conservan la estructura dentro de la sesión. Los candidatos ganan, sobre estos «nulos», neto total mediana ≈ US$73.000 en 3 años por contrato (rango por campaña US$27.000–65.000 en la campaña B00), 11–12 de 12 trimestres positivos. Esto contrasta con un mundo sintético de ruido puro (mejor t ≈ 3). Interpretación (hipótesis, no demostrada): las sesiones reales del IS contienen estructura intradía que la búsqueda explota; o hay un artefacto del motor sobre ese tipo de datos. Para saberlo hace falta un nulo verdadero (p. ej. barajar los retornos de minuto dentro de cada sesión).
3. **Consecuencia sobre el control positivo:** inyectar +20/+60/+100 sobre N1 (que ya trae US$80 por operación de «edge» propio a los ganadores) no puede detectarse; la curva de potencia no es interpretable hasta tener un nulo verdadero.
4. Efecto de D-039: la aptitud v2 y el mínimo de 250 operaciones llevaron a TODOS los PBO por encima de 0,40 (mín 0,43): candidatos más homogéneos acentúan el sesgo del punto 1.

## Aviso de procedimiento
Estos datasets se derivan de sesiones reales del IS; el resultado económico de las estrategias sobre ellos ha sido visible. Se registra aquí como exposición no buscada, coherente con el límite de interpretación ya documentado en la Fase A.

## Siguiente
Decisión del usuario (ver conversación): (a) PBO sobre una muestra no sesgada de todos los ensayos evaluados (el reservorio de ET3 ya guarda 5.000 series diarias); (b) nulo verdadero N3 (barajado intradía) para FP y como base del control positivo.
