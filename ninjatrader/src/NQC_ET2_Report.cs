#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
#endregion

//==============================================================================
// NQC_ET2_Report - Estrategia de NT8 SIN ORDENES que genera ET2_CALIBRATION_REPORT.txt a partir de la carpeta de un lote de NQC_RUN_Batch.
// Toda la logica vive en NQCET2_ReportBuilder (NQC_ET2_ReportBuilder.cs, pura, tambien ejecutable fuera de NT8: ver ninjatrader/runbooks/ET2_REPORT_CLI.cs).
// No usa barras ni ordenes: solo lee archivos. SOLO DESARROLLO hasta correrla en NT8 sobre el lote real y guardar el log + hash.
// Configuracion: NQC_Results\ET2_REPORT_CONFIG.txt (clave=valor, # comentarios):
//   batch_dir=NQC_Results\RUNBATCH_ET2_FASE_A     (relativa a NQC_Results o absoluta; obligatoria)
//   label=  commit=  phase=FASE_A_REDUCED  expected_null=20  expected_inj=10  max_fp=0.05  min_power=0.70  ppass_floor=0.50
// Salida: NQC_Results\ET2REPORT_<lote>_<fecha>\ET2_CALIBRATION_REPORT.txt, ET2_REPORT_INPUTS.txt (sha256 de cada archivo leido) y SUMMARY.txt.
// Requiere NQC_ET2_Calibration.cs y NQC_ET2_ReportBuilder.cs.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public class NQC_ET2_Report : Strategy
	{
		private const string ScriptVersion = "ET2REPORTRUN-1.0.0";
		private bool started, done;
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults) { Description = "Genera ET2_CALIBRATION_REPORT.txt desde un lote de NQC_RUN_Batch (sin ordenes, solo lee archivos)."; Name = "NQC_ET2_Report"; Calculate = Calculate.OnBarClose; BarsRequiredToTrade = 0; IsExitOnSessionCloseStrategy = false; TraceOrders = false; }
			else if (State == State.DataLoaded) started = true;
			else if (State == State.Terminated)
			{
				if (!started || done) return; done = true;
				try { RunAll(); }
				catch (Exception ex)
				{
					Print("NQC_ET2_REPORT_ERROR " + ex.Message);
					try { string d = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results"); Directory.CreateDirectory(d); File.WriteAllText(Path.Combine(d, "ET2_REPORT_ERROR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt"), ex.ToString()); } catch { }
				}
			}
		}
		protected override void OnBarUpdate() { }

		public static NQCET2_ReportParams ParseConfig(string text, out string batchDir, out string error)
		{
			batchDir = ""; error = ""; NQCET2_ReportParams p = new NQCET2_ReportParams(); CultureInfo ic = CultureInfo.InvariantCulture;
			foreach (string raw in text.Split('\n'))
			{
				string line = raw.Trim(); if (line.Length == 0 || line[0] == '#') continue; int e = line.IndexOf('='); if (e <= 0) { error = "linea sin '=': " + line; return null; }
				string k = line.Substring(0, e).Trim().ToLowerInvariant(), v = line.Substring(e + 1).Trim(); double d; int n;
				switch (k)
				{
					case "batch_dir": batchDir = v; break; case "label": p.Label = v; break; case "commit": p.Commit = v; break; case "phase": p.Phase = v; break;
					case "expected_null": if (!int.TryParse(v, NumberStyles.Integer, ic, out n)) { error = "expected_null ilegible"; return null; } p.ExpectedNull = n; break;
					case "expected_inj": if (!int.TryParse(v, NumberStyles.Integer, ic, out n)) { error = "expected_inj ilegible"; return null; } p.ExpectedInj = n; break;
					case "max_fp": if (!double.TryParse(v, NumberStyles.Float, ic, out d)) { error = "max_fp ilegible"; return null; } p.MaxFp = d; break;
					case "min_power": if (!double.TryParse(v, NumberStyles.Float, ic, out d)) { error = "min_power ilegible"; return null; } p.MinPower = d; break;
					case "ppass_floor": if (!double.TryParse(v, NumberStyles.Float, ic, out d)) { error = "ppass_floor ilegible"; return null; } p.PpassFloor = d; break;
					default: error = "clave desconocida: " + k; return null;
				}
			}
			if (batchDir.Length == 0) { error = "falta batch_dir"; return null; }
			return p;
		}

		private void RunAll()
		{
			string res = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results"); Directory.CreateDirectory(res);
			string cfgPath = Path.Combine(res, "ET2_REPORT_CONFIG.txt");
			if (!File.Exists(cfgPath)) { Print("NQC_ET2_REPORT falta " + cfgPath); File.WriteAllText(Path.Combine(res, "ET2_REPORT_ERROR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt"), "falta " + cfgPath); return; }
			string bd, err; NQCET2_ReportParams p = ParseConfig(File.ReadAllText(cfgPath), out bd, out err);
			if (p == null) { Print("NQC_ET2_REPORT config invalida: " + err); File.WriteAllText(Path.Combine(res, "ET2_REPORT_ERROR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt"), "config invalida: " + err); return; }
			string dir = Path.IsPathRooted(bd) ? bd : Path.Combine(NinjaTrader.Core.Globals.UserDataDir, bd);
			if (!Directory.Exists(dir)) dir = Path.Combine(res, bd);
			p.GeneratedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
			NQCET2_ReportResult r = NQCET2_ReportBuilder.Build(dir, p);
			UTF8Encoding enc = new UTF8Encoding(false);
			string outDir = Path.Combine(res, "ET2REPORT_" + Path.GetFileName(dir.TrimEnd('\\', '/')) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)); Directory.CreateDirectory(outDir);
			if (!r.Ok) { File.WriteAllText(Path.Combine(outDir, "SUMMARY.txt"), "NQC ET2 REPORT - FALLO LIMPIO\nscript_version : " + ScriptVersion + "\nbatch_dir : " + dir + "\nerror : " + r.Error + "\n", enc); Print("NQC_ET2_REPORT fallo limpio: " + r.Error); return; }
			File.WriteAllText(Path.Combine(outDir, "ET2_CALIBRATION_REPORT.txt"), r.Text, enc);
			StringBuilder sb = new StringBuilder("inputs_manifest_sha256=" + r.InputsSha + "\nreport_sha256=" + r.ReportSha + "\n"); foreach (string[] f in r.Inputs) sb.Append(f[2]).Append("  ").Append(f[1]).Append("  ").Append(f[0]).Append('\n');
			File.WriteAllText(Path.Combine(outDir, "ET2_REPORT_INPUTS.txt"), sb.ToString(), enc);
			File.WriteAllText(Path.Combine(outDir, "SUMMARY.txt"), "NQC ET2 REPORT - SUMMARY\nscript_version : " + ScriptVersion + "\nbuilder : " + NQCET2_ReportBuilder.Version + "\ncontract : NQC 2.0.0\nbatch_dir : " + dir + "\nveredicto_mecanico : " + r.Verdict + "\nnulas : " + r.NNull + "\ninyectadas : " + r.NInj + "\nexcluidas : " + r.Excluded + "\nreport_sha256 : " + r.ReportSha + "\ninputs_manifest_sha256 : " + r.InputsSha + "\n", enc);
			Print("NQC_ET2_REPORT_END veredicto=" + r.Verdict + " report_sha256=" + r.ReportSha + " dir=" + outDir);
		}
	}
}
