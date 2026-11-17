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

		// ---------------- D-038: N_eff desde MUESTRA (Chao1). Referencias independientes: ninjatrader/runbooks/ET7_NEFF_REF.py (Python, splitmix64 + Chao1 + IC propios).
		static int[] World(int G, int floor, double expo, int N)  // abundancias poblacionales: floor + reparto proporcional a g^-expo; el resto al grupo 1
		{
			double[] w = new double[G]; double sw = 0; for (int g = 0; g < G; g++) { w[g] = Math.Pow(g + 1, -expo); sw += w[g]; }
			int[] a = new int[G]; int sum = 0; for (int g = 0; g < G; g++) { a[g] = floor + (int)(w[g] / sw * (N - floor * G)); sum += a[g]; }
			a[0] += N - sum; return a;
		}
		static int[] SampleLabels(int[] ab, int k, long seed)   // muestra uniforme sin reemplazo: Fisher-Yates parcial con NQCE_Rng
		{
			int N = 0; foreach (int c in ab) N += c; int[] lab = new int[N]; int p = 0; for (int g = 0; g < ab.Length; g++) for (int j = 0; j < ab[g]; j++) lab[p++] = g;
			NQCE_Rng r = new NQCE_Rng(seed);
			for (int i = 0; i < k; i++) { int j = i + r.NextInt(N - i); int t = lab[i]; lab[i] = lab[j]; lab[j] = t; }
			int[] o = new int[k]; Array.Copy(lab, o, k); return o;
		}
		static int[] Counts(int[] sampleLab, int G) { int[] c = new int[G]; foreach (int g in sampleLab) c[g]++; List<int> l = new List<int>(); foreach (int v in c) if (v > 0) l.Add(v); return l.ToArray(); }
		static int[] NQCE7_Slice(int[] a, int k) { int[] o = new int[k]; Array.Copy(a, o, k); return o; }
		static string Q(double x) { return ((long)Math.Floor(x * 10000 + 0.5)).ToString(CultureInfo.InvariantCulture); }   // redondeo sin empates ambiguos (igual en Python)
		static string Pad(NQCE7_NeffResult e) { return e.GruposObservados + "," + e.F1 + "," + e.F2 + "," + e.NEffEstimado.ToString("0.0000", CultureInfo.InvariantCulture) + "," + e.Inferior.ToString("0.0000", CultureInfo.InvariantCulture) + "," + e.Superior.ToString("0.0000", CultureInfo.InvariantCulture); }
		static string Scenario(int G, int floor, double expo, int N, int k, int reps, long seed0, out int cover, out double meanObs, out double meanEst, out int sumPop)
		{
			int[] ab = World(G, floor, expo, N); sumPop = 0; foreach (int c in ab) sumPop += c; cover = 0; meanObs = 0; meanEst = 0; StringBuilder sb = new StringBuilder();
			for (int r = 0; r < reps; r++)
			{
				NQCE7_NeffResult e = NQCE7_Neff.EstimateFromAbundances(Counts(SampleLabels(ab, k, seed0 + r), G), N);
				if (e.Falla.Length == 0 && e.Inferior <= G && G <= e.Superior) cover++;
				meanObs += e.GruposObservados; meanEst += e.NEffEstimado; sb.Append(e.GruposObservados + "," + e.F1 + "," + e.F2 + "," + Q(e.NEffEstimado) + "," + Q(e.Inferior) + "," + Q(e.Superior) + ";");
			}
			meanObs /= reps; meanEst /= reps; return NQCE_Const.Sha256Text(sb.ToString());
		}

		// ===== Equivalencia y carga del N_eff (optimizacion de rendimiento: matriz de distancias por bloques/paralela, sin cambiar resultados) =====
		private static ulong rs;
		private static double U() { rs ^= rs << 13; rs ^= rs >> 7; rs ^= rs << 17; return (rs >> 11) * (1.0 / 9007199254740992.0); }
		private static double Gs() { double a = 1.0 - U(), b = U(); return Math.Sqrt(-2.0 * Math.Log(a)) * Math.Cos(2.0 * Math.PI * b); }
		// kind: mix (grupos de ~5 miembros + 1/3 ruido), corr (20 grupos muy correlados), ties (series enteras duplicadas -> distancias exactamente iguales),
		// special (planas, T corto no aplica aqui, duplicados, anticorrelacion).
		private static double[][] GenSeries(int n, int T, string kind, ulong seed)
		{
			rs = seed * 2654435761UL + 88172645463325252UL; double[][] s = new double[n][];
			int G = kind == "corr" ? 20 : Math.Max(2, n / 5); double rho = kind == "corr" ? 0.9 : 0.85;
			double[][] bs = new double[G][]; bool[][] mk = new bool[G][];
			for (int g = 0; g < G; g++) { bs[g] = new double[T]; mk[g] = new bool[T]; for (int t = 0; t < T; t++) { mk[g][t] = U() < 0.35; bs[g][t] = Gs(); } }
			for (int i = 0; i < n; i++)
			{
				s[i] = new double[T]; int g = (int)(U() * G);
				if (kind == "ties") { for (int t = 0; t < T; t++) s[i][t] = Math.Floor(bs[g][t] * 2.0); if (i % 7 == 3 && i > 0) s[i] = (double[])s[i - 1].Clone(); continue; }
				if (kind == "special" && i % 11 == 0) { for (int t = 0; t < T; t++) s[i][t] = 0; continue; }
				if (kind == "special" && i % 11 == 5 && i > 0) { for (int t = 0; t < T; t++) s[i][t] = -s[i - 1][t]; continue; }
				bool noise = kind == "mix" && i % 3 == 0; double w = noise ? 0 : Math.Sqrt(rho), u = Math.Sqrt(1 - w * w);
				for (int t = 0; t < T; t++) s[i][t] = (noise ? (U() < 0.35 ? 1 : 0) : (mk[g][t] ? 1 : 0)) * (w * bs[g][t] + u * Gs()) * 100.0;
			}
			return s;
		}
		private static bool SameNEff(NQCE7_NEffResult a, NQCE7_NEffResult b)
		{
			if (a.Fail != b.Fail || a.Clusters != b.Clusters || a.NSeries != b.NSeries || a.Coverage != b.Coverage) return false;
			if (!a.NEff.Equals(b.NEff) || !a.SrVar.Equals(b.SrVar)) return false;
			if ((a.Label == null) != (b.Label == null)) return false;
			if (a.Label != null) { if (a.Label.Length != b.Label.Length) return false; for (int i = 0; i < a.Label.Length; i++) if (a.Label[i] != b.Label[i]) return false; }
			return true;
		}
		private void RunNeffPerfTests()
		{
			int eq = 0, tot = 0; string bad = "";
			string[] kinds = { "mix", "corr", "ties", "special" }; int[] ns = { 2, 3, 33, 64, 150, 400 }; double[] cuts = { 0.20, 0.40, 0.60 }; int[] ths = { 1, 3, 8 };
			int saveTh = NQCE7_NEff.Threads;
			foreach (string kd in kinds) foreach (int n in ns) foreach (double cut in cuts)
			{
				double[][] s = GenSeries(n, 120, kd, (ulong)(n * 31 + (int)(cut * 100) + kd.Length));
				NQCE7_NEffResult o = OldNEff.Compute(s, n + 5, cut, 0.0, int.MaxValue);
				foreach (int th in ths) { NQCE7_NEff.Threads = th; NQCE7_NEffResult nw = NQCE7_NEff.Compute(s, n + 5, cut, 0.0, int.MaxValue); tot++; if (SameNEff(o, nw)) eq++; else if (bad.Length < 120) bad += kd + "/" + n + "/" + cut + "/th" + th + " "; }
			}
			NQCE7_NEff.Threads = saveTh;
			Chk("Q1 equivalencia del agrupamiento nuevo vs el anterior (copiado en la prueba): " + tot + " casos (4 tipos x 6 tamanos n<=400 x 3 cortes x hilos 1/3/8): grupos, etiquetas, N_eff y SrVar IDENTICOS", eq == tot, eq + "/" + tot + " " + bad);
			// matriz de distancias bit a bit contra Dist(Corr) ingenuo, con series planas, duplicadas, anticorrelacionadas y T = 2 (Corr = NaN -> d = 1)
			bool dbit = true; int cells = 0;
			foreach (int T in new int[] { 2, 5, 120 })
			{
				double[][] s = GenSeries(70, T, "special", (ulong)(900 + T)); NQCE7_NEff.Threads = 3;
				double[,] D = NQCE7_NEff.DistMatrix(s, 70, T, 3);
				for (int i = 0; i < 70; i++) for (int j = i + 1; j < 70; j++) { cells++; double e = NQCE7_NEff.Dist(NQCE7_NEff.Corr(s[i], s[j])); if (!D[i, j].Equals(e) || !D[j, i].Equals(e)) dbit = false; }
			}
			NQCE7_NEff.Threads = saveTh;
			Chk("Q2 matriz de distancias: " + cells + " celdas IDENTICAS bit a bit a Dist(Corr(x,y)) (incluye planas, duplicadas, anticorrelacion y T=2 -> NaN -> d=1)", dbit, "");
			// ruta SAMPLE_CHAO1: el nuevo EstimateFromSample (grupos, f1, f2, IC) coincide con Chao1 sobre los grupos del agrupamiento anterior
			double[][] sm = GenSeries(400, 200, "mix", 77); sm[3] = new double[200]; sm[9] = new double[200]; sm[200] = new double[200];
			NQCE7_NeffResult en = NQCE7_Neff.EstimateFromSample(sm, 100000, 0.40);
			List<double[]> act = new List<double[]>(); foreach (double[] x in sm) { double mm = 0; foreach (double v in x) mm += v; bool flat = true; foreach (double v in x) if (v != x[0]) flat = false; if (!flat) act.Add(x); }
			NQCE7_NEffResult oo = OldNEff.Compute(act.ToArray(), act.Count, 0.40, 1.0, int.MaxValue);
			int[] cnt = new int[oo.Clusters + 1]; foreach (int g in oo.Label) cnt[g]++; cnt[oo.Clusters] = 3;
			NQCE7_NeffResult eo = NQCE7_Neff.EstimateFromAbundances(cnt, 100000);
			Chk("Q3 EstimateFromSample con el agrupamiento nuevo == Chao1 sobre las abundancias del agrupamiento anterior (S_obs, f1, f2, S_est, IC, SrVar)", en.Falla.Length == 0 && en.GruposObservados == eo.GruposObservados && en.F1 == eo.F1 && en.F2 == eo.F2 && en.NEffEstimado.Equals(eo.NEffEstimado) && en.Inferior.Equals(eo.Inferior) && en.Superior.Equals(eo.Superior) && en.SrVar.Equals(oo.SrVar), "S_obs=" + en.GruposObservados + " f1=" + en.F1 + " f2=" + en.F2);
			// carga: n = 5.000 series x 933 sesiones (lo del paso real de SAMPLE_CHAO1); mide tiempo y memoria (informativo; cota holgada)
			double[][] big = GenSeries(5000, 933, "mix", 5000);
			long m0 = GC.GetTotalMemory(true); System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			NQCE7_NeffResult eb = NQCE7_Neff.EstimateFromSample(big, 150000, 0.40); sw.Stop();
			double mb = (GC.GetTotalMemory(false) - m0) / 1048576.0;
			Print("[CARGA Q4] n=5000 x 933: tiempo " + sw.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s, hilos " + (saveTh <= 0 ? Math.Min(Environment.ProcessorCount, 16) : saveTh) + ", memoria gestionada ~" + mb.ToString("0", CultureInfo.InvariantCulture) + " MB (informativo; NO entra en el informe para que sea determinista)");
			Chk("Q4 carga n=5000 x 933 sesiones (SAMPLE_CHAO1): termina sin falla, grupos > 1000 y < 120 s (el tiempo y la memoria se imprimen aparte)", eb.Falla.Length == 0 && eb.GruposObservados > 1000 && sw.Elapsed.TotalSeconds < 120.0, "S_obs=" + eb.GruposObservados);
		}

		// Copia del agrupamiento ANTERIOR (implementacion ingenua de referencia: matriz con Corr por par, cache de vecino mas cercano); NO usar en produccion.
		private static class OldNEff
		{
			private static double Corr(double[] x, double[] y) { return NQCE7_NEff.Corr(x, y); }
			private static double Dist(double r) { return NQCE7_NEff.Dist(r); }
			private static double DailySharpe(double[] x) { return NQCE7_NEff.DailySharpe(x); }
			public static NQCE7_NEffResult Compute(double[][] series, int nGross, double cut, double coverageMin, int maxSeries)
			{
				NQCE7_NEffResult R = new NQCE7_NEffResult(); R.NGross = nGross; R.Cut = cut; R.NEff = double.NaN; R.SrVar = double.NaN;
				int n = series == null ? 0 : series.Length; R.NSeries = n;
				if (n == 0) { R.Fail = "sin series de ensayos"; return R; }
				if (nGross < n) { R.Fail = "N bruto (" + nGross.ToString(CultureInfo.InvariantCulture) + ") menor que el numero de series"; return R; }
				if (n > maxSeries) { R.Fail = "series (" + n.ToString(CultureInfo.InvariantCulture) + ") superan MaxSeries (" + maxSeries.ToString(CultureInfo.InvariantCulture) + "): reducir antes"; return R; }
				if (double.IsNaN(cut) || cut <= 0 || cut >= 1) { R.Fail = "corte invalido"; return R; }
				int T = series[0].Length; for (int i = 0; i < n; i++) if (series[i].Length != T) { R.Fail = "series de longitud distinta"; return R; }
				R.Coverage = Math.Min(1.0, (double)n / nGross);
				if (R.Coverage < coverageMin) { R.Fail = "cobertura de series " + R.Coverage.ToString("0.0000", CultureInfo.InvariantCulture) + " < minimo " + coverageMin.ToString("0.0000", CultureInfo.InvariantCulture); return R; }

				double[,] D = new double[n, n];
				for (int i = 0; i < n; i++) for (int j = i + 1; j < n; j++) { double d = Dist(Corr(series[i], series[j])); D[i, j] = d; D[j, i] = d; }
				int[] size = new int[n]; bool[] act = new bool[n]; int[] rep = new int[n]; int[] label = new int[n];
				for (int i = 0; i < n; i++) { size[i] = 1; act[i] = true; label[i] = i; }
				int[] nn = new int[n]; double[] nd = new double[n];
				for (int i = 0; i < n; i++) Near(D, act, n, i, nn, nd);
				int clusters = n;
				while (clusters > 1)
				{
					int bi = -1; double bd = double.MaxValue;
					for (int i = 0; i < n; i++) if (act[i] && nn[i] >= 0 && (nd[i] < bd || (nd[i] == bd && i < bi))) { bd = nd[i]; bi = i; }
					if (bi < 0 || bd > cut) break;
					int bj = nn[bi]; int a = Math.Min(bi, bj), b = Math.Max(bi, bj); // a absorbe a b
					for (int k = 0; k < n; k++) if (act[k] && k != a && k != b)
					{
						double nv = (size[a] * D[a, k] + size[b] * D[b, k]) / (size[a] + size[b]);
						D[a, k] = nv; D[k, a] = nv;
					}
					size[a] += size[b]; act[b] = false; clusters--;
					for (int i = 0; i < n; i++) if (label[i] == b) label[i] = a;
					for (int i = 0; i < n; i++) if (act[i] && (i == a || nn[i] == a || nn[i] == b || nd[i] > D[i, a])) Near(D, act, n, i, nn, nd);
				}
				R.Clusters = clusters;
				Dictionary<int, int> map = new Dictionary<int, int>(); R.Label = new int[n];
				for (int i = 0; i < n; i++) { int g; if (!map.TryGetValue(label[i], out g)) { g = map.Count; map[label[i]] = g; rep[g] = i; } R.Label[i] = g; }
				R.NEff = Math.Max(1.0, clusters / R.Coverage);
				List<double> srs = new List<double>();
				for (int g = 0; g < clusters; g++) { double s = DailySharpe(series[rep[g]]); if (!double.IsNaN(s)) srs.Add(s); }
				if (srs.Count >= 2) { double m = 0; foreach (double s in srs) m += s; m /= srs.Count; double v = 0; foreach (double s in srs) v += (s - m) * (s - m); R.SrVar = v / (srs.Count - 1); }
				else R.SrVar = 0;
				return R;
			}
			private static void Near(double[,] D, bool[] act, int n, int i, int[] nn, double[] nd)
			{
				nn[i] = -1; nd[i] = double.MaxValue;
				for (int k = 0; k < n; k++) if (act[k] && k != i && D[i, k] < nd[i]) { nd[i] = D[i, k]; nn[i] = k; }
			}
		}

		private void RunNeffTests()
		{
			// ---- casos puntuales (referencia Python ref_neff.py)
			int[] c1 = new int[28]; for (int i = 0; i < 28; i++) c1[i] = i < 5 ? 1 : (i < 8 ? 2 : 10);
			NQCE7_NeffResult e1 = NQCE7_Neff.EstimateFromAbundances(c1, 5000);
			Chk("S1 Chao1 puntual vs Python (S_obs 28, f1 5, f2 3 -> S_est 30,5; IC [28,368; 44,982]; cobertura GT 0,9763)", e1.Falla == "" && e1.GruposObservados == 28 && e1.F1 == 5 && e1.F2 == 3 && Near(e1.NEffEstimado, 30.5, 1e-12) && Near(e1.Inferior, 28.36803425120234, 1e-9) && Near(e1.Superior, 44.98211506016558, 1e-9) && Near(e1.CoberturaMuestra, 0.976303317535545, 1e-12) && Near(e1.VarChao, 10.0, 1e-12), Pad(e1));
			int[] c2 = new int[10]; for (int i = 0; i < 10; i++) c2[i] = 5;
			NQCE7_NeffResult e2 = NQCE7_Neff.EstimateFromAbundances(c2, 1000);
			Chk("S2 con f1 = 0, S_est = S_obs y el IC colapsa en S_obs; cobertura = 1", e2.Falla == "" && e2.NEffEstimado == 10 && e2.Inferior == 10 && e2.Superior == 10 && e2.CoberturaMuestra == 1.0, Pad(e2));
			int[] c3 = new int[14]; for (int i = 0; i < 14; i++) c3[i] = i < 4 ? 1 : 30;
			NQCE7_NeffResult e3 = NQCE7_Neff.EstimateFromAbundances(c3, 100000);
			Chk("S3 f2 = 0 con f1 pequeno (4): definido por la correccion de sesgo; S_est = 20; IC vs Python", e3.Falla == "" && Near(e3.NEffEstimado, 20, 1e-12) && Near(e3.Inferior, 14.908775750517199, 1e-9) && Near(e3.Superior, 53.61373306837447, 1e-9), Pad(e3));
			int[] c4 = new int[30]; for (int i = 0; i < 30; i++) c4[i] = i < 9 ? 1 : (i < 10 ? 2 : 40);
			NQCE7_NeffResult e4 = NQCE7_Neff.EstimateFromAbundances(c4, 100000);
			Chk("S4 S_est >= S_obs; IC contiene S_est; Inferior >= S_obs (vs Python 48 / [33,985; 111,303])", e4.NEffEstimado >= e4.GruposObservados && e4.Inferior <= e4.NEffEstimado && e4.NEffEstimado <= e4.Superior && e4.Inferior >= e4.GruposObservados && Near(e4.NEffEstimado, 48, 1e-12) && Near(e4.Inferior, 33.985100980777894, 1e-9) && Near(e4.Superior, 111.30283311836052, 1e-8), Pad(e4));
			int[] c5 = new int[40]; for (int i = 0; i < 40; i++) c5[i] = i < 38 ? 1 : 2;
			NQCE7_NeffResult e5 = NQCE7_Neff.EstimateFromAbundances(c5, 60);
			Chk("S5 Superior y S_est se acotan a N_poblacion (no hay mas grupos que ensayos)", e5.Falla == "" && e5.NEffEstimado <= 60 && e5.Superior <= 60, Pad(e5));
			int[] c6 = new int[20]; for (int i = 0; i < 20; i++) c6[i] = i < 12 ? 1 : 3;
			Chk("S6 fallas cerradas: n<30, N<n, f2=0 con f1>=10, abundancia 0, vacio", NQCE7_Neff.EstimateFromAbundances(new int[] { 3, 4, 5 }, 1000).Falla.Length > 0 && NQCE7_Neff.EstimateFromAbundances(c2, 40).Falla.Length > 0 && NQCE7_Neff.EstimateFromAbundances(c6, 1000).Falla.Length > 0 && double.IsNaN(NQCE7_Neff.EstimateFromAbundances(c6, 1000).NEffEstimado) && NQCE7_Neff.EstimateFromAbundances(new int[] { 0, 40 }, 1000).Falla.Length > 0 && NQCE7_Neff.EstimateFromAbundances(null, 10).Falla.Length > 0, NQCE7_Neff.EstimateFromAbundances(c6, 1000).Falla);
			// ---- replicas: poblacion sintetica con G grupos conocidos, 100.000 ensayos, muestra uniforme de 5.000
			int cA, cB, sA, sB; double moA, meA, moB, meB;
			string hA = Scenario(200, 15, 1.3, 100000, 5000, 50, 777, out cA, out moA, out meA, out sA);
			string hB = Scenario(3000, 3, 1.1, 100000, 5000, 50, 888, out cB, out moB, out meB, out sB);
			Chk("S7 escenario A (200 grupos, ley de potencias, abundancia minima 45): poblacion suma 100.000; hash de 50 replicas = Python; S_est medio >= S_obs medio", sA == 100000 && sB == 100000 && hA == "0653ece44fbf95d0d45ed407c158ecc437721a9d3250498e4160bc0d385898d2" && meA >= moA, hA);
			Chk("S8 cobertura del IC95 en 50 replicas, escenario A (G=200): 42/50 = 84% (igual a Python); se exige >= 75%", cA == 42 && cA >= 38, "cobertura_IC=" + cA + "/50 S_obs_medio=" + moA.ToString("0.00", CultureInfo.InvariantCulture) + " S_est_medio=" + meA.ToString("0.00", CultureInfo.InvariantCulture));
			Chk("S9 escenario B (3.000 grupos, abundancia 5-16.430, mayoria rara): Chao1 SUBESTIMA (limite inferior) y el IC cubre solo 10/50 (= Python); hash = Python; es limite del metodo, no error", cB == 10 && hB == "4cfd7bf1c664678687d3b268c99d8c12afb3350120c142b1b5bc81f7c2d1dd71" && meB < 3000 && meB > moB, "cobertura_IC=" + cB + "/50 S_obs_medio=" + moB.ToString("0.0", CultureInfo.InvariantCulture) + " S_est_medio=" + meB.ToString("0.0", CultureInfo.InvariantCulture) + " G=3000 hash=" + hB);
			// ---- de extremo a extremo: series agrupadas (G=40 conocido, N=8000, muestra 400, T=120)
			int G = 40, k = 400, TT = 120; int[] ab = World(G, 5, 1.0, 8000); int[] lab = SampleLabels(ab, k, 4242);
			double[][] bases = new double[G][]; for (int g = 0; g < G; g++) bases[g] = Rand(TT, 5000 + g);
			double[][] ser = new double[k][]; for (int i = 0; i < k; i++) { double[] nz = Rand(TT, 9000 + i); double[] z = new double[TT]; for (int t = 0; t < TT; t++) z[t] = bases[lab[i]][t] + 0.25 * nz[t]; ser[i] = z; }
			int[] truth = Counts(lab, G); int t1 = 0, t2 = 0; foreach (int v in truth) { if (v == 1) t1++; else if (v == 2) t2++; }
			NQCE7_NeffResult es = NQCE7_Neff.EstimateFromSample(ser, 8000, 0.40);
			Chk("S10 extremo a extremo: el agrupamiento recupera los grupos reales de la muestra (S_obs, f1, f2 = verdad) y S_est >= S_obs", es.Falla == "" && es.GruposObservados == truth.Length && es.F1 == t1 && es.F2 == t2 && es.NMuestra == k && es.NPoblacion == 8000 && es.NEffEstimado >= es.GruposObservados && es.Inferior <= es.NEffEstimado && es.NEffEstimado <= es.Superior && es.SrVar > 0 && es.Label.Length == k, Pad(es) + " verdad=" + truth.Length + "," + t1 + "," + t2 + " " + es.Falla);
			// ---- fallas cerradas de EstimateFromSample
			double[][] ind = new double[40][]; for (int i = 0; i < 40; i++) ind[i] = Rand(100, 20000 + i);
			double[][] fl = new double[40][]; for (int i = 0; i < 40; i++) fl[i] = new double[100];
			double[][] sh = new double[40][]; for (int i = 0; i < 40; i++) sh[i] = i == 7 ? new double[10] : Rand(100, 20000 + i);
			Chk("S11 fallas cerradas: muestra < 30, N < n, varianza cero generalizada, f2=0 con f1 grande (40 independientes), corte invalido, longitudes distintas, nula",
				NQCE7_Neff.EstimateFromSample(new double[][] { ind[0], ind[1] }, 1000, 0.4).Falla.Length > 0 && NQCE7_Neff.EstimateFromSample(ind, 39, 0.4).Falla.Length > 0 && NQCE7_Neff.EstimateFromSample(fl, 1000, 0.4).Falla.Contains("varianza cero")
				&& NQCE7_Neff.EstimateFromSample(ind, 1000, 0.4).Falla.Contains("f2=0") && NQCE7_Neff.EstimateFromSample(ind, 1000, 1.5).Falla.Length > 0 && NQCE7_Neff.EstimateFromSample(sh, 1000, 0.4).Falla.Length > 0 && NQCE7_Neff.EstimateFromSample(null, 1000, 0.4).Falla.Length > 0
				&& double.IsNaN(NQCE7_Neff.EstimateFromSample(ind, 1000, 0.4).NEffEstimado), NQCE7_Neff.EstimateFromSample(ind, 1000, 0.4).Falla);
			// ---- politica en la evidencia
			string er; string basecfg = "neff_method=SAMPLE_CHAO1\nneff_sample_min_n=100\nneff_sample_cov_min=0.5\nneff_sample_max_series=500\n";
			NQCE7_Config cs = NQCE7_Config.Parse(basecfg + "mc_paths_exploratory=200\nmc_seed=7\n", out er), cp = NQCE7_Config.Parse(basecfg + "neff_bound=POINT\n", out er), cl = NQCE7_Config.Parse(basecfg + "neff_bound=LOWER\n", out er);
			NQCE7_NEffResult nU = NQCE7_Evidence.ComputeNEff(cs, ser, 8000), nP = NQCE7_Evidence.ComputeNEff(cp, ser, 8000), nL = NQCE7_Evidence.ComputeNEff(cl, ser, 8000);
			Chk("S12 politica SAMPLE_CHAO1: N_eff usado = limite SUPERIOR (defecto); POINT = S_est; LOWER = inferior; orden L <= P <= U; sellada por config_sha256", nU.Fail == "" && nU.Method == "SAMPLE_CHAO1" && nU.NEff == es.Superior && nP.NEff == es.NEffEstimado && nL.NEff == Math.Max(1.0, es.Inferior) && nL.NEff <= nP.NEff && nP.NEff <= nU.NEff && nU.NGross == 8000 && nU.NSeries == k && nU.Clusters == es.GruposObservados && cs.Sha != cp.Sha && cs.Sha.Length == 64, nU.NEff + "/" + nP.NEff + "/" + nL.NEff + " " + nU.Fail);
			NQCE7_Config c1k = NQCE7_Config.Parse("neff_method=SAMPLE_CHAO1\nneff_sample_cov_min=0.5\n", out er), c99 = NQCE7_Config.Parse(basecfg.Replace("cov_min=0.5", "cov_min=0.9999"), out er), cmx = NQCE7_Config.Parse("neff_method=SAMPLE_CHAO1\nneff_sample_min_n=100\nneff_sample_max_series=50\n", out er);
			NQCE7_NEffResult fMin = NQCE7_Evidence.ComputeNEff(c1k, ser, 8000), fCov = NQCE7_Evidence.ComputeNEff(c99, ser, 8000), fMax = NQCE7_Evidence.ComputeNEff(cmx, ser, 8000), fNo = NQCE7_Evidence.ComputeNEff(cs, null, 8000);
			Chk("S13 falla cerrada de la politica: muestra < neff_sample_min_n (defecto 1000), cobertura Good-Turing < minimo, muestra > max, sin series; N_eff = NaN y el DSR rechaza",
				fMin.Fail.Contains("neff_sample_min_n") && fCov.Fail.Contains("Good-Turing") && fMax.Fail.Contains("neff_sample_max_series") && fNo.Fail.Length > 0 && double.IsNaN(fMin.NEff) && double.IsNaN(NQCE7_Dsr.Compute(Rand(400, 3), fMin.NEff, fMin.SrVar).Dsr), fMin.Fail + " | " + fCov.Fail + " | " + fNo.Fail);
			Chk("S14 configuracion D-038: defectos (COVERED, UPPER, 0,90, 1000, 5000); claves invalidas = error; neff_bound!=UPPER se marca", NQCE7_Config.Parse("", out er).NEffMethod == "COVERED" && NQCE7_Config.Parse("", out er).NEffBound == "UPPER" && NQCE7_Config.Parse("", out er).NEffSampleCovMin == 0.90 && NQCE7_Config.Parse("", out er).NEffSampleMinN == 1000 && NQCE7_Config.Parse("", out er).NEffSampleMaxSeries == 5000 && NQCE7_Config.Parse("neff_method=Z", out er) == null && NQCE7_Config.Parse("neff_bound=Z", out er) == null && cp.Weaker().Contains("neff_bound!=UPPER") && NQCE7_Config.Parse(basecfg, out er).Weaker().Count == 0 && NQCE7_Config.Parse("neff_bound=POINT", out er).Weaker().Count == 0, "");
			double[][] indp = new double[40][]; for (int i = 0; i < 40; i++) indp[i] = Rand(500, 1000 + i);
			NQCE7_NEffResult nCov = NQCE7_Evidence.ComputeNEff(NQCE7_Config.Parse("", out er), indp, 40);
			Chk("S15 compatibilidad: ComputeNEff con el defecto COVERED == NQCE7_NEff.Compute (mismos grupos, N_eff y V)", nCov.Method == "COVERED" && nCov.Clusters == 40 && nCov.NEff == 40 && nCov.SrVar == NQCE7_NEff.Compute(indp, 40, 0.40, 1.0, 4000).SrVar, "");
			// ---- informe en modo SAMPLE_CHAO1
			NQCE7_CandidateInput gg = new NQCE7_CandidateInput(); gg.Id = "P_MUESTRA"; double[] dd = new double[400]; for (int t = 0; t < 400; t++) dd[t] = 10.0 + (((t * 37) % 101) - 50) * 1.0; gg.DailyPnl = dd; gg.Trades = new List<NQCE7_TradeRec>(); for (int i = 0; i < 60; i++) gg.Trades.Add(new NQCE7_TradeRec(i, 100, 0)); gg.Sessions = 60; gg.Fitness = 100;
			double[] nl = new double[30]; for (int i = 0; i < 30; i++) nl[i] = i;
			string cm = "0123456789abcdef0123456789abcdef01234567";
			List<NQCE7_Verdict> v1 = new List<NQCE7_Verdict>(); v1.Add(NQCE7_Evidence.Evaluate(gg, nU, nl, cs, NQC_AccountProfile.RapidIntraday50K()));
			string rs1 = NQCE7_Evidence.Report(cs, nU, nl, v1, cm, "spec_sha256=x"), rs2 = NQCE7_Evidence.Report(cs, nU, nl, v1, cm, "spec_sha256=x");
			Chk("S16 informe SAMPLE_CHAO1 determinista: imprime N_bruto, observados, f1, f2, N_eff estimado, IC95, cobertura GT y limite usado", rs1 == rs2 && rs1.Contains("N_bruto=8000") && rs1.Contains("observados=" + es.GruposObservados) && rs1.Contains(" f1=" + es.F1 + " f2=" + es.F2) && rs1.Contains("N_eff_estimado(Chao1)=") && rs1.Contains("IC95=[") && rs1.Contains("cobertura_GoodTuring=") && rs1.Contains("limite_usado=UPPER") && rs1.Contains("N_eff_usado="), rs1.Substring(rs1.IndexOf("N_eff_muestra")).Split('\n')[0]);
			NQCE7_Verdict vf = NQCE7_Evidence.Evaluate(gg, fMin, nl, cs, NQC_AccountProfile.RapidIntraday50K());
			Chk("S17 con muestra insuficiente el portafolio se rechaza por DSR (falla cerrada) y el informe lo dice", !vf.Pass && vf.Causes[0].StartsWith("DSR: N_eff:") && NQCE7_Evidence.Report(cs, fMin, nl, new List<NQCE7_Verdict> { vf }, cm, "").Contains("FALLA_CERRADA="), vf.Causes[0]);
			// ---- D-038 series planas (genomas inactivos): UN solo grupo; Python independiente = ET7_NEFF_REF.py (FLAT)
			double[][] mixed = new double[500][]; for (int i = 0; i < 300; i++) mixed[i] = ser[i]; for (int i = 300; i < 500; i++) mixed[i] = new double[TT];
			NQCE7_NeffResult ef = NQCE7_Neff.EstimateFromSample(mixed, 8000, 0.40);
			int[] actC = Counts(NQCE7_Slice(lab, 300), G); List<int> allC = new List<int>(actC); allC.Add(200); NQCE7_NeffResult efRef = NQCE7_Neff.EstimateFromAbundances(allC.ToArray(), 8000);
			Chk("S18 muestra con 40% de planas: UN grupo plano (observados = activos + 1; f1/f2/IC = EstimateFromAbundances con abundancia 200), NPlanas=200, fraccion 0,4, etiquetas coherentes", ef.Falla == "" && ef.NPlanas == 200 && ef.NActivas == 300 && Math.Abs(ef.FraccionPlanas - 0.4) < 1e-12 && ef.GruposObservados == actC.Length + 1 && Pad(ef) == Pad(efRef) && ef.Label.Length == 500 && ef.Label[299] != ef.Label[300] && ef.Label[300] == ef.Label[499] && ef.Label[300] == actC.Length, Pad(ef) + " | " + Pad(efRef) + " " + ef.Falla);
			double[][] m995 = new double[1000][]; for (int i = 0; i < 1000; i++) m995[i] = i < 5 ? ser[i] : new double[TT];
			double[][] m990 = new double[1000][]; for (int i = 0; i < 1000; i++) m990[i] = i < 10 ? ser[i] : new double[TT];
			double[][] m989 = new double[1000][]; for (int i = 0; i < 1000; i++) m989[i] = i < 11 ? ser[i] : new double[TT];
			NQCE7_NeffResult f995 = NQCE7_Neff.EstimateFromSample(m995, 8000, 0.40), f990 = NQCE7_Neff.EstimateFromSample(m990, 8000, 0.40), f989 = NQCE7_Neff.EstimateFromSample(m989, 8000, 0.40);
			Chk("S19 99,5% y 99% planas: falla cerrada (GA degenerado) y reporta n/fraccion; 98,9% con solo 11 activas tambien falla (activas < 30); NaN en N_eff", f995.Falla.Contains("varianza cero") && f990.Falla.Contains("varianza cero") && f995.NPlanas == 995 && Math.Abs(f995.FraccionPlanas - 0.995) < 1e-12 && f989.Falla.Contains("series con actividad") && f989.NPlanas == 989 && double.IsNaN(f995.NEffEstimado), f995.Falla + " | " + f989.Falla);
			NQCE7_Config cfp = NQCE7_Config.Parse("neff_method=SAMPLE_CHAO1\nneff_sample_min_n=100\nneff_sample_cov_min=0.5\n", out er);
			NQCE7_NEffResult pOk = NQCE7_Evidence.ComputeNEff(cfp, mixed, 8000), pBad = NQCE7_Evidence.ComputeNEff(cfp, m995, 8000);
			double[][] m150 = new double[500][]; for (int i = 0; i < 500; i++) m150[i] = i < 90 ? ser[i] : new double[TT];
			NQCE7_NEffResult pMin = NQCE7_Evidence.ComputeNEff(cfp, m150, 8000);
			string rp = NQCE7_Evidence.Report(cfp, pOk, nl, new List<NQCE7_Verdict>(), cm, "");
			Chk("S20 politica: 40% planas NO falla cerrado y el informe imprime series_planas y fraccion; 99,5% falla; activas (90) < neff_sample_min_n (100) falla aunque el total sea 500", pOk.Fail == "" && !double.IsNaN(pOk.NEff) && pBad.Fail.Length > 0 && pMin.Fail.Contains("neff_sample_min_n") && rp.Contains("series_planas=200 fraccion_planas=0.4000") && NQCE7_Evidence.Report(cfp, pBad, nl, new List<NQCE7_Verdict>(), cm, "").Contains("series_planas=995"), pOk.Fail + " | " + pBad.Fail + " | " + pMin.Fail);
			List<int> rc = new List<int>(Counts(SampleLabels(World(200, 15, 1.3, 100000), 3000, 555), 200)); rc.Add(2000); NQCE7_NeffResult rf = NQCE7_Neff.EstimateFromAbundances(rc.ToArray(), 100000);
			Chk("S21 referencia Python independiente (FLAT): 3000 activas + UN grupo plano de 2000 -> 184,44,35,210.2778,196.4017,239.6798; contarlas como singletons daria ~60.181 (inflado)", Pad(rf) == "184,44,35,210.2778,196.4017,239.6798", Pad(rf));
			// ---- efecto en el DSR: N_eff mayor => SR0 mayor => DSR menor (misma serie, V = 0,0004; referencia Python)
			double[] xx = new double[400]; for (int t = 0; t < 400; t++) xx[t] = ((t * 37) % 101 - 48) * 1.0 + (t % 7 == 0 ? 3.0 : 0.0) + (t % 2 == 0 ? 0.8 : 0.0);
			NQCE7_DsrResult q1 = NQCE7_Dsr.Compute(xx, 150, 0.0004), q2 = NQCE7_Dsr.Compute(xx, 200, 0.0004), q3 = NQCE7_Dsr.Compute(xx, 260, 0.0004);
			Chk("S22 efecto en el DSR: N_eff 150 < 200 < 260 => SR0 0,053402 < 0,055310 < 0,057004 y DSR 0,79049 > 0,77936 > 0,76921 (vs Python); usar el limite superior endurece el DSR", q1.Sr0 < q2.Sr0 && q2.Sr0 < q3.Sr0 && q1.Dsr > q2.Dsr && q2.Dsr > q3.Dsr && Near(q1.Sr0, 0.053402126187284606, 1e-12) && Near(q2.Sr0, 0.05531047809267094, 1e-12) && Near(q3.Sr0, 0.057003996654779156, 1e-12) && Near(q1.Dsr, 0.7904919048097996, 1e-10) && Near(q2.Dsr, 0.779360799880516, 1e-10) && Near(q3.Dsr, 0.7692051067253196, 1e-10), F(q1.Dsr) + "/" + F(q2.Dsr) + "/" + F(q3.Dsr));
		}

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
			string legacyPrefix = rep.ToString(); int legacyPass = pass;
			RunNeffTests();
			RunNeffPerfTests();
			string legacyText = legacyPrefix + "\n--- informe de muestra (sintetico) ---\n" + r1 + "\nresultado: " + legacyPass + " PASS / 0 FALLA\n";
			Chk("R5 compatibilidad: modo COVERED reproduce el hash de la prueba anterior (report_sha256 4eca95fe...)", NQCE_Const.Sha256Text(legacyText) == "4eca95fe38be59ea5b49e606c5dd4661552dbce66a578aa7819b1236db0fbf52", NQCE_Const.Sha256Text(legacyText));
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
