using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

//==============================================================================
// NQC_ET7_Stats - Estadistica de ET7 (N efectivo, DSR, percentil nulo). Contrato NQC 2.0.0. SOLO DESARROLLO. Biblioteca PURA (sin API de NinjaTrader).
// Depende de NQC_ET1_Core.cs (NQCE_Rng, NQCE_Const) y NQC_ET1C_AccountSim.cs. Ver docs/stages/ET7.md para las decisiones.
//
// N EFECTIVO: series de P&L diario del IS (una por ensayo con serie). rho = Pearson. Distancia d = sqrt(0,5*(1-rho)) (0 si rho=1; 0,707 si rho=0; 1 si rho=-1).
//   Linkage PROMEDIO (UPGMA, Lance-Williams). Se funden grupos mientras la distancia minima <= Corte (parametro declarado ANTES de correr; no se ajusta tras ver resultados).
//   Corte por defecto 0,40 (<=> rho >= 0,68). Un corte MENOR da MAS grupos = N_eff mayor = DSR mas duro (lado conservador).
//   Serie de varianza cero: rho indefinido -> grupo propio (distancia 1 a todo; conservador).
//   Cobertura: si solo una fraccion c de los ensayos brutos tiene serie, N_eff_total = grupos / c (extrapolacion lineal, conservadora); c < CoberturaMin => falla cerrada.
// DSR (Bailey & Lopez de Prado 2014), con SR por periodo (diario, NO anualizado):
//   SR0 = sqrt(V) * ((1-g)*Z(1-1/N) + g*Z(1-1/(N*e))),  g = 0,5772156649 (Euler-Mascheroni), N = N_eff, V = varianza entre grupos del SR diario; N<=1 => SR0 = 0.
//   DSR = Phi( (SR - SR0) * sqrt(T-1) / sqrt(1 - g3*SR + ((g4-1)/4)*SR^2) ),  g3 = asimetria, g4 = curtosis (NO exceso; normal = 3), T = dias con serie.
//   Anualizado (solo informativo): SR*sqrt(252). Retorno diario del portafolio = P&L neto de la sesion en US$ (dias sin operacion = 0); el SR es invariante a la escala.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public static class NQCE7_Norm
	{
		// erfc de doble precision: serie de Taylor de erf para |z|<=3 y fraccion continua para z>3.
		public static double Erfc(double z)
		{
			if (double.IsNaN(z)) return double.NaN;
			if (z < 0) return 2.0 - Erfc(-z);
			if (z > 27) return 0;
			if (z <= 3.0)
			{
				double sum = z, term = z; double z2 = z * z;
				for (int n = 1; n < 200; n++)
				{
					term *= -z2 / n;
					double add = term / (2 * n + 1);
					sum += add;
					if (Math.Abs(add) < 1e-17 * Math.Abs(sum)) break;
				}
				return 1.0 - 2.0 / Math.Sqrt(Math.PI) * sum;
			}
			double f = z;
			for (int k = 80; k >= 1; k--) f = z + (k / 2.0) / f;
			return Math.Exp(-z * z) / Math.Sqrt(Math.PI) / f;
		}
		public static double Cdf(double x) { return 0.5 * Erfc(-x / Math.Sqrt(2.0)); }
		public static double Pdf(double x) { return Math.Exp(-0.5 * x * x) / Math.Sqrt(2.0 * Math.PI); }

		// Normal inversa: semilla de Acklam + pasos de Halley contra Cdf (precision ~1e-15).
		public static double InvCdf(double p)
		{
			if (double.IsNaN(p) || p <= 0 || p >= 1) { if (p == 0) return double.NegativeInfinity; if (p == 1) return double.PositiveInfinity; return double.NaN; }
			double[] a = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
			double[] b = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01 };
			double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
			double[] d = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00 };
			if (p > 0.5) return -InvCdf(1.0 - p);
			double x;
			if (p < 0.02425) { double q = Math.Sqrt(-2 * Math.Log(p)); x = (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1); }
			else if (p > 1 - 0.02425) { double q = Math.Sqrt(-2 * Math.Log(1 - p)); x = -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1); }
			else { double q = p - 0.5, r = q * q; x = (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q / (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1); }
			for (int it = 0; it < 3; it++)
			{
				double u = (Cdf(x) - p) / Pdf(x);
				x = x - u / (1 + x * u / 2);
			}
			return x;
		}
	}

	public sealed class NQCE7_NEffResult
	{
		public int NGross;          // ensayos brutos del ledger acumulado
		public int NSeries;         // series usadas en el agrupamiento
		public int Clusters;        // grupos entre las series
		public double Coverage;     // NSeries / NGross (tope 1)
		public double NEff;         // grupos / cobertura (>= 1); NaN si falla cerrada
		public double SrVar;        // varianza entre grupos del SR diario (representante = miembro de menor indice)
		public double Cut;
		public int[] Label;         // grupo de cada serie
		public string Fail = "";    // causa si falla cerrada
		public string Method = "COVERED"; // COVERED (D-036) o SAMPLE_CHAO1 (D-038)
		public NQCE7_NeffResult Sample;   // detalle de Chao1 (solo SAMPLE_CHAO1)
	}

	public static class NQCE7_NEff
	{
		public static double Corr(double[] x, double[] y)
		{
			int n = x.Length; if (n != y.Length || n < 3) return double.NaN;
			double mx = 0, my = 0; for (int i = 0; i < n; i++) { mx += x[i]; my += y[i]; } mx /= n; my /= n;
			double sxx = 0, syy = 0, sxy = 0;
			for (int i = 0; i < n; i++) { double dx = x[i] - mx, dy = y[i] - my; sxx += dx * dx; syy += dy * dy; sxy += dx * dy; }
			if (sxx <= 1e-18 || syy <= 1e-18) return double.NaN;
			double r = sxy / Math.Sqrt(sxx * syy); return Math.Max(-1.0, Math.Min(1.0, r));
		}
		public static double Dist(double rho) { return double.IsNaN(rho) ? 1.0 : Math.Sqrt(0.5 * (1.0 - rho)); }
		public static double DailySharpe(double[] x)
		{
			int n = x.Length; if (n < 2) return double.NaN;
			double m = 0; foreach (double v in x) m += v; m /= n;
			double s = 0; foreach (double v in x) s += (v - m) * (v - m); s = Math.Sqrt(s / (n - 1));
			return s <= 1e-12 ? double.NaN : m / s;
		}

		// Hilos de la matriz de distancias (0 = automatico: min(nucleos, 16)). No cambia ningun resultado (cada par se calcula de forma independiente y con la
		// misma aritmetica que Corr: ver DistMatrix); solo el tiempo.
		public static int Threads = 0;
		private static int EffThreads(int t) { if (t <= 0) t = Math.Min(Environment.ProcessorCount, 16); return Math.Max(1, t); }

		// Matriz simetrica de distancias d = Dist(Corr(i,j)). BIT A BIT identica a la ingenua Dist(Corr(series[i], series[j])): se precalculan una vez la media, las
		// desviaciones dx = x - m y sxx de cada serie con las mismas operaciones y el mismo orden que Corr, y por par solo queda sxy (suma secuencial, sin
		// reordenar), r = sxy / sqrt(sxx*syy) y el recorte a [-1,1]. Calculo por bloques (cabe en cache) y en paralelo; cada celda la escribe un unico hilo y no hay
		// reducciones entre hilos, asi que el resultado no depende del numero de hilos.
		public static double[,] DistMatrix(double[][] series, int n, int T, int threads)
		{
			double[,] D = new double[n, n];
			double[][] dev = new double[n][]; double[] sxx = new double[n]; bool[] bad = new bool[n];
			for (int i = 0; i < n; i++)
			{
				double[] x = series[i]; double m = 0; for (int k = 0; k < T; k++) m += x[k]; m /= T;
				double[] d = new double[T]; double sq = 0; for (int k = 0; k < T; k++) { double dx = x[k] - m; d[k] = dx; sq += dx * dx; }
				dev[i] = d; sxx[i] = sq; bad[i] = T < 3 || sq <= 1e-18;
			}
			const int B = 32; int nb = (n + B - 1) / B;
			int th = EffThreads(threads);
			Action<int> rowBlock = delegate(int bi)
			{
				int i0 = bi * B, i1 = Math.Min(n, i0 + B);
				for (int bj = bi; bj < nb; bj++)
				{
					int j0 = bj * B, j1 = Math.Min(n, j0 + B);
					for (int i = i0; i < i1; i++)
					{
						double[] di = dev[i]; double sxi = sxx[i]; bool bi_ = bad[i];
						int j = Math.Max(j0, i + 1);
						// 4 pares a la vez (4 sumas independientes, cada una secuencial: mismo resultado bit a bit, mas paralelismo de instrucciones)
						for (; j + 3 < j1; j += 4)
						{
							if (bi_ || bad[j] || bad[j + 1] || bad[j + 2] || bad[j + 3]) { for (int q = j; q < j + 4; q++) SetD(D, dev, sxx, bad, di, sxi, bi_, i, q, T); continue; }
							double[] d0 = dev[j], d1 = dev[j + 1], d2 = dev[j + 2], d3 = dev[j + 3]; double a0 = 0, a1 = 0, a2 = 0, a3 = 0;
							for (int k = 0; k < T; k++) { double v = di[k]; a0 += v * d0[k]; a1 += v * d1[k]; a2 += v * d2[k]; a3 += v * d3[k]; }
							D[i, j] = D[j, i] = DistFrom(a0, sxi, sxx[j]); D[i, j + 1] = D[j + 1, i] = DistFrom(a1, sxi, sxx[j + 1]);
							D[i, j + 2] = D[j + 2, i] = DistFrom(a2, sxi, sxx[j + 2]); D[i, j + 3] = D[j + 3, i] = DistFrom(a3, sxi, sxx[j + 3]);
						}
						for (; j < j1; j++) SetD(D, dev, sxx, bad, di, sxi, bi_, i, j, T);
					}
				}
			};
			if (th == 1 || nb == 1) { for (int bi = 0; bi < nb; bi++) rowBlock(bi); }
			else
			{   // bloques de fila de coste decreciente: reparto dinamico por contador atomico (sin orden fijo; el resultado no depende de el)
				int next = -1; System.Threading.Thread[] ts = new System.Threading.Thread[th];
				for (int t = 0; t < th; t++) { ts[t] = new System.Threading.Thread(delegate() { int b; while ((b = System.Threading.Interlocked.Increment(ref next)) < nb) rowBlock(b); }); ts[t].Start(); }
				for (int t = 0; t < th; t++) ts[t].Join();
			}
			return D;
		}

		private static double DistFrom(double sxy, double sxi, double sxj)
		{ double r = sxy / Math.Sqrt(sxi * sxj); r = Math.Max(-1.0, Math.Min(1.0, r)); return Math.Sqrt(0.5 * (1.0 - r)); }
		private static void SetD(double[,] D, double[][] dev, double[] sxx, bool[] bad, double[] di, double sxi, bool bi_, int i, int j, int T)
		{
			double d;
			if (bi_ || bad[j]) d = 1.0;
			else { double[] dj = dev[j]; double sxy = 0; for (int k = 0; k < T; k++) sxy += di[k] * dj[k]; d = DistFrom(sxy, sxi, sxx[j]); }
			D[i, j] = d; D[j, i] = d;
		}

		// nGross = ensayos brutos del ledger acumulado; series = series de P&L diario del IS (todas de la misma longitud).
		public static NQCE7_NEffResult Compute(double[][] series, int nGross, double cut, double coverageMin, int maxSeries)
		{
			NQCE7_NEffResult R = new NQCE7_NEffResult(); R.NGross = nGross; R.Cut = cut; R.NEff = double.NaN; R.SrVar = double.NaN;
			int n = series == null ? 0 : series.Length; R.NSeries = n;
			if (n == 0) { R.Fail = "sin series de ensayos"; return R; }
			if (nGross < n) { R.Fail = "N bruto (" + nGross.ToString(CultureInfo.InvariantCulture) + ") menor que el numero de series"; return R; }
			if (n > maxSeries) { R.Fail = "series (" + n.ToString(CultureInfo.InvariantCulture) + ") superan MaxSeries (" + maxSeries.ToString(CultureInfo.InvariantCulture) + "): reducir antes"; return R; }
			if (double.IsNaN(cut) || cut <= 0 || cut >= 1) { R.Fail = "corte invalido"; return R; }
			int T = series[0].Length; for (int i = 0; i < n; i++) if (series[i].Length != T) { R.Fail = "series de longitud distinta"; return R; }
			R.Coverage = Math.Min(1.0, (double)n / nGross);
			if (R.Coverage < coverageMin) { R.Fail = "cobertura de series " + R.Coverage.ToString("0.0000", CultureInfo.InvariantCulture) + " < minimo " + coverageMin.ToString("0.0000", CultureInfo.InvariantCulture); return R; }

			double[,] D = DistMatrix(series, n, T, Threads);
			int[] size = new int[n]; bool[] act = new bool[n]; int[] rep = new int[n]; int[] label = new int[n];
			for (int i = 0; i < n; i++) { size[i] = 1; act[i] = true; label[i] = i; }
			int[] nn = new int[n]; double[] nd = new double[n];
			for (int i = 0; i < n; i++) Near(D, act, n, i, nn, nd);
			int clusters = n;
			while (clusters > 1)
			{
				int bi = -1; double bd = double.MaxValue;
				for (int i = 0; i < n; i++) if (act[i] && nn[i] >= 0 && (nd[i] < bd || (nd[i] == bd && i < bi))) { bd = nd[i]; bi = i; }
				if (bi < 0 || bd > cut) break;
				int bj = nn[bi]; int a = Math.Min(bi, bj), b = Math.Max(bi, bj); // a absorbe a b
				for (int k = 0; k < n; k++) if (act[k] && k != a && k != b)
				{
					double nv = (size[a] * D[a, k] + size[b] * D[b, k]) / (size[a] + size[b]);
					D[a, k] = nv; D[k, a] = nv;
				}
				size[a] += size[b]; act[b] = false; clusters--;
				for (int i = 0; i < n; i++) if (label[i] == b) label[i] = a;
				for (int i = 0; i < n; i++) if (act[i] && (i == a || nn[i] == a || nn[i] == b || nd[i] > D[i, a])) Near(D, act, n, i, nn, nd);
			}
			R.Clusters = clusters;
			Dictionary<int, int> map = new Dictionary<int, int>(); R.Label = new int[n];
			for (int i = 0; i < n; i++) { int g; if (!map.TryGetValue(label[i], out g)) { g = map.Count; map[label[i]] = g; rep[g] = i; } R.Label[i] = g; }
			R.NEff = Math.Max(1.0, clusters / R.Coverage);
			List<double> srs = new List<double>();
			for (int g = 0; g < clusters; g++) { double s = DailySharpe(series[rep[g]]); if (!double.IsNaN(s)) srs.Add(s); }
			if (srs.Count >= 2) { double m = 0; foreach (double s in srs) m += s; m /= srs.Count; double v = 0; foreach (double s in srs) v += (s - m) * (s - m); R.SrVar = v / (srs.Count - 1); }
			else R.SrVar = 0;
			return R;
		}
		private static void Near(double[,] D, bool[] act, int n, int i, int[] nn, double[] nd)
		{
			nn[i] = -1; nd[i] = double.MaxValue;
			for (int k = 0; k < n; k++) if (act[k] && k != i && D[i, k] < nd[i]) { nd[i] = D[i, k]; nn[i] = k; }
		}
	}

	public sealed class NQCE7_DsrResult
	{
		public double Sr, SrAnnual, Skew, Kurt, Sr0, Dsr, Psr0; public int T; public string Fail = "";
	}

	public static class NQCE7_Dsr
	{
		public const double EulerGamma = 0.5772156649015329;
		public const double SessionsPerYear = 252.0;

		public static double ExpectedMaxSr(double srVar, double nEff)
		{
			if (nEff <= 1.0 || srVar <= 0) return 0;
			return Math.Sqrt(srVar) * ((1.0 - EulerGamma) * NQCE7_Norm.InvCdf(1.0 - 1.0 / nEff) + EulerGamma * NQCE7_Norm.InvCdf(1.0 - 1.0 / (nEff * Math.E)));
		}

		public static NQCE7_DsrResult Compute(double[] daily, double nEff, double srVar)
		{
			NQCE7_DsrResult R = new NQCE7_DsrResult(); R.Dsr = double.NaN; R.T = daily == null ? 0 : daily.Length;
			if (R.T < 30) { R.Fail = "menos de 30 dias de retorno"; return R; }
			if (double.IsNaN(nEff) || nEff < 1.0) { R.Fail = "N_eff no disponible"; return R; }
			if (double.IsNaN(srVar) || srVar < 0) { R.Fail = "varianza del SR no disponible"; return R; }
			int T = R.T; double m = 0; foreach (double v in daily) m += v; m /= T;
			double m2 = 0, m3 = 0, m4 = 0; foreach (double v in daily) { double d = v - m; m2 += d * d; m3 += d * d * d; m4 += d * d * d * d; }
			m2 /= T; m3 /= T; m4 /= T;
			if (m2 <= 1e-18) { R.Fail = "retornos de varianza cero"; return R; }
			double sd = Math.Sqrt(m2 * T / (T - 1));            // desviacion muestral para el SR
			R.Sr = m / sd; R.SrAnnual = R.Sr * Math.Sqrt(SessionsPerYear);
			R.Skew = m3 / Math.Pow(m2, 1.5); R.Kurt = m4 / (m2 * m2);
			R.Sr0 = ExpectedMaxSr(srVar, nEff);
			double den = 1.0 - R.Skew * R.Sr + (R.Kurt - 1.0) / 4.0 * R.Sr * R.Sr;
			if (den <= 0) { R.Fail = "denominador del DSR no positivo"; return R; }
			double sq = Math.Sqrt(T - 1.0);
			R.Dsr = NQCE7_Norm.Cdf((R.Sr - R.Sr0) * sq / Math.Sqrt(den));
			R.Psr0 = NQCE7_Norm.Cdf(R.Sr * sq / Math.Sqrt(den));
			return R;
		}
	}

	public static class NQCE7_NullPct
	{
		// Percentil conservador: 100 * #{nulo < x} / n (los empates NO suman). NaN si faltan datos.
		public static double Percentile(double x, double[] nullBest, int minN, out string fail)
		{
			fail = "";
			if (nullBest == null || nullBest.Length < minN) { fail = "distribucion nula con " + (nullBest == null ? 0 : nullBest.Length).ToString(CultureInfo.InvariantCulture) + " valores (< " + minN.ToString(CultureInfo.InvariantCulture) + ")"; return double.NaN; }
			if (double.IsNaN(x)) { fail = "fitness del portafolio no disponible"; return double.NaN; }
			int below = 0; foreach (double v in nullBest) { if (double.IsNaN(v)) { fail = "NaN en la distribucion nula"; return double.NaN; } if (v < x) below++; }
			return 100.0 * below / nullBest.Length;
		}
	}
}
