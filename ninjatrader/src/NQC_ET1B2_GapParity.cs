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
// NQC_ET1B2_GapParity  -  ET1 (parte B2, cierre C1 del acta): paridad del motor en saltos
// Contrato NQC 2.0.0, EXECUTOR_SPEC 2.3.0.
//
// Estrategia SIN ordenes. Carga las barras M1 del IS, calcula ATR(14) por su cuenta con el
// mismo codigo del motor de ET1B y simula las 46 operaciones de la sonda de saltos de ET1A
// (misma lista de senales, direccion y k). Compara operacion por operacion con el libro
// nativo de NT8 (ET1A_GAP_*\TRADES_NATIVE.tsv). Meta: 46 de 46 iguales y 46 de 46 con salto.
//
// Strategy Analyzer: NQ M1 Last, Merge back adjusted, 2020-01-01 a 2022-12-31
// (el MISMO rango que ET1B, para que el ATR de Wilder arranque igual).
// Sin calendario de noticias ni dias excluidos: son casos puntuales, igual que la sonda.
// Sus resultados economicos NO se miran.
// Salida: <Documentos>\NinjaTrader 8\NQC_Results\ET1B2_<fecha_hora>\
//   PARITY_REPORT.txt, ENGINE_GAP.tsv, SUMMARY.txt
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	internal static class Et1b2Core
	{
		public class GapTrade
		{
			public bool Ok;
			public int Dir;
			public long Sig, ExitTime;
			public double Entry, Stop, ExitPx;
			public string Name;
			public bool Gapped;
		}

		public static double Rt(double x) { return Math.Round(x * 4.0) / 4.0; }

		// Mismo ATR (Wilder, arranque con media creciente) que NQC_ET1B_EngineParity.
		public static double[] AtrOwn(double[] h, double[] l, double[] c, int p)
		{
			int n = h.Length; double[] r = new double[n];
			for (int i = 0; i < n; i++)
			{
				if (i == 0) { r[0] = h[0] - l[0]; continue; }
				double tr = Math.Max(Math.Abs(l[i] - c[i - 1]), Math.Max(h[i] - l[i], Math.Abs(h[i] - c[i - 1])));
				int m = Math.Min(i + 1, p);
				r[i] = ((m - 1) * r[i - 1] + tr) / m;
			}
			return r;
		}

		public static int IndexOf(long[] T, long t)
		{
			int lo = 0, hi = T.Length - 1;
			while (lo <= hi)
			{
				int mid = (lo + hi) / 2;
				if (T[mid] == t) return mid;
				if (T[mid] < t) lo = mid + 1; else hi = mid - 1;
			}
			return -1;
		}

		// Reglas de EXECUTOR_SPEC 2.3.0 para una entrada a mercado al cierre de la barra de senal:
		//  - entrada = apertura de la barra siguiente; stop = entrada -/+ k x ATR(senal), al tick;
		//  - el stop se activa al tocar el precio y esta activo desde la barra de entrada;
		//  - si la barra abre mas alla del stop, la salida es a la apertura; si solo lo toca, al precio del stop;
		//  - la salida de las 11:30 se envia al cierre de esa barra y se ejecuta en la apertura de la siguiente.
		public static GapTrade Run(long[] T, double[] O, double[] H, double[] L, double[] A14, long sig, int dir, double k)
		{
			GapTrade t = new GapTrade();
			t.Sig = sig; t.Dir = dir; t.Ok = false;
			int i = IndexOf(T, sig);
			if (i < 0 || i + 1 >= T.Length) return t;
			int e = i + 1;
			double entry = O[e];
			double atr = A14[i];
			double stop = Rt(dir == 1 ? entry - k * atr : entry + k * atr);
			t.Entry = entry; t.Stop = stop;
			bool pend = false;
			for (int j = e; j < T.Length; j++)
			{
				long ct = T[j];
				double o = O[j], h = H[j], l = L[j];
				if (pend)
				{
					t.Name = "TIME"; t.ExitPx = o; t.ExitTime = ct; t.Gapped = false; t.Ok = true;
					return t;
				}
				bool sl = dir == 1 ? (l <= stop) : (h >= stop);
				if (sl)
				{
					bool gapped = dir == 1 ? (o < stop) : (o > stop);
					t.Name = "SL"; t.ExitPx = gapped ? o : stop; t.ExitTime = ct; t.Gapped = gapped; t.Ok = true;
					return t;
				}
				DateTime d = new DateTime(ct);
				if (d.Hour * 60 + d.Minute == 690) pend = true;
			}
			return t;
		}
	}

	public class NQC_ET1B2_GapParity : Strategy
	{
		private const string ScriptVersion = "ET1B2-1.0.0";
		private const string ContractVersion = "NQC 2.0.0";
		private const string ExecutorSpec = "2.3.0";
		private const string NativeSha256 = "e2aab0565b45ea228ecd38fedcf5ea38031d647a7291f9a47d299182f41475ca";
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

		private ATR nA14;
		private List<long> tT = new List<long>();
		private List<double> tO = new List<double>(), tH = new List<double>(), tL = new List<double>(), tC = new List<double>();
		private List<double> v14 = new List<double>();
		private bool done;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "ET1B2 paridad del motor en saltos sobre el stop (sin ordenes).";
				Name = "NQC_ET1B2_GapParity";
				Calculate = Calculate.OnBarClose;
				BarsRequiredToTrade = 0;
				IsExitOnSessionCloseStrategy = false;
				TraceOrders = false;
			}
			else if (State == State.DataLoaded)
			{
				nA14 = ATR(14);
			}
			else if (State == State.Terminated)
			{
				try { RunAll(); }
				catch (Exception ex)
				{
					Print("NQC_ET1B2_ERROR " + ex.Message + " | " + ex.StackTrace);
					try
					{
						string d = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results");
						Directory.CreateDirectory(d);
						File.WriteAllText(Path.Combine(d, "ET1B2_ERROR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt"), ex.ToString());
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
			tO.Add(Open[0]); tH.Add(High[0]); tL.Add(Low[0]); tC.Add(Close[0]);
			v14.Add(nA14[0]);
		}

		private class NatRow { public int Dir; public double Entry, Stop, ExitPx; public string Name; public long ExitTime; }

		private static string Hex(byte[] b)
		{
			StringBuilder h = new StringBuilder();
			foreach (byte x in b) h.Append(x.ToString("x2"));
			return h.ToString();
		}

		// Busca el libro nativo de la sonda; prefiere la carpeta cuyo SHA256 coincide con el registrado.
		private Dictionary<long, NatRow> LoadNative(out string folder, out string actualSha, out bool shaOk)
		{
			folder = null; actualSha = null; shaOk = false;
			string root = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results");
			if (!Directory.Exists(root)) return null;
			string[] dirs = Directory.GetDirectories(root, "ET1A_GAP_*");
			Array.Sort(dirs); Array.Reverse(dirs);
			string pick = null, pickSha = null;
			foreach (string d in dirs)
			{
				string f = Path.Combine(d, "TRADES_NATIVE.tsv");
				if (!File.Exists(f)) continue;
				string sha;
				using (SHA256 s = SHA256.Create()) sha = Hex(s.ComputeHash(File.ReadAllBytes(f)));
				if (pick == null) { pick = d; pickSha = sha; }
				if (sha == NativeSha256) { pick = d; pickSha = sha; break; }
			}
			if (pick == null) return null;
			folder = pick; actualSha = pickSha; shaOk = pickSha == NativeSha256;
			Dictionary<long, NatRow> m = new Dictionary<long, NatRow>();
			string[] lines = File.ReadAllText(Path.Combine(pick, "TRADES_NATIVE.tsv")).Split('\n');
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

		private static bool Eq(double a, double b) { return Math.Abs(a - b) < 1e-9; }
		private static string F(DateTime d) { return d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); }
		private static string I(long x) { return x.ToString(CultureInfo.InvariantCulture); }

		private void RunAll()
		{
			if (done || tT.Count == 0) return;
			done = true;
			int n = tT.Count;
			long[] T = tT.ToArray();
			double[] O = tO.ToArray(), H = tH.ToArray(), L = tL.ToArray(), C = tC.ToArray();
			double[] A14 = Et1b2Core.AtrOwn(H, L, C, 14);

			// ATR propio frente a NT8, todas las barras
			double mx = 0; long big = 0;
			for (int i = 0; i < n; i++)
			{
				double d = Math.Abs(A14[i] - v14[i]);
				if (d > mx) mx = d;
				if (d > 1e-7) big++;
			}
			bool atrOk = big == 0;

			StringBuilder rep = new StringBuilder();
			rep.Append("NQC ET1B2 - GAP_PARITY_REPORT\n");
			rep.Append("script_version : " + ScriptVersion + "\n");
			rep.Append("contract       : " + ContractVersion + "\n");
			rep.Append("executor_spec  : " + ExecutorSpec + "\n");
			rep.Append("instrument     : " + Instrument.FullName + "\n");
			rep.Append("bars           : " + I(n) + "\n");
			rep.Append("first_bar      : " + F(new DateTime(T[0])) + "\n");
			rep.Append("last_bar       : " + F(new DateTime(T[n - 1])) + "\n\n");
			rep.Append("[ATR14 propio frente a NT8, todas las barras]\n");
			rep.Append("ATR14 max|dif| " + mx.ToString("0.000000000", CultureInfo.InvariantCulture) + "  barras>1e-7: " + I(big) + "\n\n");

			string folder, nativeSha; bool shaOk;
			Dictionary<long, NatRow> nat = LoadNative(out folder, out nativeSha, out shaOk);
			if (nat == null)
			{
				rep.Append("[Libro nativo] FALTA ET1A_GAP_*\\TRADES_NATIVE.tsv en NQC_Results\n");
				Finish(rep, false, null, n, T, 0, 0, 0, 0, mx, "", false);
				return;
			}
			rep.Append("[Libro nativo]\nfolder  : " + Path.GetFileName(folder) + "\nsha256  : " + nativeSha + "\nsha_ok  : " + (shaOk ? "SI (coincide con el registrado)" : "NO (esperado " + NativeSha256 + ")") + "\n\n");

			string outDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results",
				"ET1B2_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
			Directory.CreateDirectory(outDir);

			StringBuilder tsv = new StringBuilder();
			tsv.Append("ref\tdir\tsignalTime\tentryPrice\tstopPrice\texitName\texitExecTime\texitPrice\tgapped\n");
			int engineOk = 0, both = 0, eqDir = 0, eqEntry = 0, eqStop = 0, eqName = 0, eqTime = 0, eqPx = 0, eqAll = 0, onlyEng = 0, gapped = 0, gl = 0, gs = 0;
			double gmin = double.MaxValue, gmax = 0;
			List<string> bad = new List<string>();
			HashSet<long> seen = new HashSet<long>();
			foreach (string p in Probes)
			{
				string[] f = p.Split(',');
				long sig = DateTime.ParseExact(f[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture).Ticks;
				int dir = int.Parse(f[1], CultureInfo.InvariantCulture);
				double k = double.Parse(f[2], CultureInfo.InvariantCulture);
				Et1b2Core.GapTrade t = Et1b2Core.Run(T, O, H, L, A14, sig, dir, k);
				if (!t.Ok) { if (bad.Count < 5) bad.Add("motor sin operacion " + f[0]); continue; }
				engineOk++;
				if (t.Gapped)
				{
					gapped++; if (dir == 1) gl++; else gs++;
					double g = Math.Abs(t.ExitPx - t.Stop);
					if (g < gmin) gmin = g; if (g > gmax) gmax = g;
				}
				tsv.Append("G\t").Append(I(t.Dir)).Append('\t').Append(F(new DateTime(t.Sig))).Append('\t')
				   .Append(t.Entry.ToString("0.00", CultureInfo.InvariantCulture)).Append('\t').Append(t.Stop.ToString("0.00", CultureInfo.InvariantCulture)).Append('\t')
				   .Append(t.Name).Append('\t').Append(F(new DateTime(t.ExitTime))).Append('\t')
				   .Append(t.ExitPx.ToString("0.00", CultureInfo.InvariantCulture)).Append('\t').Append(t.Gapped ? "1" : "0").Append('\n');
				NatRow r;
				if (!nat.TryGetValue(t.Sig, out r)) { onlyEng++; if (bad.Count < 5) bad.Add("solo motor " + f[0]); continue; }
				seen.Add(t.Sig); both++;
				bool a = r.Dir == t.Dir, b = Eq(r.Entry, t.Entry), c2 = Eq(r.Stop, t.Stop), d2 = r.Name == t.Name, e2 = r.ExitTime == t.ExitTime, f2 = Eq(r.ExitPx, t.ExitPx);
				if (a) eqDir++; if (b) eqEntry++; if (c2) eqStop++; if (d2) eqName++; if (e2) eqTime++; if (f2) eqPx++;
				if (a && b && c2 && d2 && e2 && f2) eqAll++;
				else if (bad.Count < 5) bad.Add("difiere " + f[0] + " nat=" + r.Name + "@" + r.ExitPx.ToString("0.00", CultureInfo.InvariantCulture) + " motor=" + t.Name + "@" + t.ExitPx.ToString("0.00", CultureInfo.InvariantCulture));
			}
			UTF8Encoding enc = new UTF8Encoding(false);
			File.WriteAllText(Path.Combine(outDir, "ENGINE_GAP.tsv"), tsv.ToString(), enc);

			int onlyNat = nat.Count - seen.Count;
			bool ok = atrOk && shaOk && Probes.Length == 46 && engineOk == 46 && nat.Count == 46 && both == 46
				&& onlyEng == 0 && onlyNat == 0 && eqAll == 46 && gapped == 46;
			rep.Append("[Paridad en saltos: motor frente a NT8 nativo]\n");
			rep.Append("probes=" + I(Probes.Length) + " motor=" + I(engineOk) + " nativo=" + I(nat.Count) + " iguales=" + I(eqAll)
				+ " soloNat=" + I(onlyNat) + " soloMotor=" + I(onlyEng)
				+ " dir=" + I(eqDir) + " ent=" + I(eqEntry) + " stop=" + I(eqStop) + " nom=" + I(eqName) + " hora=" + I(eqTime) + " px=" + I(eqPx) + "\n");
			foreach (string s in bad) rep.Append("    " + s + "\n");
			rep.Append("\n[Cobertura del caso salto]\n");
			rep.Append("gapped_motor=" + I(gapped) + " de 46 (largos " + I(gl) + ", cortos " + I(gs) + ")");
			if (gapped > 0) rep.Append("  salto_vs_stop_pts min=" + gmin.ToString("0.00", CultureInfo.InvariantCulture) + " max=" + gmax.ToString("0.00", CultureInfo.InvariantCulture));
			rep.Append("\n");
			Finish(rep, ok, outDir, n, T, engineOk, nat.Count, eqAll, gapped, mx, nativeSha, shaOk);
		}

		private void Finish(StringBuilder rep, bool ok, string outDir, int n, long[] T, int engineN, int natN, int equal, int gapped, double atrMax, string nativeSha, bool shaOk)
		{
			string status = ok ? "PASS" : "FALLA";
			rep.Append("\nstatus: " + status + "\n");
			string dir = outDir;
			if (dir == null || !Directory.Exists(dir))
			{
				dir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results", "ET1B2_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
				Directory.CreateDirectory(dir);
			}
			UTF8Encoding enc = new UTF8Encoding(false);
			string text = rep.ToString();
			File.WriteAllText(Path.Combine(dir, "PARITY_REPORT.txt"), text, enc);
			string hash;
			using (SHA256 sha = SHA256.Create()) hash = Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
			StringBuilder su = new StringBuilder();
			su.Append("NQC ET1B2 - SUMMARY\n");
			su.Append("script_version : " + ScriptVersion + "\n");
			su.Append("contract       : " + ContractVersion + "\n");
			su.Append("executor_spec  : " + ExecutorSpec + "\n");
			su.Append("instrument     : " + Instrument.FullName + "\n");
			su.Append("bars           : " + I(n) + "\n");
			if (n > 0)
			{
				su.Append("first_bar      : " + F(new DateTime(T[0])) + "\n");
				su.Append("last_bar       : " + F(new DateTime(T[n - 1])) + "\n");
			}
			su.Append("probes         : " + I(Probes.Length) + "\n");
			su.Append("engine_trades  : " + I(engineN) + "\n");
			su.Append("native_trades  : " + I(natN) + "\n");
			su.Append("equal          : " + I(equal) + "\n");
			su.Append("gapped_engine  : " + I(gapped) + "\n");
			su.Append("atr14_maxdiff  : " + atrMax.ToString("0.000000000", CultureInfo.InvariantCulture) + "\n");
			su.Append("native_sha256  : " + nativeSha + "\n");
			su.Append("native_sha_ok  : " + (shaOk ? "SI" : "NO") + "\n");
			su.Append("status         : " + status + "\n");
			su.Append("report_sha256  : " + hash + "\n");
			File.WriteAllText(Path.Combine(dir, "SUMMARY.txt"), su.ToString(), enc);
			Print("NQC_ET1B2_END status=" + status + " equal=" + I(equal) + "/46 gapped=" + I(gapped) + "/46 sha=" + hash.Substring(0, 12) + " dir=" + dir);
		}
	}
}
