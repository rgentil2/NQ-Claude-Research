// ET2_REPORT_CLI.cs - utilidad de linea de comandos del generador del informe de calibracion de ET2 (sin NinjaTrader).
// Compilar (mono): mcs -out:et2report.exe ninjatrader/src/NQC_ET2_Calibration.cs ninjatrader/src/NQC_ET2_ReportBuilder.cs ninjatrader/runbooks/ET2_REPORT_CLI.cs
// Uso: mono et2report.exe <carpeta_del_lote> [salida.txt] [clave=valor ...]
//   claves: label= commit= phase= generated_utc= expected_null= expected_inj= max_fp= min_power= ppass_floor=
// Codigo de salida: 0 informe generado; 2 uso; 3 fallo limpio (carpeta invalida/incompleta). Escribe salida.txt (por defecto ET2_CALIBRATION_REPORT.txt en el cwd) y ET2_REPORT_INPUTS.txt al lado.
using System;
using System.Globalization;
using System.IO;
using System.Text;
using NinjaTrader.NinjaScript.Strategies;

public static class ET2ReportCli
{
	public static int Main(string[] args)
	{
		if (args.Length < 1) { Console.Error.WriteLine("uso: et2report.exe <carpeta_del_lote> [salida.txt] [clave=valor ...]"); return 2; }
		NQCET2_ReportParams p = new NQCET2_ReportParams(); string outPath = "ET2_CALIBRATION_REPORT.txt";
		for (int i = 1; i < args.Length; i++)
		{
			int e = args[i].IndexOf('=');
			if (e < 0) { outPath = args[i]; continue; }
			string k = args[i].Substring(0, e), v = args[i].Substring(e + 1); double d; int n;
			switch (k)
			{
				case "label": p.Label = v; break; case "commit": p.Commit = v; break; case "phase": p.Phase = v; break; case "generated_utc": p.GeneratedUtc = v; break;
				case "expected_null": if (int.TryParse(v, out n)) p.ExpectedNull = n; break; case "expected_inj": if (int.TryParse(v, out n)) p.ExpectedInj = n; break;
				case "max_fp": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) p.MaxFp = d; break;
				case "min_power": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) p.MinPower = d; break;
				case "ppass_floor": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) p.PpassFloor = d; break;
				default: Console.Error.WriteLine("clave desconocida: " + k); return 2;
			}
		}
		NQCET2_ReportResult r = NQCET2_ReportBuilder.Build(args[0], p);
		if (!r.Ok) { Console.Error.WriteLine("ET2 REPORT FALLO LIMPIO: " + r.Error); return 3; }
		File.WriteAllText(outPath, r.Text, new UTF8Encoding(false));
		StringBuilder sb = new StringBuilder(); foreach (string[] f in r.Inputs) sb.Append(f[2]).Append("  ").Append(f[1]).Append("  ").Append(f[0]).Append('\n');
		File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outPath)), "ET2_REPORT_INPUTS.txt"), "inputs_manifest_sha256=" + r.InputsSha + "\nreport_sha256=" + r.ReportSha + "\n" + sb.ToString(), new UTF8Encoding(false));
		Console.WriteLine("ok report_sha256=" + r.ReportSha + " inputs_sha256=" + r.InputsSha + " veredicto=" + r.Verdict + " nulas=" + r.NNull + " inyectadas=" + r.NInj + " excluidas=" + r.Excluded);
		return 0;
	}
}
