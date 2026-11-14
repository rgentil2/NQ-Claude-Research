using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;

//==============================================================================
// NQC_ET7_EvidenceTest - Prueba TECNICA SINTETICA de NQC_ET7_* (N_eff, DSR, percentil, Monte Carlo, congelado, informe). Sin ordenes ni datos de mercado;
// NO reporta resultados economicos. Valores de referencia calculados APARTE (Python: statistics.NormalDist y una copia del generador splitmix64 del NQCE_Rng).
// Requiere NQC_ET1_Core, NQC_ET1C_AccountSim, NQC_ET7_Stats, NQC_ET7_MonteCarlo, NQC_ET7_Freeze, NQC_ET7_Evidence.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public class NQC_ET7_EvidenceTest : Strategy
	{
		private const string ScriptVersion = "ET7EVIDENCETEST-1.0.0";
		private bool started, done; private StringBuilder rep = new StringBuilder(); private int pass, fail;
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults) { Name = "NQC_ET7_EvidenceTest"; Calculate = Calculate.OnBarClose; BarsRequiredToTrade = 0; IsExitOnSessionCloseStrategy = false; TraceOrders = false; }
			else if (State == State.DataLoaded) started = true;
			else if (State == State.Terminated) { if (!started || done) return; done = true; RunAll(); }
		}
		protected override void OnBarUpdate() { }
		private static string F(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }
		private void Chk(string name, bool ok, string info) { if (ok) pass++; else fail++; rep.Append(ok ? "[PASS] " : "[FALLA] ").Append(name).Append(" | ").Append(info).Append("\n"); }
		private static bool Near(double a, double b, double tol) { return Math.Abs(a - b) <= tol; }

		static double[] Orth(int T, double rho, bool x)  // x = patron +1,-1 ; z = +1,+1,-1,-1 ; y = rho*x + sqrt(1-rho^2)*z
		{
			double[] r = new double[T];
			for (int t = 0; t < T; t++) { double xv = (t % 2 == 0) ? 1 : -1, zv = ((t / 2) % 2 == 0) ? 1 : -1; r[t] = x ? xv : rho * xv + Math.Sqrt(1 - rho * rho) * zv; }
			return r;
		}
		static double[] Rand(int T, long seed) { NQCE_Rng g = new NQCE_Rng(seed); double[] r = new double[T]; for (int t = 0; t < T; t++) r[t] = g.NextDouble() - 0.5; return r; }

		private void RunAll()
		{
			// ---- N1 normal
			Chk("N1 Cdf vs referencia", Near(NQCE7_Norm.Cdf(1.96), 0.9750021048517796, 1e-13) && Near(NQCE7_Norm.Cdf(-1), 0.15865525393145707, 1e-13) && Near(NQCE7_Norm.Cdf(3.5), 0.9997673709209645, 1e-13) && Near(NQCE7_Norm.Cdf(0.3), 0.6179114221889526, 1e-13) && Math.Abs(NQCE7_Norm.Cdf(-6) / 9.865876449133282e-10 - 1) < 1e-9, "");
			Chk("N2 InvCdf vs referencia", Near(NQCE7_Norm.InvCdf(0.95), 1.6448536269514715, 1e-12) && Near(NQCE7_Norm.InvCdf(0.975), 1.9599639845400536, 1e-12) && Near(NQCE7_Norm.InvCdf(0.99), 2.3263478740408408, 1e-12) && Near(NQCE7_Norm.InvCdf(0.999999), 4.753424308817089, 1e-9) && Near(NQCE7_Norm.InvCdf(1e-6), -4.753424308822899, 1e-9) && NQCE7_Norm.InvCdf(0.5) == 0.0, "");
			// ---- N_eff
			double[][] ident = new double[5][]; for (int i = 0; i < 5; i++) ident[i] = Orth(400, 0, true);
			NQCE7_NEffResult a = NQCE7_NEff.Compute(ident, 5, 0.40, 1.0, 4000);
			Chk("E1 series identicas -> N_eff = 1", a.Clusters == 1 && a.NEff == 1.0 && a.NGross == 5, "grupos=" + a.Clusters);
			double[][] indep = new double[40][]; for (int i = 0; i < 40; i++) indep[i] = Rand(500, 1000 + i);
			NQCE7_NEffResult b = NQCE7_NEff.Compute(indep, 40, 0.40, 1.0, 4000);
			Chk("E2 series independientes -> N_eff ~ N (=40)", b.Clusters == 40 && b.NEff == 40 && b.SrVar > 0, "grupos=" + b.Clusters);
			double[][] pair = new double[][] { Orth(400, 0, true), Orth(400, 0.8, false) };           // rho = 0,8 -> d = 0,3162
			double[][] pair5 = new double[][] { Orth(400, 0, true), Orth(400, 0.5, false) };          // rho = 0,5 -> d = 0,5
			Chk("E3 rho 0,8 se funde con corte 0,40 (d=0,316); rho 0,5 no (d=0,5)", NQCE7_NEff.Compute(pair, 2, 0.40, 1.0, 4000).Clusters == 1 && NQCE7_NEff.Compute(pair5, 2, 0.40, 1.0, 4000).Clusters == 2, "");
			Chk("E4 el corte decide: 0,30 -> 2 grupos; 0,32 -> 1 grupo (d=0,3162)", NQCE7_NEff.Compute(pair, 2, 0.30, 1.0, 4000).Clusters == 2 && NQCE7_NEff.Compute(pair, 2, 0.32, 1.0, 4000).Clusters == 1, "");
			double[] neg = new double[400]; double[] o = Orth(400, 0, true); for (int t = 0; t < 400; t++) neg[t] = -o[t];
			Chk("E5 rho = -1 -> d = 1, no se funde", NQCE7_NEff.Compute(new double[][] { o, neg }, 2, 0.40, 1.0, 4000).Clusters == 2 && Near(NQCE7_NEff.Dist(-1), 1, 1e-15) && Near(NQCE7_NEff.Dist(0), Math.Sqrt(0.5), 1e-15) && NQCE7_NEff.Dist(1) == 0, "");
			NQCE7_NEffResult cv = NQCE7_NEff.Compute(new double[][] { Orth(400, 0, true), Orth(400, 0.5, false), Orth(400, 0, true) }, 6, 0.40, 0.4, 4000);
			Chk("E6 cobertura 0,5: grupos 2 -> N_eff = 4; cobertura < minimo -> falla cerrada", cv.Clusters == 2 && Near(cv.NEff, 4, 1e-12) && NQCE7_NEff.Compute(new double[][] { o, o }, 6, 0.40, 0.9, 4000).Fail.Length > 0 && double.IsNaN(NQCE7_NEff.Compute(new double[][] { o, o }, 6, 0.40, 0.9, 4000).NEff), "");
			double[] flat = new double[400];
			Chk("E7 serie de varianza cero = grupo propio; MaxSeries y longitud distinta fallan cerrado", NQCE7_NEff.Compute(new double[][] { flat, flat }, 2, 0.40, 1.0, 4000).Clusters == 2 && NQCE7_NEff.Compute(indep, 40, 0.40, 1.0, 10).Fail.Length > 0 && NQCE7_NEff.Compute(new double[][] { o, new double[10] }, 2, 0.40, 1.0, 4000).Fail.Length > 0, "");
			// ---- DSR
			Chk("D1 SR0 esperado vs referencia", Near(NQCE7_Dsr.ExpectedMaxSr(0.0004, 50), 0.04552606186840696, 1e-12) && Near(NQCE7_Dsr.ExpectedMaxSr(0.0004, 2), 0.010395106885611878, 1e-12) && Near(NQCE7_Dsr.ExpectedMaxSr(0.0001, 500000), 0.047289513337031006, 1e-9) && NQCE7_Dsr.ExpectedMaxSr(0.0004, 1) == 0, "");
			double[] x = new double[400]; for (int t = 0; t < 400; t++) x[t] = ((t * 37) % 101 - 48) * 1.0 + (t % 7 == 0 ? 3.0 : 0.0) + (t % 2 == 0 ? 0.8 : 0.0);
			NQCE7_DsrResult d1 = NQCE7_Dsr.Compute(x, 50, 0.0004), d2 = NQCE7_Dsr.Compute(x, 500000, 0.0001);
			Chk("D2 momentos (SR, asimetria, curtosis) vs referencia", Near(d1.Sr, 0.09388712140753325, 1e-12) && Near(d1.Skew, 0.004015407040784575, 1e-12) && Near(d1.Kurt, 1.7978510726935788, 1e-12) && Near(d1.SrAnnual, d1.Sr * Math.Sqrt(252), 1e-12), "SR=" + F(d1.Sr));
			Chk("D3 DSR vs referencia (N_eff 50 y 500.000)", Near(d1.Dsr, 0.8328139694917917, 1e-10) && Near(d2.Dsr, 0.8238518229181542, 1e-10), F(d1.Dsr) + " / " + F(d2.Dsr));
			Chk("D4 DSR baja con N_eff; falla cerrada sin N_eff/varianza/datos", NQCE7_Dsr.Compute(x, 5, 0.0004).Dsr > d1.Dsr && double.IsNaN(NQCE7_Dsr.Compute(x, double.NaN, 0.0004).Dsr) && double.IsNaN(NQCE7_Dsr.Compute(x, 50, double.NaN).Dsr) && double.IsNaN(NQCE7_Dsr.Compute(new double[10], 50, 0.0004).Dsr) && double.IsNaN(NQCE7_Dsr.Compute(new double[100], 50, 0.0004).Dsr), "");
			// ---- percentil
			string pf; double[] nul = new double[20]; for (int i = 0; i < 20; i++) nul[i] = i;
			Chk("P1 percentil: 9,5 -> 50; empate no suma (10 -> 50); 19,5 -> 100; nulo corto/NaN falla", Near(NQCE7_NullPct.Percentile(9.5, nul, 20, out pf), 50, 1e-12) && Near(NQCE7_NullPct.Percentile(10, nul, 20, out pf), 50, 1e-12) && Near(NQCE7_NullPct.Percentile(19.5, nul, 20, out pf), 100, 1e-12) && double.IsNaN(NQCE7_NullPct.Percentile(5, nul, 21, out pf)) && pf.Length > 0 && double.IsNaN(NQCE7_NullPct.Percentile(double.NaN, nul, 20, out pf)), "");
			// ---- Monte Carlo
			List<NQCE7_TradeRec> tr = new List<NQCE7_TradeRec>();
			for (int i = 0; i < 40; i++) { double net = (((i * 13) % 17) - 6) * 10.0; tr.Add(new NQCE7_TradeRec(i / 2, net, Math.Min(0.0, net - 15))); }
			NQCE7_McResult m1 = NQCE7_MonteCarlo.Run(tr, 20, 7, 8, 12345, NQC_AccountProfile.RapidIntraday50K());
			double[] refDd = { 155, 95, 125, 95, 95, 95, 85 }; bool same = m1.Dd != null && m1.Dd.Length == 7; for (int i = 0; same && i < 7; i++) if (m1.Dd[i] != refDd[i]) same = false;
			Chk("M1 DD por trayectoria con semilla 12345 = referencia independiente", same, m1.Dd == null ? m1.Fail : string.Join(",", Array.ConvertAll(m1.Dd, delegate(double v) { return F(v); })));
			Chk("M2 P95 = cuantil ceil(0,95*7)=7 -> maximo (155)", m1.DdP95 == 155 && m1.DdMax == 155, F(m1.DdP95));
			NQCE7_McResult m2 = NQCE7_MonteCarlo.Run(tr, 20, 7, 8, 12345, NQC_AccountProfile.RapidIntraday50K()), m3 = NQCE7_MonteCarlo.Run(tr, 20, 7, 8, 12346, NQC_AccountProfile.RapidIntraday50K());
			Chk("M3 determinismo (misma semilla igual; otra semilla distinta)", F(m1.DdMean) == F(m2.DdMean) && F(m1.PpassBoot) == F(m2.PpassBoot) && F(m1.DdMean) != F(m3.DdMean), "");
			List<NQCE7_TradeRec> win = new List<NQCE7_TradeRec>(); for (int i = 0; i < 60; i++) win.Add(new NQCE7_TradeRec(i, 100, 0));
			NQCE7_McResult m4 = NQCE7_MonteCarlo.Run(win, 60, 50, 8, 1, NQC_AccountProfile.RapidIntraday50K());
			Chk("M4 todo ganador: DD 0 y P_pass 1 (a mano: objetivo en 30 sesiones; arranques 0..30 deciden PASS, el resto censurado)", m4.DdP95 == 0 && m4.PpassBoot == 1.0 && m4.PpassPaths == 50, F(m4.PpassBoot));
			List<NQCE7_TradeRec> los = new List<NQCE7_TradeRec>(); for (int i = 0; i < 60; i++) los.Add(new NQCE7_TradeRec(i, -100, -100));
			NQCE7_McResult m5 = NQCE7_MonteCarlo.Run(los, 60, 50, 8, 1, NQC_AccountProfile.RapidIntraday50K());
			Chk("M5 todo perdedor: DD = 6000 y P_pass 0", m5.DdP95 == 6000 && m5.PpassBoot == 0.0, F(m5.DdP95));
			Chk("M6 pocas operaciones o calendario invalido -> falla cerrada", double.IsNaN(NQCE7_MonteCarlo.Run(tr.GetRange(0, 10), 20, 7, 8, 1, NQC_AccountProfile.RapidIntraday50K()).DdP95) && double.IsNaN(NQCE7_MonteCarlo.Run(tr, 10, 7, 8, 1, NQC_AccountProfile.RapidIntraday50K()).DdP95), "");
			// ---- congelado
			string dir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results", "ET7TEST_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
			UTF8Encoding enc = new UTF8Encoding(false);
			string spec = Path.Combine(dir, "PORTFOLIO_SPEC.json"); File.WriteAllText(spec, "{\"schema\":\"NQC_PORTFOLIO_SPEC/1\",\"members\":[1,2,3]}\n", enc);
			List<string> eng = new List<string>(); foreach (string nm in NQCE7_Freeze.MinEngineFiles) { string p = Path.Combine(dir, nm); File.WriteAllText(p, "// motor " + nm + "\n", enc); eng.Add(p); }
			string commit = "0123456789abcdef0123456789abcdef01234567";
			string fz = NQCE7_Freeze.Build(spec, eng, commit, "2026-11-14T00:00:00Z"); string fz2 = NQCE7_Freeze.Build(spec, eng, commit, "2026-11-14T00:00:00Z");
			Chk("F1 congelado determinista y contiene spec, motor, commit y sello", fz == fz2 && fz.Contains("spec_sha256=") && fz.Contains("engine.NQC_ET1_Core.cs=") && fz.Contains("commit=" + commit) && fz.Contains("seal_sha256="), "");
			Chk("F2 verificacion OK con todo igual", NQCE7_Freeze.Verify(fz, spec, eng, commit).Ok, string.Join(";", NQCE7_Freeze.Verify(fz, spec, eng, commit).Errors));
			string before = NQCE7_Freeze.Sha256File(spec); byte[] sb = File.ReadAllBytes(spec); sb[10] ^= 1; File.WriteAllBytes(spec, sb);
			Chk("F3 cambiar UN byte del spec cambia el hash y la verificacion falla", NQCE7_Freeze.Sha256File(spec) != before && !NQCE7_Freeze.Verify(fz, spec, eng, commit).Ok, ""); sb[10] ^= 1; File.WriteAllBytes(spec, sb);
			string c0 = eng[0]; byte[] eb = File.ReadAllBytes(c0); eb[3] ^= 1; File.WriteAllBytes(c0, eb);
			Chk("F4 cambiar un byte del motor falla", !NQCE7_Freeze.Verify(fz, spec, eng, commit).Ok, ""); eb[3] ^= 1; File.WriteAllBytes(c0, eb);
			Chk("F5 commit distinto falla", !NQCE7_Freeze.Verify(fz, spec, eng, "ffffffffffffffffffffffffffffffffffffffff").Ok && !NQCE7_Freeze.Verify(fz, spec, eng, "zz").Ok, "");
			Chk("F6 archivo congelado alterado (sello roto) falla", !NQCE7_Freeze.Verify(fz.Replace("2026-11-14", "2026-11-15"), spec, eng, commit).Ok, "");
			List<string> fewer = new List<string>(eng); fewer.RemoveAt(2); List<string> more = new List<string>(eng); string extra = Path.Combine(dir, "Otro.cs"); File.WriteAllText(extra, "x", enc); more.Add(extra);
			Chk("F7 motor entregado de menos o de mas falla", !NQCE7_Freeze.Verify(fz, spec, fewer, commit).Ok && !NQCE7_Freeze.Verify(fz, spec, more, commit).Ok, "");
			Chk("F8 fallas cerradas: texto vacio, spec ausente, basura", !NQCE7_Freeze.Verify("", spec, eng, commit).Ok && !NQCE7_Freeze.Verify(fz, Path.Combine(dir, "no.json"), eng, commit).Ok && !NQCE7_Freeze.Verify("basura\n", spec, eng, commit).Ok && !NQCE7_Freeze.Verify(null, spec, eng, commit).Ok, "");
			bool thr = false; try { NQCE7_Freeze.Build(spec, fewer, commit, "t"); } catch (ArgumentException) { thr = true; } bool thr2 = false; try { NQCE7_Freeze.Build(spec, eng, "xyz", "t"); } catch (ArgumentException) { thr2 = true; }
			Chk("F9 Build exige motor completo y commit hexadecimal", thr && thr2, "");
			// ---- configuracion e informe
			string err; NQCE7_Config cfg = NQCE7_Config.Parse("", out err);
			Chk("C1 defectos del contrato (DSR 0,95; pct 95; DD 2000; 500/5000; bloque 8)", cfg != null && cfg.DsrMin == 0.95 && cfg.NullPctMin == 95 && cfg.McDdP95Max == 2000 && cfg.PathsExploratory == 500 && cfg.PathsConfirmatory == 5000 && cfg.Block == 8 && cfg.Paths == 500 && cfg.Weaker().Count == 0, "");
			Chk("C2 configuracion: lee valores, modo confirmatorio = 5000, marca laxitud; clave desconocida/valor ilegible = error", NQCE7_Config.Parse("mc_mode=confirmatory\ndsr_min=0,90\n", out err).Paths == 5000 && NQCE7_Config.Parse("dsr_min=0,90", out err).Weaker().Count == 1 && NQCE7_Config.Parse("foo=1", out err) == null && NQCE7_Config.Parse("dsr_min=abc", out err) == null && NQCE7_Config.Parse("mc_mode=X", out err) == null, "");
			NQCE7_Config cf = NQCE7_Config.Parse("mc_paths_exploratory=200\nmc_seed=7\nnull_min_n=20\n", out err);
			double[] daily = new double[400]; for (int t = 0; t < 400; t++) daily[t] = 10.0 + (((t * 37) % 101) - 50) * 1.0;   // SR alto
			NQCE7_NEffResult ne = NQCE7_NEff.Compute(indep, 40, 0.40, 1.0, 4000);
			NQCE7_CandidateInput good = new NQCE7_CandidateInput(); good.Id = "P_BUENO"; good.DailyPnl = daily; good.Trades = win; good.Sessions = 60; good.Fitness = 100;
			NQCE7_CandidateInput badDd = new NQCE7_CandidateInput(); badDd.Id = "P_DD"; badDd.DailyPnl = daily; badDd.Trades = los; badDd.Sessions = 60; badDd.Fitness = 100;
			NQCE7_CandidateInput badP = new NQCE7_CandidateInput(); badP.Id = "P_PCT"; badP.DailyPnl = daily; badP.Trades = win; badP.Sessions = 60; badP.Fitness = 5;
			NQCE7_CandidateInput badS = new NQCE7_CandidateInput(); badS.Id = "P_DSR"; badS.DailyPnl = Rand(400, 99); badS.Trades = win; badS.Sessions = 60; badS.Fitness = 100;
			double[] nullV = new double[30]; for (int i = 0; i < 30; i++) nullV[i] = i;
			List<NQCE7_Verdict> vs = new List<NQCE7_Verdict>(); foreach (NQCE7_CandidateInput ci in new NQCE7_CandidateInput[] { good, badDd, badP, badS }) vs.Add(NQCE7_Evidence.Evaluate(ci, ne, nullV, cf, NQC_AccountProfile.RapidIntraday50K()));
			Chk("R1 solo pasa el portafolio bueno", vs[0].Pass && !vs[1].Pass && !vs[2].Pass && !vs[3].Pass, vs[0].Pass + "," + vs[1].Pass + "," + vs[2].Pass + "," + vs[3].Pass + " dsr=" + F(vs[0].Dsr.Dsr) + " dsrS=" + F(vs[3].Dsr.Dsr));
			Chk("R2 causa de rechazo por puerta", vs[1].Causes.Count == 1 && vs[1].Causes[0].StartsWith("MC_DD_P95") && vs[2].Causes.Count == 1 && vs[2].Causes[0].StartsWith("PERCENTIL_NULO") && vs[3].Causes.Count == 1 && vs[3].Causes[0].StartsWith("DSR"), string.Join(" | ", vs[3].Causes));
			NQCE7_Verdict vNo = NQCE7_Evidence.Evaluate(good, NQCE7_NEff.Compute(null, 100, 0.4, 1, 4000), nullV, cf, NQC_AccountProfile.RapidIntraday50K());
			NQCE7_Verdict vNu = NQCE7_Evidence.Evaluate(good, ne, null, cf, NQC_AccountProfile.RapidIntraday50K());
			Chk("R3 sin N_eff o sin nulo de ET2 -> rechaza (falla cerrada)", !vNo.Pass && !vNu.Pass, "");
			string r1 = NQCE7_Evidence.Report(cf, ne, nullV, vs, commit, "spec_sha256=" + NQCE7_Freeze.Sha256File(spec)), r2 = NQCE7_Evidence.Report(cf, ne, nullV, vs, commit, "spec_sha256=" + NQCE7_Freeze.Sha256File(spec));
			Chk("R4 informe determinista, con umbrales, N bruto/N_eff y causas", r1 == r2 && r1.Contains("N_bruto=40") && r1.Contains("N_eff=40") && r1.Contains("causa_rechazo MC_DD_P95") && r1.Contains("RESUMEN: 1 de 4"), "");
			rep.Append("\n--- informe de muestra (sintetico) ---\n").Append(r1);
			string text = rep.ToString() + "\nresultado: " + pass + " PASS / " + fail + " FALLA\n";
			string outDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results", "ET7EVIDENCETEST_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
			Directory.CreateDirectory(outDir);
			File.WriteAllText(Path.Combine(outDir, "EVIDENCE_TEST_REPORT.txt"), text, enc);
			File.WriteAllText(Path.Combine(outDir, "SUMMARY.txt"), "NQC ET7 EVIDENCE TEST - SUMMARY\nscript_version : " + ScriptVersion + "\ncontract : " + NQCE_Const.ContractVersion + "\nresultado : " + pass + " PASS / " + fail + " FALLA\nreport_sha256 : " + NQCE_Const.Sha256Text(text) + "\n", enc);
			Print(text);
		}
	}
}
