using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

//==============================================================================
// NQC_ET6_Portfolio - Portafolios con el ejecutor central en el bucle (ET6). Contrato NQC 2.0.0. SOLO DESARROLLO.
// Biblioteca PURA (sin API de NinjaTrader, sin ordenes). Depende de NQC_ET1_Core.cs, NQC_ET1C_AccountSim.cs, NQC_ET3_Genome.cs y NQC_ET3_Fitness.cs (TStat).
// Entrada: pool robusto de ET5 (genomas + fitness IS de ET3). Salida: hasta 5 finalistas con PORTFOLIO_SPEC.json, TSV de rechazos, TSV de ensayos.
//
// DECISIONES DE DISENO (ver docs/stages/ET6.md; INTERPRETACION pendiente de confirmacion del usuario, D-019):
//  D6-1 PRIORIDAD (D-016): el orden de los miembros del portafolio es el orden del pool por fitness IS descendente (empate: TrialId ascendente).
//       Se fija al construir y se congela en el spec. El ejecutor resuelve conflictos (misma barra / posicion abierta) por ese orden.
//  D6-2 ACOTAR EL BEAM: solo los MaxPool (30) mejores del pool por fitness son candidatos; ancho de beam W (10); profundidad = MaxMembers (5).
//       Nivel 1: todos los candidatos solos; nivel s>=2: cada estado del beam (conjunto) se extiende con cualquier candidato no incluido.
//       Evaluaciones maximas ~ K + (MaxMembers-1)*W*K (K=30, W=10: 1.230). El conjunto se representa ordenado por rango de prioridad.
//  D6-3 RESTRICCIONES DURAS (D-022): Stability y Symmetry se exigen (estrictamente > umbral) a TODA combinacion evaluada con >= MinMembers (3) miembros.
//       Si falla, se descarta: no entra al beam (no se expande) y queda en el TSV de rechazos. Los parciales de 1-2 miembros NO son portafolios y no se
//       filtran por ellas (un unico miembro largo tiene Symmetry 0 por definicion); sirven de semilla del beam. Lectura alternativa a revisar por el orquestador.
//  D6-4 PUNTUACION DEL BEAM por nivel: (menos puertas falladas, mas t de la expectativa neta por operacion del portafolio ejecutado [misma metrica que D-016], desempate
//       por hash(semilla|clave)). Estados con la misma huella de operaciones ejecutadas (portafolios equivalentes, p. ej. un miembro que nunca ejecuta) se deduplican.
//  D6-5 EVALUACION PEREZOSA: el ejecutor corre a 1x costo para todas; la corrida a 1,5x y P_pass solo si las demas puertas pasan (si no, "NE" en el TSV).
//  D6-6 FINALISTAS: de las combinaciones ACEPTADAS (todas las puertas) con 3..5 miembros: puntuacion = t del portafolio (D-016), luego P_pass desc, menos miembros, hash.
//       Seleccion voraz con diversidad: se salta una combinacion si su Jaccard de miembros con un finalista ya elegido > MaxJaccard (0,5) o si su huella de operaciones es identica.
//  D6-7 ENSAYOS: cada combinacion UNICA evaluada con el ejecutor (cualquier tamano, incluidos los parciales) es un ensayo (cache por clave; reevaluar no cuenta).
//       Las corridas extra a 1,5x no son ensayos aparte. Ledger acumulado = PriorTrials (ET3+ET4...) + ensayos de esta corrida; se desglosa por tamano.
//  D6-8 P_PASS: NQC_AccountSim.Rolling sobre las sesiones del ejecutor (RAPID_INTRADAY_50K, arranques rodantes, censurados excluidos). El umbral lo fija ET2: PpassMin = NaN por
//       defecto => la puerta NO se puede pasar (falla cerrada, "umbral no fijado"). Ningun otro valor numerico del perfil esta fijo en el codigo salvo los del contrato como DEFECTO.
//  D6-9 Mes sin operaciones cuenta como 0 (los meses se toman de las sesiones no excluidas del ejecutor). Peor perdida diaria = -min(DailyNet) (cierre de sesion).
//  D6-11 DD M2M: medicion propia por hook (ver NQCE6_M2mHooks); el DdM2m de NQCE_Executor cuenta doble el cierre de sesion (defecto de ET1 reportado).
//  D6-10 IS solamente: el constructor lanza excepcion si hay barras fuera de 2020-01-01..2022-12-31.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public sealed class NQCE6_PoolEntry
	{
		public string TrialId; public NQCE3_Genome Genome; public double Fitness;
		public NQCE6_PoolEntry(string trialId, NQCE3_Genome g, double fitness) { TrialId = trialId; Genome = g; Fitness = fitness; }
	}

	public sealed class NQCE6_Params
	{
		public const string SchemaVersion = "NQC_PORTFOLIO_SPEC/1";
		// busqueda
		public int MaxPool = 30, BeamWidth = 10, MinMembers = 3, MaxMembers = 5, Finalists = 5;
		public long Seed = 0; public double MaxJaccard = 0.5;
		// perfil de aceptacion: DEFECTOS del contrato (ET6); todo se lee de aqui
		public double PfMin = 1.25, StabilityMin = 30.0, SymmetryMin = 30.0, DdMax = 2000.0, WorstDayMax = 1000.0, StressMult = 1.5;
		public int MinTrades = 150, MinTradesPerMonth = 5;
		public double PpassMin = double.NaN;     // lo fija ET2; NaN = no fijado (la puerta falla cerrada)
		public NQC_AccountProfile Account = NQC_AccountProfile.RapidIntraday50K();
		// trazabilidad
		public string Campaign = "C1", EngineHash = "", LibraryHash = ""; public int PriorTrials = 0;
			// D-039: pool equilibrado largos/cortos (clave del runner: et6.pool_balance). false = comportamiento y hashes anteriores.
			public bool PoolBalance = false;
	}

	public sealed class NQCE6_Eval
	{
		public string Key, TrialId, Fingerprint; public int[] Ranks; public int Size;
		public int Trades, MinMonthCount, MinMonth; public double Net, Pf, Stability, Symmetry, DdClosed, DdM2m, DdM2mCore, WorstDay, TStat, StressNet = double.NaN, Ppass = double.NaN;
		public int PpassDecided; public bool StressRun;
		public List<string> Failed = new List<string>();   // "puerta=valor(umbral)"
		public string FirstFail = ""; public bool HardFail, AllPass; public double TieBreak;
		public string TieKey;
		public int NFailed { get { return Failed.Count; } }
	}

	// DD M2M M1 propio (EXECUTOR_SPEC: pico a valle de la equity marcada al cierre de cada barra M1, posicion abierta incluida).
	// NQCE_Executor.DdM2m suma dos veces el P&L ya cerrado de la sesion (cumReal se actualiza al cerrar y eq=sessReal tambien lo incluye): defecto reportado al orquestador (ET1).
	// ET6 usa esta medicion corregida (acumulado al inicio de la sesion + equity de sesion) y registra el valor del nucleo como informativo.
	public sealed class NQCE6_M2mHooks : NQCE_NullHooks
	{
		public double Dd, CumStart, Peak;
		public override void OnBarMark(int dayKey, long tick, double sessionEquity, bool positionOpen)
		{
			double m = CumStart + sessionEquity; if (m > Peak) Peak = m; if (Peak - m > Dd) Dd = Peak - m;
		}
		public override void OnSessionEnd(NQCE_SessionOut s) { CumStart += s.Row.Pnl; }
	}

	public sealed class NQCE6_Finalist { public int Rank; public NQCE6_Eval Eval; public string SpecJson, SpecHash; }

	public sealed class NQCE6_Result
	{
		public List<NQCE6_Finalist> Finalists = new List<NQCE6_Finalist>();
		public string RejectionsTsv, TrialsTsv, Summary, SummaryHash;
		public int Trials, ExecutorRuns, Accepted, HardDiscarded, PoolUsed; public int[] TrialsBySize = new int[6];
		// diagnostico D-039: fuera de Summary/SummaryHash (los hashes previos con PoolBalance=false se conservan); va en Diagnostics y se anexa a SUMMARY.txt
		public int InputLong, InputShort, PoolLong, PoolShort, SeedPairs;
		public SortedDictionary<string, int> GateFail = new SortedDictionary<string, int>(), GateOnly = new SortedDictionary<string, int>();
		public int Eval3to5, CompAllLong, CompAllShort, CompMixed, SymFailAllLong, SymFailAllShort, SymFailMixed, AcceptedMixed;
		public string Diagnostics;
		public int CumulativeTrials;
	}

	public sealed class NQCE6_Builder
	{
		private NQCE_Bars bars; private NQCE_Config cfg; private INQCE3_Library lib; private NQCE6_Params p;
		private List<NQCE6_PoolEntry> pool = new List<NQCE6_PoolEntry>(); private List<NQCE_Member> members = new List<NQCE_Member>();
		private Dictionary<string, NQCE6_Eval> cache = new Dictionary<string, NQCE6_Eval>();
		private List<NQCE6_Eval> order = new List<NQCE6_Eval>();     // en orden de evaluacion (primera vez)
		public int ExecutorRuns; private int inLong, inShort, seedPairs; private bool[] isLong;
		public const int IsFromKey = 20200101, IsToKey = 20221231;

		static string F(double v) { return double.IsNaN(v) ? "NA" : (double.IsPositiveInfinity(v) ? "INF" : v.ToString("R", CultureInfo.InvariantCulture)); }
		static string I(long v) { return v.ToString(CultureInfo.InvariantCulture); }

		public int PoolCount { get { return pool.Count; } }
		public NQCE6_PoolEntry Entry(int rank) { return pool[rank]; }

		public NQCE6_Builder(NQCE_Bars isBars, double[] volume, NQCE_Config config, INQCE3_Library library, IList<NQCE6_PoolEntry> input, NQCE6_Params parameters)
		{
			for (int i = 0; i < isBars.Count; i++)
				if (isBars.DayKey[i] < IsFromKey || isBars.DayKey[i] > IsToKey)
					throw new InvalidOperationException("ET6: barras fuera del IS 2020-2022 (DayKey=" + I(isBars.DayKey[i]) + ")");
			if (library is NQCE3_Library)
			{
				if (volume == null || volume.Length != isBars.Count) throw new ArgumentException("ET6: se exige volumen alineado con las barras (Bind) para la biblioteca D-018");
				((NQCE3_Library)library).Bind(isBars, volume);
			}
			bars = isBars; cfg = config; lib = library; p = parameters;
			if (p.MinMembers < 1 || p.MaxMembers < p.MinMembers || p.MaxMembers > 5 || p.BeamWidth < 1 || p.MaxPool < 1) throw new ArgumentException("ET6: parametros de busqueda invalidos");
			// pool: sin duplicados, por fitness desc y TrialId asc (D6-1), recortado a MaxPool (D6-2)
			List<NQCE6_PoolEntry> tmp = new List<NQCE6_PoolEntry>(); HashSet<string> seen = new HashSet<string>();
			foreach (NQCE6_PoolEntry e in input)
			{
				NQCE3_Genome g = e.Genome.Clone(); g.Repair(lib);
				string id = e.TrialId != null && e.TrialId.Length > 0 ? e.TrialId : g.TrialId(lib);
				if (!seen.Add(id)) continue;
				tmp.Add(new NQCE6_PoolEntry(id, g, e.Fitness));
			}
			tmp.Sort(delegate(NQCE6_PoolEntry a, NQCE6_PoolEntry b)
			{ int c = b.Fitness.CompareTo(a.Fitness); return c != 0 ? c : string.CompareOrdinal(a.TrialId, b.TrialId); });
			foreach (NQCE6_PoolEntry e in tmp) { if (e.Genome.G[NQCE3_Genome.GDir] == 1) inLong++; else inShort++; }
			if (p.PoolBalance && tmp.Count > p.MaxPool)
			{
				// D-039: hasta MaxPool/2 mejores por aptitud de cada direccion; si un lado tiene menos, completa con los mejores restantes del otro (desequilibrio reportado)
				List<NQCE6_PoolEntry> lg = new List<NQCE6_PoolEntry>(), sh = new List<NQCE6_PoolEntry>();
				foreach (NQCE6_PoolEntry e in tmp) { if (e.Genome.G[NQCE3_Genome.GDir] == 1) lg.Add(e); else sh.Add(e); }
				int half = p.MaxPool / 2, nl = Math.Min(lg.Count, half), ns = Math.Min(sh.Count, half);
				List<NQCE6_PoolEntry> pick = new List<NQCE6_PoolEntry>();
				pick.AddRange(lg.GetRange(0, nl)); pick.AddRange(sh.GetRange(0, ns));
				List<NQCE6_PoolEntry> extra = new List<NQCE6_PoolEntry>(); extra.AddRange(lg.GetRange(nl, lg.Count - nl)); extra.AddRange(sh.GetRange(ns, sh.Count - ns));
				Comparison<NQCE6_PoolEntry> byFit = delegate(NQCE6_PoolEntry a, NQCE6_PoolEntry b) { int c = b.Fitness.CompareTo(a.Fitness); return c != 0 ? c : string.CompareOrdinal(a.TrialId, b.TrialId); };
				extra.Sort(byFit);
				int need = Math.Min(p.MaxPool - pick.Count, extra.Count);
				pick.AddRange(extra.GetRange(0, need)); pick.Sort(byFit);
				tmp = pick;
			}
			else if (tmp.Count > p.MaxPool) tmp.RemoveRange(p.MaxPool, tmp.Count - p.MaxPool);
			pool = tmp;
			isLong = new bool[pool.Count]; for (int i = 0; i < pool.Count; i++) isLong[i] = pool[i].Genome.G[NQCE3_Genome.GDir] == 1;
			foreach (NQCE6_PoolEntry e in pool)
			{
				NQCE_Member m = e.Genome.ToMember(lib, e.TrialId.Substring(0, 12));
				if (m == null) throw new ArgumentException("ET6: genoma sin condiciones en el pool (" + e.TrialId + ")");
				members.Add(m);
			}
		}

		static NQCE_Config Copy(NQCE_Config c, double mult)
		{
			NQCE_Config n = new NQCE_Config();
			n.CostRT = c.CostRT; n.CostMult = mult; n.Protect = c.Protect; n.PauseLevel = c.PauseLevel; n.CloseLevel = c.CloseLevel; n.Cushion = c.Cushion;
			n.PermanentDiagnostic = c.PermanentDiagnostic; n.Profile = c.Profile; n.News = c.News; n.Excluded = c.Excluded; n.Seed = c.Seed;
			return n;
		}

		static string KeyOf(int[] ranks) { StringBuilder sb = new StringBuilder(); for (int i = 0; i < ranks.Length; i++) { if (i > 0) sb.Append(','); sb.Append(ranks[i]); } return sb.ToString(); }

		NQCE_Portfolio BuildPf(int[] ranks)
		{
			NQCE_Portfolio pf = new NQCE_Portfolio(); pf.Name = "ET6";
			foreach (int r in ranks) pf.Members.Add(members[r]);   // orden = prioridad (rango por fitness)
			return pf;
		}

		public static double ProfitFactor(List<NQCE_Trade> trades)
		{
			double g = 0, l = 0; foreach (NQCE_Trade t in trades) { if (t.Net > 0) g += t.Net; else l -= t.Net; }
			if (l <= 0) return g > 0 ? double.PositiveInfinity : 0.0;
			return g / l;
		}

		// Evalua UNA combinacion (rangos ascendentes) con el ejecutor central. Cacheada: reevaluar no es ensayo nuevo.
		public NQCE6_Eval Evaluate(int[] ranksIn)
		{
			int[] ranks = (int[])ranksIn.Clone(); Array.Sort(ranks);
			string key = KeyOf(ranks);
			NQCE6_Eval hit; if (cache.TryGetValue(key, out hit)) return hit;
			NQCE6_Eval e = new NQCE6_Eval(); e.Key = key; e.Ranks = ranks; e.Size = ranks.Length;
			StringBuilder idt = new StringBuilder(); idt.Append("lib=").Append(lib.Version).Append("|pf=");
			foreach (int r in ranks) idt.Append(pool[r].TrialId).Append(',');
			e.TrialId = NQCE_Const.Sha256Text(idt.ToString());
			e.TieKey = NQCE_Const.Sha256Text(I(p.Seed) + "|" + key);

			NQCE6_M2mHooks hk = new NQCE6_M2mHooks();
			NQCE_Result res = NQCE_Executor.Run(bars, BuildPf(ranks), Copy(cfg, 1.0), hk); ExecutorRuns++;
			e.Trades = res.Trades.Count;
			foreach (NQCE_Trade t in res.Trades) e.Net += t.Net;
			e.Pf = ProfitFactor(res.Trades);
			e.Stability = NQCE_Metrics.Stability(res.DailyNet); e.Symmetry = NQCE_Metrics.Symmetry(res.Trades);
			e.DdClosed = res.DdClosed; e.DdM2m = hk.Dd; e.DdM2mCore = res.DdM2m;
			double mn = 0; foreach (double d in res.DailyNet) if (d < mn) mn = d; e.WorstDay = -mn;
			double mean, sd; e.TStat = NQCE3_Fitness.TStat(res.Trades, out mean, out sd);
			// operaciones por mes (meses de las sesiones no excluidas; mes sin operaciones = 0)
			SortedDictionary<int, int> months = new SortedDictionary<int, int>();
			foreach (NQCE_SessionOut s in res.Sessions) months[s.DayKey / 100] = 0;
			foreach (NQCE_Trade t in res.Trades) { int mk = t.DayKey / 100; if (months.ContainsKey(mk)) months[mk]++; }
			e.MinMonthCount = int.MaxValue; e.MinMonth = 0;
			foreach (KeyValuePair<int, int> kv in months) if (kv.Value < e.MinMonthCount) { e.MinMonthCount = kv.Value; e.MinMonth = kv.Key; }
			if (months.Count == 0) e.MinMonthCount = 0;
			StringBuilder fp = new StringBuilder();
			foreach (NQCE_Trade t in res.Trades) fp.Append(I(t.EntryTick)).Append(t.Dir == 1 ? "L" : "S").Append(t.Member).Append(';');
			e.Fingerprint = NQCE_Const.Sha256Text(fp.ToString());

			// puertas en orden de costo creciente
			if (e.Trades < p.MinTrades) Fail(e, "operaciones", I(e.Trades), ">=" + I(p.MinTrades));
			if (!(e.Pf >= p.PfMin)) Fail(e, "PF_neto", F(e.Pf), ">=" + F(p.PfMin));
			if (!(e.Stability > p.StabilityMin)) { Fail(e, "Stability", F(e.Stability), ">" + F(p.StabilityMin)); if (e.Size >= p.MinMembers) e.HardFail = true; }
			if (!(e.Symmetry > p.SymmetryMin)) { Fail(e, "Symmetry", F(e.Symmetry), ">" + F(p.SymmetryMin)); if (e.Size >= p.MinMembers) e.HardFail = true; }
			if (e.DdClosed > p.DdMax) Fail(e, "DD_cerrado", F(e.DdClosed), "<=" + F(p.DdMax));
			if (e.DdM2m > p.DdMax) Fail(e, "DD_M2M", F(e.DdM2m), "<=" + F(p.DdMax));
			if (e.WorstDay > p.WorstDayMax) Fail(e, "peor_dia", F(e.WorstDay), "<=" + F(p.WorstDayMax));
			if (e.MinMonthCount < p.MinTradesPerMonth) Fail(e, "ops_por_mes", I(e.MinMonthCount) + "@" + I(e.MinMonth), ">=" + I(p.MinTradesPerMonth));
			if (e.Failed.Count == 0)
			{
				// puertas caras, solo si el resto pasa (D6-5)
				NQCE_Result rs = NQCE_Executor.Run(bars, BuildPf(ranks), Copy(cfg, p.StressMult), new NQCE_NullHooks()); ExecutorRuns++; e.StressRun = true; e.StressNet = 0;
				foreach (NQCE_Trade t in rs.Trades) e.StressNet += t.Net;
								if (!(e.StressNet > 0)) Fail(e, "neto_costo_x" + F(p.StressMult), F(e.StressNet), ">0");
				int dec; e.Ppass = NQC_AccountSim.Rolling(res.Rows(), p.Account, out dec); e.PpassDecided = dec;
				if (double.IsNaN(p.PpassMin)) Fail(e, "P_pass", F(e.Ppass), "umbral_ET2_no_fijado");
				else if (!(e.Ppass >= p.PpassMin)) Fail(e, "P_pass", F(e.Ppass), ">=" + F(p.PpassMin));
			}
			e.AllPass = e.Failed.Count == 0;
			if (e.Failed.Count > 0) e.FirstFail = e.Failed[0].Substring(0, e.Failed[0].IndexOf('='));
			cache[key] = e; order.Add(e);
			return e;
		}
		static void Fail(NQCE6_Eval e, string gate, string val, string thr) { e.Failed.Add(gate + "=" + val + "(" + thr + ")"); }

		static int Cmp(NQCE6_Eval a, NQCE6_Eval b)
		{
			int c = a.NFailed.CompareTo(b.NFailed); if (c != 0) return c;
			c = b.TStat.CompareTo(a.TStat); if (c != 0) return c;
			return string.CompareOrdinal(a.TieKey, b.TieKey);
		}

		// Busqueda por beam. Determinista: sin azar fuera del hash(semilla|clave).
		public NQCE6_Result Search()
		{
			int K = pool.Count;
			List<NQCE6_Eval> level = new List<NQCE6_Eval>();
			for (int r = 0; r < K; r++) level.Add(Evaluate(new int[] { r }));
			List<NQCE6_Eval> beam = Select(level);
			for (int s = 2; s <= p.MaxMembers; s++)
			{
				List<NQCE6_Eval> cand = new List<NQCE6_Eval>(); HashSet<string> seen = new HashSet<string>();
				if (s == 2 && p.PoolBalance)
				{
					// D-039: semillas = TODOS los pares largo+corto (cada uno es un ensayo); beam de nivel 2 por la puntuacion de siempre (D-016/D-035)
					for (int a = 0; a < K; a++) for (int c = a + 1; c < K; c++)
						if (isLong[a] != isLong[c]) cand.Add(Evaluate(new int[] { a, c }));
					seedPairs = cand.Count;
					if (cand.Count > 0) { beam = Select(cand); continue; }
				}
				foreach (NQCE6_Eval st in beam)
					for (int r = 0; r < K; r++)
					{
						if (Array.IndexOf(st.Ranks, r) >= 0) continue;
						int[] nr = new int[s]; Array.Copy(st.Ranks, nr, s - 1); nr[s - 1] = r; Array.Sort(nr);
						if (!seen.Add(KeyOf(nr))) continue;
						cand.Add(Evaluate(nr));
					}
				beam = Select(cand);
			}
			return Finish();
		}

		// supervivientes: duros descartados (si size>=Min), dedupe por huella, mejores W
		List<NQCE6_Eval> Select(List<NQCE6_Eval> cand)
		{
			List<NQCE6_Eval> ok = new List<NQCE6_Eval>();
			foreach (NQCE6_Eval e in cand) if (!e.HardFail) ok.Add(e);
			ok.Sort(Cmp);
			List<NQCE6_Eval> res = new List<NQCE6_Eval>(); HashSet<string> fps = new HashSet<string>();
			foreach (NQCE6_Eval e in ok)
			{
				if (!fps.Add(e.Fingerprint)) continue;
				res.Add(e); if (res.Count >= p.BeamWidth) break;
			}
			return res;
		}

		static double Jaccard(int[] a, int[] b)
		{
			int inter = 0; foreach (int x in a) if (Array.IndexOf(b, x) >= 0) inter++;
			return (double)inter / (a.Length + b.Length - inter);
		}

		static int CmpFin(NQCE6_Eval a, NQCE6_Eval b)
		{
			int c = b.TStat.CompareTo(a.TStat); if (c != 0) return c;
			c = b.Ppass.CompareTo(a.Ppass); if (c != 0) return c;     // NaN ordena primero en CompareTo; en aceptadas Ppass es finito o NaN con umbral NaN (nunca aceptadas)
			c = a.Size.CompareTo(b.Size); if (c != 0) return c;
			return string.CompareOrdinal(a.TieKey, b.TieKey);
		}

		NQCE6_Result Finish()
		{
			NQCE6_Result r = new NQCE6_Result(); r.PoolUsed = pool.Count;
			List<NQCE6_Eval> acc = new List<NQCE6_Eval>();
			StringBuilder rej = new StringBuilder(), led = new StringBuilder();
			rej.Append("combo_id\tsize\tmembers\ttrades\tdescartada_dura\tprimera_puerta\tpuertas_falladas\n");
			led.Append("portfolio_trial_id\tcampaign\tsize\tmembers\ttrades\tnet_usd\tpf\tstability\tsymmetry\tstatus\n");
			foreach (NQCE6_Eval e in order)
			{
				r.Trials++; r.TrialsBySize[e.Size]++;
				string mem = MemberList(e);
				string status = e.Size < p.MinMembers ? "PARCIAL" : (e.AllPass ? "ACEPTADA" : (e.HardFail ? "DESCARTADA_DURA" : "RECHAZADA"));
				led.Append(e.TrialId).Append('\t').Append(p.Campaign).Append('\t').Append(I(e.Size)).Append('\t').Append(mem).Append('\t').Append(I(e.Trades)).Append('\t')
				   .Append(F(e.Net)).Append('\t').Append(F(e.Pf)).Append('\t').Append(F(e.Stability)).Append('\t').Append(F(e.Symmetry)).Append('\t').Append(status).Append('\n');
				if (e.Size < p.MinMembers || e.Size > p.MaxMembers) continue;
				if (e.HardFail) r.HardDiscarded++;
				if (e.AllPass) { acc.Add(e); continue; }
				rej.Append(e.TrialId.Substring(0, 12)).Append('\t').Append(I(e.Size)).Append('\t').Append(mem).Append('\t').Append(I(e.Trades)).Append('\t').Append(e.HardFail ? "SI" : "NO").Append('\t')
				   .Append(e.FirstFail).Append('\t').Append(string.Join(";", e.Failed.ToArray())).Append('\n');
			}
			r.InputLong = inLong; r.InputShort = inShort; r.SeedPairs = seedPairs;
			for (int i = 0; i < pool.Count; i++) { if (isLong[i]) r.PoolLong++; else r.PoolShort++; }
			foreach (NQCE6_Eval e in order)
			{
				if (e.Size < p.MinMembers || e.Size > p.MaxMembers) continue;
				r.Eval3to5++; int nL = 0; foreach (int k in e.Ranks) if (isLong[k]) nL++;
				bool symF = false;
				foreach (string f in e.Failed) { string g = f.Substring(0, f.IndexOf('=')); int v; r.GateFail.TryGetValue(g, out v); r.GateFail[g] = v + 1; if (e.Failed.Count == 1) { r.GateOnly.TryGetValue(g, out v); r.GateOnly[g] = v + 1; } if (g == "Symmetry") symF = true; }
				if (nL == e.Size) { r.CompAllLong++; if (symF) r.SymFailAllLong++; } else if (nL == 0) { r.CompAllShort++; if (symF) r.SymFailAllShort++; } else { r.CompMixed++; if (symF) r.SymFailMixed++; if (e.AllPass) r.AcceptedMixed++; }
			}
			r.Accepted = acc.Count; r.ExecutorRuns = ExecutorRuns; r.CumulativeTrials = p.PriorTrials + r.Trials;
			acc.Sort(CmpFin);
			List<NQCE6_Eval> chosen = new List<NQCE6_Eval>();
			foreach (NQCE6_Eval e in acc)
			{
				if (chosen.Count >= p.Finalists) break;
				bool dup = false;
				foreach (NQCE6_Eval c in chosen) if (c.Fingerprint == e.Fingerprint || Jaccard(c.Ranks, e.Ranks) > p.MaxJaccard) { dup = true; break; }
				if (!dup) chosen.Add(e);
			}
			int rank = 1;
			foreach (NQCE6_Eval e in chosen)
			{
				NQCE6_Finalist f = new NQCE6_Finalist(); f.Rank = rank++; f.Eval = e;
				string body = SpecJson(e, "");
				f.SpecHash = NQCE_Const.Sha256Text(body);
				f.SpecJson = SpecJson(e, f.SpecHash);
				r.Finalists.Add(f);
			}
			r.RejectionsTsv = rej.ToString(); r.TrialsTsv = led.ToString();
			StringBuilder sb = new StringBuilder();
			sb.Append("NQC ET6 PORTAFOLIOS | contrato ").Append(NQCE_Const.ContractVersion).Append(" | nucleo ").Append(NQCE_Const.CoreVersion).Append(" | SOLO IS\n");
			sb.Append("campania=").Append(p.Campaign).Append(" semilla=").Append(I(p.Seed)).Append(" pool_usado=").Append(I(r.PoolUsed)).Append(" beam=").Append(I(p.BeamWidth)).Append(" miembros=").Append(I(p.MinMembers)).Append("..").Append(I(p.MaxMembers)).Append('\n');
			sb.Append("ensayos_esta_corrida=").Append(I(r.Trials)).Append(" (por tamano 1..5: ");
			for (int k = 1; k <= 5; k++) { if (k > 1) sb.Append('/'); sb.Append(I(r.TrialsBySize[k])); }
			sb.Append(") corridas_ejecutor=").Append(I(r.ExecutorRuns)).Append(" ledger_previo=").Append(I(p.PriorTrials)).Append(" ledger_acumulado=").Append(I(r.CumulativeTrials)).Append('\n');
			sb.Append("combinaciones_3a5_aceptadas=").Append(I(r.Accepted)).Append(" descartadas_duras=").Append(I(r.HardDiscarded)).Append(" finalistas=").Append(I(r.Finalists.Count)).Append('\n');
			sb.Append("ppass_umbral=").Append(F(p.PpassMin)).Append(" (fijado por ET2)\n");
			foreach (NQCE6_Finalist f in r.Finalists) sb.Append("finalista ").Append(I(f.Rank)).Append(" | miembros=").Append(MemberList(f.Eval)).Append(" | spec_hash=").Append(f.SpecHash).Append('\n');
			sb.Append("rechazos_sha256=").Append(NQCE_Const.Sha256Text(r.RejectionsTsv)).Append(" ensayos_sha256=").Append(NQCE_Const.Sha256Text(r.TrialsTsv)).Append('\n');
			if (p.PoolBalance) sb.Append("pool_balance=true pool_largos=").Append(I(r.PoolLong)).Append(" pool_cortos=").Append(I(r.PoolShort)).Append(" pares_semilla_mixtos=").Append(I(r.SeedPairs)).Append('\n');
			StringBuilder dg = new StringBuilder();
			dg.Append("[DIAGNOSTICO ET6 D-039] pool_balance=").Append(p.PoolBalance ? "true" : "false").Append(" candidatos_entrada largos/cortos=").Append(I(inLong)).Append('/').Append(I(inShort)).Append(" pool_usado largos/cortos=").Append(I(r.PoolLong)).Append('/').Append(I(r.PoolShort));
			dg.Append(r.PoolLong == r.PoolShort ? " equilibrado" : " DESEQUILIBRADO(" + I(Math.Abs(r.PoolLong - r.PoolShort)) + ")").Append(" pares_semilla_mixtos=").Append(I(r.SeedPairs)).Append('\n');
			dg.Append("combinaciones_3a5_evaluadas=").Append(I(r.Eval3to5)).Append(" solo_largos=").Append(I(r.CompAllLong)).Append(" solo_cortos=").Append(I(r.CompAllShort)).Append(" mixtas=").Append(I(r.CompMixed)).Append(" | falla_Symmetry l/c/m=").Append(I(r.SymFailAllLong)).Append('/').Append(I(r.SymFailAllShort)).Append('/').Append(I(r.SymFailMixed)).Append(" aceptadas_mixtas=").Append(I(r.AcceptedMixed)).Append('\n');
			dg.Append("puerta\tfallan\tfallan_solo_ella (3..5 miembros; neto_costo_x/P_pass solo se evaluan si el resto pasa)\n");
			foreach (KeyValuePair<string, int> kv in r.GateFail) { int o; r.GateOnly.TryGetValue(kv.Key, out o); dg.Append(kv.Key).Append('\t').Append(I(kv.Value)).Append('\t').Append(I(o)).Append('\n'); }
			r.Diagnostics = dg.ToString();
			r.Summary = sb.ToString(); r.SummaryHash = NQCE_Const.Sha256Text(r.Summary);
			return r;
		}

		string MemberList(NQCE6_Eval e)
		{
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < e.Ranks.Length; i++) { if (i > 0) sb.Append('+'); sb.Append(pool[e.Ranks[i]].TrialId.Substring(0, 12)); }
			return sb.ToString();
		}

		// JSON a mano, orden de campos fijo, sin fechas: el mismo portafolio y semilla dan el mismo hash. spec_hash = SHA256 del texto con "spec_hash":"".
		string SpecJson(NQCE6_Eval e, string hash)
		{
			StringBuilder j = new StringBuilder();
			j.Append("{\n");
			j.Append("\"schema\":\"").Append(NQCE6_Params.SchemaVersion).Append("\",\n");
			j.Append("\"contract\":\"").Append(NQCE_Const.ContractVersion).Append("\",\"executor_spec_version\":\"").Append(NQCE_Const.SpecVersion).Append("\",\"core_version\":\"").Append(NQCE_Const.CoreVersion).Append("\",\n");
			j.Append("\"stage\":\"ET6\",\"data_scope\":\"IS\",\"campaign\":\"").Append(p.Campaign).Append("\",\"seed\":").Append(I(p.Seed)).Append(",\n");
			j.Append("\"portfolio_id\":\"").Append(e.TrialId).Append("\",\n");
			j.Append("\"spec_hash\":\"").Append(hash).Append("\",\n");
			j.Append("\"engine_hash\":\"").Append(p.EngineHash).Append("\",\"library_version\":\"").Append(lib.Version).Append("\",\"library_hash\":\"").Append(p.LibraryHash.Length > 0 ? p.LibraryHash : lib.Version).Append("\",\n");
			j.Append("\"priority_rule\":\"fitness_is_desc (D-016); el orden de members es la prioridad\",\n");
			j.Append("\"executor_config\":{\"cost_rt\":").Append(F(cfg.CostRT)).Append(",\"protect\":").Append(cfg.Protect ? "true" : "false").Append(",\"pause_level\":").Append(F(cfg.PauseLevel)).Append(",\"close_level\":").Append(F(cfg.CloseLevel)).Append(",\"cushion\":").Append(F(cfg.Cushion))
			 .Append(",\"account_profile\":\"").Append(p.Account.Name).Append("\"},\n");
			j.Append("\"acceptance\":{\"pf_min\":").Append(F(p.PfMin)).Append(",\"stability_min_exclusive\":").Append(F(p.StabilityMin)).Append(",\"symmetry_min_exclusive\":").Append(F(p.SymmetryMin)).Append(",\"dd_max\":").Append(F(p.DdMax)).Append(",\"worst_day_max\":").Append(F(p.WorstDayMax))
			 .Append(",\"min_trades\":").Append(I(p.MinTrades)).Append(",\"min_trades_per_month\":").Append(I(p.MinTradesPerMonth)).Append(",\"stress_mult\":").Append(F(p.StressMult)).Append(",\"ppass_min\":").Append(double.IsNaN(p.PpassMin) ? "null" : F(p.PpassMin)).Append("},\n");
			j.Append("\"members\":[\n");
			for (int i = 0; i < e.Ranks.Length; i++)
			{
				NQCE6_PoolEntry pe = pool[e.Ranks[i]]; NQCE_Member m = members[e.Ranks[i]];
				j.Append("{\"priority\":").Append(I(i + 1)).Append(",\"trial_id\":\"").Append(pe.TrialId).Append("\",\"member_id\":\"").Append(m.Id).Append("\",\"fitness_is\":").Append(F(pe.Fitness)).Append(",\"genes\":[");
				for (int g = 0; g < NQCE3_Genome.GeneCount; g++) { if (g > 0) j.Append(','); j.Append(I(pe.Genome.G[g])); }
				j.Append("],\"canonical\":\"").Append(pe.Genome.CanonicalText(lib)).Append("\",\"genome_text\":\"").Append(m.GenomeText().Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"}");
				j.Append(i + 1 < e.Ranks.Length ? ",\n" : "\n");
			}
			j.Append("],\n");
			j.Append("\"metrics_is\":{\"trades\":").Append(I(e.Trades)).Append(",\"net\":").Append(F(e.Net)).Append(",\"pf\":").Append(double.IsPositiveInfinity(e.Pf) ? "null" : F(e.Pf)).Append(",\"stability\":").Append(F(e.Stability)).Append(",\"symmetry\":").Append(F(e.Symmetry))
			 .Append(",\"dd_closed\":").Append(F(e.DdClosed)).Append(",\"dd_m2m\":").Append(F(e.DdM2m)).Append(",\"dd_m2m_core_informativo\":").Append(F(e.DdM2mCore)).Append(",\"worst_day\":").Append(F(e.WorstDay)).Append(",\"min_month_trades\":").Append(I(e.MinMonthCount)).Append(",\"stress_net\":").Append(F(e.StressNet))
			 .Append(",\"ppass\":").Append(double.IsNaN(e.Ppass) ? "null" : F(e.Ppass)).Append(",\"ppass_decided_starts\":").Append(I(e.PpassDecided)).Append(",\"t_stat\":").Append(F(e.TStat)).Append("}\n");
			j.Append("}\n");
			return j.ToString();
		}

		public static void Save(string dir, NQCE6_Result r)
		{
			Directory.CreateDirectory(dir); UTF8Encoding enc = new UTF8Encoding(false);
			foreach (NQCE6_Finalist f in r.Finalists) File.WriteAllText(Path.Combine(dir, "PORTFOLIO_SPEC_F" + f.Rank.ToString(CultureInfo.InvariantCulture) + ".json"), f.SpecJson, enc);
			File.WriteAllText(Path.Combine(dir, "ET6_REJECTIONS.tsv"), r.RejectionsTsv, enc);
			File.WriteAllText(Path.Combine(dir, "ET6_TRIALS.tsv"), r.TrialsTsv, enc);
			File.WriteAllText(Path.Combine(dir, "SUMMARY.txt"), r.Summary + r.Diagnostics, enc);
			File.WriteAllText(Path.Combine(dir, "ET6_GATES.txt"), r.Diagnostics, enc);
		}
	}
}
