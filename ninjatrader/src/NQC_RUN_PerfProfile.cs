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
// NQC_RUN_PerfProfile - PERFIL de coste de la evaluacion de genomas (NQCRUN-1.3.0). SIN ordenes; SOLO un mundo sintetico de ~1.000.000 barras M1 (NQCE3_SynthM1) con la biblioteca real NQCL;
// NO calcula resultados economicos de mercado (solo tiempos, conteos de cache y GC). Requiere NQC_ET1_Core, NQC_ET1C_AccountSim, NQC_ET3_* (incluye NQC_ET3_GaTest para NQCE3_SynthM1).
// Salida: tabla de coste por componente (ATR, numerador por familia y periodo, ejecutor, aciertos/fallos de cache, GC) y origen de la cola larga. Un solo hilo; no es el rendimiento de NT8.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public class NQC_RUN_PerfProfile : Strategy
	{
		private const string ScriptVersion = "RUNPERFPROFILE-1.0.0";
		private bool started, done; public string ReportText = "";
		public static int Days = 780, Genomes = 500;       // 780 dias x 1380 barras = 1.076.400 barras M1

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults) { Description = "Perfil de coste de la evaluacion de genomas (mundo sintetico, sin ordenes)."; Name = "NQC_RUN_PerfProfile"; Calculate = Calculate.OnBarClose; BarsRequiredToTrade = 0; IsExitOnSessionCloseStrategy = false; TraceOrders = false; }
			else if (State == State.DataLoaded) started = true;
			else if (State == State.Terminated)
			{
				if (!started || done) return; done = true;
				try { ReportText = RunAll(); Print(ReportText); try { string d = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results", "RUNPERF_PROFILE_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)); Directory.CreateDirectory(d); File.WriteAllText(Path.Combine(d, "PROFILE.txt"), ReportText, new UTF8Encoding(false)); } catch { } }
				catch (Exception ex) { Print("NQC_PERFPROFILE_ERROR " + ex.Message + " | " + ex.StackTrace); }
			}
		}
		protected override void OnBarUpdate() { }

		private static string I(long v) { return v.ToString(CultureInfo.InvariantCulture); }
		private static string F(double v, string f) { return v.ToString(f, CultureInfo.InvariantCulture); }
		private static double Ms(long t0) { return (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency; }
		private static double Pct(List<double> s, double q) { int n = s.Count; int i = (int)Math.Ceiling(q * n) - 1; if (i < 0) i = 0; if (i >= n) i = n - 1; return s[i]; }

		private string RunAll()
		{
			StringBuilder o = new StringBuilder(); int cores = Environment.ProcessorCount;
			o.Append("NQC RUN PerfProfile ").Append(ScriptVersion).Append(" | runner ").Append(NQCRUN_Funnel.Version).Append(" | SOLO datos sinteticos, un hilo; NO es el rendimiento de NT8 ni resultados de mercado\n");
			NQCE3_Series.MemoBudgetBytes = 2048L * 1024 * 1024; NQCE3_Series.MemoReserveEntries = 0;
			double[] vol; long t0 = Stopwatch.GetTimestamp(); NQCE_Bars bars = NQCE3_SynthM1.Make(Days, 777L, out vol); double msMake = Ms(t0);
			o.Append("mundo: barras=").Append(I(bars.Count)).Append(" dias=").Append(I(Days)).Append(" (construccion ").Append(F(msMake, "0")).Append(" ms)\n");
			NQCE3_Library lib = new NQCE3_Library();
			NQCE3_Fitness fit = new NQCE3_Fitness(bars, vol, new NQCE_Config(), lib); fit.FreshEvalMs = new List<double>();
			NQCE3_Series ser = lib.SeriesFor(bars);

			// 1. ATR de Wilder (96 series, las mismas que usa el ejecutor)
			t0 = Stopwatch.GetTimestamp(); for (int p = NQCE3_Genome.AtrMin; p <= NQCE3_Genome.AtrMax; p++) bars.Atr(p); double msAtr = Ms(t0);
			o.Append("\n[1] ATR de Wilder: 96 series (periodos 5..100) = ").Append(F(msAtr, "0")).Append(" ms en total (").Append(F(msAtr / 96, "0.0")).Append(" ms por serie; ").Append(F(8.0 * bars.Count / 1048576, "0.0")).Append(" MB cada una). Se paga UNA vez por corrida, no por evaluacion.\n");

			// 2. evaluacion en frio (cache de numeradores vacia) de N genomas aleatorios + GC
			NQCE3_Params gp = new NQCE3_Params(); NQCE_Rng rng = new NQCE_Rng(20269321L); List<NQCE3_Genome> gs = new List<NQCE3_Genome>();
			for (int i = 0; i < Genomes; i++) gs.Add(NQCE3_Operators.RandomGenome(rng, lib, gp));
			int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2); long mem0 = GC.GetTotalMemory(true);
			List<double> cold = new List<double>(); List<string> txt = new List<string>(); List<int> miss = new List<int>();
			Stopwatch swAll = Stopwatch.StartNew();
			for (int i = 0; i < gs.Count; i++)
			{
				int m0 = ser.MemoCount; long a = Stopwatch.GetTimestamp(); NQCE3_Eval e = fit.Evaluate(gs[i]); cold.Add(Ms(a)); txt.Add(e.GenomeText); miss.Add(ser.MemoCount - m0);
			}
			swAll.Stop(); int d0 = GC.CollectionCount(0) - g0, d1 = GC.CollectionCount(1) - g1, d2 = GC.CollectionCount(2) - g2; long mem1 = GC.GetTotalMemory(false);
			// 3. la misma lista con cache de numeradores CALIENTE (otro NQCE3_Fitness: sin cache de genomas): ejecutor + lectura de cache + construccion
			// segunda pasada con la MISMA serie (no se re-enlaza: Bind descartaria la cache de numeradores) y la cache de GENOMAS vaciada por reflexion; solo para este perfil
			NQCE3_Fitness fit2 = fit; fit.FreshEvalMs = new List<double>(); System.Reflection.FieldInfo gcf = typeof(NQCE3_Fitness).GetField("cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			((System.Collections.IDictionary)gcf.GetValue(fit)).Clear();
			int h0 = GC.CollectionCount(0); Stopwatch sw2 = Stopwatch.StartNew(); for (int i = 0; i < gs.Count; i++) fit2.Evaluate(gs[i]); sw2.Stop(); int dh0 = GC.CollectionCount(0) - h0;
			List<double> warm = new List<double>(fit2.FreshEvalMs); double sumCold = 0, sumWarm = 0; foreach (double x in cold) sumCold += x; foreach (double x in warm) sumWarm += x;
			List<double> cs = new List<double>(cold); cs.Sort(); List<double> ws = new List<double>(warm); ws.Sort();
			o.Append("\n[2] evaluacion de ").Append(I(Genomes)).Append(" genomas aleatorios, un hilo, cache de numeradores presupuesto 2048 MB\n");
			o.Append("  EN FRIO  (cache vacia, la parte inicial de una corrida): media=").Append(F(sumCold / cold.Count, "0.0")).Append(" mediana=").Append(F(Pct(cs, 0.5), "0.0")).Append(" p90=").Append(F(Pct(cs, 0.9), "0.0")).Append(" p99=").Append(F(Pct(cs, 0.99), "0.0")).Append(" max=").Append(F(cs[cs.Count - 1], "0.0")).Append(" ms\n");
			o.Append("  EN CALIENTE (mismos genomas, cache llena): media=").Append(F(sumWarm / warm.Count, "0.0")).Append(" mediana=").Append(F(Pct(ws, 0.5), "0.0")).Append(" p90=").Append(F(Pct(ws, 0.9), "0.0")).Append(" max=").Append(F(ws[ws.Count - 1], "0.0")).Append(" ms  (= ejecutor + lectura de cache + miembro/hash)\n");
			o.Append("  desglose de la media en frio: ejecutor+lectura+hash ~ ").Append(F(sumWarm / warm.Count, "0.0")).Append(" ms (").Append(F(100.0 * sumWarm / sumCold, "0")).Append("%), CALCULO DE NUMERADORES (fallos de cache) ~ ").Append(F((sumCold - sumWarm) / cold.Count, "0.0")).Append(" ms (").Append(F(100.0 * (sumCold - sumWarm) / sumCold, "0")).Append("%)\n");
			o.Append("  GC en la pasada fria: gen0=").Append(I(d0)).Append(" gen1=").Append(I(d1)).Append(" gen2=").Append(I(d2)).Append("; en la pasada caliente gen0=").Append(I(dh0)).Append(" (").Append(F((double)dh0 / Genomes, "0.00")).Append(" por evaluacion). Memoria gestionada ").Append(F(mem0 / 1048576.0, "0")).Append(" -> ").Append(F(mem1 / 1048576.0, "0")).Append(" MB; entradas de cache=").Append(I(ser.MemoCount)).Append(" (").Append(F(ser.MemoBytes / 1048576.0, "0")).Append(" MB)\n");
			int zeroT = 0; foreach (double x in warm) if (x < 1.0) zeroT++;
			o.Append("  evaluaciones calientes < 1 ms (sin condiciones activas/sin operaciones): ").Append(I(zeroT)).Append(" de ").Append(I(Genomes)).Append("\n");

			// 4. coste de un numerador por familia y periodo (ruta directa, sin cache) sobre la ventana: ms por barra x barras de ventana
			List<int> win = new List<int>(); for (int i = 0; i < bars.Count; i++) if (bars.Stamp[i] >= 567 && bars.Stamp[i] <= 692 && bars.DayKey[i] >= 20200301) win.Add(i);
			int stride = Math.Max(1, win.Count / 400); List<int> sample = new List<int>(); for (int k = 0; k < win.Count; k += stride) sample.Add(win[k]);
			int[] ns = new int[] { 5, 15, 30 }; List<KeyValuePair<double, string>> fam = new List<KeyValuePair<double, string>>(); double[] tot = new double[ns.Length]; double winBars = win.Count;
			for (int f = 0; f < NQCE3_Catalog.Families; f++)
			{
				if (!NQCE3_Catalog.IsActiveFamily(f)) continue; char kd = NQCE3_Catalog.AtrKindOf(f); double[] c = new double[ns.Length];
				for (int q = 0; q < ns.Length; q++)
				{
					int n = ns[q]; long a = Stopwatch.GetTimestamp(); int cnt = 0;
					foreach (int at in sample) { if (at - (7 * n + 10) < 0) continue; ser.Direct(f, n, 14, at, kd != '2'); cnt++; }
					c[q] = cnt == 0 ? 0 : Ms(a) / cnt * winBars; tot[q] += c[q];
				}
				fam.Add(new KeyValuePair<double, string>(c[2], f.ToString(CultureInfo.InvariantCulture) + "\t" + kd + "\t" + F(c[0], "0") + "\t" + F(c[1], "0") + "\t" + F(c[2], "0")));
			}
			fam.Sort(delegate(KeyValuePair<double, string> x, KeyValuePair<double, string> y) { return y.Key.CompareTo(x.Key); });
			o.Append("\n[3] coste de LLENAR un numerador completo (toda la ventana 09:27-11:32 del IS = ").Append(I(win.Count)).Append(" barras) por familia y periodo n; ruta directa (extrapolada de ").Append(I(sample.Count)).Append(" barras)\n");
			o.Append("  suma de las ").Append(I(fam.Count)).Append(" familias activas: n=5 ").Append(F(tot[0] / 1000, "0.0")).Append(" s | n=15 ").Append(F(tot[1] / 1000, "0.0")).Append(" s | n=30 ").Append(F(tot[2] / 1000, "0.0")).Append(" s  (media por familia n=15: ").Append(F(tot[1] / fam.Count, "0")).Append(" ms)\n");
			o.Append("  las 15 familias MAS CARAS (ms para llenar 1 numerador):\n  fam\tkind\tn=5\tn=15\tn=30\n");
			for (int i = 0; i < Math.Min(15, fam.Count); i++) o.Append("  ").Append(fam[i].Value).Append('\n');
			double top15 = 0; for (int i = 0; i < Math.Min(15, fam.Count); i++) top15 += fam[i].Key; double allf = tot[2];
			o.Append("  las 15 mas caras suponen el ").Append(F(100.0 * top15 / allf, "0")).Append("% del coste de llenar un numerador de cada familia con n=30\n");
			// numeradores distintos pedidos por los genomas: familias y (familia,n)
			HashSet<string> keys = new HashSet<string>(); foreach (string t in txt) foreach (string c in t.Split(new char[] { ' ', ';', '|' })) if (c.Contains("f=")) keys.Add(c);
			// 5. cola larga: las 10 evaluaciones mas lentas en frio con sus condiciones
			List<int> idx = new List<int>(); for (int i = 0; i < cold.Count; i++) idx.Add(i); idx.Sort(delegate(int a, int b) { return cold[b].CompareTo(cold[a]); });
			o.Append("\n[4] COLA LARGA: las 10 evaluaciones mas lentas en frio (ms frio / ms caliente / entradas nuevas de cache):\n");
			for (int k = 0; k < 10 && k < idx.Count; k++) { int i = idx[k]; string t = txt[i]; if (t.Length > 150) t = t.Substring(0, 150) + "..."; o.Append("  ").Append(F(cold[i], "0")).Append(" / ").Append(F(warm[i], "0")).Append(" / ").Append(I(miss[i])).Append("  ").Append(t).Append('\n'); }
			double sumTop = 0; for (int k = 0; k < idx.Count / 20; k++) sumTop += cold[idx[k]];
			o.Append("  el 5% mas lento de las evaluaciones suma el ").Append(F(100.0 * sumTop / sumCold, "0")).Append("% del tiempo total en frio; las evaluaciones con 0 entradas nuevas de cache cuestan en media ").Append(F(AvgIf(cold, miss, true), "0.0")).Append(" ms frente a ").Append(F(AvgIf(cold, miss, false), "0.0")).Append(" ms las que llenan al menos un numerador\n");
			// 6. efecto del presupuesto de cache (fallos de cache): misma lista con 256 MB
			NQCE3_Series.MemoBudgetBytes = 256L * 1024 * 1024; NQCE3_Fitness fit3 = new NQCE3_Fitness(bars, vol, new NQCE_Config(), new NQCE3_Library()); fit3.FreshEvalMs = new List<double>();
			Stopwatch sw3 = Stopwatch.StartNew(); for (int i = 0; i < gs.Count; i++) fit3.Evaluate(gs[i]); sw3.Stop();
			o.Append("\n[5] presupuesto de cache: la misma lista en frio con 256 MB tarda ").Append(F(sw3.Elapsed.TotalSeconds, "0.0")).Append(" s frente a ").Append(F(swAll.Elapsed.TotalSeconds, "0.0")).Append(" s con 2048 MB (").Append(F(100.0 * sw3.Elapsed.TotalSeconds / swAll.Elapsed.TotalSeconds, "0")).Append("%)\n");
			o.Append("\nnucleos logicos=").Append(I(cores)).Append("\n");
			return o.ToString();
		}
		private static double AvgIf(List<double> v, List<int> m, bool zero) { double s = 0; int n = 0; for (int i = 0; i < v.Count; i++) if ((m[i] == 0) == zero) { s += v[i]; n++; } return n == 0 ? 0 : s / n; }
	}
}
