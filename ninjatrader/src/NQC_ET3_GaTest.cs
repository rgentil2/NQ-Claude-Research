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
// NQC_ET3_GaTest - Prueba TECNICA sintetica de los operadores geneticos D-019 y del bucle de campana (ET3 WP3/WP5).
// Contrato NQC 2.0.0. Estrategia SIN ordenes. Usa SOLO barras sinteticas (no mercado real) y NO reporta resultados economicos.
// Requiere NQC_ET1_Core.cs, NQC_ET1C_AccountSim.cs y los NQC_ET3_*.cs de genoma/biblioteca/fitness/operadores/ledger.
//
//   G1  Parametros D-019: 500 x 200 = 100.000 <= tope, torneo 3, mutacion 5%/gen, elite 5% (25), inmigrantes 5% (25), top-K 2.000, semilla C1 20261114.
//   G2  NextGeneration: tamano exacto de la poblacion (500), elite = clones de los 25 mejores, determinismo con la misma semilla, otra semilla difiere.
//   G3  Los inmigrantes aparecen: 25 genomas validos, distintos entre si, reproducibles desde el mismo flujo NQCE_Rng, y distintos de los padres.
//   G4  Operadores: torneo (mejor de 3), cruce uniforme (cada gen de un padre), mutacion 5%/gen (tasa observada), todo valido.
//   G5  Campana corta sobre barras M1 sinteticas: determinismo (mismo ledger SHA con la misma semilla), tope de evaluaciones (inmigrantes cuentan),
//       top-K deduplicado y acotado.
// NQCE3_Bench (clase auxiliar, no se ejecuta desde NT8): mide costo de tiempo y memoria de una evaluacion tipica de genoma (D-031(3)).
// Salida: <Documentos>\NinjaTrader 8\NQC_Results\ET3GA_<fecha_hora>\GA_TEST_REPORT.txt y SUMMARY.txt
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	// Barras M1 sinteticas con la forma del IS (dias habiles 2020-01-02..., 00:00..22:59, volumen) para pruebas y mediciones.
	public static class NQCE3_SynthM1
	{
		public static NQCE_Bars Make(int days, long seed, out double[] volume)
		{
			int per = 1380, n = days * per;
			long[] t = new long[n]; double[] o = new double[n], h = new double[n], l = new double[n], c = new double[n]; volume = new double[n];
			NQCE_Rng r = new NQCE_Rng(seed); double prev = 9000.0; int k = 0; DateTime d = new DateTime(2020, 1, 2);
			for (int day = 0; day < days; day++)
			{
				while (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) d = d.AddDays(1);
				for (int m = 0; m < per; m++, k++)
				{
					t[k] = d.Ticks + m * 600000000L;
					o[k] = prev; c[k] = prev + (r.NextDouble() - 0.5) * 6.0;
					h[k] = Math.Max(o[k], c[k]) + r.NextDouble() * 1.5; l[k] = Math.Min(o[k], c[k]) - r.NextDouble() * 1.5;
					volume[k] = 100.0 + r.NextDouble() * 900.0; prev = c[k];
				}
				d = d.AddDays(1);
			}
			return new NQCE_Bars(t, o, h, l, c);
		}
	}

	public class NQC_ET3_GaTest : Strategy
	{
		private const string ScriptVersion = "ET3GATEST-1.0.0";
		private bool started, done;
		private StringBuilder rep = new StringBuilder();
		private int pass, fail;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Prueba tecnica sintetica de los operadores geneticos D-019 (sin ordenes).";
				Name = "NQC_ET3_GaTest";
				Calculate = Calculate.OnBarClose;
				BarsRequiredToTrade = 0;
				IsExitOnSessionCloseStrategy = false;
				TraceOrders = false;
			}
			else if (State == State.DataLoaded) { started = true; }
			else if (State == State.Terminated)
			{
				if (!started || done) return;
				done = true;
				try { RunAll(); }
				catch (Exception ex)
				{
					Print("NQC_ET3GA_ERROR " + ex.Message + " | " + ex.StackTrace);
					try
					{
						string d = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results");
						Directory.CreateDirectory(d);
						File.WriteAllText(Path.Combine(d, "ET3GA_ERROR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt"), ex.ToString());
					}
					catch { }
				}
			}
		}

		protected override void OnBarUpdate() { }

		private static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }
		private void Chk(string name, bool ok, string info)
		{
			if (ok) pass++; else fail++;
			rep.Append(ok ? "[PASS] " : "[FALLA] ").Append(name).Append(" | ").Append(info).Append("\n");
		}

		private static string Pop(List<NQCE3_Genome> pop, INQCE3_Library lib)
		{
			StringBuilder sb = new StringBuilder();
			foreach (NQCE3_Genome g in pop) sb.Append(g.CanonicalText(lib)).Append('\n');
			return NQCE_Const.Sha256Text(sb.ToString());
		}

		private static bool ValidGenome(NQCE3_Genome g, INQCE3_Library lib)
		{
			for (int sl = 0; sl < NQCE3_Genome.Slots; sl++)
			{
				int cc = g.G[2 * sl], pp = g.G[2 * sl + 1];
				if (cc != 0 && !lib.IsValidCondition(cc)) return false;
				if (cc == 0 && pp != 0) return false;
				if (cc != 0 && (pp < 0 || pp >= NQCE3_Genome.ParamRange(lib, cc))) return false;
			}
			return g.G[NQCE3_Genome.GAtr] >= NQCE3_Genome.AtrMin && g.G[NQCE3_Genome.GAtr] <= NQCE3_Genome.AtrMax
				&& g.G[NQCE3_Genome.GKsl] >= 0 && g.G[NQCE3_Genome.GKsl] < NQCE3_Genome.KslLevels && g.G[NQCE3_Genome.GKtp] >= 0 && g.G[NQCE3_Genome.GKtp] < NQCE3_Genome.KtpLevels
				&& (g.G[NQCE3_Genome.GDir] == 0 || g.G[NQCE3_Genome.GDir] == 1);
		}

		// Poblacion inicial determinista y fitness sintetico (determinista, sin evaluar nada real).
		private static List<NQCE3_Genome> InitPop(NQCE_Rng r, INQCE3_Library lib, NQCE3_Params p)
		{
			List<NQCE3_Genome> pop = new List<NQCE3_Genome>();
			for (int i = 0; i < p.PopSize; i++) pop.Add(NQCE3_Operators.InitWithMinSignal(r, lib, p, null));
			return pop;
		}
		private static double[] SynthFit(List<NQCE3_Genome> pop)
		{
			double[] f = new double[pop.Count];
			for (int i = 0; i < pop.Count; i++) f[i] = (i * 7919 % 1000) / 100.0 - 5.0;   // permutacion fija de valores distintos-ish; empates resueltos por indice
			return f;
		}

		private void RunAll()
		{
			rep.Append("NQC ET3 GaTest ").Append(ScriptVersion).Append(" | contrato ").Append(NQCE_Const.ContractVersion).Append(" | SOLO barras sinteticas\n\n");
			NQCE3_Library lib = new NQCE3_Library(); NQCE3_Params p = new NQCE3_Params();

			// ---- G1
			Chk("G1 parametros D-019: 500 x 200 = 100.000 <= tope, torneo 3, mutacion 5%, elite 5% = 25, inmigrantes 5% = 25, top-K 2.000, semilla C1 20261114",
				p.PopSize == 500 && p.Generations == 200 && p.PopSize * p.Generations <= NQCE3_Campaign.MaxEvaluations && p.Tournament == 3 && p.MutationRate == 0.05 && p.EliteFrac == 0.05 && p.ImmigrantFrac == 0.05
				&& NQCE3_Operators.EliteCount(p) == 25 && NQCE3_Operators.ImmigrantCount(p) == 25 && p.TopK == 2000 && p.Seed == 20261114L && NQCE3_Campaign.MaxEvaluations == 100000,
				"pop=" + I(p.PopSize) + " gen=" + I(p.Generations) + " elite=" + I(NQCE3_Operators.EliteCount(p)) + " inmigrantes=" + I(NQCE3_Operators.ImmigrantCount(p)));

			// ---- G2 tamano, elite, determinismo
			NQCE_Rng r0 = new NQCE_Rng(p.Seed); List<NQCE3_Genome> pop0 = InitPop(r0, lib, p); double[] fit0 = SynthFit(pop0);
			long state = 99991L;
			List<NQCE3_Genome> nA = NQCE3_Operators.NextGeneration(new NQCE_Rng(state), pop0, fit0, p, lib);
			List<NQCE3_Genome> nB = NQCE3_Operators.NextGeneration(new NQCE_Rng(state), pop0, fit0, p, lib);
			List<NQCE3_Genome> nC = NQCE3_Operators.NextGeneration(new NQCE_Rng(state + 1), pop0, fit0, p, lib);
			bool sizeOk = nA.Count == 500 && pop0.Count == 500;
			int[] rank = NQCE3_Operators.RankDesc(fit0); bool eliteOk = true;
			for (int i = 0; i < 25; i++) eliteOk &= nA[i].CanonicalText(lib) == pop0[rank[i]].CanonicalText(lib) && !object.ReferenceEquals(nA[i], pop0[rank[i]]);
			Chk("G2.1 NextGeneration: poblacion de exactamente 500, los 25 primeros son clones de los 25 mejores", sizeOk && eliteOk, "tamano=" + I(nA.Count));
			Chk("G2.2 determinismo: misma semilla => misma poblacion (hash); otra semilla => distinta", Pop(nA, lib) == Pop(nB, lib) && Pop(nA, lib) != Pop(nC, lib), "hash=" + Pop(nA, lib).Substring(0, 12));
			bool allValid = true; foreach (NQCE3_Genome g in nA) allValid &= ValidGenome(g, lib);
			Chk("G2.3 los 500 genomas de la nueva generacion son validos (ids activos, parametros en rango)", allValid, "ok");

			// ---- G3 inmigrantes
			NQCE_Rng rim = new NQCE_Rng(state); bool reprod = true; HashSet<string> imms = new HashSet<string>(); bool immValid = true;
			for (int i = 0; i < 25; i++)
			{
				NQCE3_Genome expect = NQCE3_Operators.InitWithMinSignal(rim, lib, p, null);
				reprod &= nA[25 + i].CanonicalText(lib) == expect.CanonicalText(lib); imms.Add(nA[25 + i].CanonicalText(lib)); immValid &= ValidGenome(nA[25 + i], lib);
			}
			HashSet<string> parents = new HashSet<string>(); foreach (NQCE3_Genome g in pop0) parents.Add(g.CanonicalText(lib));
			int clash = 0; for (int i = 25; i < 50; i++) if (parents.Contains(nA[i].CanonicalText(lib))) clash++;
			Chk("G3.1 los inmigrantes aparecen: posiciones 25..49, validos, reproducibles desde NQCE_Rng, 25 distintos entre si y ajenos a la poblacion anterior", reprod && immValid && imms.Count == 25 && clash == 0, "distintos=" + I(imms.Count) + " repetidos_con_padres=" + I(clash));
			NQCE3_Params p0 = new NQCE3_Params(); p0.ImmigrantFrac = 0.0;
			List<NQCE3_Genome> nZ = NQCE3_Operators.NextGeneration(new NQCE_Rng(state), pop0, fit0, p0, lib);
			int same = 0; for (int i = 25; i < 50; i++) if (nZ[i].CanonicalText(lib) == nA[i].CanonicalText(lib)) same++;
			Chk("G3.2 con inmigrantes 0% las posiciones 25..49 son hijos (difieren de los inmigrantes) y el tamano sigue siendo 500", nZ.Count == 500 && same == 0, "iguales=" + I(same));
			List<NQCE3_Genome> nAcc = NQCE3_Operators.NextGeneration(new NQCE_Rng(state), pop0, fit0, p, lib, delegate(NQCE3_Genome g) { return g.ActiveSlots() >= 2; });
			bool accOk = true; for (int i = 25; i < 50; i++) accOk &= nAcc[i].ActiveSlots() >= 1;
			Chk("G3.3 los inmigrantes respetan el filtro de senal minima cuando se entrega (mismo InitWithMinSignal)", nAcc.Count == 500 && accOk, "ok");

			// ---- G4 operadores
			NQCE_Rng r4 = new NQCE_Rng(777L); double[] ft = new double[] { 1, 5, 3, 9, 2, 8, 7, 4, 6, 0 }; int best = 0, wins = 0;
			for (int k = 0; k < 2000; k++) { int w = NQCE3_Operators.Tournament(r4, ft, 3); wins += ft[w] >= 3 ? 1 : 0; best += w == 3 ? 1 : 0; }
			Chk("G4.1 torneo de 3: el mejor de la poblacion gana ~27% (1-0.9^3) y los ganadores no son los peores", best > 450 && best < 650 && wins > 1500, "mejor=" + I(best) + "/2000");
			int fromA = 0, fromB = 0, other = 0; NQCE_Rng r5 = new NQCE_Rng(31L);
			for (int k = 0; k < 500; k++)
			{
				NQCE3_Genome a = NQCE3_Operators.RandomGenome(r5, lib, p), b = NQCE3_Operators.RandomGenome(r5, lib, p), c = NQCE3_Operators.Crossover(r5, a, b, lib);
				for (int gi = 16; gi < 20; gi++) { if (c.G[gi] == a.G[gi]) fromA++; else if (c.G[gi] == b.G[gi]) fromB++; else other++; }
			}
			Chk("G4.2 cruce uniforme: los genes de salida salen de uno de los dos padres y de ambos (~50/50)", other == 0 && fromA > 800 && fromB > 800, "A=" + I(fromA) + " B=" + I(fromB) + " otros=" + I(other));
			int changed = 0, tot = 0; NQCE_Rng r6 = new NQCE_Rng(5L);
			for (int k = 0; k < 3000; k++)
			{
				NQCE3_Genome g0 = NQCE3_Operators.RandomGenome(r6, lib, p); NQCE3_Genome g1 = g0.Clone(); NQCE3_Operators.Mutate(r6, g1, p, lib);
				if (!ValidGenome(g1, lib)) { changed = -1000000; break; }
				for (int gi = 16; gi < 20; gi++) { tot++; if (g0.G[gi] != g1.G[gi]) changed++; }
			}
			double rate = (double)changed / tot;
			Chk("G4.3 mutacion 5%/gen: tasa observada de cambio en los genes de salida entre 3% y 6,5% (con coincidencias por azar) y genomas validos", rate > 0.03 && rate < 0.065, "tasa=" + rate.ToString("0.0000", CultureInfo.InvariantCulture));

			// ---- G5 campana corta sobre M1 sintetica
			double[] vol; NQCE_Bars bars = NQCE3_SynthM1.Make(45, 4242L, out vol);
			string sha1, sha2; int fresh1, fresh2; int bank1, imm1;
			{
				NQCE3_Params pc = new NQCE3_Params(); pc.PopSize = 20; pc.Generations = 6; pc.TopK = 10; pc.Seed = 20261114;
				NQCE3_Library lc = new NQCE3_Library(); NQCE3_Fitness fit = new NQCE3_Fitness(bars, vol, new NQCE_Config(), lc);
				NQCE3_Campaign camp = new NQCE3_Campaign(); camp.Run(fit, lc, pc, null);
				sha1 = camp.Ledger.Sha256(); fresh1 = fit.FreshEvaluations; bank1 = camp.Bank.Top().Count; imm1 = NQCE3_Operators.ImmigrantCount(pc);
				NQCE3_Library lc2 = new NQCE3_Library(); NQCE3_Fitness fit2 = new NQCE3_Fitness(bars, vol, new NQCE_Config(), lc2);
				NQCE3_Campaign camp2 = new NQCE3_Campaign(); camp2.Run(fit2, lc2, pc, null);
				sha2 = camp2.Ledger.Sha256(); fresh2 = fit2.FreshEvaluations;
				Chk("G5.1 campana corta (pop 20 x 6 gen, inmigrantes 1/gen): misma semilla => mismo ledger (SHA256) y mismas evaluaciones", sha1 == sha2 && fresh1 == fresh2 && camp.Ledger.Rows == fresh1, "sha=" + sha1.Substring(0, 12) + " evaluaciones=" + I(fresh1) + " inmigrantes/gen=" + I(imm1));
				Chk("G5.2 evaluaciones nuevas <= pop x gen (elite repetida no cuenta, inmigrantes si)", fresh1 <= pc.PopSize * pc.Generations && fresh1 > pc.PopSize && bank1 <= 10, "evaluaciones=" + I(fresh1) + " top=" + I(bank1));
				// top-K deduplicado: 40 evaluaciones sinteticas (huellas repetidas, invalidas) con K=10
				NQCE3_Databank bk = new NQCE3_Databank(10);
				for (int q = 0; q < 40; q++)
				{
					NQCE3_Eval ev = new NQCE3_Eval(); ev.TrialId = "t" + q.ToString("D3", CultureInfo.InvariantCulture); ev.TradeFingerprint = "fp" + (q % 25).ToString(CultureInfo.InvariantCulture);
					ev.Fitness = (q * 37 % 40) / 10.0; ev.Trades = 150; ev.Valid = q % 7 != 0; ev.GenomeText = "g"; bk.Offer(ev);
				}
				List<NQCE3_Eval> top = bk.Top(); HashSet<string> fps = new HashSet<string>(); bool sorted = true, vOnly = true;
				for (int q = 0; q < top.Count; q++) { fps.Add(top[q].TradeFingerprint); vOnly &= top[q].Valid; if (q > 0 && top[q].Fitness > top[q - 1].Fitness) sorted = false; }
				Chk("G5.2b top-K deduplicado: 10 de 40 ensayos (huellas repetidas e invalidos), sin huellas duplicadas, solo validos, orden descendente", top.Count == 10 && fps.Count == 10 && sorted && vOnly, "top=" + I(top.Count) + " huellas=" + I(fps.Count));
				// tope duro: los inmigrantes cuentan como evaluaciones
				NQCE3_Library lc3 = new NQCE3_Library(); NQCE3_Fitness fit3 = new NQCE3_Fitness(bars, vol, new NQCE_Config(), lc3);
				NQCE3_Campaign camp3 = new NQCE3_Campaign(); camp3.MaxEvals = 45; camp3.Run(fit3, lc3, pc, null);
				Chk("G5.3 tope duro de evaluaciones (prueba con tope 45): la campana se detiene en el tope", fit3.FreshEvaluations <= 45 && fit3.FreshEvaluations >= 40, "evaluaciones=" + I(fit3.FreshEvaluations));
				// otra semilla => otro ledger
				NQCE3_Params pd = new NQCE3_Params(); pd.PopSize = 20; pd.Generations = 6; pd.TopK = 10; pd.Seed = 20261115;
				NQCE3_Library lc4 = new NQCE3_Library(); NQCE3_Fitness fit4 = new NQCE3_Fitness(bars, vol, new NQCE_Config(), lc4);
				NQCE3_Campaign camp4 = new NQCE3_Campaign(); camp4.Run(fit4, lc4, pd, null);
				Chk("G5.4 otra semilla => otro ledger", camp4.Ledger.Sha256() != sha1, "sha=" + camp4.Ledger.Sha256().Substring(0, 12));
			}

			string status = fail == 0 ? "PASS" : "FALLA";
			rep.Append("\nchecks_ok=" + I(pass) + " checks_falla=" + I(fail) + " status=" + status + "\n");
			rep.Append("NOTA: prueba tecnica con datos sinteticos; no mide ni valida ningun resultado economico.\n");
			string text = rep.ToString();
			string outDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results", "ET3GA_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
			Directory.CreateDirectory(outDir);
			UTF8Encoding enc = new UTF8Encoding(false);
			File.WriteAllText(Path.Combine(outDir, "GA_TEST_REPORT.txt"), text, enc);
			string hash = NQCE_Const.Sha256Text(text);
			StringBuilder su = new StringBuilder();
			su.Append("NQC ET3 GA TEST - SUMMARY\nscript_version : " + ScriptVersion + "\ncore_version   : " + NQCE_Const.CoreVersion + "\ncontract       : " + NQCE_Const.ContractVersion + "\n");
			su.Append("library        : " + lib.Version + "\nchecks_ok      : " + I(pass) + "\nchecks_fail    : " + I(fail) + "\n");
			su.Append("data           : SINTETICA (sin mercado real)\nstatus         : " + status + "\nreport_sha256  : " + hash + "\n");
			File.WriteAllText(Path.Combine(outDir, "SUMMARY.txt"), su.ToString(), enc);
			Print("NQC_ET3GA_END status=" + status + " ok=" + I(pass) + " falla=" + I(fail) + " sha=" + hash.Substring(0, 12) + " dir=" + outDir);
		}
	}

	// Medicion de costo (D-031(3)): tiempo y memoria de una evaluacion tipica de genoma sobre un IS sintetico del tamano real (~1,08 M barras M1).
	// No se ejecuta desde NT8. Uso: NQCE3_Bench.Run(780, 60, false) y Run(780, 60, true) (la segunda trata las '1' como '2', sin descomposicion).
	public static class NQCE3_Bench
	{
		public static string Run(int days, int genomes, bool noDecomposition)
		{
			StringBuilder sb = new StringBuilder();
			NQCE3_Catalog.DebugForceDirect = noDecomposition;
			Stopwatch sw = Stopwatch.StartNew();
			double[] vol; NQCE_Bars bars = NQCE3_SynthM1.Make(days, 777L, out vol);
			long memBars = GC.GetTotalMemory(true);
			sb.Append("barras=" + bars.Count.ToString(CultureInfo.InvariantCulture) + " dias=" + days.ToString(CultureInfo.InvariantCulture) + " decomposicion=" + (!noDecomposition) + "\n");
			NQCE3_Library lib = new NQCE3_Library(); NQCE3_Fitness fit = new NQCE3_Fitness(bars, vol, new NQCE_Config(), lib);
			sb.Append("construccion (serie+volumen+Bind): " + sw.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms\n");
			sw.Restart();
			for (int p = 5; p <= 100; p++) { double[] a = bars.Atr(p); if (a.Length == 0) throw new Exception(); }
			long memAtr = GC.GetTotalMemory(true) - memBars;
			sb.Append("96 series ATR de Wilder (5..100): " + sw.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms, +" + (memAtr / 1048576).ToString(CultureInfo.InvariantCulture) + " MB (ya las usa el ejecutor)\n");
			NQCE3_Params pr = new NQCE3_Params(); NQCE_Rng r = new NQCE_Rng(20261114L);
			List<double> ms = new List<double>(); int slotsTot = 0; double totalMs = 0;
			for (int i = 0; i < genomes; i++)
			{
				NQCE3_Genome g = NQCE3_Operators.RandomGenome(r, lib, pr);
				sw.Restart(); fit.Evaluate(g); double t = sw.Elapsed.TotalMilliseconds; ms.Add(t); totalMs += t; slotsTot += g.ActiveSlots();
			}
			ms.Sort();
			long memAll = GC.GetTotalMemory(true);
			sb.Append("evaluaciones de genomas aleatorios: " + genomes.ToString(CultureInfo.InvariantCulture) + " (condiciones activas por genoma: " + ((double)slotsTot / genomes).ToString("0.0", CultureInfo.InvariantCulture) + ")\n");
			sb.Append("tiempo por evaluacion: media " + (totalMs / genomes).ToString("0", CultureInfo.InvariantCulture) + " ms, mediana " + ms[ms.Count / 2].ToString("0", CultureInfo.InvariantCulture) + " ms, p90 " + ms[(int)(ms.Count * 0.9)].ToString("0", CultureInfo.InvariantCulture) + " ms, max " + ms[ms.Count - 1].ToString("0", CultureInfo.InvariantCulture) + " ms\n");
			sb.Append("memoria gestionada tras las evaluaciones: " + (memAll / 1048576).ToString(CultureInfo.InvariantCulture) + " MB (barras+volumen ~" + (memBars / 1048576).ToString(CultureInfo.InvariantCulture) + " MB; ATR +" + (memAtr / 1048576).ToString(CultureInfo.InvariantCulture) + " MB; cache de caracteristicas y demas +" + ((memAll - memBars - memAtr) / 1048576).ToString(CultureInfo.InvariantCulture) + " MB)\n");
			NQCE3_Catalog.DebugForceDirect = false;
			return sb.ToString();
		}

		// Campana corta con los parametros reales de D-019 (elite, inmigrantes, mutacion) sobre el IS sintetico: costo medio por evaluacion nueva con la cache en regimen.
		public static string RunCampaign(int days, int pop, int gens, bool noDecomposition)
		{
			NQCE3_Catalog.DebugForceDirect = noDecomposition;
			double[] vol; NQCE_Bars bars = NQCE3_SynthM1.Make(days, 777L, out vol);
			NQCE3_Library lib = new NQCE3_Library(); NQCE3_Fitness fit = new NQCE3_Fitness(bars, vol, new NQCE_Config(), lib);
			NQCE3_Params pr = new NQCE3_Params(); pr.PopSize = pop; pr.Generations = gens;
			Stopwatch sw = Stopwatch.StartNew(); NQCE3_Campaign camp = new NQCE3_Campaign(); camp.Run(fit, lib, pr, null); sw.Stop();
			long mem = GC.GetTotalMemory(true);
			NQCE3_Catalog.DebugForceDirect = false;
			return "campana pop=" + pop + " gen=" + gens + " decomposicion=" + (!noDecomposition) + ": evaluaciones nuevas=" + fit.FreshEvaluations + " tiempo=" + (sw.ElapsedMilliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)
				+ " s, " + ((double)sw.ElapsedMilliseconds / Math.Max(1, fit.FreshEvaluations)).ToString("0", CultureInfo.InvariantCulture) + " ms/evaluacion, memoria gestionada " + (mem / 1048576) + " MB, cache de la serie=" + lib.SeriesFor(bars).MemoCount + " entradas\n";
		}
	}
}
