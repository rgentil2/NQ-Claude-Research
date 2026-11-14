#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
#endregion

//==============================================================================
// NQC_ET1_Core  -  Motor generico + ejecutor de portafolio para ET2/ET3 (WP1..WP5)
// Contrato NQC 2.0.0, EXECUTOR_SPEC 2.3.0.
//
// Biblioteca PURA: no usa ninguna API de NinjaTrader (solo System.*). No es una estrategia:
// no aparece en el Strategy Analyzer. La usan NQC_ET1_Regression (arnes de regresion) y,
// mas adelante, los ejecutores de ET2..ET7.
//
// Depende de NQC_AccountProfile, NQC_SessionRow y NQC_AccountSim, definidos en
// NQC_ET1C_AccountSim.cs (aprobado en ET1, SIN cambios). Ambos archivos deben estar instalados.
//
// Reutiliza LITERALMENTE la semantica de NQC_ET1B_EngineParity (8.110/8.110) y
// NQC_ET1B2_GapParity (46/46): ATR de Wilder con arranque de media creciente, EMA, redondeo al
// tick, stop al tocar, objetivo al atravesar, extremo mas cercano a la apertura en la misma barra,
// salto a la apertura, salidas forzadas al cierre de barra y ejecucion en la apertura siguiente.
//
// Determinismo: el motor no usa azar. La semilla (Config.Seed) se registra en SUMMARY y se
// ofrece NQCE_Rng (SplitMix64, identica en cualquier runtime) para el generador de ET3.
// Estilo: C# conservador (sin interpolacion, sin ?., sin out var) por no poder compilar aqui.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	// ------------------------------------------------------------------ constantes
	public static class NQCE_Const
	{
		public const string CoreVersion = "ET1CORE-1.0.0";
		public const string ContractVersion = "NQC 2.0.0";
		public const string SpecVersion = "2.3.0";
		public const double PointValue = 20.0;     // US$ por punto, 1 NQ
		public const double CostRT = 24.68;        // D-006: 2 ticks entrada + 2 salida + comision RT
		public const int StampFirstEntry = 571;    // 09:31
		public const int StampLastEntry = 689;     // senal en el cierre de 11:29 (instante <= 11:29:00)
		public const int StampFlat = 690;          // 11:30: orden de plano al cierre de esta barra
		public const int StampLoadFrom = 569;      // barras cargadas por dia: 09:29 .. 11:32
		public const int StampLoadTo = 692;
		public const long Min5 = 5L * 60L * 10000000L;
		public const string NewsFileName = "usd_high_medium_2020_2026.csv";
		public const string NewsSha256 = "93d837ac323c6f144786c507606b0b77f25c2fa33fba6d21bb941d2d4d9c377d";
		// Dias excluidos por ET0 (INCOMPLETA / SIN_VENTANA), igual que ET1B.
		public static readonly string[] ExcludedDays = new string[] {
			"2020-01-01","2020-03-09","2020-03-12","2020-03-16","2020-06-03","2020-10-23","2021-04-02","2021-08-24",
			"2022-08-03","2022-08-30","2022-12-26","2023-01-02","2023-04-07","2023-12-25","2024-01-01","2024-07-19",
			"2024-07-25","2024-08-05","2024-12-25","2025-01-01","2025-01-09","2025-06-18","2025-12-25","2026-01-01","2026-04-03"
		};
		public static HashSet<int> ExcludedSet()
		{
			HashSet<int> s = new HashSet<int>();
			foreach (string d in ExcludedDays) s.Add(int.Parse(d.Replace("-", ""), CultureInfo.InvariantCulture));
			return s;
		}
		public static double Rt(double x) { return Math.Round(x * 4.0) / 4.0; }
		public static string Hex(byte[] b) { StringBuilder h = new StringBuilder(); foreach (byte x in b) h.Append(x.ToString("x2")); return h.ToString(); }
		public static string Sha256Text(string s) { using (SHA256 sha = SHA256.Create()) return Hex(sha.ComputeHash(new UTF8Encoding(false).GetBytes(s))); }
	}

	// ------------------------------------------------------------------ azar determinista (ET3)
	public sealed class NQCE_Rng
	{
		private ulong s;
		public NQCE_Rng(long seed) { s = unchecked((ulong)seed); }
		public ulong Next()
		{
			unchecked
			{
				s += 0x9E3779B97F4A7C15UL;
				ulong z = s;
				z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
				z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
				return z ^ (z >> 31);
			}
		}
		public double NextDouble() { return (Next() >> 11) * (1.0 / 9007199254740992.0); }
		public int NextInt(int n) { return (int)(Next() % (ulong)n); }
	}

	// ------------------------------------------------------------------ barras e indicadores propios
	public sealed class NQCE_Bars
	{
		public long[] T; public double[] O, H, L, C; public int[] Stamp, DayKey; public int Count;
		private Dictionary<int, double[]> atr = new Dictionary<int, double[]>();
		private Dictionary<int, double[]> ema = new Dictionary<int, double[]>();

		public NQCE_Bars(long[] t, double[] o, double[] h, double[] l, double[] c)
		{
			T = t; O = o; H = h; L = l; C = c; Count = t.Length;
			Stamp = new int[Count]; DayKey = new int[Count];
			for (int i = 0; i < Count; i++)
			{
				DateTime d = new DateTime(T[i]);
				Stamp[i] = d.Hour * 60 + d.Minute;
				DayKey[i] = d.Year * 10000 + d.Month * 100 + d.Day;
			}
		}

		// ATR de Wilder; arranque: r[0]=H-L y media creciente hasta completar p (identico a ET1B/ET1B2).
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

		public static double[] EmaOwn(double[] c, int p)
		{
			int n = c.Length; double[] r = new double[n];
			double k1 = 2.0 / (1.0 + p), k2 = 1.0 - k1;
			for (int i = 0; i < n; i++) r[i] = (i == 0) ? c[0] : c[i] * k1 + k2 * r[i - 1];
			return r;
		}

		public double[] Atr(int p)
		{
			double[] r;
			if (!atr.TryGetValue(p, out r)) { r = AtrOwn(H, L, C, p); atr[p] = r; }
			return r;
		}

		public double[] Ema(int p)
		{
			double[] r;
			if (!ema.TryGetValue(p, out r)) { r = EmaOwn(C, p); ema[p] = r; }
			return r;
		}

		// Dias habiles con las barras de sello 09:29..11:32 (i >= 1), como ET1B.
		public SortedDictionary<int, List<int>> BuildDays()
		{
			SortedDictionary<int, List<int>> days = new SortedDictionary<int, List<int>>();
			for (int i = 1; i < Count; i++)
			{
				DateTime d = new DateTime(T[i]);
				if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) continue;
				int st = Stamp[i];
				if (st < NQCE_Const.StampLoadFrom || st > NQCE_Const.StampLoadTo) continue;
				List<int> li;
				if (!days.TryGetValue(DayKey[i], out li)) { li = new List<int>(); days[DayKey[i]] = li; }
				li.Add(i);
			}
			return days;
		}
	}

	// ------------------------------------------------------------------ noticias (D-027)
	public sealed class NQCE_News
	{
		public long[] Ev;
		public string Status = "SIN_CALENDARIO";
		public bool Ok;

		// Parsea el CSV (cabecera + filas con fecha en col 3 y hora en col 4) y verifica el SHA256.
		public static NQCE_News FromBytes(byte[] bytes, string expectedSha)
		{
			NQCE_News n = new NQCE_News();
			string sha;
			using (SHA256 s = SHA256.Create()) sha = NQCE_Const.Hex(s.ComputeHash(bytes));
			if (expectedSha != null && sha != expectedSha) { n.Status = "SHA256 distinto " + sha; return n; }
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
			n.Ev = list.ToArray(); n.Ok = true;
			n.Status = "OK " + n.Ev.Length.ToString(CultureInfo.InvariantCulture) + " eventos sha=" + sha;
			return n;
		}

		// c (sello de cierre de barra) en [t-5, t+5) de algun evento t.
		public bool InBlackout(long c)
		{
			if (Ev == null) return false;
			long target = c - NQCE_Const.Min5;
			int lo = 0, hi = Ev.Length;
			while (lo < hi) { int mid = (lo + hi) / 2; if (Ev[mid] > target) hi = mid; else lo = mid + 1; }
			return lo < Ev.Length && Ev[lo] <= c + NQCE_Const.Min5;
		}
	}

	// ------------------------------------------------------------------ genoma de entradas
	public sealed class NQCE_Ctx
	{
		public NQCE_Bars B; public int I; public int Stamp; public List<int> DayIdx;
	}

	public interface INQCE_Condition
	{
		bool Eval(NQCE_Ctx c);   // funcion pura de las barras hasta c.I; sin estado propio
		string Describe();
	}

	// ---- Condiciones de REFERENCIA (las necesarias para N1..N9). NO son la biblioteca D-018.
	public sealed class NQCE_CondStamp : INQCE_Condition
	{
		private int st; public NQCE_CondStamp(int stamp) { st = stamp; }
		public bool Eval(NQCE_Ctx c) { return c.Stamp == st; }
		public string Describe() { return "STAMP==" + st.ToString(CultureInfo.InvariantCulture); }
	}

	public sealed class NQCE_CondEmaCross : INQCE_Condition
	{
		private int f, s; public NQCE_CondEmaCross(int fast, int slow) { f = fast; s = slow; }
		public bool Eval(NQCE_Ctx c)
		{
			if (c.I < 1) return false;
			double[] a = c.B.Ema(f), b = c.B.Ema(s);
			return a[c.I] > b[c.I] && a[c.I - 1] <= b[c.I - 1];
		}
		public string Describe() { return "EMA" + f.ToString(CultureInfo.InvariantCulture) + "_CRUZA_ARRIBA_EMA" + s.ToString(CultureInfo.InvariantCulture); }
	}

	// Cierre > maximo de los sellos [from,to] del dia, solo con sello > afterStamp.
	public sealed class NQCE_CondCloseAboveRange : INQCE_Condition
	{
		private int from, to, after;
		public NQCE_CondCloseAboveRange(int fromStamp, int toStamp, int afterStamp) { from = fromStamp; to = toStamp; after = afterStamp; }
		public bool Eval(NQCE_Ctx c)
		{
			if (c.Stamp <= after) return false;
			double rh = double.NaN;
			for (int k = 0; k < c.DayIdx.Count; k++)
			{
				int ix = c.DayIdx[k]; int st = c.B.Stamp[ix];
				if (st >= from && st <= to) rh = double.IsNaN(rh) ? c.B.H[ix] : Math.Max(rh, c.B.H[ix]);
			}
			if (double.IsNaN(rh)) return false;
			return c.B.C[c.I] > rh;
		}
		public string Describe() { return "CIERRE>MAX(" + from.ToString(CultureInfo.InvariantCulture) + ".." + to.ToString(CultureInfo.InvariantCulture) + ")@>" + after.ToString(CultureInfo.InvariantCulture); }
	}

	// Miembro = genoma de entrada (AND de condiciones) + genes de salida ATR (D-021).
	public sealed class NQCE_Member
	{
		public string Id;
		public int Dir;                 // +1 largo, -1 corto
		public List<INQCE_Condition> Conditions = new List<INQCE_Condition>();
		public int AtrPeriod;           // D-019a: 5..100
		public double Ksl;              // D-019a: 0,5..6,0 paso 0,25
		public double Ktp = double.NaN; // NaN = sin objetivo
		public int MaxPerDay = 1;       // senales aceptadas por sesion (0 = sin limite)
		public int TimeExitStamp = -1;  // hora de plano propia (sello), -1 = ninguna

		public bool Signal(NQCE_Ctx c)
		{
			if (Conditions.Count == 0) return false;
			for (int k = 0; k < Conditions.Count; k++) if (!Conditions[k].Eval(c)) return false;
			return true;
		}
		public string GenomeText()
		{
			StringBuilder sb = new StringBuilder();
			sb.Append("dir=").Append(Dir.ToString(CultureInfo.InvariantCulture)).Append(" atr=").Append(AtrPeriod.ToString(CultureInfo.InvariantCulture))
			  .Append(" ksl=").Append(Ksl.ToString("0.00", CultureInfo.InvariantCulture))
			  .Append(" ktp=").Append(double.IsNaN(Ktp) ? "NA" : Ktp.ToString("0.00", CultureInfo.InvariantCulture))
			  .Append(" maxday=").Append(MaxPerDay.ToString(CultureInfo.InvariantCulture)).Append(" timex=").Append(TimeExitStamp.ToString(CultureInfo.InvariantCulture)).Append(" if ");
			for (int k = 0; k < Conditions.Count; k++) { if (k > 0) sb.Append(" AND "); sb.Append(Conditions[k].Describe()); }
			return sb.ToString();
		}
	}

	public sealed class NQCE_Portfolio
	{
		public string Name; public List<NQCE_Member> Members = new List<NQCE_Member>();   // el orden ES la prioridad (D-016)
	}

	// ------------------------------------------------------------------ configuracion, hooks y resultados
	public sealed class NQCE_Config
	{
		public double CostRT = NQCE_Const.CostRT;
		public double CostMult = 1.0;                    // 1,5 para estres
		public bool Protect = false;                     // capa protectora D-007
		public double PauseLevel = -500, CloseLevel = -750, Cushion = 400;
		public bool PermanentDiagnostic = false;         // LOCK_MODE=PERMANENT_DIAGNOSTIC: nunca para aceptar portafolios
		public NQC_AccountProfile Profile = NQC_AccountProfile.RapidIntraday50K();
		public NQCE_News News;                           // null = sin noticias
		public HashSet<int> Excluded = NQCE_Const.ExcludedSet();
		public long Seed = 0;                            // se registra; el motor no usa azar
		public double Cost() { return CostRT * CostMult; }
	}

	public sealed class NQCE_Trade
	{
		public string Portfolio, Member, Reason; public int Dir, DayKey;
		public long SigTick, EntryTick, ExitTick;
		public double EntryPx, ExitPx, Stop, Tp = double.NaN, Atr, Ksl, Ktp = double.NaN;
		public bool Gapped; public double MaePts, MfePts, GrossPts, Net;
	}

	public sealed class NQCE_ProtectEvent
	{
		public string Kind; public long Tick; public int DayKey;
		public double SessionPnl, Balance, Peak, DdClosed, DdM2m, Cushion;
	}

	public sealed class NQCE_SessionOut { public int DayKey; public NQC_SessionRow Row; public string AccountEvent; }

	// Hooks para ET2..ET7 y el simulador de cuenta. Todos opcionales (pasar null o heredar de NQCE_NullHooks).
	public interface INQCE_Hooks
	{
		void OnSignal(int dayKey, long tick, string member, string outcome);      // outcome: EXECUTED o motivo de descarte
		void OnTradeClosed(NQCE_Trade t);
		void OnBarMark(int dayKey, long tick, double sessionEquity, bool positionOpen);
		void OnProtect(NQCE_ProtectEvent e);
		void OnSessionEnd(NQCE_SessionOut s);
	}
	public class NQCE_NullHooks : INQCE_Hooks
	{
		public virtual void OnSignal(int dayKey, long tick, string member, string outcome) { }
		public virtual void OnTradeClosed(NQCE_Trade t) { }
		public virtual void OnBarMark(int dayKey, long tick, double sessionEquity, bool positionOpen) { }
		public virtual void OnProtect(NQCE_ProtectEvent e) { }
		public virtual void OnSessionEnd(NQCE_SessionOut s) { }
	}

	public sealed class NQCE_Result
	{
		public string PortfolioName;
		public List<NQCE_Trade> Trades = new List<NQCE_Trade>();
		public List<NQCE_SessionOut> Sessions = new List<NQCE_SessionOut>();      // solo sesiones NO excluidas
		public List<NQCE_ProtectEvent> Protect = new List<NQCE_ProtectEvent>();
		// clave "yyyymm|miembro" -> contadores en el orden de NQCE_Executor.WfCols
		public SortedDictionary<string, int[]> Waterfall = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
		public int OpenAtEnd;            // posiciones abiertas al terminar la sesion (descartadas); debe ser 0
		public int ExcludedDaysSeen;
		public double DdClosed, DdM2m;   // maximo pico a valle (US$), acumulado de todo el rango
		public double[] DailyNet;        // neto por sesion no excluida (incluye sesiones sin operaciones)

		public List<NQC_SessionRow> Rows()
		{
			List<NQC_SessionRow> r = new List<NQC_SessionRow>();
			foreach (NQCE_SessionOut s in Sessions) r.Add(s.Row);
			return r;
		}
	}

	// Cuenta continua para la guardia de DD. Misma formula que NQC_AccountSim.RunFrom; se reinicia
	// tras PASS o FAIL. (Interpretacion de ET1: D-007 no define con que arranque se calcula el colchon.)
	public sealed class NQCE_AccountTracker
	{
		public double Bal, Hi, Level, Best; public int Days; private NQC_AccountProfile p;
		public NQCE_AccountTracker(NQC_AccountProfile prof) { p = prof; Reset(); }
		public void Reset() { Bal = 0; Hi = 0; Level = -p.Mll; Best = 0; Days = 0; }
		public double Room() { return Bal - Level; }
		// Devuelve "", "PASS" o "FAIL:<razon>"; aplica el reinicio.
		public string Update(double pnl, double minEq, int n)
		{
			if (n > 0 && Bal + minEq <= Level) { Reset(); return "FAIL:MLL"; }
			Bal += pnl;
			if (n > 0) { Days++; if (pnl > Best) Best = pnl; }
			if (Bal <= Level && n > 0) { Reset(); return "FAIL:MLL_EOD"; }
			if (Bal > Hi) Hi = Bal;
			Level = Math.Min(Hi - p.Mll, p.LockLevel);
			if (Bal >= p.Target && Days >= p.MinDays && Best <= p.Consistency * Bal) { Reset(); return "PASS"; }
			return "";
		}
	}

	// ------------------------------------------------------------------ nucleo de fills (compartido)
	public static class NQCE_Fill
	{
		// Reglas de EXECUTOR_SPEC 2.3.0 §3. Devuelve true si la barra cierra la posicion.
		// name: "SL" o "TP". px: precio de salida. gapped: salida a la apertura mas alla del stop.
		public static bool Check(int dir, double o, double h, double l, double stop, double tp, out string name, out double px, out bool gapped)
		{
			name = null; px = 0; gapped = false;
			bool slHit = dir == 1 ? (l <= stop) : (h >= stop);
			bool tpHit = false;
			if (!double.IsNaN(tp)) tpHit = dir == 1 ? (h > tp) : (l < tp);
			if (!slHit && !tpHit) return false;
			string first;
			if (slHit && tpHit)
			{
				bool hiFirst = Math.Abs(h - o) <= Math.Abs(l - o);
				if (dir == 1) first = hiFirst ? "TP" : "SL"; else first = hiFirst ? "SL" : "TP";
			}
			else first = slHit ? "SL" : "TP";
			name = first;
			if (first == "SL")
			{
				gapped = dir == 1 ? (o < stop) : (o > stop);
				px = gapped ? o : stop;
			}
			else px = tp;
			return true;
		}

		public static double StopLevel(int dir, double entry, double ksl, double atr) { return NQCE_Const.Rt(dir == 1 ? entry - ksl * atr : entry + ksl * atr); }
		public static double TpLevel(int dir, double entry, double ktp, double atr) { return NQCE_Const.Rt(dir == 1 ? entry + ktp * atr : entry - ktp * atr); }

		public class IsolatedTrade
		{
			public bool Ok; public int Dir; public long Sig, ExitTime; public double Entry, Stop, ExitPx; public string Name; public bool Gapped;
		}

		// Operacion aislada (sin ventana, sin noticias, sin conflictos): la usa la regresion de los 46 saltos.
		// Mismo orden de pasos que Et1b2Core.Run de ET1B2: ejecucion pendiente, stop, plano de las 11:30.
		public static IsolatedTrade SimulateIsolated(NQCE_Bars b, double[] atrArr, long sig, int dir, double ksl)
		{
			IsolatedTrade t = new IsolatedTrade(); t.Sig = sig; t.Dir = dir;
			int i = Array.BinarySearch(b.T, sig);
			if (i < 0 || i + 1 >= b.Count) return t;
			int e = i + 1;
			double entry = b.O[e];
			double stop = StopLevel(dir, entry, ksl, atrArr[i]);
			t.Entry = entry; t.Stop = stop;
			bool pend = false;
			for (int j = e; j < b.Count; j++)
			{
				if (pend) { t.Name = "TIME"; t.ExitPx = b.O[j]; t.ExitTime = b.T[j]; t.Gapped = false; t.Ok = true; return t; }
				string nm; double px; bool gp;
				if (Check(dir, b.O[j], b.H[j], b.L[j], stop, double.NaN, out nm, out px, out gp))
				{ t.Name = nm; t.ExitPx = px; t.ExitTime = b.T[j]; t.Gapped = gp; t.Ok = true; return t; }
				if (b.Stamp[j] == NQCE_Const.StampFlat) pend = true;
			}
			return t;
		}
	}

	// ------------------------------------------------------------------ ejecutor central de portafolio
	public sealed class NQCE_Executor
	{
		public static readonly string[] WfCols = new string[] { "RAW", "OUT_WINDOW", "NEWS", "BUSY", "PAUSE", "DAILY_CLOSE", "DD_GUARD", "MAXDAY", "EXCL_DAY", "EXECUTED" };
		private const int cRaw = 0, cOut = 1, cNews = 2, cBusy = 3, cPause = 4, cClose = 5, cGuard = 6, cMax = 7, cExcl = 8, cExec = 9;

		private class Pos
		{
			public NQCE_Member M; public int Dir; public double Entry, Stop, Tp, Atr, Ksl, Ktp, Mae, Mfe; public long Sig, EntryTick;
		}

		private NQCE_Bars bars; private NQCE_Portfolio pf; private NQCE_Config cfg; private INQCE_Hooks hk;
		private NQCE_Result res; private NQCE_AccountTracker acct;
		private double cumReal, peakClosed, peakM2m;      // para DD cerrado y DD M2M
		private bool permLock;

		public static NQCE_Result Run(NQCE_Bars bars, NQCE_Portfolio pf, NQCE_Config cfg, INQCE_Hooks hooks)
		{
			NQCE_Executor x = new NQCE_Executor();
			x.bars = bars; x.pf = pf; x.cfg = cfg; x.hk = hooks == null ? new NQCE_NullHooks() : hooks;
			x.res = new NQCE_Result(); x.res.PortfolioName = pf.Name;
			x.acct = new NQCE_AccountTracker(cfg.Profile);
			x.Execute();
			return x.res;
		}

		private int[] Wf(int dayKey, int memberIdx)
		{
			string k = (dayKey / 100).ToString(CultureInfo.InvariantCulture) + "|" + pf.Members[memberIdx].Id;
			int[] a;
			if (!res.Waterfall.TryGetValue(k, out a)) { a = new int[WfCols.Length]; res.Waterfall[k] = a; }
			return a;
		}

		private void Execute()
		{
			SortedDictionary<int, List<int>> days = bars.BuildDays();
			List<double> daily = new List<double>();
			foreach (KeyValuePair<int, List<int>> kv in days)
			{
				if (cfg.Excluded.Contains(kv.Key)) { ExcludedDay(kv.Key, kv.Value); continue; }
				double net = RunDay(kv.Key, kv.Value);
				daily.Add(net);
			}
			res.DailyNet = daily.ToArray();
		}

		// Dia excluido por ET0: no se generan senales ni posiciones; solo se cuentan las brutas.
		private void ExcludedDay(int dayKey, List<int> idx)
		{
			res.ExcludedDaysSeen++;
			NQCE_Ctx ctx = new NQCE_Ctx(); ctx.B = bars; ctx.DayIdx = idx;
			foreach (int i in idx)
			{
				int st = bars.Stamp[i];
				if (st < NQCE_Const.StampFirstEntry || st > NQCE_Const.StampLastEntry) continue;
				ctx.I = i; ctx.Stamp = st;
				for (int m = 0; m < pf.Members.Count; m++)
					if (pf.Members[m].Signal(ctx)) { int[] w = Wf(dayKey, m); w[cRaw]++; w[cExcl]++; }
			}
		}

		private double RunDay(int dayKey, List<int> idx)
		{
			int nm = pf.Members.Count;
			int[] today = new int[nm];
			bool hasPos = false; Pos P = null;
			int pend = 0;                         // 0 nada, 1 entrada, 2 salida
			NQCE_Member pM = null; int pDir = 0; double pAtr = 0; long pSig = 0; string pWhy = null;
			double sessReal = 0, sessMin = 0; int nTrades = 0;
			double room0 = acct.Room();
			bool pauseOn = false, closeOn = false, guardOn = false;
			if (permLock && cfg.Protect) pauseOn = true;
			NQCE_Ctx ctx = new NQCE_Ctx(); ctx.B = bars; ctx.DayIdx = idx;

			for (int j = 0; j < idx.Count; j++)
			{
				int i = idx[j]; long ct = bars.T[i]; int stamp = bars.Stamp[i];
				double o = bars.O[i], h = bars.H[i], l = bars.L[i], c = bars.C[i];

				// 1) ejecuciones a la apertura de la barra
				if (pend == 1)
				{
					pend = 0;
					P = new Pos(); P.M = pM; P.Dir = pDir; P.Entry = o; P.Sig = pSig; P.EntryTick = ct; P.Atr = pAtr; P.Ksl = pM.Ksl; P.Ktp = pM.Ktp;
					P.Stop = NQCE_Fill.StopLevel(pDir, o, pM.Ksl, pAtr);
					P.Tp = double.IsNaN(pM.Ktp) ? double.NaN : NQCE_Fill.TpLevel(pDir, o, pM.Ktp, pAtr);
					hasPos = true;
				}
				else if (pend == 2 && hasPos)
				{
					sessReal += CloseTrade(P, dayKey, ct, o, pWhy, false);
					nTrades++; hasPos = false; P = null; pend = 0;
					if (sessReal < sessMin) sessMin = sessReal;
				}

				// 2) stop / objetivo dentro de la barra (activos desde la barra de entrada)
				if (hasPos)
				{
					double adv = P.Dir == 1 ? P.Entry - l : h - P.Entry, fav = P.Dir == 1 ? h - P.Entry : P.Entry - l;
					if (adv > P.Mae) P.Mae = adv; if (fav > P.Mfe) P.Mfe = fav;
					string nmx; double px; bool gp;
					if (NQCE_Fill.Check(P.Dir, o, h, l, P.Stop, P.Tp, out nmx, out px, out gp))
					{
						sessReal += CloseTrade(P, dayKey, ct, px, nmx, gp);
						nTrades++; hasPos = false; P = null;
						if (sessReal < sessMin) sessMin = sessReal;
					}
				}

				// 3) marca M2M al cierre de la barra (la posicion abierta incluye ya el costo de ida y vuelta)
				double eq = sessReal;
				if (hasPos) eq += P.Dir * (c - P.Entry) * NQCE_Const.PointValue - cfg.Cost();
				if (eq < sessMin) sessMin = eq;
				double cumM2m = cumReal + eq;
				if (cumM2m > peakM2m) peakM2m = cumM2m;
				if (peakM2m - cumM2m > res.DdM2m) res.DdM2m = peakM2m - cumM2m;
				hk.OnBarMark(dayKey, ct, eq, hasPos);

				// 4) capa protectora (D-007): dispara con la marca al cierre; se libera al terminar la sesion
				bool closeNow = false;
				if (cfg.Protect)
				{
					if (!closeOn && eq <= cfg.CloseLevel) { closeOn = true; closeNow = true; Ev("DAILY_CLOSE", ct, dayKey, sessReal, eq, room0); }
					if (!pauseOn && sessReal <= cfg.PauseLevel) { pauseOn = true; Ev("PAUSE", ct, dayKey, sessReal, eq, room0); }
				}

				// 5) logica al cierre de la barra
				bool inb = cfg.News != null && cfg.News.InBlackout(ct);
				if (hasPos)
				{
					if (pend == 0)
					{
						string why = null;
						if (stamp == NQCE_Const.StampFlat) why = "TIME";
						else if (P.M.TimeExitStamp > 0 && stamp == P.M.TimeExitStamp) why = "TIME";
						else if (inb) why = "NEWS";
						else if (closeNow) why = "DAILY_CLOSE";
						if (why != null) { pend = 2; pWhy = why; }
					}
				}
				// Senales: se evaluan todos los miembros por prioridad. Evaluar no cambia estado.
				bool inWin = stamp >= NQCE_Const.StampFirstEntry && stamp <= NQCE_Const.StampLastEntry;
				bool busy = hasPos || pend != 0;
				bool taken = false;
				ctx.I = i; ctx.Stamp = stamp;
				for (int m = 0; m < nm; m++)
				{
					NQCE_Member mem = pf.Members[m];
					if (!mem.Signal(ctx)) continue;
					int[] w = Wf(dayKey, m);
					w[cRaw]++;
					if (!inWin) { w[cOut]++; hk.OnSignal(dayKey, ct, mem.Id, "OUT_WINDOW"); continue; }
					if (inb) { w[cNews]++; hk.OnSignal(dayKey, ct, mem.Id, "NEWS"); continue; }
					if (busy || taken) { w[cBusy]++; hk.OnSignal(dayKey, ct, mem.Id, "BUSY"); continue; }
					if (cfg.Protect)
					{
						if (closeOn) { w[cClose]++; hk.OnSignal(dayKey, ct, mem.Id, "DAILY_CLOSE"); continue; }
						if (pauseOn) { w[cPause]++; hk.OnSignal(dayKey, ct, mem.Id, "PAUSE"); continue; }
						if (guardOn || room0 + sessReal <= cfg.Cushion)
						{
							if (!guardOn) { guardOn = true; Ev("DD_GUARD", ct, dayKey, sessReal, eq, room0); }
							w[cGuard]++; hk.OnSignal(dayKey, ct, mem.Id, "DD_GUARD"); continue;
						}
					}
					if (mem.MaxPerDay > 0 && today[m] >= mem.MaxPerDay) { w[cMax]++; hk.OnSignal(dayKey, ct, mem.Id, "MAXDAY"); continue; }
					// aceptada
					today[m]++; taken = true; w[cExec]++;
					pend = 1; pM = mem; pDir = mem.Dir; pSig = ct; pAtr = bars.Atr(mem.AtrPeriod)[i];
					hk.OnSignal(dayKey, ct, mem.Id, "EXECUTED");
				}
			}

			// fin de sesion: una posicion aun abierta es un defecto de datos (barras 11:31 ausentes); se descarta y se cuenta
			if (hasPos || pend != 0) res.OpenAtEnd++;
			if (cfg.Protect && cfg.PermanentDiagnostic && (pauseOn || closeOn || guardOn)) permLock = true;

			NQCE_SessionOut so = new NQCE_SessionOut(); so.DayKey = dayKey;
			so.Row = new NQC_SessionRow(sessReal, Math.Min(sessMin, 0.0), nTrades);
			so.AccountEvent = acct.Update(so.Row.Pnl, so.Row.MinEq, so.Row.N);
			res.Sessions.Add(so);
			hk.OnSessionEnd(so);
			return sessReal;
		}

		private double CloseTrade(Pos p, int dayKey, long exitTick, double exitPx, string reason, bool gapped)
		{
			NQCE_Trade t = new NQCE_Trade();
			t.Portfolio = pf.Name; t.Member = p.M.Id; t.Dir = p.Dir; t.DayKey = dayKey;
			t.SigTick = p.Sig; t.EntryTick = p.EntryTick; t.ExitTick = exitTick;
			t.EntryPx = p.Entry; t.ExitPx = exitPx; t.Stop = p.Stop; t.Tp = p.Tp; t.Atr = p.Atr; t.Ksl = p.Ksl; t.Ktp = p.Ktp;
			t.Reason = reason; t.Gapped = gapped; t.MaePts = p.Mae; t.MfePts = p.Mfe;
			t.GrossPts = p.Dir * (exitPx - p.Entry);
			t.Net = t.GrossPts * NQCE_Const.PointValue - cfg.Cost();     // costo fijo, una sola vez, despues de la simulacion bruta (§4)
			cumReal += t.Net;
			if (cumReal > peakClosed) peakClosed = cumReal;
			if (peakClosed - cumReal > res.DdClosed) res.DdClosed = peakClosed - cumReal;
			res.Trades.Add(t);
			hk.OnTradeClosed(t);
			return t.Net;
		}

		private void Ev(string kind, long tick, int dayKey, double sessPnl, double eq, double room0)
		{
			NQCE_ProtectEvent e = new NQCE_ProtectEvent();
			e.Kind = kind; e.Tick = tick; e.DayKey = dayKey; e.SessionPnl = sessPnl;
			e.Balance = acct.Bal; e.Peak = acct.Hi; e.DdClosed = peakClosed - cumReal; e.DdM2m = peakM2m - (cumReal + eq); e.Cushion = room0 + sessPnl;
			res.Protect.Add(e); hk.OnProtect(e);
		}
	}

	// ------------------------------------------------------------------ metricas (D-022) y auditoria (§9)
	public static class NQCE_Metrics
	{
		// 100 x R^2 de la regresion OLS de la equity neta diaria contra el indice de dia; 0 si la pendiente <= 0.
		public static double Stability(double[] dailyNet)
		{
			int n = dailyNet.Length; if (n < 3) return 0;
			double[] y = new double[n]; double cum = 0;
			for (int i = 0; i < n; i++) { cum += dailyNet[i]; y[i] = cum; }
			double mx = (n - 1) / 2.0, my = 0; for (int i = 0; i < n; i++) my += y[i]; my /= n;
			double sxx = 0, sxy = 0, syy = 0;
			for (int i = 0; i < n; i++) { double dx = i - mx, dy = y[i] - my; sxx += dx * dx; sxy += dx * dy; syy += dy * dy; }
			if (sxx <= 0 || syy <= 0) return 0;
			double slope = sxy / sxx; if (slope <= 0) return 0;
			return 100.0 * (sxy * sxy) / (sxx * syy);
		}

		public static double Symmetry(List<NQCE_Trade> trades)
		{
			double lg = 0, sh = 0;
			foreach (NQCE_Trade t in trades) { if (t.Dir == 1) lg += t.Net; else sh += t.Net; }
			if (lg > 0 && sh > 0) return 100.0 * Math.Min(lg, sh) / Math.Max(lg, sh);
			return 0;
		}
	}

	public static class NQCE_Audit
	{
		private static string I(long x) { return x.ToString(CultureInfo.InvariantCulture); }
		private static string D2(double x) { return x.ToString("0.00", CultureInfo.InvariantCulture); }
		private static string Ts(long ticks) { return new DateTime(ticks).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); }

		// MAE/MFE: extremos de barra sin cronologia => PROXY (§6). No dispara ningun control.
		public static string TradesTsv(NQCE_Result r)
		{
			StringBuilder s = new StringBuilder();
			s.Append("portfolio\tmember\tsignal_bar\tentry_time\texit_time\tdir\tentry_px\texit_px\tatr\tk_sl\tk_tp\tstop_pts\tstop_usd\texit_reason\tgapped\tmae_proxy_usd\tmfe_proxy_usd\tnet_usd\n");
			foreach (NQCE_Trade t in r.Trades)
			{
				double sp = Math.Abs(t.EntryPx - t.Stop);
				s.Append(t.Portfolio).Append('\t').Append(t.Member).Append('\t').Append(Ts(t.SigTick)).Append('\t').Append(Ts(t.EntryTick)).Append('\t').Append(Ts(t.ExitTick)).Append('\t')
				 .Append(I(t.Dir)).Append('\t').Append(D2(t.EntryPx)).Append('\t').Append(D2(t.ExitPx)).Append('\t').Append(t.Atr.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\t')
				 .Append(D2(t.Ksl)).Append('\t').Append(double.IsNaN(t.Ktp) ? "" : D2(t.Ktp)).Append('\t').Append(D2(sp)).Append('\t').Append(D2(sp * NQCE_Const.PointValue)).Append('\t')
				 .Append(t.Reason).Append('\t').Append(t.Gapped ? "1" : "0").Append('\t').Append(D2(t.MaePts * NQCE_Const.PointValue)).Append('\t').Append(D2(t.MfePts * NQCE_Const.PointValue)).Append('\t')
				 .Append(D2(t.Net)).Append('\n');
			}
			return s.ToString();
		}

		public static string WaterfallTsv(NQCE_Result r)
		{
			StringBuilder s = new StringBuilder();
			s.Append("portfolio\tmonth\tmember");
			foreach (string c in NQCE_Executor.WfCols) s.Append('\t').Append(c.ToLowerInvariant());
			s.Append('\n');
			foreach (KeyValuePair<string, int[]> kv in r.Waterfall)
			{
				string[] k = kv.Key.Split('|');
				s.Append(r.PortfolioName).Append('\t').Append(k[0].Substring(0, 4)).Append('-').Append(k[0].Substring(4)).Append('\t').Append(k[1]);
				foreach (int v in kv.Value) s.Append('\t').Append(I(v));
				s.Append('\n');
			}
			return s.ToString();
		}

		public static string ProtectTsv(NQCE_Result r)
		{
			StringBuilder s = new StringBuilder();
			s.Append("kind\ttimestamp\tsession_pnl\tbalance\tpeak\tdd_closed\tdd_m2m\tcushion_to_mll\trelease\n");
			foreach (NQCE_ProtectEvent e in r.Protect)
			{
				// liberacion: al terminar la ventana (11:30) de la misma sesion; ningun bloqueo es permanente (D-007)
				string rel = new DateTime(e.Tick).Date.AddHours(11).AddMinutes(30).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
				s.Append(e.Kind).Append('\t').Append(Ts(e.Tick)).Append('\t').Append(D2(e.SessionPnl)).Append('\t').Append(D2(e.Balance)).Append('\t').Append(D2(e.Peak)).Append('\t')
				 .Append(D2(e.DdClosed)).Append('\t').Append(D2(e.DdM2m)).Append('\t').Append(D2(e.Cushion)).Append('\t').Append(rel).Append('\n');
			}
			return s.ToString();
		}

		// Un renglon por sesion de arranque posible; NQC_AccountSim.RunFrom (aprobado en ET1C).
		public static string AccountSimTsv(NQCE_Result r, NQC_AccountProfile p, out double pPass, out int pass, out int fail, out int cens, out double meanDays)
		{
			List<NQC_SessionRow> rows = r.Rows();
			StringBuilder s = new StringBuilder();
			s.Append("start_date\tresult\tsessions\tbest_day_usd\tfinal_balance\tfail_reason\n");
			pass = 0; fail = 0; cens = 0; long dsum = 0; int dn = 0;
			for (int st = 0; st < rows.Count; st++)
			{
				NQC_AccResult a = NQC_AccountSim.RunFrom(rows, st, p);
				double best = 0;
				for (int k = st; k < st + a.Sessions && k < rows.Count; k++) if (rows[k].N > 0 && rows[k].Pnl > best) best = rows[k].Pnl;
				if (a.Status == "PASS") pass++; else if (a.Status == "FAIL") fail++; else cens++;
				if (a.Status != "CENSORED") { dsum += a.Sessions; dn++; }
				int dk = r.Sessions[st].DayKey;
				s.Append((dk / 10000).ToString("0000", CultureInfo.InvariantCulture) + "-" + ((dk / 100) % 100).ToString("00", CultureInfo.InvariantCulture) + "-" + (dk % 100).ToString("00", CultureInfo.InvariantCulture))
				 .Append('\t').Append(a.Status).Append('\t').Append(I(a.Sessions)).Append('\t').Append(D2(best)).Append('\t').Append(D2(a.Bal)).Append('\t').Append(a.Why).Append('\n');
			}
			pPass = (pass + fail) == 0 ? double.NaN : (double)pass / (pass + fail);
			meanDays = dn == 0 ? double.NaN : (double)dsum / dn;
			return s.ToString();
		}
	}
}
