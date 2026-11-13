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
// NQC_ET1A_GapProbe  -  ET1 (parte A3): sonda de saltos sobre el stop
// Entra en 46 momentos elegidos fuera de linea, en los que la barra SIGUIENTE a la de
// entrada abre mas alla del stop (calculado como k x ATR(14) desde el precio de entrada).
// Sirve para saber a que precio ejecuta NT8 un stop cuando la barra abre mas alla de el.
// Sin calendario de noticias ni dias excluidos: son casos puntuales. Sus resultados
// economicos NO se miran.
//
// Strategy Analyzer: NQ M1 Last, Merge back adjusted, 2020-01-01 a 2022-12-31,
// Slippage 0 y comision ninguna. Las barras posteriores a 2022-12-31 se ignoran.
// Salida: <Documentos>\NinjaTrader 8\NQC_Results\ET1A_GAP_<fecha_hora>\
//   TRADES_NATIVE.tsv, SUMMARY.txt
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public class NQC_ET1A_GapProbe : Strategy
	{
		private const string ScriptVersion = "ET1A-GAP-1.0.0";
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

		private Dictionary<DateTime, KeyValuePair<int, double>> probes;
		private ATR atr14;
		private int entrySubmittedBar = -1;
		private int exitSubmittedBar = -1;
		private double sigAtr, sigK, curStop;
		private DateTime sigTime, entryTime, entryTime0;
		private double entryPrice;
		private int dir, entryBar;
		private List<string> rows;
		private bool wrote;
		private long barsSeen;
		private DateTime firstBar, lastBar;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "ET1A sonda de saltos sobre el stop";
				Name = "NQC_ET1A_GapProbe";
				Calculate = Calculate.OnBarClose;
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = false;
				BarsRequiredToTrade = 0;
				TraceOrders = false;
			}
			else if (State == State.Configure)
			{
				probes = new Dictionary<DateTime, KeyValuePair<int, double>>();
				foreach (string p in Probes)
				{
					string[] f = p.Split(',');
					DateTime t = DateTime.ParseExact(f[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
					probes[t] = new KeyValuePair<int, double>(int.Parse(f[1], CultureInfo.InvariantCulture), double.Parse(f[2], CultureInfo.InvariantCulture));
				}
				rows = new List<string>();
			}
			else if (State == State.DataLoaded)
			{
				atr14 = ATR(14);
			}
			else if (State == State.Terminated)
			{
				WriteOutputs();
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) return;
			DateTime c = Time[0];
			if (c > new DateTime(2022, 12, 31, 23, 59, 59)) return;
			barsSeen++;
			if (barsSeen == 1) firstBar = c;
			lastBar = c;
			int stamp = c.Hour * 60 + c.Minute;

			if (Position.MarketPosition != MarketPosition.Flat)
			{
				if (stamp == 690 && exitSubmittedBar != CurrentBar)
				{
					exitSubmittedBar = CurrentBar;
					if (Position.MarketPosition == MarketPosition.Long) ExitLong(1, "TIME", "ENT");
					else ExitShort(1, "TIME", "ENT");
				}
				return;
			}
			if (entrySubmittedBar == CurrentBar) return;
			KeyValuePair<int, double> kv;
			if (!probes.TryGetValue(c, out kv)) return;
			dir = kv.Key;
			sigK = kv.Value;
			sigAtr = atr14[0];
			sigTime = c;
			entrySubmittedBar = CurrentBar;
			if (dir > 0) EnterLong(1, "ENT"); else EnterShort(1, "ENT");
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			string nm = execution.Order.Name;
			if (nm == "ENT")
			{
				entryPrice = price;
				entryTime = time;
				entryTime0 = Time[0];
				entryBar = CurrentBar;
				curStop = Instrument.MasterInstrument.RoundToTickSize(dir == 1 ? price - sigK * sigAtr : price + sigK * sigAtr);
				if (dir == 1) ExitLongStopMarket(0, true, quantity, curStop, "SL", "ENT");
				else ExitShortStopMarket(0, true, quantity, curStop, "SL", "ENT");
			}
			else if (nm == "SL" || nm == "TIME")
			{
				rows.Add(string.Join("\t", new string[] {
					"G", (rows.Count + 1).ToString(CultureInfo.InvariantCulture), dir.ToString(CultureInfo.InvariantCulture),
					sigTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
					entryTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
					entryTime0.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
					entryPrice.ToString("0.00", CultureInfo.InvariantCulture),
					time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
					Time[0].ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
					price.ToString("0.00", CultureInfo.InvariantCulture),
					nm, sigAtr.ToString("0.0000", CultureInfo.InvariantCulture),
					curStop.ToString("0.00", CultureInfo.InvariantCulture),
					sigK.ToString("0.0", CultureInfo.InvariantCulture),
					entryBar.ToString(CultureInfo.InvariantCulture), CurrentBar.ToString(CultureInfo.InvariantCulture)
				}));
			}
		}

		private void WriteOutputs()
		{
			if (wrote || rows == null) return;
			wrote = true;
			StringBuilder sb = new StringBuilder();
			sb.Append("ref\tn\tdir\tsignalTime\tentryExecTime\tentryBarTime\tentryPrice\texitExecTime\texitBarTime\texitPrice\texitName\tatrSignal\tstopPrice\tk\tentryBar\texitBar\n");
			foreach (string r in rows) sb.Append(r).Append('\n');
			string tsv = sb.ToString();
			string hash;
			using (SHA256 sha = SHA256.Create())
			{
				StringBuilder h = new StringBuilder();
				foreach (byte b in sha.ComputeHash(Encoding.UTF8.GetBytes(tsv))) h.Append(b.ToString("x2"));
				hash = h.ToString();
			}
			StringBuilder su = new StringBuilder();
			su.Append("NQC ET1A GAP - SUMMARY\n");
			su.Append("script_version : " + ScriptVersion + "\n");
			su.Append("instrument     : " + Instrument.FullName + "\n");
			su.Append("bars_processed : " + barsSeen.ToString(CultureInfo.InvariantCulture) + "\n");
			su.Append("first_bar      : " + firstBar.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\n");
			su.Append("last_bar       : " + lastBar.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\n");
			su.Append("probes         : " + Probes.Length.ToString(CultureInfo.InvariantCulture) + "\n");
			su.Append("trades         : " + rows.Count.ToString(CultureInfo.InvariantCulture) + "\n");
			su.Append("trades_sha256  : " + hash + "\n");
			string dirPath = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results",
				"ET1A_GAP_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
			Directory.CreateDirectory(dirPath);
			UTF8Encoding enc = new UTF8Encoding(false);
			File.WriteAllText(Path.Combine(dirPath, "TRADES_NATIVE.tsv"), tsv, enc);
			File.WriteAllText(Path.Combine(dirPath, "SUMMARY.txt"), su.ToString(), enc);
			Print("NQC_ET1A_GAP_END trades=" + rows.Count.ToString(CultureInfo.InvariantCulture) + " sha=" + hash.Substring(0, 12) + " dir=" + dirPath);
		}
	}
}
