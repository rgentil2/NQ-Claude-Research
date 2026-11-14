using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

//==============================================================================
// NQC_ET5_Robust - Robustez interna por bloques (ET5): filtro por anios + PBO por CSCV. Contrato NQC 2.0.0.
// Codigo puro y determinista (sin NT8, sin ordenes). Entrada: matriz candidatos x 12 trimestres de P&L NETO tras costos
// (US$ por contrato, ya extraida del ledger/ejecutor). La matriz sale del ledger: ET5 NO genera ensayos nuevos.
//
// DECISIONES DE DISENO (ver docs/stages/ET5.md):
//  D5-1 Trimestres 0..11 = 2020Q1..2022Q4 del IS; anio a = trimestres 4a..4a+3 (a = 0,1,2). P&L neto anual = suma de los 4 trimestres.
//  D5-2 Filtro por candidato [B]: neto anual > 0 (estricto) en >= 2 de 3 anios.
//  D5-3 Metrica de ranking del PBO: P&L NETO total en el conjunto de trimestres (aditiva, sin supuestos de normalidad).
//       Un trimestre sin operaciones aporta 0 (no se excluye ni se imputa). Alternativa Expectativa (neto/operaciones) disponible, no es la por defecto.
//  D5-4 CSCV (Bailey et al.): las C(12,6)=924 particiones S (entrenamiento) / complemento (prueba), todas, orden lexicografico.
//       n* = argmax rendimiento en entrenamiento (empate: menor indice de candidato). omega = rango(n*) en prueba / (N+1), rango ascendente con
//       rango medio en empates. lambda = ln(omega/(1-omega)). PBO = fraccion de particiones con lambda <= 0 (n* en la mitad inferior o en la mediana).
//       Convencion conservadora: omega exactamente 0,5 (incluye empate total) cuenta como sobreajuste.
//  D5-5 PBO se calcula sobre el pool que paso el filtro de anios (el procedimiento que ET5 reenvia). Tambien se reporta, informativo, sobre el pool completo de ET4.
//  D5-6 N < 2 candidatos: PBO no evaluable (NaN) -> no se aprueba (falla cerrada) y se cierra campania; pregunta abierta para el orquestador.
//  D5-7 Cierre de campania si pool robusto vacio, PBO evaluable > 0,40, o PBO no evaluable.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public enum NQCE5_Metric { NetSum = 0, Expectancy = 1 }

	public class NQCE5_Candidate
	{
		public string Id;                 // identificador estable (p. ej. hash canonico del genoma)
		public double[] QuarterNet;       // 12 valores: P&L neto tras costos por trimestre
		public int[] QuarterTrades;       // 12 valores (solo usado por Expectancy; puede ser null con NetSum)
		public NQCE5_Candidate(string id, double[] q, int[] t) { Id = id; QuarterNet = q; QuarterTrades = t; }
	}

	public class NQCE5_Params
	{
		public const int Quarters = 12, Years = 3, Half = 6;
		public int MinPositiveYears = 2;       // [B]
		public double PboMax = 0.40;           // [B]
		public NQCE5_Metric Metric = NQCE5_Metric.NetSum;
	}

	public class NQCE5_Result
	{
		public List<NQCE5_Candidate> Robust = new List<NQCE5_Candidate>();
		public List<string> Rejected = new List<string>();   // "id | causa"
		public int InputCount;
		public double Pbo = double.NaN, PboFullPool = double.NaN, MeanLogit = double.NaN;
		public int Partitions;
		public bool PboEvaluable, PboPass, CampaignClosed;
		public string CloseReason = "";
		public string PoolHash = "", ReportHash = "", Report = "";
	}

	public static class NQCE5_Robust
	{
		static string F(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }

		public static double[] YearNet(NQCE5_Candidate c)
		{
			double[] y = new double[NQCE5_Params.Years];
			for (int q = 0; q < NQCE5_Params.Quarters; q++) y[q / 4] += c.QuarterNet[q];
			return y;
		}

		// Rendimiento de un conjunto de trimestres (mascara de 12 bits) para un candidato.
		static double Perf(NQCE5_Candidate c, int mask, NQCE5_Metric m)
		{
			double s = 0; int n = 0;
			for (int q = 0; q < NQCE5_Params.Quarters; q++)
				if ((mask >> q & 1) != 0) { s += c.QuarterNet[q]; if (m == NQCE5_Metric.Expectancy) n += c.QuarterTrades[q]; }
			if (m == NQCE5_Metric.NetSum) return s;
			return n == 0 ? 0.0 : s / n;   // sin operaciones en el conjunto: expectativa 0
		}

		public static List<int> Partitions()
		{
			List<int> l = new List<int>();
			for (int m = 0; m < (1 << NQCE5_Params.Quarters); m++) if (PopCount(m) == NQCE5_Params.Half) l.Add(m);   // 924, orden creciente
			return l;
		}
		static int PopCount(int v) { int c = 0; while (v != 0) { c += v & 1; v >>= 1; } return c; }

		// PBO por CSCV. lambdas (opcional) recibe el logit de cada particion.
		public static double Pbo(IList<NQCE5_Candidate> pool, NQCE5_Metric metric, List<double> lambdas)
		{
			int n = pool.Count;
			if (n < 2) return double.NaN;
			List<int> parts = Partitions(); int full = (1 << NQCE5_Params.Quarters) - 1; int bad = 0;
			double[] tr = new double[n], te = new double[n];
			foreach (int s in parts)
			{
				for (int i = 0; i < n; i++) { tr[i] = Perf(pool[i], s, metric); te[i] = Perf(pool[i], full ^ s, metric); }
				int best = 0; for (int i = 1; i < n; i++) if (tr[i] > tr[best]) best = i;   // empate: gana el menor indice
				double less = 0, eq = 0; for (int i = 0; i < n; i++) { if (te[i] < te[best]) less++; else if (te[i] == te[best]) eq++; }
				double rank = less + (eq + 1.0) / 2.0;                                      // rango medio ascendente (1..N)
				double omega = rank / (n + 1.0);
				double lam = Math.Log(omega / (1.0 - omega));
				if (lambdas != null) lambdas.Add(lam);
				if (lam <= 0.0) bad++;
			}
			return (double)bad / parts.Count;
		}

		public static NQCE5_Result Run(IList<NQCE5_Candidate> input, NQCE5_Params p)
		{
			NQCE5_Result r = new NQCE5_Result(); r.InputCount = input.Count; r.Partitions = Partitions().Count;
			foreach (NQCE5_Candidate c in input)
			{
				if (c.QuarterNet == null || c.QuarterNet.Length != NQCE5_Params.Quarters) throw new ArgumentException("candidato " + c.Id + ": se requieren 12 trimestres");
				if (p.Metric == NQCE5_Metric.Expectancy && (c.QuarterTrades == null || c.QuarterTrades.Length != NQCE5_Params.Quarters)) throw new ArgumentException("candidato " + c.Id + ": Expectancy requiere 12 conteos de operaciones");
				double[] y = YearNet(c); int pos = 0; for (int a = 0; a < y.Length; a++) if (y[a] > 0.0) pos++;
				if (pos >= p.MinPositiveYears) r.Robust.Add(c);
				else r.Rejected.Add(c.Id + " | anios positivos " + pos.ToString(CultureInfo.InvariantCulture) + "/3 (neto anual " + F(y[0]) + ";" + F(y[1]) + ";" + F(y[2]) + ")");
			}
			r.PboFullPool = Pbo(new List<NQCE5_Candidate>(input), p.Metric, null);
			List<double> lam = new List<double>();
			r.Pbo = Pbo(r.Robust, p.Metric, lam);
			r.PboEvaluable = !double.IsNaN(r.Pbo);
			if (r.PboEvaluable) { double s = 0; foreach (double l in lam) s += l; r.MeanLogit = s / lam.Count; }
			r.PboPass = r.PboEvaluable && r.Pbo <= p.PboMax;
			if (r.Robust.Count == 0) { r.CampaignClosed = true; r.CloseReason = "pool robusto vacio"; }
			else if (!r.PboEvaluable) { r.CampaignClosed = true; r.CloseReason = "PBO no evaluable (N<2)"; }
			else if (!r.PboPass) { r.CampaignClosed = true; r.CloseReason = "PBO " + F(r.Pbo) + " > " + F(p.PboMax); }
			if (r.CampaignClosed) { /* el pool robusto no se reenvia a ET6 */ }

			StringBuilder pl = new StringBuilder();
			foreach (NQCE5_Candidate c in r.Robust) pl.Append(c.Id).Append('\n');
			r.PoolHash = NQCE_Const.Sha256Text(pl.ToString());

			StringBuilder sb = new StringBuilder();
			sb.Append("NQC ET5 ROBUSTEZ | contrato ").Append(NQCE_Const.ContractVersion).Append('\n');
			sb.Append("metrica=").Append(p.Metric).Append(" min_anios_positivos=").Append(p.MinPositiveYears).Append(" pbo_max=").Append(F(p.PboMax)).Append(" particiones=").Append(r.Partitions).Append('\n');
			sb.Append("candidatos_entrada=").Append(r.InputCount).Append(" robustos=").Append(r.Robust.Count).Append(" rechazados=").Append(r.Rejected.Count).Append('\n');
			foreach (string s in r.Rejected) sb.Append("rechazo ").Append(s).Append('\n');
			sb.Append("pbo=").Append(r.PboEvaluable ? F(r.Pbo) : "NA").Append(" pbo_pool_completo_informativo=").Append(double.IsNaN(r.PboFullPool) ? "NA" : F(r.PboFullPool)).Append(" logit_medio=").Append(r.PboEvaluable ? F(r.MeanLogit) : "NA").Append('\n');
			sb.Append("pbo_pass=").Append(r.PboPass ? "SI" : "NO").Append(" campania_cerrada=").Append(r.CampaignClosed ? "SI" : "NO").Append(" motivo=").Append(r.CloseReason).Append('\n');
			sb.Append("pool_hash=").Append(r.PoolHash).Append('\n');
			r.Report = sb.ToString();
			r.ReportHash = NQCE_Const.Sha256Text(r.Report);
			return r;
		}
	}
}
