using System;
using System.Collections.Generic;
using System.Globalization;

//==============================================================================
// NQC_ET7_Neff - Estimacion de N_eff de TODA la poblacion de ensayos a partir de una MUESTRA ALEATORIA UNIFORME (D-038). Contrato NQC 2.0.0. SOLO DESARROLLO.
// Biblioteca PURA. Depende de NQC_ET7_Stats.cs (agrupamiento jerarquico NQCE7_NEff.Compute: linkage promedio, d = sqrt(0,5(1-rho)), corte).
//
// METODO. 1) La muestra (n series de P&L diario) se agrupa con el MISMO agrupamiento de ET7 (corte declarado antes de correr). 2) Se cuentan los ensayos de la
//   muestra en cada grupo (abundancias). 3) Se estima la riqueza de grupos de la poblacion con Chao1 con correccion de sesgo (Chao 1984/1987; Chiu 2014):
//       S_est = S_obs + f1 (f1 - 1) / (2 (f2 + 1)),   f1 = grupos con 1 individuo en la muestra, f2 = grupos con 2.
//   Varianza (Chao 2005 / EstimateS, factor (n-1)/n omitido a proposito para coincidir con la formula declarada):
//       V = f1(f1-1)/(2(f2+1)) + f1(2f1-1)^2/(4(f2+1)^2) + f1^2 f2 (f1-1)^2 / (4 (f2+1)^4).
//   IC 95% (Chao 1987, transformacion logaritmica del exceso T = S_est - S_obs): K = exp(z sqrt(ln(1 + V/T^2))), [S_obs + T/K, S_obs + T*K], z = 1,959964.
//   Si T = 0 el IC es [S_obs, S_obs + z sqrt(V)]. Superior se acota a N_poblacion (no puede haber mas grupos que ensayos).
//   Cobertura de la muestra (Good-Turing): C = 1 - f1/n (proporcion de INDIVIDUOS de la poblacion que caen en grupos ya vistos).
// SUPUESTOS Y SESGO CONOCIDO.
//   (a) La muestra es uniforme (cada ensayo evaluado con igual probabilidad; muestreo de reservorio con semilla). Un top-K NO sirve: sesga todo.
//   (b) Chao1 es un LIMITE INFERIOR de la riqueza verdadera: asume heterogeneidad de abundancias y estima lo no visto solo con singletons y doubletons; con
//       muchos grupos raros (abundancia poblacional << N/n) subestima, y el IC (que cuantifica el error de muestreo, no ese sesgo) puede NO cubrir el valor real.
//   (c) Se asume muestreo con reemplazo/poblacion grande; con n/N alto el estimador sobreestima algo lo no visto (lado conservador para el DSR).
//   (d) Los grupos se definen sobre la MUESTRA: con poblacion completa, el linkage promedio puede fundir o separar distinto (grupos fronterizos). No se corrige.
//   (e) SERIES PLANAS (orquestador, D-038): los genomas con 0 operaciones dan series identicas (todo ceros); NO son ruido. Se separan, forman UN SOLO grupo
//       (abundancia = n de planas) que entra en las abundancias de Chao1, y el agrupamiento corre solo sobre las series con actividad. Siempre se reporta NPlanas y
//       FraccionPlanas. Falla cerrada si quedan < MinMuestra series activas o si las planas son >= 99% de la muestra (GA degenerado). La regla de >= 50% planas
//       ya no aplica aqui (solo al modo COVERED, que no usa este archivo).
//   (f) f2 = 0 con f1 >= 10 (MaxF1SinF2) es indefinible: la formula da f1(f1-1)/2 sin base empirica -> falla cerrado.
//   Fallas cerradas: n < 30, N < n, corte invalido, series de longitud distinta o <3 dias, varianza cero generalizada, f2=0 con f1 grande, sin series.
// La muestra NO se re-agrupa para N_eff por cobertura: la formula grupos/cobertura de D-036 sigue en el modo COVERED.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public sealed class NQCE7_NeffResult
	{
		public int GruposObservados;        // S_obs: grupos distintos entre las series de la muestra
		public int F1, F2;                  // grupos con exactamente 1 y 2 ensayos de la muestra
		public double NEffEstimado;         // S_est de Chao1 (limite inferior de la riqueza verdadera), acotado a N_poblacion
		public double Inferior, Superior;   // IC 95%
		public double CoberturaMuestra;     // Good-Turing 1 - f1/n
		public int NMuestra;
		public long NPoblacion;
		public double VarChao;              // varianza de S_est
		public double Cut = double.NaN;     // corte de distancia usado (solo EstimateFromSample)
		public double SrVar = double.NaN;   // varianza entre grupos observados del SR diario (solo EstimateFromSample)
		public int[] Label;                 // grupo de cada serie de la muestra (solo EstimateFromSample)
		public int NPlanas = -1, NActivas = -1; // series planas (P&L diario constante = genomas inactivos, D-038) y series con actividad de la muestra
		public double FraccionPlanas = double.NaN;
		public string Falla = "";           // causa si falla cerrada (los numericos quedan NaN)
	}

	public static class NQCE7_Neff
	{
		public const double Z95 = 1.959963984540054;
		public const int MinMuestra = 30;
		public const int MaxF1SinF2 = 10;
		public const double PlanasMaxFraccion = 0.99;

		// Riqueza a partir de las abundancias (ensayos de la muestra en cada grupo observado; cada valor >= 1).
		public static NQCE7_NeffResult EstimateFromAbundances(int[] counts, long populationTrials)
		{
			NQCE7_NeffResult R = new NQCE7_NeffResult(); R.NPoblacion = populationTrials;
			R.NEffEstimado = double.NaN; R.Inferior = double.NaN; R.Superior = double.NaN; R.CoberturaMuestra = double.NaN; R.VarChao = double.NaN;
			if (counts == null || counts.Length == 0) { R.Falla = "sin abundancias"; return R; }
			long n = 0; int f1 = 0, f2 = 0;
			foreach (int c in counts) { if (c < 1) { R.Falla = "abundancia < 1"; return R; } n += c; if (c == 1) f1++; else if (c == 2) f2++; }
			R.NMuestra = (int)Math.Min(n, int.MaxValue); R.GruposObservados = counts.Length; R.F1 = f1; R.F2 = f2;
			if (n < MinMuestra) { R.Falla = "muestra de " + n.ToString(CultureInfo.InvariantCulture) + " ensayos (< " + MinMuestra.ToString(CultureInfo.InvariantCulture) + ")"; return R; }
			if (populationTrials < n) { R.Falla = "N poblacion (" + populationTrials.ToString(CultureInfo.InvariantCulture) + ") menor que la muestra"; return R; }
			R.CoberturaMuestra = 1.0 - (double)f1 / n;
			if (f2 == 0 && f1 >= MaxF1SinF2) { R.Falla = "f2=0 con f1=" + f1.ToString(CultureInfo.InvariantCulture) + " (>= " + MaxF1SinF2.ToString(CultureInfo.InvariantCulture) + "): Chao1 indefinible"; return R; }
			double a = (double)f1 * (f1 - 1) / (2.0 * (f2 + 1));
			double d1 = f2 + 1.0;
			double v = a + (double)f1 * (2.0 * f1 - 1.0) * (2.0 * f1 - 1.0) / (4.0 * d1 * d1) + (double)f1 * f1 * f2 * (f1 - 1.0) * (f1 - 1.0) / (4.0 * d1 * d1 * d1 * d1);
			double sobs = counts.Length, pop = populationTrials;
			double est = Math.Min(sobs + a, pop), T = est - sobs, lo, hi;
			if (T > 0) { double K = Math.Exp(Z95 * Math.Sqrt(Math.Log(1.0 + v / (T * T)))); lo = sobs + T / K; hi = sobs + T * K; }
			else { lo = sobs; hi = sobs + Z95 * Math.Sqrt(v); }
			R.NEffEstimado = est; R.VarChao = v; R.Inferior = Math.Max(sobs, Math.Min(lo, est)); R.Superior = Math.Min(pop, Math.Max(hi, est));
			return R;
		}

		// Firma acordada con ET3/ET7: muestra uniforme de series de P&L diario (todas de la misma longitud) + N de la poblacion de ensayos evaluados + corte de distancia.
		public static NQCE7_NeffResult EstimateFromSample(double[][] sampleSeries, long populationTrials, double cut)
		{
			NQCE7_NeffResult R = new NQCE7_NeffResult(); R.NPoblacion = populationTrials; R.Cut = cut;
			R.NEffEstimado = double.NaN; R.Inferior = double.NaN; R.Superior = double.NaN; R.CoberturaMuestra = double.NaN; R.VarChao = double.NaN;
			int n = sampleSeries == null ? 0 : sampleSeries.Length; R.NMuestra = n;
			if (n == 0) { R.Falla = "sin series de muestra"; return R; }
			if (n < MinMuestra) { R.Falla = "muestra de " + n.ToString(CultureInfo.InvariantCulture) + " series (< " + MinMuestra.ToString(CultureInfo.InvariantCulture) + ")"; return R; }
			if (populationTrials < n) { R.Falla = "N poblacion (" + populationTrials.ToString(CultureInfo.InvariantCulture) + ") menor que la muestra"; return R; }
			for (int i = 1; i < n; i++) if (sampleSeries[i] != null && sampleSeries[0] != null && sampleSeries[i].Length != sampleSeries[0].Length) { R.Falla = "series de longitud distinta"; return R; }
			int flat = 0; bool[] isFlat = new bool[n];
			for (int i = 0; i < n; i++)
			{
				if (sampleSeries[i] == null) { R.Falla = "serie nula en la muestra"; return R; }
				double m = 0; foreach (double x in sampleSeries[i]) m += x; m /= Math.Max(1, sampleSeries[i].Length);
				double s = 0; foreach (double x in sampleSeries[i]) s += (x - m) * (x - m);
				if (s <= 1e-18) { flat++; isFlat[i] = true; }
			}
			R.NPlanas = flat; R.NActivas = n - flat; R.FraccionPlanas = (double)flat / n;
			if (R.FraccionPlanas >= PlanasMaxFraccion) { R.Falla = "varianza cero generalizada: " + flat.ToString(CultureInfo.InvariantCulture) + " de " + n.ToString(CultureInfo.InvariantCulture) + " series planas (>= 99%, GA degenerado)"; return R; }
			if (n - flat < MinMuestra) { R.Falla = "solo " + (n - flat).ToString(CultureInfo.InvariantCulture) + " series con actividad (< " + MinMuestra.ToString(CultureInfo.InvariantCulture) + ") tras separar " + flat.ToString(CultureInfo.InvariantCulture) + " planas"; return R; }
			double[][] act = new double[n - flat][]; int[] map = new int[n - flat]; int q = 0;
			for (int i = 0; i < n; i++) if (!isFlat[i]) { act[q] = sampleSeries[i]; map[q] = i; q++; }
			NQCE7_NEffResult c = NQCE7_NEff.Compute(act, act.Length, cut, 1.0, int.MaxValue);
			if (c.Fail.Length > 0) { R.Falla = c.Fail; return R; }
			int extra = flat > 0 ? 1 : 0;
			int[] cnt = new int[c.Clusters + extra]; foreach (int g in c.Label) cnt[g]++;
			if (flat > 0) cnt[c.Clusters] = flat;   // UN grupo plano
			int[] lab = new int[n]; for (int i = 0; i < n; i++) lab[i] = isFlat[i] ? c.Clusters : -1;
			for (int j = 0; j < map.Length; j++) lab[map[j]] = c.Label[j];
			NQCE7_NeffResult E = EstimateFromAbundances(cnt, populationTrials);
			E.NPlanas = flat; E.NActivas = n - flat; E.FraccionPlanas = R.FraccionPlanas;
			E.Cut = cut; E.SrVar = c.SrVar; E.Label = lab;
			return E;
		}
	}
}
