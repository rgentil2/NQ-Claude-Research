#region Using declarations
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
		public int MonthsCovered; public double TRaw, Top5Share;
		public string TrialId, GenomeText; public double Fitness, MeanNet, StdNet, TotalNet; public int Trades; public bool Valid;
		public bool ZeroTradesWithRejects;   // 0 operaciones y alguna condicion sin senal por calentamiento/volumen/no finito (diagnostico A4)
		public string TradeFingerprint;    // SHA256 de la secuencia (EntryTick, Dir) para deduplicar el databank
		public NQCE3_Genome Genome;        // genoma reparado que produjo el ensayo (copia; no entra en ningun hash). Lo usa el runner para ET4/ET6/ET7
	}

	// Resultado de un genoma dentro de un lote (NQCE3_Fitness.EvaluateBatch). Skipped = omitido por el tope de evaluaciones; Fresh = ensayo nuevo.
	public sealed class NQCE3_BatchItem { public NQCE3_Eval Eval; public bool Fresh, Skipped; }

	// D-039: especificacion de la aptitud y del minimo de operaciones. DEFECTO DEL CODIGO = legado (T, 100, sin cobertura): hashes anteriores intactos.
	// T_SHRUNK (aptitud v2): F = t_adj * sqrt(n/(n+n0)) * conc, con t_adj = mean/(max(sd, sd_floor_k*mean|net|)/sqrt(n)) y conc = min(1, top5_max/share5) (share5 = parte de la ganancia bruta que dan las 5 mejores operaciones).
	public sealed class NQCE3_FitnessSpec
	{
		public int MinTrades = 100, MinMonths = 0; public string Mode = "T"; public double N0 = 250.0, SdFloorK = 0.8, Top5Max = 0.25;
		public bool IsLegacy { get { return Mode == "T" && MinTrades == 100 && MinMonths == 0; } }
		public string Text() { return "mode=" + Mode + " min_trades=" + MinTrades.ToString(CultureInfo.InvariantCulture) + " min_months=" + MinMonths.ToString(CultureInfo.InvariantCulture) + (Mode == "T_SHRUNK" ? " n0=" + N0.ToString("R", CultureInfo.InvariantCulture) + " sd_floor_k=" + SdFloorK.ToString("R", CultureInfo.InvariantCulture) + " top5_max=" + Top5Max.ToString("R", CultureInfo.InvariantCulture) : ""); }
		public string Validate() { if (Mode != "T" && Mode != "T_SHRUNK") return "et3.fitness_mode debe ser T o T_SHRUNK"; if (MinTrades < 2 || MinTrades > 100000) return "et3.min_trades fuera de 2..100000"; if (MinMonths < 0 || MinMonths > 36) return "et3.min_months_covered fuera de 0..36"; if (N0 < 0 || SdFloorK < 0 || Top5Max <= 0 || Top5Max > 1) return "parametros de T_SHRUNK fuera de rango"; return ""; }
		public static NQCE3_FitnessSpec Shrunk() { NQCE3_FitnessSpec f = new NQCE3_FitnessSpec(); f.Mode = "T_SHRUNK"; f.MinTrades = 250; f.MinMonths = 24; return f; }
	}

	public sealed class NQCE3_Fitness
	{
		public const int MinTrades = 100;
		public NQCE3_FitnessSpec Spec = new NQCE3_FitnessSpec();
		public const int IsFromKey = 20200101, IsToKey = 20221231;
		private NQCE_Bars bars; private NQCE_Config cfg; private INQCE3_Library lib;
		private Dictionary<string, NQCE3_Eval> cache = new Dictionary<string, NQCE3_Eval>();
		public int FreshEvaluations;       // evaluaciones nuevas = ensayos del ledger
		public int ZeroTradeEvals;         // evaluaciones nuevas con 0 operaciones
		public int ZeroTradeWithRejectEvals; // de ellas, con condiciones sin senal por calentamiento/volumen/no finito
		public const double InvalidBase = -1000.0;   // fitness de invalidos: InvalidBase + trades/MinTrades (< todo t valido realista)
		public const int Probe0931 = 9 * 60 + 31, MaxPeriodBars = 7 * 30 + 10;
		public NQCE3_Reservoir Reservoir;  // opcional (D-038, aditivo): recibe la serie diaria de cada evaluacion NUEVA; null = sin muestreo
		public List<double> FreshEvalMs;   // opcional (modo TIMING del lanzador, aditivo): milisegundos de cada evaluacion NUEVA; null = sin medir
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

		// ------------------------------------------------------------------ evaluacion (NQCRUN-1.3.0: lote determinista, opcionalmente multihilo)
		// Evaluate(g) conserva la semantica anterior (un genoma, cache de genomas, ensayo nuevo = FreshEvaluations++) y se apoya en el lote.
		public NQCE3_Eval Evaluate(NQCE3_Genome g)
		{
			NQCE3_BatchItem[] r = EvaluateBatch(new NQCE3_Genome[] { g }, int.MaxValue, 1);
			return r[0].Eval;
		}

		private sealed class Job
		{
			public int Index; public NQCE3_Genome G; public NQCE_Member M; public string Id; public NQCE3_Eval E; public double[] Daily; public double Ms; public long W, V, N;
		}

		// Evalua los genomas de una generacion (o de un lote de vecinos) en `threads` hilos y consolida TODO en ORDEN DE INDICE (nunca de llegada):
		//   - decide en el hilo principal, en orden, que genomas son ensayo nuevo (no estan en la cache de genomas ni repiten uno anterior del lote) y cuales se omiten por
		//     el tope de evaluaciones (maxEvals: el primer genoma con FreshEvaluations >= maxEvals y todos los siguientes se omiten, igual que el bucle secuencial);
		//   - evalua los nuevos en paralelo (cada uno con su propio miembro, ejecutor y arreglos de salida; la unica estructura compartida es la cache de numeradores, thread-safe);
		//   - actualiza cache de genomas, contadores, reservorio y milisegundos en orden de indice.
		// El resultado (evaluaciones, ledger, databank, curva, reservorio) es IDENTICO para threads = 1, 2, 8, 24: cada evaluacion es funcion pura de (genoma, barras, cfg).
		public NQCE3_BatchItem[] EvaluateBatch(IList<NQCE3_Genome> genomes, int maxEvals, int threads)
		{
			int n = genomes.Count; NQCE3_BatchItem[] items = new NQCE3_BatchItem[n];
			List<Job> jobs = new List<Job>(); Dictionary<string, int> firstOf = new Dictionary<string, int>(); int[] dupOf = new int[n];
			NQCE3_Library nl = lib as NQCE3_Library; NQCE3_Series ser = nl == null ? null : nl.SeriesFor(bars);
			int freshCount = FreshEvaluations; bool stop = false;
			for (int i = 0; i < n; i++)
			{
				items[i] = new NQCE3_BatchItem(); dupOf[i] = -1;
				if (stop || freshCount >= maxEvals) { stop = true; items[i].Skipped = true; continue; }
				NQCE3_Genome g = genomes[i];
				g.Repair(lib);
				NQCE_Member m = g.ToMember(lib, "x");                    // un solo ToMember por evaluacion (M6)
				string id = NQCE3_Genome.TrialIdOf(lib, m);
				NQCE3_Eval hit;
				if (cache.TryGetValue(id, out hit)) { items[i].Eval = hit; continue; }   // genoma repetido: no es ensayo nuevo
				int fi;
				if (firstOf.TryGetValue(id, out fi)) { dupOf[i] = fi; continue; }       // repite un genoma anterior del mismo lote
				Job j = new Job(); j.Index = i; j.G = g; j.M = m; j.Id = id; firstOf[id] = i; freshCount++;
				j.E = new NQCE3_Eval(); j.E.TrialId = id; j.E.Genome = g.Clone();
				if (m != null)
				{
					m.Id = id.Substring(0, 12);
					bars.Atr(m.AtrPeriod); if (ser != null) ser.PrepareAtr(m.AtrPeriod);   // el diccionario de ATR solo se LEE en paralelo
				}
				jobs.Add(j); items[i].Fresh = true;
			}
			bars.BuildDays();                                              // deja la lista de dias lista antes de los hilos
			NQCE3_Series.MemoReserveEntries = threads > 1 ? 10 * threads + 6 : 0;
			if (threads <= 1 || jobs.Count <= 1) { for (int k = 0; k < jobs.Count; k++) Run(jobs[k]); }
			else
			{
				int next = -1; int nt = Math.Min(threads, jobs.Count); System.Threading.Thread[] th = new System.Threading.Thread[nt]; Exception err = null;
				for (int t = 0; t < nt; t++)
				{
					th[t] = new System.Threading.Thread(delegate()
					{
						try { while (true) { int k = System.Threading.Interlocked.Increment(ref next); if (k >= jobs.Count || err != null) break; Run(jobs[k]); } }
						catch (Exception ex) { err = ex; }
					});
					th[t].IsBackground = true; th[t].Start();
				}
				for (int t = 0; t < nt; t++) th[t].Join();
				if (err != null) throw new InvalidOperationException("ET3: fallo en un hilo de evaluacion: " + err.Message, err);
			}
			// consolidacion en orden de indice
			int ji = 0;
			for (int i = 0; i < n; i++)
			{
				if (items[i].Skipped) continue;
				if (dupOf[i] >= 0) { items[i].Eval = items[dupOf[i]].Eval; continue; }
				if (!items[i].Fresh) continue;
				Job j = jobs[ji++]; NQCE3_Eval e = j.E; items[i].Eval = e;
				FreshEvaluations++;
				if (j.M == null) { ZeroTradeEvals++; }
				else
				{
					if (Reservoir != null) Reservoir.Offer(j.Id, j.Daily);
					if (e.Trades == 0)
					{
						ZeroTradeEvals++;
						if (j.W + j.V + j.N > 0) { e.ZeroTradesWithRejects = true; ZeroTradeWithRejectEvals++; }
					}
					if (nl != null) nl.AddRejects(j.W, j.V, j.N);
				}
				cache[j.Id] = e;
				if (FreshEvalMs != null) FreshEvalMs.Add(j.Ms);
			}
			return items;
		}

		// Aptitud v2 (ver NQCE3_FitnessSpec). Solo se llama con n >= min_trades >= 2.
		private double Shrunk(List<NQCE_Trade> trades, double mean, double sd, NQCE3_Eval e)
		{
			int n = trades.Count; double absSum = 0, gross = 0; double[] top = new double[5]; for (int i = 0; i < 5; i++) top[i] = double.NegativeInfinity;
			foreach (NQCE_Trade t in trades)
			{
				absSum += Math.Abs(t.Net);
				if (t.Net > 0) { gross += t.Net; int m = 0; for (int i = 1; i < 5; i++) if (top[i] < top[m]) m = i; if (t.Net > top[m]) top[m] = t.Net; }
			}
			double s5 = 0; for (int i = 0; i < 5; i++) if (top[i] > 0) s5 += top[i];
			e.Top5Share = gross > 0 ? s5 / gross : 0;
			double sdEff = Math.Max(sd, Spec.SdFloorK * absSum / n); if (sdEff <= 0) return 0;
			double tAdj = mean / (sdEff / Math.Sqrt((double)n));
			double conc = (tAdj > 0 && e.Top5Share > Spec.Top5Max) ? Spec.Top5Max / e.Top5Share : 1.0;
			return tAdj * Math.Sqrt(n / (n + Spec.N0)) * conc;
		}

		// Una evaluacion nueva: funcion pura de (genoma, barras, cfg). Solo escribe en su Job (nada compartido salvo la cache de numeradores).
		private void Run(Job j)
		{
			long t0 = Stopwatch.GetTimestamp();
			NQCE3_Eval e = j.E; NQCE_Member m = j.M;
			if (m == null) { e.GenomeText = "EMPTY"; e.Fitness = InvalidBase; j.Ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency; return; }
			long w0, v0, n0; NQCE3_Library.ThreadRejects(out w0, out v0, out n0);
			e.GenomeText = m.GenomeText();
			NQCE_Portfolio pf = new NQCE_Portfolio(); pf.Name = "ET3"; pf.Members.Add(m);
			NQCE_Result res = NQCE_Executor.Run(bars, pf, cfg, new NQCE_NullHooks());
			j.Daily = res.DailyNet;
			e.Trades = res.Trades.Count;
			double mean, sd; double t = TStat(res.Trades, out mean, out sd);
			e.MeanNet = mean; e.StdNet = sd; foreach (NQCE_Trade tr in res.Trades) e.TotalNet += tr.Net;
			e.TRaw = t;
			HashSet<int> mo = new HashSet<int>(); foreach (NQCE_Trade tr in res.Trades) mo.Add(tr.DayKey / 100); e.MonthsCovered = mo.Count;
			int minT = Spec.MinTrades;
			e.Valid = e.Trades >= minT && e.MonthsCovered >= Spec.MinMonths;
			// invalido: por debajo de todo valido; primero por operaciones (<min), luego por cobertura de meses (A3)
			if (e.Valid)
			{
				if (Spec.Mode == "T_SHRUNK") e.Fitness = Shrunk(res.Trades, mean, sd, e);
				else e.Fitness = t;
			}
			else e.Fitness = e.Trades < minT ? InvalidBase + (double)e.Trades / minT : InvalidBase + 1.0 + (double)e.MonthsCovered / Math.Max(1, Spec.MinMonths);
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			foreach (NQCE_Trade tr in res.Trades) sb.Append(tr.EntryTick.ToString(CultureInfo.InvariantCulture)).Append(tr.Dir == 1 ? "L" : "S").Append(';');
			e.TradeFingerprint = NQCE_Const.Sha256Text(sb.ToString());
			long w1, v1, n1; NQCE3_Library.ThreadRejects(out w1, out v1, out n1); j.W = w1 - w0; j.V = v1 - v0; j.N = n1 - n0;
			j.Ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
		}
	}
}
