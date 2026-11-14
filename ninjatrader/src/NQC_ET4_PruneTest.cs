#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

//==============================================================================
// NQC_ET4_PruneTest - Prueba TECNICA sintetica de la poda ET4 (NQC_ET4_Prune.cs). SIN ordenes, SOLO barras sinteticas hechas a mano,
// NO calcula ni reporta resultados economicos de mercado. Requiere NQC_ET1_Core.cs, NQC_ET1C_AccountSim.cs y NQC_ET3_Genome/Fitness.cs.
// Usa una biblioteca FALSA (4 condiciones de juguete) para que los resultados se calculen a mano; no es la biblioteca D-018.
//
// Mundo sintetico: 24 sesiones (2 por trimestre, 2020Q1..2022Q4), 1380 barras M1 por dia (00:00..22:59). Cada barra O=C=10000, H=10001,
// L=9999 (ATR de Wilder = 2,0 para cualquier periodo; con ksl=0,5 el stop 9999 se toca en la barra de entrada). Excepciones:
//   sello 600 (10:00): H = 10003 (activa un objetivo de +1,5 pts)       sello 691 (11:31): O=C=10002, H=10003, L=10001 (salida de las 11:30 gana +2 pts)
// Condiciones de juguete: 101 (3x3 niveles): dispara en sello 571+10*pl si umbral>0; 102 (3x3): verdadera si pl>0 y umbral>0;
//   103 (1x5): sello 571 solo en trimestres con indice < 4+umbral; 104 (1x1): siempre verdadera.
// Resultados a mano por operacion larga (cuesta 24,68; 1,5x = 37,02): salida a las 11:30 -> bruto +2 -> neto 15,32 (1,5x: 2,98);
//   objetivo +1,5 -> neto 5,32 (1,5x: -7,02); ksl=0,5 -> stop en la barra de entrada, bruto -1 -> neto -44,68.
//   A  101(pl1,thr1) ksl1,25 sin objetivo : 24 op, neto 367,68, 1,5x 71,52, 12/12 trimestres, stop 2,5 pts = US$50, vecinos 7/8 -> PASA
//   B  101(pl1,thr1) ksl1,25 k_tp0,75      : 24 op, neto 127,68, 1,5x -168,48 -> falla F1
//   C  101+102 (pl1,thr1) ksl0,75          : pasa F1/F2, vecinos 8/12 = 66,7% (<70%) -> falla F3; stop 1,5 pts = US$30
//   D  103(thr2) ksl1,25                   : 12 op (trimestres 0..5), neto 183,84, 1,5x 35,76, 6/12 trimestres -> falla F2
//   E  103(thr3) ksl1,25                   : 14 op (trimestres 0..6), neto 214,48, 1,5x 41,72, 7/12 (limite) y vecinos 6/6 -> PASA
//   Vecinos evaluados: 8 (A) + 12 (C) + 6 (E) = 26; E.thr-1 es el genoma D (candidato, ya en el ledger de ET3) y A.ksl+1 se declara conocido => 24 ensayos nuevos;
//   corridas del ejecutor: 5 candidatos + 26 vecinos = 31 (un vecino conocido por ET3 se reevalua, pero no es ensayo nuevo).
// Salida: <Documentos>\NinjaTrader 8\NQC_Results\ET4PRUNE_<fecha_hora>\PRUNE_TEST_REPORT.txt y SUMMARY.txt (+ TSV del caso de prueba)
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public sealed class NQCE4_ToyCond : INQCE_Condition
	{
		private int id, pl, thr;
		public NQCE4_ToyCond(int condId, int periodLevel, int thresholdLevel) { id = condId; pl = periodLevel; thr = thresholdLevel; }
		public bool Eval(NQCE_Ctx c)
		{
			if (id == 101) return thr > 0 && c.Stamp == 571 + 10 * pl;
			if (id == 102) return pl > 0 && thr > 0;
			if (id == 103) return c.Stamp == 571 && NQCE4_Prune.QuarterIndex(c.B.DayKey[c.I]) < 4 + thr;
			return true;
		}
		public string Describe() { return "TOY" + id.ToString(CultureInfo.InvariantCulture) + "(pl=" + pl.ToString(CultureInfo.InvariantCulture) + ",thr=" + thr.ToString(CultureInfo.InvariantCulture) + ")"; }
	}

	public sealed class NQCE4_ToyLib : INQCE3_Library
	{
		public string Version { get { return "TOYLIB-1"; } }
		public int ConditionCount { get { return 4; } }
		private static readonly int[] Ids = new int[] { 101, 102, 103, 104 };
		public int ConditionId(int index) { return Ids[index]; }
		public bool IsValidCondition(int condId) { return condId >= 101 && condId <= 104; }
		public int PeriodLevels(int condId) { return (condId == 101 || condId == 102) ? 3 : 1; }
		public int ThresholdLevels(int condId) { return (condId == 101 || condId == 102) ? 3 : (condId == 103 ? 5 : 1); }
		public INQCE_Condition Create(int condId, int periodLevel, int thresholdLevel, int atrPeriod, int dir) { return new NQCE4_ToyCond(condId, periodLevel, thresholdLevel); }
	}

	public class NQC_ET4_PruneTest : Strategy
	{
		private const string ScriptVersion = "ET4PRUNETEST-1.0.0";
		private bool started, done;
		private StringBuilder rep = new StringBuilder();
		private int pass, fail;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Prueba tecnica sintetica de la poda ET4 (sin ordenes).";
				Name = "NQC_ET4_PruneTest";
				Calculate = Calculate.OnBarClose;
				BarsRequiredToTrade = 0;
				IsExitOnSessionCloseStrategy = false;
				TraceOrders = false;
			}
			else if (State == State.DataLoaded) { started = true; }
			else if (State == State.Terminated)
			{
				if (!started || done) return;
				done = true;
				try { RunAll(); }
				catch (Exception ex)
				{
					Print("NQC_ET4PRUNE_ERROR " + ex.Message + " | " + ex.StackTrace);
					try
					{
						string d = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results");
						Directory.CreateDirectory(d);
						File.WriteAllText(Path.Combine(d, "ET4PRUNE_ERROR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt"), ex.ToString());
					}
					catch { }
				}
			}
		}

		protected override void OnBarUpdate() { }

		private static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }
		private static string D(double v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }
		private void Chk(string name, bool ok, string info)
		{
			if (ok) pass++; else fail++;
			rep.Append(ok ? "[PASS] " : "[FALLA] ").Append(name).Append(" | ").Append(info).Append("\n");
		}
		private static bool Near(double a, double b) { return Math.Abs(a - b) < 1e-6; }

		// 2 sesiones por trimestre (dias 11 y siguiente habil del mes central), barras M1 00:00..22:59 hechas a mano.
		private static NQCE_Bars MakeBars(List<DateTime> days, out double[] volume)
		{
			int per = 1380, n = days.Count * per; double P = 10000.0;
			long[] t = new long[n]; double[] o = new double[n], h = new double[n], l = new double[n], c = new double[n]; volume = new double[n];
			int k = 0;
			foreach (DateTime d in days)
				for (int m = 0; m < per; m++, k++)
				{
					t[k] = d.Date.Ticks + m * 600000000L;
					o[k] = P; c[k] = P; h[k] = P + 1; l[k] = P - 1; volume[k] = 500.0;
					if (m == 600) h[k] = P + 3;
					if (m == 691) { o[k] = P + 2; c[k] = P + 2; h[k] = P + 3; l[k] = P + 1; }
				}
			return new NQCE_Bars(t, o, h, l, c);
		}
		private static List<DateTime> IsDays()
		{
			List<DateTime> l = new List<DateTime>();
			for (int q = 0; q < 12; q++)
			{
				DateTime d = new DateTime(2020 + q / 4, (q % 4) * 3 + 2, 11); int got = 0;
				while (got < 2) { if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday) { l.Add(d); got++; } d = d.AddDays(1); }
			}
			return l;
		}

		private static NQCE3_Genome G(int[] slots, int atr, int ksl, int ktp)
		{
			NQCE3_Genome g = new NQCE3_Genome();
			for (int i = 0; i < slots.Length; i++) g.G[i] = slots[i];
			g.G[NQCE3_Genome.GDir] = 1; g.G[NQCE3_Genome.GAtr] = atr; g.G[NQCE3_Genome.GKsl] = ksl; g.G[NQCE3_Genome.GKtp] = ktp;
			return g;
		}

		private void RunAll()
		{
			rep.Append("NQC ET4 PruneTest ").Append(ScriptVersion).Append(" | poda ").Append(NQCE4_Prune.Version).Append(" | contrato ").Append(NQCE_Const.ContractVersion).Append(" | SOLO barras sinteticas y biblioteca de juguete\n\n");
			NQCE4_ToyLib lib = new NQCE4_ToyLib();
			double[] vol; NQCE_Bars bars = MakeBars(IsDays(), out vol);
			NQCE_Config cfg = new NQCE_Config(); cfg.Excluded = new HashSet<int>();

			// ---- P1 constantes de costo y parametros [B]
			Chk("P1 costos 24,68 y 37,02 (1,5x); umbrales [B]: 1,5x, >=7/12, 70%",
				Near(cfg.CostRT, 24.68) && Near(cfg.CostRT * NQCE4_Params.StressMult, 37.02) && NQCE4_Params.StressMult == 1.5 && NQCE4_Params.MinQuartersPositive == 7 && NQCE4_Params.Quarters == 12 && NQCE4_Params.NeighborPassPercent == 70,
				"CostRT=" + D(cfg.CostRT) + " estres=" + D(cfg.CostRT * 1.5));

			// ---- P2 trimestres e umbrales de decision
			bool qOk = NQCE4_Prune.QuarterIndex(20200101) == 0 && NQCE4_Prune.QuarterIndex(20200331) == 0 && NQCE4_Prune.QuarterIndex(20200401) == 1 && NQCE4_Prune.QuarterIndex(20221231) == 11 && NQCE4_Prune.QuarterIndex(20210701) == 6;
			bool thrown1 = false, thrown2 = false;
			try { NQCE4_Prune.QuarterIndex(20191231); } catch (InvalidOperationException) { thrown1 = true; }
			try { NQCE4_Prune.QuarterIndex(20230101); } catch (InvalidOperationException) { thrown2 = true; }
			Chk("P2 trimestres calendario 2020Q1..2022Q4 (indices 0..11); fuera del IS lanza excepcion", qOk && thrown1 && thrown2, "ok");
			Chk("P3 compuertas: F2 6->no 7->si; F3 7/10 si, 6/9 no (60<63), 14/20 si, 0/0 no",
				!NQCE4_Prune.PassesF2(6) && NQCE4_Prune.PassesF2(7) && NQCE4_Prune.PassesF3(7, 10) && !NQCE4_Prune.PassesF3(6, 9) && NQCE4_Prune.PassesF3(14, 20) && !NQCE4_Prune.PassesF3(0, 0), "ok");

			// ---- P4 rejilla de vecinos
			NQCE3_Genome gA = G(new int[] { 101, 4 }, 14, 3, 0);
			List<NQCE4_Neighbor> nA = NQCE4_Neighbors.Build(gA, lib);
			string la = ""; foreach (NQCE4_Neighbor n in nA) la += n.Label + ",";
			Chk("P4.1 vecinos de A: s0.pl-1,s0.thr-1,s0.pl+1,s0.thr+1,atr-1,atr+1,ksl-1,ksl+1 (8; sin k_tp porque 'sin objetivo' no tiene vecinos)", nA.Count == 8 && la == "s0.pl-1,s0.thr-1,s0.pl+1,s0.thr+1,atr-1,atr+1,ksl-1,ksl+1,", la);
			NQCE3_Genome gX = G(new int[] { 101, 0, 104, 0 }, 5, 0, 31);
			List<NQCE4_Neighbor> nX = NQCE4_Neighbors.Build(gX, lib);
			string lx = ""; foreach (NQCE4_Neighbor n in nX) lx += n.Label + ",";
			Chk("P4.2 bordes: pl0/thr0, atr 5, ksl 0, ktp 31 => solo vecinos hacia dentro; condicion 1x1 sin vecinos; fuera de rejilla no existe ni se repara", nX.Count == 5 && lx == "s0.pl+1,s0.thr+1,atr+1,ksl+1,ktp-1,", lx);
			NQCE3_Genome gY = G(new int[] { 101, 4 }, 100, 22, 1);
			List<NQCE4_Neighbor> nY = NQCE4_Neighbors.Build(gY, lib);
			string ly = ""; foreach (NQCE4_Neighbor n in nY) ly += n.Label + ",";
			Chk("P4.3 bordes superiores: atr 100, ksl 22, ktp 1 (el vecino 'ausente' no existe) => atr-1, ksl-1, ktp+1", ly == "s0.pl-1,s0.thr-1,s0.pl+1,s0.thr+1,atr-1,ksl-1,ktp+1,", ly);
			bool unique = true; HashSet<string> ids = new HashSet<string>(); foreach (NQCE4_Neighbor n in nA) unique &= ids.Add(n.Genome.TrialId(lib)) && n.Genome.TrialId(lib) != gA.TrialId(lib);
			Chk("P4.4 los 8 vecinos de A son distintos entre si y del candidato (TrialId)", unique, "ok");

			// ---- candidatos
			NQCE3_Genome A = gA;
			NQCE3_Genome B = G(new int[] { 101, 4 }, 14, 3, 2);
			NQCE3_Genome C = G(new int[] { 101, 4, 102, 4 }, 14, 1, 0);
			NQCE3_Genome Dg = G(new int[] { 103, 2 }, 14, 3, 0);
			NQCE3_Genome E = G(new int[] { 103, 3 }, 14, 3, 0);
			List<NQCE4_Candidate> cands = new List<NQCE4_Candidate>();
			NQCE3_Genome[] gs = new NQCE3_Genome[] { A, B, C, Dg, E }; string[] nm = new string[] { "A", "B", "C", "D", "E" };
			for (int i = 0; i < gs.Length; i++) { gs[i].Repair(lib); cands.Add(new NQCE4_Candidate(gs[i].TrialId(lib), gs[i], "fp" + nm[i])); }

			List<string> known = new List<string>(); known.Add(nA[7].Genome.TrialId(lib));      // A.ksl+1 ya esta en el ledger de ET3
			NQCE4_Prune pr = new NQCE4_Prune(bars, vol, cfg, lib, known);
			pr.Evaluate(cands);
			NQCE4_Outcome oA = pr.Outcomes[0], oB = pr.Outcomes[1], oC = pr.Outcomes[2], oD = pr.Outcomes[3], oE = pr.Outcomes[4];

			Chk("P5 A pasa todo: 24 op, neto 367,68, 1,5x 71,52, 12/12 trimestres, vecinos 7/8, stop 2,5 pts",
				oA.Status == "PASS" && oA.Trades == 24 && Near(oA.Net1, 367.68) && Near(oA.Net15, 71.52) && oA.QPos == 12 && oA.NbTotal == 8 && oA.NbPass == 7 && oA.StopPts.Length == 24 && Near(oA.StopPts[0], 2.5) && Near(oA.StopPts[23], 2.5),
				oA.Status + " n=" + I(oA.Trades) + " net=" + D(oA.Net1) + " net15=" + D(oA.Net15) + " q=" + I(oA.QPos) + " vec=" + I(oA.NbPass) + "/" + I(oA.NbTotal));
			Chk("P6 B falla F1: 24 op, neto 127,68 (>0), 1,5x -168,48; corte temprano: F2 y F3 no evaluados",
				oB.Status == "F1" && oB.Trades == 24 && Near(oB.Net1, 127.68) && Near(oB.Net15, -168.48) && !oB.F2Done && !oB.F3Done && oB.NbTotal == 0,
				oB.Status + " net=" + D(oB.Net1) + " net15=" + D(oB.Net15) + " | " + oB.Cause);
			Chk("P7 C falla F3: pasa F1 y F2, vecinos 8/12 = 66,7% < 70%; stop 1,5 pts",
				oC.Status == "F3" && oC.F2Done && oC.F3Done && oC.QPos == 12 && Near(oC.Net15, 71.52) && oC.NbTotal == 12 && oC.NbPass == 8 && Near(oC.StopPts[0], 1.5),
				oC.Status + " vec=" + I(oC.NbPass) + "/" + I(oC.NbTotal) + " | " + oC.Cause);
			Chk("P8 D falla F2 por trimestres: 12 op en 6 trimestres, neto 183,84, 1,5x 35,76 (pasa F1), 6/12 < 7; F3 no evaluado",
				oD.Status == "F2" && oD.Trades == 12 && Near(oD.Net1, 183.84) && Near(oD.Net15, 35.76) && oD.QPos == 6 && oD.QTrades[5] == 2 && oD.QTrades[6] == 0 && !oD.F3Done && oD.NbTotal == 0,
				oD.Status + " q=" + I(oD.QPos) + " | " + oD.Cause);
			Chk("P9 E en el limite: 14 op, 1,5x 41,72, exactamente 7/12 trimestres (pasa), vecinos 6/6 => PASA",
				oE.Status == "PASS" && oE.Trades == 14 && Near(oE.Net1, 214.48) && Near(oE.Net15, 41.72) && oE.QPos == 7 && oE.NbTotal == 6 && oE.NbPass == 6,
				oE.Status + " q=" + I(oE.QPos) + " vec=" + I(oE.NbPass) + "/" + I(oE.NbTotal));

			// ---- ledger / ensayos
			int ledgerRows = pr.LedgerTsv().Split('\n').Length - 2;
			Chk("P10 ensayos: 26 vecinos evaluados (8+12+6) 2 ya conocidos (A.ksl+1 declarado de ET3; E.thr-1 = candidato D) => 24 nuevos; 31 corridas del ejecutor; el acumulado suma ET3 + 24",
				pr.NeighborEvaluations == 26 && pr.FreshTrials == 24 && ledgerRows == 26 && pr.ExecutorRuns == 31 && pr.Summary(cands, 100000).Contains("TRIALS_LEDGER_acumulado=100024"),
				"vecinos=" + I(pr.NeighborEvaluations) + " nuevos=" + I(pr.FreshTrials) + " filas=" + I(ledgerRows) + " corridas=" + I(pr.ExecutorRuns));

			// ---- P11 F1 == corrida completa del ejecutor a 1,5x
			NQCE_Config cfg15 = new NQCE_Config(); cfg15.Excluded = new HashSet<int>(); cfg15.CostMult = 1.5;
			bool eq = true; double[] expect15 = new double[] { oA.Net15, oB.Net15, oC.Net15, oD.Net15, oE.Net15 };
			for (int i = 0; i < gs.Length; i++)
			{
				NQCE_Portfolio pf = new NQCE_Portfolio(); pf.Name = "chk"; pf.Members.Add(gs[i].ToMember(lib, "m"));
				NQCE_Result r = NQCE_Executor.Run(bars, pf, cfg15, new NQCE_NullHooks()); double s = 0; foreach (NQCE_Trade t in r.Trades) s += t.Net;
				eq &= Near(s, expect15[i]);
			}
			Chk("P11 neto 1,5x de F1 == suma de neto de una corrida completa del ejecutor con CostMult=1,5 (no se duplica la semantica de costos)", eq, "5 candidatos");

			// ---- salidas
			string pool = pr.PoolTsv(cands), rej = pr.RejectsTsv();
			string[] pl = pool.Split('\n'); string[] rl = rej.Split('\n');
			Chk("P12 pool = {A,E} en orden de entrada; TSV de rechazos con 5 renglones y causa F1/F2/F3/PASS",
				pl.Length == 4 && pl[1].Contains(oA.TrialId) && pl[2].Contains(oE.TrialId) && rl.Length == 7 && rl[2].Contains("\tF1\t") && rl[3].Contains("\tF3\t") && rl[4].Contains("\tF2\t") && rl[1].Contains("\tPASS\t") && rl[5].Contains("\tPASS\t"),
				"pool=" + I(pl.Length - 2) + " rechazos=" + I(rl.Length - 2));
			Chk("P13 F4 informativo no elimina: stop en pts y US$ presente para todos (A 2,5 pts/US$50; C 1,5 pts/US$30); B (rechazado por F1) lo conserva",
				rl[1].Contains("\t2.50\t2.50\t2.50\t2.50\t2.50\t2.50\t50.00\t50.00\t50.00\t50.00") && rl[3].Contains("\t1.50\t1.50\t1.50\t1.50\t1.50\t1.50\t30.00\t30.00\t30.00\t30.00") && rl[2].Contains("\t2.50\t2.50\t2.50\t2.50\t2.50\t2.50\t50.00"),
				"ok");

			// ---- determinismo: segunda instancia, mismo todo
			NQCE4_Prune pr2 = new NQCE4_Prune(bars, vol, cfg, lib, known); List<NQCE4_Candidate> cands2 = new List<NQCE4_Candidate>();
			for (int i = 0; i < gs.Length; i++) cands2.Add(new NQCE4_Candidate(gs[i].TrialId(lib), gs[i].Clone(), "fp" + nm[i]));
			pr2.Evaluate(cands2);
			string hp = pr.PoolSha256(cands), hr = pr.RejectsSha256(), hl = pr.LedgerSha256();
			Chk("P14 determinismo: segunda corrida => mismo hash de pool, de rechazos y de ledger", hp == pr2.PoolSha256(cands2) && hr == pr2.RejectsSha256() && hl == pr2.LedgerSha256() && pr.FreshTrials == pr2.FreshTrials,
				"pool=" + hp.Substring(0, 16) + " rechazos=" + hr.Substring(0, 16) + " ledger=" + hl.Substring(0, 16));

			// ---- integridad de entrada y guardia de datos
			bool badId = false;
			try { List<NQCE4_Candidate> bad = new List<NQCE4_Candidate>(); bad.Add(new NQCE4_Candidate("00", gs[0].Clone(), "x")); new NQCE4_Prune(bars, vol, cfg, lib, null).Evaluate(bad); } catch (ArgumentException) { badId = true; }
			Chk("P15 un TrialId que no corresponde al genoma se rechaza (integridad del databank)", badId, "ok");
			bool guard = false;
			try
			{
				List<DateTime> d19 = new List<DateTime>(); d19.Add(new DateTime(2019, 12, 12)); double[] v19; NQCE_Bars b19 = MakeBars(d19, out v19);
				new NQCE4_Prune(b19, v19, cfg, lib, null);
			}
			catch (InvalidOperationException) { guard = true; }
			bool costGuard = false;
			try { NQCE_Config bad = new NQCE_Config(); bad.CostMult = 1.5; new NQCE4_Prune(bars, vol, bad, lib, null); } catch (ArgumentException) { costGuard = true; }
			Chk("P16 solo IS (barras de 2019 => excepcion) y cfg debe ser 1,0x", guard && costGuard, "ok");

			// ---- salida
			string summary = pr.Summary(cands, 100000);
			rep.Append("\n").Append(summary);
			rep.Append("\nRESUMEN: ").Append(I(pass)).Append(" PASS / ").Append(I(fail)).Append(" FALLA\n");
			string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
			string dir = Path.Combine(Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results"), "ET4PRUNE_" + stamp);
			Directory.CreateDirectory(dir);
			UTF8Encoding u = new UTF8Encoding(false);
			File.WriteAllText(Path.Combine(dir, "PRUNE_TEST_REPORT.txt"), rep.ToString(), u);
			pr.Save(Path.Combine(dir, "case"), cands, 100000);
			string sum = "ET4 PruneTest " + ScriptVersion + " | " + I(pass) + " PASS / " + I(fail) + " FALLA | " + (fail == 0 ? "PASS" : "FALLA") + "\n" +
				"pool_sha256=" + hp + "\nrechazos_sha256=" + hr + "\nledger_sha256=" + hl + "\nreporte_sha256=" + NQCE_Const.Sha256Text(rep.ToString()) + "\n";
			File.WriteAllText(Path.Combine(dir, "SUMMARY.txt"), sum, u);
			Print(rep.ToString()); Print(sum);
		}
	}
}
