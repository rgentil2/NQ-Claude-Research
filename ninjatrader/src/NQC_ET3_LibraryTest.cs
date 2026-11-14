#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

//==============================================================================
// NQC_ET3_LibraryTest - Prueba TECNICA de la biblioteca D-018 (NQC_ET3_Library/LibCore/LibMath/LibFeatures).
// Contrato NQC 2.0.0. Estrategia SIN ordenes. Usa SOLO barras sinteticas (no mercado real) y NO calcula
// ni reporta resultados economicos. Requiere NQC_ET1_Core.cs, NQC_ET1C_AccountSim.cs y NQC_ET3_Genome.cs.
//
//   L1  Conteo exacto: 120 familias x 11 reglas = 1.320 condiciones; nombres distintos; auditoria codificada
//       (clases R5 A53/B44/E4/C17/D2; 24 familias con volumen); version de la biblioteca estable.
//   L2  Tabla de verdad de las 11 reglas con valores calculados a mano (NQCE3_Rules.Resolve).
//   L3  Valores de familias sobre una rampa sintetica (O=C, H=C+1, L=C-1, C=20000+k) calculados a mano:
//       familias 0,4,6,7,24,26,36,80,81,115,116,117,118 y su espejo bajista; reglas de la familia 0.
//   L4  Sin futuro: para las 1.320 condiciones x periodos {4,12,30} x 8 barras de senal, el resultado con la
//       serie completa == serie truncada en la barra de senal == serie con las barras futuras reemplazadas.
//   L5  Memoria == calculo directo; sin volumen las 24 familias con volumen no dan senal (264 condiciones).
//   L6  Calentamiento: SameDay rechaza lookback que cruza al dia anterior; AnyHistory lo acepta; sin historia suficiente no hay senal.
//   L7  Eval(NQCE_Ctx) coincide con Direction para dir +1 y -1.
// Salida: <Documentos>\NinjaTrader 8\NQC_Results\ET3LIB_<fecha_hora>\LIBRARY_TEST_REPORT.txt y SUMMARY.txt
// Strategy Analyzer: cualquier instrumento y rango (basta una barra); la prueba no mira los datos de NT8.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public class NQC_ET3_LibraryTest : Strategy
	{
		private const string ScriptVersion = "ET3LIBTEST-1.0.0";
		private bool started, done;
		private StringBuilder rep = new StringBuilder();
		private int pass, fail;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Prueba tecnica sintetica de la biblioteca de condiciones D-018 (sin ordenes).";
				Name = "NQC_ET3_LibraryTest";
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
					Print("NQC_ET3LIB_ERROR " + ex.Message + " | " + ex.StackTrace);
					try
					{
						string d = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results");
						Directory.CreateDirectory(d);
						File.WriteAllText(Path.Combine(d, "ET3LIB_ERROR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt"), ex.ToString());
					}
					catch { }
				}
			}
		}

		protected override void OnBarUpdate() { }

		private static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }
		private static string F(double v) { return v.ToString("0.000000", CultureInfo.InvariantCulture); }
		private void Chk(string name, bool ok, string info)
		{
			if (ok) pass++; else fail++;
			rep.Append(ok ? "[PASS] " : "[FALLA] ").Append(name).Append(" | ").Append(info).Append("\n");
		}

		// ---------------------------------------------------------------- barras sinteticas
		private static readonly long Start = new DateTime(2022, 1, 3, 0, 0, 0).Ticks;
		private static NQCE_Bars Make(double[] o, double[] h, double[] l, double[] c, int count)
		{
			long[] t = new long[count]; double[] oo = new double[count], hh = new double[count], ll = new double[count], cc = new double[count];
			for (int i = 0; i < count; i++) { t[i] = Start + i * 600000000L; oo[i] = o[i]; hh[i] = h[i]; ll[i] = l[i]; cc[i] = c[i]; }
			return new NQCE_Bars(t, oo, hh, ll, cc);
		}
		private static double[] Cut(double[] a, int count) { double[] r = new double[count]; Array.Copy(a, r, count); return r; }

		private static void Ramp(int n, double sign, out double[] o, out double[] h, out double[] l, out double[] c, out double[] v)
		{
			o = new double[n]; h = new double[n]; l = new double[n]; c = new double[n]; v = new double[n];
			for (int k = 0; k < n; k++) { c[k] = sign > 0 ? 20000.0 + k : 30000.0 - k; o[k] = c[k]; h[k] = c[k] + 1; l[k] = c[k] - 1; v[k] = 1000; }
		}

		// Paseo aleatorio determinista con volumen (valido: l <= o,c <= h).
		private static void Walk(long seed, int n, int from, double[] o, double[] h, double[] l, double[] c, double[] v)
		{
			NQCE_Rng r = new NQCE_Rng(seed);
			double prev = from == 0 ? 20000.0 : c[from - 1];
			for (int k = from; k < n; k++)
			{
				o[k] = prev; c[k] = prev + (r.NextDouble() - 0.5) * 8.0;
				h[k] = Math.Max(o[k], c[k]) + r.NextDouble() * 2.0; l[k] = Math.Min(o[k], c[k]) - r.NextDouble() * 2.0;
				v[k] = 500.0 + r.NextDouble() * 1500.0; prev = c[k];
			}
		}

		private void RunAll()
		{
			rep.Append("NQC ET3 LibraryTest ").Append(ScriptVersion).Append(" | contrato ").Append(NQCE_Const.ContractVersion).Append(" | SOLO barras sinteticas\n\n");
			NQCE3_Library lib = new NQCE3_Library();

			// ---- L1
			{
				HashSet<string> names = new HashSet<string>();
				for (int id = 1; id <= lib.ConditionCount; id++) names.Add(NQCE3_Catalog.CondName(id));
				Chk("L1.1 conteo 120 x 11 = 1320", NQCE3_Catalog.Families == 120 && NQCE3_Rules.Count == 11 && lib.ConditionCount == 1320 && NQCE3_Catalog.FamilyNames.Length == 120 && NQCE3_Rules.Names.Length == 11, "condiciones=" + I(lib.ConditionCount));
				Chk("L1.2 1320 nombres distintos", names.Count == 1320, "distintos=" + I(names.Count));
				int a = 0, b = 0, c = 0, d = 0, e = 0, vol = 0;
				for (int f = 0; f < 120; f++)
				{
					char u = NQCE3_Catalog.UnitOf(f);
					if (u == 'A') a++; else if (u == 'B') b++; else if (u == 'C') c++; else if (u == 'D') d++; else if (u == 'E') e++;
					if (NQCE3_Catalog.NeedsVolume(f)) vol++;
				}
				Chk("L1.3 auditoria codificada A53 B44 E4 C17 D2, volumen 24", NQCE3_Catalog.UnitClass.Length == 120 && NQCE3_Catalog.VolumeDep.Length == 120 && a == 53 && b == 44 && e == 4 && c == 17 && d == 2 && vol == 24,
					"A=" + I(a) + " B=" + I(b) + " E=" + I(e) + " C=" + I(c) + " D=" + I(d) + " volumen=" + I(vol));
				NQCE3_Library lib2 = new NQCE3_Library();
				Chk("L1.4 version estable", lib.Version == lib2.Version && lib.Version.StartsWith("NQCL-1.0.0:"), lib.Version);
				Chk("L1.5 niveles de parametro 27 (periodo 4..30)", lib.ParamLevels(1) == 27 && lib.ParamLevels(1320) == 27, "niveles=" + I(lib.ParamLevels(7)));
			}

			// ---- L2 tabla de verdad (x,y,z,regla -> esperado), calculada a mano
			{
				double[][] cases = new double[][] {
					new double[] { 0.5, 0.2, 0.1, 0, 1 }, new double[] { -0.5, 0.2, 0.1, 0, -1 }, new double[] { 0, 0, 0, 0, 0 },
					new double[] { 0.3, 0.2, 0, 1, 1 }, new double[] { 0.1, 0.2, 0, 1, -1 }, new double[] { 0.2, 0.2, 0, 1, 0 },
					new double[] { 0.1, -0.1, 0, 2, 1 }, new double[] { 0.1, 0.1, 0, 2, 0 }, new double[] { -0.1, 0.1, 0, 2, -1 }, new double[] { 0, 0, 0, 2, 0 },
					new double[] { 0.2, 0, 0, 3, 1 }, new double[] { 0.15, 0, 0, 3, 0 }, new double[] { -0.2, 0, 0, 3, -1 },
					new double[] { 0.36, 0, 0, 4, 1 }, new double[] { 0.35, 0, 0, 4, 0 }, new double[] { -0.36, 0, 0, 4, -1 },
					new double[] { 0.61, 0, 0, 5, 1 }, new double[] { 0.6, 0, 0, 5, 0 }, new double[] { -0.7, 0, 0, 5, -1 },
					new double[] { 0.2, 0.1, 0, 6, 1 }, new double[] { 0.2, 0.2, 0, 6, 0 }, new double[] { -0.2, -0.1, 0, 6, -1 },
					new double[] { 0.4, 0.3, 0, 7, 1 }, new double[] { 0.4, 0.4, 0, 7, 0 }, new double[] { -0.4, -0.3, 0, 7, -1 },
					new double[] { -0.5, -0.7, 0, 8, 1 }, new double[] { 0.5, 0.7, 0, 8, -1 }, new double[] { 0.5, 0.5, 0, 8, 0 },
					new double[] { 0.3, 0.1, 0, 9, 1 }, new double[] { 0.0, 0.1, 0.1, 9, -1 }, new double[] { 0.2, 0.1, 0, 9, 0 },
					new double[] { 0.1, 0.1, 0, 10, 1 }, new double[] { -0.1, -0.1, 0, 10, -1 }, new double[] { 0.1, -0.1, 0, 10, 0 },
					new double[] { double.NaN, 0, 0, 0, -2 }, new double[] { 0, 0, 0, 11, -2 } };
				int bad = 0; string first = "";
				for (int k = 0; k < cases.Length; k++)
				{
					int r = NQCE3_Rules.Resolve(cases[k][0], cases[k][1], cases[k][2], (int)cases[k][3]);
					if (r != (int)cases[k][4]) { bad++; if (first.Length == 0) first = "caso " + I(k) + " obtenido " + I(r); }
				}
				Chk("L2 tabla de verdad de las 11 reglas (" + I(cases.Length) + " casos a mano)", bad == 0, "malos=" + I(bad) + " " + first);
			}

			// ---- L3 rampa
			{
				double[] o, h, l, c, v; Ramp(400, 1.0, out o, out h, out l, out c, out v);
				NQCE_Bars bars = Make(o, h, l, c, 400); NQCE3_Series s = new NQCE3_Series(bars, v); int at = 300;
				// (familia, n, esperado)
				double[][] exp = new double[][] {
					new double[] { 0, 5, 1.0 }, new double[] { 4, 5, 1.0 }, new double[] { 6, 4, 1.0 }, new double[] { 7, 5, 2.0 / 3.0 }, new double[] { 24, 5, 0.8 },
					new double[] { 26, 5, 1.0 }, new double[] { 36, 5, 0.75 }, new double[] { 80, 5, 0.0 }, new double[] { 81, 5, 1.0 }, new double[] { 115, 5, 0.0 },
					new double[] { 116, 5, 0.0 }, new double[] { 117, 5, 0.0 }, new double[] { 118, 5, 1.0 } };
				int bad = 0; string first = "";
				for (int k = 0; k < exp.Length; k++)
				{
					double x = s.Feature((int)exp[k][0], (int)exp[k][1], at);
					if (double.IsNaN(x) || Math.Abs(x - exp[k][2]) > 1e-9) { bad++; if (first.Length == 0) first = "familia " + I((int)exp[k][0]) + " obtenido " + F(x) + " esperado " + F(exp[k][2]); }
				}
				Chk("L3.1 valores de 13 familias sobre rampa alcista (a mano)", bad == 0, "malos=" + I(bad) + " " + first);
				int[] er = new int[] { 1, 0, 0, 1, 1, 1, 0, 0, 0, 0, 1 };   // familia 0, n=5: x=y=z=1
				int badR = 0;
				for (int r = 0; r < 11; r++) if (NQCE3_Cond.Direction(s, 0, r, 5, at, NQCE3_Library.WarmAnyHistory) != er[r]) badR++;
				Chk("L3.2 11 reglas de la familia 0 (n=5) sobre la rampa (a mano)", badR == 0, "malas=" + I(badR));
				double[] o2, h2, l2, c2, v2; Ramp(400, -1.0, out o2, out h2, out l2, out c2, out v2);
				NQCE3_Series s2 = new NQCE3_Series(Make(o2, h2, l2, c2, 400), v2);
				bool m = Math.Abs(s2.Feature(0, 5, at) + 1.0) < 1e-9 && Math.Abs(s2.Feature(4, 5, at) + 1.0) < 1e-9 && Math.Abs(s2.Feature(7, 5, at) + 2.0 / 3.0) < 1e-9 && Math.Abs(s2.Feature(118, 5, at) + 1.0) < 1e-9
					&& NQCE3_Cond.Direction(s2, 0, 0, 5, at, NQCE3_Library.WarmAnyHistory) == -1 && NQCE3_Cond.Direction(s2, 0, 5, 5, at, NQCE3_Library.WarmAnyHistory) == -1;
				Chk("L3.3 espejo bajista (familias 0,4,7,118 y reglas 0 y 5)", m, "f0=" + F(s2.Feature(0, 5, at)) + " f7=" + F(s2.Feature(7, 5, at)));
			}

			// ---- L4 sin futuro
			{
				int N = 2880; int[] ats = new int[] { 400, 590, 650, 700, 1500, 2012, 2040, 2100 }; int[] ns = new int[] { 4, 12, 30 };
				double[] o = new double[N], h = new double[N], l = new double[N], c = new double[N], v = new double[N];
				Walk(20261114L, N, 0, o, h, l, c, v);
				NQCE_Bars full = Make(o, h, l, c, N);
				int diff = 0, tot = 0, nonzero = 0; string first = "";
				for (int w = 0; w < ats.Length; w++)
				{
					int at = ats[w];
					double[] o3 = (double[])o.Clone(), h3 = (double[])h.Clone(), l3 = (double[])l.Clone(), c3 = (double[])c.Clone(), v3 = (double[])v.Clone();
					Walk(777L + w, N, at + 1, o3, h3, l3, c3, v3);   // futuro distinto (a partir de at+1)
					NQCE_Bars scr = Make(o3, h3, l3, c3, N);
					NQCE_Bars trn = Make(o, h, l, c, at + 1);
					NQCE3_Series sF = new NQCE3_Series(full, v), sS = new NQCE3_Series(scr, v3), sT = new NQCE3_Series(trn, Cut(v, at + 1));
					for (int ni = 0; ni < ns.Length; ni++)
						for (int id = 1; id <= 1320; id++)
						{
							int fam = NQCE3_Catalog.FamilyOf(id), rule = NQCE3_Catalog.RuleOf(id);
							int rf = NQCE3_Cond.Direction(sF, fam, rule, ns[ni], at, NQCE3_Library.WarmAnyHistory);
							int rs = NQCE3_Cond.Direction(sS, fam, rule, ns[ni], at, NQCE3_Library.WarmAnyHistory);
							int rt = NQCE3_Cond.Direction(sT, fam, rule, ns[ni], at, NQCE3_Library.WarmAnyHistory);
							tot++; if (rf == 1 || rf == -1) nonzero++;
							if (rf != rs || rf != rt) { diff++; if (first.Length == 0) first = "id=" + I(id) + " n=" + I(ns[ni]) + " at=" + I(at) + " " + I(rf) + "/" + I(rs) + "/" + I(rt); }
						}
				}
				Chk("L4 sin futuro: completa == truncada == futuro reemplazado", diff == 0, "comparaciones=" + I(tot) + " con_senal=" + I(nonzero) + " distintas=" + I(diff) + " " + first);
				Chk("L4b la muestra tiene senales (la prueba no es vacia)", nonzero > 1000, "con_senal=" + I(nonzero));

				// ---- L5 memoria == directo; sin volumen
				int memBad = 0;
				NQCE3_Series sM = new NQCE3_Series(full, v);
				int[] fs = new int[] { 0, 31, 44, 63, 69, 94, 99, 119 };
				for (int a = 0; a < fs.Length; a++)
					for (int w = 0; w < ats.Length; w++)
					{
						double m1 = sM.Feature(fs[a], 12, ats[w]), m2 = sM.Feature(fs[a], 12, ats[w]);
						double dd = NQCE3_Features.Compute(sM, ats[w], fs[a], 12);
						bool dn = double.IsNaN(dd) || double.IsInfinity(dd);
						if (!(dn ? (double.IsNaN(m1) && double.IsNaN(m2)) : (m1 == dd && m2 == dd))) memBad++;
					}
				Chk("L5.1 memoria == calculo directo (64 casos)", memBad == 0, "distintos=" + I(memBad));
				NQCE3_Series sN = new NQCE3_Series(full, null);
				int volOnly = 0, volFalse = 0, other = 0;
				for (int id = 1; id <= 1320; id++)
				{
					int fam = NQCE3_Catalog.FamilyOf(id); int r = NQCE3_Cond.Direction(sN, fam, NQCE3_Catalog.RuleOf(id), 12, 2040, NQCE3_Library.WarmAnyHistory);
					if (NQCE3_Catalog.NeedsVolume(fam)) { volOnly++; if (r == -2) volFalse++; } else if (r != -2) other++;
				}
				Chk("L5.2 sin volumen: 264 condiciones sin senal; el resto evalua", volOnly == 264 && volFalse == 264 && other > 900, "con_volumen=" + I(volOnly) + " sin_senal=" + I(volFalse) + " resto_evaluable=" + I(other));

				// ---- L6 calentamiento
				int atD2 = 1500;   // 01:00 del dia 2: DayStart=1440
				int rSame = NQCE3_Cond.Direction(sM, 0, 0, 10, atD2, NQCE3_Library.WarmSameDay);
				int rAny = NQCE3_Cond.Direction(sM, 0, 0, 10, atD2, NQCE3_Library.WarmAnyHistory);
				int rEarly = NQCE3_Cond.Direction(sM, 0, 0, 30, 100, NQCE3_Library.WarmAnyHistory);
				int rOk = NQCE3_Cond.Direction(sM, 0, 0, 10, 2040, NQCE3_Library.WarmSameDay);
				Chk("L6 calentamiento: SameDay=-2 al cruzar el dia, AnyHistory evalua, sin historia=-2, 10:00 evalua", sM.DayStart[atD2] == 1440 && rSame == -2 && rAny != -2 && rEarly == -2 && rOk != -2,
					"same=" + I(rSame) + " any=" + I(rAny) + " temprano=" + I(rEarly) + " 10:00=" + I(rOk) + " (requeridas n=10: " + I(NQCE3_Catalog.RequiredBars(10)) + ")");

				// ---- L7 Eval(NQCE_Ctx)
				NQCE3_Library lb = new NQCE3_Library(); lb.WarmupMode = NQCE3_Library.WarmAnyHistory; lb.Bind(full, v);
				int evBad = 0, evTot = 0;
				for (int w = 0; w < ats.Length; w++)
					for (int id = 1; id <= 1320; id += 7)
					{
						NQCE_Ctx cx = new NQCE_Ctx(); cx.B = full; cx.I = ats[w]; cx.Stamp = full.Stamp[ats[w]]; cx.DayIdx = new List<int>();
						int dr = NQCE3_Cond.Direction(lb.SeriesFor(full), NQCE3_Catalog.FamilyOf(id), NQCE3_Catalog.RuleOf(id), 9, ats[w], NQCE3_Library.WarmAnyHistory);
						bool up = lb.Create(id, 5, 14, 1).Eval(cx), dn = lb.Create(id, 5, 14, -1).Eval(cx);
						evTot++; if (up != (dr == 1) || dn != (dr == -1)) evBad++;
					}
				Chk("L7 Eval(NQCE_Ctx) == Direction para dir +1 y -1", evBad == 0 && evTot > 1000, "casos=" + I(evTot) + " distintos=" + I(evBad));
			}

			string status = fail == 0 ? "PASS" : "FALLA";
			rep.Append("\nchecks_ok=" + I(pass) + " checks_falla=" + I(fail) + " status=" + status + "\n");
			rep.Append("NOTA: prueba tecnica con datos sinteticos; no mide ni valida ningun resultado economico.\n");
			string text = rep.ToString();
			string outDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results", "ET3LIB_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
			Directory.CreateDirectory(outDir);
			UTF8Encoding enc = new UTF8Encoding(false);
			File.WriteAllText(Path.Combine(outDir, "LIBRARY_TEST_REPORT.txt"), text, enc);
			string hash = NQCE_Const.Sha256Text(text);
			StringBuilder su = new StringBuilder();
			su.Append("NQC ET3 LIBRARY TEST - SUMMARY\nscript_version : " + ScriptVersion + "\ncore_version   : " + NQCE_Const.CoreVersion + "\ncontract       : " + NQCE_Const.ContractVersion + "\n");
			su.Append("library        : " + lib.Version + "\nconditions     : " + I(lib.ConditionCount) + "\nchecks_ok      : " + I(pass) + "\nchecks_fail    : " + I(fail) + "\n");
			su.Append("data           : SINTETICA (sin mercado real)\nstatus         : " + status + "\nreport_sha256  : " + hash + "\n");
			File.WriteAllText(Path.Combine(outDir, "SUMMARY.txt"), su.ToString(), enc);
			Print("NQC_ET3LIB_END status=" + status + " ok=" + I(pass) + " falla=" + I(fail) + " sha=" + hash.Substring(0, 12) + " dir=" + outDir);
		}
	}
}
