using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;

//==============================================================================
// NQC_ET6_PortfolioTest - Prueba TECNICA SINTETICA de NQC_ET6_Portfolio.cs (beam + perfil + spec). SIN ordenes, SOLO barras hechas a mano,
// NO calcula ni reporta resultados economicos de mercado. Requiere NQC_ET1_Core, NQC_ET1C_AccountSim, NQC_ET3_Genome, NQC_ET3_Fitness, NQC_ET6_Portfolio.
//
// Mundo: 720 sesiones = las primeras 20 jornadas habiles de cada mes 2020-01..2022-12 (36 meses x 20). Barras M1 sellos 569..692 (09:29..11:32):
// O=C=10000, H=10001, L=9999 (ATR = 2 -> con k_sl 2,5 el stop dista 5 pts y nunca se toca). Excepcion: sello 691 (la salida de las 11:30 se ejecuta a su apertura):
// O=C=10000+s[d] (H/L la contienen). Sin dias excluidos. Costo 24,68 (1,5x = 37,02). Sesion d (0..719): clase c=d%10, j=d/10 (0..71).
// Condiciones de juguete: 201(pl): sello 571 si d%10==pl; 202(pl): sello 571 si d%20==pl. Una operacion por dia como maximo (todas duran hasta las 11:30).
// Neto de una operacion = dir*s[d]*20 - costo. Desplazamientos s[d] por clase (dir del miembro D=+1 largo/-1 corto):
//   Estables (c0 L, c1 S, c2 L, c3 S, c8 L, c9 S): s = D*(j%4==3 ? -1 : +4)   -> bueno +55,32 (54 por clase), malo -44,68 (18 por clase); 1,5x: +42,98 / -57,02
//   Tempranas (c4 L, c5 S, c6 L): s = D*(j<7 ? 4 : (j par ? 2 : 0,5))         -> +55,32 (7), luego +15,32 (j par 8..70: 32) / -14,68 (j impar 7..71: 33)
//   Racha (c7 L): s = j<20 ? -10 (perdida -224,68, 20 ops) : +8 (+135,32, 52 ops)
// A mano (clases estables, 72 ops por clase): neto 54*55,32 - 18*44,68 = 2.183,04; bruto ganador 2.987,28; perdedor 804,24; 1,5x: 54*42,98 - 18*57,02 = 1.294,56.
//   P1 {M0,M1,M2}: 216 ops, neto 6.549,12, PF 2.987,28/804,24 = 3,7144; Symmetry = 2.183,04/4.366,08 = 50; 1,5x 3.883,68; 6 ops por mes; peor dia 44,68;
//       DD cerrado = DD M2M = 3*44,68 = 134,04 (los tres perdedores consecutivos de cada j%4==3). Stability y P_pass: contra el oraculo independiente.
//   P2 {M4,M5,M6} (tempranas): todo el beneficio llega en el primer ~10% -> Stability baja (< 30; R2 de un escalon al 10% ~ 3p(1-p) = 0,27) y otras puertas pasan.
//   P3 {M0,M1,M7} (racha larga): DD cerrado > 2.000 (la racha de 20 x -224,68 = -4.493,60 menos lo que ganan M0/M1 mientras tanto).
//   P4 {M8,M9,M10} (esparcidos, 1 op/mes cada uno): 3 ops por mes (< 5) y 108 ops (< 150).
//   P5 conflicto: X=(201,pl0,L,fitness 9) y Z=(201,pl0,S): ambos disparan los mismos 72 dias en el mismo sello. Si X tiene mas fitness opera X (neto 2.183,04);
//      si Z tiene mas fitness opera Z: 54*(-104,68) + 18*(-4,68) = -5.736,96.
// El oraculo (clase NQC6_Oracle) es una reimplementacion independiente: no usa NQCE_Executor ni NQCE_Metrics.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public sealed class NQC6_ToyCond : INQCE_Condition
	{
		public static Dictionary<int, int> DayOrd = new Dictionary<int, int>();
		private int id, pl;
		public NQC6_ToyCond(int condId, int periodLevel) { id = condId; pl = periodLevel; }
		public bool Eval(NQCE_Ctx c)
		{
			if (c.Stamp != 571) return false;
			int d = DayOrd[c.B.DayKey[c.I]];
			return id == 201 ? (d % 10 == pl) : (d % 20 == pl);
		}
		public string Describe() { return "TOY" + id.ToString(CultureInfo.InvariantCulture) + "(pl=" + pl.ToString(CultureInfo.InvariantCulture) + ")"; }
	}

	public sealed class NQC6_ToyLib : INQCE3_Library
	{
		public string Version { get { return "TOYLIB6-1"; } }
		public int ConditionCount { get { return 2; } }
		public int ConditionId(int index) { return index == 0 ? 201 : 202; }
		public bool IsValidCondition(int condId) { return condId == 201 || condId == 202; }
		public int PeriodLevels(int condId) { return condId == 201 ? 10 : 20; }
		public int ThresholdLevels(int condId) { return 1; }
		public INQCE_Condition Create(int condId, int periodLevel, int thresholdLevel, int atrPeriod, int dir) { return new NQC6_ToyCond(condId, periodLevel); }
	}

	public sealed class NQC6_OracleResult
	{
		public int Trades, MinMonth; public double Net, Pf, Stab, Sym, DdC, DdM, Worst, Stress, Ppass; public double[] Daily; public List<NQC_SessionRow> Rows = new List<NQC_SessionRow>();
		public List<int> TradeDays = new List<int>(); public List<int> TradeMember = new List<int>();
	}

	public static class NQC6_Oracle
	{
		public const int N = 720;
		public static double Shift(int d)
		{
			int c = d % 10, j = d / 10;
			if (c == 7) return j < 20 ? -10.0 : 8.0;
			double mag; int D = (c % 2 == 0) ? 1 : -1;
			if (c == 4 || c == 5 || c == 6) { D = (c == 5) ? -1 : 1; mag = j < 7 ? 4.0 : (j % 2 == 0 ? 2.0 : 0.5); }
			else mag = (j % 4 == 3) ? -1.0 : 4.0;
			return D * mag;
		}
		// genes: [cond, pl, dir(1/0)] por miembro en orden de prioridad
		public static NQC6_OracleResult Run(List<int[]> mem, double mult)
		{
			NQC6_OracleResult r = new NQC6_OracleResult(); double cost = 24.68 * mult;
			r.Daily = new double[N]; double cum = 0, peakC = 0, ddC = 0, peakM = 0, ddM = 0;
			int[] month = new int[36]; double gain = 0, loss = 0, lg = 0, sh = 0;
			for (int d = 0; d < N; d++)
			{
				int who = -1;
				for (int m = 0; m < mem.Count; m++) { int cnd = mem[m][0], pl = mem[m][1]; bool f = cnd == 201 ? (d % 10 == pl) : (d % 20 == pl); if (f) { who = m; break; } }
				double mk0 = cum, mk1 = cum;
				double net = 0, minEq = 0; int n = 0;
				if (who >= 0)
				{
					int dir = mem[who][2] == 1 ? 1 : -1; net = dir * Shift(d) * 20.0 - cost; n = 1; r.Trades++; r.Net += net; month[d / 20]++;
					r.TradeDays.Add(d); r.TradeMember.Add(who);
					if (net > 0) gain += net; else loss -= net;
					if (dir == 1) lg += net; else sh += net;
					mk0 = cum - cost; mk1 = cum + net; minEq = Math.Min(0.0, Math.Min(-cost, net));
					double[] mks = new double[] { mk0, mk1 };
					foreach (double mk in mks) { if (mk > peakM) peakM = mk; if (peakM - mk > ddM) ddM = peakM - mk; }
					cum += net; if (cum > peakC) peakC = cum; if (peakC - cum > ddC) ddC = peakC - cum;
				}
				r.Daily[d] = net; r.Rows.Add(new NQC_SessionRow(net, minEq, n));
			}
			r.Pf = loss <= 0 ? (gain > 0 ? double.PositiveInfinity : 0) : gain / loss; r.DdC = ddC; r.DdM = ddM;
			r.Worst = 0; foreach (double x in r.Daily) if (-x > r.Worst) r.Worst = -x;
			r.MinMonth = int.MaxValue; foreach (int mc in month) if (mc < r.MinMonth) r.MinMonth = mc;
			r.Sym = (lg > 0 && sh > 0) ? 100.0 * Math.Min(lg, sh) / Math.Max(lg, sh) : 0;
			// Stability: 1 - SSE/SST de la regresion MCO de la equity diaria acumulada contra el indice; 0 si pendiente <= 0
			double[] y = new double[N]; double c2 = 0; for (int i = 0; i < N; i++) { c2 += r.Daily[i]; y[i] = c2; }
			double sx = 0, sy = 0, sxx = 0, sxy = 0; for (int i = 0; i < N; i++) { sx += i; sy += y[i]; sxx += (double)i * i; sxy += i * y[i]; }
			double b = (N * sxy - sx * sy) / (N * sxx - sx * sx), a = (sy - b * sx) / N, sse = 0, sst = 0, my = sy / N;
			for (int i = 0; i < N; i++) { double e = y[i] - (a + b * i); sse += e * e; sst += (y[i] - my) * (y[i] - my); }
			r.Stab = (b <= 0 || sst <= 0) ? 0 : 100.0 * (1.0 - sse / sst);
			int dec; r.Ppass = NQC_AccountSim.Rolling(r.Rows, NQC_AccountProfile.RapidIntraday50K(), out dec);
			return r;
		}
	}

	public class NQC_ET6_PortfolioTest : Strategy
	{
		private const string ScriptVersion = "ET6PORTFOLIOTEST-1.0.0";
		private bool started, done; private StringBuilder rep = new StringBuilder(); private int pass, fail;
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults) { Name = "NQC_ET6_PortfolioTest"; Calculate = Calculate.OnBarClose; BarsRequiredToTrade = 0; IsExitOnSessionCloseStrategy = false; TraceOrders = false; }
			else if (State == State.DataLoaded) started = true;
			else if (State == State.Terminated) { if (!started || done) return; done = true; RunAll(); }
		}
		protected override void OnBarUpdate() { }
		private static string F(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }
		private void Chk(string name, bool ok, string info) { if (ok) pass++; else fail++; rep.Append(ok ? "[PASS] " : "[FALLA] ").Append(name).Append(" | ").Append(info).Append("\n"); }
		private static bool Near(double a, double b, double tol) { return Math.Abs(a - b) <= tol; }

		public static List<DateTime> Days()
		{
			List<DateTime> l = new List<DateTime>();
			for (int y = 2020; y <= 2022; y++) for (int m = 1; m <= 12; m++)
			{
				int n = 0; DateTime d = new DateTime(y, m, 1);
				while (n < 20) { if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday) { l.Add(d); n++; } d = d.AddDays(1); }
			}
			return l;
		}

		public static NQCE_Bars MakeBars(List<DateTime> days)
		{
			List<long> t = new List<long>(); List<double> o = new List<double>(), h = new List<double>(), l = new List<double>(), c = new List<double>();
			for (int d = 0; d < days.Count; d++)
				for (int st = 569; st <= 692; st++)
				{
					double op = 10000, cl = 10000;
					if (st == 691) { op = 10000 + NQC6_Oracle.Shift(d); cl = op; }
					t.Add(days[d].AddMinutes(st).Ticks); o.Add(op); c.Add(cl); h.Add(Math.Max(10001.0, Math.Max(op, cl))); l.Add(Math.Min(9999.0, Math.Min(op, cl)));
				}
			return new NQCE_Bars(t.ToArray(), o.ToArray(), h.ToArray(), l.ToArray(), c.ToArray());
		}

		static NQCE6_PoolEntry PE(NQC6_ToyLib lib, int cond, int pl, bool lng, double fit)
		{
			NQCE3_Genome g = new NQCE3_Genome(); g.G[0] = cond; g.G[1] = pl; g.G[NQCE3_Genome.GDir] = lng ? 1 : 0; g.G[NQCE3_Genome.GAtr] = 14; g.G[NQCE3_Genome.GKsl] = 8; g.G[NQCE3_Genome.GKtp] = 0;
			g.Repair(lib); return new NQCE6_PoolEntry(null, g, fit);
		}

		static List<NQCE6_PoolEntry> Pool(NQC6_ToyLib lib)
		{
			List<NQCE6_PoolEntry> p = new List<NQCE6_PoolEntry>();
			p.Add(PE(lib, 201, 0, true, 9)); p.Add(PE(lib, 201, 1, false, 8)); p.Add(PE(lib, 201, 2, true, 7)); p.Add(PE(lib, 201, 3, false, 6));
			p.Add(PE(lib, 201, 4, true, 5)); p.Add(PE(lib, 201, 5, false, 4)); p.Add(PE(lib, 201, 6, true, 3)); p.Add(PE(lib, 201, 7, true, 2));
			p.Add(PE(lib, 202, 8, true, 1.5)); p.Add(PE(lib, 202, 9, false, 1)); p.Add(PE(lib, 202, 18, true, 0.5));
			return p;
		}

		static List<int[]> Genes(NQCE6_Builder b, int[] ranks)
		{
			List<int[]> l = new List<int[]>(); foreach (int r in ranks) { NQCE3_Genome g = b.Entry(r).Genome; l.Add(new int[] { g.G[0], g.G[1], g.G[NQCE3_Genome.GDir] }); } return l;
		}

		static bool Has(NQCE6_Eval e, string gate) { foreach (string s in e.Failed) if (s.StartsWith(gate + "=")) return true; return false; }

		private void RunAll()
		{
			rep.Append("NQC ET6 PortfolioTest ").Append(ScriptVersion).Append(" | contrato ").Append(NQCE_Const.ContractVersion).Append(" | SOLO datos sinteticos\n\n");
			NQC6_ToyLib lib = new NQC6_ToyLib(); List<DateTime> days = Days();
			NQC6_ToyCond.DayOrd.Clear(); for (int i = 0; i < days.Count; i++) NQC6_ToyCond.DayOrd[days[i].Year * 10000 + days[i].Month * 100 + days[i].Day] = i;
			Chk("S0 mundo: 720 sesiones", days.Count == 720, "n=" + days.Count);
			NQCE_Bars bars = MakeBars(days);
			NQCE_Config cfg = new NQCE_Config(); cfg.Excluded = new HashSet<int>();
			NQCE6_Params P = new NQCE6_Params(); P.PpassMin = 0.5; P.EngineHash = "ENGINEHASH-TEST"; P.LibraryHash = "LIBHASH-TEST"; P.PriorTrials = 1000; P.Seed = 7;

			NQCE6_Builder b = new NQCE6_Builder(bars, null, cfg, lib, Pool(lib), P);
			Chk("S1 pool ordenado por fitness desc (prioridad D-016)", b.PoolCount == 11 && b.Entry(0).Fitness == 9 && b.Entry(10).Fitness == 0.5, "k=" + b.PoolCount);

			// P1: cumple el perfil
			NQCE6_Eval e1 = b.Evaluate(new int[] { 0, 1, 2 }); NQC6_OracleResult o1 = NQC6_Oracle.Run(Genes(b, e1.Ranks), 1.0), o1s = NQC6_Oracle.Run(Genes(b, e1.Ranks), 1.5);
			Chk("P1a a mano: 216 ops, neto 6.549,12, Symmetry 50, 6 ops/mes, peor dia 44,68", e1.Trades == 216 && Near(e1.Net, 6549.12, 1e-6) && Near(e1.Symmetry, 50.0, 1e-9) && e1.MinMonthCount == 6 && Near(e1.WorstDay, 44.68, 1e-9), "ops=" + e1.Trades + " neto=" + F(e1.Net) + " sym=" + F(e1.Symmetry) + " mes=" + e1.MinMonthCount + " peor=" + F(e1.WorstDay));
			Chk("P1b a mano: PF 3,7144, DD cerrado 134,04, DD M2M 158,72, 1,5x 3.883,68", Near(e1.Pf, 3 * 2987.28 / (3 * 804.24), 1e-9) && Near(e1.DdClosed, 134.04, 1e-6) && Near(e1.DdM2m, 158.72, 1e-6) && Near(e1.StressNet, 3883.68, 1e-6), "pf=" + F(e1.Pf) + " ddC=" + F(e1.DdClosed) + " ddM=" + F(e1.DdM2m) + " 1,5x=" + F(e1.StressNet));
			Chk("P1c contra oraculo: Stability, Symmetry, DD, PF, P_pass", Near(e1.Stability, o1.Stab, 1e-6) && Near(e1.Ppass, o1.Ppass, 1e-12) && Near(e1.DdClosed, o1.DdC, 1e-6) && Near(e1.DdM2m, o1.DdM, 1e-6) && Near(e1.Pf, o1.Pf, 1e-9) && Near(e1.StressNet, o1s.Net, 1e-6), "stab=" + F(e1.Stability) + " oraculo=" + F(o1.Stab) + " ppass=" + F(e1.Ppass));
			rep.Append("[INFO] defecto ET1 (no cuenta como PASS/FALLA): DdM2m del nucleo = " + F(e1.DdM2mCore) + " vs DD M2M correcto a mano 158,72 (el nucleo cuenta doble el cierre de sesion)\n");
			Chk("P1d cumple TODO el perfil (Stability>30, P_pass>=0,5)", e1.AllPass && e1.Stability > 30 && !e1.HardFail, "fallas=" + string.Join(";", e1.Failed.ToArray()));

			// P2: falla Stability (dura)
			NQCE6_Eval e2 = b.Evaluate(new int[] { 4, 5, 6 }); NQC6_OracleResult o2 = NQC6_Oracle.Run(Genes(b, e2.Ranks), 1.0);
			Chk("P2 falla Stability (dura, 3 miembros), valor = oraculo, descartada del beam", Has(e2, "Stability") && e2.HardFail && !e2.AllPass && Near(e2.Stability, o2.Stab, 1e-6) && o2.Stab < 30, "stab=" + F(e2.Stability) + " otras=" + string.Join(";", e2.Failed.ToArray()));

			// P3: falla DD
			NQCE6_Eval e3 = b.Evaluate(new int[] { 0, 1, 7 }); NQC6_OracleResult o3 = NQC6_Oracle.Run(Genes(b, e3.Ranks), 1.0);
			Chk("P3 falla DD cerrado (> 2.000), valor = oraculo", Has(e3, "DD_cerrado") && Near(e3.DdClosed, o3.DdC, 1e-6) && o3.DdC > 2000, "ddC=" + F(e3.DdClosed) + " ddM2m=" + F(e3.DdM2m) + " fallas=" + string.Join(";", e3.Failed.ToArray()));

			// P4: falla >=5/mes
			NQCE6_Eval e4 = b.Evaluate(new int[] { 8, 9, 10 }); NQC6_OracleResult o4 = NQC6_Oracle.Run(Genes(b, e4.Ranks), 1.0);
			Chk("P4 falla >=5 ops por mes (3) y >=150 ops (108), valores = oraculo", Has(e4, "ops_por_mes") && e4.MinMonthCount == 3 && o4.MinMonth == 3 && Has(e4, "operaciones") && e4.Trades == 108 && o4.Trades == 108, "mes=" + e4.MinMonthCount + " ops=" + e4.Trades + " fallas=" + string.Join(";", e4.Failed.ToArray()));

			// P5: conflicto de prioridad (pool propio de 2 miembros)
			List<NQCE6_PoolEntry> c1 = new List<NQCE6_PoolEntry>(); c1.Add(PE(lib, 201, 0, true, 9)); c1.Add(PE(lib, 201, 0, false, 3));
			List<NQCE6_PoolEntry> c2 = new List<NQCE6_PoolEntry>(); c2.Add(PE(lib, 201, 0, true, 3)); c2.Add(PE(lib, 201, 0, false, 9));
			NQCE6_Params Pc = new NQCE6_Params(); Pc.MinMembers = 1; Pc.PpassMin = 0.5;
			NQCE6_Builder bx = new NQCE6_Builder(bars, null, cfg, lib, c1, Pc), bz = new NQCE6_Builder(bars, null, cfg, lib, c2, Pc);
			NQCE6_Eval ex = bx.Evaluate(new int[] { 0, 1 }), ez = bz.Evaluate(new int[] { 0, 1 });
			NQC6_OracleResult ox = NQC6_Oracle.Run(Genes(bx, ex.Ranks), 1.0), oz = NQC6_Oracle.Run(Genes(bz, ez.Ranks), 1.0);
			Chk("P5a mayor fitness primero: opera el LARGO X (72 ops, neto 2.183,04)", ex.Trades == 72 && Near(ex.Net, 2183.04, 1e-6) && Near(ox.Net, 2183.04, 1e-6), "ops=" + ex.Trades + " neto=" + F(ex.Net));
			Chk("P5b mayor fitness primero: opera el CORTO Z (72 ops, neto -5.736,96)", ez.Trades == 72 && Near(ez.Net, -5736.96, 1e-6) && Near(oz.Net, -5736.96, 1e-6), "ops=" + ez.Trades + " neto=" + F(ez.Net));
			Chk("P5c las huellas de operaciones difieren (la prioridad cambia el resultado)", ex.Fingerprint != ez.Fingerprint, "");

			// umbrales desde configuracion, P_pass fail-closed
			NQCE6_Params Pn = new NQCE6_Params(); NQCE6_Builder bn = new NQCE6_Builder(bars, null, cfg, lib, Pool(lib), Pn);
			NQCE6_Eval en = bn.Evaluate(new int[] { 0, 1, 2 });
			Chk("U1 P_pass sin umbral de ET2 (NaN): falla cerrada con causa", !en.AllPass && Has(en, "P_pass") && en.Failed.Count == 1 && en.Failed[0].Contains("umbral_ET2_no_fijado"), string.Join(";", en.Failed.ToArray()));
			NQCE6_Params Pu = new NQCE6_Params(); Pu.PpassMin = 0.5; Pu.PfMin = 10.0; Pu.MinTrades = 300; Pu.MinTradesPerMonth = 7;
			NQCE6_Eval eu = new NQCE6_Builder(bars, null, cfg, lib, Pool(lib), Pu).Evaluate(new int[] { 0, 1, 2 });
			Chk("U2 umbrales leidos de la configuracion (PF>=10, ops>=300, mes>=7 -> fallan)", Has(eu, "PF_neto") && Has(eu, "operaciones") && Has(eu, "ops_por_mes") && !eu.AllPass, string.Join(";", eu.Failed.ToArray()));

			// busqueda completa (beam), dos corridas con la misma semilla
			NQCE6_Result r1 = new NQCE6_Builder(bars, null, cfg, lib, Pool(lib), P).Search();
			NQCE6_Builder bs = new NQCE6_Builder(bars, null, cfg, lib, Pool(lib), P); NQCE6_Result r2 = bs.Search();
			bool detOk = r1.SummaryHash == r2.SummaryHash && r1.Finalists.Count == r2.Finalists.Count && r1.Finalists.Count > 0;
			for (int i = 0; detOk && i < r1.Finalists.Count; i++) if (r1.Finalists[i].SpecHash != r2.Finalists[i].SpecHash || r1.Finalists[i].SpecJson != r2.Finalists[i].SpecJson) detOk = false;
			Chk("D1 determinismo: misma semilla -> mismo hash de spec de cada finalista y del resumen", detOk, "resumen=" + r1.SummaryHash);
			Chk("D2 finalistas <= 5 y cada uno cumple TODO el perfil segun el oraculo independiente", r1.Finalists.Count <= 5 && CheckFinalists(bs, r1, P), "finalistas=" + r1.Finalists.Count);
			bool div = true; for (int i = 0; i < r1.Finalists.Count; i++) for (int k = i + 1; k < r1.Finalists.Count; k++) { int inter = 0; foreach (int x in r1.Finalists[i].Eval.Ranks) if (Array.IndexOf(r1.Finalists[k].Eval.Ranks, x) >= 0) inter++; double jac = (double)inter / (r1.Finalists[i].Eval.Size + r1.Finalists[k].Eval.Size - inter); if (jac > P.MaxJaccard) div = false; }
			Chk("D3 diversidad: Jaccard de miembros entre finalistas <= 0,5", div, "");
			int rows = r1.TrialsTsv.Split('\n').Length - 2; int sum = 0; for (int s = 1; s <= 5; s++) sum += r1.TrialsBySize[s];
			Chk("L1 ledger: ensayos = filas unicas del TSV = suma por tamano; acumulado = previo 1.000 + ensayos", rows == r1.Trials && sum == r1.Trials && r1.CumulativeTrials == 1000 + r1.Trials && r1.ExecutorRuns >= r1.Trials, "ensayos=" + r1.Trials + " por tamano=" + r1.TrialsBySize[1] + "/" + r1.TrialsBySize[2] + "/" + r1.TrialsBySize[3] + "/" + r1.TrialsBySize[4] + "/" + r1.TrialsBySize[5] + " corridas=" + r1.ExecutorRuns);
			Chk("L2 TSV de rechazos: causas con valor y umbral; las descartadas duras llevan SI", r1.RejectionsTsv.Split('\n').Length > 2 && r1.RejectionsTsv.Contains("("), "filas=" + (r1.RejectionsTsv.Split('\n').Length - 2) + " duras=" + r1.HardDiscarded);

			// spec
			if (r1.Finalists.Count == 0) { Chk("J0 hay al menos un finalista", false, "ninguno"); } else {
			NQCE6_Finalist f0 = r1.Finalists[0];
			Chk("J1 spec: hash = SHA256 del texto con spec_hash vacio; trae hash del motor y de la biblioteca", f0.SpecJson.Contains("\"spec_hash\":\"" + f0.SpecHash + "\"") && NQCE_Const.Sha256Text(f0.SpecJson.Replace("\"spec_hash\":\"" + f0.SpecHash + "\"", "\"spec_hash\":\"\"")) == f0.SpecHash && f0.SpecJson.Contains("ENGINEHASH-TEST") && f0.SpecJson.Contains("LIBHASH-TEST") && f0.SpecJson.Contains("NQC_PORTFOLIO_SPEC/1"), f0.SpecHash);
			bool genes = true; for (int i = 0; i < f0.Eval.Size; i++) { string g = string.Join(",", Array.ConvertAll(bs.Entry(f0.Eval.Ranks[i]).Genome.G, delegate(int v) { return v.ToString(CultureInfo.InvariantCulture); })); if (!f0.SpecJson.Contains("\"priority\":" + (i + 1) + ",") || !f0.SpecJson.Contains("\"genes\":[" + g + "]")) genes = false; }
			Chk("J2 spec: prioridades 1..n por fitness desc y los 20 genes de cada miembro", genes, "miembros=" + f0.Eval.Size);
			}

			// IS solamente
			bool thrown = false;
			try { List<DateTime> d19 = new List<DateTime>(); d19.Add(new DateTime(2019, 12, 12)); new NQCE6_Builder(MakeBars(d19), null, cfg, lib, Pool(lib), P); } catch (InvalidOperationException) { thrown = true; }
			Chk("G1 barras fuera del IS (2019) -> excepcion", thrown, "");

			string text = rep.ToString() + "\nresultado: " + pass + " PASS / " + fail + " FALLA\n";
			string outDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results", "ET6PORTFOLIOTEST_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
			Directory.CreateDirectory(outDir); UTF8Encoding enc = new UTF8Encoding(false);
			File.WriteAllText(Path.Combine(outDir, "PORTFOLIO_TEST_REPORT.txt"), text, enc);
			NQCE6_Builder.Save(Path.Combine(outDir, "caso_beam"), r1);
			File.WriteAllText(Path.Combine(outDir, "SUMMARY.txt"), "NQC ET6 PORTFOLIO TEST - SUMMARY\nscript_version : " + ScriptVersion + "\ncontract : " + NQCE_Const.ContractVersion + "\nresultado : " + pass + " PASS / " + fail + " FALLA\nreport_sha256 : " + NQCE_Const.Sha256Text(text) + "\nresumen_beam_sha256 : " + r1.SummaryHash + "\nspec_f1_sha256 : " + (r1.Finalists.Count > 0 ? r1.Finalists[0].SpecHash : "NA") + "\nensayos : " + r1.Trials + "\n", enc);
			Print(text); Print(r1.Summary);
		}

		private bool CheckFinalists(NQCE6_Builder b, NQCE6_Result r, NQCE6_Params P)
		{
			foreach (NQCE6_Finalist f in r.Finalists)
			{
				List<int[]> g = Genes(b, f.Eval.Ranks); NQC6_OracleResult o = NQC6_Oracle.Run(g, 1.0), os = NQC6_Oracle.Run(g, 1.5);
				bool ok = o.Trades >= P.MinTrades && o.Pf >= P.PfMin && o.Stab > P.StabilityMin && o.Sym > P.SymmetryMin && o.DdC <= P.DdMax && o.DdM <= P.DdMax && o.Worst <= P.WorstDayMax && o.MinMonth >= P.MinTradesPerMonth && os.Net > 0 && o.Ppass >= P.PpassMin;
				if (!ok) return false;
			}
			return true;
		}
	}
}
