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
// NQC_ET1A_NativeRefs  -  ET1 (parte A): estrategias nativas de referencia N1..N9
// Contrato NQC 2.0.0, EXECUTOR_SPEC 2.1.0. Ver docs/ET1_REFERENCES.md.
//
// Son estrategias triviales con ORDENES NATIVAS de NT8. Su unico fin es producir el
// "libro de operaciones" que NT8 considera verdadero, para compararlo despues con el
// motor propio. Sus resultados economicos NO son hallazgos y NO se miran.
//
// Configuracion obligatoria en Strategy Analyzer: Slippage 0, comision ninguna,
// rango 2020-01-01 a 2022-12-31 (IS). Las barras posteriores a 2022-12-31 se ignoran.
// Serie: NQ M1 Last, Merge back adjusted.
//
// Salidas en <Documentos>\NinjaTrader 8\NQC_Results\ET1A_N<id>_<fecha_hora>\ :
//   TRADES_NATIVE.tsv, SUMMARY.txt
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public abstract class NQC_NativeRefBase : Strategy
	{
		private const string ScriptVersion = "ET1A-1.0.0";
		private const string NewsSha256 = "93d837ac323c6f144786c507606b0b77f25c2fa33fba6d21bb941d2d4d9c377d";
		private const string NewsFileName = "usd_high_medium_2020_2026.csv";

		// Dias excluidos por ET0 (sesiones incompletas o sin ventana).
		private static readonly string[] ExcludedList = new string[] {
			"2020-01-01","2020-03-09","2020-03-12","2020-03-16","2020-06-03","2020-10-23","2021-04-02","2021-08-24",
			"2022-08-03","2022-08-30","2022-12-26","2023-01-02","2023-04-07","2023-12-25","2024-01-01","2024-07-19",
			"2024-07-25","2024-08-05","2024-12-25","2025-01-01","2025-01-09","2025-06-18","2025-12-25","2026-01-01","2026-04-03"
		};

		private const int FirstEntryStamp = 9 * 60 + 31;   // 09:31
		private const int LastEntryStamp  = 11 * 60 + 29;  // 11:29 (entrada en el instante 11:29:00)
		private const int FlatStamp       = 11 * 60 + 30;  // 11:30

		// ---- configuracion de cada referencia (la fija Setup() en la subclase)
		protected abstract int RefId { get; }
		protected abstract void Setup();
		protected abstract int Signal(int stamp);          // +1 long, -1 short, 0 nada

		protected int    AtrPeriod = 14;
		protected double KSl = 1.5;
		protected double KTp = -1;                          // < 0: sin objetivo
		protected int    MaxTradesPerDay = 1;
		protected int    TimeExitStamp = -1;                // salida por hora propia (ej. 10:30)
		protected double SigKSl;                            // lo puede cambiar Signal() (N8)
		protected double SigKTp;

		// estado
		private HashSet<string> excluded;
		private List<DateTime> news;
		private int newsPtr;
		private bool newsOk;
		private string newsMsg = "";
		private int newsCount;
		private ATR atr;
		protected EMA ema1;
		protected EMA ema2;
		protected double rangeHigh, rangeLow;
		private string curDay = "";
		private int tradesToday;
		private int entrySubmittedBar = -1;
		private int exitSubmittedBar = -1;
		private double sigAtr;
		private int sigStamp;
		private DateTime sigTime;
		private double curStop, curTp;
		private int dir;
		private double entryPrice;
		private DateTime entryTime;
		private DateTime entryTime0;
		private int entryBar;
		private bool wroteOutputs;
		private DateTime firstBar, lastBar;
		private long barsSeen;
		private List<string> rows;
		private Dictionary<string, int> exitCount;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "ET1A referencia nativa N" + RefId.ToString(CultureInfo.InvariantCulture);
				Name = "NQC_ET1A_N" + RefId.ToString(CultureInfo.InvariantCulture);
				Calculate = Calculate.OnBarClose;
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = false;
				BarsRequiredToTrade = 0;
				TraceOrders = false;
			}
			else if (State == State.Configure)
			{
				Setup();
				excluded = new HashSet<string>(ExcludedList);
				rows = new List<string>();
				exitCount = new Dictionary<string, int>();
			}
			else if (State == State.DataLoaded)
			{
				atr = ATR(AtrPeriod);
				ema1 = EMA(5);
				ema2 = EMA(13);
				LoadNews();
			}
			else if (State == State.Terminated)
			{
				WriteOutputs();
			}
		}

		private string FindNewsPath()
		{
			string root = NinjaTrader.Core.Globals.UserDataDir;
			string[] folders = new string[] { "NQC_Data", "NQC Data" };
			foreach (string f in folders)
			{
				string p = Path.Combine(Path.Combine(root, f), NewsFileName);
				if (File.Exists(p)) return p;
			}
			return null;
		}

		private void LoadNews()
		{
			news = new List<DateTime>();
			newsOk = false;
			string p = FindNewsPath();
			if (p == null) { newsMsg = "FALTA el archivo " + NewsFileName + " en NQC_Data o NQC Data"; return; }
			byte[] bytes = File.ReadAllBytes(p);
			using (SHA256 sha = SHA256.Create())
			{
				StringBuilder sb = new StringBuilder();
				foreach (byte b in sha.ComputeHash(bytes)) sb.Append(b.ToString("x2"));
				if (sb.ToString() != NewsSha256) { newsMsg = "SHA256 distinto: " + sb.ToString(); return; }
			}
			string text = Encoding.UTF8.GetString(bytes);
			string[] lines = text.Split('\n');
			for (int i = 1; i < lines.Length; i++)
			{
				string ln = lines[i].TrimEnd('\r');
				if (ln.Length == 0) continue;
				string[] f = ln.Split(new char[] { ',' }, 5);
				if (f.Length < 4) continue;
				DateTime t;
				if (DateTime.TryParseExact(f[2] + " " + f[3], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
					news.Add(t);
			}
			news.Sort();
			newsCount = news.Count;
			newsOk = newsCount > 0;
			newsMsg = newsOk ? "OK " + newsCount.ToString(CultureInfo.InvariantCulture) + " eventos" : "archivo sin eventos";
			newsPtr = 0;
		}

		// true si c esta dentro de [t-5, t+5) de algun evento. c crece de forma monotona.
		private bool InBlackout(DateTime c)
		{
			if (!newsOk) return false;
			while (newsPtr < news.Count && news[newsPtr].AddMinutes(5) <= c) newsPtr++;
			for (int j = newsPtr; j < news.Count; j++)
			{
				DateTime t = news[j];
				if (t.AddMinutes(-5) > c) break;
				if (t.AddMinutes(5) > c) return true;
			}
			return false;
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) return;
			DateTime c = Time[0];
			if (c > new DateTime(2022, 12, 31, 23, 59, 59)) return;   // el OOS no se toca
			if (!newsOk) return;                                       // sin calendario validado no se opera

			barsSeen++;
			if (barsSeen == 1) firstBar = c;
			lastBar = c;

			string day = c.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
			if (day != curDay)
			{
				curDay = day;
				tradesToday = 0;
				rangeHigh = double.MinValue;
				rangeLow = double.MaxValue;
			}
			if (excluded.Contains(day)) return;

			int stamp = c.Hour * 60 + c.Minute;
			bool inBlk = InBlackout(c);

			// rango de apertura 09:30-09:45 (barras con sello 09:31..09:45)
			if (stamp >= 571 && stamp <= 585)
			{
				if (High[0] > rangeHigh) rangeHigh = High[0];
				if (Low[0] < rangeLow) rangeLow = Low[0];
			}

			if (Position.MarketPosition != MarketPosition.Flat)
			{
				if (exitSubmittedBar != CurrentBar)
				{
					string why = null;
					if (stamp == FlatStamp) why = "TIME";
					else if (TimeExitStamp > 0 && stamp == TimeExitStamp) why = "TIME";
					else if (inBlk) why = "NEWS";
					if (why != null)
					{
						exitSubmittedBar = CurrentBar;
						if (Position.MarketPosition == MarketPosition.Long) ExitLong(1, why, "ENT");
						else ExitShort(1, why, "ENT");
					}
				}
				return;
			}

			if (entrySubmittedBar == CurrentBar) return;
			if (stamp < FirstEntryStamp || stamp > LastEntryStamp) return;
			if (inBlk) return;
			if (tradesToday >= MaxTradesPerDay) return;

			SigKSl = KSl;
			SigKTp = KTp;
			int s = Signal(stamp);
			if (s == 0) return;
			sigAtr = atr[0];
			sigStamp = stamp;
			sigTime = c;
			entrySubmittedBar = CurrentBar;
			tradesToday++;
			if (s > 0) EnterLong(1, "ENT"); else EnterShort(1, "ENT");
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			string nm = execution.Order.Name;
			if (nm == "ENT")
			{
				dir = marketPosition == MarketPosition.Long ? 1 : -1;
				entryPrice = price;
				entryTime = time;
				entryTime0 = Time[0];
				entryBar = CurrentBar;
				curStop = Instrument.MasterInstrument.RoundToTickSize(dir == 1 ? price - SigKSl * sigAtr : price + SigKSl * sigAtr);
				if (dir == 1) ExitLongStopMarket(0, true, quantity, curStop, "SL", "ENT");
				else ExitShortStopMarket(0, true, quantity, curStop, "SL", "ENT");
				curTp = double.NaN;
				if (SigKTp > 0)
				{
					curTp = Instrument.MasterInstrument.RoundToTickSize(dir == 1 ? price + SigKTp * sigAtr : price - SigKTp * sigAtr);
					if (dir == 1) ExitLongLimit(0, true, quantity, curTp, "TP", "ENT");
					else ExitShortLimit(0, true, quantity, curTp, "TP", "ENT");
				}
			}
			else if (nm == "SL" || nm == "TP" || nm == "TIME" || nm == "NEWS")
			{
				int n;
				exitCount.TryGetValue(nm, out n);
				exitCount[nm] = n + 1;
				string tpTxt = double.IsNaN(curTp) ? "" : curTp.ToString("0.00", CultureInfo.InvariantCulture);
				rows.Add(string.Join("\t", new string[] {
					"N" + RefId.ToString(CultureInfo.InvariantCulture),
					(rows.Count + 1).ToString(CultureInfo.InvariantCulture),
					dir.ToString(CultureInfo.InvariantCulture),
					sigTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
					entryTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
					entryTime0.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
					entryPrice.ToString("0.00", CultureInfo.InvariantCulture),
					time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
					Time[0].ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
					price.ToString("0.00", CultureInfo.InvariantCulture),
					nm,
					sigAtr.ToString("0.0000", CultureInfo.InvariantCulture),
					curStop.ToString("0.00", CultureInfo.InvariantCulture),
					tpTxt,
					entryBar.ToString(CultureInfo.InvariantCulture),
					CurrentBar.ToString(CultureInfo.InvariantCulture)
				}));
			}
		}

		private void WriteOutputs()
		{
			if (wroteOutputs || rows == null) return;
			wroteOutputs = true;
			StringBuilder sb = new StringBuilder();
			sb.Append("ref\tn\tdir\tsignalTime\tentryExecTime\tentryBarTime\tentryPrice\texitExecTime\texitBarTime\texitPrice\texitName\tatrSignal\tstopPrice\ttpPrice\tentryBar\texitBar\n");
			foreach (string r in rows) sb.Append(r).Append('\n');
			StringBuilder su = new StringBuilder();
			su.Append("NQC ET1A - SUMMARY\n");
			su.Append("script_version : " + ScriptVersion + "\n");
			su.Append("ref            : N" + RefId.ToString(CultureInfo.InvariantCulture) + "\n");
			su.Append("instrument     : " + Instrument.FullName + "\n");
			su.Append("bars_processed : " + barsSeen.ToString(CultureInfo.InvariantCulture) + "\n");
			su.Append("first_bar      : " + firstBar.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\n");
			su.Append("last_bar       : " + lastBar.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\n");
			su.Append("news           : " + newsMsg + " (sha esperado " + NewsSha256.Substring(0, 12) + ")\n");
			su.Append("excluded_days  : " + ExcludedList.Length.ToString(CultureInfo.InvariantCulture) + "\n");
			su.Append("trades         : " + rows.Count.ToString(CultureInfo.InvariantCulture) + "\n");
			foreach (KeyValuePair<string, int> kv in exitCount)
				su.Append("exit_" + kv.Key + "        : " + kv.Value.ToString(CultureInfo.InvariantCulture) + "\n");
			string tsv = sb.ToString();
			string hash;
			using (SHA256 sha = SHA256.Create())
			{
				StringBuilder h = new StringBuilder();
				foreach (byte b in sha.ComputeHash(Encoding.UTF8.GetBytes(tsv))) h.Append(b.ToString("x2"));
				hash = h.ToString();
			}
			su.Append("trades_sha256  : " + hash + "\n");
			string dirPath = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results",
				"ET1A_N" + RefId.ToString(CultureInfo.InvariantCulture) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
			Directory.CreateDirectory(dirPath);
			UTF8Encoding enc = new UTF8Encoding(false);
			File.WriteAllText(Path.Combine(dirPath, "TRADES_NATIVE.tsv"), tsv, enc);
			File.WriteAllText(Path.Combine(dirPath, "SUMMARY.txt"), su.ToString(), enc);
			Print("NQC_ET1A_END ref=N" + RefId.ToString(CultureInfo.InvariantCulture) + " trades=" + rows.Count.ToString(CultureInfo.InvariantCulture)
				+ " news=" + newsMsg + " sha=" + hash.Substring(0, 12) + " dir=" + dirPath);
		}
	}

	// N1: long a las 09:35, stop 1,5 ATR(14), objetivo 3 ATR(14)
	public class NQC_ET1A_N1 : NQC_NativeRefBase
	{
		protected override int RefId { get { return 1; } }
		protected override void Setup() { AtrPeriod = 14; KSl = 1.5; KTp = 3.0; }
		protected override int Signal(int stamp) { return stamp == 575 ? 1 : 0; }
	}

	// N2: short a las 09:40, stop 2 ATR(20), objetivo 4 ATR(20)
	public class NQC_ET1A_N2 : NQC_NativeRefBase
	{
		protected override int RefId { get { return 2; } }
		protected override void Setup() { AtrPeriod = 20; KSl = 2.0; KTp = 4.0; }
		protected override int Signal(int stamp) { return stamp == 580 ? -1 : 0; }
	}

	// N3: cruce EMA(5)/EMA(13), long, stop 2 ATR(14), sin objetivo, varias por dia
	public class NQC_ET1A_N3 : NQC_NativeRefBase
	{
		protected override int RefId { get { return 3; } }
		protected override void Setup() { AtrPeriod = 14; KSl = 2.0; KTp = -1; MaxTradesPerDay = 99; }
		protected override int Signal(int stamp) { return CrossAbove(ema1, ema2, 1) ? 1 : 0; }
	}

	// N4: ruptura del rango 09:30-09:45 (cierre sobre el maximo), long, stop 2 ATR(14), sin objetivo
	public class NQC_ET1A_N4 : NQC_NativeRefBase
	{
		protected override int RefId { get { return 4; } }
		protected override void Setup() { AtrPeriod = 14; KSl = 2.0; KTp = -1; }
		protected override int Signal(int stamp)
		{
			if (stamp <= 585 || rangeHigh == double.MinValue) return 0;
			return Close[0] > rangeHigh ? 1 : 0;
		}
	}

	// N5: long a las 09:35 con stop y objetivo muy cortos (0,3 ATR(5)): provoca ambos en la misma barra
	public class NQC_ET1A_N5 : NQC_NativeRefBase
	{
		protected override int RefId { get { return 5; } }
		protected override void Setup() { AtrPeriod = 5; KSl = 0.3; KTp = 0.3; }
		protected override int Signal(int stamp) { return stamp == 575 ? 1 : 0; }
	}

	// N6: long en el ultimo instante permitido (11:29:00), stop 3 ATR(14), sin objetivo: plano a las 11:30
	public class NQC_ET1A_N6 : NQC_NativeRefBase
	{
		protected override int RefId { get { return 6; } }
		protected override void Setup() { AtrPeriod = 14; KSl = 3.0; KTp = -1; }
		protected override int Signal(int stamp) { return stamp == 689 ? 1 : 0; }
	}

	// N7: long a las 09:40, stop 6 ATR(50), sin objetivo, salida por hora 10:30
	public class NQC_ET1A_N7 : NQC_NativeRefBase
	{
		protected override int RefId { get { return 7; } }
		protected override void Setup() { AtrPeriod = 50; KSl = 6.0; KTp = -1; TimeExitStamp = 630; }
		protected override int Signal(int stamp) { return stamp == 580 ? 1 : 0; }
	}

	// N8: dos miembros con prioridad. A (long, 1,5/3 ATR14) gana a B (short, 2,5/1 ATR14) si coinciden a las 09:35;
	// B tambien intenta a las 09:38 si esta plano.
	public class NQC_ET1A_N8 : NQC_NativeRefBase
	{
		protected override int RefId { get { return 8; } }
		protected override void Setup() { AtrPeriod = 14; KSl = 1.5; KTp = 3.0; MaxTradesPerDay = 2; }
		protected override int Signal(int stamp)
		{
			if (stamp == 575) { SigKSl = 1.5; SigKTp = 3.0; return 1; }      // miembro A
			if (stamp == 575 || stamp == 578) { SigKSl = 2.5; SigKTp = 1.0; return -1; }   // miembro B
			return 0;
		}
	}

	// N9: long a las 09:35, stop 1,5 ATR(14) y objetivo 0,5 ATR(14): fija la convencion de ordenes limite
	public class NQC_ET1A_N9 : NQC_NativeRefBase
	{
		protected override int RefId { get { return 9; } }
		protected override void Setup() { AtrPeriod = 14; KSl = 1.5; KTp = 0.5; }
		protected override int Signal(int stamp) { return stamp == 575 ? 1 : 0; }
	}
}
