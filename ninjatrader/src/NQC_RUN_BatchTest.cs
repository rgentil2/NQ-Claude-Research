#region Using declarations
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

//==============================================================================
// NQC_RUN_BatchTest - Prueba TECNICA SINTETICA del lanzador por lotes NQC_RUN_Batch, del reservorio del N_eff (D-038) y de la guarda ShouldRun del runner.
// SIN ordenes, SOLO el mundo de juguete de NQC_RUN_FunnelTest (hace falta ese archivo: NQC_RUN_FunnelTest.MakeWorld y NQCRT_ToyLib). NO calcula ni reporta resultados
// economicos de mercado. Requiere ademas NQC_ET1_Core, NQC_ET1C_AccountSim, NQC_ET2_Datasets, NQC_ET2_EdgeInjector, NQC_ET3_*, NQC_ET4_Prune, NQC_ET5_Robust,
// NQC_ET6_Portfolio, NQC_ET7_* (sin Test), NQC_RUN_Funnel, NQC_RUN_NeffAdapter y NQC_RUN_Batch.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public class NQC_RUN_BatchTest : Strategy
	{
		private const string ScriptVersion = "RUNBATCHTEST-1.1.0";
		private bool started, done; private StringBuilder rep = new StringBuilder(); private int pass, fail;
		public string SummaryText = "";

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults) { Description = "Prueba sintetica del lanzador por lotes, el reservorio de N_eff y la guarda ShouldRun (sin ordenes)."; Name = "NQC_RUN_BatchTest"; Calculate = Calculate.OnBarClose; BarsRequiredToTrade = 0; IsExitOnSessionCloseStrategy = false; TraceOrders = false; }
			else if (State == State.DataLoaded) started = true;
			else if (State == State.Terminated)
			{
				if (!started || done) return; done = true;
				try { RunAll(); }
				catch (Exception ex)
				{
					Print("NQC_BATCHTEST_ERROR " + ex.Message + " | " + ex.StackTrace);
					try { string d = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results"); Directory.CreateDirectory(d); File.WriteAllText(Path.Combine(d, "BATCHTEST_ERROR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt"), ex.ToString()); } catch { }
				}
			}
		}
		protected override void OnBarUpdate() { }

		private static string I(long v) { return v.ToString(CultureInfo.InvariantCulture); }
		private void Chk(string name, bool ok, string info) { if (ok) pass++; else fail++; rep.Append(ok ? "[PASS] " : "[FALLA] ").Append(name).Append(" | ").Append(info).Append("\n"); }

		private const string Glob = "batch=TOY\nallow_custom=1\net2_approved=1\ntol=10\nmemo_budget_mb=64\nneff_sample=40\net6.ppass_min=0\net7.neff_coverage_min=0.01\nresume=1\n";
		private const string Sz = " size=CUSTOM pop=24 gen=4 maxevals=2000 topk=50";
		private static string Manifest4() { return Glob + "campaign id=C1 transform=IDENTITY gaseed=20269001" + Sz + "\ncampaign id=T-N1 transform=N1 tseed=11 gaseed=20269002" + Sz + "\ncampaign id=T-N2 transform=N2 tseed=12 gaseed=20269003" + Sz + "\ncampaign id=T-INJ transform=N1+INJ tseed=13 gaseed=20269004" + Sz + "\n"; }

		private static NQCRUN_BatchOptions Opt()
		{
			NQCRUN_BatchOptions o = new NQCRUN_BatchOptions(); o.LibraryFactory = delegate() { return new NQCRT_ToyLib(); };
			o.Config = new NQCE_Config(); o.Config.Excluded = new HashSet<int>(); o.EngineHash = "TEST-ENGINE-HASH";
			double[] nb = new double[30]; for (int i = 0; i < 30; i++) nb[i] = 0.05 * (i + 1); o.NullBest = nb; return o;
		}
		private static NQCRUN_BatchResult Go(NQC_RUN_FunnelTest.World w, string manifest, string root) { return NQCRUN_Batch.Run(manifest, w.T, w.O, w.H, w.L, w.C, w.V, root, Opt()); }
		private static string ParseErr(string text) { string e; NQCRUN_BatchManifest m = NQCRUN_BatchManifest.Parse(text, out e); return m == null ? e : ""; }

		private void RunAll()
		{
			rep.Append("NQC RUN BatchTest ").Append(ScriptVersion).Append(" | lanzador ").Append(NQCRUN_Batch.Version).Append(" | runner ").Append(NQCRUN_Funnel.Version).Append(" | contrato ").Append(NQCE_Const.ContractVersion).Append(" | SOLO mundo de juguete sintetico (NO son resultados de mercado)\n\n");
			string root = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results", "RUNBATCH_TEST_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
			Directory.CreateDirectory(root);
			NQC_RUN_FunnelTest.World w = NQC_RUN_FunnelTest.MakeWorld();
			Stopwatch swAll = Stopwatch.StartNew();

			// ---- B1 guarda del defecto FULLPACK run1 y manifiesto
			Chk("B1.1 ShouldRun: solo con DataLoaded Y barras recibidas (instanciada sin datos no hace nada)", !NQC_RUN_Funnel.ShouldRun(false, false) && !NQC_RUN_Funnel.ShouldRun(true, false) && !NQC_RUN_Funnel.ShouldRun(false, true) && NQC_RUN_Funnel.ShouldRun(true, true), "ok");
			NQCRUN_BatchManifest mm; { string e; mm = NQCRUN_BatchManifest.Parse(Manifest4(), out e); }
			Chk("B1.2 el manifiesto de 4 campanas (identidad, N1, N2, N1+INJ) se lee; N1+INJ se normaliza; tamano CUSTOM; hash del manifiesto estable", mm != null && mm.Campaigns.Count == 4 && mm.Campaigns[3].Transform == "N1INJ" && mm.Campaigns[0].Transform == "IDENTITY" && mm.Campaigns[1].TSeed == 11 && mm.Sha.Length == 64 && mm.Sha == NQCRUN_BatchManifest.Parse(Manifest4() + "# comentario\n", out perrDummy).Sha, mm == null ? "nulo" : "campanas=" + I(mm.Campaigns.Count));
			NQCRUN_BatchManifest red = NQCRUN_BatchManifest.Parse("campaign id=A transform=N1 tseed=1 gaseed=2\ncampaign id=B transform=N2 tseed=1 gaseed=3 size=FULL\ncampaign id=Z transform=N1 tseed=5 gaseed=6 size=TIMING evals=2000\n", out perrDummy);
			Chk("B1.3 tamanos: REDUCED = 200x50 / 10.000 / top-K 200; FULL = 500x200 / 100.000 / top-K 2.000; TIMING = N evaluaciones (2.000 por defecto)", red != null && red.Campaigns[0].Pop == 200 && red.Campaigns[0].Gen == 50 && red.Campaigns[0].MaxEvals == 10000 && red.Campaigns[0].TopK == 200 && red.Campaigns[1].Pop == 500 && red.Campaigns[1].Gen == 200 && red.Campaigns[1].MaxEvals == 100000 && red.Campaigns[1].TopK == 2000 && red.Campaigns[2].Size == "TIMING" && red.Campaigns[2].Evals == 2000, "ok");
			string[] bad = new string[] {
				"campaign id=A transform=N1 tseed=1\n",                                   // sin gaseed
				"campaign id=A transform=N7 tseed=1 gaseed=2\n",                           // transformador desconocido
				"campaign id=A transform=N1 gaseed=2\n",                                   // N1 sin tseed
				"campaign id=A transform=N1 tseed=1 gaseed=2\ncampaign id=A transform=N2 tseed=1 gaseed=2\n",   // id repetido
				"et2_approved=1\ncampaign id=X1 transform=IDENTITY gaseed=2\n",            // IDENTITY (IS real) con id que no es C1..C5
				"campaign id=C2 transform=IDENTITY gaseed=2\n",                            // IDENTITY sin et2_approved
				"campaign id=A transform=IDENTITY gaseed=2 size=TIMING\n",                 // TIMING sobre el IS original
				"campaign id=A transform=N1INJ tseed=1 gaseed=2 size=TIMING\n",            // TIMING sobre N1+INJ
				"campaign id=A transform=N1 tseed=1 gaseed=2 size=CUSTOM pop=1 gen=1 maxevals=1 topk=1\n",   // CUSTOM sin allow_custom
				"freeze=1\ncampaign id=A transform=N1 tseed=1 gaseed=2\n",                 // congelado prohibido en el lote
				"seed=5\ncampaign id=A transform=N1 tseed=1 gaseed=2\n",                   // semilla global prohibida
				"campaign id=A transform=N1 tseed=1 gaseed=2 foo=1\n",                     // clave de campana desconocida
				"clave_rara=1\ncampaign id=A transform=N1 tseed=1 gaseed=2\n",             // clave global desconocida (la valida el runner)
				"# vacio\n" };
			bool allBad = true; string whyBad = ""; foreach (string b in bad) if (ParseErr(b).Length == 0) { allBad = false; whyBad += "[" + b.Trim().Replace('\n', '|') + "] "; }
			Chk("B1.4 manifiestos invalidos rechazados (sin gaseed, transformador/tseed, id repetido, IDENTITY solo C1..C5 con et2_approved, TIMING solo sobre N1/N2, CUSTOM sin allow_custom, freeze y seed globales, claves desconocidas, vacio)", allBad, "aceptados indebidamente: " + whyBad);
			{
				NQC_RUN_FunnelTest.World a = new NQC_RUN_FunnelTest.World(); a.T = (long[])w.T.Clone(); a.O = w.O; a.H = w.H; a.L = w.L; a.C = w.C; a.V = w.V; a.Sessions = w.Sessions; a.T[a.T.Length - 1] = new DateTime(2023, 1, 3, 10, 0, 0).Ticks;
				string dOos = Path.Combine(root, "oos"); string eo = ""; try { Go(a, Manifest4().Replace("batch=TOY", "batch=OOS") + "out_root=" + dOos + "\n", root); } catch (NQCRUN_Exception ex) { eo = ex.Message; }
				Chk("B1.5 guarda de datos del lote: una barra fuera del IS (2023) aborta ANTES de correr ninguna campana; el OOS nunca se toca", eo.Contains("fuera del IS") && eo.Contains("OOS") && !Directory.Exists(Path.Combine(dOos, "C1")), eo.Substring(0, Math.Min(80, eo.Length)));
			}

			// ---- B2 lote de 4 campanas
			string rootA = Path.Combine(root, "A");
			Stopwatch swA = Stopwatch.StartNew(); NQCRUN_BatchResult rA = Go(w, Manifest4() + "out_root=" + rootA + "\n", root); swA.Stop();
			bool allOk = rA.Status == "COMPLETO" && rA.Rows.Count == 4; string wh = ""; foreach (NQCRUN_BatchRow r in rA.Rows) { if (r.State != "OK" || r.Digest.Length != 64) { allOk = false; wh += r.Id + ":" + r.State + ":" + r.Note + " "; } }
			Chk("B2.1 el lote ejecuta en secuencia identidad, N1, N2 y N1+INJ (mundo de juguete) y las 4 terminan sin error", allOk, "estado=" + rA.Status + " " + wh);
			bool files = true; string miss = ""; foreach (NQCRUN_BatchRow r in rA.Rows) foreach (string f in new string[] { "SUMMARY.txt", "BATCH_ENTRY.txt", "CKPT_ET3.txt", "ET3/ET3_NEFF_SAMPLE.bin" }) if (!File.Exists(Path.Combine(rootA, r.Id, f.Replace('/', Path.DirectorySeparatorChar)))) { files = false; miss += r.Id + "/" + f + " "; }
			Chk("B2.2 una carpeta por campana con SUMMARY.txt del runner, BATCH_ENTRY.txt, checkpoints y la muestra del N_eff; SUMMARY del lote presente", files && File.Exists(Path.Combine(rootA, "SUMMARY.txt")) && File.Exists(Path.Combine(rootA, "BATCH_LOG.txt")), miss);
			string s1 = File.ReadAllText(Path.Combine(rootA, "T-N1", "SUMMARY.txt")), s4 = File.ReadAllText(Path.Combine(rootA, "T-INJ", "SUMMARY.txt")), s0 = File.ReadAllText(Path.Combine(rootA, "C1", "SUMMARY.txt"));
			Chk("B2.3 cada SUMMARY lleva su transformador y su semilla; la identidad es C1 (campana real) y las de calibracion usan su etiqueta", s1.Contains("transform=N1 ") && s1.Contains("campaign=T-N1") && s1.Contains("seed=20269002") && s4.Contains("transform=N1+INJ ") && s0.Contains("transform=IDENTITY") && s0.Contains("campaign=C1"), "ok");
			HashSet<string> dg = new HashSet<string>(); foreach (NQCRUN_BatchRow r in rA.Rows) dg.Add(r.Digest);
			string sum = rA.Summary;
			Chk("B2.4 SUMMARY del lote: tabla id/estado/transformador/tamano/semilla/ensayos/estado del embudo/digest, tiempos aparte, 4 digests distintos", dg.Count == 4 && sum.Contains("id\tstate\ttransform\tsize\tga_seed\ttrials\tfunnel_status\tdigest") && sum.Contains("batch_digest_sha256=") && sum.Contains("--- no determinista") && sum.Contains("T-N2\tOK\tN2\tCUSTOM\t20269003\tET3="), "digest_lote=" + rA.BatchDigest.Substring(0, 12));

			// ---- B3 reanudacion
			NQCRUN_BatchResult rB = Go(w, Manifest4() + "out_root=" + rootA + "\n", root);
			bool allRes = rB.Rows.Count == 4; foreach (NQCRUN_BatchRow r in rB.Rows) if (!r.Resumed) allRes = false;
			Chk("B3.1 reanudar: con SUMMARY valido y hash de entrada igual las 4 campanas se saltan y el digest del lote es el mismo", allRes && rB.BatchDigest == rA.BatchDigest && rB.Rows[1].Digest == rA.Rows[1].Digest, "saltadas=" + (allRes ? "4" : "menos"));
			NQCRUN_BatchResult rC = Go(w, Manifest4().Replace("gaseed=20269003", "gaseed=20269099") + "out_root=" + rootA + "\n", root);
			Chk("B3.2 cambiar la semilla del GA de UNA campana (T-N2) solo recalcula esa; las otras se saltan; el digest del lote cambia", rC.Rows[0].Resumed && rC.Rows[1].Resumed && !rC.Rows[2].Resumed && rC.Rows[3].Resumed && rC.Rows[2].Digest != rA.Rows[2].Digest && rC.BatchDigest != rA.BatchDigest, "resumed=" + rC.Rows[0].Resumed + "/" + rC.Rows[1].Resumed + "/" + rC.Rows[2].Resumed + "/" + rC.Rows[3].Resumed);
			string sp = Path.Combine(rootA, "T-N1", "SUMMARY.txt"); File.AppendAllText(sp, "alterado\n");
			NQCRUN_BatchResult rD = Go(w, Manifest4() + "out_root=" + rootA + "\n", root);
			Chk("B3.3 SUMMARY alterado de T-N1: esa campana se recalcula (checkpoints del runner reutilizados) y todo vuelve al digest original del lote", !rD.Rows[1].Resumed && rD.Rows[0].Resumed && rD.Rows[3].Resumed && rD.Rows[1].Digest == rA.Rows[1].Digest && rD.BatchDigest == rA.BatchDigest, "resumed=" + rD.Rows[0].Resumed + "/" + rD.Rows[1].Resumed + "/" + rD.Rows[2].Resumed + "/" + rD.Rows[3].Resumed);

			// ---- B4 determinismo
			string rootB = Path.Combine(root, "B");
			NQCRUN_BatchResult rE = Go(w, Manifest4() + "out_root=" + rootB + "\n", root);
			bool sameAll = rE.BatchDigest == rA.BatchDigest; for (int k = 0; k < 4; k++) if (rE.Rows[k].Digest != rA.Rows[k].Digest || rE.Rows[k].Resumed) sameAll = false;
			Chk("B4.1 determinismo: un lote nuevo en otra carpeta (sin reanudar) da los mismos digests por campana y el mismo digest de lote", sameAll, "lote=" + rE.BatchDigest.Substring(0, 12));
			string ba = NQCE_Const.Sha256Text(File.ReadAllText(Path.Combine(rootA, "T-INJ", "SUMMARY.txt")).Split(new string[] { "--- no determinista" }, StringSplitOptions.None)[0]);
			string bb = NQCE_Const.Sha256Text(File.ReadAllText(Path.Combine(rootB, "T-INJ", "SUMMARY.txt")).Split(new string[] { "--- no determinista" }, StringSplitOptions.None)[0]);
			string ha = NQCE7_Freeze.Sha256File(Path.Combine(rootA, "T-N2", "ET3", "ET3_NEFF_SAMPLE.bin")), hb = NQCE7_Freeze.Sha256File(Path.Combine(rootB, "T-N2", "ET3", "ET3_NEFF_SAMPLE.bin"));
			Chk("B4.2 la parte determinista del SUMMARY de campana y la muestra del N_eff (binario) son identicas entre lotes", ba == bb && ha == hb, "muestra_sha=" + ha.Substring(0, 12));

			// ---- B5 reservorio (D-038)
			{
				int items = 200, K = 20, seeds = 2000; int[] hit = new int[items]; bool det = true;
				for (int sd = 0; sd < seeds; sd++)
				{
					NQCE3_Reservoir r = new NQCE3_Reservoir(K, 1000 + sd); for (int i = 0; i < items; i++) r.Offer(I(i), new double[] { i, 0, 1 });
					for (int j = 0; j < r.Count; j++) hit[(int)r.Series(j)[0]]++;
					if (sd < 5) { NQCE3_Reservoir r2 = new NQCE3_Reservoir(K, 1000 + sd); for (int i = 0; i < items; i++) r2.Offer(I(i), new double[] { i, 0, 1 }); for (int j = 0; j < K; j++) if (r2.Id(j) != r.Id(j)) det = false; }
				}
				double maxDev = 0, firstF = (double)hit[0] / seeds, lastF = (double)hit[items - 1] / seeds; for (int i = 0; i < items; i++) maxDev = Math.Max(maxDev, Math.Abs((double)hit[i] / seeds - (double)K / items));
				Chk("B5.1 reservorio uniforme: cada uno de 200 ensayos entra con prob. K/n = 0,10 (2.000 semillas; desviacion maxima < 0,035; primero y ultimo equilibrados), determinista por semilla", maxDev < 0.035 && Math.Abs(firstF - lastF) < 0.035 && det, "desv_max=" + maxDev.ToString("0.0000", CultureInfo.InvariantCulture) + " primero=" + firstF.ToString("0.000", CultureInfo.InvariantCulture) + " ultimo=" + lastF.ToString("0.000", CultureInfo.InvariantCulture));
				NQCE3_Reservoir rs = new NQCE3_Reservoir(50, 7); for (int i = 0; i < 30; i++) rs.Offer("id" + I(i), i % 3 == 0 ? new double[] { 0, 0, 0, 0 } : new double[] { i, -i, 2 * i, 1 });
				string f2 = Path.Combine(root, "res_test.bin"); rs.Save(f2); string[] ids; long seen, gs; double[][] ld = NQCE3_Reservoir.Load(f2, out ids, out seen, out gs);
				bool same = ld.Length == 30 && seen == 30 && gs == 7 && ids[4] == "id4" && ld[5][2] == 10 && ld[5].Length == 4;
				Chk("B5.2 reservorio con K > n guarda todo; archivo binario guarda y recarga exacto; cuenta series planas; coste de memoria documentado (5.000 x 780 sesiones)", same && rs.ConstantSeries() == 10 && NQCE3_Reservoir.EstimateBytes(5000, 780) == 5000L * (780L * 8 + 100), "planas=" + I(rs.ConstantSeries()) + " memoria(5000x780)=" + I(NQCE3_Reservoir.EstimateBytes(5000, 780) / 1048576) + " MB");
				Dictionary<string, string> m3 = Meta(Path.Combine(rootA, "C1", "CKPT_ET3.txt")); Dictionary<string, string> m7 = Meta(Path.Combine(rootA, "C1", "CKPT_ET7.txt"));
				string[] sid; long sSeen, sSeed; double[][] sm = NQCE3_Reservoir.Load(Path.Combine(rootA, "C1", "ET3", "ET3_NEFF_SAMPLE.bin"), out sid, out sSeen, out sSeed);
				int fresh = int.Parse(m3["fresh_evals"], CultureInfo.InvariantCulture), nS = int.Parse(m3["neff_sample_n"], CultureInfo.InvariantCulture);
				Chk("B5.3 campana ET3: la muestra (40) es de TODOS los ensayos evaluados (no del top-K), N poblacion = ensayos evaluados, series de longitud = sesiones, id en el ledger", sm.Length == nS && nS == Math.Min(40, (int)sSeen) && sSeen <= fresh && m3["neff_population"] == I(fresh) && sm[0].Length == w.Sessions && File.ReadAllText(Path.Combine(rootA, "C1", "ET3", "ET3_LEDGER.tsv")).Contains(sid[0]),
					"vistos=" + I(sSeen) + " evaluaciones=" + I(fresh) + " muestra=" + I(nS) + " planas=" + m3["neff_sample_flat"] + " T=" + I(sm[0].Length));
				string estPath = Path.Combine(rootA, "C1", "ET7", "ET7_NEFF_SAMPLE_ESTIMATE.txt"); string est = File.Exists(estPath) ? File.ReadAllText(estPath) : "";
				Chk("B5.4 adaptador ET7: la muestra y N_poblacion (N bruto, con N_ET3 aparte) llegan a NQCE7_Neff.EstimateFromSample; el estimado (o su falla cerrada) queda en ET7_NEFF_SAMPLE_ESTIMATE.txt y en el checkpoint", est.Contains("N_ET3=" + I(fresh)) && est.Contains("N_poblacion_usada=") && est.Contains("estimador=Chao1") && m7 != null && m7.ContainsKey("neff_sample_status") && m7["neff_adapter"] == NQCRUN_NeffAdapter.Id, m7 == null ? "sin ET7" : "estado=" + m7["neff_sample_status"]);
			}

			// ---- B6 TIMING
			{
				string tm = Glob + "campaign id=TM1 transform=N1 tseed=21 gaseed=20269010 size=TIMING evals=150\n"; string rootT = Path.Combine(root, "T");
				NQCRUN_BatchResult rT = Go(w, tm + "out_root=" + rootT + "\n", root); string ts = rT.Rows.Count == 1 && File.Exists(Path.Combine(rootT, "TM1", "SUMMARY.txt")) ? File.ReadAllText(Path.Combine(rootT, "TM1", "SUMMARY.txt")) : "";
				string lower = ts.ToLowerInvariant();
				Chk("B6.1 TIMING: N evaluaciones sobre un dataset N1; escribe ms/evaluacion (media, mediana, p90) y memoria; sin guardar ni escribir economia (sin ledger, databank, fitness, t, neto)", rT.Rows[0].State == "TIMING" && ts.Contains("ms_por_evaluacion: media=") && ts.Contains("mediana=") && ts.Contains("p90=") && ts.Contains("pico_working_set_mb=") && ts.Contains("transform=N1") && !lower.Contains("fitness") && !lower.Contains("net") && !lower.Contains("best_t") && !File.Exists(Path.Combine(rootT, "TM1", "ET3", "ET3_LEDGER.tsv")) && !File.Exists(Path.Combine(rootT, "TM1", "ET3", "ET3_DATABANK.tsv")), "ensayos=" + rT.Rows[0].Trials);
				NQCRUN_BatchResult rT2 = Go(w, tm + "out_root=" + rootT + "\n", root);
				Chk("B6.2 TIMING nunca se salta (es una medicion de esta maquina) y no entra en el digest del lote (su digest es NA)", !rT2.Rows[0].Resumed && rT2.BatchDigest == rT.BatchDigest && rT.Rows[0].Digest == "NA", "ok");
			}
			swAll.Stop();

			rep.Append("\n--- Medicion (mundo de juguete, biblioteca de juguete, mono; NO es el rendimiento del embudo real) ---\n");
			rep.Append("lote de 4 campanas (pop 24 x 4 gen): ").Append((swA.ElapsedMilliseconds / 1000.0).ToString("0.00", CultureInfo.InvariantCulture)).Append(" s; prueba completa=").Append((swAll.ElapsedMilliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)).Append(" s\n");
			rep.Append("\nresumen del lote A (juguete):\n").Append(rA.Summary);
			StringBuilder su = new StringBuilder();
			su.Append("NQC RUN BATCH TEST - SUMMARY\nscript_version : " + ScriptVersion + "\nbatch_version  : " + NQCRUN_Batch.Version + "\nrunner_version : " + NQCRUN_Funnel.Version + "\ncore_version   : " + NQCE_Const.CoreVersion + "\ncontract       : " + NQCE_Const.ContractVersion + "\n");
			su.Append("checks_ok      : " + I(pass) + "\nchecks_fail    : " + I(fail) + "\nbatch_digest_A : " + rA.BatchDigest + "\n");
			string report = rep.ToString(); su.Append("report_sha256  : " + NQCE_Const.Sha256Text(report.Substring(0, report.IndexOf("--- Medicion", StringComparison.Ordinal))) + "\n");
			su.Append("result         : " + (fail == 0 ? "PASS" : "FAIL") + "\n");
			File.WriteAllText(Path.Combine(root, "BATCH_TEST_REPORT.txt"), report, new UTF8Encoding(false));
			File.WriteAllText(Path.Combine(root, "SUMMARY.txt"), su.ToString(), new UTF8Encoding(false));
			SummaryText = su.ToString();
			Print(report); Print(su.ToString());
		}

		private static string perrDummy;
		private static Dictionary<string, string> Meta(string path)
		{
			if (!File.Exists(path)) return null; Dictionary<string, string> d = new Dictionary<string, string>();
			foreach (string line in File.ReadAllText(path).Split('\n')) if (line.StartsWith("meta.", StringComparison.Ordinal)) { int e = line.IndexOf('='); d[line.Substring(5, e - 5)] = line.Substring(e + 1); }
			return d;
		}
	}
}
