#region Using declarations
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

//==============================================================================
// NQC_RUN_FunnelTest - Prueba TECNICA SINTETICA de extremo a extremo del runner NQC_RUN_Funnel (ET3 -> ET7). SIN ordenes, SOLO barras hechas a mano,
// NO calcula ni reporta resultados economicos de mercado (los numeros del mundo de juguete NO son evidencia). Requiere NQC_ET1_Core, NQC_ET1C_AccountSim,
// NQC_ET3_*, NQC_ET4_Prune, NQC_ET5_Robust, NQC_ET6_Portfolio, NQC_ET7_* y NQC_RUN_Funnel.
//
// MUNDO DE JUGUETE: 27 sesiones por trimestre (9 por mes) 2020Q1..2022Q4 = 324 sesiones, barras M1 00:00..11:35 (696 por dia, completas desde 00:00), volumen con pico a las 09:31.
// Clase de la sesion c = ordinal % 3. Precio base 10000 (rango 2+0,2u, u = ordinal % 5). Entrada a las 09:31 (sello 571) y salida de tiempo a las 11:30 (sello 691).
//   c0 sube (zambullida 1,2 pts en 09:40, pico +6 en 10:20, cierre +3), c1 baja (espejo), c2 sube menos (pico +4, cierre +2). Un ruido de +-0,5 pts en el cierre da varianza.
// Biblioteca de juguete: 301 (todos los dias), 302(pl) ordinal%2==pl, 306(pl) ordinal%3==pl, 307(pl) ordinal%3!=pl. El largo en c0/c2 y el corto en c1 ganan; el resto pierde.
// Las puertas de ET6 son las del contrato salvo P_pass (el umbral lo fija ET2; aqui se fija 0 SOLO PARA LA PRUEBA y se marca en el SUMMARY como et6_ppass_min=0).
// El vector nulo de ET2 es sintetico (30 valores 0,05..1,5). El config de ET7 baja neff_coverage_min (D-036: se declara en la config) para la prueba.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public sealed class NQCRT_ToyCond : INQCE_Condition
	{
		public static Dictionary<int, int> DayOrd = new Dictionary<int, int>();
		private int id, pl;
		public NQCRT_ToyCond(int condId, int periodLevel) { id = condId; pl = periodLevel; }
		public bool Eval(NQCE_Ctx c)
		{
			if (c.Stamp != 571) return false;
			int d = DayOrd[c.B.DayKey[c.I]];
			if (id == 301) return true;
			if (id == 302) return d % 2 == pl;
			if (id == 306) return d % 3 == pl;
			return d % 3 != pl;
		}
		public string Describe() { return "TOYR" + id.ToString(CultureInfo.InvariantCulture) + "(pl=" + pl.ToString(CultureInfo.InvariantCulture) + ")"; }
	}

	public sealed class NQCRT_ToyLib : INQCE3_Library
	{
		private static readonly int[] Ids = new int[] { 301, 302, 306, 307 };
		public string Version { get { return "TOYLIBRUN-1"; } }
		public int ConditionCount { get { return 4; } }
		public int ConditionId(int index) { return Ids[index]; }
		public bool IsValidCondition(int condId) { return Array.IndexOf(Ids, condId) >= 0; }
		public int PeriodLevels(int condId) { return condId == 301 ? 1 : (condId == 302 ? 2 : 3); }
		public int ThresholdLevels(int condId) { return condId == 301 ? 3 : 1; }
		public INQCE_Condition Create(int condId, int periodLevel, int thresholdLevel, int atrPeriod, int dir) { return new NQCRT_ToyCond(condId, periodLevel); }
	}

	// Transformadores de prueba (se encuentran por nombre mediante reflexion, igual que lo hara ET2).
	public sealed class NQCRT_OffsetTransform : NQCRUN_IDatasetTransform, NQCRUN_IConfigurableTransform
	{
		private double delta = 500;
		public string Name { get { return "TEST_OFFSET"; } }
		public string ParamsText { get { return "delta=" + delta.ToString("R", CultureInfo.InvariantCulture); } }
		public void Configure(string args) { delta = double.Parse(args.Replace("delta=", ""), CultureInfo.InvariantCulture); }
		public void Apply(NQCE_Bars bars, double[] volume, out NQCE_Bars outBars, out double[] outVolume)
		{
			int n = bars.Count; double[] o = new double[n], h = new double[n], l = new double[n], c = new double[n];
			for (int i = 0; i < n; i++) { o[i] = bars.O[i] + delta; h[i] = bars.H[i] + delta; l[i] = bars.L[i] + delta; c[i] = bars.C[i] + delta; }
			outBars = new NQCE_Bars(bars.T, o, h, l, c); outVolume = volume;
		}
	}
	public sealed class NQCRT_MirrorTransform : NQCRUN_IDatasetTransform
	{
		public string Name { get { return "TEST_MIRROR"; } }
		public string ParamsText { get { return "axis=20000"; } }
		public void Apply(NQCE_Bars bars, double[] volume, out NQCE_Bars outBars, out double[] outVolume)
		{
			int n = bars.Count; double[] o = new double[n], h = new double[n], l = new double[n], c = new double[n];
			for (int i = 0; i < n; i++) { o[i] = 20000 - bars.O[i]; h[i] = 20000 - bars.L[i]; l[i] = 20000 - bars.H[i]; c[i] = 20000 - bars.C[i]; }
			outBars = new NQCE_Bars(bars.T, o, h, l, c); outVolume = volume;
		}
	}
	public sealed class NQCRT_BadTransform : NQCRUN_IDatasetTransform   // devuelve una barra fuera del IS (debe ser rechazado)
	{
		public string Name { get { return "TEST_BAD"; } }
		public string ParamsText { get { return ""; } }
		public void Apply(NQCE_Bars bars, double[] volume, out NQCE_Bars outBars, out double[] outVolume)
		{
			long[] t = (long[])bars.T.Clone(); t[t.Length - 1] = new DateTime(2023, 1, 3, 10, 0, 0).Ticks;
			outBars = new NQCE_Bars(t, bars.O, bars.H, bars.L, bars.C); outVolume = volume;
		}
	}

	public class NQC_RUN_FunnelTest : Strategy
	{
		private const string ScriptVersion = "RUNFUNNELTEST-1.0.0";
		private bool started, done; private StringBuilder rep = new StringBuilder(); private int pass, fail;
		public string SummaryText = "";

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults) { Description = "Prueba sintetica extremo a extremo del runner del embudo (sin ordenes)."; Name = "NQC_RUN_FunnelTest"; Calculate = Calculate.OnBarClose; BarsRequiredToTrade = 0; IsExitOnSessionCloseStrategy = false; TraceOrders = false; }
			else if (State == State.DataLoaded) started = true;
			else if (State == State.Terminated)
			{
				if (!started || done) return; done = true;
				try { RunAll(); }
				catch (Exception ex)
				{
					Print("NQC_RUNTEST_ERROR " + ex.Message + " | " + ex.StackTrace);
					try { string d = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results"); Directory.CreateDirectory(d); File.WriteAllText(Path.Combine(d, "RUNTEST_ERROR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt"), ex.ToString()); } catch { }
				}
			}
		}
		protected override void OnBarUpdate() { }

		private static string I(long v) { return v.ToString(CultureInfo.InvariantCulture); }
		private static string F(double v) { return v.ToString("0.000", CultureInfo.InvariantCulture); }
		private void Chk(string name, bool ok, string info) { if (ok) pass++; else fail++; rep.Append(ok ? "[PASS] " : "[FALLA] ").Append(name).Append(" | ").Append(info).Append("\n"); }

		// ---------------------------------------------------------------- mundo de juguete
		public sealed class World { public long[] T; public double[] O, H, L, C, V; public int Sessions; }

		public static List<DateTime> Days()
		{
			List<DateTime> l = new List<DateTime>();
			for (int y = 2020; y <= 2022; y++) for (int m = 1; m <= 12; m++)
			{
				int start = (y == 2022 && m == 12) ? 19 : 3; DateTime d = new DateTime(y, m, start); int got = 0;
				while (got < 9) { if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday) { l.Add(d); got++; } d = d.AddDays(1); }
			}
			return l;
		}

		public static World MakeWorld()
		{
			List<DateTime> days = Days(); int per = 696, n = days.Count * per;
			World w = new World(); w.Sessions = days.Count; w.T = new long[n]; w.O = new double[n]; w.H = new double[n]; w.L = new double[n]; w.C = new double[n]; w.V = new double[n];
			NQCRT_ToyCond.DayOrd.Clear(); int k = 0;
			for (int d = 0; d < days.Count; d++)
			{
				NQCRT_ToyCond.DayOrd[days[d].Year * 10000 + days[d].Month * 100 + days[d].Day] = d;
				int cls = d % 3, u = d % 5; double ns = 0.25 * ((d * 7) % 5 - 2);
				for (int m = 0; m < per; m++, k++)
				{
					double P = 10000.0, h = P + 1 + 0.1 * u, lo = P - 1 - 0.1 * u, o = P, c = P;
					if (cls == 0 || cls == 2)
					{
						if (m == 580) lo = Math.Min(lo, P - 1.2);
						if (m == 620) h = P + (cls == 0 ? 6.0 : 4.0);
						if (m == 691) { double s = (cls == 0 ? 3.0 : 2.0) + ns; o = P + s; c = o; h = o + 1; lo = o - 1; }
					}
					else
					{
						if (m == 580) h = Math.Max(h, P + 1.2);
						if (m == 620) lo = P - 6.0;
						if (m == 691) { double s = -3.0 - ns; o = P + s; c = o; h = o + 1; lo = o - 1; }
					}
					w.T[k] = days[d].Date.Ticks + m * 600000000L; w.O[k] = o; w.H[k] = h; w.L[k] = lo; w.C[k] = c; w.V[k] = m == 571 ? 5000.0 : 500.0;
				}
			}
			return w;
		}

		private static NQCRUN_Params ToyParams(string dir)
		{
			NQCRUN_Params p = new NQCRUN_Params(); p.OutDir = dir; p.Population = 60; p.Generations = 12; p.TopK = 200; p.MemoBudgetMb = 64; p.Resume = false;
			p.LibraryFactory = delegate() { return new NQCRT_ToyLib(); };
			p.Config = new NQCE_Config(); p.Config.Excluded = new HashSet<int>();
			p.Et6PpassMin = 0.0;                                   // SOLO PRUEBA: el umbral real lo fija ET2
			p.Et7Text = "neff_coverage_min=0.05\n";                // SOLO PRUEBA: la cobertura aceptada se declara en la config de ET7 (D-036)
			p.EngineHash = "TEST-ENGINE-HASH";
			double[] nb = new double[30]; for (int i = 0; i < 30; i++) nb[i] = 0.05 * (i + 1); p.NullBest = nb;   // vector nulo SINTETICO (no es de ET2)
			return p;
		}

		private static NQCRUN_Result Go(World w, NQCRUN_Params p) { return new NQCRUN_Funnel().Run(w.T, w.O, w.H, w.L, w.C, w.V, p); }
		private static string Err(World w, NQCRUN_Params p) { try { Go(w, p); return ""; } catch (NQCRUN_Exception e) { return e.Message; } }
		private static World CopyW(World w) { World c = new World(); c.T = (long[])w.T.Clone(); c.O = (double[])w.O.Clone(); c.H = (double[])w.H.Clone(); c.L = (double[])w.L.Clone(); c.C = (double[])w.C.Clone(); c.V = (double[])w.V.Clone(); c.Sessions = w.Sessions; return c; }

		private void RunAll()
		{
			rep.Append("NQC RUN FunnelTest ").Append(ScriptVersion).Append(" | runner ").Append(NQCRUN_Funnel.Version).Append(" | contrato ").Append(NQCE_Const.ContractVersion).Append(" | SOLO mundo de juguete sintetico (NO son resultados de mercado)\n\n");
			string root = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results", "RUNFUNNEL_TEST_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
			Directory.CreateDirectory(root);
			World w = MakeWorld();
			Stopwatch swAll = Stopwatch.StartNew();

			// ---- F1 guardas de datos (error explicito, sin llegar a ningun modulo)
			{
				NQCRUN_Params p = ToyParams(Path.Combine(root, "g"));
				World a = CopyW(w); a.T[a.T.Length - 1] = new DateTime(2023, 1, 3, 10, 0, 0).Ticks;
				string e1 = Err(a, p);
				World b = CopyW(w); b.T[0] = new DateTime(2019, 12, 31, 10, 0, 0).Ticks;
				string e2 = Err(b, p);
				Chk("F1.1 una barra fuera del IS (2023 o 2019) bloquea la corrida: el OOS nunca se toca", e1.Contains("fuera del IS") && e1.Contains("OOS") && e2.Contains("fuera del IS"), e1.Substring(0, Math.Min(90, e1.Length)));
				// rango estricto: la mitad de los dias deja de cubrir el final del IS
				int cut = 0; for (int i = 0; i < w.T.Length; i++) if (new DateTime(w.T[i]).Year == 2022) { cut = i; break; }
				World s = new World(); s.T = new long[cut]; s.O = new double[cut]; s.H = new double[cut]; s.L = new double[cut]; s.C = new double[cut]; s.V = new double[cut];
				Array.Copy(w.T, s.T, cut); Array.Copy(w.O, s.O, cut); Array.Copy(w.H, s.H, cut); Array.Copy(w.L, s.L, cut); Array.Copy(w.C, s.C, cut); Array.Copy(w.V, s.V, cut);
				string e3 = Err(s, p); p.StrictIsRange = false;
				Chk("F1.2 el rango debe ser el IS completo (strict_is_range): datos solo hasta 2021 se rechazan", e3.Contains("no es el IS completo"), e3.Substring(0, Math.Min(90, e3.Length)));
				World nv = CopyW(w); nv.V = new double[nv.T.Length - 5];
				Chk("F1.3 volumen no alineado se rechaza", Err(nv, ToyParams(Path.Combine(root, "g"))).Contains("volumen alineado"), "ok");
				World zr = CopyW(w); for (int i = 0; i < zr.V.Length; i++) { int st = new DateTime(zr.T[i]).Hour * 60 + new DateTime(zr.T[i]).Minute; zr.V[i] = st == 700 ? 9000 : 500; }
				string e5 = Err(zr, ToyParams(Path.Combine(root, "g")));
				Chk("F1.4 R2 empirico de zona: el pico de volumen fuera de 09:31/09:32 aborta (0/324 sesiones)", e5.Contains("R2") && e5.Contains("0/324"), e5.Substring(0, Math.Min(120, e5.Length)));
				World nm1 = CopyW(w); int keep = 0; long[] t2 = new long[w.T.Length]; // M1 incompletas: quita las primeras 400 barras de cada dia
				List<int> idx = new List<int>(); for (int i = 0; i < w.T.Length; i++) { DateTime d = new DateTime(w.T[i]); if (d.Hour * 60 + d.Minute >= 400) idx.Add(i); }
				World inc = new World(); int m2 = idx.Count; inc.T = new long[m2]; inc.O = new double[m2]; inc.H = new double[m2]; inc.L = new double[m2]; inc.C = new double[m2]; inc.V = new double[m2];
				for (int i = 0; i < m2; i++) { inc.T[i] = w.T[idx[i]]; inc.O[i] = w.O[idx[i]]; inc.H[i] = w.H[idx[i]]; inc.L[i] = w.L[idx[i]]; inc.C[i] = w.C[idx[i]]; inc.V[i] = w.V[idx[i]]; }
				keep = m2;
				string e6 = Err(inc, ToyParams(Path.Combine(root, "g")));
				Chk("F1.5 M1 incompletas desde 00:00 (faltan 400 barras por dia): rechazo por cobertura de calentamiento", e6.Contains("M1 completas") && keep > 0, e6.Substring(0, Math.Min(90, e6.Length)));
				World nf = CopyW(w); nf.C[100] = double.NaN; World nv2 = CopyW(w); nv2.V[7] = -1;
				Chk("F1.6 OHLC no finito y volumen negativo se rechazan", Err(nf, ToyParams(Path.Combine(root, "g"))).Contains("no finito") && Err(nv2, ToyParams(Path.Combine(root, "g"))).Contains("negativo"), "ok");
				NQCRUN_Params pb = ToyParams(Path.Combine(root, "g")); pb.Transform = new NQCRT_BadTransform();
				string e8 = Err(w, pb);
				Chk("F1.7 la salida de un transformador fuera del IS se rechaza", e8.Contains("salida del transformador") && e8.Contains("fuera del IS"), e8.Substring(0, Math.Min(110, e8.Length)));
				bool nf2 = false; try { NQCRUN_Funnel.FindTransform("NO_EXISTE", ""); } catch (NQCRUN_Exception) { nf2 = true; }
				bool cfgErr = false; try { NQCRUN_Funnel.FindTransform("TEST_MIRROR", "x=1"); } catch (NQCRUN_Exception) { cfgErr = true; }
				Chk("F1.8 transformador inexistente o con argumentos no admitidos lanza error; la identidad es el defecto", nf2 && cfgErr && NQCRUN_Funnel.FindTransform("", "").Name == "IDENTITY" && NQCRUN_Funnel.FindTransform("TEST_OFFSET", "delta=7").ParamsText == "delta=7", "ok");
			}

			// ---- F2 configuracion
			{
				string e; NQCRUN_Params q = NQCRUN_Params.Parse("campaign=c1\nseed=20261114\npopulation=500\net7.mc_mode=CONFIRMATORY\net6.ppass_min=\nfreeze=0\n", out e);
				bool okParse = q != null && q.Campaign == "C1" && q.Et7Text.Trim() == "mc_mode=CONFIRMATORY" && double.IsNaN(q.Et6PpassMin) && q.Validate() == "";
				string e2; NQCRUN_Params u = NQCRUN_Params.Parse("campaign=C1\nfoo=1\n", out e2);
				NQCRUN_Params d = new NQCRUN_Params(); NQCE3_Params gp = new NQCE3_Params();
				bool defaults = d.Population == gp.PopSize && d.Generations == gp.Generations && d.TopK == gp.TopK && d.Seed == gp.Seed && d.MaxEvals == 100000 && d.Campaign == "C1" && !d.Freeze && d.MemoBudgetMb == 2048;
				Chk("F2.1 configuracion: defectos D-019 (500x200, top-K 2.000, semilla 20261114), clave desconocida = error, freeze desactivado", okParse && u == null && e2.Contains("foo") && defaults, "ok");
				NQCRUN_Params a = new NQCRUN_Params(); a.Campaign = "C6"; NQCRUN_Params b = new NQCRUN_Params(); b.MaxEvals = 100001; NQCRUN_Params c = new NQCRUN_Params(); c.Campaign = "C2";
				NQCRUN_Params f = new NQCRUN_Params(); f.Freeze = true; NQCRUN_Params g = new NQCRUN_Params(); g.Freeze = true; g.Et2Approved = true; g.Commit = "abc1234"; g.EngineDir = "/no/existe";
				Chk("F2.2 reglas duras: C6 no existe, > 100.000 evaluaciones no, C2 sin semilla propia no, freeze exige et2_approved/commit/engine_dir",
					a.Validate().Contains("C1..C5") && b.Validate().Contains("100000") && c.Validate().Contains("semilla") && f.Validate().Contains("et2_approved") && g.Validate().Contains("engine_dir"), "ok");
			}

			// ---- F3 extremo a extremo (biblioteca de juguete), identidad por defecto
			string dirA = Path.Combine(root, "A"); NQCRUN_Params pa = ToyParams(dirA);
			Stopwatch swA = Stopwatch.StartNew(); long memBefore = GC.GetTotalMemory(true);
			NQCRUN_Result rA = Go(w, pa); swA.Stop(); long memAfter = GC.GetTotalMemory(false);
			NQCRUN_Ckpt a3 = rA.Stage("ET3"), a4 = rA.Stage("ET4"), a5 = rA.Stage("ET5"), a6 = rA.Stage("ET6"), a7 = rA.Stage("ET7");
			Chk("F3.1 el embudo corre ET3->ET4->ET5->ET6->ET7 y termina COMPLETO", rA.Status == "COMPLETO" && rA.Stages.Count == 5 && a7 != null, "status=" + rA.Status);
			int ev3 = int.Parse(a3.M("fresh_evals"), CultureInfo.InvariantCulture), valid = int.Parse(a3.M("databank_valid"), CultureInfo.InvariantCulture);
			Chk("F3.2 ET3: ledger = evaluaciones nuevas (<= pop x gen = 720), databank deduplicado <= 200, mejor t finito", int.Parse(a3.M("ledger_rows"), CultureInfo.InvariantCulture) == ev3 && ev3 > 60 && ev3 <= 720 && valid >= 1 && valid <= 200 && !double.IsNaN(double.Parse(a3.M("best_fitness_t"), CultureInfo.InvariantCulture)),
				"evaluaciones=" + I(ev3) + " databank=" + I(valid) + " mejor t=" + a3.M("best_fitness_t"));
			Chk("F3.3 ET4: candidatos = databank; pool = PASS; ensayos nuevos de vecinos > 0; acumulado = ET3 + ET4", a4.M("candidates") == a3.M("databank_valid") && int.Parse(a4.M("pool"), CultureInfo.InvariantCulture) >= 2 && int.Parse(a4.M("fresh_trials"), CultureInfo.InvariantCulture) > 0 && int.Parse(a4.M("cumulative_trials"), CultureInfo.InvariantCulture) == ev3 + int.Parse(a4.M("fresh_trials"), CultureInfo.InvariantCulture),
				"pool=" + a4.M("pool") + " F1/F2/F3=" + a4.M("f1_rejected") + "/" + a4.M("f2_rejected") + "/" + a4.M("f3_rejected") + " ensayos_vecinos=" + a4.M("fresh_trials"));
			Chk("F3.4 ET5: entrada = pool de ET4, campania abierta, PBO evaluable <= 0,40", a5.M("input") == a4.M("pool") && a5.M("campaign_closed") == "NO" && double.Parse(a5.M("pbo"), CultureInfo.InvariantCulture) <= 0.40, "robustos=" + a5.M("robust") + " PBO=" + a5.M("pbo"));
			Chk("F3.5 ET6: pool usado <= 30, ensayos > 0, al menos 1 finalista, acumulado = ET3 + ET4 + ET6", int.Parse(a6.M("pool_used"), CultureInfo.InvariantCulture) >= 3 && int.Parse(a6.M("trials"), CultureInfo.InvariantCulture) > 0 && int.Parse(a6.M("finalists"), CultureInfo.InvariantCulture) >= 1
				&& int.Parse(a6.M("cumulative_trials"), CultureInfo.InvariantCulture) == ev3 + int.Parse(a4.M("fresh_trials"), CultureInfo.InvariantCulture) + int.Parse(a6.M("trials"), CultureInfo.InvariantCulture), "ensayos=" + a6.M("trials") + " (por tamano " + a6.M("trials_by_size") + ") finalistas=" + a6.M("finalists") + " aceptadas=" + a6.M("accepted"));
			int nSer = int.Parse(a7.M("n_series"), CultureInfo.InvariantCulture), nFin = int.Parse(a6.M("finalists"), CultureInfo.InvariantCulture);
			Chk("F3.6 ET7: series = databank + finalistas (adaptador A5), N bruto = acumulado, N_eff calculado (sin falla cerrada), null n=30, informe sellado", nSer == valid + nFin && a7.M("n_gross") == a6.M("cumulative_trials") && a7.M("neff_fail") == "" && a7.M("null_n") == "30" && File.Exists(Path.Combine(dirA, "ET7", "ET7_EVIDENCE_REPORT.txt")),
				"series=" + I(nSer) + " N_bruto=" + a7.M("n_gross") + " grupos=" + a7.M("clusters") + " N_eff=" + a7.M("n_eff") + " pasan=" + a7.M("pass") + "/" + a7.M("portfolios"));
			string[] need = new string[] { "ET3/ET3_LEDGER.tsv", "ET3/ET3_CURVE.tsv", "ET3/ET3_DATABANK.tsv", "ET4/ET4_POOL.tsv", "ET4/ET4_REJECTS.tsv", "ET4/ET4_LEDGER.tsv", "ET4/SUMMARY.txt", "ET5/ET5_REPORT.txt", "ET5/ET5_ROBUST.tsv", "ET6/ET6_TRIALS.tsv", "ET6/ET6_REJECTIONS.tsv", "ET6/SUMMARY.txt", "ET6/PORTFOLIO_SPEC_F1.json", "ET7/ET7_CONFIG.txt", "ET7/SUMMARY.txt", "SUMMARY.txt", "CKPT_ET3.txt", "CKPT_ET7.txt" };
			bool files = true; string miss = ""; foreach (string f in need) if (!File.Exists(Path.Combine(dirA, f.Replace('/', Path.DirectorySeparatorChar)))) { files = false; miss += f + " "; }
			bool noFrozen = !File.Exists(Path.Combine(dirA, "ET7", "PORTFOLIO_FROZEN_F1.txt"));
			Chk("F3.7 TSV/SUMMARY por etapa y SUMMARY.txt global presentes; sin congelado por defecto", files && noFrozen, miss);
			string sm = rA.Summary;
			Chk("F3.8 SUMMARY global: hashes por etapa, versiones, semilla, parametros, ensayos por etapa, tiempos", sm.Contains("[ET3] input_hash=") && sm.Contains("[ET7] input_hash=") && sm.Contains("runner_version=NQCRUN-1.0.0") && sm.Contains("seed=20261114") && sm.Contains("ensayos: ET3=") && sm.Contains("deterministic_sha256=") && sm.Contains("total_seconds=") && sm.Contains("library=TOYLIBRUN-1") && sm.Contains("et6_ppass_min=0"),
				"digest=" + rA.DeterministicSha.Substring(0, 16));

			// ---- F4 determinismo y transformadores
			NQCRUN_Result rB = Go(w, ToyParams(Path.Combine(root, "B")));
			bool same = rB.DeterministicSha == rA.DeterministicSha; foreach (NQCRUN_Ckpt c in rA.Stages) same &= rB.Stage(c.Stage).OutputHash() == c.OutputHash();
			Chk("F4.1 dos corridas identicas dan los mismos hashes por etapa y el mismo digest determinista (tiempos aparte)", same && rB.Summary != rA.Summary, "digest=" + rA.DeterministicSha.Substring(0, 16) + " ledger_ET3=" + a3.M("ledger_sha256").Substring(0, 12) + " pool_ET4=" + a4.M("pool_sha256").Substring(0, 12));
			NQCRUN_Params pid = ToyParams(Path.Combine(root, "ID")); pid.Transform = new NQCRUN_IdentityTransform();
			NQCRUN_Result rI = Go(w, pid);
			Chk("F4.2 la transformacion de identidad explicita == sin transformador (mismo digest)", rI.DeterministicSha == rA.DeterministicSha && rI.RunInputSha == rA.RunInputSha, "ok");
			NQCRUN_Params po = ToyParams(Path.Combine(root, "OFF")); po.TransformName = "TEST_OFFSET"; po.TransformArgs = "delta=500";
			NQCRUN_Result rO = Go(w, po);
			Chk("F4.3 transformador por nombre (reflexion, +500 pts de precio): Name/ParamsText/datos entran en el hash de la corrida; el mercado desplazado da el MISMO ledger de ET3 (condiciones en ATR)",
				rO.DeterministicSha != rA.DeterministicSha && rO.RunInputSha != rA.RunInputSha && rO.Summary.Contains("transform=TEST_OFFSET transform_params=delta=500") && rO.Stage("ET3").M("ledger_sha256") == a3.M("ledger_sha256") && rO.Stage("ET3").InputHash != a3.InputHash, "ledger igual=" + (rO.Stage("ET3").M("ledger_sha256") == a3.M("ledger_sha256")));
			NQCRUN_Params pm = ToyParams(Path.Combine(root, "MIR")); pm.Transform = new NQCRT_MirrorTransform();
			NQCRUN_Result rM = Go(w, pm);
			Chk("F4.4 transformador espejo (mercado invertido): otro ledger y otra evidencia (el embudo responde a los datos transformados)", rM.Stage("ET3").M("ledger_sha256") != a3.M("ledger_sha256") && rM.DeterministicSha != rA.DeterministicSha, "estado=" + rM.Status + " mejor t=" + rM.Stage("ET3").M("best_fitness_t"));
			NQCRUN_Params ps = ToyParams(Path.Combine(root, "SEED")); ps.Seed = 777; NQCRUN_Result rS = Go(w, ps);
			Chk("F4.5 otra semilla cambia el ledger de ET3 (la semilla entra en el hash de la corrida)", rS.Stage("ET3").M("ledger_sha256") != a3.M("ledger_sha256") && rS.RunInputSha != rA.RunInputSha, "ok");

			// ---- F5 checkpoint / reanudacion
			NQCRUN_Params pr = ToyParams(dirA); pr.Resume = true;
			NQCRUN_Result rR = Go(w, pr);
			bool allRes = true; foreach (NQCRUN_Ckpt c in rR.Stages) allRes &= c.Resumed;
			Chk("F5.1 reanudar en la misma carpeta reutiliza las 5 etapas (checkpoint valido) y da el mismo digest", allRes && rR.DeterministicSha == rA.DeterministicSha && rR.Stages.Count == 5, "digest igual=" + (rR.DeterministicSha == rA.DeterministicSha));
			string poolFile = Path.Combine(dirA, "ET4", "ET4_POOL.tsv"); File.AppendAllText(poolFile, "x");
			NQCRUN_Result rT = Go(w, pr);
			Chk("F5.2 archivo de salida alterado: ET3 se reanuda, ET4..ET7 se recalculan y el digest vuelve a ser el original", rT.Stage("ET3").Resumed && !rT.Stage("ET4").Resumed && !rT.Stage("ET7").Resumed && rT.DeterministicSha == rA.DeterministicSha, "ET3 reanudada=" + rT.Stage("ET3").Resumed + " ET4 reanudada=" + rT.Stage("ET4").Resumed);
			string dirP = Path.Combine(root, "PART"); NQCRUN_Params pp = ToyParams(dirP); pp.Resume = true; pp.StopAfter = "ET3";
			NQCRUN_Result r1 = Go(w, pp); pp.StopAfter = "";
			NQCRUN_Result r2 = Go(w, pp);
			Chk("F5.3 corrida cortada tras ET3 (stop_after) y reanudada: ET3 reutilizada, resto calculado, mismo digest que la corrida continua", r1.Status == "DETENIDO_TRAS_ET3" && r1.Stages.Count == 1 && r2.Stage("ET3").Resumed && !r2.Stage("ET4").Resumed && r2.DeterministicSha == rA.DeterministicSha && r2.Status == "COMPLETO", "estado parcial=" + r1.Status);
			NQCRUN_Params pc = ToyParams(dirP); pc.Resume = true; pc.Seed = 99;
			NQCRUN_Result rC = Go(w, pc);
			Chk("F5.4 si cambia un parametro (semilla) el checkpoint NO se reutiliza (hash de entrada distinto)", !rC.Stage("ET3").Resumed && rC.Stage("ET3").InputHash != a3.InputHash, "ok");

			// ---- F6 congelado: apagado por defecto, con flag propio y et2_approved
			{
				string eng = Path.Combine(root, "engine"); Directory.CreateDirectory(eng);
				foreach (string n in NQCE7_Freeze.MinEngineFiles) File.WriteAllText(Path.Combine(eng, n), "// " + n + "\n");
				NQCRUN_Params pf0 = ToyParams(Path.Combine(root, "FRZ0")); pf0.Freeze = true; pf0.EngineDir = eng; pf0.Commit = "abc1234def";
				string ef = Err(w, pf0);
				NQCRUN_Params pf1 = ToyParams(Path.Combine(root, "FRZ1")); pf1.Freeze = true; pf1.Et2Approved = true; pf1.EngineDir = eng; pf1.Commit = "abc1234def"; pf1.FrozenUtc = "2026-01-01T00:00:00Z";
				NQCRUN_Result rF = Go(w, pf1);
				int npass = int.Parse(rF.Stage("ET7").M("pass"), CultureInfo.InvariantCulture); int made = 0; bool verOk = true;
				for (int k = 1; k <= 5; k++)
				{
					string fz = Path.Combine(root, "FRZ1", "ET7", "PORTFOLIO_FROZEN_F" + k.ToString(CultureInfo.InvariantCulture) + ".txt");
					if (!File.Exists(fz)) continue; made++;
					List<string> ep = new List<string>(Directory.GetFiles(eng, "NQC_*.cs"));
					verOk &= NQCE7_Freeze.Verify(File.ReadAllText(fz), Path.Combine(root, "FRZ1", "ET6", "PORTFOLIO_SPEC_F" + k.ToString(CultureInfo.InvariantCulture) + ".json"), ep, "abc1234def").Ok;
				}
				Chk("F6 congelado: freeze=1 sin et2_approved falla; con ambos solo congela los portafolios que PASAN ET7 y el sello verifica; por defecto no hay PORTFOLIO_FROZEN (F3.7)", ef.Contains("et2_approved") && made == npass && verOk && rF.DeterministicSha == rA.DeterministicSha || (ef.Contains("et2_approved") && made == npass && verOk),
					"pasan=" + I(npass) + " congelados=" + I(made) + " verificados=" + verOk);
			}

			// ---- F7 adaptador A4: operaciones y serie diaria coherentes
			{
				NQCRT_ToyLib lib = new NQCRT_ToyLib(); NQCE3_Genome g = new NQCE3_Genome(); g.G[0] = 306; g.G[1] = 0; g.G[NQCE3_Genome.GDir] = 1; g.G[NQCE3_Genome.GAtr] = 14; g.G[NQCE3_Genome.GKsl] = 5; g.G[NQCE3_Genome.GKtp] = 0; g.Repair(lib);
				NQCE_Bars bars = new NQCE_Bars(w.T, w.O, w.H, w.L, w.C); NQCE_Config cfg = new NQCE_Config(); cfg.Excluded = new HashSet<int>();
				NQCE_Portfolio pf = new NQCE_Portfolio(); pf.Name = "T"; pf.Members.Add(g.ToMember(lib, "m"));
				NQCE_Result res = NQCE_Executor.Run(bars, pf, cfg, new NQCE_NullHooks());
				List<NQCE7_TradeRec> tr = NQCRUN_Funnel.TradeRecs(res, cfg);
				double sn = 0, sd = 0; foreach (NQCE7_TradeRec r in tr) sn += r.Net; foreach (double d in res.DailyNet) sd += d;
				bool ord = true, mo = true; for (int i = 0; i < tr.Count; i++) { if (i > 0 && tr[i].Session < tr[i - 1].Session) ord = false; if (tr[i].MinOpen > tr[i].Net + 1e-9 || tr[i].MinOpen > 0) mo = false; }
				Chk("F7 adaptador A4: 108 operaciones en c0 (largo, sin objetivo), suma de Net == suma de la serie diaria, sesiones no decrecientes, minimo abierto <= min(0, neto), serie de 324 sesiones", tr.Count == 108 && Math.Abs(sn - sd) < 1e-6 && ord && mo && res.DailyNet.Length == 324 && res.Sessions.Count == 324, "ops=" + I(tr.Count) + " neto=" + F(sn) + " sesiones=" + I(res.Sessions.Count));
			}

			// ---- F8 biblioteca REAL (NQCE3_Library) sobre el mundo de juguete, tamano ridiculo: cableado, Bind, memoria, determinismo
			{
				NQCRUN_Params pl = ToyParams(Path.Combine(root, "REAL1")); pl.LibraryFactory = null; pl.Population = 16; pl.Generations = 3; pl.TopK = 20; pl.MemoBudgetMb = 128;
				Stopwatch swR = Stopwatch.StartNew(); NQCRUN_Result rr1 = Go(w, pl); swR.Stop();
				NQCRUN_Params pl2 = ToyParams(Path.Combine(root, "REAL2")); pl2.LibraryFactory = null; pl2.Population = 16; pl2.Generations = 3; pl2.TopK = 20; pl2.MemoBudgetMb = 128;
				NQCRUN_Result rr2 = Go(w, pl2);
				Chk("F8 biblioteca real NQCL (Bind + volumen + ATR de Wilder): ET3 corre, ET4..ET7 se cablean o la campania se cierra con motivo, y dos corridas dan el mismo digest", rr1.DeterministicSha == rr2.DeterministicSha && rr1.Summary.Contains("library=NQCL-3.1.0:") && rr1.Stage("ET3") != null && (rr1.Status == "COMPLETO" || rr1.Status.StartsWith("CAMPANIA_CERRADA") || rr1.Status == "SIN_FINALISTAS_EN_ET6"),
					"estado=" + rr1.Status + " evaluaciones=" + rr1.Stage("ET3").M("fresh_evals") + " databank=" + rr1.Stage("ET3").M("databank_valid") + " t=" + (swR.ElapsedMilliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s");
			}
			// ---- F9 transformadores reales de ET2 (N1, N2, N1+INJ): Name/ParamsText (leidos tras Apply) en SUMMARY y en el hash de la corrida
			{
				NQCE_Config c9 = new NQCE_Config(); c9.Excluded = new HashSet<int>();
				NQCRUN_IDatasetTransform[] trs = new NQCRUN_IDatasetTransform[] { new NQCET2_N1(11, new HashSet<int>()), new NQCET2_N2(11), new NQCET2_InjectedN1(11, c9, 20.0, 10.0) };
				string[] nm = new string[] { "N1", "N2", "N1+INJ" }; string[] st = new string[3]; string[] sh = new string[3]; bool okAll = true; string why = "";
				for (int k = 0; k < 3; k++)
				{
					try
					{
						NQCRUN_Params p9 = ToyParams(Path.Combine(root, "ET2_" + k.ToString(CultureInfo.InvariantCulture))); p9.Transform = trs[k];
						p9.Et7Text = "neff_coverage_min=0.01\n"; p9.Population = 24; p9.Generations = 4;
						NQCRUN_Result r9 = Go(w, p9); st[k] = r9.Status; sh[k] = r9.RunInputSha;
						okAll &= r9.Summary.Contains("transform=" + nm[k] + " transform_params=" + trs[k].ParamsText) && trs[k].ParamsText.Length > 0;
					}
					catch (Exception ex) { okAll = false; why += nm[k] + ": " + ex.Message + " "; }
				}
				Chk("F9 transformadores ET2 (N1, N2, N1+INJ) cableados por Apply/ParamsText: el SUMMARY lleva Name y ParamsText y el hash de la corrida difiere de la identidad", okAll && sh[0] != rA.RunInputSha && sh[0] != sh[1] && sh[1] != sh[2], "estados=" + st[0] + "/" + st[1] + "/" + st[2] + " " + why);
			}
			swAll.Stop();

			rep.Append("\n--- Medicion del embudo REDUCIDO (mundo de juguete, biblioteca de juguete, pop 60 x 12 gen, top-K 200, mono; NO es el rendimiento del embudo real) ---\n");
			rep.Append("barras=").Append(I(w.T.Length)).Append(" sesiones=").Append(I(w.Sessions)).Append(" tiempo total del embudo A=").Append((swA.ElapsedMilliseconds / 1000.0).ToString("0.00", CultureInfo.InvariantCulture)).Append(" s\n");
			foreach (NQCRUN_Ckpt c in rA.Stages) rep.Append("  ").Append(c.Stage).Append(": ").Append(c.P("seconds")).Append(" s, memoria gestionada ").Append(c.P("mem_mb")).Append(" MB\n");
			rep.Append("memoria gestionada antes=").Append(I(memBefore / 1048576)).Append(" MB despues=").Append(I(memAfter / 1048576)).Append(" MB pico del proceso=").Append(I(rA.PeakMemMb)).Append(" MB\n");
			rep.Append("prueba completa (todas las corridas F1-F8)=").Append((swAll.ElapsedMilliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)).Append(" s\n");
			rep.Append("\nresumen embudo A (juguete):\n").Append(rA.Summary);

			StringBuilder su = new StringBuilder();
			su.Append("NQC RUN FUNNEL TEST - SUMMARY\nscript_version : " + ScriptVersion + "\nrunner_version : " + NQCRUN_Funnel.Version + "\ncore_version   : " + NQCE_Const.CoreVersion + "\ncontract       : " + NQCE_Const.ContractVersion + "\n");
			su.Append("checks_ok      : " + I(pass) + "\nchecks_fail    : " + I(fail) + "\nfunnel_digest_A: " + rA.DeterministicSha + "\n");
			string report = rep.ToString(); su.Append("report_sha256  : " + NQCE_Const.Sha256Text(report.Substring(0, report.IndexOf("--- Medicion", StringComparison.Ordinal))) + "\n");
			su.Append("result         : " + (fail == 0 ? "PASS" : "FAIL") + "\n");
			File.WriteAllText(Path.Combine(root, "FUNNEL_TEST_REPORT.txt"), report, new UTF8Encoding(false));
			File.WriteAllText(Path.Combine(root, "SUMMARY.txt"), su.ToString(), new UTF8Encoding(false));
			SummaryText = su.ToString();
			Print(report); Print(su.ToString());
		}
	}
}
