#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
#endregion

//==============================================================================
// NQC_ET4_Prune - ET4 poda barata por estrategia (IS). SOLO DESARROLLO: sin ejecutar sobre datos reales (exige ET2 aprobada).
// Contrato NQC 2.0.0, seccion ET4. Biblioteca PURA (sin API de NinjaTrader). Depende de NQC_ET1_Core.cs y NQC_ET3_*.cs
// (genoma, fitness como guardia de datos, biblioteca). No duplica el ejecutor: toda simulacion es NQCE_Executor.Run con un
// portafolio de 1 miembro (igual que ET3); ET4 solo agrega, recorta y reetiqueta el costo.
//
// Filtros en orden de costo creciente, con corte temprano (un filtro fallido impide evaluar los siguientes):
//   F1  neto > 0 con costos 1,5x (suma de neto/operacion). Costo estresado = CostRT*1,5 = 37,02 (EXECUTOR_SPEC §4, estres).
//       El costo es un cargo fijo posterior a la simulacion bruta (§4) y no altera la secuencia de operaciones; por eso el neto
//       1,5x es el neto del ejecutor menos (37,02-24,68) por operacion. La prueba lo contrasta con una corrida completa a 1,5x.
//   F2  neto > 0 (costo 1,0x) en >= 7 de los 12 TRIMESTRES CALENDARIO del IS: 2020Q1..2022Q4. Una operacion pertenece al
//       trimestre de su DayKey de sesion (fecha calendario de la sesion; la ventana 09:31-11:30 nunca cruza dias). Trimestre sin
//       operaciones tiene neto 0, no es > 0 y cuenta como no positivo. Sin minimo de operaciones por trimestre (el minimo de 100
//       es de ET3 y no se cambia aqui).
//   F3  vecindario: cada parametro +-1 paso, un parametro a la vez (ver NQCE4_Neighbors). Vecino "mantiene neto>0" si su neto
//       total a costo 1,0x > 0 (0 operaciones = neto 0 = no mantiene). Se exige  vecinosOK*100 >= 70*vecinosExistentes (entero).
//   F4  informativo: distribucion del stop (|entrada - stop|) en puntos y US$ de las operaciones del candidato; nunca elimina.
//
// Registro: TRIALS_LEDGER. Los candidatos vienen del databank de ET3 y SU evaluacion no es ensayo nuevo (ya esta en el ledger de
// ET3). Cada VECINO es un genoma nuevo: cuenta como ensayo una sola vez por TrialId (si el id ya esta en el ledger de ET3 o ya se
// evaluo en ET4 no es ensayo nuevo, misma regla que NQCE3_Fitness). El acumulado es ET3 + FreshTrials.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public sealed class NQCE4_Candidate
	{
		public string TrialId;           // id de ensayo de ET3 (SHA256); se verifica contra el genoma
		public NQCE3_Genome Genome;
		public string Fingerprint;       // huella de operaciones del databank (informativa)
		public NQCE4_Candidate(string trialId, NQCE3_Genome g, string fingerprint) { TrialId = trialId; Genome = g; Fingerprint = fingerprint; }
	}

	public sealed class NQCE4_Params
	{
		public const double StressMult = 1.5;                  // F1 [B]
		public const int Quarters = 12, MinQuartersPositive = 7;   // F2 [B]
		public const int NeighborPassPercent = 70;             // F3 [B]
		public const int IsFirstYear = 2020;
	}

	public sealed class NQCE4_Neighbor
	{
		public string Label;             // p.ej. "s0.pl-1", "atr+1", "ksl-1"
		public NQCE3_Genome Genome;
	}

	// Rejilla de vecinos +-1 paso. Un vecino fuera de rejilla NO EXISTE: no se repara (reparar = acotar produciria una copia del
	// candidato, que contaria como vecino "estable" y inflaria el cociente) ni se cuenta en el denominador. Los parametros en el
	// borde de su rango tienen un solo vecino. Se perturban: periodo y umbral de cada ranura activa (gen impar, D-030, niveles de la
	// biblioteca), periodo del ATR 5..100, k_sl indice 0..22 (0,5..6,0 paso 0,25) y k_tp indice 1..31 (0,5..8,0 paso 0,25).
	// "Sin objetivo" (k_tp=0) es una opcion categorica, no un punto de la rejilla: no tiene vecinos y tampoco es vecino de k_tp=1.
	// No se perturban condicion (id), ranuras vacias ni direccion: son estructura, no parametros.
	public static class NQCE4_Neighbors
	{
		public static List<NQCE4_Neighbor> Build(NQCE3_Genome g, INQCE3_Library lib)
		{
			List<NQCE4_Neighbor> l = new List<NQCE4_Neighbor>();
			for (int s = 0; s < NQCE3_Genome.Slots; s++)
			{
				int c = g.G[2 * s]; if (c == 0) continue;
				int pl, tl; NQCE3_Genome.Unpack(lib, c, g.G[2 * s + 1], out pl, out tl);
				int pLv = Math.Max(1, lib.PeriodLevels(c)), tLv = Math.Max(1, lib.ThresholdLevels(c));
				for (int d = -1; d <= 1; d += 2)
				{
					if (pl + d >= 0 && pl + d < pLv) Add(l, g, 2 * s + 1, NQCE3_Genome.Pack(lib, c, pl + d, tl), "s" + s.ToString(CultureInfo.InvariantCulture) + ".pl" + Sg(d));
					if (tLv > 1 && tl + d >= 0 && tl + d < tLv) Add(l, g, 2 * s + 1, NQCE3_Genome.Pack(lib, c, pl, tl + d), "s" + s.ToString(CultureInfo.InvariantCulture) + ".thr" + Sg(d));
				}
			}
			for (int d = -1; d <= 1; d += 2)
			{
				int a = g.G[NQCE3_Genome.GAtr] + d; if (a >= NQCE3_Genome.AtrMin && a <= NQCE3_Genome.AtrMax) Add(l, g, NQCE3_Genome.GAtr, a, "atr" + Sg(d));
			}
			for (int d = -1; d <= 1; d += 2)
			{
				int k = g.G[NQCE3_Genome.GKsl] + d; if (k >= 0 && k < NQCE3_Genome.KslLevels) Add(l, g, NQCE3_Genome.GKsl, k, "ksl" + Sg(d));
			}
			if (g.G[NQCE3_Genome.GKtp] >= 1)
				for (int d = -1; d <= 1; d += 2)
				{
					int k = g.G[NQCE3_Genome.GKtp] + d; if (k >= 1 && k < NQCE3_Genome.KtpLevels) Add(l, g, NQCE3_Genome.GKtp, k, "ktp" + Sg(d));
				}
			return l;
		}
		private static string Sg(int d) { return d < 0 ? "-1" : "+1"; }
		private static void Add(List<NQCE4_Neighbor> l, NQCE3_Genome g, int gene, int value, string label)
		{
			NQCE4_Neighbor n = new NQCE4_Neighbor(); n.Genome = g.Clone(); n.Genome.G[gene] = value; n.Label = label; l.Add(n);
		}
	}

	public sealed class NQCE4_Outcome
	{
		public string TrialId, Status = "PASS", Cause = "";   // Status: PASS | F1 | F2 | F3
		public int Trades; public double Net1, Net15;
		public bool F1Done, F2Done, F3Done;
		public double[] QNet = new double[NQCE4_Params.Quarters]; public int[] QTrades = new int[NQCE4_Params.Quarters]; public int QPos;
		public int NbTotal, NbPass;
		public double[] StopPts = new double[0];   // F4: |entrada - stop| por operacion, ordenado
	}

	public sealed class NQCE4_Prune
	{
		public const string Version = "ET4PRUNE-1.0.0";
		private NQCE_Bars bars; private NQCE_Config cfg; private INQCE3_Library lib;
		private HashSet<string> known;                                  // ids de ensayo ya registrados (ledger ET3 + vecinos ya evaluados)
		private Dictionary<string, double[]> nbCache = new Dictionary<string, double[]>();   // id -> {operaciones, neto}
		public int FreshTrials;                                         // vecinos nuevos = ensayos que ET4 agrega al TRIALS_LEDGER
		public int NeighborEvaluations;                                 // vecinos evaluados (incluye ya conocidos), solo informativo
		public int ExecutorRuns;                                        // corridas del ejecutor (candidatos + vecinos nuevos en ET4)
		public List<NQCE4_Outcome> Outcomes = new List<NQCE4_Outcome>();
		private StringBuilder ledger = new StringBuilder("trial_id\tparent_id\tstage\tperturbation\tfresh\ttrades\ttotal_net\n");

		// isBars/volume: barras M1 del IS (2020-01-01..2022-12-31), volumen alineado; cfg: CostMult=1,0 y sin capa protectora.
		// knownTrialIds: columna trial_id del ledger de ET3 (NQCE4_Prune.LoadLedgerIds) o null.
		public NQCE4_Prune(NQCE_Bars isBars, double[] volume, NQCE_Config config, INQCE3_Library library, IEnumerable<string> knownTrialIds)
		{
			if (config.CostMult != 1.0 || config.Protect) throw new ArgumentException("ET4: cfg debe ser costo 1,0x y sin capa protectora (el estres 1,5x lo aplica ET4)");
			NQCE3_Fitness guard = new NQCE3_Fitness(isBars, volume, config, library);   // misma guardia de datos de ET3 (IS, M1 completas, volumen) + Bind de la biblioteca
			if (guard == null) throw new InvalidOperationException("ET4: guardia de datos");
			bars = isBars; cfg = config; lib = library;
			known = new HashSet<string>(StringComparer.Ordinal);
			if (knownTrialIds != null) foreach (string s in knownTrialIds) known.Add(s);
		}

		public static List<string> LoadLedgerIds(string ledgerTsv)
		{
			List<string> ids = new List<string>(); string[] lines = ledgerTsv.Split('\n');
			for (int i = 1; i < lines.Length; i++) { int t = lines[i].IndexOf('\t'); if (t > 0) ids.Add(lines[i].Substring(0, t)); }
			return ids;
		}

		public static int QuarterIndex(int dayKey)
		{
			int y = dayKey / 10000, m = (dayKey / 100) % 100;
			int q = (y - NQCE4_Params.IsFirstYear) * 4 + (m - 1) / 3;
			if (q < 0 || q >= NQCE4_Params.Quarters) throw new InvalidOperationException("ET4: DayKey fuera del IS 2020Q1..2022Q4: " + dayKey.ToString(CultureInfo.InvariantCulture));
			return q;
		}
		public static bool PassesF2(int quartersPositive) { return quartersPositive >= NQCE4_Params.MinQuartersPositive; }
		public static bool PassesF3(int ok, int total) { return total > 0 && ok * 100 >= NQCE4_Params.NeighborPassPercent * total; }

		private NQCE_Result Run(NQCE3_Genome g, string id)
		{
			NQCE_Member m = g.ToMember(lib, id.Substring(0, 12));
			if (m == null) return new NQCE_Result();
			NQCE_Portfolio pf = new NQCE_Portfolio(); pf.Name = "ET4"; pf.Members.Add(m);
			ExecutorRuns++;
			return NQCE_Executor.Run(bars, pf, cfg, new NQCE_NullHooks());
		}

		public List<NQCE4_Outcome> Evaluate(List<NQCE4_Candidate> cands)
		{
			foreach (NQCE4_Candidate c in cands)
			{
				c.Genome.Repair(lib);
				string id = c.Genome.TrialId(lib);
				if (id != c.TrialId) throw new ArgumentException("ET4: el TrialId del candidato no coincide con su genoma (" + c.TrialId + " vs " + id + ")");
				known.Add(id);                                           // ya esta en el ledger de ET3
				Outcomes.Add(EvaluateOne(c, id));
			}
			return Outcomes;
		}

		private NQCE4_Outcome EvaluateOne(NQCE4_Candidate c, string id)
		{
			NQCE4_Outcome o = new NQCE4_Outcome(); o.TrialId = id;
			NQCE_Result res = Run(c.Genome, id);
			o.Trades = res.Trades.Count;
			double delta = cfg.CostRT * NQCE4_Params.StressMult - cfg.Cost();       // costo adicional del estres por operacion
			List<double> sp = new List<double>();
			foreach (NQCE_Trade t in res.Trades)
			{
				o.Net1 += t.Net; o.Net15 += t.Net - delta;
				int q = QuarterIndex(t.DayKey); o.QNet[q] += t.Net; o.QTrades[q]++;
				sp.Add(Math.Abs(t.EntryPx - t.Stop));                                // F4 informativo (misma definicion que NQCE_Audit.TradesTsv)
			}
			sp.Sort(); o.StopPts = sp.ToArray();
			for (int q = 0; q < NQCE4_Params.Quarters; q++) if (o.QNet[q] > 0) o.QPos++;
			// F1
			o.F1Done = true;
			if (!(o.Net15 > 0)) { o.Status = "F1"; o.Cause = "neto 1,5x = " + F2(o.Net15) + " <= 0 (" + I(o.Trades) + " operaciones)"; return o; }
			// F2
			o.F2Done = true;
			if (!PassesF2(o.QPos)) { o.Status = "F2"; o.Cause = "trimestres positivos " + I(o.QPos) + "/12 < " + I(NQCE4_Params.MinQuartersPositive); return o; }
			// F3
			o.F3Done = true;
			List<NQCE4_Neighbor> nb = NQCE4_Neighbors.Build(c.Genome, lib);
			foreach (NQCE4_Neighbor n in nb)
			{
				n.Genome.Repair(lib);                                                // no-op en vecinos validos; defensivo
				string nid = n.Genome.TrialId(lib); double[] r; NeighborEvaluations++;
				bool fresh = !known.Contains(nid);
				if (!nbCache.TryGetValue(nid, out r))
				{
					NQCE_Result nr = Run(n.Genome, nid); double net = 0; foreach (NQCE_Trade t in nr.Trades) net += t.Net;
					r = new double[] { nr.Trades.Count, net }; nbCache[nid] = r;
				}
				if (fresh) { known.Add(nid); FreshTrials++; }
				ledger.Append(nid).Append('\t').Append(id).Append('\t').Append("ET4-F3").Append('\t').Append(n.Label).Append('\t').Append(fresh ? "1" : "0").Append('\t')
				      .Append(I((int)r[0])).Append('\t').Append(F2(r[1])).Append('\n');
				o.NbTotal++; if (r[1] > 0) o.NbPass++;
			}
			if (!PassesF3(o.NbPass, o.NbTotal)) { o.Status = "F3"; o.Cause = "vecinos con neto>0 " + I(o.NbPass) + "/" + I(o.NbTotal) + " < 70%"; }
			return o;
		}

		// ---------------------------------------------------------------- salidas
		private static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }
		private static string F2(double v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }
		private static double Pct(double[] sorted, double p)    // rango mas cercano, sorted ascendente no vacio
		{
			int k = (int)Math.Ceiling(p * sorted.Length); if (k < 1) k = 1; if (k > sorted.Length) k = sorted.Length; return sorted[k - 1];
		}

		public string LedgerTsv() { return ledger.ToString(); }
		public string LedgerSha256() { return NQCE_Const.Sha256Text(ledger.ToString()); }

		// Un renglon por candidato (todos), con causa y valores de F1/F2/F3 y F4. "NA" = filtro no evaluado por corte temprano.
		public string RejectsTsv()
		{
			StringBuilder s = new StringBuilder();
			s.Append("trial_id\tstatus\tcause\ttrades\tnet_1x\tnet_1_5x\tq_positive\tq_net\tq_trades\tnb_total\tnb_pass\tnb_ratio\t")
			 .Append("stop_pts_min\tstop_pts_p25\tstop_pts_med\tstop_pts_p75\tstop_pts_max\tstop_pts_mean\tstop_usd_min\tstop_usd_med\tstop_usd_max\tstop_usd_mean\n");
			foreach (NQCE4_Outcome o in Outcomes)
			{
				s.Append(o.TrialId).Append('\t').Append(o.Status).Append('\t').Append(o.Cause).Append('\t').Append(I(o.Trades)).Append('\t').Append(F2(o.Net1)).Append('\t').Append(F2(o.Net15)).Append('\t');
				if (o.F2Done)
				{
					s.Append(I(o.QPos)).Append('\t');
					for (int q = 0; q < NQCE4_Params.Quarters; q++) { if (q > 0) s.Append(','); s.Append(F2(o.QNet[q])); }
					s.Append('\t');
					for (int q = 0; q < NQCE4_Params.Quarters; q++) { if (q > 0) s.Append(','); s.Append(I(o.QTrades[q])); }
					s.Append('\t');
				}
				else s.Append("NA\tNA\tNA\t");
				if (o.F3Done) s.Append(I(o.NbTotal)).Append('\t').Append(I(o.NbPass)).Append('\t').Append(o.NbTotal == 0 ? "NA" : ((double)o.NbPass / o.NbTotal).ToString("0.000", CultureInfo.InvariantCulture)).Append('\t');
				else s.Append("NA\tNA\tNA\t");
				double[] a = o.StopPts;
				if (a.Length == 0) s.Append("NA\tNA\tNA\tNA\tNA\tNA\tNA\tNA\tNA\tNA");
				else
				{
					double mean = 0; foreach (double v in a) mean += v; mean /= a.Length;
					s.Append(F2(a[0])).Append('\t').Append(F2(Pct(a, 0.25))).Append('\t').Append(F2(Pct(a, 0.50))).Append('\t').Append(F2(Pct(a, 0.75))).Append('\t').Append(F2(a[a.Length - 1])).Append('\t').Append(F2(mean)).Append('\t')
					 .Append(F2(a[0] * NQCE_Const.PointValue)).Append('\t').Append(F2(Pct(a, 0.50) * NQCE_Const.PointValue)).Append('\t').Append(F2(a[a.Length - 1] * NQCE_Const.PointValue)).Append('\t').Append(F2(mean * NQCE_Const.PointValue));
				}
				s.Append('\n');
			}
			return s.ToString();
		}
		public string RejectsSha256() { return NQCE_Const.Sha256Text(RejectsTsv()); }

		// Pool podado: candidatos PASS, en el orden de entrada (orden del databank de ET3).
		public string PoolTsv(List<NQCE4_Candidate> cands)
		{
			Dictionary<string, NQCE4_Candidate> by = new Dictionary<string, NQCE4_Candidate>();
			foreach (NQCE4_Candidate c in cands) by[c.TrialId] = c;
			StringBuilder s = new StringBuilder("order\ttrial_id\tfingerprint\tnet_1_5x\tq_positive\tnb_pass\tnb_total\tgenome\n"); int k = 1;
			foreach (NQCE4_Outcome o in Outcomes)
			{
				if (o.Status != "PASS") continue;
				NQCE4_Candidate c = by[o.TrialId];
				NQCE_Member m = c.Genome.ToMember(lib, "x");
				s.Append(I(k++)).Append('\t').Append(o.TrialId).Append('\t').Append(c.Fingerprint).Append('\t').Append(F2(o.Net15)).Append('\t').Append(I(o.QPos)).Append('\t')
				 .Append(I(o.NbPass)).Append('\t').Append(I(o.NbTotal)).Append('\t').Append(m.GenomeText()).Append('\n');
			}
			return s.ToString();
		}
		public string PoolSha256(List<NQCE4_Candidate> cands) { return NQCE_Const.Sha256Text(PoolTsv(cands)); }

		public int Count(string status) { int n = 0; foreach (NQCE4_Outcome o in Outcomes) if (o.Status == status) n++; return n; }

		public string Summary(List<NQCE4_Candidate> cands, int et3TrialsBefore)
		{
			StringBuilder s = new StringBuilder();
			s.Append("ET4 poda ").Append(Version).Append(" | contrato ").Append(NQCE_Const.ContractVersion).Append(" | nucleo ").Append(NQCE_Const.CoreVersion).Append(" | biblioteca ").Append(lib.Version).Append('\n');
			s.Append("candidatos=").Append(I(Outcomes.Count)).Append(" F1_rechazados=").Append(I(Count("F1"))).Append(" F2_rechazados=").Append(I(Count("F2")))
			 .Append(" F3_rechazados=").Append(I(Count("F3"))).Append(" pool=").Append(I(Count("PASS"))).Append('\n');
			s.Append("ensayos_nuevos_ET4=").Append(I(FreshTrials)).Append(" vecinos_evaluados=").Append(I(NeighborEvaluations)).Append(" corridas_ejecutor=").Append(I(ExecutorRuns))
			 .Append(" TRIALS_LEDGER_acumulado=").Append(I(et3TrialsBefore + FreshTrials)).Append(" (ET3 antes=").Append(I(et3TrialsBefore)).Append(")\n");
			s.Append("pool_sha256=").Append(PoolSha256(cands)).Append('\n').Append("rechazos_sha256=").Append(RejectsSha256()).Append('\n').Append("ledger_sha256=").Append(LedgerSha256()).Append('\n');
			return s.ToString();
		}

		public void Save(string dir, List<NQCE4_Candidate> cands, int et3TrialsBefore)
		{
			Directory.CreateDirectory(dir); UTF8Encoding u = new UTF8Encoding(false);
			File.WriteAllText(Path.Combine(dir, "ET4_POOL.tsv"), PoolTsv(cands), u);
			File.WriteAllText(Path.Combine(dir, "ET4_REJECTS.tsv"), RejectsTsv(), u);
			File.WriteAllText(Path.Combine(dir, "ET4_LEDGER.tsv"), LedgerTsv(), u);
			File.WriteAllText(Path.Combine(dir, "SUMMARY.txt"), Summary(cands, et3TrialsBefore), u);
		}
	}
}
