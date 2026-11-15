#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
#endregion

//==============================================================================
// NQC_ET3_Ledger - WP5 ledger de ensayos, databank top-K deduplicado, curva de fitness, y bucle GA.
// SOLO DESARROLLO, sin compilar. La salida a disco la hace el llamador (Save*), a runs/<fecha>_ET3_<id>/.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public sealed class NQCE3_Ledger
	{
		private StringBuilder sb = new StringBuilder();
		public int Rows;
		public NQCE3_Ledger() { sb.Append("trial_id\tcampaign\tgeneration\tfitness_t\ttrades\tmean_net\tvalid\tgenome\n"); }
		public void Add(string campaign, int gen, NQCE3_Eval e)
		{
			sb.Append(e.TrialId).Append('\t').Append(campaign).Append('\t').Append(gen.ToString(CultureInfo.InvariantCulture)).Append('\t')
			  .Append(e.Fitness.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\t').Append(e.Trades.ToString(CultureInfo.InvariantCulture)).Append('\t')
			  .Append(e.MeanNet.ToString("0.00", CultureInfo.InvariantCulture)).Append('\t').Append(e.Valid ? "1" : "0").Append('\t').Append(e.GenomeText).Append('\n');
			Rows++;
		}
		public string Text() { return sb.ToString(); }
		public string Sha256() { return NQCE_Const.Sha256Text(sb.ToString()); }
		public void Save(string path) { File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false)); }
	}

	// Databank top-K: solo validos, deduplicado por huella de operaciones (se queda el de menor TrialId en empate).
	public sealed class NQCE3_Databank
	{
		private int k; private Dictionary<string, NQCE3_Eval> byFp = new Dictionary<string, NQCE3_Eval>();
		public NQCE3_Databank(int topK) { k = topK; }
		public void Offer(NQCE3_Eval e)
		{
			if (!e.Valid) return;
			NQCE3_Eval cur;
			if (byFp.TryGetValue(e.TradeFingerprint, out cur))
			{
				if (e.Fitness > cur.Fitness || (e.Fitness == cur.Fitness && string.CompareOrdinal(e.TrialId, cur.TrialId) < 0)) byFp[e.TradeFingerprint] = e;
				return;
			}
			byFp[e.TradeFingerprint] = e;
			if (byFp.Count > 2 * k) Trim();
		}
		private List<NQCE3_Eval> Sorted()
		{
			List<NQCE3_Eval> l = new List<NQCE3_Eval>(byFp.Values);
			l.Sort(delegate(NQCE3_Eval a, NQCE3_Eval b) { int c = b.Fitness.CompareTo(a.Fitness); return c != 0 ? c : string.CompareOrdinal(a.TrialId, b.TrialId); });
			return l;
		}
		private void Trim()
		{
			List<NQCE3_Eval> l = Sorted(); byFp.Clear();
			for (int i = 0; i < l.Count && i < k; i++) byFp[l[i].TradeFingerprint] = l[i];
		}
		public List<NQCE3_Eval> Top() { List<NQCE3_Eval> l = Sorted(); if (l.Count > k) l.RemoveRange(k, l.Count - k); return l; }
		public string Tsv()
		{
			StringBuilder s = new StringBuilder("rank\ttrial_id\tfitness_t\ttrades\tmean_net\tgenome\n"); int r = 1;
			foreach (NQCE3_Eval e in Top())
				s.Append((r++).ToString(CultureInfo.InvariantCulture)).Append('\t').Append(e.TrialId).Append('\t').Append(e.Fitness.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\t')
				 .Append(e.Trades.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(e.MeanNet.ToString("0.00", CultureInfo.InvariantCulture)).Append('\t').Append(e.GenomeText).Append('\n');
			return s.ToString();
		}
	}

	// Bucle de campana. Orden de consumo del azar fijo => misma semilla, mismo resultado.
	public sealed class NQCE3_Campaign
	{
		public string Name = "C1";
		public StringBuilder Curve = new StringBuilder("generation\tbest_t\tmean_t\tvalid_frac\tfresh_evals_cum\n");
		public NQCE3_Ledger Ledger = new NQCE3_Ledger();
		public NQCE3_Databank Bank = new NQCE3_Databank(2000);   // D-019: top-K 2.000 deduplicado (se reconstruye con p.TopK al correr)
		public const int MaxEvaluations = 100000;   // tope duro D-019 (inmigrantes y elite recalculada incluidos en el conteo de evaluaciones nuevas)
		public int MaxEvals = MaxEvaluations;      // por defecto el tope; las pruebas pueden bajarlo
		public Action<int, NQCE3_Campaign> OnGeneration;   // opcional: se llama al cerrar cada generacion (progreso del runner); no afecta al azar ni al ledger

		public void Run(NQCE3_Fitness fit, INQCE3_Library lib, NQCE3_Params p, Func<NQCE3_Genome, bool> minSignal)
		{
			NQCE_Rng r = new NQCE_Rng(p.Seed); Bank = new NQCE3_Databank(p.TopK);
			List<NQCE3_Genome> pop = new List<NQCE3_Genome>();
			for (int i = 0; i < p.PopSize; i++) pop.Add(NQCE3_Operators.InitWithMinSignal(r, lib, p, minSignal));
			for (int gen = 0; gen < p.Generations; gen++)
			{
				double[] f = new double[pop.Count]; double sum = 0; int valid = 0, evaluated = 0; double best = double.NaN;
				for (int i = 0; i < pop.Count; i++)
				{
					int before = fit.FreshEvaluations;
					if (before >= MaxEvals) { f[i] = double.NegativeInfinity; continue; }      // tope duro de evaluaciones
					NQCE3_Eval e = fit.Evaluate(pop[i]);
					if (fit.FreshEvaluations > before) Ledger.Add(Name, gen, e);            // solo evaluaciones nuevas son ensayos
					Bank.Offer(e);
					f[i] = e.Fitness; sum += e.Fitness; evaluated++; if (e.Valid) valid++; if (double.IsNaN(best) || e.Fitness > best) best = e.Fitness;
				}
				Curve.Append(gen.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(double.IsNaN(best) ? "NA" : best.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\t')
				     .Append(evaluated == 0 ? "NA" : (sum / evaluated).ToString("0.0000", CultureInfo.InvariantCulture)).Append('\t').Append(evaluated == 0 ? "NA" : ((double)valid / evaluated).ToString("0.000", CultureInfo.InvariantCulture)).Append('\t')
				     .Append(fit.FreshEvaluations.ToString(CultureInfo.InvariantCulture)).Append('\n');
				if (OnGeneration != null) OnGeneration(gen, this);
				if (fit.FreshEvaluations >= MaxEvals || gen == p.Generations - 1) break;
				pop = NQCE3_Operators.NextGeneration(r, pop, f, p, lib, minSignal);
			}
		}
	}
}
