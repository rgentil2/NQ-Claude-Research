#region Using declarations
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

//==============================================================================
// NQC_RUN_Batch - LANZADOR POR LOTES de campanas del embudo (ET2 Fase A/B, paso 0 del runbook de ET2). Contrato NQC 2.0.0. SOLO DESARROLLO.
// Estrategia SIN ordenes. Lee NQC_Results\RUN_BATCH_MANIFEST.txt (formato en ninjatrader/runbooks/RUN_FUNNEL_RUNBOOK.md y ET2_BATCH_MANIFEST_FASE_A.txt), carga las barras M1 del IS
// UNA sola vez (Strategy Analyzer: NQ M1, 2020-01-01..2022-12-31), y ejecuta las campanas en SECUENCIA con el runner NQCRUN_Funnel (ET3->ET7), una carpeta por campana
// <out_root>\<id>\ (SUMMARY.txt del runner, CKPT_*, ET3..ET7) mas BATCH_ENTRY.txt (hash de entrada del lote), y un SUMMARY.txt del lote.
//
// GUARDAS (las mismas del runner; el lanzador las aplica una vez al conjunto de datos y el runner las repite por campana): solo IS 2020-2022 (una barra fuera aborta), M1 completas desde
//   00:00, R2 (zona horaria), volumen alineado. El OOS JAMAS se toca. Campanas de CALIBRACION (transformador N1/N2/N1+INJ): etiqueta libre, semilla de GA obligatoria y distinta de la de C1,
//   max_evals <= 100.000. Campana SIN transformador (IDENTITY = IS real): solo con id C1..C5 (consume una de las 5 campanas de ET3) y con et2_approved=1 en el manifiesto.
//   El congelado (freeze) esta prohibido en el lote. TIMING solo sobre un dataset N1/N2, NUNCA sobre el IS original.
// MODOS DE TAMANO: REDUCED = poblacion 200 x 50 gen, max_evals 10.000, top-K 200 | FULL = 500 x 200, 100.000, top-K 2.000 | TIMING = N evaluaciones de ET3 (defecto 2.000) sobre N1, mide ms por
//   evaluacion (media, mediana, p90) y memoria SIN guardar ni escribir ninguna economia | CUSTOM (solo pruebas; exige allow_custom=1 y pop/gen/maxevals/topk explicitos).
// REANUDACION: una campana con SUMMARY.txt valido (su sha256 coincide con BATCH_ENTRY.txt) y hash de entrada del lote IGUAL se salta. El hash de entrada cubre: datos crudos, transformador y
//   semilla, semilla del GA, tamano, config global (CanonicalText del runner), version de biblioteca, motor y vector nulo. Dentro de una campana incompleta, el checkpoint por etapa del runner reanuda.
//   TIMING nunca se salta (es una medicion de ESTA maquina).
// DIGEST DEL LOTE: sha256 de (hash del manifiesto, hash de datos, y por campana: id, transformador, tamano, semilla, estado, digest del runner, ensayos). No incluye tiempos, memoria, rutas ni si se reanudo.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public sealed class NQCRUN_BatchCampaign
	{
		public string Id = "", Transform = "IDENTITY", Size = "REDUCED";
		public long TSeed, GaSeed; public bool HasTSeed, HasGaSeed;
		public double Target = double.NaN, Tol = double.NaN;   // por campana (NaN = el global del manifiesto)
		public int Evals = 2000, Pop, Gen, MaxEvals, TopK; public int Sample = -1;     // Sample: -1 = el global neff_sample
		public int[] Threads;      // NQCRUN_Batch 1.1.0: hilos de la campana. Una campana normal usa el primero; TIMING con varios valores (threads=1,4,8,16,24) BARRE y compara. NO entra en ningun hash (el resultado no depende de los hilos).
		public string Spec(double target, double tol)
		{
			return "id=" + Id + ";transform=" + Transform + ";tseed=" + (HasTSeed ? TSeed.ToString(CultureInfo.InvariantCulture) : "-") + ";gaseed=" + GaSeed.ToString(CultureInfo.InvariantCulture) + ";size=" + Size
				+ ";evals=" + Evals.ToString(CultureInfo.InvariantCulture) + ";pop=" + Pop.ToString(CultureInfo.InvariantCulture) + ";gen=" + Gen.ToString(CultureInfo.InvariantCulture) + ";maxevals=" + MaxEvals.ToString(CultureInfo.InvariantCulture)
				+ ";topk=" + TopK.ToString(CultureInfo.InvariantCulture) + ";sample=" + Sample.ToString(CultureInfo.InvariantCulture)
				+ (Transform == "N1INJ" ? ";target=" + (double.IsNaN(Target) ? target : Target).ToString("R", CultureInfo.InvariantCulture) + ";tol=" + (double.IsNaN(Tol) ? tol : Tol).ToString("R", CultureInfo.InvariantCulture) : "");
		}
	}

	public sealed class NQCRUN_BatchManifest
	{
		public string Name = "BATCH", OutRoot = "", GlobalText = "", Sha = "";
		public double Target = 20.0, Tol = 2.0; public bool AllowCustom;
		public List<NQCRUN_BatchCampaign> Campaigns = new List<NQCRUN_BatchCampaign>();
		public const int MaxCampaigns = 200;
		private static readonly string[] Forbidden = new string[] { "campaign", "seed", "population", "generations", "max_evals", "top_k", "transform", "transform_args", "out_dir", "stop_after", "freeze", "engine_dir" };

		public static string NormTransform(string v)
		{
			v = v.Trim().ToUpperInvariant().Replace("+", "");
			if (v == "IDENTITY" || v == "IDENTIDAD") return "IDENTITY";
			if (v == "N1" || v == "N2" || v == "N1INJ") return v;
			return "";
		}
		public static string NormSize(string v)
		{
			v = v.Trim().ToUpperInvariant();
			if (v == "REDUCED" || v == "REDUCIDO") return "REDUCED";
			if (v == "FULL" || v == "COMPLETO") return "FULL";
			if (v == "TIMING" || v == "CUSTOM") return v;
			return "";
		}

		public static NQCRUN_BatchManifest Parse(string text, out string error)
		{
			error = ""; NQCRUN_BatchManifest m = new NQCRUN_BatchManifest(); StringBuilder glob = new StringBuilder(), canon = new StringBuilder(); HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
			int ln = 0;
			foreach (string raw in (text ?? "").Split('\n'))
			{
				ln++; string line = raw.Trim(); if (line.Length == 0 || line[0] == '#') continue;
				// la ruta de salida y los HILOS (threads, max_memory_mb, o el token threads= de una campana) no entran en el hash del manifiesto: el lote es el mismo en otra carpeta o con otros hilos
				bool execKey = line.StartsWith("threads", StringComparison.OrdinalIgnoreCase) || line.StartsWith("max_memory_mb", StringComparison.OrdinalIgnoreCase);
				if (!line.StartsWith("out_root", StringComparison.OrdinalIgnoreCase) && !execKey)
					canon.Append(line.StartsWith("campaign", StringComparison.Ordinal) ? System.Text.RegularExpressions.Regex.Replace(line, @"[ \t]+threads=\S*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase) : line).Append('\n');
				try
				{
					if (line.StartsWith("campaign ", StringComparison.Ordinal) || line.StartsWith("campaign\t", StringComparison.Ordinal))
					{
						NQCRUN_BatchCampaign c = new NQCRUN_BatchCampaign(); string sz = "REDUCED";
						foreach (string tok in line.Substring(9).Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
						{
							int eq = tok.IndexOf('='); if (eq <= 0) { error = "linea " + ln + ": token invalido '" + tok + "'"; return null; }
							string k = tok.Substring(0, eq).ToLowerInvariant(), v = tok.Substring(eq + 1);
							switch (k)
							{
								case "id": c.Id = v; break;
								case "transform": c.Transform = NormTransform(v); if (c.Transform.Length == 0) { error = "linea " + ln + ": transformador desconocido '" + v + "' (IDENTITY, N1, N2, N1INJ)"; return null; } break;
								case "tseed": c.TSeed = long.Parse(v, CultureInfo.InvariantCulture); c.HasTSeed = true; break;
								case "gaseed": c.GaSeed = long.Parse(v, CultureInfo.InvariantCulture); c.HasGaSeed = true; break;
								case "size": sz = NormSize(v); if (sz.Length == 0) { error = "linea " + ln + ": tamano desconocido '" + v + "' (REDUCED, FULL, TIMING, CUSTOM)"; return null; } break;
								case "evals": c.Evals = int.Parse(v, CultureInfo.InvariantCulture); break;
								case "pop": c.Pop = int.Parse(v, CultureInfo.InvariantCulture); break;
								case "gen": c.Gen = int.Parse(v, CultureInfo.InvariantCulture); break;
								case "maxevals": c.MaxEvals = int.Parse(v, CultureInfo.InvariantCulture); break;
								case "topk": c.TopK = int.Parse(v, CultureInfo.InvariantCulture); break;
								case "target": c.Target = double.Parse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture); break;
								case "tol": c.Tol = double.Parse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture); break;
								case "sample": c.Sample = int.Parse(v, CultureInfo.InvariantCulture); break;
								case "threads":
								{
									string[] parts = v.Split(','); c.Threads = new int[parts.Length];
									for (int q = 0; q < parts.Length; q++) { c.Threads[q] = int.Parse(parts[q], CultureInfo.InvariantCulture); if (c.Threads[q] < 1 || c.Threads[q] > 128) { error = "linea " + ln + ": threads fuera de 1..128"; return null; } }
									if (c.Threads.Length > 12) { error = "linea " + ln + ": threads admite a lo sumo 12 valores"; return null; }
									break;
								}
								default: error = "linea " + ln + ": clave de campana desconocida '" + k + "'"; return null;
							}
						}
						c.Size = sz;
						if ((!double.IsNaN(c.Target) || !double.IsNaN(c.Tol)) && c.Transform != "N1INJ") { error = "campana " + c.Id + ": target/tol solo con transform=N1INJ"; return null; }
						if (!double.IsNaN(c.Target) && !(c.Target > 0)) { error = "campana " + c.Id + ": target debe ser > 0"; return null; }
						if (!double.IsNaN(c.Tol) && !(c.Tol > 0)) { error = "campana " + c.Id + ": tol debe ser > 0"; return null; }
						if (c.Id.Length == 0) { error = "linea " + ln + ": falta id"; return null; }
						if (!ids.Add(c.Id)) { error = "linea " + ln + ": id repetido '" + c.Id + "'"; return null; }
						if (!c.HasGaSeed) { error = "campana " + c.Id + ": falta gaseed (la semilla del GA se declara antes de correr)"; return null; }
						if (c.Transform != "IDENTITY" && !c.HasTSeed) { error = "campana " + c.Id + ": el transformador " + c.Transform + " exige tseed"; return null; }
						if (c.Transform == "IDENTITY" && c.HasTSeed) { error = "campana " + c.Id + ": IDENTITY no admite tseed"; return null; }
						if (c.Threads != null && c.Threads.Length > 1 && c.Size != "TIMING") { error = "campana " + c.Id + ": una lista de threads (barrido) solo se admite en TIMING"; return null; }
						if (c.Size == "TIMING")
						{
							if (c.Transform != "N1" && c.Transform != "N2") { error = "campana " + c.Id + ": TIMING solo sobre un dataset N1 (o N2), nunca sobre el IS original ni N1+INJ"; return null; }
							if (c.Evals < 1 || c.Evals > 100000) { error = "campana " + c.Id + ": evals fuera de 1..100000"; return null; }
						}
						else if (c.Size == "REDUCED") { c.Pop = 200; c.Gen = 50; c.MaxEvals = 10000; c.TopK = 200; }
						else if (c.Size == "FULL") { c.Pop = 500; c.Gen = 200; c.MaxEvals = 100000; c.TopK = 2000; }
						else if (c.Pop < 1 || c.Gen < 1 || c.MaxEvals < 1 || c.TopK < 1) { error = "campana " + c.Id + ": CUSTOM exige pop, gen, maxevals y topk"; return null; }
						m.Campaigns.Add(c);
						continue;
					}
					int e = line.IndexOf('='); if (e <= 0) { error = "linea " + ln + ": linea invalida '" + line + "'"; return null; }
					string key = line.Substring(0, e).Trim().ToLowerInvariant(), val = line.Substring(e + 1).Trim();
					if (Array.IndexOf(Forbidden, key) >= 0) { error = "linea " + ln + ": la clave global '" + key + "' no se admite en el lote (va por campana o esta prohibida)"; return null; }
					switch (key)
					{
						case "batch": m.Name = val; break;
						case "out_root": m.OutRoot = val; break;
						case "target": m.Target = double.Parse(val.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture); break;
						case "tol": m.Tol = double.Parse(val.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture); break;
						case "allow_custom": m.AllowCustom = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
						default: glob.Append(key).Append('=').Append(val).Append('\n'); break;
					}
				}
				catch (FormatException) { error = "linea " + ln + ": valor ilegible"; return null; }
				catch (OverflowException) { error = "linea " + ln + ": valor fuera de rango"; return null; }
			}
			if (m.Campaigns.Count == 0) { error = "el manifiesto no tiene campanas"; return null; }
			if (m.Campaigns.Count > MaxCampaigns) { error = "demasiadas campanas (" + m.Campaigns.Count.ToString(CultureInfo.InvariantCulture) + " > " + MaxCampaigns.ToString(CultureInfo.InvariantCulture) + ")"; return null; }
			m.GlobalText = glob.ToString();
			string pe; NQCRUN_Params probe = NQCRUN_Params.Parse(m.GlobalText, out pe); if (probe == null) { error = "configuracion global: " + pe; return null; }
			foreach (NQCRUN_BatchCampaign c in m.Campaigns)
			{
				if (c.Size == "CUSTOM" && !m.AllowCustom) { error = "campana " + c.Id + ": size=CUSTOM solo con allow_custom=1 (pruebas)"; return null; }
				if (c.Transform == "IDENTITY")
				{
					if (c.Size == "TIMING") { error = "campana " + c.Id + ": IDENTITY no admite TIMING"; return null; }
					if (!(c.Id.Length == 2 && c.Id[0] == 'C' && c.Id[1] >= '1' && c.Id[1] <= '5')) { error = "campana " + c.Id + ": una campana sobre el IS REAL (IDENTITY) es una de las 5 campanas de ET3: id C1..C5"; return null; }
					if (!probe.Et2Approved) { error = "campana " + c.Id + ": IDENTITY (IS real) exige et2_approved=1 en el manifiesto"; return null; }
				}
			}
			m.Sha = NQCE_Const.Sha256Text(canon.ToString());
			return m;
		}
	}

	public sealed class NQCRUN_BatchOptions
	{
		public Func<INQCE3_Library> LibraryFactory;      // null = new NQCE3_Library()
		public NQCE_Config Config;                       // null = new NQCE_Config()
		public double[] NullBest;                        // vector nulo de ET2 (null = null_best_file del manifiesto)
		public string EngineHash = "";
		public Action<string> Log;
	}

	public sealed class NQCRUN_BatchRow
	{
		public string Id = "", Transform = "", Size = "", State = "", FunnelStatus = "NA", Digest = "NA", Trials = "NA", Note = ""; public long GaSeed; public bool Resumed; public double Seconds; public long PeakMb;
	}

	public sealed class NQCRUN_BatchResult
	{
		public string Status = "", Summary = "", BatchDigest = "", OutRoot = ""; public List<NQCRUN_BatchRow> Rows = new List<NQCRUN_BatchRow>(); public double TotalSeconds;
	}

	public static class NQCRUN_Batch
	{
		public const string Version = "NQCRUNBATCH-1.1.0";
		private static string I(long v) { return v.ToString(CultureInfo.InvariantCulture); }
		private static string F(double v, string fmt) { return v.ToString(fmt, CultureInfo.InvariantCulture); }
		private static long PeakMb() { try { return Process.GetCurrentProcess().PeakWorkingSet64 / 1048576; } catch { return -1; } }

		private static NQCRUN_IDatasetTransform MakeTransform(NQCRUN_BatchCampaign c, NQCRUN_BatchManifest m, NQCE_Config cfg)
		{
			if (c.Transform == "N1") return new NQCET2_N1(c.TSeed, cfg.Excluded);
			if (c.Transform == "N2") return new NQCET2_N2(c.TSeed);
			if (c.Transform == "N1INJ") return new NQCET2_InjectedN1(c.TSeed, cfg, double.IsNaN(c.Target) ? m.Target : c.Target, double.IsNaN(c.Tol) ? m.Tol : c.Tol);
			return new NQCRUN_IdentityTransform();
		}

		private static NQCRUN_Params MakeParams(NQCRUN_BatchManifest m, NQCRUN_BatchCampaign c, NQCRUN_BatchOptions opt, NQCE_Config cfg, string outRoot)
		{
			string e; NQCRUN_Params p = NQCRUN_Params.Parse(m.GlobalText, out e); if (p == null) throw new NQCRUN_Exception("RUN: configuracion global ilegible: " + e);
			p.Campaign = c.Id; p.Seed = c.GaSeed; p.SeedGiven = true; p.Population = c.Pop; p.Generations = c.Gen; p.MaxEvals = c.MaxEvals; p.TopK = c.TopK;
			if (c.Sample >= 0) p.NeffSample = c.Sample;
			if (c.Threads != null && c.Threads.Length >= 1) p.Threads = c.Threads[0];
			p.Calibration = c.Transform != "IDENTITY"; p.OutDir = Path.Combine(outRoot, c.Id);
			p.Config = cfg; p.EngineHash = opt.EngineHash ?? ""; p.Log = opt.Log; p.LibraryFactory = opt.LibraryFactory; p.NullBest = opt.NullBest;
			return p;
		}

		private static string Entry(string input, string summarySha, string status, string digest, string trials) { return "input_hash=" + input + "\nsummary_sha256=" + summarySha + "\nstatus=" + status + "\ndigest=" + digest + "\ntrials=" + trials + "\n"; }
		private static Dictionary<string, string> ReadEntry(string path)
		{
			try
			{
				if (!File.Exists(path)) return null; Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
				foreach (string line in File.ReadAllText(path).Split('\n')) { int eq = line.IndexOf('='); if (eq > 0) d[line.Substring(0, eq)] = line.Substring(eq + 1); }
				return d.ContainsKey("input_hash") && d.ContainsKey("summary_sha256") && d.ContainsKey("digest") && d.ContainsKey("status") && d.ContainsKey("trials") ? d : null;
			}
			catch { return null; }
		}

		private static string Nearest(List<double> sorted, double q) { int n = sorted.Count; int idx = (int)Math.Ceiling(q * n) - 1; if (idx < 0) idx = 0; if (idx >= n) idx = n - 1; return F(sorted[idx], "0.000"); }

		// ------------------------------------------------------------------ TIMING (paso 0 del runbook de ET2): N evaluaciones de ET3 sobre N1; solo tiempos y memoria, NO se guarda ni se escribe ninguna economia
		// NQCRUN_Batch 1.1.0: threads=N mide con N hilos; threads=1,4,8,16,24 BARRE varios valores en una sola corrida (cada uno con datos y cache FRIOS; defecto 200 evaluaciones por valor)
		// y reporta speed-up y eficiencia por hilo. Ademas compara el hash del ledger entre valores (el resultado debe ser identico: lo unico que cambia es el tiempo). No se guarda economia.
		private sealed class TimingStats
		{
			public int Threads, Applied, N; public double WallS, SumMs, Mean, Median, P90, Max, MeanWarm, Mean2nd, Wall2ndMsPerEval, MemGestMb, TransformS, Mem0Mb; public long PeakMb; public string LedgerSha = "", LibVersion = "", TrName = "", TrParams = ""; public int Sessions, Bars; public List<double> Ms;
		}

		private static TimingStats TimingOnce(NQCRUN_BatchManifest m, NQCRUN_BatchCampaign c, NQCRUN_BatchOptions opt, NQCE_Config cfg, NQCE_Bars rawBars, double[] vol, NQCRUN_Params baseP, int requested)
		{
			TimingStats ts = new TimingStats(); ts.Threads = requested;
			ts.Applied = NQCRUN_Threads.Resolve(requested, baseP.MemoBudgetMb, baseP.MaxMemoryMb);
			NQCRUN_IDatasetTransform tr = MakeTransform(c, m, cfg); NQCE_Bars bars; double[] volume; Stopwatch swT = Stopwatch.StartNew(); tr.Apply(rawBars, vol, out bars, out volume); swT.Stop();
			if (bars == null || volume == null) throw new NQCRUN_Exception("RUN: el transformador devolvio datos nulos");
			NQCRUN_DataReport rep2 = new NQCRUN_DataReport(); string err = NQCRUN_Data.Check(bars.T, bars.O, bars.H, bars.L, bars.C, volume, baseP.StrictIsRange, false, rep2); if (err.Length > 0) throw new NQCRUN_Exception("RUN: salida del transformador " + tr.Name + " invalida: " + err);
			if (object.ReferenceEquals(bars, rawBars)) throw new NQCRUN_Exception("RUN: TIMING sobre el IS original esta prohibido (el transformador devolvio los datos sin cambiar)");
			INQCE3_Library lib = opt.LibraryFactory != null ? opt.LibraryFactory() : new NQCE3_Library();
			long oldMemo = NQCE3_Series.MemoBudgetBytes; NQCE3_Series.MemoBudgetBytes = (long)baseP.MemoBudgetMb * 1024L * 1024L;
			try
			{
				NQCE3_Params gp = new NQCE3_Params(); gp.PopSize = 200; gp.Generations = 10 * (c.Evals / 200 + 1) + 10; gp.TopK = 200; gp.Seed = c.GaSeed;
				long mem0 = GC.GetTotalMemory(true);
				NQCE3_Fitness fit = new NQCE3_Fitness(bars, volume, cfg, lib); fit.FreshEvalMs = new List<double>(c.Evals + 256);
				NQCE3_Campaign camp = new NQCE3_Campaign(); camp.Name = c.Id; camp.MaxEvals = c.Evals; camp.Threads = ts.Applied;
				Stopwatch sw = Stopwatch.StartNew(); double tHalf = -1; int eHalf = 0; int halfTarget = Math.Max(1, c.Evals / 2);
				camp.OnGeneration = delegate(int g, NQCE3_Campaign cm) { if (tHalf < 0 && fit.FreshEvaluations >= halfTarget) { tHalf = sw.Elapsed.TotalSeconds; eHalf = fit.FreshEvaluations; } };   // marca de la mitad: mide el REGIMEN (segunda mitad) aparte del arranque en frio
				camp.Run(fit, lib, gp, null); sw.Stop();
				ts.WallS = sw.Elapsed.TotalSeconds; ts.MemGestMb = GC.GetTotalMemory(false) / 1048576.0; ts.Mem0Mb = mem0 / 1048576.0; ts.PeakMb = PeakMb();
				List<double> ms = new List<double>(fit.FreshEvalMs); int n = ms.Count; if (n == 0) throw new NQCRUN_Exception("RUN: TIMING sin evaluaciones");
				double sum = 0; foreach (double x in ms) sum += x; ts.SumMs = sum; ts.Mean = sum / n; ts.N = n;
				int skip = n / 20; double sum2 = 0; for (int i = skip; i < n; i++) sum2 += ms[i]; ts.MeanWarm = n - skip > 0 ? sum2 / (n - skip) : ts.Mean;
				{ int h = n / 2; double s2 = 0; for (int i = h; i < n; i++) s2 += ms[i]; ts.Mean2nd = n - h > 0 ? s2 / (n - h) : ts.Mean; ts.Wall2ndMsPerEval = (tHalf >= 0 && n > eHalf) ? (sw.Elapsed.TotalSeconds - tHalf) * 1000.0 / (n - eHalf) : ts.WallS * 1000.0 / n; }
				ms.Sort(); ts.Median = n % 2 == 1 ? ms[n / 2] : 0.5 * (ms[n / 2 - 1] + ms[n / 2]); ts.Ms = ms; ts.P90 = ms[Math.Max(0, Math.Min(n - 1, (int)Math.Ceiling(0.90 * n) - 1))]; ts.Max = ms[n - 1];
				ts.LedgerSha = camp.Ledger.Sha256();      // solo el hash (determinismo entre hilos); NO se guarda ni se muestra ninguna economia
				ts.LibVersion = lib.Version; ts.TrName = tr.Name; ts.TrParams = tr.ParamsText; ts.Sessions = rep2.Sessions; ts.Bars = bars.Count; ts.TransformS = swT.Elapsed.TotalSeconds;
				return ts;
			}
			finally { NQCE3_Series.MemoBudgetBytes = oldMemo; }
		}

		private static NQCRUN_BatchRow RunTiming(NQCRUN_BatchManifest m, NQCRUN_BatchCampaign c, NQCRUN_BatchOptions opt, NQCE_Config cfg, string outRoot, NQCE_Bars rawBars, double[] vol, NQCRUN_Params baseP)
		{
			NQCRUN_BatchRow row = new NQCRUN_BatchRow(); row.Id = c.Id; row.Transform = c.Transform; row.Size = "TIMING"; row.GaSeed = c.GaSeed;
			string dir = Path.Combine(outRoot, c.Id); Directory.CreateDirectory(dir);
			int[] tl = c.Threads != null ? c.Threads : new int[] { baseP.Threads };    // 0 = automatico
			List<TimingStats> all = new List<TimingStats>();
			for (int i = 0; i < tl.Length; i++)
			{
				if (i > 0) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
				all.Add(TimingOnce(m, c, opt, cfg, rawBars, vol, baseP, tl[i]));
				opt.Log("BATCH " + c.Id + " TIMING threads=" + I(all[i].Applied) + " pared=" + F(all[i].WallS, "0.0") + "s");
			}
			TimingStats a0 = all[0]; StringBuilder s = new StringBuilder();
			s.Append("NQC RUN BATCH TIMING | ").Append(Version).Append(" | dataset transformado (").Append(a0.TrName).Append("), NUNCA el IS original | solo tiempos y memoria; NO se guarda ni se mira ninguna economia | SOLO DESARROLLO\n");
			s.Append("id=").Append(c.Id).Append(" transform=").Append(a0.TrName).Append(" transform_params=").Append(a0.TrParams).Append(" ga_seed=").Append(I(c.GaSeed)).Append(" pop=200 evals_pedidas=").Append(I(c.Evals)).Append(" threads=").Append(c.Threads == null ? (baseP.Threads == 0 ? "auto" : I(baseP.Threads)) : string.Join(",", Array.ConvertAll(c.Threads, delegate(int x) { return I(x); }))).Append('\n');
			s.Append("evaluaciones_medidas=").Append(I(a0.N)).Append(" barras=").Append(I(a0.Bars)).Append(" sesiones=").Append(I(a0.Sessions)).Append(" library=").Append(a0.LibVersion).Append(" runner=").Append(NQCRUN_Funnel.Version).Append(" nucleos_logicos=").Append(I(Environment.ProcessorCount)).Append('\n');
			foreach (TimingStats t in all)
			{
				double wallMsPerEval = t.WallS * 1000.0 / t.N;
				s.Append("[threads=").Append(I(t.Applied)).Append(t.Applied != t.Threads && t.Threads > 0 ? " (pedido " + I(t.Threads) + ", limitado por memoria)" : "").Append("] ms_por_evaluacion: media=").Append(F(t.Mean, "0.000")).Append(" mediana=").Append(F(t.Median, "0.000")).Append(" p90=").Append(F(t.P90, "0.000")).Append(" max=").Append(F(t.Max, "0.000")).Append(" media_sin_el_5%_inicial=").Append(F(t.MeanWarm, "0.000")).Append(" segunda_mitad(REGIMEN): ms_en_hilo=").Append(F(t.Mean2nd, "0.000")).Append(" ms_de_PARED=").Append(F(t.Wall2ndMsPerEval, "0.000")).Append(" (tiempo de cada evaluacion dentro de su hilo)\n");
				s.Append("[threads=").Append(I(t.Applied)).Append("] tiempo_total_evaluaciones_s=").Append(F(t.WallS, "0.00")).Append(" ms_por_evaluacion_de_PARED=").Append(F(wallMsPerEval, "0.000")).Append(" tiempo_transformador_s=").Append(F(t.TransformS, "0.00")).Append(" memoria_gestionada_antes_mb=").Append(F(t.Mem0Mb, "0")).Append(" despues_mb=").Append(F(t.MemGestMb, "0")).Append(" pico_working_set_mb=").Append(I(t.PeakMb)).Append(" memo_budget_mb=").Append(I(baseP.MemoBudgetMb)).Append('\n');
			}
			if (all.Count > 1)
			{
				s.Append("BARRIDO DE HILOS (cada valor con datos y cache FRIOS, ").Append(I(a0.N)).Append(" evaluaciones; speed-up = pared(").Append(I(a0.Applied)).Append(" hilo(s)) / pared; eficiencia = speed-up / (hilos / ").Append(I(a0.Applied)).Append("))\n");
				s.Append("hilos\tpared_s\tms_pared_por_eval\tspeedup\teficiencia\tms_medio_en_hilo\tledger_sha12\n");
				bool same = true; foreach (TimingStats t in all) { if (t.LedgerSha != a0.LedgerSha) same = false; }
				foreach (TimingStats t in all)
				{
					double sp = a0.WallS / t.WallS, eff = sp / ((double)t.Applied / a0.Applied);
					s.Append(I(t.Applied)).Append('\t').Append(F(t.WallS, "0.00")).Append('\t').Append(F(t.WallS * 1000.0 / t.N, "0.000")).Append('\t').Append(F(sp, "0.00")).Append('\t').Append(F(eff, "0.00")).Append('\t').Append(F(t.Mean, "0.000")).Append('\t').Append(t.LedgerSha.Substring(0, 12)).Append('\n');
				}
				s.Append("resultado_identico_entre_hilos=").Append(same ? "SI" : "NO (DEFECTO: el ledger depende de los hilos; no usar threads>1 hasta corregirlo)").Append('\n');
				if (!same) row.Note = "LEDGER_DIFIERE_ENTRE_HILOS";
			}
			TimingStats best = a0; foreach (TimingStats t in all) if (t.WallS < best.WallS) best = t;
			double wallBest = best.WallS * 1000.0 / best.N, wall0 = a0.WallS * 1000.0 / a0.N;
			double h10k = wallBest * 10000.0 / 3600000.0, h100k = wallBest * 100000.0 / 3600000.0;
			s.Append("extrapolacion con tiempo de PARED y ").Append(I(best.Applied)).Append(" hilo(s) (SOLO ET3, ").Append(F(wallBest, "0.000")).Append(" ms/eval; ET4-ET7 NO medidos; la cache en regimen real puede cambiar el costo): 10.000 evaluaciones = ").Append(F(h10k, "0.00")).Append(" h; 100.000 = ").Append(F(h100k, "0.00")).Append(" h; 30 campanas REDUCED = ").Append(F(30 * h10k, "0.0")).Append(" h; 30 FULL = ").Append(F(30 * h100k, "0.0")).Append(" h; 20 FULL = ").Append(F(20 * h100k, "0.0")).Append(" h\n");
			{ double hr = best.Wall2ndMsPerEval * 100000.0 / 3600000.0; s.Append("extrapolacion con el ms de PARED de la SEGUNDA MITAD (regimen con cache ya llena; mas fiel a una campana FULL que la media de todo el arranque en frio): ").Append(F(best.Wall2ndMsPerEval, "0.000")).Append(" ms/eval -> 100.000 evaluaciones = ").Append(F(hr, "0.00")).Append(" h; 30 FULL = ").Append(F(30 * hr, "0.0")).Append(" h; 20 FULL = ").Append(F(20 * hr, "0.0")).Append(" h (con >= 2.000 evaluaciones la segunda mitad todavia incluye llenado de cache: es una cota SUPERIOR del regimen; usar TIMING con evals>=10.000 para ver el regimen)\n"); }
			File.WriteAllText(Path.Combine(dir, "SUMMARY.txt"), s.ToString(), new UTF8Encoding(false));
			row.State = "TIMING"; row.FunnelStatus = "TIMING_MEDIDO"; row.Trials = I(a0.N); row.Seconds = all.Count == 1 ? a0.WallS : all.Sum(delegate(TimingStats t) { return t.WallS; }); row.PeakMb = all[all.Count - 1].PeakMb;
			row.Note = (row.Note.Length > 0 ? row.Note + " " : "") + "hilos=" + I(best.Applied) + " ms/eval_pared=" + F(wallBest, "0.000") + " ms/eval_hilo_media=" + F(best.Mean, "0.000") + " mediana=" + F(best.Median, "0.000") + " p90=" + F(best.P90, "0.000") + " mem_gest_mb=" + F(best.MemGestMb, "0");
			return row;
		}

		public static NQCRUN_BatchResult Run(string manifestText, long[] t, double[] o, double[] h, double[] l, double[] c, double[] vol, string defaultOutRoot, NQCRUN_BatchOptions opt)
		{
			Stopwatch total = Stopwatch.StartNew(); if (opt == null) opt = new NQCRUN_BatchOptions();
			string perr; NQCRUN_BatchManifest m = NQCRUN_BatchManifest.Parse(manifestText, out perr); if (m == null) throw new NQCRUN_Exception("RUN: manifiesto invalido: " + perr);
			string outRoot = m.OutRoot.Length > 0 ? m.OutRoot : Path.Combine(defaultOutRoot, "RUNBATCH_" + m.Name); Directory.CreateDirectory(outRoot);
			string logPath = Path.Combine(outRoot, "BATCH_LOG.txt"); Action<string> inner = opt.Log;
			opt.Log = delegate(string s) { if (inner != null) inner(s); try { File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + s + "\n"); } catch { } };
			NQCE_Config cfg = opt.Config != null ? opt.Config : new NQCE_Config();
			// guardas de datos, UNA vez (el runner las repite por campana)
			NQCRUN_Params gp0 = MakeParams(m, m.Campaigns[0], opt, cfg, outRoot);
			NQCRUN_DataReport rep = new NQCRUN_DataReport();
			string err = NQCRUN_Data.Check(t, o, h, l, c, vol, gp0.StrictIsRange, true, rep); if (err.Length > 0) throw new NQCRUN_Exception(err);
			NQCE_Bars rawBars = new NQCE_Bars(t, o, h, l, c); string rawSha = NQCRUN_Data.HashData(rawBars, vol);
			INQCE3_Library lib0 = opt.LibraryFactory != null ? opt.LibraryFactory() : new NQCE3_Library();
			double[] nullBest = opt.NullBest; if (nullBest == null && gp0.NullBestFile.Length > 0)
			{
				List<double> nb = new List<double>(); foreach (string line in File.ReadAllText(gp0.NullBestFile).Split('\n')) { string s = line.Trim(); if (s.Length == 0 || s[0] == '#') continue; nb.Add(double.Parse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture)); }
				nullBest = nb.ToArray(); opt.NullBest = nullBest;
			}
			string nullSha = "none"; if (nullBest != null) { StringBuilder nsb = new StringBuilder(); foreach (double dv in nullBest) nsb.Append(dv.ToString("R", CultureInfo.InvariantCulture)).Append('\n'); nullSha = NQCE_Const.Sha256Text(nsb.ToString()); }
			opt.Log("BATCH " + m.Name + " manifiesto_sha256=" + m.Sha.Substring(0, 16) + " campanas=" + I(m.Campaigns.Count) + " datos_sha256=" + rawSha.Substring(0, 16) + " barras=" + I(rep.Bars) + " sesiones=" + I(rep.Sessions));

			NQCRUN_BatchResult res = new NQCRUN_BatchResult(); res.OutRoot = outRoot; int errors = 0;
			foreach (NQCRUN_BatchCampaign c0 in m.Campaigns)
			{
				NQCRUN_BatchRow row; Stopwatch sw = Stopwatch.StartNew();
				try
				{
					NQCRUN_Params p = MakeParams(m, c0, opt, cfg, outRoot);
					if (c0.Size == "TIMING") row = RunTiming(m, c0, opt, cfg, outRoot, rawBars, vol, p);
					else
					{
						string ve = p.Validate(); if (ve.Length > 0) throw new NQCRUN_Exception("RUN: configuracion invalida: " + ve);
						NQCE3_Params gp = new NQCE3_Params(); gp.PopSize = p.Population; gp.Generations = p.Generations; gp.TopK = p.TopK; gp.Seed = p.Seed;
						string input = NQCE_Const.Sha256Text("batch=" + Version + "|spec=" + c0.Spec(m.Target, m.Tol) + "|raw=" + rawSha + "|library=" + lib0.Version + "|contract=" + NQCE_Const.ContractVersion + "|core=" + NQCE_Const.CoreVersion + "|engine=" + p.EngineHash + "|null_best=" + nullSha + "\n" + p.CanonicalText(gp));
						row = new NQCRUN_BatchRow(); row.Id = c0.Id; row.Transform = c0.Transform; row.Size = c0.Size; row.GaSeed = c0.GaSeed;
						string dir = p.OutDir; string sumPath = Path.Combine(dir, "SUMMARY.txt"), entPath = Path.Combine(dir, "BATCH_ENTRY.txt");
						Dictionary<string, string> en = p.Resume ? ReadEntry(entPath) : null;
						if (en != null && en["input_hash"] == input && File.Exists(sumPath) && NQCE7_Freeze.Sha256File(sumPath) == en["summary_sha256"] && File.ReadAllText(sumPath).Contains("deterministic_sha256=" + en["digest"] + "\n"))
						{
							row.State = "OK"; row.Resumed = true; row.FunnelStatus = en["status"]; row.Digest = en["digest"]; row.Trials = en["trials"];
							opt.Log("BATCH " + c0.Id + " SALTADA (SUMMARY valido y hash de entrada igual) digest=" + row.Digest.Substring(0, 12));
						}
						else
						{
							opt.Log("BATCH " + c0.Id + " transform=" + c0.Transform + " tseed=" + (c0.HasTSeed ? I(c0.TSeed) : "-") + " gaseed=" + I(c0.GaSeed) + " size=" + c0.Size + " ...");
							if (File.Exists(entPath)) File.Delete(entPath);
							p.Transform = MakeTransform(c0, m, cfg);
							NQCRUN_Result r = new NQCRUN_Funnel().Run(t, o, h, l, c, vol, p);
							NQCRUN_Ckpt s3 = r.Stage("ET3"), s4 = r.Stage("ET4"), s6 = r.Stage("ET6");
							int tri3 = s3 == null ? 0 : int.Parse(s3.M("fresh_evals"), CultureInfo.InvariantCulture), tri4 = s4 == null ? 0 : int.Parse(s4.M("fresh_trials"), CultureInfo.InvariantCulture), tri6 = s6 == null ? 0 : int.Parse(s6.M("trials"), CultureInfo.InvariantCulture);
							row.State = "OK"; row.FunnelStatus = r.Status; row.Digest = r.DeterministicSha; row.Trials = "ET3=" + I(tri3) + " acum=" + I(tri3 + tri4 + tri6); row.Seconds = r.TotalSeconds; row.PeakMb = r.PeakMemMb;
							NQCET2_InjectedN1 inj = p.Transform as NQCET2_InjectedN1; if (inj != null) row.Note = "inj_ok=" + (inj.Ok ? "1" : "0");
							File.WriteAllText(entPath, Entry(input, NQCE7_Freeze.Sha256File(sumPath), r.Status, r.DeterministicSha, row.Trials), new UTF8Encoding(false));
							opt.Log("BATCH " + c0.Id + " " + r.Status + " digest=" + r.DeterministicSha.Substring(0, 12) + " " + F(r.TotalSeconds, "0.0") + "s");
						}
					}
				}
				catch (Exception ex)
				{
					errors++; row = new NQCRUN_BatchRow(); row.Id = c0.Id; row.Transform = c0.Transform; row.Size = c0.Size; row.GaSeed = c0.GaSeed; row.State = "ERROR"; row.Note = ex.Message.Replace('\n', ' ').Replace('\t', ' ');
					opt.Log("BATCH " + c0.Id + " ERROR " + row.Note);
					try { Directory.CreateDirectory(Path.Combine(outRoot, c0.Id)); File.WriteAllText(Path.Combine(outRoot, c0.Id, "ERROR.txt"), ex.ToString()); } catch { }
				}
				sw.Stop(); if (row.Seconds <= 0 && !row.Resumed) row.Seconds = sw.ElapsedMilliseconds / 1000.0;
				res.Rows.Add(row);
			}
			res.Status = errors == 0 ? "COMPLETO" : "CON_ERRORES";
			StringBuilder d = new StringBuilder();
			d.Append("NQC RUN BATCH SUMMARY | ").Append(Version).Append(" | SOLO IS 2020-01-01..2022-12-31 | el OOS no se toca | sin congelado | SOLO DESARROLLO (no es evidencia hasta correr en NT8 con datos reales)\n");
			d.Append("batch=").Append(m.Name).Append(" status=").Append(res.Status).Append(" campaigns=").Append(I(m.Campaigns.Count)).Append(" runner=").Append(NQCRUN_Funnel.Version).Append(" library=").Append(lib0.Version).Append('\n');
			d.Append("manifest_sha256=").Append(m.Sha).Append(" target=").Append(m.Target.ToString("R", CultureInfo.InvariantCulture)).Append(" tol=").Append(m.Tol.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
			d.Append("data: bars=").Append(I(rep.Bars)).Append(" sessions=").Append(I(rep.Sessions)).Append(" first_day=").Append(I(rep.FirstDay)).Append(" last_day=").Append(I(rep.LastDay)).Append(" r2_zone=").Append(I(rep.ZoneHits)).Append('/').Append(I(rep.ZoneSessions)).Append(" data_raw_sha256=").Append(rawSha).Append('\n');
			d.Append("id\tstate\ttransform\tsize\tga_seed\ttrials\tfunnel_status\tdigest\n");
			foreach (NQCRUN_BatchRow r in res.Rows)
			{
				bool timing = r.Size == "TIMING";   // los tiempos no son deterministas: el digest del TIMING es NA y no entra su nota
				d.Append(r.Id).Append('\t').Append(r.State).Append('\t').Append(r.Transform).Append('\t').Append(r.Size).Append('\t').Append(I(r.GaSeed)).Append('\t').Append(r.Trials).Append('\t').Append(r.FunnelStatus).Append('\t').Append(timing ? "NA" : r.Digest).Append('\n');
			}
			res.BatchDigest = NQCE_Const.Sha256Text(d.ToString());
			StringBuilder s2 = new StringBuilder(d.ToString()); s2.Append("batch_digest_sha256=").Append(res.BatchDigest).Append('\n');
			s2.Append("--- no determinista (tiempos, memoria, reanudacion y notas; no entran en batch_digest_sha256) ---\nid\tresumed\tseconds\tpeak_working_set_mb\tnota\n");
			foreach (NQCRUN_BatchRow r in res.Rows) s2.Append(r.Id).Append('\t').Append(r.Resumed ? "SI" : "NO").Append('\t').Append(F(r.Seconds, "0.00")).Append('\t').Append(I(r.PeakMb)).Append('\t').Append(r.Note).Append('\n');
			total.Stop(); res.TotalSeconds = total.ElapsedMilliseconds / 1000.0; s2.Append("total_seconds=").Append(F(res.TotalSeconds, "0.00")).Append(" peak_working_set_mb=").Append(I(PeakMb())).Append('\n');
			res.Summary = s2.ToString(); File.WriteAllText(Path.Combine(outRoot, "SUMMARY.txt"), res.Summary, new UTF8Encoding(false));
			opt.Log("BATCH " + m.Name + " " + res.Status + " batch_digest=" + res.BatchDigest.Substring(0, 16) + " total=" + F(res.TotalSeconds, "0.0") + "s");
			opt.Log = inner; return res;
		}
	}

	// ======================================================================== estrategia NT8 (sin ordenes)
	public class NQC_RUN_Batch : Strategy
	{
		private List<long> tT = new List<long>(); private List<double> tO = new List<double>(), tH = new List<double>(), tL = new List<double>(), tC = new List<double>(), tV = new List<double>();
		private bool done, outsideIs, dataLoaded, gotBars; private int outsideDay; private string instName = "", fatal = "";

		private static string ResultsDir() { string d = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results"); Directory.CreateDirectory(d); return d; }

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Lanzador por lotes de campanas del embudo (sin ordenes). Manifiesto en NQC_Results\\RUN_BATCH_MANIFEST.txt. SOLO IS 2020-2022.";
				Name = "NQC_RUN_Batch";
				Calculate = Calculate.OnBarClose;
				BarsRequiredToTrade = 0;
				IsExitOnSessionCloseStrategy = false;
				TraceOrders = false;
			}
			else if (State == State.DataLoaded)
			{
				dataLoaded = true;
				instName = Instrument.FullName;
				if (!instName.StartsWith("NQ", StringComparison.Ordinal)) fatal = "instrumento " + instName + " no es NQ";
				else if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsPeriod.Value != 1) fatal = "la serie debe ser M1 (minuto, valor 1)";
			}
			else if (State == State.Terminated)
			{
				if (done) return; done = true;
				// instanciada por NT8 sin cargar datos (o sin barras): no hace nada, sin error ni archivos
				if (!NQC_RUN_Funnel.ShouldRun(dataLoaded, gotBars)) { if (dataLoaded) Print("NQC_RUN_INFO la estrategia cargo datos pero no recibio barras en esta ejecucion; no se ejecuta nada"); return; }
				try { RunAll(); }
				catch (Exception ex)
				{
					Print("NQC_RUN_ERROR " + ex.Message);
					try { File.WriteAllText(Path.Combine(ResultsDir(), "RUN_BATCH_ERROR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt"), ex.ToString()); } catch { }
				}
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) return;
			gotBars = true;
			if (fatal.Length > 0) return;
			DateTime c = Time[0]; int day = c.Year * 10000 + c.Month * 100 + c.Day;
			if (day < NQCRUN_Data.IsFromKey || day > NQCRUN_Data.IsToKey) { if (!outsideIs) { outsideIs = true; outsideDay = day; } return; }   // no se almacena nada fuera del IS
			tT.Add(c.Ticks); tO.Add(Open[0]); tH.Add(High[0]); tL.Add(Low[0]); tC.Add(Close[0]); tV.Add(Volume[0]);
		}

		private void RunAll()
		{
			if (fatal.Length > 0) throw new NQCRUN_Exception("RUN: " + fatal);
			if (outsideIs) throw new NQCRUN_Exception("RUN: el Strategy Analyzer entrego barras fuera del IS (primer DayKey " + outsideDay.ToString(CultureInfo.InvariantCulture) + "); el OOS NUNCA se toca aqui. Fija 2020-01-01..2022-12-31");
			if (tT.Count == 0) throw new NQCRUN_Exception("RUN: sin barras");
			string mPath = Path.Combine(ResultsDir(), "RUN_BATCH_MANIFEST.txt");
			if (!File.Exists(mPath)) throw new NQCRUN_Exception("RUN: falta el manifiesto " + mPath);
			string text = File.ReadAllText(mPath);
			NQCRUN_BatchOptions opt = new NQCRUN_BatchOptions();
			opt.EngineHash = NQCRUN_Hashes.EngineHash(Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "bin", "Custom", "Strategies"));
			opt.Log = delegate(string s) { Print("NQC_RUN " + s); };
			NQCRUN_BatchResult r = NQCRUN_Batch.Run(text, tT.ToArray(), tO.ToArray(), tH.ToArray(), tL.ToArray(), tC.ToArray(), tV.ToArray(), ResultsDir(), opt);
			try { File.WriteAllText(Path.Combine(r.OutRoot, "RUN_BATCH_MANIFEST_USED.txt"), text, new UTF8Encoding(false)); } catch { }
			Print("NQC_RUN_END batch status=" + r.Status + " batch_digest=" + r.BatchDigest.Substring(0, 12) + " dir=" + r.OutRoot);
		}
	}
}
