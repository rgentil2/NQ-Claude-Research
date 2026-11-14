#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
#endregion

//==============================================================================
// NQC_ET3_Fitness - WP4 fitness: t de la expectativa neta por operacion en el IS 2020-2022.
// SOLO DESARROLLO, sin compilar. Usa NQCE_Executor.Run con un portafolio de 1 miembro.
// El ejecutor NO sabe del IS: el llamador debe entregar barras recortadas a 2020-01-01..2022-12-31;
// el constructor lo verifica y lanza excepcion si hay barras fuera del IS (regla dura de datos de ET3).
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public sealed class NQCE3_Eval
	{
		public string TrialId, GenomeText; public double Fitness, MeanNet, StdNet, TotalNet; public int Trades; public bool Valid;
		public string TradeFingerprint;    // SHA256 de la secuencia (EntryTick, Dir) para deduplicar el databank
	}

	public sealed class NQCE3_Fitness
	{
		public const int MinTrades = 100;
		public const int IsFromKey = 20200101, IsToKey = 20221231;
		private NQCE_Bars bars; private NQCE_Config cfg; private INQCE3_Library lib;
		private Dictionary<string, NQCE3_Eval> cache = new Dictionary<string, NQCE3_Eval>();
		public int FreshEvaluations;       // evaluaciones nuevas = ensayos del ledger

		public NQCE3_Fitness(NQCE_Bars isBars, NQCE_Config config, INQCE3_Library library)
		{
			for (int i = 0; i < isBars.Count; i++)
				if (isBars.DayKey[i] < IsFromKey || isBars.DayKey[i] > IsToKey)
					throw new InvalidOperationException("ET3: barras fuera del IS 2020-2022 (DayKey=" + isBars.DayKey[i].ToString(CultureInfo.InvariantCulture) + ")");
			bars = isBars; cfg = config; lib = library;
		}

		public static double TStat(List<NQCE_Trade> trades, out double mean, out double sd)
		{
			int n = trades.Count; mean = 0; sd = 0; if (n < 2) return 0;
			double s = 0; foreach (NQCE_Trade t in trades) s += t.Net; mean = s / n;
			double q = 0; foreach (NQCE_Trade t in trades) q += (t.Net - mean) * (t.Net - mean);
			sd = Math.Sqrt(q / (n - 1));
			if (sd <= 0) return 0;
			return mean / (sd / Math.Sqrt((double)n));
		}

		public NQCE3_Eval Evaluate(NQCE3_Genome g)
		{
			g.Repair(lib);
			string id = g.TrialId(lib);
			NQCE3_Eval hit;
			if (cache.TryGetValue(id, out hit)) return hit;          // genoma repetido: no es ensayo nuevo
			NQCE3_Eval e = new NQCE3_Eval(); e.TrialId = id;
			NQCE_Member m = g.ToMember(lib, id.Substring(0, 12));
			FreshEvaluations++;
			if (m == null) { e.GenomeText = "EMPTY"; e.Fitness = 0; cache[id] = e; return e; }
			e.GenomeText = m.GenomeText();
			NQCE_Portfolio pf = new NQCE_Portfolio(); pf.Name = "ET3"; pf.Members.Add(m);
			NQCE_Result res = NQCE_Executor.Run(bars, pf, cfg, new NQCE_NullHooks());
			e.Trades = res.Trades.Count;
			double mean, sd; double t = TStat(res.Trades, out mean, out sd);
			e.MeanNet = mean; e.StdNet = sd; foreach (NQCE_Trade tr in res.Trades) e.TotalNet += tr.Net;
			e.Valid = e.Trades >= MinTrades;
			e.Fitness = e.Valid ? t : 0.0;                           // <100 operaciones: penalizacion a 0
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			foreach (NQCE_Trade tr in res.Trades) sb.Append(tr.EntryTick.ToString(CultureInfo.InvariantCulture)).Append(tr.Dir == 1 ? "L" : "S").Append(';');
			e.TradeFingerprint = NQCE_Const.Sha256Text(sb.ToString());
			cache[id] = e;
			return e;
		}
	}
}
