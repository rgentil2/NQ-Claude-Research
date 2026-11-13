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
// NQC_ET1B_EngineParity  -  ET1 (parte B1): motor propio + comparador de paridad
// Contrato NQC 2.0.0, EXECUTOR_SPEC 2.3.0.
//
// Estrategia SIN ordenes. Carga las barras M1 del IS en memoria, calcula ATR y EMA por
// su cuenta, simula las referencias N1..N9 con las reglas de EXECUTOR_SPEC y las compara
// con los libros nativos de ET1A (ET1A_N<id>_*\TRADES_NATIVE.tsv). Meta: 100% de paridad.
//
// Strategy Analyzer: NQ M1 Last, Merge back adjusted, 2020-01-01 a 2022-12-31.
// Las barras posteriores a 2022-12-31 se ignoran. El calendario de noticias debe estar
// en NQC_Data o NQC Data. Salida: <Documentos>\NinjaTrader 8\NQC_Results\ET1B_<fecha_hora>\
//   PARITY_REPORT.txt, ENGINE_N<id>.tsv, SUMMARY.txt
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public class NQC_ET1B_EngineParity : Strategy
	{
		private const string ScriptVersion = "ET1B-1.0.0";
		private const string NewsSha256 = "93d837ac323c6f144786c507606b0b77f25c2fa33fba6d21bb941d2d4d9c377d";
		private const string NewsFileName = "usd_high_medium_2020_2026.csv";
		private static readonly string[] ExcludedList = new string[] {
			"2020-01-01","2020-03-09","2020-03-12","2020-03-16","2020-06-03","2020-10-23","2021-04-02","2021-08-24",
			"2022-08-03","2022-08-30","2022-12-26","2023-01-02","2023-04-07","2023-12-25","2024-01-01","2024-07-19",
			"2024-07-25","2024-08-05","2024-12-25","2025-01-01","2025-01-09","2025-06-18","2025-12-25","2026-01-01","2026-04-03"
		};
		private const long Min5 = 5L * 60L * 10000000L;      // 5 minutos en ticks

		// ---- barras y valores de NT8
		private ATR a5, a14, a20, a50;
		private EMA nE5, nE13;
		private List<long> tT = new List<long>();
		private List<double> tO = new List<double>(), tH = new List<double>(), tL = new List<double>(), tC = new List<double>();
		private List<double> v5 = new List<double>(), v14 = new List<double>(), v20 = new List<double>(), v50 = new List<double>();
		private List<double> vE5 = new List<double>(), vE13 = new List<double>();
		private bool done;

		// ---- motor
		private double[] O, H, L, C, A5, A14, A20, A50, E5, E13;
		private long[] T;
		private long[] ev;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "ET1B motor propio y paridad con libros nativos (sin ordenes).";
				Name = "NQC_ET1B_EngineParity";
				Calculate = Calculate.OnBarClose;
				BarsRequiredToTrade = 0;
				IsExitOnSessionCloseStrategy = false;
				TraceOrders = false;
			}
			else if (State == State.DataLoaded)
			{
				a5 = ATR(5); a14 = ATR(14); a20 = ATR(20); a50 = ATR(50);
				nE5 = EMA(5); nE13 = EMA(13);
			}
			else if (State == State.Terminated)
			{
				try { RunAll(); }
				catch (Exception ex)
				{
					Print("NQC_ET1B_ERROR " + ex.Message + " | " + ex.StackTrace);
					try
					{
						string d = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results");
						Directory.CreateDirectory(d);
						File.WriteAllText(Path.Combine(d, "ET1B_ERROR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt"), ex.ToString());
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
			v5.Add(a5[0]); v14.Add(a14[0]); v20.Add(a20[0]); v50.Add(a50[0]);
			vE5.Add(nE5[0]); vE13.Add(nE13[0]);
		}

		// ------------------------------------------------------------------ indicadores propios
		private static double[] AtrOwn(double[] h, double[] l, double[] c, int p)
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

		private static double[] EmaOwn(double[] c, int p)
		{
			int n = c.Length; double[] r = new double[n];
			double k1 = 2.0 / (1.0 + p), k2 = 1.0 - k1;
			for (int i = 0; i < n; i++) r[i] = (i == 0) ? c[0] : c[i] * k1 + k2 * r[i - 1];
			return r;
		}

		private static string MaxDiff(double[] own, List<double> nt, out long bigCount)
		{
			double mx = 0; bigCount = 0;
			for (int i = 0; i < own.Length; i++)
			{
				double d = Math.Abs(own[i] - nt[i]);
				if (d > mx) mx = d;
				if (d > 1e-7) bigCount++;
			}
			return mx.ToString("0.000000000", CultureInfo.InvariantCulture);
		}

		// ------------------------------------------------------------------ noticias
		private string LoadNews()
		{
			string root = NinjaTrader.Core.Globals.UserDataDir, path = null;
			foreach (string f in new string[] { "NQC_Data", "NQC Data" })
			{
				string p = Path.Combine(Path.Combine(root, f), NewsFileName);
				if (File.Exists(p)) { path = p; break; }
			}
			if (path == null) return "FALTA " + NewsFileName;
			byte[] bytes = File.ReadAllBytes(path);
			using (SHA256 sha = SHA256.Create())
			{
				StringBuilder sb = new StringBuilder();
				foreach (byte b in sha.ComputeHash(bytes)) sb.Append(b.ToString("x2"));
				if (sb.ToString() != NewsSha256) return "SHA256 distinto " + sb.ToString();
			}
			List<long> list = new List<long>();
			string[] lines = Encoding.UTF8.GetString(bytes).Split('\n');
			for (int i = 1; i < lines.Length; i++)
			{
				string ln = lines[i].TrimEnd('\r');
				if (ln.Length == 0) continue;
				string[] f = ln.Split(new char[] { ',' }, 5);
				if (f.Length < 4) continue;
				DateTime t;
				if (DateTime.TryParseExact(f[2] + " " + f[3], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) list.Add(t.Ticks);
			}
			list.Sort();
			ev = list.ToArray();
			return "OK " + ev.Length.ToString(CultureInfo.InvariantCulture) + " eventos";
		}

		// c en [t-5, t+5) de algun evento: primer t > c-5min y comprobar t <= c+5min
		private bool InBlk(long c)
		{
			long target = c - Min5;
			int lo = 0, hi = ev.Length;
			while (lo < hi) { int mid = (lo + hi) / 2; if (ev[mid] > target) hi = mid; else lo = mid + 1; }
			return lo < ev.Length && ev[lo] <= c + Min5;
		}

		// ------------------------------------------------------------------ referencias
		private class Cfg { public int Id; public int AtrIdx; public double Ksl; public double Ktp; public int MaxD; public int TimeX; }
		private class Tr
		{
			public int Dir; public long Sig; public double Entry, Stop, Tp, ExitPx; public string Name; public long ExitTime;
		}

		private Cfg MakeCfg(int id)
		{
			Cfg c = new Cfg(); c.Id = id; c.MaxD = 1; c.TimeX = -1; c.Ktp = -1;
			switch (id)
			{
				case 1: c.AtrIdx = 14; c.Ksl = 1.5; c.Ktp = 3.0; break;
				case 2: c.AtrIdx = 20; c.Ksl = 2.0; c.Ktp = 4.0; break;
				case 3: c.AtrIdx = 14; c.Ksl = 2.0; c.MaxD = 99; break;
				case 4: c.AtrIdx = 14; c.Ksl = 2.0; break;
				case 5: c.AtrIdx = 5; c.Ksl = 0.3; c.Ktp = 0.3; break;
				case 6: c.AtrIdx = 14; c.Ksl = 3.0; break;
				case 7: c.AtrIdx = 50; c.Ksl = 6.0; c.TimeX = 630; break;
				case 8: c.AtrIdx = 14; c.Ksl = 1.5; c.Ktp = 3.0; c.MaxD = 2; break;
				case 9: c.AtrIdx = 14; c.Ksl = 1.5; c.Ktp = 0.5; break;
			}
			return c;
		}

		private double[] AtrArr(int idx)
		{
			if (idx == 5) return A5;
			if (idx == 14) return A14;
			if (idx == 20) return A20;
			return A50;
		}

		private static double Rt(double x) { return Math.Round(x * 4.0) / 4.0; }

		// devuelve la direccion (+1/-1/0) y, para N8, ksl/ktp propios (NaN si no aplica)
		private int Signal(int id, int stamp, int i, double rh, out double ksl, out double ktp)
		{
			ksl = double.NaN; ktp = double.NaN;
			switch (id)
			{
				case 1: case 5: case 9: return stamp == 575 ? 1 : 0;
				case 2: return stamp == 580 ? -1 : 0;
				case 3: return (E5[i] > E13[i] && E5[i - 1] <= E13[i - 1]) ? 1 : 0;
				case 4:
					if (stamp <= 585 || double.IsNaN(rh)) return 0;
					return C[i] > rh ? 1 : 0;
				case 6: return stamp == 689 ? 1 : 0;
				case 7: return stamp == 580 ? 1 : 0;
				case 8:
					if (stamp == 575) { ksl = 1.5; ktp = 3.0; return 1; }
					if (stamp == 578) { ksl = 2.5; ktp = 1.0; return -1; }
					return 0;
			}
			return 0;
		}

		private List<Tr> Simulate(Cfg cfg, SortedDictionary<int, List<int>> days, HashSet<int> excl)
		{
			List<Tr> outL = new List<Tr>();
			double[] atr = AtrArr(cfg.AtrIdx);
			foreach (KeyValuePair<int, List<int>> kv in days)
			{
				if (excl.Contains(kv.Key)) continue;
				List<int> idx = kv.Value;
				bool hasPos = false; int pdir = 0; double pentry = 0, pstop = 0, ptp = double.NaN; long psig = 0;
				int pend = 0;   // 0 nada, 1 entrada, 2 salida
				int pDir = 0; double pKsl = 0, pKtp = -1, pAtr = 0; long pSig = 0; string pWhy = null;
				int tradesToday = 0; double rh = double.NaN;
				for (int j = 0; j < idx.Count; j++)
				{
					int i = idx[j];
					long ct = T[i];
					DateTime cdt = new DateTime(ct);
					int stamp = cdt.Hour * 60 + cdt.Minute;
					double o = O[i], h = H[i], l = L[i];

					// 1) ejecuciones dentro de la barra
					if (pend == 1)
					{
						pend = 0;
						pdir = pDir; pentry = o; psig = pSig; hasPos = true;
						pstop = pDir == 1 ? Rt(o - pKsl * pAtr) : Rt(o + pKsl * pAtr);
						ptp = double.NaN;
						if (pKtp > 0) ptp = pDir == 1 ? Rt(o + pKtp * pAtr) : Rt(o - pKtp * pAtr);
					}
					else if (pend == 2 && hasPos)
					{
						Tr t = new Tr(); t.Dir = pdir; t.Sig = psig; t.Entry = pentry; t.Stop = pstop; t.Tp = ptp; t.Name = pWhy; t.ExitTime = ct; t.ExitPx = o;
						outL.Add(t); hasPos = false; pend = 0;
					}
					if (hasPos)
					{
						bool slHit = pdir == 1 ? (l <= pstop) : (h >= pstop);
						bool tpHit = false;
						if (!double.IsNaN(ptp)) tpHit = pdir == 1 ? (h > ptp) : (l < ptp);
						if (slHit || tpHit)
						{
							string first;
							if (slHit && tpHit)
							{
								bool hiFirst = Math.Abs(h - o) <= Math.Abs(l - o);
								if (pdir == 1) first = hiFirst ? "TP" : "SL"; else first = hiFirst ? "SL" : "TP";
							}
							else first = slHit ? "SL" : "TP";
							double px;
							if (first == "SL")
							{
								bool gapped = pdir == 1 ? (o < pstop) : (o > pstop);
								px = gapped ? o : pstop;
							}
							else px = ptp;
							Tr t = new Tr(); t.Dir = pdir; t.Sig = psig; t.Entry = pentry; t.Stop = pstop; t.Tp = ptp; t.Name = first; t.ExitTime = ct; t.ExitPx = px;
							outL.Add(t); hasPos = false;
						}
					}

					// 2) logica al cierre de la barra
					if (stamp >= 571 && stamp <= 585) rh = double.IsNaN(rh) ? h : Math.Max(rh, h);
					bool inb = InBlk(ct);
					if (hasPos)
					{
						if (pend == 0)
						{
							string why = null;
							if (stamp == 690) why = "TIME";
							else if (cfg.TimeX > 0 && stamp == cfg.TimeX) why = "TIME";
							else if (inb) why = "NEWS";
							if (why != null) { pend = 2; pWhy = why; }
						}
						continue;
					}
					if (pend != 0 || stamp < 571 || stamp > 689 || inb || tradesToday >= cfg.MaxD) continue;
					double sk, st;
					int s = Signal(cfg.Id, stamp, i, rh, out sk, out st);
					if (s == 0) continue;
					tradesToday++;
					pend = 1; pDir = s; pSig = ct; pAtr = atr[i];
					pKsl = double.IsNaN(sk) ? cfg.Ksl : sk;
					pKtp = double.IsNaN(sk) ? cfg.Ktp : st;
				}
			}
			return outL;
		}

		// ------------------------------------------------------------------ comparacion con el libro nativo
		private class NatRow { public int Dir; public double Entry, Stop, ExitPx; public string Name; public long ExitTime; }

		private Dictionary<long, NatRow> LoadNative(int id, out string folder)
		{
			folder = null;
			string root = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results");
			if (!Directory.Exists(root)) return null;
			string[] dirs = Directory.GetDirectories(root, "ET1A_N" + id.ToString(CultureInfo.InvariantCulture) + "_*");
			Array.Sort(dirs); Array.Reverse(dirs);
			foreach (string d in dirs)
			{
				string f = Path.Combine(d, "TRADES_NATIVE.tsv");
				if (!File.Exists(f)) continue;
				Dictionary<long, NatRow> m = new Dictionary<long, NatRow>();
				string[] lines = File.ReadAllText(f).Split('\n');
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
				folder = d;
				return m;
			}
			return null;
		}

		private static bool Eq(double a, double b) { return Math.Abs(a - b) < 1e-9; }

		private void RunAll()
		{
			if (done || tT.Count == 0) return;
			done = true;
			int n = tT.Count;
			T = tT.ToArray(); O = tO.ToArray(); H = tH.ToArray(); L = tL.ToArray(); C = tC.ToArray();
			A5 = AtrOwn(H, L, C, 5); A14 = AtrOwn(H, L, C, 14); A20 = AtrOwn(H, L, C, 20); A50 = AtrOwn(H, L, C, 50);
			E5 = EmaOwn(C, 5); E13 = EmaOwn(C, 13);

			StringBuilder rep = new StringBuilder();
			rep.Append("NQC ET1B - PARITY_REPORT\n");
			rep.Append("script_version : " + ScriptVersion + "\n");
			rep.Append("instrument     : " + Instrument.FullName + "\n");
			rep.Append("bars           : " + n.ToString(CultureInfo.InvariantCulture) + "\n\n[Indicadores: motor propio frente a NT8, todas las barras]\n");
			long b5, b14, b20, b50, be5, be13;
			string m5 = MaxDiff(A5, v5, out b5), m14 = MaxDiff(A14, v14, out b14), m20 = MaxDiff(A20, v20, out b20), m50 = MaxDiff(A50, v50, out b50);
			string me5 = MaxDiff(E5, vE5, out be5), me13 = MaxDiff(E13, vE13, out be13);
			rep.Append("ATR5  max|dif| " + m5 + "  barras>1e-7: " + b5.ToString(CultureInfo.InvariantCulture) + "\n");
			rep.Append("ATR14 max|dif| " + m14 + "  barras>1e-7: " + b14.ToString(CultureInfo.InvariantCulture) + "\n");
			rep.Append("ATR20 max|dif| " + m20 + "  barras>1e-7: " + b20.ToString(CultureInfo.InvariantCulture) + "\n");
			rep.Append("ATR50 max|dif| " + m50 + "  barras>1e-7: " + b50.ToString(CultureInfo.InvariantCulture) + "\n");
			rep.Append("EMA5  max|dif| " + me5 + "  barras>1e-7: " + be5.ToString(CultureInfo.InvariantCulture) + "\n");
			rep.Append("EMA13 max|dif| " + me13 + "  barras>1e-7: " + be13.ToString(CultureInfo.InvariantCulture) + "\n");
			bool indOk = (b5 + b14 + b20 + b50 + be5 + be13) == 0;

			string newsMsg = LoadNews();
			rep.Append("\n[Calendario] " + newsMsg + "\n");
			if (ev == null) { Finish(rep, false, "sin calendario"); return; }

			// dias y barras de la ventana (09:29..11:32, dias habiles)
			SortedDictionary<int, List<int>> days = new SortedDictionary<int, List<int>>();
			for (int i = 0; i < n; i++)
			{
				DateTime d = new DateTime(T[i]);
				if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) continue;
				int stamp = d.Hour * 60 + d.Minute;
				if (stamp < 569 || stamp > 692) continue;
				if (i < 1) continue;
				int key = d.Year * 10000 + d.Month * 100 + d.Day;
				List<int> li;
				if (!days.TryGetValue(key, out li)) { li = new List<int>(); days[key] = li; }
				li.Add(i);
			}
			HashSet<int> excl = new HashSet<int>();
			foreach (string s in ExcludedList) excl.Add(int.Parse(s.Replace("-", ""), CultureInfo.InvariantCulture));

			string outDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results",
				"ET1B_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
			Directory.CreateDirectory(outDir);
			UTF8Encoding enc = new UTF8Encoding(false);
			bool allOk = indOk;
			rep.Append("\n[Paridad con los libros nativos]\n");
			for (int id = 1; id <= 9; id++)
			{
				List<Tr> tr = Simulate(MakeCfg(id), days, excl);
				StringBuilder tsv = new StringBuilder();
				tsv.Append("ref\tdir\tsignalTime\tentryPrice\tstopPrice\ttpPrice\texitName\texitExecTime\texitPrice\n");
				foreach (Tr t in tr)
					tsv.Append("N" + id.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(t.Dir.ToString(CultureInfo.InvariantCulture)).Append('\t')
					   .Append(new DateTime(t.Sig).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('\t')
					   .Append(t.Entry.ToString("0.00", CultureInfo.InvariantCulture)).Append('\t').Append(t.Stop.ToString("0.00", CultureInfo.InvariantCulture)).Append('\t')
					   .Append(double.IsNaN(t.Tp) ? "" : t.Tp.ToString("0.00", CultureInfo.InvariantCulture)).Append('\t')
					   .Append(t.Name).Append('\t').Append(new DateTime(t.ExitTime).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('\t')
					   .Append(t.ExitPx.ToString("0.00", CultureInfo.InvariantCulture)).Append('\n');
				File.WriteAllText(Path.Combine(outDir, "ENGINE_N" + id.ToString(CultureInfo.InvariantCulture) + ".tsv"), tsv.ToString(), enc);

				string folder;
				Dictionary<long, NatRow> nat = LoadNative(id, out folder);
				if (nat == null) { rep.Append("N" + id.ToString(CultureInfo.InvariantCulture) + ": falta el libro nativo; motor " + tr.Count.ToString(CultureInfo.InvariantCulture) + " operaciones\n"); allOk = false; continue; }
				int both = 0, eqDir = 0, eqEntry = 0, eqStop = 0, eqName = 0, eqTime = 0, eqPx = 0, eqAll = 0, onlyEng = 0;
				List<string> bad = new List<string>();
				HashSet<long> seen = new HashSet<long>();
				foreach (Tr t in tr)
				{
					NatRow r;
					if (!nat.TryGetValue(t.Sig, out r)) { onlyEng++; if (bad.Count < 3) bad.Add("solo motor " + new DateTime(t.Sig).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)); continue; }
					seen.Add(t.Sig); both++;
					bool a = r.Dir == t.Dir, b = Eq(r.Entry, t.Entry), c2 = Eq(r.Stop, t.Stop), d2 = r.Name == t.Name, e2 = r.ExitTime == t.ExitTime, f2 = Eq(r.ExitPx, t.ExitPx);
					if (a) eqDir++; if (b) eqEntry++; if (c2) eqStop++; if (d2) eqName++; if (e2) eqTime++; if (f2) eqPx++;
					if (a && b && c2 && d2 && e2 && f2) eqAll++;
					else if (bad.Count < 3) bad.Add("difiere " + new DateTime(t.Sig).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
						+ " nat=" + r.Name + "@" + r.ExitPx.ToString("0.00", CultureInfo.InvariantCulture) + " motor=" + t.Name + "@" + t.ExitPx.ToString("0.00", CultureInfo.InvariantCulture));
				}
				int onlyNat = nat.Count - seen.Count;
				bool ok = onlyEng == 0 && onlyNat == 0 && eqAll == both && both == nat.Count;
				if (!ok) allOk = false;
				rep.Append("N" + id.ToString(CultureInfo.InvariantCulture) + (ok ? "  OK " : "  FALLA ") + " nativo=" + nat.Count.ToString(CultureInfo.InvariantCulture)
					+ " motor=" + tr.Count.ToString(CultureInfo.InvariantCulture) + " iguales=" + eqAll.ToString(CultureInfo.InvariantCulture)
					+ " soloNat=" + onlyNat.ToString(CultureInfo.InvariantCulture) + " soloMotor=" + onlyEng.ToString(CultureInfo.InvariantCulture)
					+ " dir=" + eqDir.ToString(CultureInfo.InvariantCulture) + " ent=" + eqEntry.ToString(CultureInfo.InvariantCulture) + " stop=" + eqStop.ToString(CultureInfo.InvariantCulture)
					+ " nom=" + eqName.ToString(CultureInfo.InvariantCulture) + " hora=" + eqTime.ToString(CultureInfo.InvariantCulture) + " px=" + eqPx.ToString(CultureInfo.InvariantCulture) + "\n");
				foreach (string s in bad) rep.Append("    " + s + "\n");
			}
			Finish(rep, allOk, outDir);
		}

		private void Finish(StringBuilder rep, bool allOk, string outDir)
		{
			string status = allOk ? "PASS" : "FALLA";
			rep.Append("\nstatus: " + status + "\n");
			string dir = outDir;
			if (!Directory.Exists(dir))
			{
				dir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results", "ET1B_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
				Directory.CreateDirectory(dir);
			}
			UTF8Encoding enc = new UTF8Encoding(false);
			string text = rep.ToString();
			File.WriteAllText(Path.Combine(dir, "PARITY_REPORT.txt"), text, enc);
			string hash;
			using (SHA256 sha = SHA256.Create())
			{
				StringBuilder h = new StringBuilder();
				foreach (byte b in sha.ComputeHash(Encoding.UTF8.GetBytes(text))) h.Append(b.ToString("x2"));
				hash = h.ToString();
			}
			File.WriteAllText(Path.Combine(dir, "SUMMARY.txt"), "NQC ET1B - SUMMARY\nscript_version : " + ScriptVersion + "\nstatus : " + status + "\nreport_sha256 : " + hash + "\n", enc);
			Print("NQC_ET1B_END status=" + status + " sha=" + hash.Substring(0, 12) + " dir=" + dir);
		}
	}
}
