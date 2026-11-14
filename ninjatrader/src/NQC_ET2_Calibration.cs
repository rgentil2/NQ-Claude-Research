#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
#endregion

//==============================================================================
// NQC_ET2_Calibration - Biblioteca PURA de estadistica de calibracion de ET2 (sin ejecutor, sin datos). Contrato NQC 2.0.0.
// SOLO DESARROLLO. El diseno del experimento vive en docs/stages/ET2.md; aqui solo lo que debe ser calculo exacto:
//  - Clopper-Pearson exacto (intervalo de una proporcion binomial) por biseccion de la cola binomial;
//  - percentil nulo con la convencion de D-036: 100*#{nulo < x}/n (los empates no suman), n >= 20;
//  - eleccion de un umbral (P_pass, ppass_boot_min, ...) que cumple FP <= maxFp sobre los nulos y maximiza la potencia;
//  - estimador de costo (formulas; los ms por evaluacion son un SUPUESTO que se mide en el paso 0 del runbook).
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public static class NQCET2_Stats
	{
		// P(X <= x) con X~Bin(n,p), por suma estable en log.
		public static double BinCdf(int x, int n, double p)
		{
			if (x < 0) return 0; if (x >= n) return 1; if (p <= 0) return 1; if (p >= 1) return 0;
			double s = 0, lp = Math.Log(p), lq = Math.Log(1 - p);
			for (int k = 0; k <= x; k++) s += Math.Exp(LogChoose(n, k) + k * lp + (n - k) * lq);
			return Math.Min(1.0, s);
		}
		private static double LogChoose(int n, int k) { double r = 0; for (int i = 1; i <= k; i++) r += Math.Log((double)(n - k + i) / i); return r; }

		// Intervalo exacto bilateral (1-alpha). x exitos en n.
		public static void ClopperPearson(int x, int n, double alpha, out double lo, out double hi)
		{
			lo = 0; hi = 1; if (n <= 0) return;
			if (x > 0)   // menor p con P(X >= x) >= alpha/2  <=>  1 - Cdf(x-1) = alpha/2
			{
				double a = 0, b = 1; for (int it = 0; it < 200; it++) { double m = (a + b) / 2; if (1 - BinCdf(x - 1, n, m) < alpha / 2) a = m; else b = m; } lo = (a + b) / 2;
			}
			if (x < n)   // mayor p con P(X <= x) >= alpha/2
			{
				double a = 0, b = 1; for (int it = 0; it < 200; it++) { double m = (a + b) / 2; if (BinCdf(x, n, m) > alpha / 2) a = m; else b = m; } hi = (a + b) / 2;
			}
		}

		// Cota superior UNILATERAL (1-alpha) para la tasa de falsos positivos: mayor p con P(X <= x) >= alpha.
		public static double UpperOneSided(int x, int n, double alpha)
		{
			if (x >= n) return 1; double a = 0, b = 1; for (int it = 0; it < 200; it++) { double m = (a + b) / 2; if (BinCdf(x, n, m) > alpha) a = m; else b = m; } return (a + b) / 2;
		}

		// Menor n tal que 0 falsos positivos en n campanas dejan la cota superior unilateral (1-alpha) <= pMax.
		public static int MinNForZeroFp(double pMax, double alpha) { return (int)Math.Ceiling(Math.Log(alpha) / Math.Log(1 - pMax)); }

		// D-036: percentil nulo = 100 * #{nulo < x} / n (empates no suman). n >= 20 o devuelve NaN.
		public static double NullPercentile(double[] nullVals, double x)
		{
			if (nullVals == null || nullVals.Length < 20) return double.NaN;
			int c = 0; foreach (double v in nullVals) if (v < x) c++;
			return 100.0 * c / nullVals.Length;
		}

		// Cuantil de orden q de los nulos por la convencion ceil(q*n) (D-036 la usa en el P95 del Monte Carlo).
		public static double Quantile(double[] vals, double q)
		{
			double[] s = (double[])vals.Clone(); Array.Sort(s); int k = (int)Math.Ceiling(q * s.Length); if (k < 1) k = 1; if (k > s.Length) k = s.Length; return s[k - 1];
		}

		// Umbral minimo tau (entre los valores observados) tal que #{nulo >= tau}/n <= maxFp; devuelve la potencia en tau
		// (#{inyectado >= tau}/m). Si ningun valor observado cumple el FP, devuelve +inf (umbral no factible con estos datos).
		public static double ChooseThreshold(double[] nullVals, double[] injVals, double maxFp, out double fpRate, out double power)
		{
			List<double> cand = new List<double>(); foreach (double v in nullVals) cand.Add(v); foreach (double v in injVals) cand.Add(v); cand.Sort();
			fpRate = 0; power = 0;
			foreach (double tau in cand)
			{
				int fp = 0, pw = 0; foreach (double v in nullVals) if (v >= tau) fp++; foreach (double v in injVals) if (v >= tau) pw++;
				double f = (double)fp / nullVals.Length;
				if (f <= maxFp) { fpRate = f; power = injVals.Length == 0 ? 0 : (double)pw / injVals.Length; return tau; }
			}
			fpRate = 0; power = 0; return double.PositiveInfinity;
		}
	}

	public sealed class NQCET2_CostModel
	{
		// Parametros del embudo REDUCIDO propuesto (ver docs/stages/ET2.md). Los ms por evaluacion se MIDEN en el paso 0 del runbook.
		public int Pop = 200, Gens = 50, TopK = 200, NeighborsPerCandidate = 16, BeamEvals = 1230, McExploratory = 500;
		public double MsGenomeEval = 10.0, MsBeamEval = 30.0, MsFixed = 60000.0;   // SUPUESTOS declarados (no medidos)
		public double CampaignMs() { return Pop * (double)Gens * MsGenomeEval + TopK * (double)NeighborsPerCandidate * 2.0 * MsGenomeEval + BeamEvals * MsBeamEval + MsFixed; }
		public string Table(int nNull, int nInj)
		{
			StringBuilder sb = new StringBuilder();
			double c = CampaignMs(); double tot = c * (nNull + nInj);
			sb.Append("campana_reducida_ms=").Append(c.ToString("0", CultureInfo.InvariantCulture)).Append(" (").Append((c / 60000.0).ToString("0.0", CultureInfo.InvariantCulture)).Append(" min)")
			  .Append(" campanas=").Append(nNull + nInj).Append(" total_h=").Append((tot / 3600000.0).ToString("0.0", CultureInfo.InvariantCulture)).Append(" [SUPUESTO ms/eval=").Append(MsGenomeEval.ToString("0.#", CultureInfo.InvariantCulture)).Append("]");
			return sb.ToString();
		}
	}
}
