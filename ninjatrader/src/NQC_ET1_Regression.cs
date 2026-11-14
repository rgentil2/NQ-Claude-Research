#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

//==============================================================================
// NQC_ET1_Regression  -  Arnes de regresion OBLIGATORIO del motor generico (acta ET1 §6)
// Contrato NQC 2.0.0, EXECUTOR_SPEC 2.3.0. Usa NQC_ET1_Core.cs y NQC_ET1C_AccountSim.cs.
//
// Estrategia SIN ordenes. Carga las barras M1 del IS y, con el MOTOR GENERICO (NQCE_Executor):
//   R1  ATR 5/14/20/50 y EMA 5/13 propios frente a NT8, todas las barras (diferencia 0).
//   R2  N1..N9 contra los libros nativos de ET1A: 8.110 de 8.110 operaciones iguales.
//   R3  46 saltos sobre el stop contra el libro nativo de la sonda (SHA registrado): 46 de 46.
//   R4  Determinismo: dos corridas completas dan el mismo hash de TRADES.
//   R5  Capa protectora y costo en el ejecutor (barras sinteticas, valores calculados a mano).
//   R6  Cuenta continua de la guardia de DD == NQC_AccountSim.RunFrom encadenado.
// Los 22 chequeos de ET1C NO se repiten aqui: se corre NQC_ET1C_AccountSim sin cambios
// (ver ninjatrader/runbooks/ET1_REGRESSION_RUNBOOK.md); el motor usa esa misma clase.
//
// Strategy Analyzer: NQ M1 Last, Merge back adjusted, 2020-01-01 a 2022-12-31 (igual que ET1B).
// Los resultados economicos de las referencias N1..N9 NO se miran.
// Salida: <Documentos>\NinjaTrader 8\NQC_Results\ET1REG_<fecha_hora>\
//   REGRESSION_REPORT.txt, SUMMARY.txt, ENGINE_N<id>_TRADES.tsv, ENGINE_GAP.tsv
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	// Portafolios de referencia N1..N9 expresados como genomas del motor generico.
	public static class NQCE_RefPortfolios
	{
		private static NQCE_Member M(string id, int dir, INQCE_Condition cond, int atr, double ksl, double ktp, int maxDay, int timeX)
		{
			NQCE_Member m = new NQCE_Member(); m.Id = id; m.Dir = dir; m.Conditions.Add(cond);
			m.AtrPeriod = atr; m.Ksl = ksl; m.Ktp = ktp; m.MaxPerDay = maxDay; m.TimeExitStamp = timeX;
			return m;
		}
		public static NQCE_Portfolio Make(int id)
		{
			NQCE_Portfolio p = new NQCE_Portfolio(); p.Name = "N" + id.ToString(CultureInfo.InvariantCulture);
			double na = double.NaN;
			switch (id)
			{
				case 1: p.Members.Add(M("A", 1, new NQCE_CondStamp(575), 14, 1.5, 3.0, 1, -1)); break;
				case 2: p.Members.Add(M("A", -1, new NQCE_CondStamp(580), 20, 2.0, 4.0, 1, -1)); break;
				case 3: p.Members.Add(M("A", 1, new NQCE_CondEmaCross(5, 13), 14, 2.0, na, 99, -1)); break;
				case 4: p.Members.Add(M("A", 1, new NQCE_CondCloseAboveRange(571, 585, 585), 14, 2.0, na, 1, -1)); break;
				case 5: p.Members.Add(M("A", 1, new NQCE_CondStamp(575), 5, 0.3, 0.3, 1, -1)); break;
				case 6: p.Members.Add(M("A", 1, new NQCE_CondStamp(689), 14, 3.0, na, 1, -1)); break;
				case 7: p.Members.Add(M("A", 1, new NQCE_CondStamp(580), 50, 6.0, na, 1, 630)); break;
				case 8:
					p.Members.Add(M("A", 1, new NQCE_CondStamp(575), 14, 1.5, 3.0, 1, -1));
					p.Members.Add(M("B", -1, new NQCE_CondStamp(578), 14, 2.5, 1.0, 1, -1));
					break;
				case 9: p.Members.Add(M("A", 1, new NQCE_CondStamp(575), 14, 1.5, 0.5, 1, -1)); break;
			}
			return p;
		}
	}

	public class NQC_ET1_Regression : Strategy
	{
		private const string ScriptVersion = "ET1REG-1.0.0";
		private const int ExpectedN1N9 = 8110;
		private const int ExpectedGaps = 46;
		private const string NativeGapSha256 = "e2aab0565b45ea228ecd38fedcf5ea38031d647a7291f9a47d299182f41475ca";
		private static readonly string[] Probes = new string[] {
			"2020-03-13 10:56:00,1,1.0",
			"2020-06-11 09:45:00,1,0.5",
			"2020-06-11 10:42:00,1,1.0",
			"2020-09-10 10:13:00,-1,0.5",
			"2020-09-10 10:18:00,-1,0.3",
			"2020-09-10 10:26:00,1,0.3",
			"2020-09-10 10:29:00,-1,0.5",
			"2020-09-10 10:35:00,1,1.0",
			"2020-09-10 11:18:00,1,0.3",
			"2020-10-20 10:15:00,1,0.3",
			"2020-10-21 09:44:00,-1,0.3",
			"2020-10-21 10:00:00,-1,0.5",
			"2020-10-21 10:37:00,1,0.3",
			"2020-10-21 11:01:00,1,0.5",
			"2020-12-10 09:39:00,-1,1.0",
			"2020-12-10 10:01:00,1,0.5",
			"2020-12-10 10:17:00,-1,1.0",
			"2020-12-10 10:20:00,-1,1.0",
			"2020-12-10 11:16:00,1,0.3",
			"2021-03-11 09:49:00,-1,1.0",
			"2021-03-11 10:23:00,1,0.5",
			"2021-03-11 10:53:00,-1,0.5",
			"2021-03-11 11:05:00,-1,0.5",
			"2021-03-11 11:18:00,1,0.3",
			"2021-06-10 09:32:00,-1,0.5",
			"2021-06-10 09:48:00,-1,0.5",
			"2021-06-10 10:03:00,1,0.5",
			"2021-06-10 11:01:00,-1,0.3",
			"2021-09-09 10:33:00,-1,0.5",
			"2021-09-09 11:09:00,-1,0.3",
			"2021-09-09 11:12:00,-1,0.3",
			"2021-12-09 11:06:00,-1,0.5",
			"2022-03-10 11:05:00,1,0.3",
			"2022-03-10 11:11:00,1,0.3",
			"2022-04-11 10:02:00,1,0.3",
			"2022-04-29 10:35:00,1,0.3",
			"2022-06-09 10:24:00,-1,0.5",
			"2022-06-09 10:59:00,1,0.3",
			"2022-06-09 11:17:00,1,0.5",
			"2022-06-09 11:23:00,-1,0.3",
			"2022-09-08 10:00:00,1,1.0",
			"2022-09-08 10:06:00,-1,0.3",
			"2022-09-08 10:10:00,-1,0.3",
			"2022-09-08 10:13:00,-1,0.3",
			"2022-09-08 10:21:00,1,0.5",
			"2022-09-08 11:17:00,-1,0.3"
		};

		private ATR a5, a14, a20, a50;
		private EMA nE5, nE13;
		private List<long> tT = new List<long>();
		private List<double> tO = new List<double>(), tH = new List<double>(), tL = new List<double>(), tC = new List<double>();
		private List<double> tV = new List<double>();
		private List<double> v5 = new List<double>(), v14 = new List<double>(), v20 = new List<double>(), v50 = new List<double>();
		private List<double> vE5 = new List<double>(), vE13 = new List<double>();
		private bool done;
		private string instName = "";
		private string tzId = "";
		private StringBuilder rep = new StringBuilder();

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Arnes de regresion del motor generico NQC (sin ordenes).";
				Name = "NQC_ET1_Regression";
				Calculate = Calculate.OnBarClose;
				BarsRequiredToTrade = 0;
				IsExitOnSessionCloseStrategy = false;
				TraceOrders = false;
			}
			else if (State == State.DataLoaded)
			{
				a5 = ATR(5); a14 = ATR(14); a20 = ATR(20); a50 = ATR(50);
				nE5 = EMA(5); nE13 = EMA(13);
				instName = Instrument.FullName;
				try { tzId = Bars.TradingHours.TimeZoneInfo.Id; } catch { tzId = ""; }
			}
			else if (State == State.Terminated)
			{
				try { RunAll(); }
				catch (Exception ex)
				{
					Print("NQC_ET1REG_ERROR " + ex.Message + " | " + ex.StackTrace);
					try
					{
						string d = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results");
						Directory.CreateDirectory(d);
						File.WriteAllText(Path.Combine(d, "ET1REG_ERROR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt"), ex.ToString());
					}
					catch { }
				}
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) return;
			DateTime c = Time[0];
			if (c > new DateTime(2022, 12, 31, 23, 59, 59)) return;
			tT.Add(c.Ticks);
			tV.Add(Volume[0]); tO.Add(Open[0]); tH.Add(High[0]); tL.Add(Low[0]); tC.Add(Close[0]);
			v5.Add(a5[0]); v14.Add(a14[0]); v20.Add(a20[0]); v50.Add(a50[0]);
			vE5.Add(nE5[0]); vE13.Add(nE13[0]);
		}

		private static string I(long x) { return x.ToString(CultureInfo.InvariantCulture); }
		private static string F(DateTime d) { return d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); }
		private static bool Eq(double a, double b) { return Math.Abs(a - b) < 1e-9; }

		private static long CountBig(double[] own, List<double> nt, out double mx)
		{
			mx = 0; long big = 0;
			for (int i = 0; i < own.Length; i++)
			{
				double d = Math.Abs(own[i] - nt[i]);
				if (d > mx) mx = d;
				if (d > 1e-7) big++;
			}
			return big;
		}

		private class NatRow { public int Dir; public double Entry, Stop, ExitPx; public string Name; public long ExitTime; }

		// Lee un libro nativo (TRADES_NATIVE.tsv, 13 columnas) -> clave = instante de senal.
		private static Dictionary<long, NatRow> ParseNative(string file)
		{
			Dictionary<long, NatRow> m = new Dictionary<long, NatRow>();
			string[] lines = File.ReadAllText(file).Split('\n');
			for (int k = 1; k < lines.Length; k++)
			{
				string ln = lines[k].TrimEnd('\r');
				if (ln.Length == 0) continue;
				string[] c = ln.Split('\t');
				if (c.Length < 13) continue;
				NatRow r = new NatRow();
				r.Dir = int.Parse(c[2], CultureInfo.InvariantCulture);
				long sig = DateTime.ParseExact(c[3], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture).Ticks;
				r.Entry = double.Parse(c[6], CultureInfo.InvariantCulture);
				r.ExitTime = DateTime.ParseExact(c[7], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture).Ticks;
				r.ExitPx = double.Parse(c[9], CultureInfo.InvariantCulture);
				r.Name = c[10];
				r.Stop = double.Parse(c[12], CultureInfo.InvariantCulture);
				m[sig] = r;
			}
			return m;
		}

		private static Dictionary<long, NatRow> LoadNativeN(int id, out string folder)
		{
			folder = null;
			string root = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results");
			if (!Directory.Exists(root)) return null;
			string[] dirs = Directory.GetDirectories(root, "ET1A_N" + I(id) + "_*");
			Array.Sort(dirs); Array.Reverse(dirs);
			foreach (string d in dirs)
			{
				string f = Path.Combine(d, "TRADES_NATIVE.tsv");
				if (!File.Exists(f)) continue;
				folder = d;
				return ParseNative(f);
			}
			return null;
		}

		private static Dictionary<long, NatRow> LoadNativeGap(out string folder, out string sha, out bool shaOk)
		{
			folder = null; sha = null; shaOk = false;
			string root = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results");
			if (!Directory.Exists(root)) return null;
			string[] dirs = Directory.GetDirectories(root, "ET1A_GAP_*");
			Array.Sort(dirs); Array.Reverse(dirs);
			string pick = null, pickSha = null;
			foreach (string d in dirs)
			{
				string f = Path.Combine(d, "TRADES_NATIVE.tsv");
				if (!File.Exists(f)) continue;
				string h;
				using (SHA256 s = SHA256.Create()) h = NQCE_Const.Hex(s.ComputeHash(File.ReadAllBytes(f)));
				if (pick == null) { pick = d; pickSha = h; }
				if (h == NativeGapSha256) { pick = d; pickSha = h; break; }
			}
			if (pick == null) return null;
			folder = pick; sha = pickSha; shaOk = pickSha == NativeGapSha256;
			return ParseNative(Path.Combine(pick, "TRADES_NATIVE.tsv"));
		}

		private NQCE_News LoadNews()
		{
			string root = NinjaTrader.Core.Globals.UserDataDir, path = null;
			foreach (string f in new string[] { "NQC_Data", "NQC Data" })
			{
				string p = Path.Combine(Path.Combine(root, f), NQCE_Const.NewsFileName);
				if (File.Exists(p)) { path = p; break; }
			}
			if (path == null) { NQCE_News n = new NQCE_News(); n.Status = "FALTA " + NQCE_Const.NewsFileName; return n; }
			return NQCE_News.FromBytes(File.ReadAllBytes(path), NQCE_Const.NewsSha256);
		}

		// ---- bloque R5: barras sinteticas, dos sesiones habiles (2022-01-04 y 2022-01-05), 124 barras (09:29..11:32) cada una.
		// Barra por defecto: O=C=20000, H=20002, L=19998  => ATR(14) = 4 constante.
		private static NQCE_Bars SynthBars()
		{
			List<long> t = new List<long>(); List<double> o = new List<double>(), h = new List<double>(), l = new List<double>(), c = new List<double>();
			int[] daysD = new int[] { 4, 5 };
			for (int d = 0; d < 2; d++)
			{
				DateTime day = new DateTime(2022, 1, daysD[d]);
				for (int st = NQCE_Const.StampLoadFrom; st <= NQCE_Const.StampLoadTo; st++)
				{
					double bo = 20000, bh = 20002, bl = 19998, bc = 20000;
					if (d == 0 && st == 577) { bo = 20000; bh = 20000; bl = 19990; bc = 19990; }
					if (d == 0 && st == 578) { bo = 19990; bh = 19990; bl = 19963; bc = 19963; }
					if (d == 0 && st == 579) { bo = 19963; bh = 19965; bl = 19961; bc = 19963; }
					t.Add(day.AddMinutes(st).Ticks); o.Add(bo); h.Add(bh); l.Add(bl); c.Add(bc);
				}
			}
			return new NQCE_Bars(t.ToArray(), o.ToArray(), h.ToArray(), l.ToArray(), c.ToArray());
		}

		private static NQCE_Portfolio SynthPortfolio()
		{
			NQCE_Portfolio p = new NQCE_Portfolio(); p.Name = "SYNTH";
			NQCE_Member a = new NQCE_Member(); a.Id = "A"; a.Dir = 1; a.Conditions.Add(new NQCE_CondStamp(575)); a.AtrPeriod = 14; a.Ksl = 10.0; a.MaxPerDay = 1;
			NQCE_Member b = new NQCE_Member(); b.Id = "B"; b.Dir = 1; b.Conditions.Add(new NQCE_CondStamp(585)); b.AtrPeriod = 14; b.Ksl = 1.0; b.MaxPerDay = 1;
			p.Members.Add(a); p.Members.Add(b);
			return p;
		}

		private int Wf(NQCE_Result r, string key, int col)
		{
			int[] a;
			if (!r.Waterfall.TryGetValue(key, out a)) return 0;
			return a[col];
		}

		private int ProtCount(NQCE_Result r, string kind)
		{
			int n = 0; foreach (NQCE_ProtectEvent e in r.Protect) if (e.Kind == kind) n++; return n;
		}

		private int synPass, synFail;
		private void Chk(string name, bool ok, string info)
		{
			if (ok) synPass++; else synFail++;
			rep.Append((ok ? "OK    " : "FALLA ") + name + "  " + info + "\n");
		}

		private bool SyntheticChecks()
		{
			NQCE_Bars sb = SynthBars();
			NQCE_Config cfg = new NQCE_Config(); cfg.Protect = true; cfg.News = null; cfg.Excluded = new HashSet<int>();
			NQCE_Result r = NQCE_Executor.Run(sb, SynthPortfolio(), cfg, null);
			Chk("R5.1 sesiones y operaciones", r.Sessions.Count == 2 && r.Trades.Count == 2 && r.OpenAtEnd == 0, "sesiones=" + I(r.Sessions.Count) + " ops=" + I(r.Trades.Count) + " abiertas_al_fin=" + I(r.OpenAtEnd));
			if (r.Trades.Count == 2)
			{
				NQCE_Trade t0 = r.Trades[0], t1 = r.Trades[1];
				Chk("R5.2 cierre diario: motivo y precio (salida a la apertura 19963)", t0.Reason == "DAILY_CLOSE" && Eq(t0.ExitPx, 19963.0), t0.Reason + "@" + t0.ExitPx.ToString("0.00", CultureInfo.InvariantCulture));
				Chk("R5.3 neto de la operacion con cierre = -37 pts x 20 - 24,68", Math.Abs(t0.Net - (-764.68)) < 1e-6, t0.Net.ToString("0.00", CultureInfo.InvariantCulture));
				Chk("R5.4 liberacion: la sesion siguiente vuelve a operar (D-007)", t1.DayKey == 20220105 && t1.Reason == "TIME", I(t1.DayKey) + " " + t1.Reason);
				Chk("R5.5 friccion exactamente una vez: salida plana 11:30 = -24,68", Math.Abs(t1.Net - (-24.68)) < 1e-6 && Eq(t1.EntryPx, t1.ExitPx), t1.Net.ToString("0.00", CultureInfo.InvariantCulture));
			}
			else { Chk("R5.2-5 (omitidos: no hay 2 operaciones)", false, ""); }
			Chk("R5.6 cascada: B bloqueada por cierre diario el dia 1 y BUSY el dia 2", Wf(r, "202201|B", 5) == 1 && Wf(r, "202201|B", 3) == 1, "DAILY_CLOSE=" + I(Wf(r, "202201|B", 5)) + " BUSY=" + I(Wf(r, "202201|B", 3)));
			Chk("R5.7 eventos protectores del dia 1: DAILY_CLOSE y PAUSE una vez; ninguno el dia 2", ProtCount(r, "DAILY_CLOSE") == 1 && ProtCount(r, "PAUSE") == 1 && r.Protect.Count == 2, "eventos=" + I(r.Protect.Count));

			NQCE_Config c2 = new NQCE_Config(); c2.Protect = true; c2.Excluded = new HashSet<int>(); c2.Cushion = 5000;
			NQCE_Result r2 = NQCE_Executor.Run(SynthBars(), SynthPortfolio(), c2, null);
			Chk("R5.8 guardia de DD (colchon 5000 > room 2000): sin operaciones, una vez por sesion", r2.Trades.Count == 0 && Wf(r2, "202201|A", 6) == 2 && Wf(r2, "202201|B", 6) == 2 && ProtCount(r2, "DD_GUARD") == 2,
				"ops=" + I(r2.Trades.Count) + " guard_A=" + I(Wf(r2, "202201|A", 6)) + " eventos=" + I(ProtCount(r2, "DD_GUARD")));

			NQCE_Config c3 = new NQCE_Config(); c3.Protect = true; c3.Excluded = new HashSet<int>(); c3.CostMult = 1.5;
			NQCE_Result r3 = NQCE_Executor.Run(SynthBars(), SynthPortfolio(), c3, null);
			bool okStress = r3.Trades.Count == 2 && Math.Abs(r3.Trades[1].Net - (-37.02)) < 1e-6;
			Chk("R5.9 estres 1,5x: salida plana = -37,02", okStress, r3.Trades.Count == 2 ? r3.Trades[1].Net.ToString("0.00", CultureInfo.InvariantCulture) : "sin ops");

			NQCE_Config c4 = new NQCE_Config(); c4.Protect = false; c4.Excluded = new HashSet<int>();
			NQCE_Result r4 = NQCE_Executor.Run(SynthBars(), SynthPortfolio(), c4, null);
			Chk("R5.10 sin capa protectora no hay eventos ni bloqueos (regresion N1..N9 corre asi)", r4.Protect.Count == 0 && r4.Trades.Count >= 2, "ops=" + I(r4.Trades.Count) + " eventos=" + I(r4.Protect.Count));
			SynthM2mCheck();
			return synFail == 0;
		}

		// R5.11 (correccion DdM2m, ET1 2.0.0-r2): sesion con una operacion cerrada (A, stop) y otra abierta (B) en el mismo dia, valores a mano.
		// Dia 1: A entra 20000, stop 19960 (10 x ATR 4) -> -40 x 20 - 24,68 = -824,68. B entra 19960 (comision incluida en M2M: -24,68). Valle M2M = -849,36.
		// Dia 2: A abierta plana todo el dia: eq = -24,68; M2M acumulado = -849,36 - 24,68 = -874,04. Pico = 0 -> DdM2m = 874,04 (con el defecto cuenta doble el cierre del dia 1: >1600).
		private void SynthM2mCheck()
		{
			List<long> t = new List<long>(); List<double> o = new List<double>(), h = new List<double>(), l = new List<double>(), c = new List<double>();
			int[] daysD = new int[] { 4, 5 };
			for (int d = 0; d < 2; d++)
			{
				DateTime day = new DateTime(2022, 1, daysD[d]);
				for (int st = NQCE_Const.StampLoadFrom; st <= NQCE_Const.StampLoadTo; st++)
				{
					double bo = 20000, bh = 20002, bl = 19998, bc = 20000;
					if (d == 0 && st == 577) { bo = 20000; bh = 20000; bl = 19990; bc = 19990; }
					if (d == 0 && st == 578) { bo = 19990; bh = 19990; bl = 19955; bc = 19960; }
					if (d == 0 && st > 578) { bo = 19960; bh = 19962; bl = 19958; bc = 19960; }
					if (d == 0 && st == 600) { bo = 19960; bh = 19972; bl = 19960; bc = 19970; }
					t.Add(day.AddMinutes(st).Ticks); o.Add(bo); h.Add(bh); l.Add(bl); c.Add(bc);
				}
			}
			NQCE_Config cfg = new NQCE_Config(); cfg.Protect = false; cfg.News = null; cfg.Excluded = new HashSet<int>();
			NQCE_Result r = NQCE_Executor.Run(new NQCE_Bars(t.ToArray(), o.ToArray(), h.ToArray(), l.ToArray(), c.ToArray()), SynthPortfolio(), cfg, null);
			bool ok = r.Trades.Count == 3 && Math.Abs(r.Trades[0].Net - (-824.68)) < 1e-6;
			Chk("R5.11a sesion con cierre y operacion abierta: A cerrada por stop = -824,68", ok, r.Trades.Count == 3 ? r.Trades[0].Net.ToString("0.00", CultureInfo.InvariantCulture) : "ops=" + I(r.Trades.Count));
			Chk("R5.11b DdM2m del nucleo = 874,04 (sin doble conteo del P&L cerrado)", Math.Abs(r.DdM2m - 874.04) < 1e-6, r.DdM2m.ToString("0.00", CultureInfo.InvariantCulture));
			Chk("R5.11c DD cerrado = 874,04 (trayectoria cerrada -824,68, -849,36, -874,04; no cambia con la correccion)", Math.Abs(r.DdClosed - 874.04) < 1e-6, r.DdClosed.ToString("0.00", CultureInfo.InvariantCulture));
		}

		private bool TrackerCheck()
		{
			NQC_AccountProfile p = NQC_AccountProfile.RapidIntraday50K();
			List<NQC_SessionRow> rows = new List<NQC_SessionRow>();
			NQCE_Rng rng = new NQCE_Rng(7);
			for (int i = 0; i < 400; i++)
			{
				double g = 0; for (int k = 0; k < 12; k++) g += rng.NextDouble(); g -= 6.0;
				double g2 = 0; for (int k = 0; k < 12; k++) g2 += rng.NextDouble(); g2 -= 6.0;
				rows.Add(new NQC_SessionRow(30 + 300 * g, -Math.Abs(150 + 100 * g2), 1));
			}
			NQCE_AccountTracker tr = new NQCE_AccountTracker(p);
			int s = 0, segs = 0; bool ok = true;
			while (s < rows.Count)
			{
				NQC_AccResult r = NQC_AccountSim.RunFrom(rows, s, p);
				for (int k = 0; k < r.Sessions; k++)
				{
					string ev = tr.Update(rows[s + k].Pnl, rows[s + k].MinEq, rows[s + k].N);
					bool last = k == r.Sessions - 1;
					if (r.Status == "CENSORED") { if (ev != "") ok = false; }
					else if (last) { string want = r.Status == "PASS" ? "PASS" : "FAIL:" + r.Why; if (ev != want) ok = false; }
					else if (ev != "") ok = false;
				}
				if (r.Status == "CENSORED") break;
				segs++; s += r.Sessions;
			}
			Chk("R6 tracker continuo == RunFrom encadenado (400 sesiones, semilla 7)", ok && segs > 0, "tramos decididos=" + I(segs));
			return ok && segs > 0;
		}

		private void RunAll()
		{
			if (done || tT.Count == 0) return;
			done = true;
			int n = tT.Count;
			// R2 empirico de zona horaria (coherente con ET0 G4): el maximo de volumen 09:16-10:00 debe caer en la barra 09:31/09:32.
			// TradingHours.TimeZoneInfo es la zona de la plantilla de horario, NO la de Time[0]; no se usa para abortar.
			int tzSess = 0, tzHit = 0; double tzVol = 0;
			{
				long curDay = -1; double bestV = -1; int bestM = -1;
				for (int i = 0; i <= n; i++)
				{
					long day = i < n ? new DateTime(tT[i]).Date.Ticks : -2;
					if (day != curDay)
					{
						if (bestM >= 0) { tzSess++; if (bestM == 571 || bestM == 572) tzHit++; }
						curDay = day; bestV = -1; bestM = -1;
					}
					if (i >= n) break;
					DateTime dt = new DateTime(tT[i]);
					int mm = dt.Hour * 60 + dt.Minute;
					tzVol += tV[i];
					if (mm >= 556 && mm <= 600 && tV[i] > bestV && tV[i] > 0) { bestV = tV[i]; bestM = mm; }
				}
			}
			bool tzUsable = tzVol > 0 && tzSess >= 20;
			double tzFrac = tzUsable ? 100.0 * tzHit / tzSess : 0;
			string tzLine = "zona plantilla horario (informativo, no es la zona de las horas de barra): " + tzId;
			string tzRes = tzUsable ? "R2 zona de barras (empirico): pico de volumen 09:16-10:00 en barra 09:31/09:32 en " + tzHit.ToString(CultureInfo.InvariantCulture) + "/" + tzSess.ToString(CultureInfo.InvariantCulture) + " sesiones = " + tzFrac.ToString("0.00", CultureInfo.InvariantCulture) + "% (exigido >= 70%)"
				: "R2 zona de barras (empirico): NO VERIFICABLE (serie sin volumen utilizable, sesiones=" + tzSess.ToString(CultureInfo.InvariantCulture) + "); se continua sin abortar";
			if (tzUsable && tzFrac < 70.0)
			{
				string msg = "ABORTADO (R2): la hora de las barras no parece Eastern (" + tzRes + "). Revisa Tools > Options > General > Time zone de NT8 y repite.";
				Print("NQC_ET1REG_ERROR " + msg);
				try
				{
					string ed = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results");
					Directory.CreateDirectory(ed);
					File.WriteAllText(Path.Combine(ed, "ET1REG_ABORT_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt"), msg + "\n" + tzLine + "\n");
				}
				catch { }
				return;
			}
			NQCE_Bars bars = new NQCE_Bars(tT.ToArray(), tO.ToArray(), tH.ToArray(), tL.ToArray(), tC.ToArray());

			rep.Append("NQC ET1 REGRESSION - REGRESSION_REPORT\n");
			rep.Append("script_version : " + ScriptVersion + "\ncore_version   : " + NQCE_Const.CoreVersion + "\ncontract       : " + NQCE_Const.ContractVersion + "\nexecutor_spec  : " + NQCE_Const.SpecVersion + "\n");
			rep.Append("instrument     : " + instName + "\n" + tzLine + "\n" + tzRes + "\nbars           : " + I(n) + "\nfirst_bar      : " + F(new DateTime(bars.T[0])) + "\nlast_bar       : " + F(new DateTime(bars.T[n - 1])) + "\n\n");

			// ---- R1 indicadores
			rep.Append("[R1 indicadores: motor generico frente a NT8, todas las barras]\n");
			double m5, m14, m20, m50, me5, me13;
			long b5 = CountBig(bars.Atr(5), v5, out m5), b14 = CountBig(bars.Atr(14), v14, out m14), b20 = CountBig(bars.Atr(20), v20, out m20), b50 = CountBig(bars.Atr(50), v50, out m50);
			long be5 = CountBig(bars.Ema(5), vE5, out me5), be13 = CountBig(bars.Ema(13), vE13, out me13);
			rep.Append("ATR5  max|dif| " + m5.ToString("0.000000000", CultureInfo.InvariantCulture) + " barras>1e-7: " + I(b5) + "\n");
			rep.Append("ATR14 max|dif| " + m14.ToString("0.000000000", CultureInfo.InvariantCulture) + " barras>1e-7: " + I(b14) + "\n");
			rep.Append("ATR20 max|dif| " + m20.ToString("0.000000000", CultureInfo.InvariantCulture) + " barras>1e-7: " + I(b20) + "\n");
			rep.Append("ATR50 max|dif| " + m50.ToString("0.000000000", CultureInfo.InvariantCulture) + " barras>1e-7: " + I(b50) + "\n");
			rep.Append("EMA5  max|dif| " + me5.ToString("0.000000000", CultureInfo.InvariantCulture) + " barras>1e-7: " + I(be5) + "\n");
			rep.Append("EMA13 max|dif| " + me13.ToString("0.000000000", CultureInfo.InvariantCulture) + " barras>1e-7: " + I(be13) + "\n");
			bool indOk = (b5 + b14 + b20 + b50 + be5 + be13) == 0;

			NQCE_News news = LoadNews();
			rep.Append("\n[Calendario] " + news.Status + "\n");

			string outDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results", "ET1REG_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
			Directory.CreateDirectory(outDir);
			UTF8Encoding enc = new UTF8Encoding(false);

			bool allOk = indOk && news.Ok;
			int natTotal = 0, eqTotal = 0; bool nativesFound = true;
			string hash1 = "", hash2 = "";
			StringBuilder h1 = new StringBuilder();

			// ---- R2 N1..N9 (primera pasada: compara; segunda pasada: determinismo)
			rep.Append("\n[R2 paridad N1..N9 con el motor generico, capa protectora apagada]\n");
			for (int id = 1; id <= 9; id++)
			{
				NQCE_Config cfg = new NQCE_Config(); cfg.Protect = false; cfg.News = news.Ok ? news : null;
				NQCE_Result res = NQCE_Executor.Run(bars, NQCE_RefPortfolios.Make(id), cfg, null);
				string tsv = NQCE_Audit.TradesTsv(res);
				h1.Append(tsv);
				File.WriteAllText(Path.Combine(outDir, "ENGINE_N" + I(id) + "_TRADES.tsv"), tsv, enc);

				string folder;
				Dictionary<long, NatRow> nat = LoadNativeN(id, out folder);
				if (nat == null) { rep.Append("N" + I(id) + ": falta el libro nativo; motor " + I(res.Trades.Count) + " ops\n"); nativesFound = false; continue; }
				int both = 0, eqDir = 0, eqEntry = 0, eqStop = 0, eqName = 0, eqTime = 0, eqPx = 0, eqAll = 0, onlyEng = 0;
				List<string> bad = new List<string>();
				HashSet<long> seen = new HashSet<long>();
				foreach (NQCE_Trade t in res.Trades)
				{
					NatRow r;
					if (!nat.TryGetValue(t.SigTick, out r)) { onlyEng++; if (bad.Count < 3) bad.Add("solo motor " + F(new DateTime(t.SigTick))); continue; }
					seen.Add(t.SigTick); both++;
					bool a = r.Dir == t.Dir, b = Eq(r.Entry, t.EntryPx), c2 = Eq(r.Stop, t.Stop), d2 = r.Name == t.Reason, e2 = r.ExitTime == t.ExitTick, f2 = Eq(r.ExitPx, t.ExitPx);
					if (a) eqDir++; if (b) eqEntry++; if (c2) eqStop++; if (d2) eqName++; if (e2) eqTime++; if (f2) eqPx++;
					if (a && b && c2 && d2 && e2 && f2) eqAll++;
					else if (bad.Count < 3) bad.Add("difiere " + F(new DateTime(t.SigTick)) + " nat=" + r.Name + "@" + r.ExitPx.ToString("0.00", CultureInfo.InvariantCulture) + " motor=" + t.Reason + "@" + t.ExitPx.ToString("0.00", CultureInfo.InvariantCulture));
				}
				int onlyNat = nat.Count - seen.Count;
				bool ok = onlyEng == 0 && onlyNat == 0 && eqAll == both && both == nat.Count && res.OpenAtEnd == 0;
				if (!ok) allOk = false;
				natTotal += nat.Count; eqTotal += eqAll;
				rep.Append("N" + I(id) + (ok ? "  OK " : "  FALLA ") + " nativo=" + I(nat.Count) + " motor=" + I(res.Trades.Count) + " iguales=" + I(eqAll) + " soloNat=" + I(onlyNat) + " soloMotor=" + I(onlyEng)
					+ " dir=" + I(eqDir) + " ent=" + I(eqEntry) + " stop=" + I(eqStop) + " nom=" + I(eqName) + " hora=" + I(eqTime) + " px=" + I(eqPx) + " abiertas_al_fin=" + I(res.OpenAtEnd) + "\n");
				foreach (string s in bad) rep.Append("    " + s + "\n");
			}
			bool totalsOk = nativesFound && natTotal == ExpectedN1N9 && eqTotal == ExpectedN1N9;
			rep.Append("TOTAL N1..N9: nativo=" + I(natTotal) + " iguales=" + I(eqTotal) + " esperado=" + I(ExpectedN1N9) + (totalsOk ? "  OK" : "  FALLA") + "\n");
			if (!totalsOk) allOk = false;
			hash1 = NQCE_Const.Sha256Text(h1.ToString());

			// ---- R4 determinismo (segunda corrida completa)
			StringBuilder h2b = new StringBuilder();
			for (int id = 1; id <= 9; id++)
			{
				NQCE_Config cfg = new NQCE_Config(); cfg.Protect = false; cfg.News = news.Ok ? news : null;
				h2b.Append(NQCE_Audit.TradesTsv(NQCE_Executor.Run(bars, NQCE_RefPortfolios.Make(id), cfg, null)));
			}
			hash2 = NQCE_Const.Sha256Text(h2b.ToString());
			bool detOk = hash1 == hash2;
			if (!detOk) allOk = false;
			rep.Append("\n[R4 determinismo] hash corrida 1 = " + hash1 + "\n                  hash corrida 2 = " + hash2 + (detOk ? "  OK" : "  FALLA") + "\n");

			// ---- R3 saltos
			rep.Append("\n[R3 saltos sobre el stop: motor generico frente al libro nativo de la sonda]\n");
			string gfolder, gsha; bool gshaOk;
			Dictionary<long, NatRow> gnat = LoadNativeGap(out gfolder, out gsha, out gshaOk);
			int gEqual = 0, gGapped = 0, gEngine = 0, gNat = 0;
			bool gapOk = false;
			if (gnat == null) rep.Append("FALTA ET1A_GAP_*\\TRADES_NATIVE.tsv en NQC_Results\n");
			else
			{
				gNat = gnat.Count;
				rep.Append("libro nativo: sha256=" + gsha + " sha_ok=" + (gshaOk ? "SI" : "NO") + "\n");
				double[] atr14 = bars.Atr(14);
				StringBuilder gt = new StringBuilder();
				gt.Append("ref\tdir\tsignalTime\tentryPrice\tstopPrice\texitName\texitExecTime\texitPrice\tgapped\n");
				List<string> bad = new List<string>();
				HashSet<long> seen = new HashSet<long>();
				foreach (string pr in Probes)
				{
					string[] f = pr.Split(',');
					long sig = DateTime.ParseExact(f[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture).Ticks;
					int dir = int.Parse(f[1], CultureInfo.InvariantCulture);
					double k = double.Parse(f[2], CultureInfo.InvariantCulture);
					NQCE_Fill.IsolatedTrade t = NQCE_Fill.SimulateIsolated(bars, atr14, sig, dir, k);
					if (!t.Ok) { if (bad.Count < 5) bad.Add("motor sin operacion " + f[0]); continue; }
					gEngine++; if (t.Gapped) gGapped++;
					gt.Append("G\t").Append(I(t.Dir)).Append('\t').Append(F(new DateTime(t.Sig))).Append('\t').Append(t.Entry.ToString("0.00", CultureInfo.InvariantCulture)).Append('\t')
					  .Append(t.Stop.ToString("0.00", CultureInfo.InvariantCulture)).Append('\t').Append(t.Name).Append('\t').Append(F(new DateTime(t.ExitTime))).Append('\t')
					  .Append(t.ExitPx.ToString("0.00", CultureInfo.InvariantCulture)).Append('\t').Append(t.Gapped ? "1" : "0").Append('\n');
					NatRow r;
					if (!gnat.TryGetValue(t.Sig, out r)) { if (bad.Count < 5) bad.Add("solo motor " + f[0]); continue; }
					seen.Add(t.Sig);
					if (r.Dir == t.Dir && Eq(r.Entry, t.Entry) && Eq(r.Stop, t.Stop) && r.Name == t.Name && r.ExitTime == t.ExitTime && Eq(r.ExitPx, t.ExitPx)) gEqual++;
					else if (bad.Count < 5) bad.Add("difiere " + f[0] + " nat=" + r.Name + "@" + r.ExitPx.ToString("0.00", CultureInfo.InvariantCulture) + " motor=" + t.Name + "@" + t.ExitPx.ToString("0.00", CultureInfo.InvariantCulture));
				}
				File.WriteAllText(Path.Combine(outDir, "ENGINE_GAP.tsv"), gt.ToString(), enc);
				gapOk = gshaOk && Probes.Length == ExpectedGaps && gEngine == ExpectedGaps && gNat == ExpectedGaps && seen.Count == ExpectedGaps && gEqual == ExpectedGaps && gGapped == ExpectedGaps;
				rep.Append("probes=" + I(Probes.Length) + " motor=" + I(gEngine) + " nativo=" + I(gNat) + " iguales=" + I(gEqual) + " con_salto=" + I(gGapped) + (gapOk ? "  OK" : "  FALLA") + "\n");
				foreach (string s in bad) rep.Append("    " + s + "\n");
			}
			if (!gapOk) allOk = false;

			// ---- R5 / R6
			rep.Append("\n[R5 capa protectora y costos en el ejecutor (sinteticos)]\n");
			bool synOk = SyntheticChecks();
			rep.Append("\n[R6 cuenta continua]\n");
			bool trkOk = TrackerCheck();
			if (!synOk || !trkOk) allOk = false;

			rep.Append("\n[ET1C] Los 22 chequeos de NQC_ET1C_AccountSim NO se ejecutan aqui: correr ET1C sin cambios (runbook). El motor usa esa misma clase.\n");
			bool partial = new DateTime(bars.T[n - 1]) < new DateTime(2022, 12, 29);
			string status = partial ? "PARCIAL" : (allOk ? "PASS" : "FALLA");
			if (partial) rep.Append("\nNOTA: ultimo Time lejos de 2022-12-31 (corrida abortada o datos incompletos); resultado PARCIAL, no es FALLA del motor.\n");
			rep.Append("\nstatus: " + status + "\n");
			string text = rep.ToString();
			File.WriteAllText(Path.Combine(outDir, "REGRESSION_REPORT.txt"), text, enc);
			string hash = NQCE_Const.Sha256Text(text);
			StringBuilder su = new StringBuilder();
			su.Append("NQC ET1 REGRESSION - SUMMARY\n");
			su.Append("script_version : " + ScriptVersion + "\ncore_version   : " + NQCE_Const.CoreVersion + "\ncontract       : " + NQCE_Const.ContractVersion + "\nexecutor_spec  : " + NQCE_Const.SpecVersion + "\n");
			su.Append("instrument     : " + instName + "\n" + tzLine + "\n" + tzRes + "\nbars           : " + I(n) + "\nfirst_bar      : " + F(new DateTime(bars.T[0])) + "\nlast_bar       : " + F(new DateTime(bars.T[n - 1])) + "\n");
			su.Append("indicators_ok  : " + (indOk ? "SI" : "NO") + "\nnews           : " + news.Status + "\n");
			su.Append("n1n9_native    : " + I(natTotal) + "\nn1n9_equal     : " + I(eqTotal) + "\nn1n9_expected  : " + I(ExpectedN1N9) + "\n");
			su.Append("gaps_equal     : " + I(gEqual) + "\ngaps_gapped    : " + I(gGapped) + "\ngaps_expected  : " + I(ExpectedGaps) + "\ngap_native_sha : " + (gsha ?? "") + "\n");
			su.Append("determinism    : " + (detOk ? "SI" : "NO") + "\ntrades_hash_n1n9: " + hash1 + "\n");
			su.Append("synthetic_ok   : " + (synOk ? "SI" : "NO") + " (" + I(synPass) + " ok, " + I(synFail) + " falla)\ntracker_ok     : " + (trkOk ? "SI" : "NO") + "\n");
			su.Append("et1c_22_checks : EXTERNO (correr NQC_ET1C_AccountSim; exigir checks_ok=22)\n");
			su.Append("status         : " + status + "\nreport_sha256  : " + hash + "\n");
			File.WriteAllText(Path.Combine(outDir, "SUMMARY.txt"), su.ToString(), enc);
			Print("NQC_ET1REG_END status=" + status + " n1n9=" + I(eqTotal) + "/" + I(ExpectedN1N9) + " gaps=" + I(gEqual) + "/" + I(ExpectedGaps) + " sha=" + hash.Substring(0, 12) + " dir=" + outDir);
		}
	}
}
