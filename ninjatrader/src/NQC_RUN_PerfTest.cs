#region Using declarations
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

//==============================================================================
// NQC_RUN_PerfTest - Prueba TECNICA SINTETICA de la optimizacion y el paralelismo determinista de la evaluacion de genomas (NQCRUN-1.3.0). SIN ordenes, SOLO barras sinteticas
// (NQCE3_SynthM1 y el mundo de juguete del NQC_RUN_FunnelTest); NO calcula ni reporta resultados economicos de mercado. Requiere NQC_ET1_Core, NQC_ET1C_AccountSim, NQC_ET3_*, NQC_ET4_Prune,
// NQC_ET5_*, NQC_ET6_*, NQC_ET7_*, NQC_RUN_Funnel, NQC_RUN_Batch y NQC_RUN_FunnelTest (mundo de juguete y biblioteca de juguete).
//
// QUE DEMUESTRA
//   A  IDENTIDAD BIT A BIT con el commit 4006522 (antes de optimizar): constantes DORADAS calculadas con el codigo anterior (huella de ~135.000 valores de familia con la ruta cacheada y la directa;
//      evaluacion de 300 genomas aleatorios; campana completa con tope de evaluaciones, reservorio y contadores). Si una optimizacion cambiara un solo bit de un valor, estas huellas diferirian.
//      Ademas: P() sin estructura == ruta antigua (PRef); el memo auxiliar == recalcular la expresion original (DebugNoAux).
//   B  DETERMINISMO MULTIHILO: la misma campana con threads = 1, 2, 8 y 24 da el MISMO ledger, databank, curva, reservorio y contadores; lotes con genomas repetidos, vacios y tope de evaluaciones
//      a mitad de generacion; ET4 (candidatos y vecinos) y el embudo completo (juguete) con threads 1 y 8 dan el mismo digest.
//   C  CARGA: 24 hilos sobre 4 nucleos con cache minima (expulsion LRU continua), varias repeticiones; sin excepciones, mismo resultado y presupuesto de cache respetado.
//   D  Configuracion: resolucion de hilos (defecto = min(nucleos - 4, 16), tope por memoria), claves threads/max_memory_mb del runner y del lote (no entran en ningun hash), barrido TIMING.
//   E  Medicion de speed-up y eficiencia por hilo en ESTA maquina (seccion no determinista; no es el rendimiento de NT8).
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public class NQC_RUN_PerfTest : Strategy
	{
		private const string ScriptVersion = "RUNPERFTEST-1.0.0";
		private bool started, done; private StringBuilder rep = new StringBuilder(), det = new StringBuilder(); private int pass, fail;
		public string SummaryText = "";

		// Constantes DORADAS: calculadas con el codigo del commit 4006522 (antes de NQCRUN-1.3.0) por el mismo procedimiento que ejecuta esta prueba.
		private const string GoldFeatures = "f863b2cd5a7b96f7f5e6585218dd0d532d3da132369909f13c3ff43bff9282d7";   // 134.688 valores, mundo de 120 dias, paso 211
		private const string GoldGenomes = "a6ef7784aa66f0eaccde8b4bca98b441cabff5c6dd0d6c018c390b6ace613ff8";    // 300 genomas, semilla 424242
		private const string GoldLedger = "4bf232d3a372cdeb93ca1cba6306704a66bf9f8d46e1e28fa4dfe494ea64ef36", GoldBank = "8ffd170f392fb2552db45c668ec35f9a35f1691bb9a674b3ebb1145340131b18",
			GoldCurve = "6f21833c9934390d29a7d1cdce5171f137f68e7f245ed006b701ccced87effab", GoldRes = "1d99b0bb9e45025640e113b9484a6f5750ac3dd435f77503527bf18c765eae03";
		private const string GoldLedgerNeg = "c6c169e4385450a82a7fdcaa1bb73064ac37aa662818211ed6189e4108e8cb8a", GoldCurveNeg = "5d5eca15935a8b0f60edfeb15ad66178b1ecb4f36e02292846924a12fe04fb3b",
			GoldResNeg = "32122a3fae971017a8456c47a1d56fd560ff557345e3aa46eb3912d89c051629";

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults) { Description = "Prueba sintetica de optimizacion y paralelismo determinista de la evaluacion (sin ordenes)."; Name = "NQC_RUN_PerfTest"; Calculate = Calculate.OnBarClose; BarsRequiredToTrade = 0; IsExitOnSessionCloseStrategy = false; TraceOrders = false; }
			else if (State == State.DataLoaded) started = true;
			else if (State == State.Terminated)
			{
				if (!started || done) return; done = true;
				try { RunAll(); }
				catch (Exception ex)
				{
					Print("NQC_PERFTEST_ERROR " + ex.Message + " | " + ex.StackTrace);
					try { string d = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results"); Directory.CreateDirectory(d); File.WriteAllText(Path.Combine(d, "PERFTEST_ERROR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt"), ex.ToString()); } catch { }
				}
			}
		}
		protected override void OnBarUpdate() { }

		private static string I(long v) { return v.ToString(CultureInfo.InvariantCulture); }
		private static string F(double v, string f) { return v.ToString(f, CultureInfo.InvariantCulture); }
		private static string B(double x) { return BitConverter.DoubleToInt64Bits(x).ToString("x16", CultureInfo.InvariantCulture); }
		private void Chk(string name, bool ok, string info) { if (ok) pass++; else fail++; rep.Append(ok ? "[PASS] " : "[FALLA] ").Append(name).Append(" | ").Append(info).Append("\n"); det.Append(ok ? "P" : "F").Append(name.Substring(0, Math.Min(4, name.Length))).Append('\n'); }

		// ---------------------------------------------------------------- utilidades
		private sealed class Rig { public NQCE_Bars Bars; public double[] Vol; public NQCE3_Library Lib; public NQCE3_Fitness Fit; }
		private static Rig MakeRig(int days, bool neg, long memoBytes)
		{
			NQCE3_Series.MemoBudgetBytes = memoBytes;
			Rig r = new Rig(); double[] vol; r.Bars = NQCE3_SynthM1.Make(days, 777L, out vol); r.Vol = vol;
			if (neg) for (int i = 0; i < r.Bars.Count; i++) if (r.Bars.DayKey[i] >= 20200213 && r.Bars.DayKey[i] <= 20200228) { r.Bars.O[i] -= 9005; r.Bars.H[i] -= 9005; r.Bars.L[i] -= 9005; r.Bars.C[i] -= 9005; }
			r.Lib = new NQCE3_Library(); r.Fit = new NQCE3_Fitness(r.Bars, vol, new NQCE_Config(), r.Lib); return r;
		}

		private sealed class CampOut { public string Ledger, Bank, Curve, Res; public int Fresh, Zero, ZeroRej, Seen, Rows; }
		// Campana corta con el reservorio, tope de evaluaciones a mitad de generacion y la biblioteca real. threads y presupuesto de cache configurables.
		private static CampOut RunCampaign(bool neg, int threads, long memoBytes, int pop, int gens, int maxEvals, long gaSeed, int days)
		{
			Rig r = MakeRig(days, neg, memoBytes);
			r.Fit.Reservoir = new NQCE3_Reservoir(25, 20261114L);
			NQCE3_Params pr = new NQCE3_Params(); pr.PopSize = pop; pr.Generations = gens; pr.TopK = 40; pr.Seed = gaSeed;
			NQCE3_Campaign camp = new NQCE3_Campaign(); camp.Name = "G"; camp.MaxEvals = maxEvals; camp.Threads = threads;
			camp.Run(r.Fit, r.Lib, pr, null);
			StringBuilder rs = new StringBuilder();
			for (int i = 0; i < r.Fit.Reservoir.Count; i++) { rs.Append(r.Fit.Reservoir.Id(i)).Append(':'); foreach (double d in r.Fit.Reservoir.Series(i)) rs.Append(BitConverter.DoubleToInt64Bits(d).ToString("x", CultureInfo.InvariantCulture)).Append(','); rs.Append('\n'); }
			CampOut o = new CampOut(); o.Ledger = camp.Ledger.Sha256(); o.Bank = NQCE_Const.Sha256Text(camp.Bank.Tsv()); o.Curve = NQCE_Const.Sha256Text(camp.Curve.ToString()); o.Res = NQCE_Const.Sha256Text(rs.ToString());
			o.Fresh = r.Fit.FreshEvaluations; o.Zero = r.Fit.ZeroTradeEvals; o.ZeroRej = r.Fit.ZeroTradeWithRejectEvals; o.Seen = (int)r.Fit.Reservoir.Seen; o.Rows = camp.Ledger.Rows;
			NQCE3_Series.MemoBudgetBytes = 256L * 1024 * 1024; return o;
		}
		private static string Same(CampOut a, CampOut b) { return a.Ledger == b.Ledger && a.Bank == b.Bank && a.Curve == b.Curve && a.Res == b.Res && a.Fresh == b.Fresh && a.Zero == b.Zero && a.ZeroRej == b.ZeroRej && a.Seen == b.Seen && a.Rows == b.Rows ? "" : "DIFIERE"; }

		// Huella de valores de familia (ruta cacheada Feature y ruta directa Direct) sobre una muestra fija: MISMO procedimiento que el calculo de la constante dorada.
		private static string FeatureDigest(Rig r, int stride, out int count)
		{
			NQCE3_Series s = r.Lib.SeriesFor(r.Bars); NQCE_Bars bars = r.Bars;
			List<int> win = new List<int>(); for (int i = 0; i < bars.Count; i++) if (bars.Stamp[i] >= 567 && bars.Stamp[i] <= 692 && bars.DayKey[i] >= 20200301) win.Add(i);
			List<int> sample = new List<int>(); for (int k = 0; k < win.Count; k += stride) sample.Add(win[k]);
			for (int i = 0; i < bars.Count && sample.Count < 400; i++) if (bars.Stamp[i] == 569 || bars.Stamp[i] == 570) if (bars.DayKey[i] >= 20200301 && (i % 7) == 0) sample.Add(i);
			int[] ns = new int[] { 4, 5, 9, 13, 17, 22, 26, 30 }; int[] ps = new int[] { 5, 14, 37, 100 };
			StringBuilder sb = new StringBuilder(); count = 0;
			for (int f = 0; f < NQCE3_Catalog.Families; f++)
			{
				if (!NQCE3_Catalog.IsActiveFamily(f)) continue; char kd = NQCE3_Catalog.AtrKindOf(f);
				foreach (int n in ns) foreach (int p in ps)
				{
					if (kd != '2' && p != 14 && p != 100) continue;
					foreach (int at in sample)
					{
						if (at - (7 * n + 10) < 0) continue;
						double v1 = s.Feature(f, n, p, at), v2 = s.Direct(f, n, p, at, kd != '2');
						sb.Append(f).Append(',').Append(n).Append(',').Append(p).Append(',').Append(at).Append(',').Append(B(v1)).Append(',').Append(B(v2)).Append('\n'); count++;
					}
				}
			}
			return NQCE_Const.Sha256Text(sb.ToString());
		}

		private static string GenomeDigest(Rig r, int ng)
		{
			NQCE3_Params pr = new NQCE3_Params(); NQCE_Rng rng = new NQCE_Rng(424242L); StringBuilder gb = new StringBuilder();
			for (int i = 0; i < ng; i++)
			{
				NQCE3_Genome g = NQCE3_Operators.RandomGenome(rng, r.Lib, pr); NQCE3_Eval e = r.Fit.Evaluate(g);
				gb.Append(e.TrialId).Append('|').Append(B(e.Fitness)).Append('|').Append(e.Trades).Append('|').Append(B(e.MeanNet)).Append('|').Append(B(e.StdNet)).Append('|').Append(B(e.TotalNet)).Append('|').Append(e.TradeFingerprint).Append('|').Append(e.Valid).Append('|').Append(e.ZeroTradesWithRejects).Append('\n');
			}
			return NQCE_Const.Sha256Text(gb.ToString());
		}

		private static string BatchSig(NQCE3_BatchItem[] items)
		{
			StringBuilder sb = new StringBuilder();
			foreach (NQCE3_BatchItem it in items) sb.Append(it.Skipped ? "S" : (it.Fresh ? "F" : "H")).Append(it.Eval == null ? "-" : it.Eval.TrialId.Substring(0, 8) + B(it.Eval.Fitness)).Append(';');
			return sb.ToString();
		}

		private void RunAll()
		{
			rep.Append("NQC RUN PerfTest ").Append(ScriptVersion).Append(" | runner ").Append(NQCRUN_Funnel.Version).Append(" | lote ").Append(NQCRUN_Batch.Version).Append(" | biblioteca ").Append(new NQCE3_Library().Version).Append(" | contrato ").Append(NQCE_Const.ContractVersion).Append(" | SOLO datos sinteticos (NO son resultados de mercado)\n\n");
			Stopwatch swAll = Stopwatch.StartNew(); int cores = Environment.ProcessorCount;
			long oldBudget = NQCE3_Series.MemoBudgetBytes;
			StringBuilder tim = new StringBuilder();

			// ---------------------------------------------------------- A identidad bit a bit con el commit anterior
			{
				Rig r = MakeRig(120, false, 2048L * 1024 * 1024); int cnt; Stopwatch sw = Stopwatch.StartNew();
				string fd = FeatureDigest(r, 211, out cnt);
				Chk("A1 huella de " + I(cnt) + " valores de familia (cacheado y directo; periodos 4..30, ATR 5..100, barras de ventana y de inicio de ventana) IDENTICA a la del commit 4006522 (antes de optimizar)", fd == GoldFeatures, fd.Substring(0, 12));
				Rig r2 = MakeRig(120, false, 2048L * 1024 * 1024); string gd = GenomeDigest(r2, 300);
				Chk("A2 evaluacion de 300 genomas aleatorios (TrialId, fitness, operaciones, media, desviacion, total, huella de operaciones, validez) IDENTICA bit a bit a la del commit 4006522", gd == GoldGenomes, gd.Substring(0, 12));
				tim.Append("A1+A2: ").Append(F(sw.Elapsed.TotalSeconds, "0.0")).Append(" s\n");
				// P() sin estructura == ruta antigua
				NQCE3_Series s = r.Lib.SeriesFor(r.Bars); bool ok = true; int bad = 0;
				for (int at = 5; at < r.Bars.Count; at += 37) for (int kind = 0; kind < 10; kind++) if (B(NQCE3_M.P(s, at, kind)) != B(NQCE3_M.PRef(s, at, kind))) { ok = false; bad++; }
				Chk("A3 NQCE3_M.P (sin copiar la estructura de barra) == ruta antigua (PRef) bit a bit para los 10 tipos de precio", ok, "diferencias=" + I(bad));
				// memo auxiliar == recalcular la expresion original
				int[] auxFam = new int[] { 44, 45, 54, 59, 69, 70, 92, 93, 31 }; int[] nn = new int[] { 4, 11, 30 }; bool ok2 = true; int bad2 = 0, cmp = 0;
				List<int> pts = new List<int>(); for (int i = 0; i < r.Bars.Count && pts.Count < 120; i++) if (r.Bars.Stamp[i] >= 569 && r.Bars.Stamp[i] <= 692 && r.Bars.DayKey[i] >= 20200301 && (i % 53) == 0) pts.Add(i);
				foreach (int f in auxFam) foreach (int n in nn) foreach (int at in pts)
				{
					NQCE3_Series.DebugNoAux = false; double a = s.Direct(f, n, 14, at, NQCE3_Catalog.AtrKindOf(f) != '2');
					NQCE3_Series.DebugNoAux = true; double b = s.Direct(f, n, 14, at, NQCE3_Catalog.AtrKindOf(f) != '2'); NQCE3_Series.DebugNoAux = false;
					cmp++; if (B(a) != B(b)) { ok2 = false; bad2++; }
				}
				Chk("A4 memo auxiliar por barra (familias 44, 45, 54, 59, 70, 92, 93) == recalcular la expresion original en cada llamada, bit a bit", ok2, "comparaciones=" + I(cmp) + " diferencias=" + I(bad2));
				// A7: sumas prefijo de las familias 44 (KAMA) y 54 (VIDYA) (ventana parcial incremental) == bucle ORIGINAL (copia literal del codigo anterior), bit a bit, todos los periodos 4..30
				{
					bool ok3 = true; int bad3 = 0, cmp3 = 0; double[] C = s.C;
					for (int n = 4; n <= 30; n++)
						for (int at = 7 * n + 10; at < r.Bars.Count && at < 7 * n + 10 + 4000; at += 3)
						{
							if (r.Bars.Stamp[at] < 567 || r.Bars.Stamp[at] > 692) continue;
							double vid = C[at - 2 * n]; double kam = C[at - 2 * n]; int st = at - 2 * n;
							for (int k = at - 2 * n + 1; k <= at; k++)
							{
								int look = Math.Min(n, k - (at - 2 * n)); double gain = 0, loss = 0;
								for (int j = k - look + 1; j <= k; j++) { double d = C[j] - C[j - 1]; gain += Math.Max(0, d); loss += Math.Max(0, -d); }
								double cmo = Math.Abs(NQCE3_M.Div(gain - loss, gain + loss)); vid += 2.0 / (n + 1) * cmo * (C[k] - vid);
								int t2 = Math.Max(st, k - n), diffs = 0; double travel = 0;
								for (int j = t2 + 1; j <= k; j++) { travel += Math.Abs(C[j] - C[j - 1]); diffs++; }
								double er = diffs == 0 ? 0 : Math.Abs(NQCE3_M.Div(C[k] - C[t2], travel));
								double a2 = Math.Pow(er * (2.0 / 3 - 2.0 / 31) + 2.0 / 31, 2); kam += a2 * (C[k] - kam);
							}
							double r54 = (C[at] - vid) / 1.0, r44 = (C[at] - kam) / 1.0;
							double n54 = s.Direct(54, n, 14, at, true), n44 = s.Direct(44, n, 14, at, true); cmp3 += 2;
							if (B(r54) != B(n54)) { ok3 = false; bad3++; }
							if (B(r44) != B(n44)) { ok3 = false; bad3++; }
						}
					Chk("A7 familias 44 (KAMA) y 54 (VIDYA) con sumas prefijo incrementales == bucle original del commit anterior, bit a bit (periodos 4..30)", ok3, "comparaciones=" + I(cmp3) + " diferencias=" + I(bad3));
				}
				// campana completa (tope de evaluaciones a mitad de generacion, reservorio, contadores) == commit anterior; con 1 hilo
				CampOut c1 = RunCampaign(false, 1, 512L * 1024 * 1024, 60, 5, 170, 20269999L, 40);
				Chk("A5 campana (pop 60 x 5 gen, tope 170 evaluaciones, reservorio K=25): ledger, databank, curva y reservorio IDENTICOS a los del commit 4006522", c1.Ledger == GoldLedger && c1.Bank == GoldBank && c1.Curve == GoldCurve && c1.Res == GoldRes && c1.Fresh == 170 && c1.Zero == 24, "ledger=" + c1.Ledger.Substring(0, 12) + " evaluaciones=" + I(c1.Fresh) + " sin_operaciones=" + I(c1.Zero));
				CampOut c1n = RunCampaign(true, 1, 512L * 1024 * 1024, 60, 5, 170, 20269999L, 40);
				Chk("A6 igual con precios que cruzan cero (familias logaritmicas dan NaN; contadores de condiciones sin senal): identico al commit 4006522", c1n.Ledger == GoldLedgerNeg && c1n.Curve == GoldCurveNeg && c1n.Res == GoldResNeg && c1n.Bank == GoldBank, "ledger=" + c1n.Ledger.Substring(0, 12));
				det.Append("gold_features=").Append(fd).Append("\ngold_genomes=").Append(gd).Append("\ngold_ledger=").Append(c1.Ledger).Append("\n");
			}

			// ---------------------------------------------------------- B determinismo multihilo
			CampOut baseC = RunCampaign(false, 1, 512L * 1024 * 1024, 60, 5, 170, 20269999L, 40);
			{
				string why = ""; int[] tl = new int[] { 2, 8, 24 };
				foreach (int t in tl) { CampOut o = RunCampaign(false, t, 512L * 1024 * 1024, 60, 5, 170, 20269999L, 40); string d = Same(baseC, o); if (d.Length > 0) why += "threads=" + I(t) + " " + d + "; "; }
				Chk("B1 la misma campana con threads = 1, 2, 8 y 24 da el MISMO ledger, databank, curva de fitness, reservorio y contadores (con tope de evaluaciones a mitad de generacion)", why.Length == 0, why.Length == 0 ? "ledger_sha=" + baseC.Ledger.Substring(0, 12) + " evaluaciones=" + I(baseC.Fresh) : why);
				CampOut n1 = RunCampaign(true, 1, 512L * 1024 * 1024, 60, 5, 400, 20269999L, 40); string why2 = "";
				foreach (int t in tl) { CampOut o = RunCampaign(true, t, 512L * 1024 * 1024, 60, 5, 400, 20269999L, 40); string d = Same(n1, o); if (d.Length > 0) why2 += "threads=" + I(t) + " " + d + "; "; }
				Chk("B2 igual con precios que cruzan cero (NaN en familias logaritmicas) y sin tope de evaluaciones (toda la campana)", why2.Length == 0, why2.Length == 0 ? "ledger_sha=" + n1.Ledger.Substring(0, 12) + " evaluaciones=" + I(n1.Fresh) : why2);
				det.Append("B_ledger=").Append(baseC.Ledger).Append("\nB_neg_ledger=").Append(n1.Ledger).Append("\n");
			}
			{
				// lote con genomas repetidos, vacios y ya cacheados: Fresh/Hit/Skipped deterministas
				string[] sigs = new string[3]; int[] tl = new int[] { 1, 2, 8 }; int[] fresh = new int[3];
				for (int q = 0; q < 3; q++)
				{
					Rig r = MakeRig(40, false, 512L * 1024 * 1024); NQCE3_Params pr = new NQCE3_Params(); NQCE_Rng rng = new NQCE_Rng(777L);
					List<NQCE3_Genome> gl = new List<NQCE3_Genome>(); for (int i = 0; i < 14; i++) gl.Add(NQCE3_Operators.RandomGenome(rng, r.Lib, pr));
					gl.Insert(3, gl[1].Clone()); gl.Insert(5, gl[1].Clone()); gl.Insert(9, new NQCE3_Genome()); gl.Add(new NQCE3_Genome());   // repetidos y genomas vacios
					r.Fit.Evaluate(gl[0].Clone());                                                                                       // uno ya en la cache de genomas
					NQCE3_BatchItem[] it = r.Fit.EvaluateBatch(gl, 10, tl[q]);                                                           // tope 10: el resto se omite
					sigs[q] = BatchSig(it) + "|" + I(r.Fit.FreshEvaluations) + "|" + I(r.Fit.ZeroTradeEvals); fresh[q] = r.Fit.FreshEvaluations;
				}
				Chk("B3 lote con genomas repetidos, vacios, uno ya en cache y tope de evaluaciones a mitad del lote: ensayos nuevos, aciertos de cache y omitidos identicos para 1, 2 y 8 hilos", sigs[0] == sigs[1] && sigs[1] == sigs[2] && fresh[0] == 10, "ensayos_nuevos=" + I(fresh[0]) + " huella=" + NQCE_Const.Sha256Text(sigs[0]).Substring(0, 12));
				det.Append("B3=").Append(NQCE_Const.Sha256Text(sigs[0])).Append("\n");
			}
			{
				// ET4: candidatos y vecinos con 1 y 8 hilos
				string[] et4 = new string[2]; int[] tl = new int[] { 1, 8 }; int cands = 0;
				for (int q = 0; q < 2; q++)
				{
					Rig r = MakeRig(140, false, 512L * 1024 * 1024); NQCE3_Params pr = new NQCE3_Params(); pr.PopSize = 50; pr.Generations = 3; pr.TopK = 12; pr.Seed = 20269998L;   // 140 dias: con >= 100 operaciones hay candidatos validos
					NQCE3_Campaign camp = new NQCE3_Campaign(); camp.Name = "G"; camp.Threads = 1; camp.Run(r.Fit, r.Lib, pr, null);
					List<NQCE3_Eval> top = camp.Bank.Top(); List<NQCE4_Candidate> cl = new List<NQCE4_Candidate>(); foreach (NQCE3_Eval e in top) cl.Add(new NQCE4_Candidate(e.TrialId, e.Genome.Clone(), e.TradeFingerprint));
					NQCE4_Prune pn = new NQCE4_Prune(r.Bars, r.Vol, new NQCE_Config(), r.Lib, NQCE4_Prune.LoadLedgerIds(camp.Ledger.Text())); pn.Threads = tl[q]; pn.Evaluate(cl);
					et4[q] = pn.PoolSha256(cl) + "|" + pn.RejectsSha256() + "|" + pn.LedgerSha256() + "|" + I(pn.FreshTrials) + "|" + I(pn.NeighborEvaluations) + "|" + I(pn.ExecutorRuns); cands = cl.Count;
				}
				Chk("B4 ET4 (candidatos en bloques y vecinos en paralelo) con 1 y 8 hilos: mismo pool, rechazos, ledger de vecinos y contadores (con candidatos reales)", et4[0] == et4[1] && cands >= 3, "candidatos=" + I(cands) + " huella=" + NQCE_Const.Sha256Text(et4[0]).Substring(0, 12));
				det.Append("B4=").Append(NQCE_Const.Sha256Text(et4[0])).Append("\n");
			}
			{
				// embudo completo (mundo de juguete): digest identico con threads 1 y 8
				NQC_RUN_FunnelTest.World w = NQC_RUN_FunnelTest.MakeWorld(); string root = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results", "RUNPERF_TEST_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)); Directory.CreateDirectory(root);
				string[] dg = new string[2], ls = new string[2]; int[] tl = new int[] { 1, 8 };
				for (int q = 0; q < 2; q++)
				{
					NQCRUN_Params p = ToyParams(Path.Combine(root, "f" + I(tl[q]))); p.Threads = tl[q];
					NQCRUN_Result res = new NQCRUN_Funnel().Run(w.T, w.O, w.H, w.L, w.C, w.V, p); dg[q] = res.DeterministicSha; NQCRUN_Ckpt c3 = res.Stage("ET3"); ls[q] = c3.M("ledger_sha256") + c3.M("fresh_evals");
				}
				Chk("B5 embudo completo ET3 -> ET7 (mundo de juguete) con threads=1 y threads=8: el mismo digest determinista y el mismo ledger de ET3", dg[0] == dg[1] && ls[0] == ls[1] && dg[0].Length == 64, "digest=" + dg[0].Substring(0, 12));
				det.Append("B5=").Append(dg[0]).Append("\n");
				// lote: los hilos no entran en el hash del manifiesto ni en el digest
				string g = "batch=TOY\nallow_custom=1\net2_approved=1\ntol=10\nmemo_budget_mb=64\nneff_sample=40\net6.ppass_min=0\net7.neff_coverage_min=0.01\nresume=1\n";
				string cz = " size=CUSTOM pop=24 gen=4 maxevals=2000 topk=50";
				string m1 = g + "campaign id=T-N1 transform=N1 tseed=11 gaseed=20269002" + cz + "\n", m2 = "threads=8\nmax_memory_mb=4096\n" + g + "campaign id=T-N1 transform=N1 tseed=11 gaseed=20269002" + cz + " threads=2\n";
				string e1, e2; NQCRUN_BatchManifest a = NQCRUN_BatchManifest.Parse(m1, out e1), b = NQCRUN_BatchManifest.Parse(m2, out e2);
				Chk("D1 el lote acepta threads/max_memory_mb globales y threads= por campana y NO los incluye en el hash del manifiesto", a != null && b != null && a.Sha == b.Sha && b.Campaigns[0].Threads[0] == 2, e1 + e2);
				{
					// el manifiesto de barrido que se entrega al usuario (ET2_BATCH_MANIFEST_PASO0_TIMING_HILOS.txt): mismas lineas de campana
					string mt = "batch=ET2_PASO0_TIMING_HILOS\nresume=1\nmemo_budget_mb=4096\nmax_memory_mb=8192\ncampaign id=TIMING_HILOS transform=N1 tseed=20269001 gaseed=20279001 size=TIMING evals=200 threads=1,4,8,16,24\ncampaign id=TIMING_REGIMEN transform=N1 tseed=20269001 gaseed=20279002 size=TIMING evals=10000 threads=8\n"; string et;
					NQCRUN_BatchManifest mm = NQCRUN_BatchManifest.Parse(mt, out et);
					Chk("D1b el manifiesto de barrido de hilos del runbook (threads=1,4,8,16,24 con 200 evaluaciones + regimen de 10.000 con 8 hilos) es valido", mm != null && mm.Campaigns.Count == 2 && mm.Campaigns[0].Threads.Length == 5 && mm.Campaigns[0].Threads[4] == 24 && mm.Campaigns[0].Evals == 200 && mm.Campaigns[1].Evals == 10000, et ?? "");
				}
				NQCRUN_BatchResult rb1 = RunBatch(w, m1 + "out_root=" + Path.Combine(root, "b1") + "\n"), rb2 = RunBatch(w, m2 + "out_root=" + Path.Combine(root, "b2") + "\n");
				Chk("B6 lote (juguete): batch_digest con threads=1 (por defecto) y con threads=8 / threads=2 por campana es el MISMO", rb1.BatchDigest.Length == 64 && rb1.BatchDigest == rb2.BatchDigest && rb1.Status == "COMPLETO" && rb2.Status == "COMPLETO", "digest=" + rb1.BatchDigest.Substring(0, 12));
				det.Append("B6=").Append(rb1.BatchDigest).Append("\n");
				// barrido TIMING
				string tm = g + "campaign id=TM1 transform=N1 tseed=21 gaseed=20269010 size=TIMING evals=60 threads=1,2,4\n"; string rootT = Path.Combine(root, "T");
				NQCRUN_BatchResult rT = RunBatch(w, tm + "out_root=" + rootT + "\n"); string ts = File.Exists(Path.Combine(rootT, "TM1", "SUMMARY.txt")) ? File.ReadAllText(Path.Combine(rootT, "TM1", "SUMMARY.txt")) : ""; string lower = ts.ToLowerInvariant();
				Chk("D2 TIMING con barrido threads=1,2,4: tabla de speed-up y eficiencia, 'resultado_identico_entre_hilos=SI', sin economia ni ledger en disco", rT.Rows[0].State == "TIMING" && ts.Contains("BARRIDO DE HILOS") && ts.Contains("speedup") && ts.Contains("eficiencia") && ts.Contains("resultado_identico_entre_hilos=SI") && ts.Contains("ms_por_evaluacion: media=") && !lower.Contains("fitness") && !lower.Contains("net") && !File.Exists(Path.Combine(rootT, "TM1", "ET3", "ET3_LEDGER.tsv")), "filas=" + I(rT.Rows.Count));
				string perr; Chk("D3 un barrido de threads fuera de TIMING y valores fuera de 1..128 se rechazan", NQCRUN_BatchManifest.Parse(g + "campaign id=A transform=N1 tseed=1 gaseed=2" + cz + " threads=1,2\n", out perr) == null && NQCRUN_BatchManifest.Parse(g + "campaign id=A transform=N1 tseed=1 gaseed=2 size=TIMING threads=0\n", out perr) == null, "ok");
			}

			// ---------------------------------------------------------- C carga (24 hilos sobre pocos nucleos, cache minima)
			{
				string why = ""; long minBudget = 3L * 1024 * 1024; int reps = 0; long worstBytes = 0, each = 0;
				CampOut refC = RunCampaign(false, 1, minBudget, 100, 3, 250, 20269997L, 40);
				for (int q = 0; q < 3; q++)
				{
					CampOut o = RunCampaign(false, 24, minBudget, 100, 3, 250, 20269997L, 40); reps++;
					string d = Same(refC, o); if (d.Length > 0) why += "rep " + I(q) + " " + d + "; ";
				}
				// presupuesto: tras una campana con cache chica, los bytes de la cache no superan el presupuesto
				Rig r = MakeRig(40, false, 6L * 1024 * 1024); NQCE3_Params pr = new NQCE3_Params(); pr.PopSize = 80; pr.Generations = 2; pr.Seed = 20269996L;
				NQCE3_Campaign camp = new NQCE3_Campaign(); camp.Threads = 8; camp.MaxEvals = 120; camp.Run(r.Fit, r.Lib, pr, null);
				NQCE3_Series ser = r.Lib.SeriesFor(r.Bars); worstBytes = ser.MemoBytes; NQCE3_Series.MemoBudgetBytes = 256L * 1024 * 1024;
				Chk("C1 carga: 24 hilos con cache de 3 MB (expulsion LRU continua), 3 repeticiones: sin excepciones y el mismo resultado que con 1 hilo", why.Length == 0, why.Length == 0 ? "repeticiones=" + I(reps) + " ledger_sha=" + refC.Ledger.Substring(0, 12) : why);
				Chk("C2 el presupuesto de cache se respeta con 8 hilos (6 MB: bytes de la cache <= presupuesto)", worstBytes <= 6L * 1024 * 1024, "bytes_cache=" + I(worstBytes) + " entradas=" + I(ser.MemoCount));
				// contadores de condiciones sin senal: por hilo, sin mezclarse
				long[] wv = new long[2]; Thread t1 = new Thread(delegate() { for (int i = 0; i < 1000; i++) NQCE3_Library.NoteReject(1); long a, b, c; NQCE3_Library.ThreadRejects(out a, out b, out c); wv[0] = a; });
				Thread t2 = new Thread(delegate() { for (int i = 0; i < 300; i++) NQCE3_Library.NoteReject(1); long a, b, c; NQCE3_Library.ThreadRejects(out a, out b, out c); wv[1] = a; });
				t1.Start(); t2.Start(); t1.Join(); t2.Join();
				Chk("C3 los contadores de condiciones sin senal son POR HILO (delta por evaluacion exacto, sin carreras)", wv[0] == 1000 && wv[1] == 300, I(wv[0]) + "/" + I(wv[1]));
			}

			// ---------------------------------------------------------- D configuracion de hilos
			{
				int auto = NQCRUN_Threads.Auto(); int exp = Math.Max(1, Math.Min(cores - 4, 16));
				Chk("D4 hilos por defecto = min(nucleos logicos - 4, 16), al menos 1 (aqui " + I(cores) + " nucleos -> " + I(auto) + ")", auto == exp && auto >= 1 && auto <= 16 && NQCRUN_Threads.Resolve(0, 2048, 6144) == Math.Min(auto, NQCRUN_Threads.MemoryCap(2048, 6144)), "auto=" + I(auto));
				Chk("D5 tope por memoria: 24 hilos pedidos con 2048 MB de cache y 6144 MB de proceso -> " + I(NQCRUN_Threads.Resolve(24, 2048, 6144)) + "; con 3500 MB de proceso se limita; nunca < 1",
					NQCRUN_Threads.Resolve(24, 2048, 6144) == 24 && NQCRUN_Threads.Resolve(24, 2048, 3500) < 24 && NQCRUN_Threads.Resolve(24, 2048, 3500) >= 1 && NQCRUN_Threads.Resolve(5, 4000, 1024) == 1, "tope(2048,3500)=" + I(NQCRUN_Threads.MemoryCap(2048, 3500)));
				string e; NQCRUN_Params pa = NQCRUN_Params.Parse("threads=16\nmax_memory_mb=8192\n", out e); NQCRUN_Params pb = NQCRUN_Params.Parse("", out e); NQCRUN_Params pc = NQCRUN_Params.Parse("threads=200\n", out e);
				NQCRUN_Params pd = NQCRUN_Params.Parse("threads=16\n", out e); NQCE3_Params gp = new NQCE3_Params();
				bool bad = pc != null && pc.Validate().Length == 0;
				Chk("D6 runner: threads/max_memory_mb se leen, el defecto es 0 (automatico), >128 se rechaza y NO entran en el texto canonico (el hash no depende de los hilos)", pa != null && pa.Threads == 16 && pa.MaxMemoryMb == 8192 && pb.Threads == 0 && !bad && pa.CanonicalText(gp) == pb.CanonicalText(gp) && pd.CanonicalText(gp) == pb.CanonicalText(gp), "ok");
			}
			NQCE3_Series.MemoBudgetBytes = oldBudget;

			// ---------------------------------------------------------- E speed-up (no determinista)
			{
				int[] tl = new int[] { 1, 2, 4, 8 }; double[] wall = new double[tl.Length]; string sig0 = ""; bool same = true; int ng = 96; int days = 100;
				tim.Append("--- Speed-up de la evaluacion en ESTA maquina (").Append(I(cores)).Append(" nucleos logicos; ").Append(I(ng)).Append(" genomas aleatorios con la biblioteca real, ").Append(I(days)).Append(" dias sinteticos, datos y cache FRIOS en cada corrida) ---\n");
				tim.Append("hilos\tpared_s\tspeedup\teficiencia_por_hilo\n");
				for (int q = 0; q < tl.Length; q++)
				{
					GC.Collect(); Rig r = MakeRig(days, false, 1024L * 1024 * 1024); NQCE3_Params pr = new NQCE3_Params(); NQCE_Rng rng = new NQCE_Rng(31337L); List<NQCE3_Genome> gl = new List<NQCE3_Genome>(); for (int i = 0; i < ng; i++) gl.Add(NQCE3_Operators.RandomGenome(rng, r.Lib, pr));
					Stopwatch sw = Stopwatch.StartNew(); NQCE3_BatchItem[] it = r.Fit.EvaluateBatch(gl, int.MaxValue, tl[q]); sw.Stop(); wall[q] = sw.Elapsed.TotalSeconds; string sg = BatchSig(it); if (q == 0) sig0 = sg; else if (sg != sig0) same = false;
					double sp = wall[0] / wall[q]; tim.Append(I(tl[q])).Append('\t').Append(F(wall[q], "0.00")).Append('\t').Append(F(sp, "0.00")).Append('\t').Append(F(sp / tl[q], "0.00")).Append('\n');
				}
				Chk("E1 el lote de medicion da el mismo resultado con 1, 2, 4 y 8 hilos (tiempos en la seccion no determinista)", same, "genomas=" + I(ng));
				NQCE3_Series.MemoBudgetBytes = oldBudget;
			}
			swAll.Stop();

			rep.Append("\n--- Medicion (mono, datos sinteticos, biblioteca real; NO es el rendimiento de NT8) ---\n").Append(tim.ToString());
			rep.Append("nucleos logicos=").Append(I(cores)).Append(" prueba completa=").Append(F(swAll.Elapsed.TotalSeconds, "0.0")).Append(" s\n");
			StringBuilder su = new StringBuilder();
			su.Append("NQC RUN PERF TEST - SUMMARY\nscript_version : " + ScriptVersion + "\nrunner_version : " + NQCRUN_Funnel.Version + "\nbatch_version  : " + NQCRUN_Batch.Version + "\ncore_version   : " + NQCE_Const.CoreVersion + "\ncontract       : " + NQCE_Const.ContractVersion + "\nlibrary        : " + new NQCE3_Library().Version + "\n");
			su.Append("checks_ok      : " + I(pass) + "\nchecks_fail    : " + I(fail) + "\nperf_digest_A  : " + NQCE_Const.Sha256Text(det.ToString()) + "\n");
			string report = rep.ToString(); su.Append("report_sha256  : " + NQCE_Const.Sha256Text(report.Substring(0, report.IndexOf("--- Medicion", StringComparison.Ordinal))) + "\n");
			su.Append("result         : " + (fail == 0 ? "PASS" : "FAIL") + "\n");
			string root0 = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results", "RUNPERF_TEST_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)); Directory.CreateDirectory(root0);
			File.WriteAllText(Path.Combine(root0, "PERF_TEST_REPORT.txt"), report, new UTF8Encoding(false));
			File.WriteAllText(Path.Combine(root0, "SUMMARY.txt"), su.ToString(), new UTF8Encoding(false));
			SummaryText = su.ToString();
			Print(report); Print(su.ToString());
		}

		private static NQCRUN_Params ToyParams(string dir)
		{
			NQCRUN_Params p = new NQCRUN_Params(); p.OutDir = dir; p.Population = 60; p.Generations = 12; p.TopK = 200; p.MemoBudgetMb = 64; p.Resume = false;
			p.LibraryFactory = delegate() { return new NQCRT_ToyLib(); };
			p.Config = new NQCE_Config(); p.Config.Excluded = new HashSet<int>();
			p.Et6PpassMin = 0.0;                                   // SOLO PRUEBA: el umbral real lo fija ET2
			p.Et7Text = "neff_coverage_min=0.05\nneff_sample_min_n=30\nneff_sample_cov_min=0\n";
			p.EngineHash = "TEST-ENGINE-HASH";
			double[] nb = new double[30]; for (int i = 0; i < 30; i++) nb[i] = 0.05 * (i + 1); p.NullBest = nb;   // vector nulo SINTETICO (no es de ET2)
			return p;
		}
		private static NQCRUN_BatchResult RunBatch(NQC_RUN_FunnelTest.World w, string manifest)
		{
			NQCRUN_BatchOptions o = new NQCRUN_BatchOptions(); o.LibraryFactory = delegate() { return new NQCRT_ToyLib(); };
			o.Config = new NQCE_Config(); o.Config.Excluded = new HashSet<int>(); o.EngineHash = "TEST-ENGINE-HASH";
			double[] nb = new double[30]; for (int i = 0; i < 30; i++) nb[i] = 0.05 * (i + 1); o.NullBest = nb;
			return NQCRUN_Batch.Run(manifest, w.T, w.O, w.H, w.L, w.C, w.V, Path.GetTempPath(), o);
		}
	}
}
