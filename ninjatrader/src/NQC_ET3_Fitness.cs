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
		public bool ZeroTradesWithRejects;   // 0 operaciones y alguna condicion sin senal por calentamiento/volumen/no finito (diagnostico A4)
		public string TradeFingerprint;    // SHA256 de la secuencia (EntryTick, Dir) para deduplicar el databank
	}

	public sealed class NQCE3_Fitness
	{
		public const int MinTrades = 100;
		public const int IsFromKey = 20200101, IsToKey = 20221231;
		private NQCE_Bars bars; private NQCE_Config cfg; private INQCE3_Library lib;
		private Dictionary<string, NQCE3_Eval> cache = new Dictionary<string, NQCE3_Eval>();
		public int FreshEvaluations;       // evaluaciones nuevas = ensayos del ledger
		public int ZeroTradeEvals;         // evaluaciones nuevas con 0 operaciones
		public int ZeroTradeWithRejectEvals; // de ellas, con condiciones sin senal por calentamiento/volumen/no finito
		public const double InvalidBase = -1000.0;   // fitness de invalidos: InvalidBase + trades/MinTrades (< todo t valido realista)
		public const int Probe0931 = 9 * 60 + 31, MaxPeriodBars = 7 * 30 + 10;
		public double WarmupCoverage;      // fraccion de dias con >= 7*30+10 barras previas a las 09:31 del mismo dia

		// volume: volumen por barra alineado con isBars (OBLIGATORIO: se enlaza con lib.Bind; sin el, 24 familias nunca disparan).
		public NQCE3_Fitness(NQCE_Bars isBars, double[] volume, NQCE_Config config, INQCE3_Library library)
		{
			if (volume == null || volume.Length != isBars.Count)
				throw new ArgumentException("ET3: se exige volumen alineado con las barras del IS (Bind(bars, volumen)); sin el, las 24 familias de volumen no dan senal");
			// M1 completas desde 00:00: SameDay exige 7n+10 barras del mismo dia (n=30 -> 220) antes de la senal de las 09:31
			int days = 0, ok = 0;
			for (int i = 0; i < isBars.Count; i++)
			{
				if (isBars.Stamp[i] != Probe0931) continue;
				days++; int j = i; while (j > 0 && isBars.DayKey[j - 1] == isBars.DayKey[i]) j--;
				if (i - j >= MaxPeriodBars) ok++;
			}
			WarmupCoverage = days == 0 ? 0.0 : (double)ok / days;
			if (days == 0 || WarmupCoverage < 0.9)
				throw new ArgumentException("ET3: las barras no son M1 completas desde 00:00 (cobertura de calentamiento " + WarmupCoverage.ToString("0.00", CultureInfo.InvariantCulture) + " < 0,90); SameDay no senalaria con periodos grandes");
			if (library is NQCE3_Library) ((NQCE3_Library)library).Bind(isBars, volume);
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
			NQCE_Member m = g.ToMember(lib, "x");                    // un solo ToMember por evaluacion (M6)
			string id = NQCE3_Genome.TrialIdOf(lib, m);
			NQCE3_Eval hit;
			if (cache.TryGetValue(id, out hit)) return hit;          // genoma repetido: no es ensayo nuevo
			NQCE3_Eval e = new NQCE3_Eval(); e.TrialId = id;
			FreshEvaluations++;
			if (m == null) { e.GenomeText = "EMPTY"; e.Fitness = InvalidBase; ZeroTradeEvals++; cache[id] = e; return e; }
			m.Id = id.Substring(0, 12);
			NQCE3_Library nl = lib as NQCE3_Library; long rej0 = nl == null ? 0 : nl.RejectWarmup + nl.RejectVolume + nl.RejectNonFinite;
			e.GenomeText = m.GenomeText();
			NQCE_Portfolio pf = new NQCE_Portfolio(); pf.Name = "ET3"; pf.Members.Add(m);
			NQCE_Result res = NQCE_Executor.Run(bars, pf, cfg, new NQCE_NullHooks());
			e.Trades = res.Trades.Count;
			double mean, sd; double t = TStat(res.Trades, out mean, out sd);
			e.MeanNet = mean; e.StdNet = sd; foreach (NQCE_Trade tr in res.Trades) e.TotalNet += tr.Net;
			e.Valid = e.Trades >= MinTrades;
			// invalido (<100 operaciones): por debajo de todo valido, ordenado por numero de operaciones (A3)
			e.Fitness = e.Valid ? t : InvalidBase + (double)e.Trades / MinTrades;
			if (e.Trades == 0)
			{
				ZeroTradeEvals++;
				if (nl != null && nl.RejectWarmup + nl.RejectVolume + nl.RejectNonFinite > rej0) { e.ZeroTradesWithRejects = true; ZeroTradeWithRejectEvals++; }
			}
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			foreach (NQCE_Trade tr in res.Trades) sb.Append(tr.EntryTick.ToString(CultureInfo.InvariantCulture)).Append(tr.Dir == 1 ? "L" : "S").Append(';');
			e.TradeFingerprint = NQCE_Const.Sha256Text(sb.ToString());
			cache[id] = e;
			return e;
		}
	}
}
