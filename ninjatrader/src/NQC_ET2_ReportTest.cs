#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
#endregion

//==============================================================================
// NQC_ET2_ReportTest - Prueba TECNICA SINTETICA del generador del informe de calibracion de ET2 (NQCET2_ReportBuilder). SIN ordenes, SIN datos de mercado.
// Escribe en disco carpetas de lote SINTETICAS con el MISMO formato que escriben NQC_RUN_Batch y NQC_RUN_Funnel (copiado de una corrida real del runner de juguete:
// SUMMARY.txt del lote y de campana con bloques [ET3]..[ET7], ET3_DATABANK.tsv, ET4_REJECTS.tsv, ET6_REJECTIONS.tsv, ET6_FINALISTS.tsv, ET7_EVIDENCE_REPORT.txt,
// ET7_NEFF_SAMPLE_ESTIMATE.txt) con 30 campanas de resultado CONOCIDO y verifica FP, potencia, Clopper-Pearson (referencias calculadas aparte en Python con aritmetica
// exacta), el percentil recalculado, el barrido de umbral, el veredicto y los fallos limpios. Los valores son inventados: NO son resultados de mercado ni del embudo.
// Requiere NQC_ET2_Calibration.cs y NQC_ET2_ReportBuilder.cs (no depende del resto del motor).
//
// ESCENARIO 1 (como la Fase A: ET6 ppass_min=0 y SIN vector nulo => el runner falla cerrado el percentil; el informe lo recalcula leave-one-out):
//   20 nulas (A00-A09 N1, A10-A19 N2): solo A03 (N1) pasa TODO (t=4,5 = maximo del vector, P_pass 0,85/0,90); el resto cae en ET4/ET5/ET6, DSR (A02,A12), N_eff en
//   falla cerrada (A08,A18), MC (A05,A15) o percentil (A04,A07,A09,A13,A14,A17,A19; A04 con 18/19 = 94,7 < 95).  FP = 1/20 (N1 1/10, N2 0/10).
//   10 inyectadas (I20-I29): 8 con portafolio aprobado que contiene la regla o una equivalente (borde: cobertura 55% y 60% de operaciones en el par),
//   I28 aprobado pero SIN regla equivalente (cobertura 45%), I29 cerrada en ET5.  Potencia = 8/10 (cota superior sin equivalencia 9/10).
// ESCENARIO 2 (percentil registrado por el runner, nulo_ET2_n=20): 3 FP con P_pass 0,90/0,62/0,45 => barrido: tau propuesto 0,65; potencia 6/10 => gate no cumplido.
// ESCENARIO 3: carpeta con ERROR, SUMMARY ausente y SUMMARY corrupto.  ESCENARIO 4: carpetas invalidas, lote sin SUMMARY, archivo alterado, sin export de operaciones.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public class NQC_ET2_ReportTest : Strategy
	{
		private const string ScriptVersion = "ET2REPORTTEST-1.0.0";
		private bool started, done; private StringBuilder rep = new StringBuilder(); private int pass, fail;
		public string SummaryText = "";

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults) { Description = "Prueba sintetica del generador del informe de calibracion de ET2 (sin ordenes)."; Name = "NQC_ET2_ReportTest"; Calculate = Calculate.OnBarClose; BarsRequiredToTrade = 0; IsExitOnSessionCloseStrategy = false; TraceOrders = false; }
			else if (State == State.DataLoaded) started = true;
			else if (State == State.Terminated)
			{
				if (!started || done) return; done = true;
				try { RunAll(); }
				catch (Exception ex)
				{
					Print("NQC_ET2REPORTTEST_ERROR " + ex.Message + " | " + ex.StackTrace);
					try { string d = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results"); Directory.CreateDirectory(d); File.WriteAllText(Path.Combine(d, "ET2REPORTTEST_ERROR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt"), ex.ToString()); } catch { }
				}
			}
		}
		protected override void OnBarUpdate() { }

		private static readonly CultureInfo IC = CultureInfo.InvariantCulture;
		private static string I(long v) { return v.ToString(IC); }
		private static string R4(double v) { return double.IsNaN(v) ? "NaN" : v.ToString("0.0000", IC); }
		private void Chk(string name, bool ok, string info) { if (ok) pass++; else fail++; rep.Append(ok ? "[PASS] " : "[FALLA] ").Append(name).Append(" | ").Append(info).Append("\n"); }
		private static bool Near(double a, double b, double tol) { return Math.Abs(a - b) <= tol; }
		private static string Sha(string t) { return NQCET2_ReportBuilder.Sha256Text(t); }
		private static string Sha(byte[] b) { return NQCET2_ReportBuilder.Sha256Bytes(b); }

		// ============================================================ especificacion de las campanas sinteticas
		private sealed class PS { public string Tag; public double T, Ppass, Dsr = 0.97, Dd = 800, Boot = 0.92, Pct = double.NaN, Sr = 0.05; public bool McFail; public string[] Members = new string[0]; }
		private sealed class CS
		{
			public string Id, Tr; public string Close = ""; public double Best3 = 1.0; public bool NeffFail, Err, NoSummary, Garbage; public int Tseed, Gaseed; public List<PS> Ports = new List<PS>();
			public bool DsrFail { get { foreach (PS q in Ports) if (q.Dsr < 0.95) return true; return false; } }
		}
		private static string Hx(string s) { return Sha("fx:" + s); }
		private static string MemberId(string cid, string tag) { return Hx(cid + "|" + tag); }

		private static CS Mk(string id, string tr, int ti, int gi) { CS c = new CS(); c.Id = id; c.Tr = tr; c.Tseed = ti; c.Gaseed = gi; return c; }
		private static PS Pt(string tag, double t, double pp, string[] mem) { PS q = new PS(); q.Tag = tag; q.T = t; q.Ppass = pp; q.Members = mem; return q; }

		// recorded=true => escenario 2 (percentil registrado por el runner)
		private static List<CS> Specs(bool recorded)
		{
			List<CS> L = new List<CS>(); Func<double, double> pc = delegate(double v) { return recorded ? v : double.NaN; };
			for (int i = 0; i < 20; i++)
			{
				string id = "A" + i.ToString("00", IC); bool n1 = i < 10; CS c = Mk(id, n1 ? "N1" : "N2", (n1 ? 20262001 : 20263001) + (i % 10), 20271000 + i); int pf = i % 10; c.Best3 = 1.0 + 0.1 * i;
				string[] m = new string[] { "M1", "M2" };
				switch (pf)
				{
					case 0: c.Close = "ET4"; break;
					case 1: c.Close = "ET5"; break;
					case 2: { PS a = Pt("F1", 2.1, 0.7, m); a.Dsr = 0.40; a.Pct = pc(60); PS b = Pt("F2", 1.9, 0.6, m); b.Dsr = 0.35; b.Pct = pc(55); c.Ports.Add(a); c.Ports.Add(b); break; }
					case 3:
						if (i == 3) { PS a = Pt("F1", 4.5, 0.90, m); a.Pct = pc(100); PS b = Pt("F2", 4.0, 0.85, m); b.Pct = pc(97.5); c.Ports.Add(a); c.Ports.Add(b); }
						else { PS a = Pt("F1", 3.1, 0.8, m); a.Pct = pc(70); PS b = Pt("F2", 2.7, 0.7, m); b.Pct = pc(60); c.Ports.Add(a); c.Ports.Add(b); }
						break;
					case 4: { double t = i == 4 ? 2.8 : 3.3; PS a = Pt("F1", t, i == 14 ? 0.62 : 0.8, m); a.Pct = pc(i == 14 ? 96 : 80); PS b = Pt("F2", t - 0.4, 0.5, m); b.Pct = pc(60); c.Ports.Add(a); c.Ports.Add(b); break; }
					case 5: { PS a = Pt("F1", 3.0, 0.8, m); a.McFail = true; a.Dd = 2500; a.Pct = pc(70); PS b = Pt("F2", 2.6, 0.7, m); b.McFail = true; b.Dd = 2300; b.Pct = pc(60); c.Ports.Add(a); c.Ports.Add(b); break; }
					case 6: c.Close = "ET6"; break;
					case 7: { double t = i == 7 ? 3.4 : 3.2; PS a = Pt("F1", t, i == 7 ? 0.45 : 0.30, m); a.Pct = pc(i == 7 ? 95.5 : 70); PS b = Pt("F2", t - 0.4, 0.3, m); b.Pct = pc(60); c.Ports.Add(a); c.Ports.Add(b); break; }
					case 8: { c.NeffFail = true; PS a = Pt("F1", 2.5, 0.8, m); a.Pct = pc(60); a.Dsr = double.NaN; PS b = Pt("F2", 2.1, 0.7, m); b.Pct = pc(55); b.Dsr = double.NaN; c.Ports.Add(a); c.Ports.Add(b); break; }
					case 9: { PS a = Pt("F1", 2.9, 0.7, m); a.Pct = pc(65); PS b = Pt("F2", 2.5, 0.6, m); b.Pct = pc(60); c.Ports.Add(a); c.Ports.Add(b); break; }
				}
				L.Add(c);
			}
			double[] pp = new double[] { 0.95, 0.90, 0.85, 0.80, recorded ? 0.70 : 0.75, recorded ? 0.66 : 0.70, recorded ? 0.60 : 0.66, recorded ? 0.55 : 0.62 };
			for (int j = 0; j < 10; j++)
			{
				string id = "A" + (20 + j).ToString(IC); CS c = Mk(id, "N1INJ", 20264001 + j, 20271020 + j); c.Best3 = 2.0;
				if (j == 9) { c.Close = "ET5"; L.Add(c); continue; }
				string eq = "EQ" + j; PS a = Pt("F1", 3.6 + 0.08 * j, j == 8 ? 0.90 : pp[j], new string[] { eq, "DEC" }); a.Pct = pc(99); if (j == 0) a.Sr = 0.12;
				PS b = Pt("F2", 3.2 + 0.08 * j, 0.50, new string[] { "DEC", "DEC2" }); b.Pct = pc(90); c.Ports.Add(a); c.Ports.Add(b); L.Add(c);
			}
			return L;
		}

		// (en el par: 40 operaciones; miembros: n dentro del par + n fuera)
		private static int[] MemberProfile(string cid, string tag)
		{
			if (tag.StartsWith("EQ", StringComparison.Ordinal)) { if (cid == "A21") return new int[] { 22, 0 }; if (cid == "A22") return new int[] { 24, 16 }; if (cid == "A28") return new int[] { 18, 0 }; return new int[] { 30, 5 }; }
			return new int[] { 0, 10 };
		}
		private static string PairDay(int i) { return (20210100 + i).ToString(IC); }

		// ============================================================ escritura de la carpeta del lote (formato del runner)
		private static string Wr(string root, string rel, string text) { string full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(full)); byte[] b = new UTF8Encoding(false).GetBytes(text); File.WriteAllBytes(full, b); return Sha(b); }

		private static string StageBlock(string st, Dictionary<string, string> meta, Dictionary<string, string> files)
		{
			StringBuilder b = new StringBuilder(); b.Append("[").Append(st).Append("] input_hash=").Append(Hx("in" + st + meta.Count)).Append(" output_hash=").Append(Hx("out" + st + meta.Count)).Append('\n');
			List<string> keys = new List<string>(meta.Keys); keys.Sort(StringComparer.Ordinal); foreach (string k in keys) b.Append("  ").Append(k).Append('=').Append(meta[k]).Append('\n');
			if (files != null) { List<string> fk = new List<string>(files.Keys); fk.Sort(StringComparer.Ordinal); foreach (string f in fk) b.Append("  sha256 ").Append(f).Append(' ').Append(files[f]).Append('\n'); }
			return b.ToString();
		}

		private static void WriteCampaign(string root, CS c, bool recorded, bool withTrades, List<string> tradeNote)
		{
			string id = c.Id; if (c.Err) { Directory.CreateDirectory(Path.Combine(root, id)); Wr(root, id + "/ERROR.txt", "System.Exception: fallo sintetico de la campana " + id + "\n   en NQCRUN_Funnel.Run\n"); return; }
			if (c.NoSummary) { Directory.CreateDirectory(Path.Combine(root, id)); Wr(root, id + "/ET3/ET3_LEDGER.tsv", "trial_id\n"); return; }
			if (c.Garbage) { Wr(root, id + "/SUMMARY.txt", "\u0001\u0002 esto no es un SUMMARY ===\nstatus\n[ET9 basura\n"); return; }
			bool inj = c.Tr == "N1INJ"; string tr = c.Tr == "N1INJ" ? "N1+INJ" : c.Tr; bool closed4 = c.Close == "ET4", closed5 = c.Close == "ET5", closed6 = c.Close == "ET6"; bool toEt7 = c.Close.Length == 0;
			// ---- ET3
			StringBuilder db = new StringBuilder("rank\ttrial_id\tfitness_t\ttrades\tmean_net\tfingerprint\tgenes\tgenome\n");
			for (int k = 0; k < 20; k++) db.Append(I(k + 1)).Append('\t').Append(Hx(id + "g" + k)).Append('\t').Append((c.Best3 - 0.3 * k).ToString("R", IC)).Append("\t108\t10.5\t").Append(Hx(id + "fp" + k)).Append("\t0,0,1\tdir=1 atr=14 ksl=2.00 ktp=3.00 maxday=1 timex=-1 if R").Append(I(k)).Append('\n');
			Dictionary<string, string> f3 = new Dictionary<string, string>(); f3["ET3/ET3_DATABANK.tsv"] = Wr(root, id + "/ET3/ET3_DATABANK.tsv", db.ToString());
			Dictionary<string, string> m3 = new Dictionary<string, string>(); m3["best_fitness_t"] = c.Best3.ToString("R", IC); m3["campaign"] = id; m3["databank_valid"] = "20"; m3["fresh_evals"] = "10000"; m3["neff_population"] = "10000"; m3["neff_sample_flat"] = "0"; m3["neff_sample_k"] = "5000"; m3["neff_sample_n"] = "5000"; m3["neff_sample_seen"] = "9999"; m3["neff_sample_seed"] = I(c.Gaseed); m3["seed"] = I(c.Gaseed); m3["zero_trade_evals"] = "12";
			// ---- ET4
			int cand = 20, f1r = closed4 ? 20 : 14, pool = closed4 ? 0 : 6; StringBuilder rj = new StringBuilder("trial_id\tstatus\tcause\ttrades\tnet_1x\tnet_1_5x\n");
			for (int k = 0; k < f1r; k++) rj.Append(Hx(id + "r" + k)).Append("\tF1\tneto 1,5x = ").Append((-100.5 - 17.25 * k).ToString("0.00", IC)).Append(" <= 0 (").Append(I(100 + k)).Append(" operaciones)\t").Append(I(100 + k)).Append("\t-5\t-10\n");
			for (int k = 0; k < pool; k++) rj.Append(Hx(id + "p" + k)).Append("\tPASS\t\t120\t5\t3\n");
			Dictionary<string, string> f4 = new Dictionary<string, string>(); f4["ET4/ET4_REJECTS.tsv"] = Wr(root, id + "/ET4/ET4_REJECTS.tsv", rj.ToString());
			Dictionary<string, string> m4 = new Dictionary<string, string>(); m4["candidates"] = I(cand); m4["cumulative_trials"] = "11000"; m4["f1_rejected"] = I(f1r); m4["f2_rejected"] = "0"; m4["f3_rejected"] = "0"; m4["fresh_trials"] = "1000"; m4["pool"] = I(pool);
			// ---- ET5
			Dictionary<string, string> m5 = new Dictionary<string, string>(); bool robust0 = closed4 || closed5; m5["robust"] = robust0 ? "0" : "6"; m5["rejected"] = "0"; m5["pbo"] = robust0 ? "NaN" : "0.1"; m5["pbo_pass"] = robust0 ? "NO" : "SI"; m5["campaign_closed"] = robust0 ? "SI" : "NO"; m5["close_reason"] = robust0 ? "pool robusto vacio" : "";
			// ---- ET6
			Dictionary<string, string> f6 = null, m6 = null, m7 = null, f7 = null; string status;
			if (closed4 || closed5) status = "CAMPANIA_CERRADA_EN_ET5: pool robusto vacio";
			else if (closed6) status = "SIN_FINALISTAS_EN_ET6";
			else status = "COMPLETO";
			if (!closed4 && !closed5)
			{
				f6 = new Dictionary<string, string>(); m6 = new Dictionary<string, string>();
				StringBuilder rej = new StringBuilder("combo_id\tsize\tmembers\ttrades\tdescartada_dura\tprimera_puerta\tpuertas_falladas\n");
				for (int k = 0; k < 4; k++) rej.Append(Hx(id + "c" + k).Substring(0, 12)).Append("\t3\ta+b+c\t216\tSI\t").Append(k % 2 == 0 ? "Symmetry" : "PF").Append("\t").Append(k % 2 == 0 ? "Symmetry=0(>30)" : "PF=1.10(>=1.25)").Append('\n');
				f6["ET6/ET6_REJECTIONS.tsv"] = Wr(root, id + "/ET6/ET6_REJECTIONS.tsv", rej.ToString());
				StringBuilder fin = new StringBuilder("rank\tportfolio_id\tspec_hash\tt_stat\tppass\tmembers\n");
				for (int k = 0; k < c.Ports.Count; k++) { PS q = c.Ports[k]; List<string> ms = new List<string>(); foreach (string t in q.Members) ms.Add(MemberId(id, t)); fin.Append(I(k + 1)).Append('\t').Append(Hx(id + q.Tag)).Append('\t').Append(Hx("spec" + id + q.Tag)).Append('\t').Append(q.T.ToString("R", IC)).Append('\t').Append(q.Ppass.ToString("R", IC)).Append('\t').Append(string.Join(",", ms.ToArray())).Append('\n'); }
				f6["ET6/ET6_FINALISTS.tsv"] = Wr(root, id + "/ET6/ET6_FINALISTS.tsv", fin.ToString());
				m6["accepted"] = closed6 ? "0" : "25"; m6["hard_discarded"] = "4"; m6["pool_used"] = "6"; m6["finalists"] = I(c.Ports.Count); m6["trials"] = "56"; m6["trials_by_size"] = "6/15/20/10/5"; m6["cumulative_trials"] = "11056"; m6["executor_runs"] = "86";
			}
			double neffUsed = c.NeffFail ? double.NaN : 400, neffEst = 150, neffLo = 80, neffHi = 400, gt = c.NeffFail ? 0.70 : 0.95;
			if (toEt7)
			{
				f7 = new Dictionary<string, string>(); m7 = new Dictionary<string, string>(); StringBuilder ev = new StringBuilder(); int np = 0;
				string fail = c.NeffFail ? "cobertura Good-Turing de la muestra 0.7000 < minimo 0.9000" : "";
				ev.Append("ET7_EVIDENCE_REPORT (NQC 2.0.0)\nconfig_sha256=").Append(Hx("cfg")).Append("\ncommit=NA\n");
				for (int k = 0; k < c.Ports.Count; k++) ev.Append("spec_F").Append(I(k + 1)).Append("_sha256=").Append(Hx("spec" + id + c.Ports[k].Tag)).Append('\n');
				ev.Append("umbrales: dsr_min=0.95 null_pct_min=95 mc_dd_p95_max=2000 mc_mode=EXPLORATORY trayectorias=500 bloque=8 semilla=20261114 ppass_boot_min=solo_reporta\n");
				ev.Append("N_bruto=11056 series=5000 grupos=100 cobertura=0.4523 corte_distancia=0.4 N_eff=").Append(double.IsNaN(neffUsed) ? "NaN" : neffUsed.ToString("0.##", IC)).Append(" var_SR_diario=").Append(c.NeffFail ? "NaN" : "0.000400").Append(fail.Length > 0 ? " FALLA_CERRADA=" + fail : "").Append('\n');
				ev.Append("N_eff_muestra: metodo=SAMPLE_CHAO1 N_poblacion=11056 n_muestra=5000 observados=100 f1=30 f2=12 N_eff_estimado(Chao1)=150 IC95=[80,400] cobertura_GoodTuring=").Append(gt.ToString("0.0000", IC)).Append(" cobertura_min=0.9 limite_usado=UPPER N_eff_usado=").Append(double.IsNaN(neffUsed) ? "NaN" : "400").Append(" (Chao1 es limite inferior de la riqueza verdadera)\n");
				ev.Append("N_eff_planas: series_planas=3 fraccion_planas=0.0006 activas=4997 (las planas forman UN grupo)\n");
				ev.Append("nulo_ET2_n=").Append(recorded ? "20" : "0").Append('\n');
				foreach (PS q in c.Ports)
				{
					List<string> causes = new List<string>(); bool dsrBad = q.Dsr < 0.95 || double.IsNaN(q.Dsr);
					if (dsrBad) causes.Add(double.IsNaN(q.Dsr) ? "DSR: N_eff: " + fail : "DSR: DSR " + R4(q.Dsr) + " < 0.95");
					if (recorded) { if (q.Pct < 95) causes.Add("PERCENTIL_NULO: percentil " + q.Pct.ToString("0.0", IC) + " < 95.0"); } else causes.Add("PERCENTIL_NULO: distribucion nula con 0 valores (< 20)");
					if (q.McFail) causes.Add("MC_DD_P95: P95 DD " + q.Dd.ToString("0.00", IC) + " > 2000.00");
					bool ok = causes.Count == 0; if (ok) np++;
					ev.Append("--- portafolio ").Append(Hx(id + q.Tag).Substring(0, 12)).Append(" : ").Append(ok ? "PASA A ET8" : "RECHAZADO").Append('\n');
					ev.Append("  DSR=").Append(R4(q.Dsr)).Append(" SR_diario=").Append(R4(q.Sr)).Append(" SR_anual(252)=1.27 SR0=").Append(c.NeffFail ? "0.0000" : "0.0597").Append(" asimetria=0.100 curtosis=3.500 T=750\n");
					ev.Append("  percentil_nulo=").Append(recorded ? q.Pct.ToString("0.0", IC) : "NaN").Append('\n');
					ev.Append("  MC: trayectorias=500 ops=270 DD_P95=").Append(q.Dd.ToString("0.00", IC)).Append(" DD_mediana=300.00 DD_max=900.00 P_pass_bootstrap=").Append(R4(q.Boot)).Append(" (trayectorias con decididos=500)\n");
					foreach (string cz in causes) ev.Append("  causa_rechazo ").Append(cz).Append('\n');
				}
				ev.Append("RESUMEN: ").Append(I(np)).Append(" de ").Append(I(c.Ports.Count)).Append(" pasan. Estado: evidencia calculada por el codigo; solo vale con datos reales de ET6/ET2.\n");
				f7["ET7/ET7_EVIDENCE_REPORT.txt"] = Wr(root, id + "/ET7/ET7_EVIDENCE_REPORT.txt", ev.ToString());
				f7["ET7/ET7_NEFF_SAMPLE_ESTIMATE.txt"] = Wr(root, id + "/ET7/ET7_NEFF_SAMPLE_ESTIMATE.txt", "N_poblacion_usada=11056 (N bruto ET3+ET4+ET6) N_ET3=10000 N_muestra=5000 vistos_por_el_reservorio=9999 semilla_reservorio=" + I(c.Gaseed) + " series_planas=3\nestimador=Chao1_sesgo_corregido corte=0.4 estado=" + (c.NeffFail ? "COBERTURA_BAJA" : "OK") + "\nn_muestra=5000 n_poblacion=11056\ngrupos_observados=100 f1=30 f2=12\nn_eff_estimado=150 inferior=80 superior=400 cobertura_muestra=" + gt.ToString("0.0000", IC) + " var_chao=478\n");
				m7["portfolios"] = I(c.Ports.Count); m7["pass"] = I(np); m7["n_gross"] = "11056"; m7["n_series"] = "5000"; m7["clusters"] = "100"; m7["coverage"] = "0.4523"; m7["n_eff"] = double.IsNaN(neffUsed) ? "NaN" : "400"; m7["neff_fail"] = fail; m7["null_n"] = recorded ? "20" : "0"; m7["neff_flat_n"] = "3"; m7["neff_flat_frac"] = "0.0006"; m7["neff_method"] = "SAMPLE_CHAO1"; m7["report_sha256"] = Sha(ev.ToString());
			}
			// operaciones de miembros y del par (export que hoy no existe; en el escenario 5 no se escriben)
			if (inj && withTrades && toEt7)
			{
				StringBuilder pt = new StringBuilder("day\tdir\n"); for (int k = 0; k < 40; k++) pt.Append(PairDay(k)).Append('\t').Append(k % 2 == 0 ? "1" : "-1").Append('\n'); Wr(root, id + "/INJ_PAIR_TRADES.tsv", pt.ToString());
				StringBuilder mt = new StringBuilder("trial_id\tday\tdir\n"); HashSet<string> seen = new HashSet<string>();
				foreach (PS q in c.Ports) foreach (string t in q.Members) { if (!seen.Add(t)) continue; int[] pr = MemberProfile(id, t); string mid = MemberId(id, t); for (int k = 0; k < pr[0]; k++) mt.Append(mid).Append('\t').Append(PairDay(k)).Append('\t').Append(k % 2 == 0 ? "1" : "-1").Append('\n'); for (int k = 0; k < pr[1]; k++) mt.Append(mid).Append('\t').Append((20220100 + k).ToString(IC)).Append("\t1\n"); }
				Wr(root, id + "/ET6/ET6_MEMBER_TRADES.tsv", mt.ToString());
			}
			// ---- SUMMARY de la campana (formato del runner)
			StringBuilder s = new StringBuilder();
			s.Append("NQC RUN FUNNEL SUMMARY | SOLO IS 2020-01-01..2022-12-31 | el OOS no se toca | sin congelado salvo freeze=1 | SOLO DESARROLLO (no es evidencia hasta correr en NT8 con datos reales y ET2 aprobada)\n");
			s.Append("status=").Append(status).Append('\n');
			s.Append("runner_version=NQCRUN-1.3.0 contract=NQC 2.0.0 core=ET1CORE-1.0.0 executor_spec=2.3.0\nlibrary=FIXTURELIB-1 et4=ET4PRUNE-1.0.0 et6_schema=NQC_PORTFOLIO_SPEC/1 engine_hash=FIXTURE-ENGINE-HASH\n");
			s.Append("campaign=").Append(id).Append(" seed=").Append(I(c.Gaseed)).Append(" population=200 generations=50 max_evals=10000 top_k=200 et6_max_pool=30 et6_beam=10 et6_ppass_min=0 mc_paths=500 mc_mode=EXPLORATORY\n");
			s.Append("transform=").Append(tr).Append(" transform_params=seed=").Append(I(c.Tseed)).Append(inj ? ";fixed_days=0;slot0_fixed=1;min_level=1000;core=ET2DATASETS-1.0.0;target_net=20.00;tol=2.00;a=0.0850;measured_net=20.31;net_long=21.10;net_short=19.40;trades=312;rate_per_session=0.520;t_pair=2.60;ok=1;rules=dir=1 atr=14 ksl=2.00 ktp=3.00 maxday=1 timex=-1 if CIERRE>MAX(571..600)@>600 || dir=-1 atr=14 ksl=2.00 ktp=3.00 maxday=1 timex=-1 if CIERRE<MIN(571..600)@>600" : ";fixed_days=0;slot0_fixed=1;min_level=1000;core=ET2DATASETS-1.0.0").Append('\n');
			s.Append("data: bars=225504 sessions=750 first_day=20200103 last_day=20221229 warmup_coverage=1 r2_zone=750/750 (100%) strict_is_range=1\n");
			s.Append("data_raw_sha256=").Append(Hx("raw")).Append(" data_run_sha256=").Append(Hx("run" + id)).Append('\n').Append("params_sha256=").Append(Hx("par" + id)).Append(" run_input_sha256=").Append(Hx("ri" + id)).Append('\n');
			s.Append("ensayos: ET3=10000 ET4_vecinos_nuevos=").Append(closed4 ? "0" : "1000").Append(" ET5=0 ET6_combinaciones=").Append(closed4 || closed5 ? "0" : "56").Append(" ET7=0 acumulado=11056\n");
			s.Append(StageBlock("ET3", m3, f3)).Append(StageBlock("ET4", m4, f4)).Append(StageBlock("ET5", m5, null));
			if (m6 != null) s.Append(StageBlock("ET6", m6, f6)); if (m7 != null) s.Append(StageBlock("ET7", m7, f7));
			s.Append("deterministic_sha256=").Append(Hx("det" + id)).Append("\n--- no determinista (tiempos, memoria y reanudacion; no entra en deterministic_sha256) ---\nET3: seconds=100.00 mem_managed_mb=500 reanudada=NO\ntotal_seconds=180.00 mem_managed_mb=900 peak_working_set_mb=1500 memo_budget_mb=4096\n");
			Wr(root, id + "/SUMMARY.txt", s.ToString()); Wr(root, id + "/BATCH_ENTRY.txt", "input_hash=" + Hx("bi" + id) + "\nsummary_sha256=" + Sha(s.ToString()) + "\nstatus=" + status + "\ndigest=" + Hx("det" + id) + "\ntrials=ET3=10000 acum=11056\n");
		}

		private static void WriteBatch(string root, List<CS> specs, bool recorded, bool withTrades, bool writeBatchSummary)
		{
			Directory.CreateDirectory(root); List<string> note = new List<string>();
			foreach (CS c in specs) WriteCampaign(root, c, recorded, withTrades, note);
			Wr(root, "BATCH_LOG.txt", "00:00:01 lote sintetico\n");
			if (!writeBatchSummary) return;
			StringBuilder d = new StringBuilder();
			d.Append("NQC RUN BATCH SUMMARY | NQCRUNBATCH-1.1.0 | SOLO IS 2020-01-01..2022-12-31 | el OOS no se toca | sin congelado | SOLO DESARROLLO (no es evidencia hasta correr en NT8 con datos reales)\n");
			d.Append("batch=ET2_FASE_A status=COMPLETO campaigns=").Append(I(specs.Count)).Append(" runner=NQCRUN-1.3.0 library=FIXTURELIB-1\nmanifest_sha256=").Append(Hx("manifest")).Append(" target=20 tol=2\n");
			d.Append("data: bars=225504 sessions=750 first_day=20200103 last_day=20221229 r2_zone=750/750 data_raw_sha256=").Append(Hx("raw")).Append('\n').Append("id\tstate\ttransform\tsize\tga_seed\ttrials\tfunnel_status\tdigest\n");
			foreach (CS c in specs) { string st = c.Err ? "ERROR" : "OK"; d.Append(c.Id).Append('\t').Append(st).Append('\t').Append(c.Tr).Append("\tREDUCED\t").Append(I(c.Gaseed)).Append("\tET3=10000 acum=11056\t").Append(c.Close.Length == 0 ? "COMPLETO" : (c.Close == "ET6" ? "SIN_FINALISTAS_EN_ET6" : "CAMPANIA_CERRADA_EN_ET5")).Append('\t').Append(Hx("det" + c.Id)).Append('\n'); }
			d.Append("batch_digest_sha256=").Append(Hx("batchdigest")).Append("\n--- no determinista (tiempos, memoria, reanudacion y notas; no entran en batch_digest_sha256) ---\nid\tresumed\tseconds\tpeak_working_set_mb\tnota\n");
			foreach (CS c in specs) d.Append(c.Id).Append("\tNO\t180.00\t1500\t").Append(c.Tr == "N1INJ" ? "inj_ok=1" : "").Append('\n');
			d.Append("total_seconds=5400.00 peak_working_set_mb=1500\n");
			Wr(root, "SUMMARY.txt", d.ToString());
		}

		private static string Ln(string text, string startsWith) { foreach (string l in text.Split('\n')) if (l.StartsWith(startsWith, StringComparison.Ordinal)) return l; return ""; }
		private static string LnC(string text, string contains) { foreach (string l in text.Split('\n')) if (l.Contains(contains)) return l; return ""; }

		// ============================================================ pruebas
		private void RunAll()
		{
			rep.Append("NQC ET2 ReportTest ").Append(ScriptVersion).Append(" | generador ").Append(NQCET2_ReportBuilder.Version).Append(" | carpetas de lote SINTETICAS con el formato real del runner (valores inventados, NO son resultados de mercado ni del embudo)\n\n");
			string root = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results", "ET2REPORTTEST_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)); Directory.CreateDirectory(root);
			NQCET2_ReportParams P = new NQCET2_ReportParams(); P.GeneratedUtc = "2026-01-01T00:00:00Z"; P.Label = "fixture"; P.Commit = "TESTCOMMIT";

			// ---- R1 Clopper-Pearson contra referencias de Python (aritmetica exacta, biseccion independiente)
			{
				double lo, hi; bool ok = true; string why = "";
				double[][] refs = new double[][] { new double[] { 1, 20, 0.001265, 0.248733, 0.216106, 0.002561 }, new double[] { 1, 10, 0.002529, 0.445016, 0.394163, 0.005116 }, new double[] { 0, 10, 0, 0.308497, 0.258866, 0 }, new double[] { 8, 10, 0.443905, 0.974789, 0.963229, 0.493099 }, new double[] { 9, 10, 0.554984, 0.997471, 0.994884, 0.605837 }, new double[] { 0, 20, 0, 0.168433, 0.139108, 0 }, new double[] { 3, 17, 0.037985, 0.434318, 0.395641, 0.049898 } };
				foreach (double[] r in refs)
				{
					NQCET2_Stats.ClopperPearson((int)r[0], (int)r[1], 0.05, out lo, out hi); double up = NQCET2_Stats.UpperOneSided((int)r[0], (int)r[1], 0.05);
					if (!Near(lo, r[2], 2e-6) || !Near(hi, r[3], 2e-6) || !Near(up, r[4], 2e-6)) { ok = false; why += "[" + r[0] + "/" + r[1] + " lo=" + R4(lo) + " hi=" + R4(hi) + " up=" + R4(up) + "]"; }
				}
				Chk("R1 Clopper-Pearson (1/20, 1/10, 0/10, 8/10, 9/10, 0/20, 3/17) y cotas superiores unilaterales iguales a las de Python a 2e-6", ok, why.Length == 0 ? "ok" : why);
			}

			// ---- E1 escenario 1: 30 campanas, FP 1/20, potencia 8/10
			string d1 = Path.Combine(root, "E1_RUNBATCH_ET2_FASE_A"); WriteBatch(d1, Specs(false), false, true, true);
			NQCET2_ReportResult r1 = NQCET2_ReportBuilder.Build(d1, P); string t1 = r1.Text;
			Chk("E1.1 la carpeta de 30 campanas con el formato del runner se lee entera: 20 nulas (10 N1 + 10 N2), 10 inyectadas, 0 excluidas", r1.Ok && r1.NNull == 20 && r1.NN1 == 10 && r1.NN2 == 10 && r1.NInj == 10 && r1.Excluded == 0, r1.Ok ? "nulas=" + I(r1.NNull) + " inyectadas=" + I(r1.NInj) + " adv=" + I(r1.Warnings.Count) : r1.Error);
			Chk("E1.2 FALSOS POSITIVOS = 1/20 (N1 1/10, N2 0/10): solo A03 (t=4,5 = maximo del vector nulo, P_pass 0,90) pasa TODO; el resto cae en ET4/ET5/ET6/DSR/N_eff/MC/percentil", r1.KFp == 1 && r1.KFpN1 == 1 && r1.KFpN2 == 0 && t1.Contains("campanas nulas con FP: A03(P_pass=0.9,t=4.5)"), "KFp=" + I(r1.KFp) + " " + Ln(t1, "campanas nulas con FP:"));
			Chk("E1.3 POTENCIA = 8/10 con regla/equivalente (I28 aprobado pero con cobertura 45% NO es equivalente; I29 cerrada en ET5); cota superior sin equivalencia 9/10", r1.KPowerEquiv == 8 && r1.KPowerUpper == 9 && r1.NEquivEvaluable == 10 && t1.Contains("A28[SIN regla equivalente]") && t1.Contains("A20[contiene regla/equiv.]"), "equiv=" + I(r1.KPowerEquiv) + " cota=" + I(r1.KPowerUpper));
			Chk("E1.4 equivalencia en el borde (>=50% y >=50%): A21 (22 de 40 en el par = 55%, todas dentro) y A22 (24 de 40 = 60%, 60% de sus operaciones dentro) SI; A28 (18/40 = 45%) NO", t1.Contains("A21[contiene regla/equiv.]") && t1.Contains("A22[contiene regla/equiv.]") && !t1.Contains("A28[contiene"), "ok");
			Chk("E1.5 intervalos en el texto: FP 1/20 (5.0%) CP95=[0.1%,24.9%] cota_sup 21.6%; potencia 8/10 CP95=[44.4%,97.5%] cota_inf 49.3%; FP N2 0/10 CP95=[0.0%,30.8%] cota_sup 25.9%",
				t1.Contains("FP global (como corrio, tau=0): 1/20 (5.0%) CP95=[0.1%,24.9%] cota_sup_unilateral95=21.6%") && t1.Contains("POTENCIA con regla inyectada o equivalente (D-037): 8/10 (80.0%) CP95=[44.4%,97.5%]") && t1.Contains("cota_inf_unilateral95=49.3%") && t1.Contains("FP N2: 0/10 (0.0%) CP95=[0.0%,30.8%] cota_sup_unilateral95=25.9%"), Ln(t1, "FP global"));
			string dA04 = LnC(t1, "  A07\tN1\t"), dA03 = LnC(t1, "  A03\tN1\t");
			Chk("E1.6 percentil nulo RECALCULADO leave-one-out (el runner dejo nulo_ET2_n=0): A07 (t=3,4, solo A03 por encima: 18/19) = 94,7 < 95 no pasa; A03 (maximo) = 100,0 pasa; marca R", dA04.Contains("94.7R") && dA03.Contains("100.0R") && dA04.EndsWith("NO\tNE") && dA03.Contains("\tSI\tNE"), dA04 + " || " + dA03);
			Chk("E1.7 umbral de P_pass (regla de ET2, FP<=5%, piso 0,50): propuesto 0,50 con FP 1/20 y potencia 8/10; sin piso seria 0,00", Near(r1.Threshold, 0.50, 1e-9) && r1.KFpAtThreshold == 1 && r1.KPowerEquivAtThreshold == 8 && t1.Contains("umbral propuesto (ppass_min): 0.50 -> FP 1/20 (5.0%) ; potencia 8/10 (80.0%)") && t1.Contains("Sin el piso el menor tau factible seria 0.00"), "tau=" + R4(r1.Threshold));
			Chk("E1.8 VEREDICTO MECANICO: FP puntual 5% <= 5% y potencia 80% >= 70% => CUMPLIDO PUNTUALMENTE, NO DEMOSTRADO (cota sup del FP 21,6% > 5%; cota inf de la potencia 49,3% < 70%); no decide por el usuario", r1.Verdict.StartsWith("GATE CUMPLIDO PUNTUALMENTE; NO DEMOSTRADO") && r1.FpStatus == "CUMPLE_PUNTUAL_NO_DEMOSTRADO" && r1.PowerStatus == "CUMPLE_PUNTUAL_NO_DEMOSTRADO" && t1.Contains("hacen falta 59 campanas"), r1.Verdict);
			// embudo por puerta
			string rF3 = Ln(t1, "ET4 F3 (pool>0)\t"), rR = Ln(t1, "ET5 filtro de anios (robust>0)\t"), rFin = Ln(t1, "ET6 finalistas (>0)\t"), rDsr = Ln(t1, "ET7 DSR\t"), rPct = Ln(t1, "ET7 + percentil nulo"), rMc = Ln(t1, "ET7 + MC DD P95\t"), rAll = Ln(t1, "P_pass >= tau (TODO)\t");
			Chk("E1.9 embudo por puerta (nulas/N1/N2/inyectadas): pool>0 18/20; robust>0 16/20 (I29 cierra en ET5: 9/10); finalistas 14/20 y 8... 9/10; DSR 10/20; +percentil 1/20; +MC 1/20; TODO 1/20 y 8/10",
				rF3.StartsWith("ET4 F3 (pool>0)\t18/20\t9/10\t9/10\t10/10") && rR.StartsWith("ET5 filtro de anios (robust>0)\t16/20\t8/10\t8/10\t9/10") && rFin.StartsWith("ET6 finalistas (>0)\t14/20\t7/10\t7/10\t9/10") && rDsr.StartsWith("ET7 DSR\t10/20\t5/10\t5/10\t9/10") && rPct.Contains("\t1/20\t1/10\t0/10\t") && rMc.StartsWith("ET7 + MC DD P95\t1/20") && rAll.StartsWith("P_pass >= tau (TODO)\t1/20\t1/10\t0/10\t9/10"), rF3 + " || " + rR + " || " + rFin + " || " + rDsr + " || " + rAll);
			Chk("E1.10 motivo de rechazo mas frecuente SOLO entre las campanas que llegan a la puerta: ET4 F1 normalizado (numeros -> #) x2, ET5 «pool robusto vacio» x3 (A01, A11, I29), ET6 «puerta PF» x2", Ln(t1, "ET4 F1 (neto 1,5x)\t").Contains("F1: neto #x = # <= # (# operaciones) (x2)") && rR.Contains("pool robusto vacio (x3)") && Ln(t1, "ET6 combinaciones aceptadas (>0)\t").Contains("ET6 puerta PF (x2)"), Ln(t1, "ET4 F1 (neto 1,5x)\t"));
			// vector nulo
			Chk("E1.11 vector nulo D-036: 20 valores (t del mejor finalista que llega a ET7; el de ET3 si muere antes: 6 campanas = A00,A01,A06,A10,A11,A16), maximo 4,5; percentil del mejor inyectado I20 (t=3,6) = 95,0 y del I29 (cerrada, ET3 t=2,0) = 15,0",
				t1.Contains("n_nulos_con_valor=20 de 20 | llego a ET7 (mejor finalista)=14 | no llego a ET7: t del mejor genoma de ET3=6") && t1.Contains(", 4.5\n") && t1.Contains("A20 t=3.6 caso=ET7 percentil=95.0 (n=20)") && t1.Contains("A29 t=2 caso=ET3 percentil=15.0 (n=20)"), LnC(t1, "A20 t=") + " " + LnC(t1, "A29 t="));
			// N_eff y DSR (referencia Python: Sr=0.12 skew=0.1 kurt=3.5 T=750 N_eff usado 400, SR0=0.0597)
			string dsrRow = LnC(t1, "0.9741\t0.9660\t0.9508");
			Chk("E1.12 N_eff: DSR recalculado con N_eff 80 / 150 / 400 del IC (Python: 0,9741 / 0,9660 / 0,9508 con SR=0,12, T=750, SR0=0,0597 a N_eff=400); series planas, f1, f2 y cobertura Good-Turing en la tabla; falla cerrada de A08/A18",
				dsrRow.Contains("\t0.9741\t0.9660\t0.9508") && t1.Contains("A08\tN1\t5000\t100\t30\t12\t150\t80\t400\t0.7000\tNaN\t3\t0.0006\tcobertura Good-Turing") && t1.Contains("DSR >= 0.95 con el N_eff SUPERIOR (el de SAMPLE_CHAO1/UPPER): inyectadas 1/9"), dsrRow);
			// aceptacion inyectados y estados
			Chk("E1.13 aceptacion de inyectados (a, measured_net, t_pair, ok=1 en 10/10) y estado del embudo por tipo (INJ: COMPLETO x9, ET5 x1; N1: ET5 x2, COMPLETO x7, SIN_FINALISTAS_EN_ET6 x1 [estado real del runner])", t1.Contains("inyectados con ok=1: 10/10") && t1.Contains("  INJ: CAMPANIA_CERRADA_EN_ET5 (pool robusto vacio) x1 | COMPLETO x9") && t1.Contains("  N1: CAMPANIA_CERRADA_EN_ET5 (pool robusto vacio) x2 | COMPLETO x7 | SIN_FINALISTAS_EN_ET6 x1"), Ln(t1, "  N1:"));
			// barrido de otros umbrales
			Chk("E1.14 barrido de los umbrales barribles (DSR, percentil, MC DD, ppass_boot, ppass_min) y marca de «NO ajustable» para F2, F3, anios/PBO, PF/Stability/Symmetry/ops y corte de N_eff; DD y peor dia solo endurecibles", t1.Contains("DSR minimo\t0.95\tbarrible") && t1.Contains("percentil nulo minimo\t95\tbarrible") && t1.Contains("MC DD P95 maximo") && t1.Contains("ppass_min (P_pass de ET6; piso 0,50)") && t1.Contains("F2 (>=7/12 vecinos)\t>=7/12\tNO ajustable") && t1.Contains("Symmetry=") && t1.Contains("solo endurecibles, nunca aflojables"), "ok");
			// hashes y determinismo
			string body = t1.Substring(0, t1.LastIndexOf("report_sha256=", StringComparison.Ordinal)); string hl = Ln(t1, "report_sha256=");
			bool hok = hl.StartsWith("report_sha256=" + Sha(body)) && r1.ReportSha == Sha(body); string whyH = "";
			int nFiles = 0; foreach (string[] f in r1.Inputs) { nFiles++; string full = Path.Combine(d1, f[0].Replace('/', Path.DirectorySeparatorChar)); if (!File.Exists(full) || Sha(File.ReadAllBytes(full)) != f[2]) { hok = false; whyH += f[0] + " "; } }
			NQCET2_ReportResult r1b = NQCET2_ReportBuilder.Build(d1, P);
			Chk("E1.15 trazabilidad: sha256 del informe = sha256 del texto sin la ultima linea; cada archivo leido figura con su sha256 real (" + I(nFiles) + " archivos, el manifiesto tiene su hash); dos corridas dan el MISMO informe byte a byte", hok && r1b.Text == t1 && r1b.ReportSha == r1.ReportSha && r1.InputsSha.Length == 64 && nFiles > 150 && t1.Contains("inputs_manifest_sha256=" + r1.InputsSha), whyH.Length == 0 ? "informe=" + r1.ReportSha.Substring(0, 12) + " entradas=" + r1.InputsSha.Substring(0, 12) : whyH);
			Chk("E1.16 sin advertencias en un lote sano; el generador declara los sesgos (Fase A REDUCED, percentil leave-one-out n>=19) y los exports pendientes", r1.Warnings.Count == 0 && t1.Contains("percentil recalculado leave-one-out con n<20") && t1.Contains("ET6_MEMBER_TRADES.tsv e INJ_PAIR_TRADES.tsv"), "adv=" + I(r1.Warnings.Count));

			// ---- E2 escenario 2: percentil registrado por el runner; 3 FP; barrido de P_pass
			string d2 = Path.Combine(root, "E2_RUNBATCH_ET2_FASE_C"); WriteBatch(d2, Specs(true), true, true, true);
			NQCET2_ReportResult r2 = NQCET2_ReportBuilder.Build(d2, P); string t2 = r2.Text;
			Chk("E2.1 con vector del runner (nulo_ET2_n=20) se usa el veredicto REGISTRADO (pct E, no recalculado): FP como corrio (tau=0) = 3/20 (A03 0,90; A14 0,62; A07 0,45)", r2.Ok && r2.KFp == 3 && t2.Contains("A03(P_pass=0.9,t=4.5)") && t2.Contains("A14(P_pass=0.62,t=3.3)") && t2.Contains("A07(P_pass=0.45,t=3.4)") && LnC(t2, "  A04\tN1\t").Contains("80.0E"), "KFp=" + I(r2.KFp));
			Chk("E2.2 barrido de P_pass: tau 0,50 deja FP 2/20 = 10% > 5%; tau 0,65 deja 1/20 => umbral propuesto 0,65 (piso 0,50 no activo); potencia con regla a 0,65 = 6/10 (I26 0,60 e I27 0,55 caen)", Near(r2.Threshold, 0.65, 1e-9) && r2.KFpAtThreshold == 1 && r2.KPowerEquivAtThreshold == 6 && Ln(t2, "0.50\t").StartsWith("0.50\t2/20 (10.0%)") && Ln(t2, "0.65\t").StartsWith("0.65\t1/20 (5.0%)"), "tau=" + R4(r2.Threshold) + " pot=" + I(r2.KPowerEquivAtThreshold));
			Chk("E2.3 veredicto: FP puntual 5% cumple pero potencia 60% < 70% a ese umbral => GATE NO CUMPLIDO; el siguiente paso sugiere diagnosticar con la cascada y la curva de potencia +40/+60 sin decidir por el usuario", r2.Verdict == "GATE NO CUMPLIDO" && r2.PowerStatus == "NO_CUMPLE" && t2.Contains("CURVA DE POTENCIA a +40 y +60") && t2.Contains("decide el usuario"), r2.Verdict);

			// ---- E3 escenario 3: ERROR, SUMMARY ausente, SUMMARY corrupto (A05, A06, A07)
			string d3 = Path.Combine(root, "E3_RUNBATCH_ET2_INCOMPLETO"); List<CS> sp3 = Specs(false);
			foreach (CS c in sp3) { if (c.Id == "A05") c.Err = true; if (c.Id == "A06") c.NoSummary = true; if (c.Id == "A07") c.Garbage = true; }
			WriteBatch(d3, sp3, false, true, true); NQCET2_ReportResult r3 = NQCET2_ReportBuilder.Build(d3, P); string t3 = r3.Text;
			Chk("E3.1 campanas con ERROR (A05), sin SUMMARY (A06) y con SUMMARY corrupto (A07): NO rompen el generador; se EXCLUYEN de las tasas y se listan (n nulas 17)", r3.Ok && r3.Excluded == 3 && r3.NNull == 17 && r3.NInj == 10 && t3.Contains("campana excluida de las tasas: A05 (campana con ERROR:") && t3.Contains("campana excluida de las tasas: A06 (falta SUMMARY.txt de la campana)") && LnC(t3, "campana excluida de las tasas: A07").Length > 0, "excluidas=" + I(r3.Excluded) + " nulas=" + I(r3.NNull));
			Chk("E3.2 con 17 nulas el vector leave-one-out (16) queda bajo el minimo 19: el percentil NO es evaluable, el generador lo ADVIERTE y el veredicto de FP es INDETERMINADO (no dice «0 FP»)", r3.PctNotEvaluable && r3.KFp == 0 && r3.FpStatus == "NO_EVALUABLE" && r3.Verdict == "GATE INDETERMINADO" && t3.Contains("PERCENTIL NO EVALUABLE") && t3.Contains("campanas nulas validas = 17 (esperadas 20)"), r3.Verdict);
			NQCET2_ReportParams P3 = new NQCET2_ReportParams(); P3.GeneratedUtc = P.GeneratedUtc; P3.NullMinNOffline = 10; NQCET2_ReportResult r3b = NQCET2_ReportBuilder.Build(d3, P3); string t3b = r3b.Text;
			Chk("E3.3 relajando el minimo leave-one-out a 10 (parametro explicito): FP 1/17 con CP95=[0.1%,28.7%] cota_sup 25.0% (Python 0,001488 / 0,286889 / 0,250124); se avisa que faltan campanas (con 17 nulas el percentil de los inyectados cae a 94,1 y la potencia es 0: «NO CUMPLIDO», consecuencia correcta)", r3b.KFp == 1 && t3b.Contains("FP global (como corrio, tau=0): 1/17 (5.9%) CP95=[0.1%,28.7%] cota_sup_unilateral95=25.0%") && r3b.Warnings.Contains("campanas nulas validas = 17 (esperadas 20)"), Ln(t3b, "FP global") + " " + r3b.Verdict);

			// ---- E4 escenario 4: fallos limpios y deteccion de alteraciones
			NQCET2_ReportResult rn = NQCET2_ReportBuilder.Build(Path.Combine(root, "no_existe"), P), rnull = NQCET2_ReportBuilder.Build(null, P);
			string dEmpty = Path.Combine(root, "E4_vacia"); Directory.CreateDirectory(dEmpty); NQCET2_ReportResult re = NQCET2_ReportBuilder.Build(dEmpty, P);
			string dTxt = Path.Combine(root, "E4_otra_cosa"); Directory.CreateDirectory(Path.Combine(dTxt, "docs")); File.WriteAllText(Path.Combine(dTxt, "notas.txt"), "nada"); NQCET2_ReportResult rt = NQCET2_ReportBuilder.Build(dTxt, P);
			Chk("E4.1 fallos LIMPIOS (Ok=false, mensaje, sin excepcion, sin informe): carpeta inexistente, ruta nula, carpeta vacia y carpeta que no es un lote", !rn.Ok && rn.Error.Contains("no existe") && !rnull.Ok && !re.Ok && re.Error.Contains("no es un lote") && !rt.Ok && rn.Text.Length == 0 && re.Text.Length == 0, rn.Error + " | " + re.Error);
			string d4 = Path.Combine(root, "E4_sin_resumen_de_lote"); WriteBatch(d4, Specs(false), false, true, false); NQCET2_ReportResult r4 = NQCET2_ReportBuilder.Build(d4, P);
			Chk("E4.2 lote SIN SUMMARY.txt del lote (corrida interrumpida): se descubren las 30 campanas por carpeta, con advertencia LOTE INCOMPLETO y el mismo FP/potencia", r4.Ok && r4.NNull == 20 && r4.NInj == 10 && r4.KFp == 1 && r4.KPowerEquiv == 8 && r4.Text.Contains("LOTE INCOMPLETO") && r4.Text.Contains("batch_digest_sha256=NA"), "adv=" + I(r4.Warnings.Count));
			string d5 = Path.Combine(root, "E4_alterado"); WriteBatch(d5, Specs(false), false, true, true); File.AppendAllText(Path.Combine(d5, "A04", "ET7", "ET7_EVIDENCE_REPORT.txt"), "# alterado\n"); NQCET2_ReportResult r5 = NQCET2_ReportBuilder.Build(d5, P);
			Chk("E4.3 un archivo alterado despues de la corrida se DETECTA (su sha256 ya no coincide con el declarado en el SUMMARY de la campana) y el informe lo advierte; el hash de entradas cambia", r5.Ok && r5.Warnings.Exists(delegate(string w) { return w.StartsWith("A04: sha256 de ET7/ET7_EVIDENCE_REPORT.txt NO coincide", StringComparison.Ordinal); }) && r5.InputsSha != r1.InputsSha, "adv=" + (r5.Warnings.Count > 0 ? r5.Warnings[0] : "ninguna"));
			string d6 = Path.Combine(root, "E4_sin_export_de_operaciones"); WriteBatch(d6, Specs(false), false, false, true); NQCET2_ReportResult r6 = NQCET2_ReportBuilder.Build(d6, P); string t6 = r6.Text;
			Chk("E4.4 sin los exports de operaciones (como hoy): la potencia con equivalencia es NO EVALUABLE (KPowerEquiv=-1), se da la COTA SUPERIOR 9/10, se propone EXACTAMENTE que archivo falta y el veredicto es INDETERMINADO (no inventa)", r6.Ok && r6.KPowerEquiv == -1 && r6.KPowerUpper == 9 && r6.NEquivEvaluable == 1 && t6.Contains("NO EVALUABLE con los archivos disponibles (1/10") && t6.Contains("ET6_MEMBER_TRADES.tsv") && t6.Contains("INJ_PAIR_TRADES.tsv") && r6.PowerStatus.StartsWith("INDETERMINADA") && r6.Verdict == "GATE INDETERMINADO", r6.Verdict + " " + r6.PowerStatus);
			string d7 = Path.Combine(root, "E4_potencia_baja"); List<CS> sp7 = Specs(false); foreach (CS c in sp7) if (c.Tr == "N1INJ" && c.Close.Length == 0) { c.Close = "ET4"; }
			WriteBatch(d7, sp7, false, false, true); NQCET2_ReportResult r7 = NQCET2_ReportBuilder.Build(d7, P);
			Chk("E4.5 si ni siquiera la cota superior de la potencia llega al 70% (inyectadas muertas en ET4: 0/10) el veredicto es NO CUMPLIDO aun sin poder evaluar la equivalencia", r7.Ok && r7.KPowerUpper == 0 && r7.PowerStatus == "NO_CUMPLE" && r7.Verdict == "GATE NO CUMPLIDO", r7.Verdict);

			// ---- configuracion de la estrategia
			{
				string bd, err; NQCET2_ReportParams pp = NQC_ET2_Report.ParseConfig("# c\nbatch_dir=NQC_Results\\RUNBATCH_ET2_FASE_A\nlabel=x\ncommit=abc\nexpected_null=20\nmax_fp=0.05\nppass_floor=0.5\n", out bd, out err);
				string bd2, err2; NQCET2_ReportParams bad1 = NQC_ET2_Report.ParseConfig("label=x\n", out bd2, out err2); string bd3, err3; NQCET2_ReportParams bad2 = NQC_ET2_Report.ParseConfig("batch_dir=a\nfoo=1\n", out bd3, out err3);
				Chk("E5 configuracion de la estrategia (NQC_Results\\ET2_REPORT_CONFIG.txt): lee batch_dir/label/commit/umbrales; rechaza config sin batch_dir o con claves desconocidas", pp != null && bd == "NQC_Results\\RUNBATCH_ET2_FASE_A" && pp.Commit == "abc" && bad1 == null && err2.Contains("batch_dir") && bad2 == null && err3.Contains("foo"), err2 + " | " + err3);
			}

			string text = rep.ToString() + "\nresultado: " + pass + " PASS / " + fail + " FALLA\n";
			text += "\nHASHES\ninforme_E1=" + r1.ReportSha + "\nentradas_E1=" + r1.InputsSha + "\ninforme_E2=" + r2.ReportSha + "\ninforme_E3=" + r3.ReportSha + "\ninforme_E4_sin_export=" + r6.ReportSha + "\n";
			text += "\n---- INFORME E1 (muestra, secciones 3 y 11) ----\n" + Section(t1, "== 3.", "== 4.") + Section(t1, "== 11.", "== ADVERTENCIAS");
			string outDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results", "ET2REPORTTEST_OUT_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)); Directory.CreateDirectory(outDir); UTF8Encoding enc = new UTF8Encoding(false);
			File.WriteAllText(Path.Combine(outDir, "REPORT_TEST_REPORT.txt"), text, enc); File.WriteAllText(Path.Combine(outDir, "E1_ET2_CALIBRATION_REPORT.txt"), t1, enc);
			SummaryText = "NQC ET2 REPORT TEST - SUMMARY\nscript_version : " + ScriptVersion + "\nbuilder : " + NQCET2_ReportBuilder.Version + "\ncontract : " + NQCE_Const_ContractVersion + "\nchecks_ok : " + pass + "\nchecks_fail : " + fail + "\nreport_E1_sha256 : " + r1.ReportSha + "\nresult : " + (fail == 0 ? "PASS" : "FAIL") + "\n";
			File.WriteAllText(Path.Combine(outDir, "SUMMARY.txt"), SummaryText, enc);
			Print(text); Print(SummaryText);
		}
		private const string NQCE_Const_ContractVersion = "NQC 2.0.0";
		private static string Section(string t, string from, string to) { int a = t.IndexOf(from, StringComparison.Ordinal); if (a < 0) return ""; int b = t.IndexOf(to, a + 1, StringComparison.Ordinal); if (b < 0) b = t.Length; return t.Substring(a, b - a); }
	}
}
