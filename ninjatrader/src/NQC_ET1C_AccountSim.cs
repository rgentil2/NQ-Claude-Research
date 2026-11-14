#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
#endregion

//==============================================================================
// NQC_ET1C_AccountSim  -  ET1 (parte C): simulador de cuenta + capa protectora + autopruebas P2..P6
// Contrato NQC 2.0.0, EXECUTOR_SPEC 2.3.0. Espejo exacto de tools/lab/et1c/account_sim.py.
// Estrategia SIN ordenes y sin datos de mercado: corre las autopruebas al terminar (State.Terminated).
// Strategy Analyzer: cualquier rango corto de NQ M1 (p.ej. 2022-01-03 a 2022-01-07).
// Salida: <Documentos>\NinjaTrader 8\NQC_Results\ET1C_<fecha_hora>\SELFTEST_REPORT.txt, SUMMARY.txt
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public class NQC_AccountProfile
	{
		public string Name; public double Target, Mll, LockLevel, Consistency; public int MinDays;
		public NQC_AccountProfile(string n, double t, double m, double l, double c, int d) { Name = n; Target = t; Mll = m; LockLevel = l; Consistency = c; MinDays = d; }
		public static NQC_AccountProfile RapidIntraday50K() { return new NQC_AccountProfile("RAPID_INTRADAY_50K", 3000, 2000, 100, 0.50, 2); }
		public static NQC_AccountProfile RapidEod50K() { return new NQC_AccountProfile("RAPID_EOD_50K", 3000, 2000, 100, 0.30, 4); }
	}

	public class NQC_SessionRow
	{
		public double Pnl, MinEq; public int N;
		public NQC_SessionRow(double pnl, double minEq, int n) { Pnl = pnl; MinEq = minEq; N = n; }
	}

	public class NQC_AccResult { public string Status, Why; public int Sessions; public double Bal; }

	public static class NQC_AccountSim
	{
		public const double CostRT = 24.68;
		public const double StressMult = 1.5;
		public static double CostStress() { return Math.Round(CostRT * StressMult * 100.0) / 100.0; }

		public static NQC_AccResult RunFrom(List<NQC_SessionRow> rows, int start, NQC_AccountProfile p)
		{
			double bal = 0, hi = 0, level = -p.Mll, best = 0; int days = 0;
			for (int i = start; i < rows.Count; i++)
			{
				NQC_SessionRow r = rows[i];
				if (r.N > 0 && bal + r.MinEq <= level) return Mk("FAIL", "MLL", i - start + 1, bal);
				bal += r.Pnl;
				if (r.N > 0) { days++; if (r.Pnl > best) best = r.Pnl; }
				if (bal <= level && r.N > 0) return Mk("FAIL", "MLL_EOD", i - start + 1, bal);
				if (bal > hi) hi = bal;
				level = Math.Min(hi - p.Mll, p.LockLevel);
				if (bal >= p.Target && days >= p.MinDays && best <= p.Consistency * bal) return Mk("PASS", "", i - start + 1, bal);
			}
			return Mk("CENSORED", "", rows.Count - start, bal);
		}

		private static NQC_AccResult Mk(string s, string w, int n, double b)
		{ NQC_AccResult r = new NQC_AccResult(); r.Status = s; r.Why = w; r.Sessions = n; r.Bal = b; return r; }

		// P_pass sobre arranques rodantes; censurados excluidos. Devuelve NaN si no hay decididos.
		public static double Rolling(List<NQC_SessionRow> rows, NQC_AccountProfile p, out int decided)
		{
			int pass = 0; decided = 0;
			for (int s = 0; s < rows.Count; s++)
			{
				NQC_AccResult r = RunFrom(rows, s, p);
				if (r.Status == "CENSORED") continue;
				decided++; if (r.Status == "PASS") pass++;
			}
			return decided == 0 ? double.NaN : (double)pass / decided;
		}

		// trades: pares {pnlNeto, minimoAbiertoTrasEntrada} de UNA sesion. room = balance - nivelMLL al inicio de la sesion.
		public static void Protect(List<double[]> trades, double room, out double real, out double mn, out int kept)
		{
			const double pause = -500, close = -750, cushion = 400;
			real = 0; mn = 0; kept = 0;
			foreach (double[] t in trades)
			{
				if (real <= pause) break;
				if (room + real <= cushion) break;
				if (real + t[1] <= close) { real = close; if (close < mn) mn = close; kept++; break; }
				if (real + t[1] < mn) mn = real + t[1];
				real += t[0]; if (real < mn) mn = real; kept++;
			}
		}
	}

	public class NQC_ET1C_AccountSim : Strategy
	{
		private const string ScriptVersion = "ET1C-1.0.0";
		private bool done;
		private StringBuilder rep = new StringBuilder();
		private int pass, fail;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "ET1C simulador de cuenta, capa protectora y autopruebas (sin ordenes).";
				Name = "NQC_ET1C_AccountSim";
				Calculate = Calculate.OnBarClose;
				BarsRequiredToTrade = 0;
				IsExitOnSessionCloseStrategy = false;
			}
			else if (State == State.Terminated)
			{
				try { RunAll(); }
				catch (Exception ex) { Print("NQC_ET1C_ERROR " + ex.Message + " | " + ex.StackTrace); }
			}
		}

		protected override void OnBarUpdate() { }

		private static List<NQC_SessionRow> D(params double[] x)
		{
			List<NQC_SessionRow> l = new List<NQC_SessionRow>();
			foreach (double v in x) l.Add(new NQC_SessionRow(v, Math.Min(v, 0), 1));
			return l;
		}

		private void Chk(string name, bool ok, string info)
		{
			if (ok) pass++; else fail++;
			rep.Append((ok ? "OK   " : "FALLA ") + name + "  " + info + "\n");
		}

		private void Acc(string name, List<NQC_SessionRow> rows, NQC_AccountProfile p, string status, string why)
		{
			NQC_AccResult r = NQC_AccountSim.RunFrom(rows, 0, p);
			Chk(name, r.Status == status && (why == null || r.Why == why), r.Status + "/" + r.Why + " esperado " + status + "/" + (why ?? "*"));
		}

		private void Prot(string name, List<double[]> tr, double room, double eReal, double eMn, int eKept)
		{
			double real, mn; int kept;
			NQC_AccountSim.Protect(tr, room, out real, out mn, out kept);
			Chk(name, real == eReal && mn == eMn && kept == eKept, real.ToString(CultureInfo.InvariantCulture) + "," + mn.ToString(CultureInfo.InvariantCulture) + "," + kept.ToString(CultureInfo.InvariantCulture));
		}

		private static List<double[]> TR(params double[] flat)
		{
			List<double[]> l = new List<double[]>();
			for (int i = 0; i + 1 < flat.Length; i += 2) l.Add(new double[] { flat[i], flat[i + 1] });
			return l;
		}

		private void RunAll()
		{
			if (done) return; done = true;
			NQC_AccountProfile P = NQC_AccountProfile.RapidIntraday50K(), E = NQC_AccountProfile.RapidEod50K();
			rep.Append("NQC ET1C - SELFTEST_REPORT\nscript_version : " + ScriptVersion + "\n\n[P3 costos]\n");
			Chk("costo_RT", Math.Abs(NQC_AccountSim.CostRT - 24.68) < 1e-9, NQC_AccountSim.CostRT.ToString(CultureInfo.InvariantCulture));
			Chk("costo_estres_1.5x", Math.Abs(NQC_AccountSim.CostStress() - 37.02) < 1e-9, NQC_AccountSim.CostStress().ToString(CultureInfo.InvariantCulture));

			rep.Append("\n[P5 trayectorias sinteticas de resultado conocido]\n");
			Acc("pass_simple", D(800, 800, 800, 800), P, "PASS", "");
			Acc("consistency_wait", D(2000, 500, 500, 500, 500, 500, 500), P, "PASS", "");
			Acc("consistency_censored", D(2500, 300, 300), P, "CENSORED", null);
			Acc("min_days_block", D(3000, 10), P, "CENSORED", null);
			List<NQC_SessionRow> a = new List<NQC_SessionRow>(); a.Add(new NQC_SessionRow(100, -2000, 1));
			Acc("intraday_mll_exact", a, P, "FAIL", "MLL");
			a = new List<NQC_SessionRow>(); a.Add(new NQC_SessionRow(500, -2001, 1));
			Acc("intraday_mll_over", a, P, "FAIL", "MLL");
			a = new List<NQC_SessionRow>(); a.Add(new NQC_SessionRow(100, -1999, 1));
			Acc("above_level", a, P, "CENSORED", null);
			a = new List<NQC_SessionRow>(); a.Add(new NQC_SessionRow(1500, 0, 1)); a.Add(new NQC_SessionRow(-600, -600, 1));
			Acc("trailing_raise_survive", a, P, "CENSORED", null);
			a = new List<NQC_SessionRow>(); a.Add(new NQC_SessionRow(1500, 0, 1)); a.Add(new NQC_SessionRow(-2000, -2000, 1));
			Acc("trailing_raise_fail", a, P, "FAIL", "MLL");
			a = new List<NQC_SessionRow>(); a.Add(new NQC_SessionRow(1200, 0, 1)); a.Add(new NQC_SessionRow(1300, 0, 1)); a.Add(new NQC_SessionRow(-2300, -2300, 1));
			Acc("lock_cap_survive", a, P, "CENSORED", null);
			a = new List<NQC_SessionRow>(); a.Add(new NQC_SessionRow(1200, 0, 1)); a.Add(new NQC_SessionRow(1300, 0, 1)); a.Add(new NQC_SessionRow(-2450, -2450, 1));
			Acc("lock_cap_fail", a, P, "FAIL", "MLL");
			a = new List<NQC_SessionRow>(); a.Add(new NQC_SessionRow(0, 0, 0)); a.Add(new NQC_SessionRow(3000, 0, 1));
			Acc("no_trade_day_not_counted", a, P, "CENSORED", null);
			Acc("eod_profile_min4", D(1500, 1500), E, "CENSORED", null);
			Acc("eod_profile_pass", D(900, 900, 900, 900), E, "PASS", "");

			rep.Append("\n[P4 capa protectora]\n");
			Prot("prot_pause", TR(-500, -500, 300, 0), 2000, -500, -500, 1);
			Prot("prot_close", TR(-100, -800), 2000, -750, -750, 1);
			Prot("prot_none", TR(200, -50, 100, -20), 2000, 300, -50, 2);
			Prot("guard_start", TR(100, 0), 300, 0, 0, 0);
			Prot("guard_mid", TR(-450, -450, 300, 0), 800, -450, -450, 1);

			rep.Append("\n[P6 determinismo]\n");
			List<NQC_SessionRow> rows = new List<NQC_SessionRow>();
			Random rnd = new Random(7);
			for (int i = 0; i < 400; i++)
			{
				double g = 0; for (int k = 0; k < 12; k++) g += rnd.NextDouble(); g -= 6.0;      // ~N(0,1)
				double g2 = 0; for (int k = 0; k < 12; k++) g2 += rnd.NextDouble(); g2 -= 6.0;
				rows.Add(new NQC_SessionRow(30 + 300 * g, -Math.Abs(150 + 100 * g2), 1));
			}
			int d1, d2; double p1 = NQC_AccountSim.Rolling(rows, P, out d1), p2 = NQC_AccountSim.Rolling(rows, P, out d2);
			Chk("determinismo", p1 == p2 && d1 == d2 && d1 > 0, "P_pass=" + p1.ToString("0.0000", CultureInfo.InvariantCulture) + " n=" + d1.ToString(CultureInfo.InvariantCulture));

			string status = fail == 0 ? "PASS" : "FALLA";
			rep.Append("\nchecks_ok=" + pass.ToString(CultureInfo.InvariantCulture) + " checks_falla=" + fail.ToString(CultureInfo.InvariantCulture) + "\nstatus: " + status + "\n");
			string dir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results", "ET1C_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
			Directory.CreateDirectory(dir);
			UTF8Encoding enc = new UTF8Encoding(false);
			string text = rep.ToString();
			File.WriteAllText(Path.Combine(dir, "SELFTEST_REPORT.txt"), text, enc);
			string hash;
			using (SHA256 sha = SHA256.Create())
			{
				StringBuilder h = new StringBuilder();
				foreach (byte b in sha.ComputeHash(Encoding.UTF8.GetBytes(text))) h.Append(b.ToString("x2"));
				hash = h.ToString();
			}
			File.WriteAllText(Path.Combine(dir, "SUMMARY.txt"), "NQC ET1C - SUMMARY\nscript_version : " + ScriptVersion + "\nstatus : " + status + "\nreport_sha256 : " + hash + "\n", enc);
			Print("NQC_ET1C_END status=" + status + " ok=" + pass.ToString(CultureInfo.InvariantCulture) + " falla=" + fail.ToString(CultureInfo.InvariantCulture) + " sha=" + hash.Substring(0, 12) + " dir=" + dir);
		}
	}
}
