#region Using declarations
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

//==============================================================================
// NQC_RUN_Funnel - WP6 runner del embudo ET3 -> ET4 -> ET5 -> ET6 -> ET7 en NinjaTrader 8 (Strategy Analyzer, NQ M1). SOLO DESARROLLO.
// Estrategia SIN ordenes. Carga TODAS las barras M1 del IS (con volumen), bloquea cualquier fecha fuera de 2020-01-01..2022-12-31 (el OOS NUNCA se toca aqui),
// verifica R2 (zona horaria, chequeo empirico de ET1 Regression), construye NQCE_Bars + volumen, hace Bind de la biblioteca, aplica un TRANSFORMADOR de dataset
// opcional (interfaz NQCRUN_IDatasetTransform; la identidad es el defecto) y orquesta el embudo con los modulos reales:
//   ET3 campana (NQCE3_Campaign: Ledger/Operators/Fitness) -> ET4 poda (NQCE4_Prune) -> ET5 robustez (NQCE5_Robust) -> ET6 portafolios (NQCE6_Builder) -> ET7 evidencia (NQCE7_*).
// El runner NO congela ni abre el OOS. El congelado (NQCE7_Freeze) tiene su propio flag (freeze=1), exige ademas et2_approved=1 y esta DESACTIVADO por defecto.
//
// ADAPTADORES (escritos aqui; no se editaron ET1/ET4/ET5/ET6/ET7):
//   A1 databank ET3 -> candidatos ET4 (genoma, huella). El genoma viaja en NQCE3_Eval.Genome (campo aditivo de ET3).
//   A2 resultados ET4 (QNet/QTrades por trimestre a costo 1,0x) -> matriz candidatos x 12 trimestres de ET5 (NQCE5_Candidate).
//   A3 pool robusto de ET5 (ids) + databank -> NQCE6_PoolEntry (genoma, fitness IS de ET3).
//   A4 finalistas ET6 (ids de miembros en orden de prioridad) -> portafolio del ejecutor -> serie diaria de P&L (NQCE_Result.DailyNet) y lista de operaciones
//      NQCE7_TradeRec (indice de sesion, neto, minimo abierto = -MAE*valor_punto - costo, misma convencion de costo abierto que el DD M2M del ejecutor).
//   A5 series para el N_eff de ET7: una serie diaria por entrada del databank de ET3 (cobertura = series / N bruto; con neff_coverage_min = 1,0 de D-036 falla cerrado: el
//      config de ET7 debe declarar la cobertura aceptada ANTES de correr) mas una por finalista. N bruto = ET3 + vecinos nuevos de ET4 + combinaciones de ET6.
//
// CONFIGURACION: NQC_Results\RUN_FUNNEL_CONFIG.txt (clave=valor; ver NQCRUN_Params.Parse). Todo cambio de parametros cambia el hash de la corrida.
// CHECKPOINT: cada etapa deja CKPT_<ETn>.txt (hash de entrada, hash de cada archivo de salida, contadores) como ULTIMO paso; al reanudar (resume=1) se reutiliza la etapa si
//   la entrada y todos los hashes coinciden; si no, se recalcula esa etapa y las siguientes. No hay checkpoint DENTRO de la campana ET3 (C1 interrumpida mid-campana se repite entera).
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	// Interfaz comun de transformadores de dataset (la implementa ET2 con generadores nulos/inyectados). Name y ParamsText entran en el hash de la corrida.
	public interface NQCRUN_IDatasetTransform
	{
		string Name { get; }
		string ParamsText { get; }
		void Apply(NQCE_Bars bars, double[] volume, out NQCE_Bars outBars, out double[] outVolume);
	}

	// Opcional: un transformador que acepta argumentos de texto desde la configuracion (transform_args=...).
	public interface NQCRUN_IConfigurableTransform { void Configure(string args); }

	public sealed class NQCRUN_IdentityTransform : NQCRUN_IDatasetTransform
	{
		public string Name { get { return "IDENTITY"; } }
		public string ParamsText { get { return ""; } }
		public void Apply(NQCE_Bars bars, double[] volume, out NQCE_Bars outBars, out double[] outVolume) { outBars = bars; outVolume = volume; }
	}

	public sealed class NQCRUN_Exception : Exception { public NQCRUN_Exception(string m) : base(m) { } }

	public sealed class NQCRUN_Params
	{
		public string Campaign = "C1"; public long Seed = 20261114; public bool SeedGiven;
		public int Population = 500, Generations = 200, MaxEvals = 100000, TopK = 2000;
		public int MemoBudgetMb = 2048;
		public bool StrictIsRange = true, Resume = true;
		public string OutDir = "", TransformName = "", TransformArgs = "", NullBestFile = "", Commit = "", EngineDir = "", StopAfter = "", FrozenUtc = "";
		public bool Freeze, Et2Approved;
		public int NeffSample = NQCE3_Reservoir.DefaultK;   // D-038: tamano del reservorio de series diarias de los ensayos de ET3 (0 = sin muestreo)
		public bool Calibration;                           // campana de CALIBRACION (ET2, datos transformados; la fija el lanzador por lotes): etiqueta libre en vez de C1..C5, no consume las 5 campanas
		public int Et6MaxPool = 30, Et6BeamWidth = 10; public double Et6PpassMin = double.NaN;
		public string Et7Text = "";
		// D-038 integrado (NQCRUN-1.2.0): con neff_sample>0 el veredicto de ET7 usa neff_method=SAMPLE_CHAO1 con neff_bound=UPPER. Las claves que el usuario no declare se completan con los defectos
		// propuestos y quedan en la config EFECTIVA (ET7_CONFIG.txt y config_sha256 la sellan). Una clave declarada por el usuario manda. neff_sample=0 deja el texto intacto (modo COVERED, hashes anteriores).
		public string EffectiveEt7Text()
		{
			if (NeffSample <= 0) return Et7Text;
			System.Collections.Generic.HashSet<string> have = new System.Collections.Generic.HashSet<string>();
			foreach (string raw in Et7Text.Split('\n')) { string l = raw.Trim(); int q = l.IndexOf('='); if (l.Length > 0 && l[0] != '#' && q > 0) have.Add(l.Substring(0, q).Trim()); }
			StringBuilder b = new StringBuilder(Et7Text); if (b.Length > 0 && b[b.Length - 1] != '\n') b.Append('\n');
			string[][] d = new string[][] { new string[] { "neff_method", "SAMPLE_CHAO1" }, new string[] { "neff_bound", "UPPER" }, new string[] { "neff_sample_cov_min", "0.90" }, new string[] { "neff_sample_min_n", "1000" } };
			foreach (string[] kv in d) if (!have.Contains(kv[0])) b.Append(kv[0]).Append('=').Append(kv[1]).Append('\n');
			return b.ToString();
		}
		// Inyecciones de codigo (no vienen del archivo de configuracion)
		public NQCRUN_IDatasetTransform Transform;                 // si es null se usa TransformName (reflexion) o la identidad
		public Func<INQCE3_Library> LibraryFactory;                // null = new NQCE3_Library()
		public NQCE_Config Config = new NQCE_Config();             // costo 1,0x, sin capa protectora, dias excluidos por defecto
		public double[] NullBest;                                  // vector nulo de ET2 (mejor t por corrida nula); null = se lee NullBestFile; sin vector la puerta de percentil falla cerrada
		public string EngineHash = "";                             // hash del motor para el spec de ET6 (NQCRUN_Hashes.EngineHash)
		public Action<string> Log;

		private static double D(string v) { return double.Parse(v.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture); }
		private static bool B(string v) { v = v.Trim(); if (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase)) return true; if (v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase)) return false; throw new FormatException(v); }

		public static NQCRUN_Params Parse(string text, out string error)
		{
			error = ""; NQCRUN_Params p = new NQCRUN_Params(); StringBuilder e7 = new StringBuilder();
			foreach (string raw in (text ?? "").Split('\n'))
			{
				string line = raw.Trim(); if (line.Length == 0 || line[0] == '#') continue;
				int eq = line.IndexOf('='); if (eq <= 0) { error = "linea invalida: " + line; return null; }
				string k = line.Substring(0, eq).Trim().ToLowerInvariant(), v = line.Substring(eq + 1).Trim();
				try
				{
					if (k.StartsWith("et7.", StringComparison.Ordinal)) { e7.Append(k.Substring(4)).Append('=').Append(v).Append('\n'); continue; }
					switch (k)
					{
						case "campaign": p.Campaign = v.ToUpperInvariant(); break;
						case "seed": p.Seed = long.Parse(v, CultureInfo.InvariantCulture); p.SeedGiven = true; break;
						case "population": p.Population = int.Parse(v, CultureInfo.InvariantCulture); break;
						case "generations": p.Generations = int.Parse(v, CultureInfo.InvariantCulture); break;
						case "max_evals": p.MaxEvals = int.Parse(v, CultureInfo.InvariantCulture); break;
						case "top_k": p.TopK = int.Parse(v, CultureInfo.InvariantCulture); break;
						case "memo_budget_mb": p.MemoBudgetMb = int.Parse(v, CultureInfo.InvariantCulture); break;
						case "strict_is_range": p.StrictIsRange = B(v); break;
						case "resume": p.Resume = B(v); break;
						case "out_dir": p.OutDir = v; break;
						case "transform": p.TransformName = v; break;
						case "transform_args": p.TransformArgs = v; break;
						case "null_best_file": p.NullBestFile = v; break;
						case "commit": p.Commit = v; break;
						case "freeze": p.Freeze = B(v); break;
						case "et2_approved": p.Et2Approved = B(v); break;
						case "neff_sample": p.NeffSample = int.Parse(v, CultureInfo.InvariantCulture); break;
						case "engine_dir": p.EngineDir = v; break;
						case "stop_after": p.StopAfter = v.ToUpperInvariant(); break;
						case "et6.max_pool": p.Et6MaxPool = int.Parse(v, CultureInfo.InvariantCulture); break;
						case "et6.beam_width": p.Et6BeamWidth = int.Parse(v, CultureInfo.InvariantCulture); break;
						case "et6.ppass_min": p.Et6PpassMin = v.Length == 0 ? double.NaN : D(v); break;
						default: error = "clave desconocida: " + k; return null;
					}
				}
				catch (FormatException) { error = "valor ilegible en " + k; return null; }
				catch (OverflowException) { error = "valor fuera de rango en " + k; return null; }
			}
			p.Et7Text = e7.ToString();
			return p;
		}

		// Reglas duras (contrato ET3, D-019, D-029). Devuelve "" si todo esta bien.
		public string Validate()
		{
			if (Calibration)
			{
				if (Campaign.Length < 1 || Campaign.Length > 40) return "campana de calibracion: etiqueta de 1..40 caracteres";
				foreach (char ch in Campaign) if (!((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_' || ch == '-')) return "campana de calibracion: etiqueta solo con letras, digitos, _ y -";
				if (!SeedGiven || Seed == 20261114) return "campana de calibracion " + Campaign + ": la semilla del GA debe declararse y no puede ser la de C1";
			}
			else
			{
				if (Campaign.Length != 2 || Campaign[0] != 'C' || Campaign[1] < '1' || Campaign[1] > '5') return "campaign debe ser C1..C5 (maximo 5 campanas)";
				if (Campaign != "C1" && (!SeedGiven || Seed == 20261114)) return "campana " + Campaign + ": la semilla debe declararse antes (clave seed) y no puede reutilizar la de C1";
			}
			if (NeffSample < 0 || NeffSample > 100000) return "neff_sample fuera de 0..100000";
			if (Population < 1 || Generations < 1 || TopK < 1) return "population, generations y top_k deben ser >= 1";
			if (MaxEvals < 1 || MaxEvals > NQCE3_Campaign.MaxEvaluations) return "max_evals fuera de 1..100000 (tope duro D-019)";
			if (MemoBudgetMb < 16 || MemoBudgetMb > 16384) return "memo_budget_mb fuera de 16..16384";
			if (Et6MaxPool < 1 || Et6BeamWidth < 1) return "et6.max_pool y et6.beam_width deben ser >= 1";
			if (!(StopAfter == "" || StopAfter == "ET3" || StopAfter == "ET4" || StopAfter == "ET5" || StopAfter == "ET6")) return "stop_after debe ser vacio o ET3/ET4/ET5/ET6";
			string e7; if (NQCE7_Config.Parse(EffectiveEt7Text(), out e7) == null) return "config de ET7 invalida: " + e7;
			if (Freeze)
			{
				if (!Et2Approved) return "freeze=1 exige et2_approved=1 (el congelado real solo tras ET2 aprobada)";
				if (!NQCE7_Freeze.IsHex(Commit, 7, 40)) return "freeze=1 exige commit hexadecimal (git rev-parse HEAD)";
				if (EngineDir.Length == 0 || !Directory.Exists(EngineDir)) return "freeze=1 exige engine_dir existente (carpeta con los NQC_*.cs del motor)";
			}
			return "";
		}

		// Texto canonico de TODO lo que influye en los resultados (orden fijo). No incluye memoria, resume, ruta de salida ni stop_after.
		public string CanonicalText(NQCE3_Params gp)
		{
			StringBuilder b = new StringBuilder();
			b.Append("campaign=").Append(Campaign).Append("|seed=").Append(Seed.ToString(CultureInfo.InvariantCulture)).Append("|max_evals=").Append(MaxEvals.ToString(CultureInfo.InvariantCulture)).Append('\n');
			b.Append("ga: pop=").Append(gp.PopSize.ToString(CultureInfo.InvariantCulture)).Append(" gen=").Append(gp.Generations.ToString(CultureInfo.InvariantCulture)).Append(" tournament=").Append(gp.Tournament.ToString(CultureInfo.InvariantCulture))
			 .Append(" mut=").Append(gp.MutationRate.ToString("R", CultureInfo.InvariantCulture)).Append(" elite=").Append(gp.EliteFrac.ToString("R", CultureInfo.InvariantCulture)).Append(" imm=").Append(gp.ImmigrantFrac.ToString("R", CultureInfo.InvariantCulture))
			 .Append(" topk=").Append(gp.TopK.ToString(CultureInfo.InvariantCulture)).Append(" initmax=").Append(gp.InitMaxActive.ToString(CultureInfo.InvariantCulture)).Append(" inittries=").Append(gp.MaxInitTries.ToString(CultureInfo.InvariantCulture)).Append('\n');
			b.Append("cfg: ").Append(NQCRUN_Funnel.CfgText(Config)).Append('\n');
			b.Append("et6: max_pool=").Append(Et6MaxPool.ToString(CultureInfo.InvariantCulture)).Append(" beam=").Append(Et6BeamWidth.ToString(CultureInfo.InvariantCulture)).Append(" ppass_min=").Append(double.IsNaN(Et6PpassMin) ? "NaN" : Et6PpassMin.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
			b.Append("et7_config_sha256=").Append(NQCE_Const.Sha256Text(EffectiveEt7Text())).Append('\n');
			b.Append("neff_sample=").Append(NeffSample.ToString(CultureInfo.InvariantCulture)).Append('\n');
			b.Append("freeze=").Append(Freeze ? "1" : "0").Append(" strict_is_range=").Append(StrictIsRange ? "1" : "0").Append(" commit=").Append(Commit).Append('\n');
			return b.ToString();
		}
	}

	// Resultado de la verificacion de datos (R2 y demas).
	public sealed class NQCRUN_DataReport
	{
		public int Bars, Sessions, FirstDay, LastDay, ZoneSessions, ZoneHits; public double WarmupCoverage, ZoneFrac; public bool ZoneUsable, ZoneChecked;
	}

	public static class NQCRUN_Data
	{
		public const int IsFromKey = 20200101, IsToKey = 20221231;
		public const int StrictFirstMax = 20200110, StrictLastMin = 20221223;   // el rango del Strategy Analyzer debe ser el IS completo (primer dia habil de enero 2020 .. ultimos dias de dic 2022)
		public const int Probe0931 = 9 * 60 + 31, MaxPeriodBars = 7 * 30 + 10;  // mismos valores que NQCE3_Fitness

		private static string I(long v) { return v.ToString(CultureInfo.InvariantCulture); }

		// Devuelve "" si los datos son aptos o el motivo del error. zoneCheck: R2 empirico de zona horaria (solo sobre los datos reales; los transformados ya no tienen por que conservar el pico).
		public static string Check(long[] t, double[] o, double[] h, double[] l, double[] c, double[] v, bool strictRange, bool zoneCheck, NQCRUN_DataReport rep)
		{
			if (t == null || o == null || h == null || l == null || c == null || t.Length == 0) return "RUN: sin barras";
			int n = t.Length;
			if (o.Length != n || h.Length != n || l.Length != n || c.Length != n) return "RUN: arreglos OHLC de distinta longitud";
			if (v == null || v.Length != n) return "RUN: se exige volumen alineado con las barras (longitud " + (v == null ? "nula" : I(v.Length)) + " vs " + I(n) + ")";
			int[] day = new int[n], stamp = new int[n];
			for (int i = 0; i < n; i++)
			{
				if (double.IsNaN(o[i]) || double.IsInfinity(o[i]) || double.IsNaN(h[i]) || double.IsInfinity(h[i]) || double.IsNaN(l[i]) || double.IsInfinity(l[i]) || double.IsNaN(c[i]) || double.IsInfinity(c[i])) return "RUN: OHLC no finito en la barra " + I(i);
				if (double.IsNaN(v[i]) || double.IsInfinity(v[i]) || v[i] < 0) return "RUN: volumen no finito o negativo en la barra " + I(i);
				if (i > 0 && t[i] <= t[i - 1]) return "RUN: tiempos no estrictamente crecientes en la barra " + I(i);
				DateTime d = new DateTime(t[i]);
				day[i] = d.Year * 10000 + d.Month * 100 + d.Day; stamp[i] = d.Hour * 60 + d.Minute;
				if (day[i] < IsFromKey || day[i] > IsToKey)
					return "RUN: barra fuera del IS 2020-01-01..2022-12-31 (DayKey=" + I(day[i]) + ", barra " + I(i) + "): el OOS NUNCA se toca aqui; ajusta el rango del Strategy Analyzer";
			}
			rep.Bars = n; rep.FirstDay = day[0]; rep.LastDay = day[n - 1];
			int sess = 1; for (int i = 1; i < n; i++) if (day[i] != day[i - 1]) sess++;
			rep.Sessions = sess;
			if (strictRange && (day[0] > StrictFirstMax || day[n - 1] < StrictLastMin))
				return "RUN: el rango no es el IS completo (primer dia " + I(day[0]) + " > " + I(StrictFirstMax) + " o ultimo dia " + I(day[n - 1]) + " < " + I(StrictLastMin) + "); fija 2020-01-01..2022-12-31 en el Strategy Analyzer";
			// M1 completas desde 00:00: SameDay exige 7n+10 barras del mismo dia antes de la senal de las 09:31 (igual que NQCE3_Fitness)
			int days = 0, ok = 0;
			for (int i = 0; i < n; i++)
			{
				if (stamp[i] != Probe0931) continue;
				days++; int j = i; while (j > 0 && day[j - 1] == day[i]) j--;
				if (i - j >= MaxPeriodBars) ok++;
			}
			rep.WarmupCoverage = days == 0 ? 0.0 : (double)ok / days;
			if (days == 0 || rep.WarmupCoverage < 0.9)
				return "RUN: las barras no son M1 completas desde 00:00 (cobertura de calentamiento " + rep.WarmupCoverage.ToString("0.00", CultureInfo.InvariantCulture) + " < 0,90)";
			if (!zoneCheck) return "";
			// R2 empirico de zona horaria (misma logica que NQC_ET1_Regression.RunAll): el maximo de volumen 09:16-10:00 debe caer en la barra 09:31/09:32 en >= 70% de las sesiones.
			int tzSess = 0, tzHit = 0; double tzVol = 0; { int cur = -1; double bestV = -1; int bestM = -1;
				for (int i = 0; i <= n; i++)
				{
					int dd = i < n ? day[i] : -2;
					if (dd != cur) { if (bestM >= 0) { tzSess++; if (bestM == 571 || bestM == 572) tzHit++; } cur = dd; bestV = -1; bestM = -1; }
					if (i >= n) break;
					tzVol += v[i];
					if (stamp[i] >= 556 && stamp[i] <= 600 && v[i] > bestV && v[i] > 0) { bestV = v[i]; bestM = stamp[i]; }
				} }
			rep.ZoneChecked = true; rep.ZoneSessions = tzSess; rep.ZoneHits = tzHit; rep.ZoneUsable = tzVol > 0 && tzSess >= 20; rep.ZoneFrac = rep.ZoneUsable ? 100.0 * tzHit / tzSess : 0;
			if (!rep.ZoneUsable) return "RUN: R2 no verificable (serie sin volumen utilizable o sesiones=" + I(tzSess) + " < 20); el embudo exige volumen";
			if (rep.ZoneFrac < 70.0) return "RUN: ABORTADO (R2): la hora de las barras no parece Eastern (pico de volumen 09:16-10:00 en barra 09:31/09:32 en " + I(tzHit) + "/" + I(tzSess) + " sesiones = " + rep.ZoneFrac.ToString("0.00", CultureInfo.InvariantCulture) + "% < 70%). Revisa Tools > Options > General > Time zone de NT8";
			return "";
		}

		public static string HashData(NQCE_Bars b, double[] vol)
		{
			using (SHA256 sha = SHA256.Create())
			{
				const int chunk = 65536; byte[] buf = new byte[chunk * 8];
				Feed(sha, b.T, buf, chunk); Feed(sha, b.O, buf, chunk); Feed(sha, b.H, buf, chunk); Feed(sha, b.L, buf, chunk); Feed(sha, b.C, buf, chunk); Feed(sha, vol, buf, chunk);
				sha.TransformFinalBlock(new byte[0], 0, 0);
				return NQCE_Const.Hex(sha.Hash);
			}
		}
		private static void Feed(SHA256 sha, Array a, byte[] buf, int chunk)
		{
			int n = ((Array)a).Length;
			for (int i = 0; i < n; i += chunk) { int len = Math.Min(chunk, n - i); Buffer.BlockCopy(a, i * 8, buf, 0, len * 8); sha.TransformBlock(buf, 0, len * 8, null, 0); }
		}
	}

	public static class NQCRUN_Hashes
	{
		// Hash del motor: SHA256 de las lineas "nombre=sha" de los NQC_*.cs de la carpeta, ordenadas. "NA" si la carpeta no existe.
		public static string EngineHash(string dir)
		{
			try
			{
				if (dir == null || dir.Length == 0 || !Directory.Exists(dir)) return "NA";
				string[] files = Directory.GetFiles(dir, "NQC_*.cs"); Array.Sort(files, StringComparer.Ordinal);
				StringBuilder b = new StringBuilder();
				foreach (string f in files) b.Append(Path.GetFileName(f)).Append('=').Append(NQCE7_Freeze.Sha256File(f)).Append('\n');
				return NQCE_Const.Sha256Text(b.ToString());
			}
			catch { return "NA"; }
		}
	}

	// Candidato del databank de ET3 con su genoma (A1).
	public sealed class NQCRUN_Cand
	{
		public string TrialId, Fingerprint, GenomeText; public NQCE3_Genome Genome; public double Fitness, MeanNet; public int Trades;
	}

	// Marcador de checkpoint de una etapa.
	public sealed class NQCRUN_Ckpt
	{
		public string Stage, InputHash;
		public SortedDictionary<string, string> Files = new SortedDictionary<string, string>(StringComparer.Ordinal);   // ruta relativa -> sha256
		public SortedDictionary<string, string> Meta = new SortedDictionary<string, string>(StringComparer.Ordinal);    // deterministas
		public SortedDictionary<string, string> Perf = new SortedDictionary<string, string>(StringComparer.Ordinal);    // tiempos/memoria (no entran en el digest)
		public bool Resumed;
		public NQCRUN_Ckpt(string stage, string inputHash) { Stage = stage; InputHash = inputHash; }
		public string OutputHash() { StringBuilder b = new StringBuilder(); foreach (KeyValuePair<string, string> kv in Files) b.Append(kv.Key).Append('=').Append(kv.Value).Append('\n'); return NQCE_Const.Sha256Text(b.ToString()); }
		public string M(string k) { string v; return Meta.TryGetValue(k, out v) ? v : "NA"; }
		public string P(string k) { string v; return Perf.TryGetValue(k, out v) ? v : "NA"; }
		public string Text()
		{
			StringBuilder b = new StringBuilder();
			b.Append("stage=").Append(Stage).Append('\n').Append("input_hash=").Append(InputHash).Append('\n');
			foreach (KeyValuePair<string, string> kv in Files) b.Append("file=").Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
			foreach (KeyValuePair<string, string> kv in Meta) b.Append("meta.").Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
			foreach (KeyValuePair<string, string> kv in Perf) b.Append("perf.").Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
			return b.ToString();
		}
		public static NQCRUN_Ckpt Parse(string text)
		{
			NQCRUN_Ckpt c = new NQCRUN_Ckpt("", "");
			foreach (string line in text.Split('\n'))
			{
				if (line.Length == 0) continue;
				if (line.StartsWith("stage=", StringComparison.Ordinal)) c.Stage = line.Substring(6);
				else if (line.StartsWith("input_hash=", StringComparison.Ordinal)) c.InputHash = line.Substring(11);
				else if (line.StartsWith("file=", StringComparison.Ordinal)) { string[] f = line.Substring(5).Split('\t'); if (f.Length != 2) return null; c.Files[f[0]] = f[1]; }
				else if (line.StartsWith("meta.", StringComparison.Ordinal)) { int e = line.IndexOf('='); if (e < 0) return null; c.Meta[line.Substring(5, e - 5)] = line.Substring(e + 1); }
				else if (line.StartsWith("perf.", StringComparison.Ordinal)) { int e = line.IndexOf('='); if (e < 0) return null; c.Perf[line.Substring(5, e - 5)] = line.Substring(e + 1); }
				else return null;
			}
			return c.Stage.Length == 0 ? null : c;
		}
	}

	public sealed class NQCRUN_Result
	{
		public string Status = "", Summary = "", DeterministicSha = "", OutDir = "", RunInputSha = "";
		public List<NQCRUN_Ckpt> Stages = new List<NQCRUN_Ckpt>();
		public double TotalSeconds; public long PeakMemMb;
		public NQCRUN_Ckpt Stage(string name) { foreach (NQCRUN_Ckpt c in Stages) if (c.Stage == name) return c; return null; }
	}

	public sealed class NQCRUN_Funnel
	{
		public const string Version = "NQCRUN-1.2.0";
		private NQCRUN_Params P; private string dir; private Action<string> log;
		private static string I(long v) { return v.ToString(CultureInfo.InvariantCulture); }
		private static string R(double v) { return double.IsNaN(v) ? "NaN" : (double.IsPositiveInfinity(v) ? "INF" : (double.IsNegativeInfinity(v) ? "-INF" : v.ToString("R", CultureInfo.InvariantCulture))); }
		private static double PR(string s) { if (s == "NaN") return double.NaN; if (s == "INF") return double.PositiveInfinity; if (s == "-INF") return double.NegativeInfinity; return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture); }

		public static string CfgText(NQCE_Config c)
		{
			List<int> ex = new List<int>(c.Excluded); ex.Sort(); StringBuilder e = new StringBuilder(); foreach (int d in ex) e.Append(d.ToString(CultureInfo.InvariantCulture)).Append(',');
			return "cost_rt=" + R(c.CostRT) + " cost_mult=" + R(c.CostMult) + " protect=" + (c.Protect ? "1" : "0") + " pause=" + R(c.PauseLevel) + " close=" + R(c.CloseLevel) + " cushion=" + R(c.Cushion)
				+ " perm_diag=" + (c.PermanentDiagnostic ? "1" : "0") + " profile=" + c.Profile.Name + " news=" + (c.News == null ? "none" : "present") + " excluded_sha=" + NQCE_Const.Sha256Text(e.ToString()).Substring(0, 16) + " excluded_n=" + I(ex.Count) + " seed=" + I(c.Seed);
		}

		private void Log(string s) { if (log != null) log(s); }

		// ------------------------------------------------------------------ transformador por nombre (reflexion sobre el ensamblado del runner)
		public static NQCRUN_IDatasetTransform FindTransform(string name, string args)
		{
			if (name == null || name.Length == 0 || name == "IDENTITY") return new NQCRUN_IdentityTransform();
			NQCRUN_IDatasetTransform found = null; int hits = 0;
			foreach (Type ty in typeof(NQCRUN_Funnel).Assembly.GetTypes())
			{
				if (ty.IsAbstract || ty.IsInterface || !typeof(NQCRUN_IDatasetTransform).IsAssignableFrom(ty) || ty.GetConstructor(Type.EmptyTypes) == null) continue;
				NQCRUN_IDatasetTransform tr; try { tr = (NQCRUN_IDatasetTransform)Activator.CreateInstance(ty); } catch { continue; }
				if (tr.Name == name || ty.Name == name) { found = tr; hits++; }
			}
			if (hits == 0) throw new NQCRUN_Exception("RUN: transformador no encontrado: " + name);
			if (hits > 1) throw new NQCRUN_Exception("RUN: transformador ambiguo (" + I(hits) + " coincidencias): " + name);
			if (args != null && args.Length > 0)
			{
				NQCRUN_IConfigurableTransform ct = found as NQCRUN_IConfigurableTransform;
				if (ct == null) throw new NQCRUN_Exception("RUN: el transformador " + name + " no acepta transform_args");
				ct.Configure(args);
			}
			return found;
		}

		// ------------------------------------------------------------------ checkpoint
		private string CkptPath(string stage) { return Path.Combine(dir, "CKPT_" + stage + ".txt"); }
		private NQCRUN_Ckpt TryResume(string stage, string input)
		{
			if (!P.Resume) return null;
			try
			{
				string p = CkptPath(stage); if (!File.Exists(p)) return null;
				NQCRUN_Ckpt c = NQCRUN_Ckpt.Parse(File.ReadAllText(p)); if (c == null || c.Stage != stage || c.InputHash != input) return null;
				foreach (KeyValuePair<string, string> kv in c.Files)
				{
					string f = Path.Combine(dir, kv.Key.Replace('/', Path.DirectorySeparatorChar));
					if (!File.Exists(f) || NQCE7_Freeze.Sha256File(f) != kv.Value) return null;
				}
				c.Resumed = true; return c;
			}
			catch { return null; }
		}
		private void Commit(NQCRUN_Ckpt c)
		{
			string tmp = CkptPath(c.Stage) + ".tmp";
			File.WriteAllText(tmp, c.Text(), new UTF8Encoding(false));
			if (File.Exists(CkptPath(c.Stage))) File.Delete(CkptPath(c.Stage));
			File.Move(tmp, CkptPath(c.Stage));
		}
		private void Invalidate(string stage) { try { if (File.Exists(CkptPath(stage))) File.Delete(CkptPath(stage)); } catch { } }
		private string Put(NQCRUN_Ckpt c, string rel, string text)
		{
			string f = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(f));
			File.WriteAllText(f, text, new UTF8Encoding(false)); string sha = NQCE7_Freeze.Sha256File(f); c.Files[rel] = sha; return sha;
		}
		private void Reg(NQCRUN_Ckpt c, string rel) { c.Files[rel] = NQCE7_Freeze.Sha256File(Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar))); }
		private string Read(string rel) { return File.ReadAllText(Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar))); }
		private static long MemMb() { return GC.GetTotalMemory(false) / 1048576; }
		private static long PeakMb() { try { return Process.GetCurrentProcess().PeakWorkingSet64 / 1048576; } catch { return -1; } }

		// ------------------------------------------------------------------ A1/A2/A3 serializacion de candidatos
		private static string DatabankTsv(List<NQCE3_Eval> top)
		{
			StringBuilder s = new StringBuilder("rank\ttrial_id\tfitness_t\ttrades\tmean_net\tfingerprint\tgenes\tgenome\n"); int r = 1;
			foreach (NQCE3_Eval e in top)
			{
				s.Append(I(r++)).Append('\t').Append(e.TrialId).Append('\t').Append(R(e.Fitness)).Append('\t').Append(I(e.Trades)).Append('\t').Append(R(e.MeanNet)).Append('\t').Append(e.TradeFingerprint).Append('\t');
				for (int g = 0; g < NQCE3_Genome.GeneCount; g++) { if (g > 0) s.Append(','); s.Append(I(e.Genome.G[g])); }
				s.Append('\t').Append(e.GenomeText).Append('\n');
			}
			return s.ToString();
		}
		private static List<NQCRUN_Cand> ParseDatabank(string text, INQCE3_Library lib)
		{
			List<NQCRUN_Cand> l = new List<NQCRUN_Cand>(); string[] lines = text.Split('\n');
			for (int i = 1; i < lines.Length; i++)
			{
				if (lines[i].Length == 0) continue;
				string[] f = lines[i].Split('\t'); if (f.Length != 8) throw new NQCRUN_Exception("RUN: databank corrupto (linea " + I(i + 1) + ")");
				NQCRUN_Cand c = new NQCRUN_Cand(); c.TrialId = f[1]; c.Fitness = PR(f[2]); c.Trades = int.Parse(f[3], CultureInfo.InvariantCulture); c.MeanNet = PR(f[4]); c.Fingerprint = f[5]; c.GenomeText = f[7];
				string[] gs = f[6].Split(','); if (gs.Length != NQCE3_Genome.GeneCount) throw new NQCRUN_Exception("RUN: genoma corrupto (linea " + I(i + 1) + ")");
				c.Genome = new NQCE3_Genome(); for (int g = 0; g < gs.Length; g++) c.Genome.G[g] = int.Parse(gs[g], CultureInfo.InvariantCulture);
				c.Genome.Repair(lib);
				if (c.Genome.TrialId(lib) != c.TrialId) throw new NQCRUN_Exception("RUN: el TrialId del databank no coincide con su genoma (biblioteca distinta o archivo alterado): " + c.TrialId);
				l.Add(c);
			}
			return l;
		}
		private static string OutcomesTsv(List<NQCE4_Outcome> outs)
		{
			StringBuilder s = new StringBuilder("trial_id\tstatus\ttrades\tnet_1x\tnet_1_5x\tq_pos\tq_net\tq_trades\tnb_total\tnb_pass\n");
			foreach (NQCE4_Outcome o in outs)
			{
				s.Append(o.TrialId).Append('\t').Append(o.Status).Append('\t').Append(I(o.Trades)).Append('\t').Append(R(o.Net1)).Append('\t').Append(R(o.Net15)).Append('\t').Append(I(o.QPos)).Append('\t');
				for (int q = 0; q < NQCE4_Params.Quarters; q++) { if (q > 0) s.Append(','); s.Append(R(o.QNet[q])); }
				s.Append('\t');
				for (int q = 0; q < NQCE4_Params.Quarters; q++) { if (q > 0) s.Append(','); s.Append(I(o.QTrades[q])); }
				s.Append('\t').Append(I(o.NbTotal)).Append('\t').Append(I(o.NbPass)).Append('\n');
			}
			return s.ToString();
		}
		// A2: filas PASS -> candidatos de ET5 (P&L neto a costo 1,0x por trimestre calendario, trimestre sin operaciones = 0).
		private static List<NQCE5_Candidate> Et5Input(string outcomesTsv)
		{
			List<NQCE5_Candidate> l = new List<NQCE5_Candidate>(); string[] lines = outcomesTsv.Split('\n');
			for (int i = 1; i < lines.Length; i++)
			{
				if (lines[i].Length == 0) continue; string[] f = lines[i].Split('\t'); if (f.Length != 10) throw new NQCRUN_Exception("RUN: resultados de ET4 corruptos (linea " + I(i + 1) + ")");
				if (f[1] != "PASS") continue;
				string[] qn = f[6].Split(','), qt = f[7].Split(','); double[] qv = new double[qn.Length]; int[] qc = new int[qt.Length];
				for (int q = 0; q < qn.Length; q++) qv[q] = PR(qn[q]); for (int q = 0; q < qt.Length; q++) qc[q] = int.Parse(qt[q], CultureInfo.InvariantCulture);
				l.Add(new NQCE5_Candidate(f[0], qv, qc));
			}
			return l;
		}

		// A4: corre un portafolio de miembros (en orden de prioridad) y devuelve resultado + insumos de ET7.
		private NQCE_Result RunPortfolio(NQCE_Bars bars, INQCE3_Library lib, List<NQCRUN_Cand> members)
		{
			NQCE_Portfolio pf = new NQCE_Portfolio(); pf.Name = "ET7";
			foreach (NQCRUN_Cand c in members) pf.Members.Add(c.Genome.ToMember(lib, c.TrialId.Substring(0, 12)));
			return NQCE_Executor.Run(bars, pf, P.Config, new NQCE_NullHooks());
		}
		public static List<NQCE7_TradeRec> TradeRecs(NQCE_Result res, NQCE_Config cfg)
		{
			Dictionary<int, int> idx = new Dictionary<int, int>(); for (int s = 0; s < res.Sessions.Count; s++) idx[res.Sessions[s].DayKey] = s;
			List<NQCE7_TradeRec> l = new List<NQCE7_TradeRec>(); int prev = -1;
			foreach (NQCE_Trade t in res.Trades)
			{
				int s; if (!idx.TryGetValue(t.DayKey, out s)) throw new NQCRUN_Exception("RUN: operacion en una sesion fuera del calendario del ejecutor (DayKey=" + I(t.DayKey) + ")");
				if (s < prev) throw new NQCRUN_Exception("RUN: operaciones no ordenadas por sesion");
				prev = s;
				double minOpen = Math.Min(0.0, -t.MaePts * NQCE_Const.PointValue) - cfg.Cost();   // convencion del DD M2M del ejecutor: el costo se carga mientras la operacion esta abierta
				l.Add(new NQCE7_TradeRec(s, t.Net, Math.Min(minOpen, t.Net)));
			}
			return l;
		}

		// ================================================================== embudo
		public NQCRUN_Result Run(long[] t, double[] o, double[] h, double[] l, double[] c, double[] vol, NQCRUN_Params p)
		{
			P = p; log = p.Log; Stopwatch total = Stopwatch.StartNew();
			string err = p.Validate(); if (err.Length > 0) throw new NQCRUN_Exception("RUN: configuracion invalida: " + err);
			dir = p.OutDir.Length > 0 ? p.OutDir : Path.Combine(Path.GetTempPath(), "NQC_RUNFUNNEL_" + p.Campaign);
			Directory.CreateDirectory(dir);
			// (a) datos: IS estricto, M1 completas, volumen, R2
			NQCRUN_DataReport rep = new NQCRUN_DataReport();
			err = NQCRUN_Data.Check(t, o, h, l, c, vol, p.StrictIsRange, true, rep); if (err.Length > 0) throw new NQCRUN_Exception(err);
			NQCE_Bars rawBars = new NQCE_Bars(t, o, h, l, c);
			string rawSha = NQCRUN_Data.HashData(rawBars, vol);
			// (c) transformador
			NQCRUN_IDatasetTransform tr = p.Transform != null ? p.Transform : FindTransform(p.TransformName, p.TransformArgs);
			NQCE_Bars bars; double[] volume; tr.Apply(rawBars, vol, out bars, out volume);
			// Name/ParamsText se leen DESPUES de Apply (p. ej. N1+INJ conoce a/convergencia solo tras aplicarse); es lo que entra en el hash
			if (tr.Name == null || tr.Name.Length == 0 || tr.Name.IndexOf('\n') >= 0 || tr.ParamsText == null || tr.ParamsText.IndexOf('\n') >= 0) throw new NQCRUN_Exception("RUN: Name/ParamsText del transformador invalidos (vacio o con salto de linea)");
			if (bars == null || volume == null) throw new NQCRUN_Exception("RUN: el transformador devolvio datos nulos");
			NQCRUN_DataReport rep2 = new NQCRUN_DataReport();
			err = NQCRUN_Data.Check(bars.T, bars.O, bars.H, bars.L, bars.C, volume, p.StrictIsRange, false, rep2); if (err.Length > 0) throw new NQCRUN_Exception("RUN: salida del transformador " + tr.Name + " invalida: " + err);
			string runSha = object.ReferenceEquals(bars, rawBars) && object.ReferenceEquals(volume, vol) ? rawSha : NQCRUN_Data.HashData(bars, volume);
			// (b) biblioteca
			INQCE3_Library lib = p.LibraryFactory != null ? p.LibraryFactory() : new NQCE3_Library();
			long oldMemo = NQCE3_Series.MemoBudgetBytes; NQCE3_Series.MemoBudgetBytes = (long)p.MemoBudgetMb * 1024L * 1024L;
			try { return RunStages(p, rep, rawSha, runSha, tr, bars, volume, lib, total); }
			finally { NQCE3_Series.MemoBudgetBytes = oldMemo; }
		}

		private NQCRUN_Result RunStages(NQCRUN_Params p, NQCRUN_DataReport rep, string rawSha, string runSha, NQCRUN_IDatasetTransform tr, NQCE_Bars bars, double[] volume, INQCE3_Library lib, Stopwatch total)
		{
			NQCRUN_Result res = new NQCRUN_Result(); res.OutDir = dir;
			NQCE3_Params gp = new NQCE3_Params(); gp.PopSize = p.Population; gp.Generations = p.Generations; gp.TopK = p.TopK; gp.Seed = p.Seed;
			string paramsText = p.CanonicalText(gp);
			double[] nullBest = p.NullBest;
			string nullSha = "none";
			if (nullBest == null && p.NullBestFile.Length > 0)
			{
				List<double> nb = new List<double>();
				foreach (string line in File.ReadAllText(p.NullBestFile).Split('\n')) { string s = line.Trim(); if (s.Length == 0 || s[0] == '#') continue; nb.Add(double.Parse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture)); }
				nullBest = nb.ToArray();
			}
			if (nullBest != null) { StringBuilder nsb = new StringBuilder(); foreach (double d in nullBest) nsb.Append(R(d)).Append('\n'); nullSha = NQCE_Const.Sha256Text(nsb.ToString()); }
			StringBuilder ri = new StringBuilder();
			ri.Append("runner=").Append(Version).Append("|contract=").Append(NQCE_Const.ContractVersion).Append("|core=").Append(NQCE_Const.CoreVersion).Append("|library=").Append(lib.Version).Append("|et4=").Append(NQCE4_Prune.Version).Append("|et6schema=").Append(NQCE6_Params.SchemaVersion).Append('\n');
			ri.Append("transform=").Append(tr.Name).Append("|transform_params=").Append(tr.ParamsText).Append("|data_raw_sha256=").Append(rawSha).Append("|data_run_sha256=").Append(runSha).Append("|null_best_sha256=").Append(nullSha).Append('\n');
			ri.Append("engine_hash=").Append(p.EngineHash).Append('\n').Append(paramsText);
			string runInput = NQCE_Const.Sha256Text(ri.ToString()); res.RunInputSha = runInput;
			Log("RUN input_sha256=" + runInput.Substring(0, 16) + " transform=" + tr.Name + " barras=" + I(bars.Count) + " sesiones=" + I(rep.Sessions));
			NQCE_Config cfg = p.Config;
			NQCE7_Config cfg7; { string e7; cfg7 = NQCE7_Config.Parse(p.EffectiveEt7Text(), out e7); }
			Stopwatch sw = new Stopwatch();

			// ============================== ET3: campana evolutiva
			string in3 = NQCE_Const.Sha256Text(runInput + "|ET3");
			NQCRUN_Ckpt c3 = TryResume("ET3", in3);
			if (c3 == null)
			{
				Invalidate("ET3"); Invalidate("ET4"); Invalidate("ET5"); Invalidate("ET6"); Invalidate("ET7");
				sw.Reset(); sw.Start(); c3 = new NQCRUN_Ckpt("ET3", in3);
				NQCE3_Fitness fit = new NQCE3_Fitness(bars, volume, cfg, lib);
				NQCE3_Reservoir rsv = p.NeffSample > 0 ? new NQCE3_Reservoir(p.NeffSample, p.Seed) : null; fit.Reservoir = rsv;   // D-038: muestra uniforme de TODOS los ensayos evaluados (no solo el top-K)
				NQCE3_Campaign camp = new NQCE3_Campaign(); camp.Name = p.Campaign; camp.MaxEvals = p.MaxEvals;
				Stopwatch gs = Stopwatch.StartNew();
				camp.OnGeneration = delegate(int g, NQCE3_Campaign cm) { Log("ET3 gen " + I(g + 1) + "/" + I(gp.Generations) + " evaluaciones=" + I(fit.FreshEvaluations) + " t=" + (gs.ElapsedMilliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "s mem=" + I(MemMb()) + "MB"); };
				camp.Run(fit, lib, gp, null);
				List<NQCE3_Eval> top = camp.Bank.Top();
				Put(c3, "ET3/ET3_LEDGER.tsv", camp.Ledger.Text()); Put(c3, "ET3/ET3_CURVE.tsv", camp.Curve.ToString()); Put(c3, "ET3/ET3_DATABANK.tsv", DatabankTsv(top));
				if (rsv != null)
				{
					rsv.Save(Path.Combine(dir, "ET3", "ET3_NEFF_SAMPLE.bin")); Reg(c3, "ET3/ET3_NEFF_SAMPLE.bin");
					c3.Meta["neff_sample_k"] = I(p.NeffSample); c3.Meta["neff_sample_n"] = I(rsv.Count); c3.Meta["neff_sample_seen"] = I(rsv.Seen); c3.Meta["neff_sample_flat"] = I(rsv.ConstantSeries());
					c3.Meta["neff_population"] = I(fit.FreshEvaluations); c3.Meta["neff_sample_seed"] = I(p.Seed ^ NQCE3_Reservoir.SeedMix);
				}
				double best = double.NaN; foreach (NQCE3_Eval e in top) if (double.IsNaN(best) || e.Fitness > best) best = e.Fitness;
				c3.Meta["fresh_evals"] = I(fit.FreshEvaluations); c3.Meta["ledger_rows"] = I(camp.Ledger.Rows); c3.Meta["ledger_sha256"] = camp.Ledger.Sha256();
				c3.Meta["zero_trade_evals"] = I(fit.ZeroTradeEvals); c3.Meta["zero_trade_with_reject"] = I(fit.ZeroTradeWithRejectEvals); c3.Meta["databank_valid"] = I(top.Count);
				c3.Meta["best_fitness_t"] = R(best); c3.Meta["warmup_coverage"] = R(fit.WarmupCoverage); c3.Meta["seed"] = I(p.Seed); c3.Meta["campaign"] = p.Campaign;
				sw.Stop(); c3.Perf["seconds"] = (sw.ElapsedMilliseconds / 1000.0).ToString("0.00", CultureInfo.InvariantCulture); c3.Perf["mem_mb"] = I(MemMb());
				Commit(c3);
			}
			res.Stages.Add(c3); Log("ET3 " + (c3.Resumed ? "REANUDADA" : "calculada") + " evaluaciones=" + c3.M("fresh_evals") + " databank=" + c3.M("databank_valid"));
			string out3 = c3.OutputHash();
			if (p.StopAfter == "ET3") return Finish(res, rep, rawSha, runSha, tr, lib, paramsText, total, "DETENIDO_TRAS_ET3", cfg7);
			int et3Fresh = int.Parse(c3.M("fresh_evals"), CultureInfo.InvariantCulture);
			List<NQCRUN_Cand> cands = ParseDatabank(Read("ET3/ET3_DATABANK.tsv"), lib);
			Dictionary<string, NQCRUN_Cand> byId = new Dictionary<string, NQCRUN_Cand>(StringComparer.Ordinal); foreach (NQCRUN_Cand k in cands) byId[k.TrialId] = k;

			// ============================== ET4: poda
			string in4 = NQCE_Const.Sha256Text(runInput + "|ET4|" + out3);
			NQCRUN_Ckpt c4 = TryResume("ET4", in4);
			if (c4 == null)
			{
				Invalidate("ET4"); Invalidate("ET5"); Invalidate("ET6"); Invalidate("ET7");
				sw.Reset(); sw.Start(); c4 = new NQCRUN_Ckpt("ET4", in4);
				List<NQCE4_Candidate> c4in = new List<NQCE4_Candidate>(); foreach (NQCRUN_Cand k in cands) c4in.Add(new NQCE4_Candidate(k.TrialId, k.Genome.Clone(), k.Fingerprint));
				NQCE4_Prune pr = new NQCE4_Prune(bars, volume, cfg, lib, NQCE4_Prune.LoadLedgerIds(Read("ET3/ET3_LEDGER.tsv")));
				pr.Evaluate(c4in);
				pr.Save(Path.Combine(dir, "ET4"), c4in, et3Fresh);
				Reg(c4, "ET4/ET4_POOL.tsv"); Reg(c4, "ET4/ET4_REJECTS.tsv"); Reg(c4, "ET4/ET4_LEDGER.tsv"); Reg(c4, "ET4/SUMMARY.txt");
				Put(c4, "ET4/ET4_OUTCOMES.tsv", OutcomesTsv(pr.Outcomes));
				c4.Meta["candidates"] = I(pr.Outcomes.Count); c4.Meta["f1_rejected"] = I(pr.Count("F1")); c4.Meta["f2_rejected"] = I(pr.Count("F2")); c4.Meta["f3_rejected"] = I(pr.Count("F3")); c4.Meta["pool"] = I(pr.Count("PASS"));
				c4.Meta["fresh_trials"] = I(pr.FreshTrials); c4.Meta["neighbor_evals"] = I(pr.NeighborEvaluations); c4.Meta["executor_runs"] = I(pr.ExecutorRuns); c4.Meta["cumulative_trials"] = I(et3Fresh + pr.FreshTrials);
				c4.Meta["pool_sha256"] = pr.PoolSha256(c4in); c4.Meta["rejects_sha256"] = pr.RejectsSha256(); c4.Meta["ledger_sha256"] = pr.LedgerSha256();
				sw.Stop(); c4.Perf["seconds"] = (sw.ElapsedMilliseconds / 1000.0).ToString("0.00", CultureInfo.InvariantCulture); c4.Perf["mem_mb"] = I(MemMb());
				Commit(c4);
			}
			res.Stages.Add(c4); Log("ET4 " + (c4.Resumed ? "REANUDADA" : "calculada") + " pool=" + c4.M("pool") + " ensayos_nuevos=" + c4.M("fresh_trials"));
			string out4 = c4.OutputHash();
			if (p.StopAfter == "ET4") return Finish(res, rep, rawSha, runSha, tr, lib, paramsText, total, "DETENIDO_TRAS_ET4", cfg7);
			int et4Fresh = int.Parse(c4.M("fresh_trials"), CultureInfo.InvariantCulture);

			// ============================== ET5: robustez
			string in5 = NQCE_Const.Sha256Text(runInput + "|ET5|" + out4);
			NQCRUN_Ckpt c5 = TryResume("ET5", in5);
			if (c5 == null)
			{
				Invalidate("ET5"); Invalidate("ET6"); Invalidate("ET7");
				sw.Reset(); sw.Start(); c5 = new NQCRUN_Ckpt("ET5", in5);
				List<NQCE5_Candidate> c5in = Et5Input(Read("ET4/ET4_OUTCOMES.tsv"));
				NQCE5_Result r5 = NQCE5_Robust.Run(c5in, new NQCE5_Params());
				Put(c5, "ET5/ET5_REPORT.txt", r5.Report);
				StringBuilder rb = new StringBuilder("order\ttrial_id\n"); int ro = 1; foreach (NQCE5_Candidate k in r5.Robust) rb.Append(I(ro++)).Append('\t').Append(k.Id).Append('\n');
				Put(c5, "ET5/ET5_ROBUST.tsv", rb.ToString());
				c5.Meta["input"] = I(r5.InputCount); c5.Meta["robust"] = I(r5.Robust.Count); c5.Meta["rejected"] = I(r5.Rejected.Count); c5.Meta["pbo"] = R(r5.Pbo); c5.Meta["pbo_full_pool"] = R(r5.PboFullPool);
				c5.Meta["pbo_pass"] = r5.PboPass ? "SI" : "NO"; c5.Meta["campaign_closed"] = r5.CampaignClosed ? "SI" : "NO"; c5.Meta["close_reason"] = r5.CloseReason; c5.Meta["pool_hash"] = r5.PoolHash; c5.Meta["report_hash"] = r5.ReportHash;
				sw.Stop(); c5.Perf["seconds"] = (sw.ElapsedMilliseconds / 1000.0).ToString("0.00", CultureInfo.InvariantCulture); c5.Perf["mem_mb"] = I(MemMb());
				Commit(c5);
			}
			res.Stages.Add(c5); Log("ET5 " + (c5.Resumed ? "REANUDADA" : "calculada") + " robustos=" + c5.M("robust") + " PBO=" + c5.M("pbo") + " cerrada=" + c5.M("campaign_closed"));
			string out5 = c5.OutputHash();
			if (c5.M("campaign_closed") == "SI") return Finish(res, rep, rawSha, runSha, tr, lib, paramsText, total, "CAMPANIA_CERRADA_EN_ET5: " + c5.M("close_reason"), cfg7);
			if (p.StopAfter == "ET5") return Finish(res, rep, rawSha, runSha, tr, lib, paramsText, total, "DETENIDO_TRAS_ET5", cfg7);

			// ============================== ET6: portafolios
			string in6 = NQCE_Const.Sha256Text(runInput + "|ET6|" + out5);
			NQCRUN_Ckpt c6 = TryResume("ET6", in6);
			if (c6 == null)
			{
				Invalidate("ET6"); Invalidate("ET7");
				sw.Reset(); sw.Start(); c6 = new NQCRUN_Ckpt("ET6", in6);
				List<NQCE6_PoolEntry> pool = new List<NQCE6_PoolEntry>(); string[] rl = Read("ET5/ET5_ROBUST.tsv").Split('\n');
				for (int i = 1; i < rl.Length; i++) { if (rl[i].Length == 0) continue; string id = rl[i].Split('\t')[1]; NQCRUN_Cand k; if (!byId.TryGetValue(id, out k)) throw new NQCRUN_Exception("RUN: candidato robusto ausente del databank: " + id); pool.Add(new NQCE6_PoolEntry(id, k.Genome.Clone(), k.Fitness)); }
				NQCE6_Params p6 = new NQCE6_Params(); p6.MaxPool = p.Et6MaxPool; p6.BeamWidth = p.Et6BeamWidth; p6.Seed = p.Seed; p6.PpassMin = p.Et6PpassMin; p6.Campaign = p.Campaign;
				p6.EngineHash = p.EngineHash; p6.LibraryHash = lib.Version; p6.PriorTrials = et3Fresh + et4Fresh;
				NQCE6_Builder b6 = new NQCE6_Builder(bars, volume, cfg, lib, pool, p6);
				NQCE6_Result r6 = b6.Search();
				NQCE6_Builder.Save(Path.Combine(dir, "ET6"), r6);
				Reg(c6, "ET6/ET6_REJECTIONS.tsv"); Reg(c6, "ET6/ET6_TRIALS.tsv"); Reg(c6, "ET6/SUMMARY.txt");
				StringBuilder fb = new StringBuilder("rank\tportfolio_id\tspec_hash\tt_stat\tppass\tmembers\n");
				foreach (NQCE6_Finalist f in r6.Finalists)
				{
					Reg(c6, "ET6/PORTFOLIO_SPEC_F" + I(f.Rank) + ".json");
					StringBuilder mem = new StringBuilder(); for (int i = 0; i < f.Eval.Ranks.Length; i++) { if (i > 0) mem.Append(','); mem.Append(b6.Entry(f.Eval.Ranks[i]).TrialId); }
					fb.Append(I(f.Rank)).Append('\t').Append(f.Eval.TrialId).Append('\t').Append(f.SpecHash).Append('\t').Append(R(f.Eval.TStat)).Append('\t').Append(R(f.Eval.Ppass)).Append('\t').Append(mem.ToString()).Append('\n');
				}
				Put(c6, "ET6/ET6_FINALISTS.tsv", fb.ToString());
				c6.Meta["trials"] = I(r6.Trials); c6.Meta["executor_runs"] = I(r6.ExecutorRuns); c6.Meta["accepted"] = I(r6.Accepted); c6.Meta["hard_discarded"] = I(r6.HardDiscarded); c6.Meta["pool_used"] = I(r6.PoolUsed);
				c6.Meta["finalists"] = I(r6.Finalists.Count); c6.Meta["cumulative_trials"] = I(r6.CumulativeTrials); c6.Meta["summary_hash"] = r6.SummaryHash;
				c6.Meta["trials_by_size"] = I(r6.TrialsBySize[1]) + "/" + I(r6.TrialsBySize[2]) + "/" + I(r6.TrialsBySize[3]) + "/" + I(r6.TrialsBySize[4]) + "/" + I(r6.TrialsBySize[5]);
				sw.Stop(); c6.Perf["seconds"] = (sw.ElapsedMilliseconds / 1000.0).ToString("0.00", CultureInfo.InvariantCulture); c6.Perf["mem_mb"] = I(MemMb());
				Commit(c6);
			}
			res.Stages.Add(c6); Log("ET6 " + (c6.Resumed ? "REANUDADA" : "calculada") + " ensayos=" + c6.M("trials") + " finalistas=" + c6.M("finalists"));
			string out6 = c6.OutputHash();
			if (p.StopAfter == "ET6") return Finish(res, rep, rawSha, runSha, tr, lib, paramsText, total, "DETENIDO_TRAS_ET6", cfg7);
			if (c6.M("finalists") == "0") return Finish(res, rep, rawSha, runSha, tr, lib, paramsText, total, "SIN_FINALISTAS_EN_ET6", cfg7);
			int nGross = int.Parse(c6.M("cumulative_trials"), CultureInfo.InvariantCulture);

			// ============================== ET7: evidencia (sin congelado salvo freeze=1 con et2_approved=1)
			string in7 = NQCE_Const.Sha256Text(runInput + "|ET7|" + out6 + "|neff_adapter=" + NQCRUN_NeffAdapter.Id);
			NQCRUN_Ckpt c7 = TryResume("ET7", in7);
			if (c7 == null)
			{
				Invalidate("ET7");
				sw.Reset(); sw.Start(); c7 = new NQCRUN_Ckpt("ET7", in7);
				// finalistas (A4)
				List<string[]> fin = new List<string[]>(); foreach (string line in Read("ET6/ET6_FINALISTS.tsv").Split('\n')) { if (line.Length == 0 || line.StartsWith("rank\t", StringComparison.Ordinal)) continue; fin.Add(line.Split('\t')); }
				List<NQCE7_CandidateInput> inputs = new List<NQCE7_CandidateInput>(); List<double[]> series = new List<double[]>();
				bool sampleMode = cfg7.NEffMethod == "SAMPLE_CHAO1";
				double[][] sample = null; long sSeen = 0, sSeed = 0; string samplePath = Path.Combine(dir, "ET3", "ET3_NEFF_SAMPLE.bin");
				if (File.Exists(samplePath)) { string[] sid; sample = NQCE3_Reservoir.Load(samplePath, out sid, out sSeen, out sSeed); }
				// A5: COVERED (D-036) = serie diaria por entrada del databank (tope neff_max_series menos los finalistas) + finalistas. SAMPLE_CHAO1 (D-038) = solo la muestra uniforme del reservorio de ET3 (no se recorre el databank)
				if (!sampleMode)
				{
					int cap = Math.Max(0, cfg7.NEffMaxSeries - fin.Count); int nb = Math.Min(cands.Count, cap);
					for (int i = 0; i < nb; i++) { List<NQCRUN_Cand> one = new List<NQCRUN_Cand>(); one.Add(cands[i]); series.Add(RunPortfolio(bars, lib, one).DailyNet); }
				}
				List<string> finIds = new List<string>(); StringBuilder specs = new StringBuilder();
				foreach (string[] f in fin)
				{
					List<NQCRUN_Cand> ms = new List<NQCRUN_Cand>(); foreach (string id in f[5].Split(',')) ms.Add(byId[id]);
					NQCE_Result pr7 = RunPortfolio(bars, lib, ms);
					NQCE7_CandidateInput ci = new NQCE7_CandidateInput(); ci.Id = f[1].Substring(0, 12); ci.DailyPnl = pr7.DailyNet; ci.Trades = TradeRecs(pr7, cfg); ci.Sessions = pr7.Sessions.Count; ci.Fitness = PR(f[3]);
					inputs.Add(ci); if (!sampleMode) series.Add(pr7.DailyNet); finIds.Add(f[0]);
					if (specs.Length > 0) specs.Append('\n'); specs.Append("spec_F").Append(f[0]).Append("_sha256=").Append(f[2]);
				}
				// N_poblacion (decision del runner, D-038/D-036): N bruto del ledger acumulado = ET3 + vecinos nuevos de ET4 + combinaciones de ET6 (el N que penaliza el DSR). La muestra solo contiene ensayos de ET3 (aproximacion declarada)
				NQCE7_NEffResult ne = NQCE7_Evidence.ComputeNEff(cfg7, sampleMode ? sample : series.ToArray(), nGross);
				List<NQCE7_Verdict> vs = new List<NQCE7_Verdict>();
				foreach (NQCE7_CandidateInput ci in inputs) vs.Add(NQCE7_Evidence.Evaluate(ci, ne, nullBest, cfg7, cfg.Profile));
				string neffStatus = "SIN_MUESTRA"; int flatN = 0;
				if (sample != null)
				{
					foreach (double[] sr in sample) { double m = 0; foreach (double x in sr) m += x; m /= Math.Max(1, sr.Length); double q = 0; foreach (double x in sr) q += (x - m) * (x - m); if (q <= 1e-18) flatN++; }
					string neffText = NQCRUN_NeffAdapter.Estimate(sample, nGross, cfg7.NEffCut, out neffStatus);
					Put(c7, "ET7/ET7_NEFF_SAMPLE_ESTIMATE.txt", "N_poblacion_usada=" + I(nGross) + " (N bruto ET3+ET4+ET6) N_ET3=" + I(et3Fresh) + " N_muestra=" + I(sample.Length) + " vistos_por_el_reservorio=" + I(sSeen) + " semilla_reservorio=" + I(sSeed) + " series_planas=" + I(flatN) + "\n" + neffText);
				}
				c7.Meta["neff_sample_status"] = neffStatus; c7.Meta["neff_adapter"] = NQCRUN_NeffAdapter.Id;
				if (sampleMode)
				{
					NQCE7_NeffResult sm = ne.Sample; string NA = "NA";
					c7.Meta["neff_method"] = "SAMPLE_CHAO1"; c7.Meta["neff_bound"] = cfg7.NEffBound; c7.Meta["neff_n_gross"] = I(nGross); c7.Meta["neff_n_population_et3"] = I(et3Fresh); c7.Meta["neff_n_population_used"] = I(nGross);
					c7.Meta["neff_n_sample"] = I(sample == null ? 0 : sample.Length); c7.Meta["neff_observed"] = sm == null ? NA : I(sm.GruposObservados); c7.Meta["neff_f1"] = sm == null ? NA : I(sm.F1); c7.Meta["neff_f2"] = sm == null ? NA : I(sm.F2);
					c7.Meta["neff_estimated"] = sm == null ? NA : R(sm.NEffEstimado); c7.Meta["neff_ci_lo"] = sm == null ? NA : R(sm.Inferior); c7.Meta["neff_ci_hi"] = sm == null ? NA : R(sm.Superior);
					c7.Meta["neff_good_turing"] = sm == null ? NA : R(sm.CoberturaMuestra); c7.Meta["neff_flat_n"] = I(flatN); c7.Meta["neff_flat_frac"] = R(sample == null || sample.Length == 0 ? double.NaN : (double)flatN / sample.Length);
				}
				Put(c7, "ET7/ET7_CONFIG.txt", cfg7.Text);
				string report = NQCE7_Evidence.Report(cfg7, ne, nullBest, vs, p.Commit.Length > 0 ? p.Commit : "NA", specs.ToString());
				Put(c7, "ET7/ET7_EVIDENCE_REPORT.txt", report);
				int npass = 0; for (int i = 0; i < vs.Count; i++) if (vs[i].Pass) npass++;
				if (p.Freeze)
				{
					string[] eng = Directory.GetFiles(p.EngineDir, "NQC_*.cs"); Array.Sort(eng, StringComparer.Ordinal);
					string utc = p.FrozenUtc.Length > 0 ? p.FrozenUtc : DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
					for (int i = 0; i < vs.Count; i++)
						if (vs[i].Pass)
						{
							string spec = Path.Combine(dir, "ET6", "PORTFOLIO_SPEC_F" + finIds[i] + ".json");
							Put(c7, "ET7/PORTFOLIO_FROZEN_F" + finIds[i] + ".txt", NQCE7_Freeze.Build(spec, eng, p.Commit, utc));
						}
				}
				c7.Meta["portfolios"] = I(vs.Count); c7.Meta["pass"] = I(npass); c7.Meta["n_gross"] = I(nGross); c7.Meta["n_series"] = I(ne.NSeries); c7.Meta["clusters"] = I(ne.Clusters); c7.Meta["coverage"] = R(ne.Coverage);
				c7.Meta["n_eff"] = R(ne.NEff); c7.Meta["neff_fail"] = ne.Fail; c7.Meta["null_n"] = I(nullBest == null ? 0 : nullBest.Length); c7.Meta["report_sha256"] = NQCE_Const.Sha256Text(report); c7.Meta["config_sha256"] = cfg7.Sha;
				c7.Meta["freeze"] = p.Freeze ? "1" : "0";
				StringBuilder s7 = new StringBuilder("NQC ET7 (runner) | " + Version + " | SOLO IS | sin congelado salvo freeze=1\n");
				s7.Append("portafolios=").Append(I(vs.Count)).Append(" pasan=").Append(I(npass)).Append(" N_bruto=").Append(I(nGross)).Append(" series=").Append(I(ne.NSeries)).Append(" N_eff=").Append(R(ne.NEff)).Append(" falla_neff=").Append(ne.Fail).Append('\n');
				if (sampleMode)
					s7.Append("neff_method=SAMPLE_CHAO1 limite=").Append(cfg7.NEffBound).Append(" N_bruto=").Append(I(nGross)).Append(" N_poblacion_usada=").Append(I(nGross)).Append(" N_ET3=").Append(I(et3Fresh)).Append(" (la muestra solo contiene ensayos de ET3: aproximacion)")
					 .Append(" observados=").Append(c7.M("neff_observed")).Append(" f1=").Append(c7.M("neff_f1")).Append(" f2=").Append(c7.M("neff_f2")).Append(" N_eff_estimado=").Append(c7.M("neff_estimated")).Append(" IC95=[").Append(c7.M("neff_ci_lo")).Append(',').Append(c7.M("neff_ci_hi"))
					 .Append("] cobertura_GoodTuring=").Append(c7.M("neff_good_turing")).Append(" series_planas=").Append(I(flatN)).Append(" (").Append(c7.M("neff_flat_frac")).Append(") N_eff_usado=").Append(R(ne.NEff)).Append('\n');
				s7.Append("report_sha256=").Append(c7.M("report_sha256")).Append(" config_sha256=").Append(cfg7.Sha).Append('\n');
				Put(c7, "ET7/SUMMARY.txt", s7.ToString());
				sw.Stop(); c7.Perf["seconds"] = (sw.ElapsedMilliseconds / 1000.0).ToString("0.00", CultureInfo.InvariantCulture); c7.Perf["mem_mb"] = I(MemMb());
				Commit(c7);
			}
			res.Stages.Add(c7); Log("ET7 " + (c7.Resumed ? "REANUDADA" : "calculada") + " pasan=" + c7.M("pass") + "/" + c7.M("portfolios"));
			return Finish(res, rep, rawSha, runSha, tr, lib, paramsText, total, "COMPLETO", cfg7);
		}

		// ------------------------------------------------------------------ SUMMARY.txt global
		private NQCRUN_Result Finish(NQCRUN_Result res, NQCRUN_DataReport rep, string rawSha, string runSha, NQCRUN_IDatasetTransform tr, INQCE3_Library lib, string paramsText, Stopwatch total, string status, NQCE7_Config cfg7)
		{
			res.Status = status;
			StringBuilder d = new StringBuilder();   // parte determinista (sin tiempos, sin memoria, sin rutas)
			d.Append("NQC RUN FUNNEL SUMMARY | SOLO IS 2020-01-01..2022-12-31 | el OOS no se toca | sin congelado salvo freeze=1 | SOLO DESARROLLO (no es evidencia hasta correr en NT8 con datos reales y ET2 aprobada)\n");
			d.Append("status=").Append(status).Append('\n');
			d.Append("runner_version=").Append(Version).Append(" contract=").Append(NQCE_Const.ContractVersion).Append(" core=").Append(NQCE_Const.CoreVersion).Append(" executor_spec=").Append(NQCE_Const.SpecVersion).Append('\n');
			d.Append("library=").Append(lib.Version).Append(" et4=").Append(NQCE4_Prune.Version).Append(" et6_schema=").Append(NQCE6_Params.SchemaVersion).Append(" engine_hash=").Append(P.EngineHash.Length == 0 ? "NA" : P.EngineHash).Append('\n');
			d.Append("campaign=").Append(P.Campaign).Append(" seed=").Append(I(P.Seed)).Append(" population=").Append(I(P.Population)).Append(" generations=").Append(I(P.Generations)).Append(" max_evals=").Append(I(P.MaxEvals)).Append(" top_k=").Append(I(P.TopK))
			 .Append(" et6_max_pool=").Append(I(P.Et6MaxPool)).Append(" et6_beam=").Append(I(P.Et6BeamWidth)).Append(" et6_ppass_min=").Append(R(P.Et6PpassMin)).Append(" mc_paths=").Append(I(cfg7.Paths)).Append(" mc_mode=").Append(cfg7.McMode).Append('\n');
			d.Append("transform=").Append(tr.Name).Append(" transform_params=").Append(tr.ParamsText).Append('\n');
			d.Append("data: bars=").Append(I(rep.Bars)).Append(" sessions=").Append(I(rep.Sessions)).Append(" first_day=").Append(I(rep.FirstDay)).Append(" last_day=").Append(I(rep.LastDay)).Append(" warmup_coverage=").Append(R(rep.WarmupCoverage))
			 .Append(" r2_zone=").Append(I(rep.ZoneHits)).Append('/').Append(I(rep.ZoneSessions)).Append(" (").Append(R(rep.ZoneFrac)).Append("%) strict_is_range=").Append(P.StrictIsRange ? "1" : "0").Append('\n');
			d.Append("data_raw_sha256=").Append(rawSha).Append(" data_run_sha256=").Append(runSha).Append('\n');
			d.Append("params_sha256=").Append(NQCE_Const.Sha256Text(paramsText)).Append(" run_input_sha256=").Append(res.RunInputSha).Append('\n');
			NQCRUN_Ckpt s3 = res.Stage("ET3"), s4 = res.Stage("ET4"), s5 = res.Stage("ET5"), s6 = res.Stage("ET6"), s7 = res.Stage("ET7");
			int tri3 = s3 == null ? 0 : int.Parse(s3.M("fresh_evals"), CultureInfo.InvariantCulture), tri4 = s4 == null ? 0 : int.Parse(s4.M("fresh_trials"), CultureInfo.InvariantCulture), tri6 = s6 == null ? 0 : int.Parse(s6.M("trials"), CultureInfo.InvariantCulture);
			d.Append("ensayos: ET3=").Append(I(tri3)).Append(" ET4_vecinos_nuevos=").Append(I(tri4)).Append(" ET5=0 ET6_combinaciones=").Append(I(tri6)).Append(" ET7=0 acumulado=").Append(I(tri3 + tri4 + tri6)).Append('\n');
			foreach (NQCRUN_Ckpt c in res.Stages)
			{
				d.Append('[').Append(c.Stage).Append("] input_hash=").Append(c.InputHash).Append(" output_hash=").Append(c.OutputHash()).Append('\n');
				foreach (KeyValuePair<string, string> kv in c.Meta) d.Append("  ").Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
				foreach (KeyValuePair<string, string> kv in c.Files) if (kv.Key.IndexOf("PORTFOLIO_FROZEN", StringComparison.Ordinal) < 0) d.Append("  sha256 ").Append(kv.Key).Append(' ').Append(kv.Value).Append('\n');
			}
			res.DeterministicSha = NQCE_Const.Sha256Text(d.ToString());
			StringBuilder s = new StringBuilder(d.ToString());
			s.Append("deterministic_sha256=").Append(res.DeterministicSha).Append('\n');
			s.Append("--- no determinista (tiempos, memoria y reanudacion; no entra en deterministic_sha256) ---\n");
			total.Stop(); res.TotalSeconds = total.ElapsedMilliseconds / 1000.0; res.PeakMemMb = PeakMb();
			foreach (NQCRUN_Ckpt c in res.Stages) s.Append(c.Stage).Append(": seconds=").Append(c.P("seconds")).Append(" mem_managed_mb=").Append(c.P("mem_mb")).Append(" reanudada=").Append(c.Resumed ? "SI" : "NO").Append('\n');
			s.Append("total_seconds=").Append(res.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture)).Append(" mem_managed_mb=").Append(I(MemMb())).Append(" peak_working_set_mb=").Append(I(res.PeakMemMb)).Append(" memo_budget_mb=").Append(I(P.MemoBudgetMb)).Append('\n');
			if (s7 != null) foreach (KeyValuePair<string, string> kv in s7.Files) if (kv.Key.IndexOf("PORTFOLIO_FROZEN", StringComparison.Ordinal) >= 0) s.Append("frozen ").Append(kv.Key).Append(' ').Append(kv.Value).Append('\n');
			res.Summary = s.ToString();
			File.WriteAllText(Path.Combine(dir, "SUMMARY.txt"), res.Summary, new UTF8Encoding(false));
			Log("RUN " + status + " deterministic_sha256=" + res.DeterministicSha.Substring(0, 16) + " total=" + res.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s");
			return res;
		}
	}

	// ======================================================================== estrategia NT8 (sin ordenes)
	public class NQC_RUN_Funnel : Strategy
	{
		private List<long> tT = new List<long>(); private List<double> tO = new List<double>(), tH = new List<double>(), tL = new List<double>(), tC = new List<double>(), tV = new List<double>();
		private bool done, outsideIs, dataLoaded, gotBars; private int outsideDay; private string instName = "", fatal = "";

		private static string ResultsDir() { string d = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results"); Directory.CreateDirectory(d); return d; }

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Runner del embudo ET3-ET7 (sin ordenes). Configuracion en NQC_Results\\RUN_FUNNEL_CONFIG.txt. SOLO IS 2020-2022.";
				Name = "NQC_RUN_Funnel";
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
				// NT8 instancia la estrategia sin cargar datos (enumeracion de propiedades, etc.) y la lleva a Terminated: sin DataLoaded y barras recibidas NO se hace nada (ni error ni archivos)
				if (!ShouldRun(dataLoaded, gotBars)) { if (dataLoaded) Print("NQC_RUN_INFO la estrategia cargo datos pero no recibio barras en esta ejecucion; no se ejecuta nada"); return; }
				try { RunAll(); }
				catch (Exception ex)
				{
					Print("NQC_RUN_ERROR " + ex.Message);
					try { File.WriteAllText(Path.Combine(ResultsDir(), "RUN_ERROR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt"), ex.ToString()); } catch { }
				}
			}
		}

		// Defecto de la corrida FULLPACK run1: RunAll solo si la estrategia llego a State.DataLoaded Y recibio barras en ESTA ejecucion.
		public static bool ShouldRun(bool dataLoaded, bool gotBars) { return dataLoaded && gotBars; }

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
			string cfgPath = Path.Combine(ResultsDir(), "RUN_FUNNEL_CONFIG.txt"); string text = File.Exists(cfgPath) ? File.ReadAllText(cfgPath) : "";
			string err; NQCRUN_Params p = NQCRUN_Params.Parse(text, out err); if (p == null) throw new NQCRUN_Exception("RUN: configuracion ilegible: " + err);
			if (p.OutDir.Length == 0) p.OutDir = Path.Combine(ResultsDir(), "RUNFUNNEL_" + p.Campaign);
			if (p.EngineHash.Length == 0) p.EngineHash = NQCRUN_Hashes.EngineHash(Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "bin", "Custom", "Strategies"));
			Directory.CreateDirectory(p.OutDir);
			string logPath = Path.Combine(p.OutDir, "RUN_LOG.txt");
			p.Log = delegate(string s) { Print("NQC_RUN " + s); try { File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + s + "\n"); } catch { } };
			File.WriteAllText(Path.Combine(p.OutDir, "RUN_CONFIG_USED.txt"), text, new UTF8Encoding(false));
			NQCRUN_Result r = new NQCRUN_Funnel().Run(tT.ToArray(), tO.ToArray(), tH.ToArray(), tL.ToArray(), tC.ToArray(), tV.ToArray(), p);
			Print("NQC_RUN_END status=" + r.Status + " deterministic_sha256=" + r.DeterministicSha.Substring(0, 12) + " dir=" + r.OutDir);
		}
	}
}
