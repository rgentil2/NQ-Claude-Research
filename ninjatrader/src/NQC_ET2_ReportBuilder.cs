#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
#endregion

//==============================================================================
// NQC_ET2_ReportBuilder - Generador PURO del informe de calibracion de ET2 (ET2_CALIBRATION_REPORT.txt). Contrato NQC 2.0.0. SOLO DESARROLLO.
// No depende de NinjaTrader: lee de DISCO la carpeta de un lote de NQC_RUN_Batch (RUNBATCH_<batch>\), con el formato REAL que escriben NQC_RUN_Batch y NQC_RUN_Funnel
// (SUMMARY.txt del lote y de cada campana, ET3_DATABANK.tsv, ET4_REJECTS.tsv, ET6_REJECTIONS.tsv, ET6_FINALISTS.tsv, ET7_EVIDENCE_REPORT.txt, ET7_NEFF_SAMPLE_ESTIMATE.txt)
// y devuelve el texto del informe + los hashes. NO decide nada por el usuario: el veredicto es mecanico (docs/stages/ET2.md §4 y §10).
// Requiere NQC_ET2_Calibration.cs (NQCET2_Stats: Clopper-Pearson, cota unilateral). Sin dependencias de datos de mercado: solo lee los archivos del lote.
//
// Convenciones de este informe (declaradas tambien en su cabecera):
//  - FP = campanas NULAS (N1, N2) con >= 1 portafolio que pasa TODAS las puertas de ET7 y P_pass; potencia = campanas INYECTADAS con un portafolio que lo pasa todo
//    y contiene la regla inyectada o una EQUIVALENTE (D-037). La campana IDENTITY (real) y las campanas con ERROR/incompletas NO entran en las tasas (se listan).
//  - Fase A corre ET6 con ppass_min=0 y SIN vector nulo (null_best_file): la puerta del percentil de ET7 falla cerrada en el runner (nulo_ET2_n=0). El informe la
//    RECALCULA con el vector «mejor t por campana nula» (D-036), dejando la campana propia fuera (leave-one-out; con 20 nulos quedan 19: se relaja n>=20 a n>=19
//    SOLO en este recalculo y se declara). Si el SUMMARY de ET7 ya trae nulo_ET2_n >= 20 se usa el veredicto registrado.
//  - Equivalencia (potencia): necesita las operaciones (dia, direccion) de los miembros y del par inyectado, que el runner NO exporta (ver ET6_MEMBER_TRADES.tsv e
//    INJ_PAIR_TRADES.tsv abajo). Sin ellos la potencia se da como COTA SUPERIOR (cualquier portafolio aprobado) y la equivalencia como NO EVALUABLE.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public sealed class NQCET2_ReportParams
	{
		public double MaxFp = 0.05, MinPower = 0.70, Alpha = 0.05;        // gate de ET2 y nivel de los intervalos
		public double PpassFloor = 0.50, PpassStep = 0.05;                // piso del umbral de P_pass (D-037) y rejilla del barrido
		public double NullPctMin = 95.0;                                  // percentil nulo minimo (D-036), para el recalculo
		public int NullMinN = 20;                                         // D-036: n >= 20 para el percentil registrado por el runner
		public int NullMinNOffline = 19;                                  // recalculo leave-one-out con 20 nulos
		public int ExpectedNull = 20, ExpectedInj = 10;                   // campanas esperadas (D-037); solo para avisar si faltan
		public double DsrMin = 0.95;                                      // umbral vigente de DSR (para la rejilla)
		public string Label = "", Commit = "NA", GeneratedUtc = "NA", Phase = "FASE_A_REDUCED";
		public bool VerifyHashes = true;                                  // contrasta los sha256 de los SUMMARY contra los archivos leidos
	}

	public sealed class NQCET2_ReportResult
	{
		public bool Ok; public string Error = ""; public string Text = ""; public string ReportSha = "", InputsSha = "";
		public List<string[]> Inputs = new List<string[]>();             // {ruta relativa, bytes, sha256}
		public List<string> Warnings = new List<string>();
		// valores numericos (para pruebas y para el orquestador)
		public int NNull, KFp, NN1, KFpN1, NN2, KFpN2, NInj, KPowerUpper, KPowerEquiv = -1, NEquivEvaluable;
		public double Threshold = double.NaN, FpAtThreshold = double.NaN, PowerUpperAtThreshold = double.NaN, PowerEquivAtThreshold = double.NaN;
		public int KFpAtThreshold, KPowerUpperAtThreshold, KPowerEquivAtThreshold = -1;
		public string Verdict = "", FpStatus = "", PowerStatus = "";
		public int Excluded; public bool PctNotEvaluable;
		// D-040: potencia por nivel de edge (target_net). MinEdge = menor nivel con potencia puntual >= MinPower (NaN si ninguno).
		public List<NQCET2_LevelResult> Levels = new List<NQCET2_LevelResult>(); public double MinEdge = double.NaN, MinEdgeDemostrado = double.NaN;
	}

	public sealed class NQCET2_LevelResult
	{
		public bool CeilingFromLot; public double Level; public int N, K, KUpper, NOk; public double Power, PowerLow, TPairMedian = double.NaN, CeilingMedian = double.NaN; public string Status = "";
	}

	internal sealed class RPort
	{
		public string Id12 = "", Id = "", Status = ""; public bool Pass;
		public double Dsr = double.NaN, Sr = double.NaN, Sr0 = double.NaN, Skew = double.NaN, Kurt = double.NaN, Pct = double.NaN, DdP95 = double.NaN, PpassBoot = double.NaN; public int T;
		public List<string> Causes = new List<string>();
		public double Tstat = double.NaN, Ppass = double.NaN; public string[] Members = new string[0]; public int Rank;
		// derivados
		public bool DsrOk, McOk, PbootOk, PctOk, OtherOk, Equiv; public double PctUsed = double.NaN; public bool PctRecomputed;
		public bool HasCause(string g) { foreach (string c in Causes) if (c.StartsWith(g + ":", StringComparison.Ordinal) || c.StartsWith(g + " ", StringComparison.Ordinal) || c == g) return true; return false; }
	}

	internal sealed class RCamp
	{
		public string Id = "", Dir = "", State = "", TableTransform = "", Kind = "", RunStatus = "", Problem = "";
		public bool HasSummary, Complete; public string ClosedAt = "", ClosedWhy = "";
		public Dictionary<string, string> Head = new Dictionary<string, string>(StringComparer.Ordinal);
		public Dictionary<string, Dictionary<string, string>> Meta = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
		public Dictionary<string, string> Trials = new Dictionary<string, string>(StringComparer.Ordinal);
		public Dictionary<string, string> InjParams = new Dictionary<string, string>(StringComparer.Ordinal);
		public double Seconds = double.NaN, PeakMb = double.NaN; public string Note = "", Digest = "";
		public List<double> DatabankT = new List<double>();
		public List<string[]> Et4Rejects = new List<string[]>();            // {status, causa normalizada}
		public List<string[]> Et6Rej = new List<string[]>();                // {primera_puerta, size}
		public List<RPort> Ports = new List<RPort>(); public int NullN = -1; public string Et7Fail = "", PpassBootMinText = "";
		public double NEffUsed = double.NaN, NEffEst = double.NaN, NEffLo = double.NaN, NEffHi = double.NaN, GoodTuring = double.NaN, SrVar = double.NaN;
		public int NEffObs = -1, NEffF1 = -1, NEffF2 = -1, NSample = -1, FlatN = -1; public double FlatFrac = double.NaN; public string EstimateState = "";
		public double BestT = double.NaN; public string BestTCase = "";
		public Dictionary<string, HashSet<string>> MemberTrades = null; public HashSet<string> PairTrades = null;
		public double PpassMinRun = double.NaN; public string Et6Gates = null;
		// resultados por campana
		public double VAll = -1, VUpper = -1, VEquiv = -1;                  // mejor P_pass entre portafolios que pasan las demas puertas (-1 = ninguno)
		public bool EquivEvaluable;
		public int G(string st, string key, int def) { Dictionary<string, string> m; string v; if (Meta.TryGetValue(st, out m) && m.TryGetValue(key, out v)) { double d; if (NQCET2_ReportBuilder.TryD(v, out d)) return (int)d; } return def; }
		public double GD(string st, string key) { Dictionary<string, string> m; string v; double d; if (Meta.TryGetValue(st, out m) && m.TryGetValue(key, out v) && NQCET2_ReportBuilder.TryD(v, out d)) return d; return double.NaN; }
		public string GS(string st, string key) { Dictionary<string, string> m; string v; if (Meta.TryGetValue(st, out m) && m.TryGetValue(key, out v)) return v; return ""; }
		public bool IsNull { get { return Kind == "N1" || Kind == "N2"; } }
		public bool IsInj { get { return Kind == "INJ"; } }
		public bool Valid { get { return State == "OK" && HasSummary && Problem.Length == 0; } }
	}

	public static class NQCET2_ReportBuilder
	{
		public const string Version = "ET2REPORT-1.1.0";
		private static readonly CultureInfo IC = CultureInfo.InvariantCulture;

		// ------------------------------------------------------------------ utilidades
		public static bool TryD(string s, out double d)
		{
			d = double.NaN; if (s == null) return false; s = s.Trim(); if (s.Length == 0 || s == "NA" || s == "NaN" || s == "n/a") return false;
			if (s == "INF" || s == "Infinity" || s == "+Infinity") { d = double.PositiveInfinity; return true; }
			if (s == "-INF" || s == "-Infinity") { d = double.NegativeInfinity; return true; }
			return double.TryParse(s.Replace(',', '.'), NumberStyles.Float, IC, out d);
		}
		private static double D(string s) { double d; return TryD(s, out d) ? d : double.NaN; }
		private static string I(long v) { return v.ToString(IC); }
		private static string F(double v, string f) { return double.IsNaN(v) ? "NaN" : (double.IsInfinity(v) ? (v > 0 ? "inf" : "-inf") : v.ToString(f, IC)); }
		public static string Sha256Bytes(byte[] b) { using (SHA256 h = SHA256.Create()) { byte[] x = h.ComputeHash(b); StringBuilder sb = new StringBuilder(); foreach (byte y in x) sb.Append(y.ToString("x2", IC)); return sb.ToString(); } }
		public static string Sha256Text(string t) { return Sha256Bytes(new UTF8Encoding(false).GetBytes(t)); }
		private static string[] Lines(string t) { string[] a = t.Split('\n'); for (int i = 0; i < a.Length; i++) a[i] = a[i].TrimEnd('\r'); return a; }
		private static Dictionary<string, string> Tokens(string line)
		{
			Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (string tok in line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)) { int e = tok.IndexOf('='); if (e > 0 && !d.ContainsKey(tok.Substring(0, e))) d[tok.Substring(0, e)] = tok.Substring(e + 1); }
			return d;
		}
		private static string Tk(Dictionary<string, string> d, string k) { string v; return d != null && d.TryGetValue(k, out v) ? v : ""; }
		private static int Col(string[] h, string name) { for (int i = 0; i < h.Length; i++) if (h[i] == name) return i; return -1; }
		private static List<string[]> Tsv(string text, out string[] header)
		{
			header = new string[0]; List<string[]> rows = new List<string[]>(); bool first = true;
			foreach (string l in Lines(text)) { if (l.Length == 0 || l[0] == '#') continue; string[] c = l.Split('\t'); if (first) { header = c; first = false; } else rows.Add(c); }
			return rows;
		}
		private static string Cell(string[] r, int i) { return i >= 0 && i < r.Length ? r[i] : ""; }

		// ------------------------------------------------------------------ lector con registro de hashes
		private sealed class Reader
		{
			public string Root; public SortedDictionary<string, string[]> Files = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
			public Reader(string root) { Root = root; }
			public string Read(string rel)
			{
				string full = Path.Combine(Root, rel.Replace('/', Path.DirectorySeparatorChar)); if (!File.Exists(full)) return null;
				byte[] b; try { b = File.ReadAllBytes(full); } catch (Exception) { return null; }
				Files[rel] = new string[] { rel, I(b.Length), Sha256Bytes(b) };
				return new UTF8Encoding(false).GetString(b);
			}
		}

		// ------------------------------------------------------------------ distribucion normal (propia: este archivo no depende de NQC_ET7_*)
		private static double NormCdf(double x)
		{
			// Abramowitz-Stegun 7.1.26 mejorado via erfc de alta precision (Cody simplificado por fraccion continua para colas) -> aqui: aproximacion de West (1e-15)
			double ax = Math.Abs(x), r;
			if (ax > 37) r = 0; else
			{
				double e = Math.Exp(-ax * ax / 2.0);
				if (ax < 7.07106781186547) { double b = 3.52624965998911E-02 * ax + 0.700383064443688; b = b * ax + 6.37396220353165; b = b * ax + 33.912866078383; b = b * ax + 112.079291497871; b = b * ax + 221.213596169931; b = b * ax + 220.206867912376; double c = 8.83883476483184E-02 * ax + 1.75566716318264; c = c * ax + 16.064177579207; c = c * ax + 86.7807322029461; c = c * ax + 296.564248779674; c = c * ax + 637.333633378831; c = c * ax + 793.826512519948; c = c * ax + 440.413735824752; r = e * b / c; }
				else { double b = ax + 0.65; b = ax + 4 / b; b = ax + 3 / b; b = ax + 2 / b; b = ax + 1 / b; r = e / b / 2.506628274631; }
			}
			return x > 0 ? 1.0 - r : r;
		}
		private static double NormInv(double p)
		{
			if (p <= 0) return double.NegativeInfinity; if (p >= 1) return double.PositiveInfinity;
			double lo = -40, hi = 40; for (int it = 0; it < 200; it++) { double m = (lo + hi) / 2; if (NormCdf(m) < p) lo = m; else hi = m; } return (lo + hi) / 2;
		}
		private static double ExpMaxK(double nEff) { const double g = 0.5772156649015329; return (1.0 - g) * NormInv(1.0 - 1.0 / nEff) + g * NormInv(1.0 - 1.0 / (nEff * Math.E)); }

		// ------------------------------------------------------------------ estadistica descriptiva
		private static string Dist(List<double> v)
		{
			List<double> s = new List<double>(); foreach (double x in v) if (!double.IsNaN(x) && !double.IsInfinity(x)) s.Add(x); s.Sort();
			if (s.Count == 0) return "n=0";
			return "n=" + I(s.Count) + " mediana=" + F(Qn(s, 0.5), "0.###") + " P5=" + F(Qn(s, 0.05), "0.###") + " P95=" + F(Qn(s, 0.95), "0.###") + " min=" + F(s[0], "0.###") + " max=" + F(s[s.Count - 1], "0.###");
		}
		private static double Qn(List<double> s, double q) { if (s.Count == 1) return s[0]; double pos = q * (s.Count - 1); int lo = (int)Math.Floor(pos); int hi = Math.Min(s.Count - 1, lo + 1); return s[lo] + (pos - lo) * (s[hi] - s[lo]); }
		private static double MedianOf(List<double> v) { List<double> s = new List<double>(); foreach (double x in v) if (!double.IsNaN(x) && !double.IsInfinity(x)) s.Add(x); if (s.Count == 0) return double.NaN; s.Sort(); return Qn(s, 0.5); }
		private static string Prop(int k, int n, NQCET2_ReportParams p)
		{
			if (n <= 0) return "n=0 (sin datos)";
			double lo, hi; NQCET2_Stats.ClopperPearson(k, n, p.Alpha, out lo, out hi); double up = NQCET2_Stats.UpperOneSided(k, n, p.Alpha); double lw = LowerOneSided(k, n, p.Alpha);
			return I(k) + "/" + I(n) + " (" + F(100.0 * k / n, "0.0") + "%) CP95=[" + F(100 * lo, "0.0") + "%," + F(100 * hi, "0.0") + "%] cota_sup_unilateral95=" + F(100 * up, "0.0") + "% cota_inf_unilateral95=" + F(100 * lw, "0.0") + "%";
		}
		// cota inferior unilateral (1-alpha): menor p con P(X >= x) >= alpha  <=> 1 - Cdf(x-1) = alpha
		private static double LowerOneSided(int x, int n, double alpha)
		{
			if (x <= 0) return 0; double a = 0, b = 1; for (int it = 0; it < 200; it++) { double m = (a + b) / 2; if (1 - NQCET2_Stats.BinCdf(x - 1, n, m) < alpha) a = m; else b = m; } return (a + b) / 2;
		}

		// ------------------------------------------------------------------ analisis de archivos
		private static string KindOf(string tr)
		{
			string t = (tr ?? "").Trim().ToUpperInvariant().Replace("+", "");
			if (t == "N1") return "N1"; if (t == "N2") return "N2"; if (t == "N1INJ") return "INJ"; if (t == "IDENTITY") return "IDENTITY"; return "OTRO";
		}
		private static readonly Regex NumRe = new Regex(@"-?\d+(?:[.,]\d+)?", RegexOptions.Compiled);
		private static string NormCause(string c) { return NumRe.Replace(c ?? "", "#"); }

		private static void ParseSummary(string text, RCamp c)
		{
			string[] L = Lines(text); Dictionary<string, string> cur = null; bool any = false;
			Regex stage = new Regex(@"^\[(ET\d)\]\s+input_hash=(\S+)\s+output_hash=(\S+)", RegexOptions.Compiled);
			Regex secs = new Regex(@"^(ET\d):\s+seconds=([\d.]+)", RegexOptions.Compiled);
			foreach (string line in L)
			{
				Match m = stage.Match(line);
				if (m.Success) { cur = new Dictionary<string, string>(StringComparer.Ordinal); c.Meta[m.Groups[1].Value] = cur; cur["input_hash"] = m.Groups[2].Value; cur["output_hash"] = m.Groups[3].Value; any = true; continue; }
				if (line.StartsWith("  ", StringComparison.Ordinal) && cur != null)
				{
					string t = line.Trim();
					if (t.StartsWith("sha256 ", StringComparison.Ordinal)) { string[] pp = t.Split(' '); if (pp.Length >= 3) c.Trials["sha:" + pp[1]] = pp[2]; }
					else { int e = t.IndexOf('='); if (e > 0) cur[t.Substring(0, e)] = t.Substring(e + 1); }
					continue;
				}
				cur = null;
				if (line.StartsWith("status=", StringComparison.Ordinal)) { c.RunStatus = line.Substring(7); any = true; }
				else if (line.StartsWith("campaign=", StringComparison.Ordinal)) { foreach (KeyValuePair<string, string> kv in Tokens(line)) c.Head["campaign." + kv.Key] = kv.Value; }
				else if (line.StartsWith("runner_version=", StringComparison.Ordinal) || line.StartsWith("library=", StringComparison.Ordinal)) { foreach (KeyValuePair<string, string> kv in Tokens(line)) c.Head[kv.Key] = kv.Value; }
				else if (line.StartsWith("transform=", StringComparison.Ordinal))
				{
					int sp = line.IndexOf(" transform_params=", StringComparison.Ordinal);
					c.Head["transform"] = sp < 0 ? line.Substring(10).Trim() : line.Substring(10, sp - 10);
					if (sp >= 0) foreach (string kv in line.Substring(sp + 18).Split(';')) { int e = kv.IndexOf('='); if (e > 0 && !c.InjParams.ContainsKey(kv.Substring(0, e))) c.InjParams[kv.Substring(0, e)] = kv.Substring(e + 1); }
				}
				else if (line.StartsWith("data_raw_sha256=", StringComparison.Ordinal) || line.StartsWith("params_sha256=", StringComparison.Ordinal) || line.StartsWith("data:", StringComparison.Ordinal)) { foreach (KeyValuePair<string, string> kv in Tokens(line)) c.Head[kv.Key] = kv.Value; }
				else if (line.StartsWith("ensayos:", StringComparison.Ordinal)) { foreach (KeyValuePair<string, string> kv in Tokens(line.Substring(8))) c.Trials[kv.Key] = kv.Value; }
				else if (line.StartsWith("deterministic_sha256=", StringComparison.Ordinal)) c.Head["deterministic_sha256"] = line.Substring(21).Trim();
				else if (line.StartsWith("total_seconds=", StringComparison.Ordinal)) { foreach (KeyValuePair<string, string> kv in Tokens(line)) c.Head["run." + kv.Key] = kv.Value; }
				else { Match s2 = secs.Match(line); if (s2.Success) c.Head["seconds." + s2.Groups[1].Value] = s2.Groups[2].Value; }
			}
			c.HasSummary = any && c.RunStatus.Length > 0;
			if (c.HasSummary)
			{
				c.Complete = c.RunStatus.StartsWith("COMPLETO", StringComparison.Ordinal);
				Match cm = Regex.Match(c.RunStatus, @"CAMPANIA_CERRADA_EN_(ET\d)(?::\s*(.*))?");
				if (cm.Success) { c.ClosedAt = cm.Groups[1].Value; c.ClosedWhy = cm.Groups[2].Value.Trim(); }
				Match sf = Regex.Match(c.RunStatus, @"^SIN_FINALISTAS_EN_(ET\d)");
				if (sf.Success) { c.ClosedAt = sf.Groups[1].Value; c.ClosedWhy = "sin finalistas"; }
				if (!c.Complete && !cm.Success && !sf.Success) c.Problem = "estado del embudo ilegible: " + c.RunStatus;
				string tr = Tk(c.Head, "transform"); if (tr.Length > 0) { string k = KindOf(tr); if (c.Kind.Length == 0 || c.Kind == "OTRO") c.Kind = k; }
				c.PpassMinRun = D(Tk(c.Head, "campaign.et6_ppass_min"));
			}
		}

		private static void ParseEt7(string text, RCamp c)
		{
			string[] L = Lines(text); RPort cur = null;
			Regex hd = new Regex(@"^--- portafolio (\S+) : (PASA A ET8|RECHAZADO)", RegexOptions.Compiled);
			foreach (string line in L)
			{
				Match m = hd.Match(line);
				if (m.Success) { cur = new RPort(); cur.Id12 = m.Groups[1].Value; cur.Pass = m.Groups[2].Value.StartsWith("PASA", StringComparison.Ordinal); c.Ports.Add(cur); continue; }
				if (cur == null)
				{
					if (line.StartsWith("umbrales:", StringComparison.Ordinal)) { Match pb = Regex.Match(line, @"ppass_boot_min=(\S+)"); if (pb.Success) c.PpassBootMinText = pb.Groups[1].Value; }
					else if (line.StartsWith("N_bruto=", StringComparison.Ordinal))
					{
						Match a = Regex.Match(line, @"N_eff=(\S+)"); if (a.Success) c.NEffUsed = D(a.Groups[1].Value);
						Match b = Regex.Match(line, @"var_SR_diario=(\S+)"); if (b.Success) c.SrVar = D(b.Groups[1].Value);
						int f = line.IndexOf("FALLA_CERRADA=", StringComparison.Ordinal); if (f >= 0) c.Et7Fail = line.Substring(f + 14).Trim();
					}
					else if (line.StartsWith("nulo_ET2_n=", StringComparison.Ordinal)) c.NullN = (int)D(line.Substring(11));
					else if (line.StartsWith("N_eff_muestra:", StringComparison.Ordinal))
					{
						Match a = Regex.Match(line, @"n_muestra=(\d+)"); if (a.Success) c.NSample = int.Parse(a.Groups[1].Value, IC);
						a = Regex.Match(line, @"observados=(\d+) f1=(\d+) f2=(\d+)"); if (a.Success) { c.NEffObs = int.Parse(a.Groups[1].Value, IC); c.NEffF1 = int.Parse(a.Groups[2].Value, IC); c.NEffF2 = int.Parse(a.Groups[3].Value, IC); }
						a = Regex.Match(line, @"N_eff_estimado\(Chao1\)=(\S+) IC95=\[(\S+),(\S+)\] cobertura_GoodTuring=(\S+)"); if (a.Success) { c.NEffEst = D(a.Groups[1].Value); c.NEffLo = D(a.Groups[2].Value); c.NEffHi = D(a.Groups[3].Value); c.GoodTuring = D(a.Groups[4].Value); }
					}
					else if (line.StartsWith("N_eff_planas:", StringComparison.Ordinal)) { Match a = Regex.Match(line, @"series_planas=(\d+) fraccion_planas=(\S+)"); if (a.Success) { c.FlatN = int.Parse(a.Groups[1].Value, IC); c.FlatFrac = D(a.Groups[2].Value); } }
					continue;
				}
				if (line.StartsWith("RESUMEN:", StringComparison.Ordinal)) { cur = null; continue; }
				if (line.StartsWith("  DSR=", StringComparison.Ordinal))
				{
					Dictionary<string, string> t = Tokens(line.Trim()); cur.Dsr = D(Tk(t, "DSR")); cur.Sr = D(Tk(t, "SR_diario")); cur.Sr0 = D(Tk(t, "SR0")); cur.Skew = D(Tk(t, "asimetria")); cur.Kurt = D(Tk(t, "curtosis")); double tt = D(Tk(t, "T")); cur.T = double.IsNaN(tt) ? 0 : (int)tt;
				}
				else if (line.StartsWith("  percentil_nulo=", StringComparison.Ordinal)) cur.Pct = D(line.Substring(17));
				else if (line.StartsWith("  MC:", StringComparison.Ordinal)) { Dictionary<string, string> t = Tokens(line.Trim()); cur.DdP95 = D(Tk(t, "DD_P95")); cur.PpassBoot = D(Tk(t, "P_pass_bootstrap")); }
				else if (line.StartsWith("  causa_rechazo ", StringComparison.Ordinal)) cur.Causes.Add(line.Substring(16).Trim());
			}
		}

		private static void ParseEstimate(string text, RCamp c)
		{
			foreach (string line in Lines(text))
			{
				if (line.StartsWith("N_poblacion_usada=", StringComparison.Ordinal)) { Match a = Regex.Match(line, @"series_planas=(\d+)"); if (a.Success && c.FlatN < 0) c.FlatN = int.Parse(a.Groups[1].Value, IC); a = Regex.Match(line, @"N_muestra=(\d+)"); if (a.Success && c.NSample < 0) c.NSample = int.Parse(a.Groups[1].Value, IC); }
				else if (line.StartsWith("estimador=", StringComparison.Ordinal)) { Match a = Regex.Match(line, @"estado=(\S+)"); if (a.Success) c.EstimateState = a.Groups[1].Value; }
				else if (line.StartsWith("grupos_observados=", StringComparison.Ordinal)) { Dictionary<string, string> t = Tokens(line); if (c.NEffObs < 0) { double x = D(Tk(t, "grupos_observados")); if (!double.IsNaN(x)) c.NEffObs = (int)x; } if (c.NEffF1 < 0) { double x = D(Tk(t, "f1")); if (!double.IsNaN(x)) c.NEffF1 = (int)x; } if (c.NEffF2 < 0) { double x = D(Tk(t, "f2")); if (!double.IsNaN(x)) c.NEffF2 = (int)x; } }
				else if (line.StartsWith("n_eff_estimado=", StringComparison.Ordinal)) { Dictionary<string, string> t = Tokens(line); if (double.IsNaN(c.NEffEst)) c.NEffEst = D(Tk(t, "n_eff_estimado")); if (double.IsNaN(c.NEffLo)) c.NEffLo = D(Tk(t, "inferior")); if (double.IsNaN(c.NEffHi)) c.NEffHi = D(Tk(t, "superior")); if (double.IsNaN(c.GoodTuring)) c.GoodTuring = D(Tk(t, "cobertura_muestra")); }
			}
		}

		private static void ParseCampaign(Reader rd, RCamp c, NQCET2_ReportParams p, List<string> warn)
		{
			string pre = c.Id + "/";
			string sum = rd.Read(pre + "SUMMARY.txt");
			if (sum == null) { c.Problem = "falta SUMMARY.txt de la campana"; return; }
			try { ParseSummary(sum, c); } catch (Exception ex) { c.Problem = "SUMMARY ilegible: " + ex.Message; return; }
			if (!c.HasSummary) { if (c.Problem.Length == 0) c.Problem = "SUMMARY.txt sin estado del embudo (incompleto o corrupto)"; return; }
			if (c.Problem.Length > 0) return;
			if (c.Kind.Length == 0 || c.Kind == "OTRO") { c.Kind = KindOf(Tk(c.Head, "transform")); }
			string txt;
			// ET3 databank (fitness t)
			txt = rd.Read(pre + "ET3/ET3_DATABANK.tsv");
			if (txt != null) { string[] h; List<string[]> rows = Tsv(txt, out h); int ci = Col(h, "fitness_t"); foreach (string[] r in rows) { double v = D(Cell(r, ci)); if (!double.IsNaN(v)) c.DatabankT.Add(v); } }
			// ET4 rechazos
			txt = rd.Read(pre + "ET4/ET4_REJECTS.tsv");
			if (txt != null) { string[] h; List<string[]> rows = Tsv(txt, out h); int cs = Col(h, "status"), cc = Col(h, "cause"); foreach (string[] r in rows) if (Cell(r, cs) != "PASS") c.Et4Rejects.Add(new string[] { Cell(r, cs), NormCause(Cell(r, cc)) }); }
			// ET6 rechazos y finalistas
			txt = rd.Read(pre + "ET6/ET6_REJECTIONS.tsv");
			if (txt != null) { string[] h; List<string[]> rows = Tsv(txt, out h); int cg = Col(h, "primera_puerta"), cz = Col(h, "size"); foreach (string[] r in rows) c.Et6Rej.Add(new string[] { Cell(r, cg), Cell(r, cz) }); }
			Dictionary<string, RPort> fin = new Dictionary<string, RPort>(StringComparer.Ordinal);
			txt = rd.Read(pre + "ET6/ET6_FINALISTS.tsv");
			if (txt != null)
			{
				string[] h; List<string[]> rows = Tsv(txt, out h); int ck = Col(h, "rank"), cp = Col(h, "portfolio_id"), ct = Col(h, "t_stat"), cq = Col(h, "ppass"), cm = Col(h, "members");
				foreach (string[] r in rows)
				{
					RPort q = new RPort(); q.Id = Cell(r, cp); q.Id12 = q.Id.Length >= 12 ? q.Id.Substring(0, 12) : q.Id; q.Tstat = D(Cell(r, ct)); q.Ppass = D(Cell(r, cq)); q.Members = Cell(r, cm).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries); double rk = D(Cell(r, ck)); q.Rank = double.IsNaN(rk) ? 0 : (int)rk;
					fin[q.Id12] = q;
				}
			}
			// ET7
			txt = rd.Read(pre + "ET7/ET7_EVIDENCE_REPORT.txt");
			if (txt != null)
			{
				ParseEt7(txt, c);
				foreach (RPort q in c.Ports) { RPort f; if (fin.TryGetValue(q.Id12, out f)) { q.Id = f.Id; q.Tstat = f.Tstat; q.Ppass = f.Ppass; q.Members = f.Members; q.Rank = f.Rank; } else warn.Add(c.Id + ": portafolio " + q.Id12 + " del informe de ET7 sin fila en ET6_FINALISTS.tsv"); }
				if (c.Ports.Count == 0 && fin.Count > 0) { foreach (RPort f in fin.Values) c.Ports.Add(f); c.Problem = ""; warn.Add(c.Id + ": ET6_FINALISTS.tsv tiene finalistas pero el informe de ET7 no lista portafolios"); }
			}
			else if (fin.Count > 0) { foreach (RPort f in fin.Values) c.Ports.Add(f); if (c.Complete) warn.Add(c.Id + ": falta ET7_EVIDENCE_REPORT.txt en una campana COMPLETO"); }
			txt = rd.Read(pre + "ET7/ET7_NEFF_SAMPLE_ESTIMATE.txt"); if (txt != null) ParseEstimate(txt, c);
			// meta de N_eff del SUMMARY (respaldo)
			if (c.FlatN < 0) { double x = c.GD("ET7", "neff_flat_n"); if (!double.IsNaN(x)) c.FlatN = (int)x; }
			if (double.IsNaN(c.FlatFrac)) c.FlatFrac = c.GD("ET7", "neff_flat_frac");
			if (c.NSample < 0) { double x = c.GD("ET3", "neff_sample_n"); if (!double.IsNaN(x)) c.NSample = (int)x; }
			// operaciones de los miembros y del par inyectado (export que hoy NO existe en el runner)
			c.Et6Gates = rd.Read(pre + "ET6/ET6_GATES.txt");      // diagnostico D-039 de ET6 (puede faltar)
			string mt = rd.Read(pre + "ET6/ET6_MEMBER_TRADES.tsv"), pt = rd.Read(pre + "INJ_PAIR_TRADES.tsv");
			if (mt != null && pt != null)
			{
				c.MemberTrades = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal); c.PairTrades = new HashSet<string>(StringComparer.Ordinal);
				string[] h; List<string[]> rows = Tsv(mt, out h); int ci = Col(h, "trial_id"), cd = Col(h, "day"), cr = Col(h, "dir");
				foreach (string[] r in rows) { string id = Cell(r, ci); if (id.Length > 12) id = id.Substring(0, 12); HashSet<string> hs; if (!c.MemberTrades.TryGetValue(id, out hs)) { hs = new HashSet<string>(StringComparer.Ordinal); c.MemberTrades[id] = hs; } hs.Add(Cell(r, cd) + "|" + (D(Cell(r, cr)) > 0 ? "L" : "S")); }
				rows = Tsv(pt, out h); cd = Col(h, "day"); cr = Col(h, "dir"); foreach (string[] r in rows) c.PairTrades.Add(Cell(r, cd) + "|" + (D(Cell(r, cr)) > 0 ? "L" : "S"));
			}
			// integridad: los sha256 que el SUMMARY declara para los archivos leidos
			if (p.VerifyHashes)
				foreach (KeyValuePair<string, string> kv in c.Trials)
				{
					if (!kv.Key.StartsWith("sha:", StringComparison.Ordinal)) continue; string rel = pre + kv.Key.Substring(4); string[] got; if (rd.Files.TryGetValue(rel, out got) && got[2] != kv.Value) warn.Add(c.Id + ": sha256 de " + kv.Key.Substring(4) + " NO coincide con el declarado en SUMMARY.txt (archivo alterado)");
				}
		}

		// ------------------------------------------------------------------ construccion del informe
		public static NQCET2_ReportResult Build(string batchDir, NQCET2_ReportParams p)
		{
			NQCET2_ReportResult R = new NQCET2_ReportResult(); if (p == null) p = new NQCET2_ReportParams();
			try { return BuildInner(batchDir, p, R); }
			catch (Exception ex) { R.Ok = false; R.Error = "error interno del generador: " + ex.GetType().Name + ": " + ex.Message; R.Text = ""; return R; }
		}

		private static NQCET2_ReportResult BuildInner(string batchDir, NQCET2_ReportParams p, NQCET2_ReportResult R)
		{
			if (string.IsNullOrEmpty(batchDir) || !Directory.Exists(batchDir)) { R.Error = "la carpeta del lote no existe: " + batchDir; return R; }
			Reader rd = new Reader(batchDir); List<string> warn = R.Warnings;
			// ---- lote
			Dictionary<string, string> bhead = new Dictionary<string, string>(StringComparer.Ordinal); string batchDigest = "", batchTotal = "";
			List<RCamp> camps = new List<RCamp>(); bool batchSummary = false; Dictionary<string, RCamp> byId = new Dictionary<string, RCamp>(StringComparer.Ordinal);
			string bs = rd.Read("SUMMARY.txt");
			if (bs != null)
			{
				int mode = 0; string[] L = Lines(bs);
				foreach (string line in L)
				{
					if (line.StartsWith("batch=", StringComparison.Ordinal) || line.StartsWith("manifest_sha256=", StringComparison.Ordinal)) { foreach (KeyValuePair<string, string> kv in Tokens(line)) bhead[kv.Key] = kv.Value; batchSummary = true; }
					else if (line.StartsWith("data:", StringComparison.Ordinal)) { foreach (KeyValuePair<string, string> kv in Tokens(line)) bhead["data." + kv.Key] = kv.Value; }
					else if (line.StartsWith("id\tstate\ttransform", StringComparison.Ordinal)) mode = 1;
					else if (line.StartsWith("id\tresumed\t", StringComparison.Ordinal)) mode = 2;
					else if (line.StartsWith("batch_digest_sha256=", StringComparison.Ordinal)) { batchDigest = line.Substring(20).Trim(); mode = 0; }
					else if (line.StartsWith("total_seconds=", StringComparison.Ordinal)) { batchTotal = line.Trim(); }
					else if (line.StartsWith("---", StringComparison.Ordinal)) { }
					else if (mode == 1 && line.Length > 0)
					{
						string[] c = line.Split('\t'); if (c.Length < 7) continue; RCamp k = new RCamp(); k.Id = c[0]; k.State = c[1]; k.TableTransform = c[2]; k.Kind = KindOf(c[2]); k.Digest = c.Length > 7 ? c[7] : ""; k.Trials["tabla.size"] = c[3]; k.Trials["tabla.ga_seed"] = c[4]; k.Trials["tabla.trials"] = c[5]; k.Trials["tabla.funnel"] = c[6];
						if (!byId.ContainsKey(k.Id)) { byId[k.Id] = k; camps.Add(k); }
					}
					else if (mode == 2 && line.Length > 0)
					{
						string[] c = line.Split('\t'); RCamp k; if (c.Length >= 4 && byId.TryGetValue(c[0], out k)) { k.Seconds = D(c[2]); k.PeakMb = D(c[3]); k.Note = c.Length > 4 ? c[4] : ""; }
					}
				}
				if (!batchSummary || camps.Count == 0) { bs = null; camps.Clear(); byId.Clear(); }
			}
			if (bs == null)
			{
				warn.Add("LOTE INCOMPLETO o sin SUMMARY.txt del lote legible: las campanas se descubren por sus carpetas; no hay tabla de estado ni digest del lote");
				string[] dirs = Directory.GetDirectories(batchDir); Array.Sort(dirs, StringComparer.Ordinal);
				foreach (string d in dirs)
				{
					string id = Path.GetFileName(d); if (!File.Exists(Path.Combine(d, "SUMMARY.txt")) && !File.Exists(Path.Combine(d, "ERROR.txt"))) continue;
					RCamp k = new RCamp(); k.Id = id; k.State = "?"; byId[id] = k; camps.Add(k);
				}
				if (camps.Count == 0) { R.Error = "la carpeta no es un lote de NQC_RUN_Batch: sin SUMMARY.txt del lote y sin carpetas de campana con SUMMARY.txt/ERROR.txt: " + batchDir; return R; }
			}
			rd.Read("BATCH_LOG.txt");
			foreach (RCamp k in camps)
			{
				k.Dir = k.Id;
				if (File.Exists(Path.Combine(batchDir, k.Id, "ERROR.txt"))) { string et = rd.Read(k.Id + "/ERROR.txt"); k.State = "ERROR"; k.Problem = "campana con ERROR: " + FirstLine(et); continue; }
				if (k.State.StartsWith("ERROR", StringComparison.Ordinal) || (k.State != "OK" && k.State != "?")) { k.Problem = "estado de la campana en el lote: " + k.State; if (!Directory.Exists(Path.Combine(batchDir, k.Id))) continue; }
				ParseCampaign(rd, k, p, warn);
				if (k.State == "?") k.State = k.Problem.Length == 0 ? "OK" : "INCOMPLETA";
				if (k.Kind.Length == 0) k.Kind = "OTRO";
			}
			// ---- clasificacion
			List<RCamp> nulls = new List<RCamp>(), injs = new List<RCamp>(), excluded = new List<RCamp>(), others = new List<RCamp>();
			foreach (RCamp k in camps)
			{
				if (!k.Valid) { excluded.Add(k); continue; }
				if (k.IsNull) nulls.Add(k); else if (k.IsInj) injs.Add(k); else others.Add(k);
			}
			R.Excluded = excluded.Count; R.NNull = nulls.Count; R.NInj = injs.Count;
			foreach (RCamp k in excluded) warn.Add("campana excluida de las tasas: " + k.Id + " (" + k.Problem + ")");
			if (nulls.Count != p.ExpectedNull) warn.Add("campanas nulas validas = " + I(nulls.Count) + " (esperadas " + I(p.ExpectedNull) + ")");
			if (injs.Count != p.ExpectedInj) warn.Add("campanas inyectadas validas = " + I(injs.Count) + " (esperadas " + I(p.ExpectedInj) + ")");

			// ---- mejor portafolio por campana (D-036): t del mejor finalista que llega a ET7; si no hay, t del mejor genoma de ET3
			foreach (RCamp k in camps)
			{
				if (!k.Valid) continue; double best = double.NaN;
				foreach (RPort q in k.Ports) if (!double.IsNaN(q.Tstat) && (double.IsNaN(best) || q.Tstat > best)) best = q.Tstat;
				if (!double.IsNaN(best)) { k.BestT = best; k.BestTCase = "ET7"; }
				else { double b3 = k.GD("ET3", "best_fitness_t"); if (double.IsNaN(b3) && k.DatabankT.Count > 0) { b3 = k.DatabankT[0]; foreach (double v in k.DatabankT) if (v > b3) b3 = v; } if (!double.IsNaN(b3)) { k.BestT = b3; k.BestTCase = "ET3"; } else k.BestTCase = "NINGUNO"; }
			}
			List<RCamp> nullVecCamps = new List<RCamp>(); foreach (RCamp k in nulls) if (!double.IsNaN(k.BestT)) nullVecCamps.Add(k);

			// ---- banderas por portafolio
			foreach (RCamp k in camps)
			{
				if (!k.Valid) continue;
				bool useRecorded = k.NullN >= p.NullMinN;
				foreach (RPort q in k.Ports)
				{
					q.DsrOk = !q.HasCause("DSR") && !double.IsNaN(q.Dsr); q.McOk = !q.HasCause("MC_DD_P95"); q.PbootOk = !q.HasCause("PPASS_BOOT");
					if (useRecorded) { q.PctOk = !q.HasCause("PERCENTIL_NULO"); q.PctUsed = q.Pct; q.PctRecomputed = false; }
					else
					{
						q.PctRecomputed = true; int n = 0, below = 0;
						foreach (RCamp o in nullVecCamps) { if (o == k) continue; n++; if (o.BestT < q.Tstat) below++; }
						if (n < p.NullMinNOffline || double.IsNaN(q.Tstat)) { q.PctUsed = double.NaN; q.PctOk = false; if (n < p.NullMinNOffline) R.PctNotEvaluable = true; }
						else { q.PctUsed = 100.0 * below / n; q.PctOk = q.PctUsed >= p.NullPctMin; }
					}
					q.OtherOk = q.DsrOk && q.PctOk && q.McOk && q.PbootOk;
					q.Equiv = false;
				}
				// equivalencia (D-037)
				k.EquivEvaluable = k.IsInj && (k.Ports.Count == 0 || (k.MemberTrades != null && k.PairTrades != null && k.PairTrades.Count > 0));   // sin portafolios (campana cerrada antes de ET7) no hay nada que evaluar: la regla no esta
				if (k.EquivEvaluable)
					foreach (RPort q in k.Ports)
						foreach (string m in q.Members)
						{
							string id = m.Length > 12 ? m.Substring(0, 12) : m; HashSet<string> mt; if (!k.MemberTrades.TryGetValue(id, out mt) || mt.Count == 0) continue;
							int inter = 0; foreach (string x in mt) if (k.PairTrades.Contains(x)) inter++;
							if ((double)inter / mt.Count >= 0.5 && (double)inter / k.PairTrades.Count >= 0.5) { q.Equiv = true; break; }
						}
				k.VAll = -1; k.VUpper = -1; k.VEquiv = -1;
				foreach (RPort q in k.Ports)
				{
					if (!q.OtherOk || double.IsNaN(q.Ppass)) continue;
					if (q.Ppass > k.VAll) k.VAll = q.Ppass; if (q.Ppass > k.VUpper) k.VUpper = q.Ppass; if (q.Equiv && q.Ppass > k.VEquiv) k.VEquiv = q.Ppass;
				}
			}
			if (R.PctNotEvaluable) warn.Add("PERCENTIL NO EVALUABLE: el vector nulo (leave-one-out) tiene menos de " + I(p.NullMinNOffline) + " valores; ningun portafolio puede pasar la puerta del percentil y el FP medido seria 0 por construccion: el veredicto de FP es INDETERMINADO");
			int equivEval = 0; foreach (RCamp k in injs) if (k.EquivEvaluable) equivEval++;
			R.NEquivEvaluable = equivEval; bool equivOk = injs.Count > 0 && equivEval == injs.Count;
			int nanPpass = 0; foreach (RCamp k in camps) if (k.Valid) foreach (RPort q in k.Ports) if (q.OtherOk && double.IsNaN(q.Ppass)) nanPpass++;
			if (nanPpass > 0) warn.Add(I(nanPpass) + " portafolio(s) que pasan las demas puertas tienen P_pass NaN y no cuentan como aprobados");

			// ---- rejilla de umbral de P_pass
			List<double> grid = new List<double>(); for (double t = 0; t <= 1.0000001; t += p.PpassStep) grid.Add(Math.Round(t, 6));
			Func<List<RCamp>, double, int> cnt = delegate(List<RCamp> set, double tau) { int a = 0; foreach (RCamp k in set) if (k.VAll >= tau && k.VAll >= 0) a++; return a; };
			Func<List<RCamp>, double, int> cntU = cnt;
			Func<List<RCamp>, double, int> cntE = delegate(List<RCamp> set, double tau) { int a = 0; foreach (RCamp k in set) if (k.VEquiv >= tau && k.VEquiv >= 0) a++; return a; };
			double tauRun = 0; { double m = double.NaN; foreach (RCamp k in camps) if (k.Valid && !double.IsNaN(k.PpassMinRun)) { if (double.IsNaN(m) || k.PpassMinRun > m) m = k.PpassMinRun; } if (!double.IsNaN(m)) tauRun = m; }
			List<RCamp> n1 = new List<RCamp>(), n2 = new List<RCamp>(); foreach (RCamp k in nulls) { if (k.Kind == "N1") n1.Add(k); else n2.Add(k); }
			R.NN1 = n1.Count; R.NN2 = n2.Count;
			R.KFp = cnt(nulls, tauRun); R.KFpN1 = cnt(n1, tauRun); R.KFpN2 = cnt(n2, tauRun); R.KPowerUpper = cntU(injs, tauRun); if (equivOk) R.KPowerEquiv = cntE(injs, tauRun);
			double chosen = double.NaN, chosenNoFloor = double.NaN;
			foreach (double t in grid) { if (double.IsNaN(chosenNoFloor) && nulls.Count > 0 && (double)cnt(nulls, t) / nulls.Count <= p.MaxFp) chosenNoFloor = t; if (t + 1e-9 >= p.PpassFloor && double.IsNaN(chosen) && nulls.Count > 0 && (double)cnt(nulls, t) / nulls.Count <= p.MaxFp) chosen = t; }
			R.Threshold = chosen;
			double tauV = double.IsNaN(chosen) ? tauRun : chosen;
			R.KFpAtThreshold = cnt(nulls, tauV); R.KPowerUpperAtThreshold = cntU(injs, tauV); if (equivOk) R.KPowerEquivAtThreshold = cntE(injs, tauV);
			R.FpAtThreshold = nulls.Count > 0 ? (double)R.KFpAtThreshold / nulls.Count : double.NaN; R.PowerUpperAtThreshold = injs.Count > 0 ? (double)R.KPowerUpperAtThreshold / injs.Count : double.NaN; R.PowerEquivAtThreshold = equivOk ? (double)R.KPowerEquivAtThreshold / injs.Count : double.NaN;

			// ---- D-040: potencia por nivel de edge
			SortedDictionary<double, List<RCamp>> byLevel = new SortedDictionary<double, List<RCamp>>();
			foreach (RCamp k in injs) { double lv = D(Tk(k.InjParams, "target_net")); lv = double.IsNaN(lv) ? -1 : Math.Round(lv); List<RCamp> l; if (!byLevel.TryGetValue(lv, out l)) { l = new List<RCamp>(); byLevel[lv] = l; } l.Add(k); }
			bool multiLevel = byLevel.Count > 1;
			foreach (KeyValuePair<double, List<RCamp>> kv in byLevel)
			{
				NQCET2_LevelResult lr = new NQCET2_LevelResult(); lr.Level = kv.Key; lr.N = kv.Value.Count; List<double> tps = new List<double>(), cls = new List<double>();
				foreach (RCamp k in kv.Value)
				{
					if (k.VAll >= tauV && k.VAll >= 0) lr.KUpper++;
					if (equivOk ? (k.VEquiv >= tauV && k.VEquiv >= 0) : (k.VAll >= tauV && k.VAll >= 0)) lr.K++;
					if (Tk(k.InjParams, "ok") == "1") lr.NOk++;
					tps.Add(D(Tk(k.InjParams, "t_pair"))); double ne = !double.IsNaN(k.NEffUsed) ? k.NEffUsed : (!double.IsNaN(k.NEffEst) ? k.NEffEst : double.NaN); cls.Add(!double.IsNaN(ne) && ne > 1 ? Math.Sqrt(2 * Math.Log(ne)) : double.NaN);
				}
				lr.Power = (double)lr.K / lr.N; lr.PowerLow = LowerOneSided(lr.K, lr.N, p.Alpha);
				lr.TPairMedian = MedianOf(tps); lr.CeilingMedian = MedianOf(cls);
				lr.Status = R.PctNotEvaluable ? "NO_EVALUABLE" : (lr.Power + 1e-12 < p.MinPower ? "NO_CUMPLE" : (lr.PowerLow >= p.MinPower ? "CUMPLE_DEMOSTRADO" : "CUMPLE_PUNTUAL_NO_DEMOSTRADO"));
				R.Levels.Add(lr);
				if (double.IsNaN(R.MinEdge) && lr.Level > 0 && lr.Status.StartsWith("CUMPLE", StringComparison.Ordinal)) R.MinEdge = lr.Level;
				if (double.IsNaN(R.MinEdgeDemostrado) && lr.Level > 0 && lr.Status == "CUMPLE_DEMOSTRADO") R.MinEdgeDemostrado = lr.Level;
			}
			{ List<double> allCl = new List<double>(); foreach (RCamp k in camps) { if (!k.Valid) continue; double ne = !double.IsNaN(k.NEffUsed) ? k.NEffUsed : (!double.IsNaN(k.NEffEst) ? k.NEffEst : double.NaN); if (!double.IsNaN(ne) && ne > 1) allCl.Add(Math.Sqrt(2 * Math.Log(ne))); }
			  double gcl = MedianOf(allCl); foreach (NQCET2_LevelResult lr in R.Levels) if (double.IsNaN(lr.CeilingMedian)) { lr.CeilingMedian = gcl; lr.CeilingFromLot = true; } }
			if (multiLevel) foreach (NQCET2_LevelResult lr in R.Levels) if (lr.N != 5) warn.Add("nivel +" + F(lr.Level, "0") + ": " + I(lr.N) + " campanas inyectadas validas (D-040 declara 5 por nivel)");

			// ================================================================== TEXTO
			StringBuilder S = new StringBuilder();
			S.Append("ET2_CALIBRATION_REPORT | generador ").Append(Version).Append(" | contrato NQC 2.0.0 | SOLO DESARROLLO: informe MECANICO calculado de los archivos de un lote de NQC_RUN_Batch; no decide umbrales, no aprueba la etapa y no sustituye la evidencia de NT8 (log + hash)\n");
			S.Append("generado_utc=").Append(p.GeneratedUtc).Append(" fase=").Append(p.Phase).Append(" etiqueta=").Append(p.Label).Append(" commit_protocolo=").Append(p.Commit).Append('\n');
			S.Append("convenciones: FP = campanas NULAS (N1/N2) con >=1 portafolio que pasa TODO (DSR, percentil nulo, MC P95, P_pass[, P_pass bootstrap]); potencia = campanas INYECTADAS con portafolio aprobado que contiene la regla inyectada o una EQUIVALENTE (D-037); IC = Clopper-Pearson exacto bilateral 95% y cotas unilaterales 95%; IDENTITY y campanas con ERROR/incompletas NO entran en las tasas.\n");

			// ---- 1. identificacion
			S.Append("\n== 1. IDENTIFICACION ==\n");
			S.Append("lote=").Append(Tk(bhead, "batch")).Append(" estado_lote=").Append(Tk(bhead, "status")).Append(" campanas_en_lote=").Append(I(camps.Count)).Append(" runner=").Append(Tk(bhead, "runner")).Append(" library=").Append(Tk(bhead, "library")).Append('\n');
			S.Append("manifest_sha256=").Append(Tk(bhead, "manifest_sha256")).Append(" target=").Append(Tk(bhead, "target")).Append(" tol=").Append(Tk(bhead, "tol")).Append(" data_raw_sha256=").Append(Tk(bhead, "data.data_raw_sha256")).Append('\n');
			S.Append("batch_digest_sha256=").Append(batchDigest.Length > 0 ? batchDigest : "NA").Append(' ').Append(batchTotal).Append('\n');
			{ string eh = "", rv = "", lv = ""; foreach (RCamp k in camps) if (k.HasSummary) { if (eh.Length == 0) eh = Tk(k.Head, "engine_hash"); if (rv.Length == 0) rv = Tk(k.Head, "runner_version"); if (lv.Length == 0) lv = Tk(k.Head, "library"); }
			  S.Append("engine_hash=").Append(eh.Length > 0 ? eh : "NA").Append(" runner_version=").Append(rv.Length > 0 ? rv : "NA").Append(" library=").Append(lv.Length > 0 ? lv : "NA").Append(" ET2_ppass_min_de_la_corrida=").Append(F(tauRun, "0.###")).Append('\n'); }
			S.Append("campanas validas: nulas=").Append(I(nulls.Count)).Append(" (N1=").Append(I(n1.Count)).Append(", N2=").Append(I(n2.Count)).Append(") inyectadas=").Append(I(injs.Count)).Append(" otras(IDENTITY/otro, fuera de las tasas)=").Append(I(others.Count)).Append(" excluidas(ERROR/incompletas)=").Append(I(excluded.Count)).Append('\n');
			S.Append("id\ttipo\testado\tga_seed\ttamano\tdata_run_sha256\tfunnel\tinj_ok\tsegundos\n");
			foreach (RCamp k in camps)
				S.Append(k.Id).Append('\t').Append(k.Kind).Append('\t').Append(k.Valid ? "OK" : "EXCLUIDA:" + k.Problem).Append('\t').Append(Tk(k.Trials, "tabla.ga_seed")).Append('\t').Append(Tk(k.Trials, "tabla.size")).Append('\t').Append(Short(Tk(k.Head, "data_run_sha256"))).Append('\t').Append(k.Valid ? FunnelState(k) : "NA").Append('\t').Append(Regex.Match(k.Note ?? "", @"inj_ok=(\d)").Success ? Regex.Match(k.Note, @"inj_ok=(\d)").Groups[1].Value : "NA").Append('\t').Append(F(k.Seconds, "0.0")).Append('\n');

			// ---- 2. aceptacion de inyectados
			S.Append("\n== 2. ACEPTACION DE LOS DATASETS INYECTADOS (D-037: neto medido 20 +- 2 por operacion del par) ==\n");
			if (injs.Count == 0) S.Append("sin campanas inyectadas validas\n");
			else
			{
				S.Append("id\ta\tmeasured_net\tnet_long\tnet_short\ttrades\trate_per_session\tt_pair\tok\n");
				int okc = 0;
				foreach (RCamp k in injs) { string ok = Tk(k.InjParams, "ok"); if (ok == "1") okc++; S.Append(k.Id).Append('\t').Append(Tk(k.InjParams, "a")).Append('\t').Append(Tk(k.InjParams, "measured_net")).Append('\t').Append(Tk(k.InjParams, "net_long")).Append('\t').Append(Tk(k.InjParams, "net_short")).Append('\t').Append(Tk(k.InjParams, "trades")).Append('\t').Append(Tk(k.InjParams, "rate_per_session")).Append('\t').Append(Tk(k.InjParams, "t_pair")).Append('\t').Append(ok).Append('\n'); }
				S.Append("inyectados con ok=1: ").Append(I(okc)).Append('/').Append(I(injs.Count)).Append(okc < injs.Count ? " ADVERTENCIA: hay inyectados que NO cumplen el criterio de aceptacion (no entran con garantia en la potencia)" : "").Append('\n');
				List<double> tp = new List<double>(); foreach (RCamp k in injs) tp.Add(D(Tk(k.InjParams, "t_pair"))); S.Append("t_pair: ").Append(Dist(tp)).Append('\n');
				if (okc < injs.Count) warn.Add("inyectados con ok=0: " + I(injs.Count - okc));
			}

			// ---- 3. FP y potencia
			S.Append("\n== 3. FALSOS POSITIVOS Y POTENCIA ==\n");
			List<RCamp> calib = new List<RCamp>(nulls); calib.AddRange(injs);
			S.Append("umbral de P_pass de la corrida (ET6 ppass_min)=").Append(F(tauRun, "0.###")).Append(". Percentil nulo: ").Append(PctSource(calib)).Append('\n');
			S.Append("FP global (como corrio, tau=").Append(F(tauRun, "0.###")).Append("): ").Append(Prop(R.KFp, nulls.Count, p)).Append('\n');
			S.Append("FP N1: ").Append(Prop(R.KFpN1, n1.Count, p)).Append('\n');
			S.Append("FP N2: ").Append(Prop(R.KFpN2, n2.Count, p)).Append('\n');
			S.Append("campanas nulas con FP: ").Append(ListIds(nulls, tauRun)).Append('\n');
			S.Append("POTENCIA cota superior (cualquier portafolio aprobado en el inyectado; NO verifica que contenga la regla): ").Append(Prop(R.KPowerUpper, injs.Count, p)).Append('\n');
			if (equivOk) S.Append("POTENCIA con regla inyectada o equivalente (D-037): ").Append(Prop(R.KPowerEquiv, injs.Count, p)).Append('\n');
			else
			{
				S.Append("POTENCIA con regla inyectada o equivalente (D-037): NO EVALUABLE con los archivos disponibles (").Append(I(equivEval)).Append('/').Append(I(injs.Count)).Append(" inyectadas evaluables: con operaciones exportadas o sin portafolios que comparar).\n");
				S.Append("  Export que falta en el runner (propuesta exacta): (a) `<id>\\ET6\\ET6_MEMBER_TRADES.tsv` con cabecera `trial_id\\tday\\tdir` = una fila por operacion EJECUTADA de cada miembro de los finalistas en el dataset de la campana (day = DayKey yyyymmdd de la sesion; dir = +1/-1); (b) `<id>\\INJ_PAIR_TRADES.tsv` con cabecera `day\\tdir` = operaciones ejecutadas del par inyectado (la regla de referencia `rules=` de transform_params) sobre ese mismo dataset. Con ambos archivos este generador aplica la definicion de equivalencia (>=50% de sus operaciones en (dia, direccion) del par y cubre >=50% de las del par) sin cambios.\n");
			}
			S.Append("campanas inyectadas con portafolio aprobado: ").Append(ListIdsInj(injs, tauRun)).Append('\n');

			// ---- 3b. potencia por nivel de edge (D-040)
			S.Append("\n== 3b. POTENCIA POR NIVEL DE EDGE (D-040; tau=").Append(F(tauV, "0.00")).Append("; ").Append(equivOk ? "con regla inyectada o equivalente" : "COTA SUPERIOR (equivalencia no evaluable)").Append(") ==\n");
			if (R.Levels.Count == 0) S.Append("sin campanas inyectadas.\n");
			foreach (NQCET2_LevelResult lr in R.Levels)
			{
				S.Append(lr.Level < 0 ? "nivel sin target_net" : "edge +US$" + F(lr.Level, "0") + " netos/operacion").Append(": potencia ").Append(Prop(lr.K, lr.N, p)).Append(" -> ").Append(lr.Status).Append('\n');
				S.Append("   cota superior (cualquier portafolio aprobado): ").Append(I(lr.KUpper)).Append('/').Append(I(lr.N)).Append("; inyectados aceptados (ok=1): ").Append(I(lr.NOk)).Append('/').Append(I(lr.N)).Append("; t_par mediano=").Append(F(lr.TPairMedian, "0.##")).Append(" ; techo de ruido sqrt(2 ln N_eff) mediano=").Append(F(lr.CeilingMedian, "0.##")).Append(lr.CeilingFromLot ? " (N_eff mediano del lote: las inyectadas de este nivel no llegan a ET7)" : "").Append(!double.IsNaN(lr.TPairMedian) && !double.IsNaN(lr.CeilingMedian) && lr.TPairMedian < lr.CeilingMedian ? "  -> t_par POR DEBAJO del techo: indetectable por diseno a este nivel" : "").Append('\n');
			}
			S.Append("edge minimo con potencia puntual >= ").Append(F(100 * p.MinPower, "0")).Append("%: ").Append(double.IsNaN(R.MinEdge) ? "NINGUNO de los niveles medidos" : "+US$" + F(R.MinEdge, "0") + " netos/operacion").Append("; con potencia demostrada (cota inferior unilateral 95% >= ").Append(F(100 * p.MinPower, "0")).Append("%): ").Append(double.IsNaN(R.MinEdgeDemostrado) ? "ninguno" : "+US$" + F(R.MinEdgeDemostrado, "0")).Append(" (el usuario decide cual es el realista; D-020 es ajustable dentro de ET2).\n");

			// ---- 3c. contadores por puerta de ET6 (D-039; ET6_GATES.txt, tolerante a su ausencia)
			S.Append("\n== 3c. PUERTAS DE ET6 Y DIRECCION DEL POOL (ET6_GATES.txt) ==\n");
			{
				SortedDictionary<string, long[]> gsum = new SortedDictionary<string, long[]>(StringComparer.Ordinal); int withG = 0, validN = 0;
				foreach (RCamp k in camps)
				{
					if (!k.Valid || !(k.IsNull || k.IsInj)) continue; validN++;
					if (k.Et6Gates == null) continue; withG++; bool first = true;
					foreach (string ln in Lines(k.Et6Gates))
					{
						if (ln.Length == 0) continue;
						if (first && ln.StartsWith("[DIAGNOSTICO ET6", StringComparison.Ordinal)) { S.Append(k.Id).Append(" (").Append(k.Kind).Append("): ").Append(ln.Length > 260 ? ln.Substring(0, 260) : ln).Append('\n'); first = false; continue; }
						string[] f = ln.Split('\t'); if (f.Length >= 3 && f[0] != "puerta") { double a1, a2; if (TryD(f[1], out a1) && TryD(f[2], out a2)) { long[] g; string key = (k.IsInj ? "INJ " : "NULO ") + f[0]; if (!gsum.TryGetValue(key, out g)) { g = new long[2]; gsum[key] = g; } g[0] += (long)a1; g[1] += (long)a2; } }
					}
				}
				if (withG == 0) S.Append("ET6_GATES.txt ausente en las ").Append(I(validN)).Append(" campanas (runner anterior a D-039): sin contadores por puerta ni direccion del pool.\n");
				else { S.Append("campanas con ET6_GATES.txt: ").Append(I(withG)).Append('/').Append(I(validN)).Append(". Suma de combinaciones que fallan cada puerta (fallan / fallan solo esa):\n"); foreach (KeyValuePair<string, long[]> kv in gsum) S.Append("  ").Append(kv.Key).Append('\t').Append(I(kv.Value[0])).Append('\t').Append(I(kv.Value[1])).Append('\n'); }
			}

			// FP por puerta (embudo acumulado)
			S.Append("\nEmbudo acumulado por puerta (campanas con >=1 candidato que llega/pasa la puerta; entre parentesis el motivo de rechazo mas frecuente en las campanas que cierran ahi):\n");
			List<string[]> gates = FunnelRows(camps, nulls, injs, n1, n2, p, tauV, equivOk);
			S.Append("puerta\tnulas\tN1\tN2\tinyectadas\tmotivo_mas_frecuente\n");
			foreach (string[] g in gates) S.Append(g[0]).Append('\t').Append(g[1]).Append('\t').Append(g[2]).Append('\t').Append(g[3]).Append('\t').Append(g[4]).Append('\t').Append(g[5]).Append('\n');
			S.Append("(el motivo se cuenta solo en las campanas que llegaron a la puerta, es decir pasaron la fila anterior; tau usado en las filas P_pass/TODO = ").Append(F(tauV, "0.###")).Append("; las filas de ET7 usan el percentil recalculado cuando el runner no tenia vector nulo)\n");
			S.Append("Tasas de paso con IC por tipo (cada fila = proporcion de campanas del tipo que pasan la puerta):\n");
			foreach (string[] g in gates) { S.Append("  ").Append(g[0]).Append(": nulas ").Append(g[6]).Append(" | inyectadas ").Append(g[7]).Append('\n'); }

			// ---- 4. distribuciones
			S.Append("\n== 4. DISTRIBUCIONES POR ETAPA Y ESTADO DEL EMBUDO ==\n");
			S.Append("Estado del embudo por campana y por tipo:\n");
			foreach (string kind in new string[] { "N1", "N2", "INJ" })
			{
				Dictionary<string, int> st = new Dictionary<string, int>(StringComparer.Ordinal); List<string> order = new List<string>();
				foreach (RCamp k in camps) if (k.Valid && k.Kind == kind) { string s = FunnelState(k); if (!st.ContainsKey(s)) { st[s] = 0; order.Add(s); } st[s]++; }
				S.Append("  ").Append(kind).Append(": "); if (order.Count == 0) S.Append("sin campanas"); order.Sort(StringComparer.Ordinal); for (int i = 0; i < order.Count; i++) S.Append(i > 0 ? " | " : "").Append(order[i]).Append(" x").Append(I(st[order[i]])); S.Append('\n');
			}
			S.Append("por campana: id=estado\n"); foreach (RCamp k in camps) if (k.Valid) S.Append("  ").Append(k.Id).Append('=').Append(FunnelState(k)).Append('\n');
			S.Append("Distribucion (por tipo): mediana, P5/P95 (interpolacion lineal), min/max\n");
			foreach (string kind in new string[] { "N1", "N2", "INJ" })
			{
				List<RCamp> set = camps.FindAll(delegate(RCamp k) { return k.Valid && k.Kind == kind; }); if (set.Count == 0) continue;
				S.Append(" [").Append(kind).Append("]\n");
				S.Append("  fitness_t del mejor genoma de ET3 (best_fitness_t): ").Append(Dist(Pick(set, delegate(RCamp k) { return k.GD("ET3", "best_fitness_t"); }))).Append('\n');
				List<double> all = new List<double>(); foreach (RCamp k in set) all.AddRange(k.DatabankT); S.Append("  fitness_t de TODO el databank top-K (agregado): ").Append(Dist(all)).Append('\n');
				S.Append("  genomas validos en el databank (databank_valid): ").Append(Dist(Pick(set, delegate(RCamp k) { return k.GD("ET3", "databank_valid"); }))).Append('\n');
				S.Append("  genomas con 0 operaciones (zero_trade_evals): ").Append(Dist(Pick(set, delegate(RCamp k) { return k.GD("ET3", "zero_trade_evals"); }))).Append('\n');
				S.Append("  supervivientes ET4 candidatos=").Append(Dist(Pick(set, delegate(RCamp k) { return k.GD("ET4", "candidates"); }))).Append(" | pool=").Append(Dist(Pick(set, delegate(RCamp k) { return k.GD("ET4", "pool"); }))).Append('\n');
				S.Append("  supervivientes ET5 robust=").Append(Dist(Pick(set, delegate(RCamp k) { return k.GD("ET5", "robust"); }))).Append(" | PBO=").Append(Dist(Pick(set, delegate(RCamp k) { return k.GD("ET5", "pbo"); }))).Append('\n');
				S.Append("  ET6 accepted=").Append(Dist(Pick(set, delegate(RCamp k) { return k.GD("ET6", "accepted"); }))).Append(" | finalists=").Append(Dist(Pick(set, delegate(RCamp k) { return k.GD("ET6", "finalists"); }))).Append(" | ET7 pasan=").Append(Dist(Pick(set, delegate(RCamp k) { return k.GD("ET7", "pass"); }))).Append('\n');
				S.Append("  ensayos: ET3=").Append(Dist(Pick(set, delegate(RCamp k) { return D(Tk(k.Trials, "ET3")); }))).Append(" ET4_vecinos=").Append(Dist(Pick(set, delegate(RCamp k) { return D(Tk(k.Trials, "ET4_vecinos_nuevos")); }))).Append(" ET6_combinaciones=").Append(Dist(Pick(set, delegate(RCamp k) { return D(Tk(k.Trials, "ET6_combinaciones")); }))).Append(" acumulado=").Append(Dist(Pick(set, delegate(RCamp k) { return D(Tk(k.Trials, "acumulado")); }))).Append('\n');
				S.Append("  t del mejor portafolio (D-036): ").Append(Dist(Pick(set, delegate(RCamp k) { return k.BestT; }))).Append('\n');
				S.Append("  DSR de los portafolios de ET7: ").Append(Dist(PortVals(set, delegate(RPort q) { return q.Dsr; }))).Append('\n');
				S.Append("  P_pass de los finalistas: ").Append(Dist(PortVals(set, delegate(RPort q) { return q.Ppass; }))).Append('\n');
				S.Append("  MC DD_P95 (US$): ").Append(Dist(PortVals(set, delegate(RPort q) { return q.DdP95; }))).Append('\n');
				S.Append("  operaciones/mes y eventos protectores: NO exportados por el runner (solo existe el conteo de operaciones del ledger; ver propuesta de exports al final)\n");
			}

			// ---- 5. cascada de la regla inyectada
			S.Append("\n== 5. CASCADA DE LA REGLA INYECTADA (diagnostico del §5/§6 de ET2.md) ==\n");
			S.Append("id\tcierra_en\tfinalistas\tmejor_t\tt_par\tsqrt(2ln N_eff)\tcontiene_regla\tpasa_todo(tau)\n");
			foreach (RCamp k in injs)
			{
				double ne = !double.IsNaN(k.NEffUsed) ? k.NEffUsed : (!double.IsNaN(k.NEffEst) ? k.NEffEst : double.NaN);
				double mx = !double.IsNaN(ne) && ne > 1 ? Math.Sqrt(2 * Math.Log(ne)) : double.NaN;
				bool hasEq = false; foreach (RPort q in k.Ports) if (q.Equiv) hasEq = true;
				S.Append(k.Id).Append('\t').Append(k.Complete ? "ET7(completo)" : k.ClosedAt + ": " + k.ClosedWhy).Append('\t').Append(I(k.Ports.Count)).Append('\t').Append(F(k.BestT, "0.###")).Append('\t').Append(Tk(k.InjParams, "t_pair")).Append('\t').Append(F(mx, "0.###")).Append('\t').Append(k.EquivEvaluable ? (hasEq ? "SI" : "NO") : "NO_EVALUABLE").Append('\t').Append(k.VAll >= tauV && k.VAll >= 0 ? "SI" : "NO").Append('\n');
			}
			S.Append("(sqrt(2 ln N_eff) = orden de magnitud del maximo t esperado bajo el nulo entre N_eff pruebas; si t_par queda por debajo, el edge es indetectable POR DISENO -> §5(d) y D-020.)\n");

			// ---- 6. vector nulo
			S.Append("\n== 6. VECTOR NULO DEL PERCENTIL (D-036: t del mejor portafolio por campana nula) ==\n");
			{
				List<double> vec = new List<double>(); int cEt7 = 0, cEt3 = 0, cNone = 0;
				foreach (RCamp k in nulls) { if (k.BestTCase == "ET7") cEt7++; else if (k.BestTCase == "ET3") cEt3++; else cNone++; if (!double.IsNaN(k.BestT)) vec.Add(k.BestT); }
				S.Append("n_nulos_con_valor=").Append(I(vec.Count)).Append(" de ").Append(I(nulls.Count)).Append(" | llego a ET7 (mejor finalista)=").Append(I(cEt7)).Append(" | no llego a ET7: t del mejor genoma de ET3=").Append(I(cEt3)).Append(" | sin valor=").Append(I(cNone)).Append(" (el caso «mejor candidato de ET6» no se distingue: los finalistas de ET6 SON los candidatos de ET7)\n");
				if (cEt3 > 0) S.Append("ADVERTENCIA: los valores de ET3 son t de un genoma suelto, no de un portafolio ejecutado; mezclarlos con los de ET7 sesga el vector (D-036 los admite como respaldo).\n");
				List<double> sv = new List<double>(vec); sv.Sort(); S.Append("vector ordenado: "); for (int i = 0; i < sv.Count; i++) S.Append(i > 0 ? ", " : "").Append(F(sv[i], "0.####")); S.Append('\n');
				S.Append("percentiles del vector: ").Append(Dist(vec)).Append('\n');
				if (vec.Count < p.NullMinN) { S.Append("n del vector (").Append(I(vec.Count)).Append(") < ").Append(I(p.NullMinN)).Append(": D-036 exige n>=20; el percentil de los inyectados es solo informativo\n"); }
				S.Append("mejor portafolio de cada inyectado y su percentil frente al vector nulo (100*#{nulo<x}/n):\n");
				foreach (RCamp k in injs)
				{
					int below = 0, n = 0; foreach (RCamp o in nullVecCamps) { n++; if (o.BestT < k.BestT) below++; }
					S.Append("  ").Append(k.Id).Append(" t=").Append(F(k.BestT, "0.###")).Append(" caso=").Append(k.BestTCase).Append(" percentil=").Append(n > 0 && !double.IsNaN(k.BestT) ? F(100.0 * below / n, "0.0") : "NA").Append(" (n=").Append(I(n)).Append(")\n");
				}
				S.Append("detalle de los portafolios que llegan a ET7 (pct: R=recalculado aqui, E=registrado por el runner; otras=pasa DSR+percentil+MC+P_pass_boot):\n");
				S.Append("  campana\ttipo\tportafolio\tt\tP_pass\tDSR\tpct\tMC_DD_P95\totras_puertas\tequiv_regla\n");
				foreach (RCamp k in camps) { if (!k.Valid || !(k.IsNull || k.IsInj)) continue; foreach (RPort q in k.Ports) S.Append("  ").Append(k.Id).Append('\t').Append(k.Kind).Append('\t').Append(q.Id12).Append('\t').Append(F(q.Tstat, "0.###")).Append('\t').Append(F(q.Ppass, "0.###")).Append('\t').Append(F(q.Dsr, "0.####")).Append('\t').Append(F(q.PctUsed, "0.0")).Append(q.PctRecomputed ? "R" : "E").Append('\t').Append(F(q.DdP95, "0.00")).Append('\t').Append(q.OtherOk ? "SI" : "NO").Append('\t').Append(k.EquivEvaluable ? (q.Equiv ? "SI" : "NO") : "NE").Append('\n'); }
				S.Append("Fase A vs Fase B: la Fase A usa REDUCED (200x50): el maximo nulo crece ~sqrt(2 ln N) con el tamano de busqueda, asi que este vector queda SESGADO A LA BAJA frente a campanas FULL (percentiles optimistas). La comparacion con la Fase B requiere correr este generador sobre RUNBATCH_ET2_FASE_B y contrastar los vectores.\n");
			}

			// ---- 7. factibilidad de umbrales [B]
			S.Append("\n== 7. TABLA DE FACTIBILIDAD DE CADA UMBRAL [B] ==\n");
			S.Append("(sin cambiar nada: solo mide que fraccion de campanas inyectadas pasa cada puerta y, donde los archivos lo permiten, barre el umbral con las demas puertas fijas y P_pass sin filtrar [tau=").Append(F(tauRun, "0.###")).Append("]; no se tocan estructura ni umbrales)\n");
			S.Append("umbral\tvigente\ttipo\tbarrido o evidencia\n");
			SweepRows(S, nulls, injs, camps, p, tauRun, equivOk);
			S.Append("DD<=2.000 y peor dia<=1.000 (derivados de la cuenta): solo endurecibles, nunca aflojables (propuesta D-037); no se barren.\n");

			// ---- 8. curva de P_pass
			S.Append("\n== 8. CURVA DE P_pass Y UMBRAL ==\n");
			S.Append("regla de ET2: menor umbral (multiplo de ").Append(F(p.PpassStep, "0.##")).Append(") con FP <= ").Append(F(100 * p.MaxFp, "0.#")).Append("% sobre los nulos, piso ").Append(F(p.PpassFloor, "0.00")).Append("; potencia = ").Append(equivOk ? "con regla/equivalente" : "COTA SUPERIOR (equivalencia no evaluable)").Append('\n');
			S.Append("tau\tFP\tFP_N1\tFP_N2\tpotencia\tpotencia_cota_superior\n");
			foreach (double t in grid)
				S.Append(F(t, "0.00")).Append('\t').Append(Frac(cnt(nulls, t), nulls.Count)).Append('\t').Append(Frac(cnt(n1, t), n1.Count)).Append('\t').Append(Frac(cnt(n2, t), n2.Count)).Append('\t').Append(equivOk ? Frac(cntE(injs, t), injs.Count) : "NE").Append('\t').Append(Frac(cntU(injs, t), injs.Count)).Append('\n');
			if (double.IsNaN(chosen)) S.Append("umbral propuesto: NO FACTIBLE (ningun tau de la rejilla con tau>=piso deja FP <= ").Append(F(100 * p.MaxFp, "0.#")).Append("%); no se fija.\n");
			else S.Append("umbral propuesto (ppass_min): ").Append(F(chosen, "0.00")).Append(" -> FP ").Append(Frac(R.KFpAtThreshold, nulls.Count)).Append(" ; potencia ").Append(equivOk ? Frac(R.KPowerEquivAtThreshold, injs.Count) : "NE").Append(" ; potencia cota superior ").Append(Frac(R.KPowerUpperAtThreshold, injs.Count)).Append(". Sin el piso el menor tau factible seria ").Append(double.IsNaN(chosenNoFloor) ? "ninguno" : F(chosenNoFloor, "0.00")).Append(".\n");
			if (!double.IsNaN(chosen)) { bool trivial = true; for (int i = 0; i < nulls.Count; i++) if (nulls[i].VAll >= 0) trivial = false; if (trivial) S.Append("NOTA: ninguna campana nula pasa las demas puertas (FP=0 para todo tau): el umbral queda en el piso por defecto, NO porque P_pass sea la puerta que discrimina. No es evidencia de que 0,50 sea el valor adecuado.\n"); }

			// ---- 9. N_eff
			S.Append("\n== 9. N_eff (D-038): muestra, IC, cobertura y series planas ==\n");
			S.Append("id\ttipo\tn_muestra\tobservados\tf1\tf2\tN_eff_Chao1\tIC95_inf\tIC95_sup\tcobertura_GT\tN_eff_usado\tplanas\tfraccion_planas\tfalla_cerrada\n");
			foreach (RCamp k in camps)
			{
				if (!k.Valid || !(k.IsNull || k.IsInj)) continue;
				S.Append(k.Id).Append('\t').Append(k.Kind).Append('\t').Append(k.NSample >= 0 ? I(k.NSample) : "NA").Append('\t').Append(k.NEffObs >= 0 ? I(k.NEffObs) : "NA").Append('\t').Append(k.NEffF1 >= 0 ? I(k.NEffF1) : "NA").Append('\t').Append(k.NEffF2 >= 0 ? I(k.NEffF2) : "NA").Append('\t').Append(F(k.NEffEst, "0.##")).Append('\t').Append(F(k.NEffLo, "0.##")).Append('\t').Append(F(k.NEffHi, "0.##")).Append('\t').Append(F(k.GoodTuring, "0.0000")).Append('\t').Append(F(k.NEffUsed, "0.##")).Append('\t').Append(k.FlatN >= 0 ? I(k.FlatN) : "NA").Append('\t').Append(F(k.FlatFrac, "0.####")).Append('\t').Append(k.Et7Fail.Length > 0 ? k.Et7Fail : (k.Ports.Count == 0 ? "no_llega_a_ET7" : "no")).Append('\n');
			}
			{
				int fails = 0, reached = 0; foreach (RCamp k in camps) if (k.Valid && (k.IsNull || k.IsInj) && k.Ports.Count > 0) { reached++; if (k.Et7Fail.Length > 0) fails++; }
				S.Append("campanas que llegan a ET7: ").Append(I(reached)).Append(" | de ellas con N_eff en FALLA CERRADA (muestra insuficiente, cobertura, planas...): ").Append(I(fails)).Append(fails > 0 ? " (en ellas el DSR es NaN y ningun portafolio puede pasar)" : "").Append('\n');
				foreach (string kind in new string[] { "N1", "N2", "INJ" })
				{
					List<RCamp> set = camps.FindAll(delegate(RCamp k) { return k.Valid && k.Kind == kind && k.NEffObs >= 0; }); if (set.Count == 0) continue;
					S.Append("  [").Append(kind).Append("] N_eff Chao1: ").Append(Dist(Pick(set, delegate(RCamp k) { return k.NEffEst; }))).Append(" | IC_sup: ").Append(Dist(Pick(set, delegate(RCamp k) { return k.NEffHi; }))).Append(" | cobertura GT: ").Append(Dist(Pick(set, delegate(RCamp k) { return k.GoodTuring; }))).Append(" | f1: ").Append(Dist(Pick(set, delegate(RCamp k) { return (double)k.NEffF1; }))).Append(" | fraccion planas: ").Append(Dist(Pick(set, delegate(RCamp k) { return k.FlatFrac; }))).Append('\n');
				}
			}
			S.Append("DSR de los portafolios con tres N_eff (punto / IC inferior / IC superior), recalculado con SR, asimetria, curtosis y T impresos en ET7 y la varianza del SR implicita en SR0 (precision de 3 decimales en asimetria/curtosis):\n");
			DsrRows(S, nulls, injs, p);
			S.Append("cobertura empirica del IC del N_eff (¿cubre el N_eff de una referencia?): NO EVALUABLE con los archivos del lote: exige agrupar el top-K o las pruebas completas de un par de campanas como referencia; el runner solo exporta la muestra uniforme (ET3_NEFF_SAMPLE.bin). La salvaguarda real son los FP de la seccion 3 (D-038).\n");
			{
				List<double> gt = new List<double>(); List<double> ns = new List<double>(); foreach (RCamp k in camps) if (k.Valid && (k.IsNull || k.IsInj)) { gt.Add(k.GoodTuring); ns.Add((double)k.NSample); }
				S.Append("propuesta mecanica de parametros (informativa, no decide): cobertura GT observada ").Append(Dist(gt)).Append(" ; n_muestra observado ").Append(Dist(ns)).Append(" -> `neff_sample_cov_min` vigente 0.90 y `neff_sample_min_n` vigente 1000 solo son coherentes si ninguna campana valida cae por debajo; con la distribucion de arriba decide el orquestador/usuario.\n");
			}

			// ---- 10. coste
			S.Append("\n== 10. COSTE REAL ==\n");
			{
				List<double> sec = new List<double>(), mem = new List<double>(); double tot = 0; foreach (RCamp k in camps) { if (!double.IsNaN(k.Seconds)) { sec.Add(k.Seconds); tot += k.Seconds; } if (!double.IsNaN(k.PeakMb)) mem.Add(k.PeakMb); }
				S.Append("segundos por campana (solo las ejecutadas en esta sesion; las reanudadas figuran 0): ").Append(Dist(sec)).Append(" ; suma=").Append(F(tot / 3600.0, "0.00")).Append(" h ; pico working set MB: ").Append(Dist(mem)).Append('\n');
				S.Append("ms/evaluacion (Paso 0) y horas de la Fase B: no estan en el lote (TIMING); citarlos del SUMMARY de TIMING.\n");
			}

			// ---- 11. veredicto
			S.Append("\n== 11. VEREDICTO MECANICO DEL GATE DE ET2 Y SIGUIENTE PASO (sin decidir por el usuario) ==\n");
			bool fpPt = nulls.Count > 0 && R.FpAtThreshold <= p.MaxFp + 1e-12;
			double fpUp = nulls.Count > 0 ? NQCET2_Stats.UpperOneSided(R.KFpAtThreshold, nulls.Count, p.Alpha) : double.NaN;
			bool fpDemo = nulls.Count > 0 && fpUp <= p.MaxFp;
			if (nulls.Count == 0 || R.PctNotEvaluable) R.FpStatus = "NO_EVALUABLE"; else if (!fpPt) R.FpStatus = "NO_CUMPLE"; else R.FpStatus = fpDemo ? "CUMPLE_DEMOSTRADO" : "CUMPLE_PUNTUAL_NO_DEMOSTRADO";
			double pw = equivOk ? R.PowerEquivAtThreshold : R.PowerUpperAtThreshold; int kpw = equivOk ? R.KPowerEquivAtThreshold : R.KPowerUpperAtThreshold;
			double pwLow = injs.Count > 0 ? LowerOneSided(kpw, injs.Count, p.Alpha) : double.NaN;
			if (injs.Count == 0 || R.PctNotEvaluable) R.PowerStatus = "NO_EVALUABLE";
			else if (!equivOk) R.PowerStatus = R.PowerUpperAtThreshold + 1e-12 < p.MinPower ? "NO_CUMPLE" : "INDETERMINADA_FALTA_EXPORT_DE_OPERACIONES";
			else R.PowerStatus = pw + 1e-12 < p.MinPower ? "NO_CUMPLE" : (pwLow >= p.MinPower ? "CUMPLE_DEMOSTRADO" : "CUMPLE_PUNTUAL_NO_DEMOSTRADO");
			bool incomplete = nulls.Count < p.ExpectedNull || injs.Count < p.ExpectedInj;
			if (R.FpStatus == "NO_CUMPLE" || R.PowerStatus == "NO_CUMPLE") R.Verdict = "GATE NO CUMPLIDO";
			else if (R.FpStatus == "NO_EVALUABLE" || R.PowerStatus == "NO_EVALUABLE" || R.PowerStatus.StartsWith("INDETERMINADA", StringComparison.Ordinal)) R.Verdict = "GATE INDETERMINADO";
			else if (R.FpStatus == "CUMPLE_DEMOSTRADO" && R.PowerStatus == "CUMPLE_DEMOSTRADO" && !incomplete) R.Verdict = "GATE CUMPLIDO Y DEMOSTRADO";
			else R.Verdict = "GATE CUMPLIDO PUNTUALMENTE; NO DEMOSTRADO (n pequeno)" + (incomplete ? "; campanas incompletas" : "");
			if (multiLevel)
			{
				// D-040: el veredicto se da POR NIVEL de edge; el FP se mide sobre las nulas y la potencia agrupada no tiene sentido (mezcla niveles).
				StringBuilder lv2 = new StringBuilder(); foreach (NQCET2_LevelResult lr in R.Levels) { if (lv2.Length > 0) lv2.Append("; "); lv2.Append("+").Append(F(lr.Level, "0")).Append(": ").Append(I(lr.K)).Append('/').Append(I(lr.N)).Append(' ').Append(lr.Status); }
				R.PowerStatus = double.IsNaN(R.MinEdge) ? (R.PctNotEvaluable ? "NO_EVALUABLE" : "NO_CUMPLE") : (R.MinEdgeDemostrado == R.MinEdge ? "CUMPLE_DEMOSTRADO" : "CUMPLE_PUNTUAL_NO_DEMOSTRADO");
				string edgeTxt = double.IsNaN(R.MinEdge) ? "ningun nivel alcanza potencia " + F(100 * p.MinPower, "0") + "%" : "edge minimo detectable +US$" + F(R.MinEdge, "0");
				if (R.FpStatus == "NO_CUMPLE") R.Verdict = "GATE NO CUMPLIDO (FP); potencia por nivel: " + lv2;
				else if (R.FpStatus == "NO_EVALUABLE" || R.PowerStatus == "NO_EVALUABLE") R.Verdict = "GATE INDETERMINADO; potencia por nivel: " + lv2;
				else if (double.IsNaN(R.MinEdge)) R.Verdict = "GATE NO CUMPLIDO EN NINGUN NIVEL (" + edgeTxt + "); potencia por nivel: " + lv2;
				else R.Verdict = "GATE CUMPLIDO PARA EDGE >= +US$" + F(R.MinEdge, "0") + (R.FpStatus == "CUMPLE_DEMOSTRADO" && R.PowerStatus == "CUMPLE_DEMOSTRADO" && !incomplete ? " (DEMOSTRADO)" : " (PUNTUAL, NO DEMOSTRADO)") + "; potencia por nivel: " + lv2 + (equivOk ? "" : "; potencia = cota superior sin export de operaciones");
			}
			S.Append("criterio (D-037): FP puntual <= ").Append(F(100 * p.MaxFp, "0.#")).Append("% Y potencia puntual >= ").Append(F(100 * p.MinPower, "0.#")).Append("%; los IC se muestran siempre; con n pequeno NO se afirma «demostrado».\n");
			S.Append("tau evaluado=").Append(F(tauV, "0.00")).Append(double.IsNaN(chosen) ? " (umbral no factible: se evalua con el tau de la corrida)" : " (umbral propuesto de la seccion 8)").Append('\n');
			S.Append("FP: ").Append(Prop(R.KFpAtThreshold, nulls.Count, p)).Append(" -> ").Append(R.FpStatus).Append('\n');
			S.Append("potencia (").Append(equivOk ? "con regla/equivalente" : "COTA SUPERIOR, equivalencia no evaluable").Append("): ").Append(Prop(kpw, injs.Count, p)).Append(" -> ").Append(R.PowerStatus).Append('\n');
			S.Append("VEREDICTO MECANICO: ").Append(R.Verdict).Append('\n');
			S.Append("Siguiente paso sugerido (mecanico; decide el usuario/orquestador):\n");
			if (R.FpStatus == "NO_CUMPLE") S.Append("  - FP > ").Append(F(100 * p.MaxFp, "0.#")).Append("%: endurecer la puerta que deja pasar a los nulos (ver seccion 3/7) y repetir con semillas nuevas; NO aflojar nada.\n");
			if (R.PowerStatus == "NO_CUMPLE") S.Append("  - potencia < ").Append(F(100 * p.MinPower, "0.#")).Append("%: diagnosticar con la cascada (seccion 5) que puerta mata a la regla; si t_par < sqrt(2 ln N_eff) correr la CURVA DE POTENCIA a +40 y +60 (D-037) y elevar D-020; aflojar solo una puerta [B] con holgura de FP; si no, redisenar el embudo como version menor.\n");
			if (R.PowerStatus.StartsWith("INDETERMINADA", StringComparison.Ordinal)) S.Append("  - la potencia con equivalencia no se puede evaluar: pedir al orquestador el export de operaciones (seccion 3) y volver a correr este generador sobre el MISMO lote (no hace falta recalcular campanas si el runner reanuda y escribe los TSV).\n");
			if (R.Verdict.StartsWith("GATE CUMPLIDO PUNTUALMENTE", StringComparison.Ordinal)) S.Append("  - el cumplimiento es puntual: con n=").Append(I(nulls.Count)).Append(" nulas la cota superior unilateral del FP es ").Append(F(100 * fpUp, "0.0")).Append("% y para demostrar FP <= ").Append(F(100 * p.MaxFp, "0.#")).Append("% con 0 FP hacen falta ").Append(I(NQCET2_Stats.MinNForZeroFp(p.MaxFp, p.Alpha))).Append(" campanas; considerar la Fase B / FULL de inyectados y mas semillas antes de congelar NQC 2.1.0.\n");
			if (incomplete) S.Append("  - lote incompleto respecto de D-037 (nulas ").Append(I(nulls.Count)).Append("/").Append(I(p.ExpectedNull)).Append(", inyectadas ").Append(I(injs.Count)).Append("/").Append(I(p.ExpectedInj)).Append("): completar antes de concluir.\n");
			S.Append("Sesgos declarados: Fase A usa tamano REDUCED (menos pruebas que deflactar: potencia optimista, vector nulo sesgado a la baja); percentil recalculado leave-one-out con n<20 (relajado a n>=").Append(I(p.NullMinNOffline)).Append(" solo en este recalculo); equivalencia ").Append(equivOk ? "evaluada" : "NO evaluada").Append(".\n");
			S.Append("Exports pendientes en el runner (si faltan): ET6_MEMBER_TRADES.tsv e INJ_PAIR_TRADES.tsv (seccion 3); operaciones/mes y eventos protectores por finalista (seccion 4); tasas de paso por puerta DENTRO de ET4 (F1/F2/F3 por candidato solo se conoce como conteo y causa de ET4_REJECTS.tsv) y de ET6 por combinacion aceptada (solo se exportan las descartadas duras).\n");

			// ---- advertencias
			S.Append("\n== ADVERTENCIAS DEL GENERADOR ==\n");
			if (warn.Count == 0) S.Append("ninguna\n"); else foreach (string w in warn) S.Append("- ").Append(w).Append('\n');

			// ---- 12. hashes
			S.Append("\n== HASHES DE TRAZABILIDAD ==\n");
			StringBuilder man = new StringBuilder(); foreach (KeyValuePair<string, string[]> kv in rd.Files) man.Append(kv.Value[2]).Append("  ").Append(kv.Value[1]).Append("  ").Append(kv.Value[0]).Append('\n');
			foreach (KeyValuePair<string, string[]> kv in rd.Files) R.Inputs.Add(kv.Value);
			R.InputsSha = Sha256Text(man.ToString());
			S.Append("archivos_leidos=").Append(I(rd.Files.Count)).Append(" inputs_manifest_sha256=").Append(R.InputsSha).Append('\n');
			S.Append("sha256  bytes  ruta (relativa a la carpeta del lote)\n").Append(man.ToString());
			string body = S.ToString(); R.ReportSha = Sha256Text(body);
			R.Text = body + "report_sha256=" + R.ReportSha + " (sha256 del texto anterior a esta linea)\n";
			R.Ok = true; return R;
		}

		// ------------------------------------------------------------------ piezas del informe
		private static string FirstLine(string t) { if (t == null) return "(ilegible)"; foreach (string l in Lines(t)) if (l.Trim().Length > 0) return l.Trim().Length > 160 ? l.Trim().Substring(0, 160) : l.Trim(); return "(vacio)"; }
		private static string Short(string h) { return h.Length > 12 ? h.Substring(0, 12) : (h.Length == 0 ? "NA" : h); }
		private static string Frac(int k, int n) { return n <= 0 ? "NA" : I(k) + "/" + I(n) + " (" + F(100.0 * k / n, "0.0") + "%)"; }
		private static string FunnelState(RCamp k)
		{
			if (k.Complete) return "COMPLETO";
			if (k.RunStatus.StartsWith("SIN_FINALISTAS_EN_", StringComparison.Ordinal)) return k.RunStatus;
			return "CAMPANIA_CERRADA_EN_" + k.ClosedAt + (k.ClosedWhy.Length > 0 ? " (" + k.ClosedWhy + ")" : "");
		}
		private static string PctSource(List<RCamp> camps)
		{
			int rec = 0, run = 0; foreach (RCamp k in camps) if (k.Valid && k.Ports.Count > 0) { if (k.NullN >= 20) run++; else rec++; }
			if (rec == 0 && run == 0) return "sin campanas que lleguen a ET7";
			return I(run) + " campana(s) con el veredicto del runner (vector nulo >= 20) y " + I(rec) + " con el percentil RECALCULADO aqui (el runner no tenia vector nulo: nulo_ET2_n < 20 y su puerta falla cerrada)";
		}
		private static string ListIds(List<RCamp> set, double tau) { List<string> a = new List<string>(); foreach (RCamp k in set) if (k.VAll >= 0 && k.VAll >= tau) a.Add(k.Id + "(P_pass=" + F(k.VAll, "0.###") + ",t=" + F(k.BestT, "0.###") + ")"); return a.Count == 0 ? "ninguna" : string.Join(", ", a.ToArray()); }
		private static string ListIdsInj(List<RCamp> set, double tau) { List<string> a = new List<string>(); foreach (RCamp k in set) if (k.VAll >= 0 && k.VAll >= tau) a.Add(k.Id + (k.EquivEvaluable ? (k.VEquiv >= 0 && k.VEquiv >= tau ? "[contiene regla/equiv.]" : "[SIN regla equivalente]") : "[equiv. no evaluable]")); return a.Count == 0 ? "ninguna" : string.Join(", ", a.ToArray()); }
		private static List<double> Pick(List<RCamp> set, Func<RCamp, double> f) { List<double> a = new List<double>(); foreach (RCamp k in set) a.Add(f(k)); return a; }
		private static List<double> PortVals(List<RCamp> set, Func<RPort, double> f) { List<double> a = new List<double>(); foreach (RCamp k in set) foreach (RPort q in k.Ports) a.Add(f(q)); return a; }

		// reachedBy(stage) devuelve si la campana tiene >=1 candidato que PASA esa puerta (acumulado)
		private static List<string[]> FunnelRows(List<RCamp> camps, List<RCamp> nulls, List<RCamp> injs, List<RCamp> n1, List<RCamp> n2, NQCET2_ReportParams p, double tau, bool equivOk)
		{
			List<string[]> rows = new List<string[]>();
			Func<RCamp, bool> prev = delegate(RCamp k0) { return true; };
			Action<string, Func<RCamp, bool>, Func<RCamp, string>> add = delegate(string name, Func<RCamp, bool> pass, Func<RCamp, string> reason)
			{
				Func<RCamp, bool> pv = prev;
				int a = 0, b = 0, c1 = 0, c2 = 0; foreach (RCamp k in nulls) if (pass(k)) { a++; if (k.Kind == "N1") c1++; else c2++; } foreach (RCamp k in injs) if (pass(k)) b++;
				Dictionary<string, int> rs = new Dictionary<string, int>(StringComparer.Ordinal); string top = "-"; int best = 0;
				foreach (RCamp k in camps) if (k.Valid && (k.IsNull || k.IsInj) && pv(k) && !pass(k)) { string r = reason == null ? "" : reason(k); if (r.Length == 0) continue; int v; rs.TryGetValue(r, out v); rs[r] = v + 1; if (rs[r] > best || (rs[r] == best && string.CompareOrdinal(r, top) < 0)) { best = rs[r]; top = r; } }
				rows.Add(new string[] { name, I(a) + "/" + I(nulls.Count), I(c1) + "/" + I(n1.Count), I(c2) + "/" + I(n2.Count), I(b) + "/" + I(injs.Count), best > 0 ? top + " (x" + I(best) + ")" : "-", Prop(a, nulls.Count, p), Prop(b, injs.Count, p) });
				prev = pass;
			};
			add("ET3 databank con genomas validos", delegate(RCamp k) { return k.G("ET3", "databank_valid", 0) > 0; }, delegate(RCamp k) { return "databank vacio"; });
			add("ET4 F1 (neto 1,5x)", delegate(RCamp k) { return k.G("ET4", "candidates", 0) - k.G("ET4", "f1_rejected", 0) > 0; }, delegate(RCamp k) { return TopCause(k, "F1"); });
			add("ET4 F2", delegate(RCamp k) { return k.G("ET4", "candidates", 0) - k.G("ET4", "f1_rejected", 0) - k.G("ET4", "f2_rejected", 0) > 0; }, delegate(RCamp k) { return TopCause(k, "F2"); });
			add("ET4 F3 (pool>0)", delegate(RCamp k) { return k.G("ET4", "pool", 0) > 0; }, delegate(RCamp k) { string c = TopCause(k, "F3"); return c.Length > 0 ? c : "pool vacio"; });
			add("ET5 filtro de anios (robust>0)", delegate(RCamp k) { return k.G("ET5", "robust", 0) > 0; }, delegate(RCamp k) { return k.ClosedAt == "ET5" ? k.ClosedWhy : (k.ClosedAt.Length > 0 ? "cerrada antes" : ""); });
			add("ET5 PBO (no cierra la campana)", delegate(RCamp k) { return k.G("ET5", "robust", 0) > 0 && k.GS("ET5", "campaign_closed") != "SI" && k.GS("ET5", "pbo_pass") != "NO"; }, delegate(RCamp k) { return k.ClosedAt == "ET5" ? k.ClosedWhy : ""; });
			add("ET6 combinaciones aceptadas (>0)", delegate(RCamp k) { return k.G("ET6", "accepted", 0) > 0; }, delegate(RCamp k) { return TopEt6(k); });
			add("ET6 finalistas (>0)", delegate(RCamp k) { return k.G("ET6", "finalists", 0) > 0 || k.Ports.Count > 0; }, delegate(RCamp k) { return k.ClosedAt.Length > 0 ? k.ClosedAt + ": " + k.ClosedWhy : "sin finalistas"; });
			add("ET7 DSR", delegate(RCamp k) { foreach (RPort q in k.Ports) if (q.DsrOk) return true; return false; }, delegate(RCamp k) { return Et7Top(k, "DSR"); });
			add("ET7 + percentil nulo (recalculado si no hay vector)", delegate(RCamp k) { foreach (RPort q in k.Ports) if (q.DsrOk && q.PctOk) return true; return false; }, delegate(RCamp k) { return Et7Top(k, "PERCENTIL_NULO"); });
			add("ET7 + MC DD P95", delegate(RCamp k) { foreach (RPort q in k.Ports) if (q.DsrOk && q.PctOk && q.McOk) return true; return false; }, delegate(RCamp k) { return Et7Top(k, "MC_DD_P95"); });
			add("ET7 + P_pass bootstrap (si hay umbral)", delegate(RCamp k) { foreach (RPort q in k.Ports) if (q.DsrOk && q.PctOk && q.McOk && q.PbootOk) return true; return false; }, delegate(RCamp k) { return Et7Top(k, "PPASS_BOOT"); });
			add("P_pass >= tau (TODO)", delegate(RCamp k) { return k.VAll >= 0 && k.VAll >= tau; }, delegate(RCamp k) { return k.Ports.Count > 0 ? "P_pass bajo tau" : ""; });
			if (equivOk) add("TODO y contiene la regla inyectada/equivalente", delegate(RCamp k) { return k.VEquiv >= 0 && k.VEquiv >= tau; }, delegate(RCamp k) { return k.IsInj ? "aprobado sin regla equivalente" : ""; });
			return rows;
		}
		private static string TopCause(RCamp k, string st) { Dictionary<string, int> d = new Dictionary<string, int>(StringComparer.Ordinal); string top = ""; int b = 0; foreach (string[] r in k.Et4Rejects) if (r[0] == st) { int v; d.TryGetValue(r[1], out v); d[r[1]] = v + 1; if (d[r[1]] > b || (d[r[1]] == b && string.CompareOrdinal(r[1], top) < 0)) { b = d[r[1]]; top = r[1]; } } return top.Length > 0 ? st + ": " + top : ""; }
		private static string TopEt6(RCamp k) { Dictionary<string, int> d = new Dictionary<string, int>(StringComparer.Ordinal); string top = ""; int b = 0; foreach (string[] r in k.Et6Rej) { int v; d.TryGetValue(r[0], out v); d[r[0]] = v + 1; if (d[r[0]] > b || (d[r[0]] == b && string.CompareOrdinal(r[0], top) < 0)) { b = d[r[0]]; top = r[0]; } } return top.Length > 0 ? "ET6 puerta " + top : (k.ClosedAt.Length > 0 && string.CompareOrdinal(k.ClosedAt, "ET6") <= 0 ? k.ClosedAt + ": " + k.ClosedWhy : ""); }
		private static string Et7Top(RCamp k, string gate)
		{
			foreach (RPort q in k.Ports) foreach (string c in q.Causes) if (c.StartsWith(gate + ":", StringComparison.Ordinal)) { string t = NormCause(c); return t.Length > 90 ? t.Substring(0, 90) : t; }
			return k.Ports.Count == 0 ? "" : gate + " (recalculado)";
		}

		// barrido de un umbral con las demas puertas fijas y P_pass sin filtrar (tau = tauRun)
		private delegate bool PassFn(RPort q, double thr);
		private static int CountCamps(List<RCamp> set, PassFn f, double thr, double tau, bool needEquiv)
		{
			int a = 0; foreach (RCamp k in set) { bool ok = false; foreach (RPort q in k.Ports) { if (double.IsNaN(q.Ppass) || q.Ppass < tau) continue; if (needEquiv && !q.Equiv) continue; if (f(q, thr)) { ok = true; break; } } if (ok) a++; } return a;
		}
		private static void Sweep(StringBuilder S, string name, string cur, double[] vals, bool lowerIsLoose, PassFn f, List<RCamp> nulls, List<RCamp> injs, NQCET2_ReportParams p, double tau, bool equivOk, string unit)
		{
			S.Append(name).Append('\t').Append(cur).Append('\t').Append("barrible con estos archivos").Append('\t');
			double pick = double.NaN; int kp = 0, kfp = 0; StringBuilder row = new StringBuilder();
			List<double> order = new List<double>(vals); order.Sort(); if (!lowerIsLoose) order.Reverse();   // del mas laxo al mas estricto
			foreach (double v in order)
			{
				int fp = CountCamps(nulls, f, v, tau, false), pw = CountCamps(injs, f, v, tau, equivOk);
				row.Append(F(v, "0.###")).Append(unit).Append(":FP=").Append(I(fp)).Append('/').Append(I(nulls.Count)).Append(",pot").Append(equivOk ? "" : "_cota_sup").Append("=").Append(I(pw)).Append('/').Append(I(injs.Count)).Append("; ");
				if (double.IsNaN(pick) && nulls.Count > 0 && (double)fp / nulls.Count <= p.MaxFp) { pick = v; kp = pw; kfp = fp; }
			}
			S.Append(row.ToString());
			if (double.IsNaN(pick)) S.Append("=> NO FACTIBLE en la rejilla (ningun valor deja FP <= ").Append(F(100 * p.MaxFp, "0.#")).Append("%)\n"); else S.Append("=> valor mas laxo con FP <= ").Append(F(100 * p.MaxFp, "0.#")).Append("%: ").Append(F(pick, "0.###")).Append(unit).Append(" (FP ").Append(I(kfp)).Append('/').Append(I(nulls.Count)).Append(", potencia ").Append(I(kp)).Append('/').Append(I(injs.Count)).Append(equivOk ? "" : " cota superior").Append(")\n");
		}
		private static void SweepRows(StringBuilder S, List<RCamp> nulls, List<RCamp> injs, List<RCamp> camps, NQCET2_ReportParams p, double tau, bool equivOk)
		{
			// cada barrido deja el resto de puertas de ET7 como estan (las banderas ya recalculadas), salvo la barrida
			PassFn dsr = delegate(RPort q, double thr) { return !double.IsNaN(q.Dsr) && q.Dsr >= thr && q.PctOk && q.McOk && q.PbootOk; };
			Sweep(S, "DSR minimo", F(p.DsrMin, "0.##"), new double[] { 0.50, 0.60, 0.70, 0.80, 0.90, 0.95, 0.99 }, true, dsr, nulls, injs, p, tau, equivOk, "");
			PassFn pct = delegate(RPort q, double thr) { return q.DsrOk && !double.IsNaN(q.PctUsed) && q.PctUsed >= thr && q.McOk && q.PbootOk; };
			Sweep(S, "percentil nulo minimo", F(p.NullPctMin, "0"), new double[] { 50, 60, 70, 80, 90, 95, 99 }, true, pct, nulls, injs, p, tau, equivOk, "");
			PassFn mc = delegate(RPort q, double thr) { return q.DsrOk && q.PctOk && !double.IsNaN(q.DdP95) && q.DdP95 <= thr && q.PbootOk; };
			Sweep(S, "MC DD P95 maximo (US$; solo endurecible <= 2000)", "2000", new double[] { 500, 750, 1000, 1250, 1500, 1750, 2000 }, false, mc, nulls, injs, p, tau, equivOk, "");
			PassFn pb = delegate(RPort q, double thr) { return q.DsrOk && q.PctOk && q.McOk && !double.IsNaN(q.PpassBoot) && q.PpassBoot >= thr; };
			Sweep(S, "ppass_boot_min (P_pass bootstrap de ET7; hoy solo se reporta)", "solo_reporta", new double[] { 0.0, 0.25, 0.50, 0.60, 0.70, 0.80, 0.90, 0.95 }, true, pb, nulls, injs, p, tau, equivOk, "");
			PassFn pp = delegate(RPort q, double thr) { return q.OtherOk && q.Ppass >= thr; };
			Sweep(S, "ppass_min (P_pass de ET6; piso 0,50)", F(tau, "0.##") + " (corrida)", new double[] { 0.0, 0.25, 0.50, 0.60, 0.70, 0.80, 0.90, 0.95, 1.0 }, true, pp, nulls, injs, p, 0.0, equivOk, "");
			// puertas de ET4/ET5/ET6: tasa de paso de las inyectadas / nulas con el valor vigente; no barribles sin re-correr
			S.Append("F2 (>=7/12 vecinos)\t>=7/12\tNO ajustable con estos archivos\tfraccion de inyectadas con >=1 superviviente tras F2 y FP acumulado: filas «ET4 F2» de la seccion 3 (para barrer hay que re-correr ET4 con otro umbral)\n");
			S.Append("F3 (>=70%)\t>=70%\tNO ajustable con estos archivos\tfilas «ET4 F3 (pool>0)» de la seccion 3\n");
			S.Append("anios positivos (>=2/3) y PBO <= 0,40\t>=2/3 ; 0,40\tNO ajustable con estos archivos\tfilas «ET5 ...» de la seccion 3 (PBO por campana en la seccion 4); requiere re-correr ET5\n");
			Dictionary<string, int[]> g6 = new Dictionary<string, int[]>(StringComparer.Ordinal); List<string> names = new List<string>();
			foreach (RCamp k in camps) if (k.Valid && (k.IsNull || k.IsInj)) foreach (string[] r in k.Et6Rej) { int[] v; if (!g6.TryGetValue(r[0], out v)) { v = new int[3]; g6[r[0]] = v; names.Add(r[0]); } if (k.Kind == "N1") v[0]++; else if (k.Kind == "N2") v[1]++; else v[2]++; }
			names.Sort(StringComparer.Ordinal);
			S.Append("PF>=1,25 / Stability>30 / Symmetry>30 / ops>=150 y >=5/mes (ET6)\tvigentes\tNO ajustable con estos archivos\tcombinaciones descartadas duras por PRIMERA puerta (N1/N2/INJ): ");
			if (names.Count == 0) S.Append("ninguna registrada"); else for (int i = 0; i < names.Count; i++) { int[] v = g6[names[i]]; S.Append(i > 0 ? "; " : "").Append(names[i]).Append('=').Append(I(v[0])).Append('/').Append(I(v[1])).Append('/').Append(I(v[2])); }
			S.Append(" (ET6_TRIALS.tsv trae pf/stability/symmetry/trades por combinacion; no se barre aqui porque el runner no marca cuales eran aceptables)\n");
			S.Append("corte de N_eff (0,40) y parametros neff_sample_*\t0,40 ; 0,90 ; 1000\tNO ajustable por estos datos\tse mide en la seccion 9 (requiere re-agrupar series)\n");
			S.Append("parametros geneticos (D-019), edge de referencia (D-020)\tD-019/D-020\tNO ajustables con estos datos\trequieren re-correr ET3 / curva de potencia\n");
		}

		private static void DsrRows(StringBuilder S, List<RCamp> nulls, List<RCamp> injs, NQCET2_ReportParams p)
		{
			S.Append("campana\ttipo\tportafolio\tSR_diario\tDSR_usado\tDSR@N_eff_inf\tDSR@N_eff_punto\tDSR@N_eff_sup\n"); int reach = 0, injReach = 0, nullReach = 0, injTot = 0, nullTot = 0;
			Action<List<RCamp>, bool> go = delegate(List<RCamp> set, bool isInj)
			{
				foreach (RCamp k in set)
				{
					if (k.Ports.Count == 0) continue; if (isInj) injTot++; else nullTot++;
					RPort best = null; foreach (RPort q in k.Ports) if (best == null || (!double.IsNaN(q.Sr) && (double.IsNaN(best.Sr) || q.Sr > best.Sr))) best = q;
					string a = "NA", b = "NA", c = "NA"; bool okUp = false;
					bool can = best != null && !double.IsNaN(best.Sr) && !double.IsNaN(k.NEffUsed) && k.NEffUsed > 1 && best.Sr0 > 0 && best.T > 30;
					if (can)
					{
						double K = ExpMaxK(k.NEffUsed); double sd = best.Sr0 / K; double den = 1.0 - best.Skew * best.Sr + (best.Kurt - 1.0) / 4.0 * best.Sr * best.Sr;
						if (den > 0 && !double.IsNaN(den))
						{
							double sq = Math.Sqrt(best.T - 1.0); double[] ns = new double[] { k.NEffLo, k.NEffEst, k.NEffHi }; string[] o = new string[3];
							for (int i = 0; i < 3; i++) { if (double.IsNaN(ns[i]) || ns[i] <= 1) { o[i] = "NA"; continue; } double d = NormCdf((best.Sr - sd * ExpMaxK(ns[i])) * sq / Math.Sqrt(den)); o[i] = F(d, "0.0000"); if (i == 2 && d >= p.DsrMin) okUp = true; }
							a = o[0]; b = o[1]; c = o[2];
						}
					}
					else if (best != null && !double.IsNaN(best.Sr) && best.T > 30 && !double.IsNaN(best.Sr0) && best.Sr0 <= 0) { a = "NA"; }
					if (okUp) { reach++; if (isInj) injReach++; else nullReach++; }
					S.Append(k.Id).Append('\t').Append(k.Kind).Append('\t').Append(best == null ? "NA" : best.Id12).Append('\t').Append(best == null ? "NA" : F(best.Sr, "0.0000")).Append('\t').Append(best == null ? "NA" : F(best.Dsr, "0.0000")).Append('\t').Append(a).Append('\t').Append(b).Append('\t').Append(c).Append('\n');
				}
			};
			go(nulls, false); go(injs, true);
			S.Append("DSR >= ").Append(F(p.DsrMin, "0.##")).Append(" con el N_eff SUPERIOR (el de SAMPLE_CHAO1/UPPER): inyectadas ").Append(I(injReach)).Append('/').Append(I(injTot)).Append(" (que llegan a ET7), nulas ").Append(I(nullReach)).Append('/').Append(I(nullTot)).Append(". Si es 0 en los inyectados el DSR NO es alcanzable con el edge de referencia (+US$20) y el IC superior del N_eff.\n");
			S.Append("(NA = no recalculable: N_eff en falla cerrada, SR0 = 0 o sin portafolios; DSR_usado es el del informe de ET7)\n");
		}
	}
}
