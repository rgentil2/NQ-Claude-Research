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
#endregion

//==============================================================================
// NQC_ET0_DataManifest  -  ET0 Datos y entorno  -  contrato NQC 1.0.0
//
// Estrategia SIN ordenes. Se ejecuta en Strategy Analyzer sobre NQ M1 (Last),
// de 2020-01-01 a 2026-10-31, y escribe un manifiesto de integridad.
// NO registra precios ni calcula metricas economicas (solo conteos y volumen
// para comprobar la zona horaria).
//
// Ventana operativa (hora de NinjaTrader, debe ser Eastern):
//   barras con sello 09:31 .. 11:30 inclusive = 120 barras M1
//   (la barra con sello 09:31 cubre 09:30:00 - 09:31:00).
//
// Salidas en <Documentos>\NinjaTrader 8\NQC_Results\ET0_<fecha_hora>\ :
//   DAYS.tsv, MONTHS.tsv, SUMMARY.txt (con SHA256 de DAYS y MONTHS)
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public class NQC_ET0_DataManifest : Strategy
	{
		private const string ScriptVersion      = "ET0-1.0.0";
		private const string ContractVersion    = "NQC-1.0.0";
		private const string DeclaredMergePolicy = "MergeBackAdjusted (declarado por el usuario; NT8 no lo expone a la estrategia)";

		private const int WinFirstStamp = 9 * 60 + 31;
		private const int WinLastStamp  = 11 * 60 + 30;
		private const int WinBars       = WinLastStamp - WinFirstStamp + 1;   // 120
		private const int TzFirstStamp  = 9 * 60 + 16;
		private const int TzLastStamp   = 10 * 60;

		private const double GateCompleteness  = 0.99;
		private const double GateTzHit         = 0.70;
		private const int    WarnHolidaysYear  = 12;

		private class DayStat
		{
			public bool[] Seen = new bool[WinBars];
			public int    NWindow;
			public double TzVolMax = -1;
			public int    TzVolMaxStamp = -1;
		}

		private class MonthStat
		{
			public int Sessions, Complete, Incomplete, SinVentana, TzHit, TzDen;
		}

		private SortedDictionary<DateTime, DayStat> windowDays;
		private HashSet<DateTime> datesSeen;
		private long     totalBars;
		private long     badSeconds;
		private DateTime firstBarTime;
		private DateTime lastBarTime;
		private bool     wroteOutputs;

		private string metaInstrument   = "N/D";
		private string metaPeriodType   = "N/D";
		private int    metaPeriodValue  = -1;
		private string metaMarketData   = "N/D";
		private string metaTradingHours = "N/D";
		private string metaThTz         = "N/D";
		private string metaPcTz         = "N/D";
		private string metaNtTz         = "N/D";

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description               = "ET0 NQC 1.0.0 - Manifiesto de datos M1 de NQ (sin ordenes, sin metricas economicas).";
				Name                      = "NQC_ET0_DataManifest";
				Calculate                 = Calculate.OnBarClose;
				BarsRequiredToTrade       = 0;
				IsExitOnSessionCloseStrategy = false;
				TraceOrders               = false;
			}
			else if (State == State.Configure)
			{
				windowDays = new SortedDictionary<DateTime, DayStat>();
				datesSeen  = new HashSet<DateTime>();
			}
			else if (State == State.DataLoaded)
			{
				CaptureMeta();
			}
			else if (State == State.Terminated)
			{
				WriteOutputs();
			}
		}

		private void CaptureMeta()
		{
			try { metaInstrument = Instrument.FullName; } catch { metaInstrument = "N/D"; }
			try
			{
				metaPeriodType  = BarsPeriod.BarsPeriodType.ToString();
				metaPeriodValue = BarsPeriod.Value;
				metaMarketData  = BarsPeriod.MarketDataType.ToString();
			}
			catch { }
			try
			{
				metaTradingHours = Bars.TradingHours.Name;
				metaThTz         = Bars.TradingHours.TimeZoneInfo.Id;
			}
			catch { }
			try { metaPcTz = System.TimeZoneInfo.Local.Id; } catch { }
			metaNtTz = TryGetNtTimeZone();
		}

		private static string TryGetNtTimeZone()
		{
			try
			{
				System.Reflection.PropertyInfo pGo = typeof(NinjaTrader.Core.Globals).GetProperty("GeneralOptions",
					System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
				if (pGo == null) return "N/D (propiedad no encontrada)";
				object go = pGo.GetValue(null, null);
				if (go == null) return "N/D";
				System.Reflection.PropertyInfo pTz = go.GetType().GetProperty("TimeZoneInfo");
				if (pTz == null) return "N/D (propiedad no encontrada)";
				object tz = pTz.GetValue(go, null);
				System.TimeZoneInfo z = tz as System.TimeZoneInfo;
				return z != null ? z.Id : (tz != null ? tz.ToString() : "N/D");
			}
			catch { return "N/D (no accesible)"; }
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) return;

			DateTime t = Time[0];
			totalBars++;
			if (totalBars == 1) firstBarTime = t;
			lastBarTime = t;
			if (t.Second != 0 || t.Millisecond != 0) badSeconds++;

			DateTime d = t.Date;
			datesSeen.Add(d);
			int stamp = t.Hour * 60 + t.Minute;

			bool inWin = stamp >= WinFirstStamp && stamp <= WinLastStamp;
			bool inTz  = stamp >= TzFirstStamp  && stamp <= TzLastStamp;
			if (!inWin && !inTz) return;

			DayStat ds;
			if (!windowDays.TryGetValue(d, out ds))
			{
				ds = new DayStat();
				windowDays[d] = ds;
			}

			if (inWin)
			{
				int idx = stamp - WinFirstStamp;
				if (!ds.Seen[idx])
				{
					ds.Seen[idx] = true;
					ds.NWindow++;
				}
			}
			if (inTz)
			{
				double v = Volume[0];
				if (v > ds.TzVolMax)
				{
					ds.TzVolMax = v;
					ds.TzVolMaxStamp = stamp;
				}
			}
		}

		private static string BlockOf(DateTime d)
		{
			if (d >= new DateTime(2020, 1, 1) && d <= new DateTime(2022, 12, 31)) return "IS";
			if (d >= new DateTime(2023, 1, 1) && d <= new DateTime(2024, 12, 31)) return "OOS1";
			if (d >= new DateTime(2025, 1, 1) && d <= new DateTime(2026, 10, 31)) return "OOS2";
			return "OTRO";
		}

		private static string StampToString(int s)
		{
			return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", s / 60, s % 60);
		}

		private static string Sha256Hex(string s)
		{
			using (SHA256 sha = SHA256.Create())
			{
				byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
				StringBuilder sb = new StringBuilder();
				foreach (byte b in h) sb.Append(b.ToString("x2"));
				return sb.ToString();
			}
		}

		private static string Pct(int num, int den)
		{
			if (den <= 0) return "n/a";
			return (100.0 * num / den).ToString("0.00", CultureInfo.InvariantCulture) + "%";
		}

		private void WriteOutputs()
		{
			if (wroteOutputs) return;
			if (datesSeen == null || datesSeen.Count == 0) return;
			wroteOutputs = true;

			List<DateTime> dates = new List<DateTime>(datesSeen);
			dates.Sort();

			StringBuilder sbDays = new StringBuilder();
			sbDays.Append("date\tdow\tblock\tclass\tnWindow\tmissing\ttzArgmaxStamp\n");

			SortedDictionary<string, MonthStat> months = new SortedDictionary<string, MonthStat>();
			Dictionary<string, MonthStat> blocks = new Dictionary<string, MonthStat>();
			SortedDictionary<int, int> holidaysByYear = new SortedDictionary<int, int>();

			foreach (DateTime d in dates)
			{
				if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) continue;

				DayStat ds;
				windowDays.TryGetValue(d, out ds);

				string cls;
				int nWin = 0;
				if (ds == null || ds.NWindow == 0) cls = "SIN_VENTANA";
				else
				{
					nWin = ds.NWindow;
					cls = (nWin == WinBars) ? "COMPLETA" : "INCOMPLETA";
				}
				string tzStr = (ds != null && ds.TzVolMaxStamp >= 0) ? StampToString(ds.TzVolMaxStamp) : "-";
				string blk = BlockOf(d);
				string mk = d.ToString("yyyy-MM", CultureInfo.InvariantCulture);

				MonthStat ms;
				if (!months.TryGetValue(mk, out ms)) { ms = new MonthStat(); months[mk] = ms; }
				MonthStat bs;
				if (!blocks.TryGetValue(blk, out bs)) { bs = new MonthStat(); blocks[blk] = bs; }

				if (cls == "COMPLETA")
				{
					ms.Sessions++; ms.Complete++; bs.Sessions++; bs.Complete++;
				}
				else if (cls == "INCOMPLETA")
				{
					ms.Sessions++; ms.Incomplete++; bs.Sessions++; bs.Incomplete++;
				}
				else
				{
					ms.SinVentana++; bs.SinVentana++;
					int c;
					holidaysByYear.TryGetValue(d.Year, out c);
					holidaysByYear[d.Year] = c + 1;
				}

				if (nWin > 0 && ds.TzVolMaxStamp >= 0)
				{
					ms.TzDen++; bs.TzDen++;
					if (ds.TzVolMaxStamp == WinFirstStamp || ds.TzVolMaxStamp == WinFirstStamp + 1)
					{
						ms.TzHit++; bs.TzHit++;
					}
				}

				sbDays.Append(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\t')
					.Append(d.DayOfWeek.ToString().Substring(0, 3)).Append('\t')
					.Append(blk).Append('\t')
					.Append(cls).Append('\t')
					.Append(nWin.ToString(CultureInfo.InvariantCulture)).Append('\t')
					.Append((cls == "SIN_VENTANA" ? 0 : WinBars - nWin).ToString(CultureInfo.InvariantCulture)).Append('\t')
					.Append(tzStr).Append('\n');
			}

			StringBuilder sbMonths = new StringBuilder();
			sbMonths.Append("month\tsessions\tcomplete\tincomplete\tsinVentana\ttzHit\ttzDen\n");
			foreach (KeyValuePair<string, MonthStat> kv in months)
			{
				MonthStat m = kv.Value;
				sbMonths.Append(kv.Key).Append('\t')
					.Append(m.Sessions).Append('\t').Append(m.Complete).Append('\t')
					.Append(m.Incomplete).Append('\t').Append(m.SinVentana).Append('\t')
					.Append(m.TzHit).Append('\t').Append(m.TzDen).Append('\n');
			}

			string daysText   = sbDays.ToString();
			string monthsText = sbMonths.ToString();
			string daysHash   = Sha256Hex(daysText);
			string monthsHash = Sha256Hex(monthsText);

			MonthStat isb;
			if (!blocks.TryGetValue("IS", out isb)) isb = new MonthStat();
			double comp = isb.Sessions > 0 ? (double)isb.Complete / isb.Sessions : 0.0;
			double tzr  = isb.TzDen > 0 ? (double)isb.TzHit / isb.TzDen : 0.0;

			bool g1 = metaInstrument.StartsWith("NQ", StringComparison.OrdinalIgnoreCase);
			bool g2 = metaPeriodType == "Minute" && metaPeriodValue == 1;
			bool g3 = comp >= GateCompleteness;
			bool g4 = tzr >= GateTzHit;
			bool g5 = badSeconds == 0;
			bool g0 = firstBarTime <= new DateTime(2020, 1, 7) && lastBarTime >= new DateTime(2026, 10, 26);
			bool holidaysOk = true;
			foreach (KeyValuePair<int, int> kv in holidaysByYear) { if (kv.Value > WarnHolidaysYear) holidaysOk = false; }
			bool all = g0 && g1 && g2 && g3 && g4 && g5;
			string status = all ? "PASS_PENDIENTE_REPRODUCIBILIDAD" : "FAIL";

			StringBuilder sb = new StringBuilder();
			sb.AppendLine("NQC ET0 - SUMMARY");
			sb.AppendLine("script_version      : " + ScriptVersion);
			sb.AppendLine("contract_version    : " + ContractVersion);
			sb.AppendLine("status              : " + status + "   (G6 reproducibilidad: comparar hashes de 2 corridas)");
			sb.AppendLine();
			sb.AppendLine("[Entorno]");
			sb.AppendLine("instrument          : " + metaInstrument);
			sb.AppendLine("barsPeriod          : " + metaPeriodType + " " + metaPeriodValue.ToString(CultureInfo.InvariantCulture) + " / " + metaMarketData);
			sb.AppendLine("tradingHours        : " + metaTradingHours + "  (tz plantilla: " + metaThTz + ")");
			sb.AppendLine("tz_NinjaTrader      : " + metaNtTz);
			sb.AppendLine("tz_PC               : " + metaPcTz);
			sb.AppendLine("merge_policy        : " + DeclaredMergePolicy);
			sb.AppendLine("primera_barra       : " + firstBarTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
			sb.AppendLine("ultima_barra        : " + lastBarTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
			sb.AppendLine("barras_totales      : " + totalBars.ToString(CultureInfo.InvariantCulture));
			sb.AppendLine("ventana             : sellos 09:31-11:30 (120 barras M1) = 09:30-11:30 ET");
			sb.AppendLine();
			sb.AppendLine("[Bloques]  sesiones = dias habiles con alguna barra en la ventana");
			string[] order = new string[] { "IS", "OOS1", "OOS2", "OTRO" };
			foreach (string b in order)
			{
				MonthStat m;
				if (!blocks.TryGetValue(b, out m)) continue;
				sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
					"{0,-5}: sesiones={1} completas={2} ({3}) incompletas={4} sin_ventana_habil={5} tz_hit={6}",
					b, m.Sessions, m.Complete, Pct(m.Complete, m.Sessions), m.Incomplete, m.SinVentana, Pct(m.TzHit, m.TzDen)));
			}
			sb.AppendLine();
			sb.AppendLine("[Dias habiles sin ventana por ano]  (esperables: festivos, ~9-12 por ano)");
			foreach (KeyValuePair<int, int> kv in holidaysByYear)
				sb.AppendLine(kv.Key.ToString(CultureInfo.InvariantCulture) + " : " + kv.Value.ToString(CultureInfo.InvariantCulture));
			sb.AppendLine();
			sb.AppendLine("[Gates ET0]");
			sb.AppendLine("G0 cobertura 2020-01..2026-10          : " + (g0 ? "PASS" : "FAIL"));
			sb.AppendLine("G1 instrumento NQ                      : " + (g1 ? "PASS" : "FAIL"));
			sb.AppendLine("G2 periodo M1                          : " + (g2 ? "PASS" : "FAIL"));
			sb.AppendLine("G3 completitud IS >= 99% (" + Pct(isb.Complete, isb.Sessions) + ")       : " + (g3 ? "PASS" : "FAIL"));
			sb.AppendLine("G4 zona horaria (pico 09:31) IS >= 70% (" + Pct(isb.TzHit, isb.TzDen) + ") : " + (g4 ? "PASS" : "FAIL"));
			sb.AppendLine("G5 barras con segundos = 0             : " + (g5 ? "PASS" : "FAIL") + " (anomalas=" + badSeconds.ToString(CultureInfo.InvariantCulture) + ")");
			sb.AppendLine("Aviso festivos por ano <= " + WarnHolidaysYear.ToString(CultureInfo.InvariantCulture) + "          : " + (holidaysOk ? "OK" : "REVISAR"));
			sb.AppendLine();
			sb.AppendLine("[Hashes]");
			sb.AppendLine("DAYS_SHA256         : " + daysHash);
			sb.AppendLine("MONTHS_SHA256       : " + monthsHash);

			string dir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results",
				"ET0_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
			Directory.CreateDirectory(dir);
			UTF8Encoding enc = new UTF8Encoding(false);
			File.WriteAllText(Path.Combine(dir, "DAYS.tsv"), daysText, enc);
			File.WriteAllText(Path.Combine(dir, "MONTHS.tsv"), monthsText, enc);
			File.WriteAllText(Path.Combine(dir, "SUMMARY.txt"), sb.ToString(), enc);

			Print("NQC_ET0_END status=" + status + " sessionsIS=" + isb.Sessions.ToString(CultureInfo.InvariantCulture)
				+ " completeIS=" + Pct(isb.Complete, isb.Sessions) + " tzHitIS=" + Pct(isb.TzHit, isb.TzDen)
				+ " daysSha=" + daysHash.Substring(0, 12) + " dir=" + dir);
		}
	}
}
