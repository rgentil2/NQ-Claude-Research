#region Using declarations
using System;
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
// NQC_ET1A_BarsExport  -  ET1 (parte A2): exporta las barras M1 de la ventana del IS
// junto con los valores de los indicadores de NT8, para replicar OFFLINE el libro de
// operaciones de las referencias nativas y fijar las convenciones de fills.
//
// Estrategia SIN ordenes. Strategy Analyzer: NQ M1 Last, Merge back adjusted,
// 2020-01-01 a 2022-12-31. Las barras posteriores a 2022-12-31 se ignoran.
// Exporta solo barras con sello 09:29 a 11:32 de dias habiles (ventana + contexto).
// Salida: <Documentos>\NinjaTrader 8\NQC_Results\ET1A_BARS_<fecha_hora>\
//   BARS_WINDOW.csv, SUMMARY.txt
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public class NQC_ET1A_BarsExport : Strategy
	{
		private const string ScriptVersion = "ET1A-BARS-1.0.0";
		private ATR atr5, atr14, atr20, atr50;
		private EMA ema5, ema13;
		private StringBuilder sb;
		private long rows;
		private long barsSeen;
		private DateTime firstBar, lastBar;
		private bool wrote;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "ET1A exporta barras de la ventana y valores de indicadores NT8 (sin ordenes).";
				Name = "NQC_ET1A_BarsExport";
				Calculate = Calculate.OnBarClose;
				BarsRequiredToTrade = 0;
				IsExitOnSessionCloseStrategy = false;
				TraceOrders = false;
			}
			else if (State == State.DataLoaded)
			{
				atr5 = ATR(5);
				atr14 = ATR(14);
				atr20 = ATR(20);
				atr50 = ATR(50);
				ema5 = EMA(5);
				ema13 = EMA(13);
				sb = new StringBuilder();
				sb.Append("time,open,high,low,close,atr5,atr14,atr20,atr50,ema5,ema13,ema5_prev,ema13_prev\n");
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
			if (c.DayOfWeek == DayOfWeek.Saturday || c.DayOfWeek == DayOfWeek.Sunday) return;
			int stamp = c.Hour * 60 + c.Minute;
			if (stamp < 569 || stamp > 692) return;     // 09:29 .. 11:32
			if (CurrentBar < 1) return;

			sb.Append(c.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(',')
			  .Append(Open[0].ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
			  .Append(High[0].ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
			  .Append(Low[0].ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
			  .Append(Close[0].ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
			  .Append(atr5[0].ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
			  .Append(atr14[0].ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
			  .Append(atr20[0].ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
			  .Append(atr50[0].ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
			  .Append(ema5[0].ToString("0.0000", CultureInfo.InvariantCulture)).Append(',')
			  .Append(ema13[0].ToString("0.0000", CultureInfo.InvariantCulture)).Append(',')
			  .Append(ema5[1].ToString("0.0000", CultureInfo.InvariantCulture)).Append(',')
			  .Append(ema13[1].ToString("0.0000", CultureInfo.InvariantCulture)).Append('\n');
			rows++;
		}

		private void WriteOutputs()
		{
			if (wrote || sb == null) return;
			wrote = true;
			string csv = sb.ToString();
			string hash;
			using (SHA256 sha = SHA256.Create())
			{
				StringBuilder h = new StringBuilder();
				foreach (byte b in sha.ComputeHash(Encoding.UTF8.GetBytes(csv))) h.Append(b.ToString("x2"));
				hash = h.ToString();
			}
			StringBuilder su = new StringBuilder();
			su.Append("NQC ET1A BARS - SUMMARY\n");
			su.Append("script_version : " + ScriptVersion + "\n");
			su.Append("instrument     : " + Instrument.FullName + "\n");
			su.Append("bars_processed : " + barsSeen.ToString(CultureInfo.InvariantCulture) + "\n");
			su.Append("first_bar      : " + firstBar.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\n");
			su.Append("last_bar       : " + lastBar.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\n");
			su.Append("rows_exported  : " + rows.ToString(CultureInfo.InvariantCulture) + "\n");
			su.Append("csv_sha256     : " + hash + "\n");
			string dir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results",
				"ET1A_BARS_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
			Directory.CreateDirectory(dir);
			UTF8Encoding enc = new UTF8Encoding(false);
			File.WriteAllText(Path.Combine(dir, "BARS_WINDOW.csv"), csv, enc);
			File.WriteAllText(Path.Combine(dir, "SUMMARY.txt"), su.ToString(), enc);
			Print("NQC_ET1A_BARS_END rows=" + rows.ToString(CultureInfo.InvariantCulture) + " sha=" + hash.Substring(0, 12) + " dir=" + dir);
		}
	}
}
