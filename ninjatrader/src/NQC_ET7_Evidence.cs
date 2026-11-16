using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

//==============================================================================
// NQC_ET7_Evidence - Configuracion, puertas e informe ET7_EVIDENCE_REPORT.txt. Contrato NQC 2.0.0. SOLO DESARROLLO. Biblioteca PURA.
// Depende de NQC_ET1_Core.cs, NQC_ET1C_AccountSim.cs, NQC_ET7_Stats.cs, NQC_ET7_MonteCarlo.cs.
// Umbrales y parametros se LEEN de un texto clave=valor (ET7_CONFIG.txt); las claves ausentes toman el DEFECTO del contrato; claves desconocidas o valores
// ilegibles => error (falla cerrada). Un umbral mas laxo que el del contrato se marca en el informe como WEAKER_THAN_CONTRACT (no se bloquea: decide el orquestador).
// Claves: dsr_min(0,95) null_pct_min(95) mc_dd_p95_max(2000) mc_mode(EXPLORATORY|CONFIRMATORY) mc_paths_exploratory(500) mc_paths_confirmatory(5000) mc_block(8)
//   mc_seed(20261114) neff_cut(0,40) neff_coverage_min(1,0) neff_max_series(4000) null_min_n(20) ppass_boot_min(vacio = solo se reporta).
//   D-038 (muestra aleatoria uniforme): neff_method(COVERED|SAMPLE_CHAO1; defecto COVERED = D-036) neff_sample_cov_min(0,90: cobertura Good-Turing minima de la muestra)
//   neff_sample_min_n(1000) neff_sample_max_series(5000) neff_bound(UPPER|POINT|LOWER; defecto UPPER = limite SUPERIOR del IC95 de N_eff en el DSR, conservador: N_eff mayor => SR0 mayor => DSR menor).
//   En SAMPLE_CHAO1 se ignoran neff_coverage_min y neff_max_series. Todas las claves van en ET7_CONFIG.txt y quedan selladas por config_sha256.
// Puertas por portafolio: DSR >= dsr_min; percentil nulo >= null_pct_min; P95 DD <= mc_dd_p95_max. Cualquier NaN/entrada faltante => la puerta FALLA (cerrado).
// P_pass bootstrap se reporta; solo es puerta si ppass_boot_min esta fijado (decision de ET2).
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public sealed class NQCE7_Config
	{
		public double DsrMin = 0.95, NullPctMin = 95, McDdP95Max = 2000, NEffCut = 0.40, NEffCoverageMin = 1.0, PpassBootMin = double.NaN, NEffSampleCovMin = 0.90;
		public string NEffMethod = "COVERED", NEffBound = "UPPER"; public int NEffSampleMinN = 1000, NEffSampleMaxSeries = 5000;
		public string McMode = "EXPLORATORY"; public int PathsExploratory = 500, PathsConfirmatory = 5000, Block = 8, NEffMaxSeries = 4000, NullMinN = 20; public long Seed = 20261114;
		public string Text = "";
		public int Paths { get { return McMode == "CONFIRMATORY" ? PathsConfirmatory : PathsExploratory; } }
		public string Sha { get { return NQCE_Const.Sha256Text(Text); } }

		private static double D(string v) { return double.Parse(v.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture); }
		public static NQCE7_Config Parse(string text, out string error)
		{
			error = ""; NQCE7_Config c = new NQCE7_Config(); c.Text = text ?? "";
			foreach (string raw in c.Text.Split('\n'))
			{
				string line = raw.Trim(); if (line.Length == 0 || line[0] == '#') continue;
				int eq = line.IndexOf('='); if (eq <= 0) { error = "linea invalida: " + line; return null; }
				string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
				try
				{
					switch (k)
					{
						case "dsr_min": c.DsrMin = D(v); break;
						case "null_pct_min": c.NullPctMin = D(v); break;
						case "mc_dd_p95_max": c.McDdP95Max = D(v); break;
						case "mc_mode": v = v.ToUpperInvariant(); if (v != "EXPLORATORY" && v != "CONFIRMATORY") { error = "mc_mode invalido"; return null; } c.McMode = v; break;
						case "mc_paths_exploratory": c.PathsExploratory = int.Parse(v, CultureInfo.InvariantCulture); break;
						case "mc_paths_confirmatory": c.PathsConfirmatory = int.Parse(v, CultureInfo.InvariantCulture); break;
						case "mc_block": c.Block = int.Parse(v, CultureInfo.InvariantCulture); break;
						case "mc_seed": c.Seed = long.Parse(v, CultureInfo.InvariantCulture); break;
						case "neff_cut": c.NEffCut = D(v); break;
						case "neff_coverage_min": c.NEffCoverageMin = D(v); break;
						case "neff_max_series": c.NEffMaxSeries = int.Parse(v, CultureInfo.InvariantCulture); break;
						case "null_min_n": c.NullMinN = int.Parse(v, CultureInfo.InvariantCulture); break;
						case "neff_method": v = v.ToUpperInvariant(); if (v != "COVERED" && v != "SAMPLE_CHAO1") { error = "neff_method invalido"; return null; } c.NEffMethod = v; break;
						case "neff_bound": v = v.ToUpperInvariant(); if (v != "UPPER" && v != "POINT" && v != "LOWER") { error = "neff_bound invalido"; return null; } c.NEffBound = v; break;
						case "neff_sample_cov_min": c.NEffSampleCovMin = D(v); break;
						case "neff_sample_min_n": c.NEffSampleMinN = int.Parse(v, CultureInfo.InvariantCulture); break;
						case "neff_sample_max_series": c.NEffSampleMaxSeries = int.Parse(v, CultureInfo.InvariantCulture); break;
						case "ppass_boot_min": c.PpassBootMin = v.Length == 0 ? double.NaN : D(v); break;
						default: error = "clave desconocida: " + k; return null;
					}
				}
				catch (FormatException) { error = "valor ilegible en " + k; return null; }
				catch (OverflowException) { error = "valor fuera de rango en " + k; return null; }
			}
			return c;
		}
		public List<string> Weaker()
		{
			List<string> w = new List<string>();
			if (DsrMin < 0.95) w.Add("dsr_min<0,95"); if (NullPctMin < 95) w.Add("null_pct_min<95"); if (McDdP95Max > 2000) w.Add("mc_dd_p95_max>2000");
			if (NEffMethod == "SAMPLE_CHAO1" && NEffBound != "UPPER") w.Add("neff_bound!=UPPER"); if (Block != 8) w.Add("mc_block!=8"); if (PathsExploratory < 500) w.Add("mc_paths_exploratory<500"); if (PathsConfirmatory < 5000) w.Add("mc_paths_confirmatory<5000");
			return w;
		}
	}

	public sealed class NQCE7_CandidateInput
	{
		public string Id; public double[] DailyPnl; public List<NQCE7_TradeRec> Trades; public double Fitness; public int Sessions;
	}

	public sealed class NQCE7_Verdict
	{
		public string Id; public bool Pass; public List<string> Causes = new List<string>();
		public NQCE7_DsrResult Dsr; public double Pct = double.NaN; public NQCE7_McResult Mc;
		public string Gate(string name, bool ok, string detail) { if (!ok) Causes.Add(name + ": " + detail); return ok ? "PASA" : "RECHAZA"; }
	}

	public static class NQCE7_Evidence
	{
		private static string F(double v, string f) { return double.IsNaN(v) ? "NaN" : v.ToString(f, CultureInfo.InvariantCulture); }

		// N_eff segun la politica declarada. COVERED: D-036 (grupos / cobertura). SAMPLE_CHAO1: D-038, series = MUESTRA ALEATORIA UNIFORME de los ensayos evaluados y
		// nGross = N de la poblacion evaluada; N_eff = limite (UPPER|POINT|LOWER) del IC95 de Chao1; falla cerrada si la muestra es insuficiente.
		public static NQCE7_NEffResult ComputeNEff(NQCE7_Config cfg, double[][] series, long nGross)
		{
			if (cfg.NEffMethod != "SAMPLE_CHAO1")
			{
				if (nGross > int.MaxValue) { NQCE7_NEffResult X = new NQCE7_NEffResult(); X.NEff = double.NaN; X.SrVar = double.NaN; X.Fail = "N bruto excede int"; return X; }
				return NQCE7_NEff.Compute(series, (int)nGross, cfg.NEffCut, cfg.NEffCoverageMin, cfg.NEffMaxSeries);
			}
			NQCE7_NEffResult R = new NQCE7_NEffResult(); R.Method = "SAMPLE_CHAO1"; R.NEff = double.NaN; R.SrVar = double.NaN; R.Cut = cfg.NEffCut;
			R.NGross = (int)Math.Min(nGross, int.MaxValue); R.NSeries = series == null ? 0 : series.Length;
			if (series != null && series.Length > cfg.NEffSampleMaxSeries) { R.Fail = "muestra (" + series.Length.ToString(CultureInfo.InvariantCulture) + ") supera neff_sample_max_series (" + cfg.NEffSampleMaxSeries.ToString(CultureInfo.InvariantCulture) + ")"; return R; }
			if (series != null && series.Length < cfg.NEffSampleMinN) { R.Fail = "muestra (" + series.Length.ToString(CultureInfo.InvariantCulture) + ") menor que neff_sample_min_n (" + cfg.NEffSampleMinN.ToString(CultureInfo.InvariantCulture) + ")"; return R; }
			NQCE7_NeffResult S = NQCE7_Neff.EstimateFromSample(series, nGross, cfg.NEffCut); R.Sample = S;
			R.Clusters = S.GruposObservados; R.Label = S.Label; R.SrVar = S.SrVar; R.Coverage = nGross > 0 ? Math.Min(1.0, (double)R.NSeries / nGross) : 0;
			if (S.Falla.Length > 0) { R.Fail = S.Falla; return R; }
			if (S.CoberturaMuestra < cfg.NEffSampleCovMin) { R.Fail = "cobertura Good-Turing de la muestra " + S.CoberturaMuestra.ToString("0.0000", CultureInfo.InvariantCulture) + " < minimo " + cfg.NEffSampleCovMin.ToString("0.0000", CultureInfo.InvariantCulture); return R; }
			R.NEff = Math.Max(1.0, cfg.NEffBound == "UPPER" ? S.Superior : (cfg.NEffBound == "LOWER" ? S.Inferior : S.NEffEstimado));
			return R;
		}

		public static NQCE7_Verdict Evaluate(NQCE7_CandidateInput c, NQCE7_NEffResult ne, double[] nullBest, NQCE7_Config cfg, NQC_AccountProfile profile)
		{
			NQCE7_Verdict V = new NQCE7_Verdict(); V.Id = c.Id;
			V.Dsr = NQCE7_Dsr.Compute(c.DailyPnl, ne.NEff, ne.SrVar);
			bool ok = !double.IsNaN(V.Dsr.Dsr) && V.Dsr.Dsr >= cfg.DsrMin;
			string d = ne.Fail.Length > 0 ? "N_eff: " + ne.Fail : (V.Dsr.Fail.Length > 0 ? V.Dsr.Fail : "DSR " + F(V.Dsr.Dsr, "0.0000") + " < " + F(cfg.DsrMin, "0.00"));
			V.Gate("DSR", ok, d);
			string pf; V.Pct = NQCE7_NullPct.Percentile(c.Fitness, nullBest, cfg.NullMinN, out pf);
			ok = !double.IsNaN(V.Pct) && V.Pct >= cfg.NullPctMin;
			V.Gate("PERCENTIL_NULO", ok, pf.Length > 0 ? pf : "percentil " + F(V.Pct, "0.0") + " < " + F(cfg.NullPctMin, "0.0"));
			V.Mc = NQCE7_MonteCarlo.Run(c.Trades, c.Sessions, cfg.Paths, cfg.Block, cfg.Seed, profile);
			ok = !double.IsNaN(V.Mc.DdP95) && V.Mc.DdP95 <= cfg.McDdP95Max;
			V.Gate("MC_DD_P95", ok, V.Mc.Fail.Length > 0 ? V.Mc.Fail : "P95 DD " + F(V.Mc.DdP95, "0.00") + " > " + F(cfg.McDdP95Max, "0.00"));
			if (!double.IsNaN(cfg.PpassBootMin))
			{ ok = !double.IsNaN(V.Mc.PpassBoot) && V.Mc.PpassBoot >= cfg.PpassBootMin; V.Gate("PPASS_BOOT", ok, "P_pass bootstrap " + F(V.Mc.PpassBoot, "0.0000") + " < " + F(cfg.PpassBootMin, "0.0000")); }
			V.Pass = V.Causes.Count == 0;
			return V;
		}

		public static string Report(NQCE7_Config cfg, NQCE7_NEffResult ne, double[] nullBest, List<NQCE7_Verdict> vs, string commit, string specHashes)
		{
			StringBuilder b = new StringBuilder();
			b.Append("ET7_EVIDENCE_REPORT (NQC 2.0.0)\n");
			b.Append("config_sha256=").Append(cfg.Sha).Append("\ncommit=").Append(commit).Append('\n');
			if (specHashes != null && specHashes.Length > 0) b.Append(specHashes).Append('\n');
			b.Append("umbrales: dsr_min=").Append(F(cfg.DsrMin, "0.####")).Append(" null_pct_min=").Append(F(cfg.NullPctMin, "0.####")).Append(" mc_dd_p95_max=").Append(F(cfg.McDdP95Max, "0.##"))
			 .Append(" mc_mode=").Append(cfg.McMode).Append(" trayectorias=").Append(cfg.Paths.ToString(CultureInfo.InvariantCulture)).Append(" bloque=").Append(cfg.Block.ToString(CultureInfo.InvariantCulture))
			 .Append(" semilla=").Append(cfg.Seed.ToString(CultureInfo.InvariantCulture)).Append(" ppass_boot_min=").Append(double.IsNaN(cfg.PpassBootMin) ? "solo_reporta" : F(cfg.PpassBootMin, "0.####")).Append('\n');
			foreach (string w in cfg.Weaker()) b.Append("ADVERTENCIA WEAKER_THAN_CONTRACT ").Append(w).Append('\n');
			b.Append("N_bruto=").Append(ne.NGross.ToString(CultureInfo.InvariantCulture)).Append(" series=").Append(ne.NSeries.ToString(CultureInfo.InvariantCulture))
			 .Append(" grupos=").Append(ne.Clusters.ToString(CultureInfo.InvariantCulture)).Append(" cobertura=").Append(F(ne.Coverage, "0.0000")).Append(" corte_distancia=").Append(F(ne.Cut, "0.####"))
			 .Append(" N_eff=").Append(F(ne.NEff, "0.##")).Append(" var_SR_diario=").Append(F(ne.SrVar, "0.000000")).Append(ne.Fail.Length > 0 ? " FALLA_CERRADA=" + ne.Fail : "").Append('\n');
			if (ne.Method == "SAMPLE_CHAO1" && ne.Sample != null)
			{
				NQCE7_NeffResult s = ne.Sample;
				b.Append("N_eff_muestra: metodo=SAMPLE_CHAO1 N_poblacion=").Append(s.NPoblacion.ToString(CultureInfo.InvariantCulture)).Append(" n_muestra=").Append(s.NMuestra.ToString(CultureInfo.InvariantCulture))
				 .Append(" observados=").Append(s.GruposObservados.ToString(CultureInfo.InvariantCulture)).Append(" f1=").Append(s.F1.ToString(CultureInfo.InvariantCulture)).Append(" f2=").Append(s.F2.ToString(CultureInfo.InvariantCulture))
				 .Append(" N_eff_estimado(Chao1)=").Append(F(s.NEffEstimado, "0.##")).Append(" IC95=[").Append(F(s.Inferior, "0.##")).Append(",").Append(F(s.Superior, "0.##"))
				 .Append("] cobertura_GoodTuring=").Append(F(s.CoberturaMuestra, "0.0000")).Append(" cobertura_min=").Append(F(cfg.NEffSampleCovMin, "0.####")).Append(" limite_usado=").Append(cfg.NEffBound)
				 .Append(" N_eff_usado=").Append(F(ne.NEff, "0.##")).Append(" (Chao1 es limite inferior de la riqueza verdadera)\n");
			}
			b.Append("nulo_ET2_n=").Append((nullBest == null ? 0 : nullBest.Length).ToString(CultureInfo.InvariantCulture)).Append('\n');
			int pass = 0;
			foreach (NQCE7_Verdict v in vs)
			{
				if (v.Pass) pass++;
				b.Append("--- portafolio ").Append(v.Id).Append(" : ").Append(v.Pass ? "PASA A ET8" : "RECHAZADO").Append('\n');
				b.Append("  DSR=").Append(F(v.Dsr.Dsr, "0.0000")).Append(" SR_diario=").Append(F(v.Dsr.Sr, "0.0000")).Append(" SR_anual(252)=").Append(F(v.Dsr.SrAnnual, "0.00")).Append(" SR0=").Append(F(v.Dsr.Sr0, "0.0000"))
				 .Append(" asimetria=").Append(F(v.Dsr.Skew, "0.000")).Append(" curtosis=").Append(F(v.Dsr.Kurt, "0.000")).Append(" T=").Append(v.Dsr.T.ToString(CultureInfo.InvariantCulture)).Append('\n');
				b.Append("  percentil_nulo=").Append(F(v.Pct, "0.0")).Append('\n');
				b.Append("  MC: trayectorias=").Append(v.Mc.Paths.ToString(CultureInfo.InvariantCulture)).Append(" ops=").Append(v.Mc.Trades.ToString(CultureInfo.InvariantCulture)).Append(" DD_P95=").Append(F(v.Mc.DdP95, "0.00"))
				 .Append(" DD_mediana=").Append(F(v.Mc.DdMedian, "0.00")).Append(" DD_max=").Append(F(v.Mc.DdMax, "0.00")).Append(" P_pass_bootstrap=").Append(F(v.Mc.PpassBoot, "0.0000"))
				 .Append(" (trayectorias con decididos=").Append(v.Mc.PpassPaths.ToString(CultureInfo.InvariantCulture)).Append(")\n");
				foreach (string c in v.Causes) b.Append("  causa_rechazo ").Append(c).Append('\n');
			}
			b.Append("RESUMEN: ").Append(pass.ToString(CultureInfo.InvariantCulture)).Append(" de ").Append(vs.Count.ToString(CultureInfo.InvariantCulture)).Append(" pasan. Estado: evidencia calculada por el codigo; solo vale con datos reales de ET6/ET2.\n");
			return b.ToString();
		}
	}
}
