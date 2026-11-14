using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;

//==============================================================================
// NQC_ET5_RobustTest - Prueba TECNICA SINTETICA de NQC_ET5_Robust (filtro por anios + PBO/CSCV). Sin ordenes, sin datos de mercado,
// no reporta resultados economicos. Matrices a mano con PBO conocido + comprobacion cruzada con una implementacion independiente.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public class NQC_ET5_RobustTest : Strategy
	{
		private const string ScriptVersion = "ET5ROBUSTTEST-1.0.0";
		private bool started, done; private StringBuilder rep = new StringBuilder(); private int pass, fail;
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults) { Name = "NQC_ET5_RobustTest"; Calculate = Calculate.OnBarClose; BarsRequiredToTrade = 0; IsExitOnSessionCloseStrategy = false; TraceOrders = false; }
			else if (State == State.DataLoaded) started = true;
			else if (State == State.Terminated) { if (!started || done) return; done = true; RunAll(); }
		}
		protected override void OnBarUpdate() { }
		private static string F(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }
		private void Chk(string name, bool ok, string info) { if (ok) pass++; else fail++; rep.Append(ok ? "[PASS] " : "[FALLA] ").Append(name).Append(" | ").Append(info).Append("\n"); }

		static NQCE5_Candidate C(string id, double[] q) { return new NQCE5_Candidate(id, q, null); }
		static double[] Rep(double v) { double[] a = new double[12]; for (int i = 0; i < 12; i++) a[i] = v; return a; }

		// Implementacion INDEPENDIENTE: recorre 4096 mascaras, rangos por conteo explicito, sin reutilizar codigo de NQCE5_Robust.
		static double RefPbo(List<double[]> m)
		{
			int n = m.Count, parts = 0, bad = 0;
			for (int mask = 0; mask < 4096; mask++)
			{
				int bits = 0; for (int b = 0; b < 12; b++) bits += (mask >> b) & 1;
				if (bits != 6) continue; parts++;
				double[] a = new double[n], b2 = new double[n];
				for (int i = 0; i < n; i++) for (int q = 0; q < 12; q++) { if (((mask >> q) & 1) == 1) a[i] += m[i][q]; else b2[i] += m[i][q]; }
				int w = 0; for (int i = 1; i < n; i++) if (a[i] > a[w]) w = i;
				int below = 0, equal = 0; for (int i = 0; i < n; i++) { if (i == w) continue; if (b2[i] < b2[w]) below++; else if (b2[i] == b2[w]) equal++; }
				double rank = below + 1 + equal / 2.0; double om = rank / (n + 1.0);
				if (om <= 0.5) bad++;
			}
			if (parts != 924) return -1;
			return (double)bad / parts;
		}

		private void RunAll()
		{
			rep.Append("NQC ET5 RobustTest ").Append(ScriptVersion).Append(" | contrato ").Append(NQCE_Const.ContractVersion).Append(" | SOLO datos sinteticos\n\n");
			NQCE5_Params p = new NQCE5_Params();

			Chk("R1 particiones CSCV: C(12,6)=924", NQCE5_Robust.Partitions().Count == 924, "n=" + NQCE5_Robust.Partitions().Count);

			// R2 filtro por anios, a mano
			double[] a = Rep(1), b = Rep(0), cc = new double[12], d = Rep(-1);
			for (int q = 0; q < 12; q++) cc[q] = q < 4 ? 5 : (q < 8 ? -9 : 5);   // anios: +20, -36, +20 -> 2 positivos
			double[] e = new double[12]; for (int q = 0; q < 12; q++) e[q] = q < 4 ? 5 : -1;  // +20, -4, -4 -> 1 positivo
			double[] f0 = Rep(0); f0[0] = 0; // neto anual 0 no cuenta como positivo
			List<NQCE5_Candidate> pool = new List<NQCE5_Candidate> { C("A", a), C("B0", b), C("C", cc), C("D", d), C("E", e) };
			NQCE5_Result r2 = NQCE5_Robust.Run(pool, p);
			string ids = ""; foreach (NQCE5_Candidate c in r2.Robust) ids += c.Id + ",";
			Chk("R2 filtro anios: A(3/3) y C(2/3) pasan; B0(0 estricto), D(0/3), E(1/3) caen", ids == "A,C," && r2.Rejected.Count == 3, "robustos=" + ids + " rechazados=" + r2.Rejected.Count);

			// R3 dominante estable: PBO = 0 exacto
			List<NQCE5_Candidate> dom = new List<NQCE5_Candidate> { C("dom", Rep(10)), C("x1", Rep(1)), C("x2", Rep(-1)), C("x3", Rep(0.5)) };
			double pd = NQCE5_Robust.Pbo(dom, NQCE5_Metric.NetSum, null);
			Chk("R3 dominante estable: n* siempre es el mejor en prueba (omega=4/5) -> PBO = 0", pd == 0.0, "PBO=" + F(pd));

			// R4 sobreajustado extremo: PBO = 1 exacto (calculo a mano en docs/stages/ET5.md: k!=3 -> 524 particiones lambda<0; k=3 -> 400 empates lambda=0 contados)
			double[] A = new double[12], B = new double[12]; for (int q = 0; q < 12; q++) { A[q] = q < 6 ? 1 : -1; B[q] = -A[q]; }
			List<NQCE5_Candidate> ov = new List<NQCE5_Candidate> { C("A", A), C("B", B) };
			double po = NQCE5_Robust.Pbo(ov, NQCE5_Metric.NetSum, null);
			Chk("R4 sobreajustado (perfiles espejo): PBO = 1 exacto", po == 1.0, "PBO=" + F(po));

			// R5 candidatos identicos: omega=0,5 siempre (empate total) -> PBO = 1 por convencion conservadora
			List<NQCE5_Candidate> idn = new List<NQCE5_Candidate> { C("i1", A), C("i2", A), C("i3", A) };
			double pi = NQCE5_Robust.Pbo(idn, NQCE5_Metric.NetSum, null);
			Chk("R5 identicos: convencion lambda<=0 cuenta -> PBO = 1 (documentado)", pi == 1.0, "PBO=" + F(pi));

			// R6 ruido puro iid, 20 candidatos: PBO ~ 0,5
			NQCE_Rng rng = new NQCE_Rng(20261114L); List<NQCE5_Candidate> noise = new List<NQCE5_Candidate>(); List<double[]> raw = new List<double[]>();
			for (int i = 0; i < 20; i++) { double[] q = new double[12]; for (int k = 0; k < 12; k++) q[k] = (rng.NextDouble() - 0.5) * 2000.0; noise.Add(C("n" + i, q)); raw.Add(q); }
			double pn = NQCE5_Robust.Pbo(noise, NQCE5_Metric.NetSum, null);
			Chk("R6 ruido puro iid (20 candidatos): PBO en [0,35 ; 0,65]", pn >= 0.35 && pn <= 0.65, "PBO=" + F(pn));

			// R7 contraste con implementacion independiente (exacto)
			double pr = RefPbo(raw), pr2 = RefPbo(new List<double[]> { A, B }), pr3 = RefPbo(new List<double[]> { Rep(10), Rep(1), Rep(-1), Rep(0.5) });
			Chk("R7 implementacion independiente coincide exactamente (ruido, espejo, dominante)", pr == pn && pr2 == po && pr3 == pd, "ruido=" + F(pr) + " espejo=" + F(pr2) + " dom=" + F(pr3));

			// R8 gate y cierre de campania
			NQCE5_Result rg = NQCE5_Robust.Run(new List<NQCE5_Candidate> { C("dom", Rep(10)), C("x1", Rep(1)), C("x2", Rep(2)) }, p);
			NQCE5_Result rb = NQCE5_Robust.Run(ov, p);   // A y B: A=(+4 anios...) cada anio: A=+4? ver abajo
			NQCE5_Result rv = NQCE5_Robust.Run(new List<NQCE5_Candidate> { C("D", d) }, p);
			NQCE5_Result r1 = NQCE5_Robust.Run(new List<NQCE5_Candidate> { C("solo", Rep(3)) }, p);
			Chk("R8a dominante: PBO 0 -> pasa, campania abierta", rg.PboPass && !rg.CampaignClosed, "pbo=" + F(rg.Pbo));
			Chk("R8b pool vacio tras filtro -> campania cerrada", rv.CampaignClosed && rv.Robust.Count == 0 && rv.CloseReason == "pool robusto vacio", rv.CloseReason);
			Chk("R8c N=1: PBO no evaluable -> falla cerrada (pregunta abierta D5-6)", r1.CampaignClosed && !r1.PboEvaluable, r1.CloseReason);
			NQCE5_Result rbad = NQCE5_Robust.Run(new List<NQCE5_Candidate> { C("n1", noise[0].QuarterNet), C("n2", noise[1].QuarterNet), C("n3", noise[2].QuarterNet), C("n4", noise[3].QuarterNet) }, new NQCE5_Params { MinPositiveYears = 0 });
			Chk("R8d PBO > 0,40 cierra la campania (si PBO>0,4 con ruido de 4 candidatos) o pasa coherentemente", rbad.CampaignClosed == (rbad.Pbo > 0.40), "pbo=" + F(rbad.Pbo) + " cerrada=" + rbad.CampaignClosed);

			// R9 determinismo
			NQCE5_Result x = NQCE5_Robust.Run(noise, new NQCE5_Params { MinPositiveYears = 0 }), y = NQCE5_Robust.Run(noise, new NQCE5_Params { MinPositiveYears = 0 });
			Chk("R9 determinismo: mismo hash de pool y de informe en dos corridas", x.PoolHash == y.PoolHash && x.ReportHash == y.ReportHash && x.PoolHash.Length == 64, "pool=" + x.PoolHash + " informe=" + x.ReportHash);

			// R10 metrica expectativa: trimestre sin operaciones aporta 0
			int[] t1 = new int[12], t2 = new int[12]; for (int q = 0; q < 12; q++) { t1[q] = 10; t2[q] = 0; }
			NQCE5_Candidate ex1 = new NQCE5_Candidate("e1", Rep(10), t1), ex2 = new NQCE5_Candidate("e2", Rep(0), t2);
			double pe = NQCE5_Robust.Pbo(new List<NQCE5_Candidate> { ex1, ex2 }, NQCE5_Metric.Expectancy, null);
			Chk("R10 expectativa: sin operaciones = 0 (no NaN); e1 (1/op) domina -> PBO 0", pe == 0.0, "PBO=" + F(pe));

			string text = rep.ToString() + "\nresultado: " + pass + " PASS / " + fail + " FALLA\n";
			string outDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results", "ET5ROBUST_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
			Directory.CreateDirectory(outDir);
			UTF8Encoding enc = new UTF8Encoding(false);
			File.WriteAllText(Path.Combine(outDir, "ROBUST_TEST_REPORT.txt"), text, enc);
			File.WriteAllText(Path.Combine(outDir, "SUMMARY.txt"), "NQC ET5 ROBUST TEST - SUMMARY\nscript_version : " + ScriptVersion + "\ncontract : " + NQCE_Const.ContractVersion + "\nresultado : " + pass + " PASS / " + fail + " FALLA\nreport_sha256 : " + NQCE_Const.Sha256Text(text) + "\npool_hash_R9 : " + x.PoolHash + "\ninforme_hash_R9 : " + x.ReportHash + "\n", enc);
			Print(text);
		}
	}
}
