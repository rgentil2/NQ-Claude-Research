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
//       (clases R5 tras D-018b: A100/B16/E4; 24 familias con volumen); version de la biblioteca estable.
//   L2  Tabla de verdad de las 11 reglas con valores calculados a mano (NQCE3_Rules.Resolve).
//   L3  Valores de familias sobre una rampa sintetica (O=C, H=C+1, L=C-1, C=20000+k) calculados a mano:
//       familias 0,4,6,7,24,26,36,80,81,115,116,117,118 y su espejo bajista; reglas de la familia 0.
//   L4  Sin futuro: para las 1.320 condiciones x periodos {4,12,30} x 8 barras de senal, el resultado con la
//       serie completa == serie truncada en la barra de senal == serie con las barras futuras reemplazadas.
//   L5  Memoria == calculo directo; sin volumen las 24 familias con volumen no dan senal (264 condiciones).
//   L6  Calentamiento: SameDay rechaza lookback que cruza al dia anterior; AnyHistory lo acepta; sin historia suficiente no hay senal.
//   L7  Eval(NQCE_Ctx) coincide con Direction para dir +1 y -1.
//   L8  D-018b: valores a mano de familias reexpresadas/corregidas a ATR (rampa) y espejo bajista; duplicados conocidos.
//   L9  D-030: rejilla de umbrales (45 combinaciones, bandas disjuntas), reglas con umbral a mano, Create/periodo+umbral,
//       empaquetado del gen, TrialId distingue umbral, RandomGenome/Mutate/Crossover siempre validos, espacio por ranura 145.800.
//   L11 D-031: ATR de Wilder (el de NQCE_Bars.Atr) con el periodo del gen de salida; clasificacion AtrKind verificada numericamente
//       (independiente / numerador/ATR / no descomponible), cache compartida entre periodos, duplicados eliminados == conservados.
//   L10 Volumen: NQCE3_Fitness exige volumen y M1 completas; sin volumen las 113-119 no lanzan excepcion; cache LRU respeta el presupuesto.
// Salida: <Documentos>\NinjaTrader 8\NQC_Results\ET3LIB_<fecha_hora>\LIBRARY_TEST_REPORT.txt y SUMMARY.txt
// Strategy Analyzer: cualquier instrumento y rango (basta una barra); la prueba no mira los datos de NT8.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public class NQC_ET3_LibraryTest : Strategy
	{
		private const string ScriptVersion = "ET3LIBTEST-3.1.0";
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

		private static bool Near(double a, double b)
		{
			bool an = double.IsNaN(a) || double.IsInfinity(a), bn = double.IsNaN(b) || double.IsInfinity(b);
			if (an || bn) return an && bn;
			return Math.Abs(a - b) <= 1e-9 * Math.Max(1.0, Math.Abs(b));
		}
		private static bool BitEq(double a, double b) { return BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b) || (double.IsNaN(a) && double.IsNaN(b)); }
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
				for (int k = 0; k < lib.ConditionCount; k++) names.Add(NQCE3_Catalog.CondName(lib.ConditionId(k)));
				Chk("L1.1 D-031(2): 120 familias en el espacio de ids, 7 eliminadas, 113 x 11 = 1243 condiciones activas", NQCE3_Catalog.Families == 120 && NQCE3_Catalog.ActiveFamilies == 113 && NQCE3_Rules.Count == 11 && lib.ConditionCount == 1243 && NQCE3_Catalog.IdSpace == 1320 && lib.ConditionId(0) == 1 && lib.ConditionId(1242) == 1320 && !lib.IsValidCondition(9 * 11 + 1) && lib.IsValidCondition(0 * 11 + 1) && NQCE3_Catalog.FamilyNames.Length == 120 && NQCE3_Rules.Names.Length == 11, "condiciones=" + I(lib.ConditionCount));
				Chk("L1.2 1243 nombres distintos", names.Count == 1243, "distintos=" + I(names.Count));
				int a = 0, b = 0, c = 0, d = 0, e = 0, vol = 0;
				for (int f = 0; f < 120; f++)
				{
					if (!NQCE3_Catalog.IsActiveFamily(f)) continue;
					char u = NQCE3_Catalog.UnitOf(f);
					if (u == 'A') a++; else if (u == 'X') b++; else if (u == 'C') c++; else if (u == 'D') d++; else if (u == 'E') e++;
					if (NQCE3_Catalog.NeedsVolume(f)) vol++;
				}
				Chk("L1.3 auditoria codificada (activas): A93, X20 (excepcion R5 D-031(1) + D-032), E0, C=D=0, volumen 21", NQCE3_Catalog.UnitClass.Length == 120 && NQCE3_Catalog.VolumeDep.Length == 120 && a == 93 && b == 20 && e == 0 && c == 0 && d == 0 && vol == 21,
					"A=" + I(a) + " X=" + I(b) + " E=" + I(e) + " C=" + I(c) + " D=" + I(d) + " volumen=" + I(vol));
				NQCE3_Library lib2 = new NQCE3_Library();
				Chk("L1.4 version estable", lib.Version == lib2.Version && lib.Version.StartsWith("NQCL-3.1.0:"), lib.Version);
				Chk("L1.5 niveles de periodo 27 (4..30) para todas las condiciones", lib.PeriodLevels(1) == 27 && lib.PeriodLevels(1320) == 27, "niveles=" + I(lib.PeriodLevels(7)));
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
					new double[] { 0, 5, 1.0 }, new double[] { 4, 5, 0.5 }, new double[] { 6, 4, 1.0 }, new double[] { 7, 5, 1.0 }, new double[] { 24, 5, 0.8 },
					new double[] { 26, 5, 2.5 }, new double[] { 36, 5, 0.75 }, new double[] { 80, 5, 0.0 }, new double[] { 81, 5, 1.0 }, new double[] { 115, 5, 0.0 },
					new double[] { 116, 5, 0.0 }, new double[] { 117, 5, 0.0 }, new double[] { 118, 5, 1.0 } };
				int bad = 0; string first = "";
				for (int k = 0; k < exp.Length; k++)
				{
					double x = s.Feature((int)exp[k][0], (int)exp[k][1], 14, at);
					if (double.IsNaN(x) || Math.Abs(x - exp[k][2]) > 1e-9) { bad++; if (first.Length == 0) first = "familia " + I((int)exp[k][0]) + " obtenido " + F(x) + " esperado " + F(exp[k][2]); }
				}
				Chk("L3.1 valores de 13 familias sobre rampa alcista (a mano)", bad == 0, "malos=" + I(bad) + " " + first);
				int[] er = new int[] { 1, 0, 0, 1, 1, 1, 0, 0, 0, 0, 1 };   // familia 0, n=5: x=y=z=1
				int badR = 0;
				for (int r = 0; r < 11; r++) if (NQCE3_Cond.Direction(s, 0, r, 5, 14, at, NQCE3_Library.WarmAnyHistory) != er[r]) badR++;
				Chk("L3.2 11 reglas de la familia 0 (n=5) sobre la rampa (a mano)", badR == 0, "malas=" + I(badR));
				double[] o2, h2, l2, c2, v2; Ramp(400, -1.0, out o2, out h2, out l2, out c2, out v2);
				NQCE3_Series s2 = new NQCE3_Series(Make(o2, h2, l2, c2, 400), v2);
				bool m = Math.Abs(s2.Feature(0, 5, 14, at) + 1.0) < 1e-9 && Math.Abs(s2.Feature(4, 5, 14, at) + 0.5) < 1e-9 && Math.Abs(s2.Feature(7, 5, 14, at) + 1.0) < 1e-9 && Math.Abs(s2.Feature(118, 5, 14, at) + 1.0) < 1e-9
					&& NQCE3_Cond.Direction(s2, 0, 0, 5, 14, at, NQCE3_Library.WarmAnyHistory) == -1 && NQCE3_Cond.Direction(s2, 0, 5, 5, 14, at, NQCE3_Library.WarmAnyHistory) == -1;
				Chk("L3.3 espejo bajista (familias 0,4,7,118 y reglas 0 y 5)", m, "f0=" + F(s2.Feature(0, 5, 14, at)) + " f7=" + F(s2.Feature(7, 5, 14, at)));
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
							if (!NQCE3_Catalog.IsActiveId(id)) continue;
							int fam = NQCE3_Catalog.FamilyOf(id), rule = NQCE3_Catalog.RuleOf(id);
							int rf = NQCE3_Cond.Direction(sF, fam, rule, ns[ni], 14, at, NQCE3_Library.WarmAnyHistory);
							int rs = NQCE3_Cond.Direction(sS, fam, rule, ns[ni], 14, at, NQCE3_Library.WarmAnyHistory);
							int rt = NQCE3_Cond.Direction(sT, fam, rule, ns[ni], 14, at, NQCE3_Library.WarmAnyHistory);
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
						double m1 = sM.Feature(fs[a], 12, 14, ats[w]), m2 = sM.Feature(fs[a], 12, 14, ats[w]);
						double dd = sM.Direct(fs[a], 12, 14, ats[w], false);
						bool dn = double.IsNaN(dd) || double.IsInfinity(dd);
						bool kd1 = NQCE3_Catalog.AtrKindOf(fs[a]) == '1';   // numerador/ATR: igual al directo a ~1e-12 relativo (no bit a bit)
						if (!(dn ? (double.IsNaN(m1) && double.IsNaN(m2)) : (kd1 ? (Math.Abs(m1 - dd) <= 1e-9 * Math.Max(1.0, Math.Abs(dd))) : (m1 == dd && m2 == dd)))) memBad++;
					}
				Chk("L5.1 memoria == calculo directo (64 casos)", memBad == 0, "distintos=" + I(memBad));
				NQCE3_Series sN = new NQCE3_Series(full, null);
				int volOnly = 0, volFalse = 0, other = 0;
				for (int id = 1; id <= 1320; id++)
				{
					if (!NQCE3_Catalog.IsActiveId(id)) continue;
					int fam = NQCE3_Catalog.FamilyOf(id); int r = NQCE3_Cond.Direction(sN, fam, NQCE3_Catalog.RuleOf(id), 12, 14, 2040, NQCE3_Library.WarmAnyHistory);
					if (NQCE3_Catalog.NeedsVolume(fam)) { volOnly++; if (r == -2) volFalse++; } else if (r != -2) other++;
				}
				Chk("L5.2 sin volumen: 231 condiciones sin senal (21 familias); el resto evalua", volOnly == 231 && volFalse == 231 && other > 900, "con_volumen=" + I(volOnly) + " sin_senal=" + I(volFalse) + " resto_evaluable=" + I(other));

				// ---- L6 calentamiento
				int atD2 = 1500;   // 01:00 del dia 2: DayStart=1440
				int rSame = NQCE3_Cond.Direction(sM, 0, 0, 10, 14, atD2, NQCE3_Library.WarmSameDay);
				int rAny = NQCE3_Cond.Direction(sM, 0, 0, 10, 14, atD2, NQCE3_Library.WarmAnyHistory);
				int rEarly = NQCE3_Cond.Direction(sM, 0, 0, 30, 14, 100, NQCE3_Library.WarmAnyHistory);
				int rOk = NQCE3_Cond.Direction(sM, 0, 0, 10, 14, 2040, NQCE3_Library.WarmSameDay);
				Chk("L6 calentamiento: SameDay=-2 al cruzar el dia, AnyHistory evalua, sin historia=-2, 10:00 evalua", sM.DayStart[atD2] == 1440 && rSame == -2 && rAny != -2 && rEarly == -2 && rOk != -2,
					"same=" + I(rSame) + " any=" + I(rAny) + " temprano=" + I(rEarly) + " 10:00=" + I(rOk) + " (requeridas n=10: " + I(NQCE3_Catalog.RequiredBars(10)) + ")");

				// ---- L7 Eval(NQCE_Ctx)
				NQCE3_Library lb = new NQCE3_Library(); lb.WarmupMode = NQCE3_Library.WarmAnyHistory; lb.Bind(full, v);
				int evBad = 0, evTot = 0;
				for (int w = 0; w < ats.Length; w++)
					for (int id = 1; id <= 1320; id += 7)
					{
						if (!NQCE3_Catalog.IsActiveId(id)) continue;
						NQCE_Ctx cx = new NQCE_Ctx(); cx.B = full; cx.I = ats[w]; cx.Stamp = full.Stamp[ats[w]]; cx.DayIdx = new List<int>();
						int dr = NQCE3_Cond.Direction(lb.SeriesFor(full), NQCE3_Catalog.FamilyOf(id), NQCE3_Catalog.RuleOf(id), 9, 14, ats[w], NQCE3_Library.WarmAnyHistory);
						int tl = lb.ThresholdLevels(id) > 1 ? 2 : 0; double thv = tl > 0 ? NQCE3_Rules.ThresholdValue(NQCE3_Catalog.RuleOf(id), tl) : double.NaN;
						int dr2 = NQCE3_Cond.Direction(lb.SeriesFor(full), NQCE3_Catalog.FamilyOf(id), NQCE3_Catalog.RuleOf(id), 9, 14, tl > 0 ? thv : NQCE3_Rules.DefaultThreshold(NQCE3_Catalog.RuleOf(id)), ats[w], NQCE3_Library.WarmAnyHistory);
						bool up = lb.Create(id, 5, tl, 14, 1).Eval(cx), dn = lb.Create(id, 5, tl, 14, -1).Eval(cx);
						dr = dr2;
						evTot++; if (up != (dr == 1) || dn != (dr == -1)) evBad++;
					}
				Chk("L7 Eval(NQCE_Ctx) == Direction para dir +1 y -1", evBad == 0 && evTot > 1000, "casos=" + I(evTot) + " distintos=" + I(evBad));
			}


			// ---- L8 D-018b: valores a mano sobre la rampa alcista (O=C, H=C+1, L=C-1, V=1000; ATR simple = 2; n=5, mitad=2)
			{
				double[] o, h, l, c, v; Ramp(400, 1.0, out o, out h, out l, out c, out v);
				NQCE3_Series s = new NQCE3_Series(Make(o, h, l, c, 400), v); int at = 300;
				double yz = Math.Sqrt((1 - 0.34 / (1.34 + 6.0 / 4.0)) * 2.0) / 2.0;
				double[][] exp = new double[][] {
					new double[] { 5, 5, 1.0 },  new double[] { 9, 5, 1.0 },  new double[] { 11, 5, 0.0 }, new double[] { 18, 5, 0.0 }, new double[] { 19, 5, 0.0 },
					new double[] { 22, 5, 0.5 }, new double[] { 25, 5, 1.0 }, new double[] { 27, 5, 0.5 }, new double[] { 28, 5, 0.5 }, new double[] { 34, 5, 0.0 },
					new double[] { 76, 5, 0.0 }, new double[] { 35, 5, 0.0 }, new double[] { 57, 5, 0.5 }, new double[] { 58, 5, 0.0 }, new double[] { 61, 5, 1.0 },
					new double[] { 62, 5, 0.0 }, new double[] { 70, 5, 1.0 }, new double[] { 73, 5, 1.0 / 3.0 }, new double[] { 107, 5, 0.0 }, new double[] { 108, 5, 0.5 },
					new double[] { 112, 5, 0.0 }, new double[] { 114, 5, 0.0 }, new double[] { 91, 5, 0.0 },
					new double[] { 20, 5, 0.5 }, new double[] { 104, 5, 0.5 }, new double[] { 100, 5, 1.0 }, new double[] { 111, 5, 1.0 },
					new double[] { 21, 5, 0.5 }, new double[] { 65, 5, 3.0 }, new double[] { 66, 5, 0.0 }, new double[] { 75, 5, 3.5 },
					new double[] { 83, 5, Math.Sqrt(2.0) }, new double[] { 86, 5, 0.0 }, new double[] { 87, 5, 0.5 / Math.Sqrt(Math.Log(2.0)) },
					new double[] { 88, 5, Math.Sqrt(2.0) / 2.0 }, new double[] { 89, 5, Math.Sqrt(2.0) / 2.0 }, new double[] { 90, 5, yz },
					new double[] { 101, 5, 0.0 }, new double[] { 102, 5, 0.0 } };
				int bad = 0; string first = "";
				for (int k = 0; k < exp.Length; k++)
				{
					double x = s.Feature((int)exp[k][0], (int)exp[k][1], 14, at);
					if (double.IsNaN(x) || Math.Abs(x - exp[k][2]) > 1e-9) { bad++; if (first.Length == 0) first = "familia " + I((int)exp[k][0]) + " obtenido " + F(x) + " esperado " + F(exp[k][2]); }
				}
				Chk("L8.1 D-018b: " + I(exp.Length) + " familias reexpresadas/corregidas a ATR, valores a mano (rampa alcista)", bad == 0, "malos=" + I(bad) + " " + first);
				// 92 (ulcer): sin caidas el denominador es el piso 1e-12: positivo y enorme pero finito
				double u92 = s.Feature(92, 5, 14, at);
				Chk("L8.2 familia 92 sin caidas: positiva y finita (piso del denominador)", u92 > 1e6 && !double.IsInfinity(u92), "valor=" + u92.ToString("E3", CultureInfo.InvariantCulture));
				// 118 corregida (1 ATR en vez de 1 punto): colineal => 1
				Chk("L8.3 familia 118 (delta/ATR): rampa colineal = 1", Math.Abs(s.Feature(118, 5, 14, at) - 1.0) < 1e-9, "f118=" + F(s.Feature(118, 5, 14, at)));
				// espejo bajista: signo opuesto en familias con signo
				double[] o2, h2, l2, c2, v2; Ramp(400, -1.0, out o2, out h2, out l2, out c2, out v2);
				NQCE3_Series s2 = new NQCE3_Series(Make(o2, h2, l2, c2, 400), v2);
				int[] fm = new int[] { 5, 9, 22, 25, 27, 28, 57, 61, 70, 73, 108, 20, 100, 21, 65, 75, 83, 87, 88, 89 };
				int badM = 0;
				for (int k = 0; k < fm.Length; k++)
				{
					double a1 = s.Feature(fm[k], 5, 14, at), a2 = s2.Feature(fm[k], 5, 14, at);
					if (double.IsNaN(a1) || double.IsNaN(a2) || Math.Abs(a1 + a2) > 1e-9) badM++;
				}
				Chk("L8.4 espejo bajista: " + I(fm.Length) + " familias dan el valor opuesto", badM == 0, "malas=" + I(badM));
				// duplicados conocidos (informativo, se verifica que siguen siendo duplicados para que el orquestador los vea)
				bool dup = Math.Abs(s.Feature(20, 9, 14, at) - s.Feature(104, 9, 14, at)) < 1e-12 && Math.Abs(s.Feature(20, 9, 14, at) - s.Feature(108, 9, 14, at)) < 1e-12;
				Chk("L8.5 las formulas eliminadas siguen en el codigo y coinciden con la conservada (20 == 104 == 108, rampa)", dup, "f20=" + F(s.Feature(20, 9, 14, at)));
				// 120 familias: todas devuelven un valor finito o NaN sin excepcion sobre una caminata con volumen
				int N = 1000; double[] ow = new double[N], hw = new double[N], lw = new double[N], cw = new double[N], vw = new double[N];
				Walk(31L, N, 0, ow, hw, lw, cw, vw);
				NQCE3_Series sw = new NQCE3_Series(Make(ow, hw, lw, cw, N), vw); int finite = 0, ex = 0;
				for (int f = 0; f < 120; f++)
				{
					try { double x = sw.Feature(f, 12, 14, 700); if (!double.IsNaN(x)) finite++; } catch (Exception) { ex++; }
				}
				Chk("L8.6 las 120 familias evaluan sin excepcion (n=12) y devuelven valor finito", ex == 0 && finite == 120, "finitas=" + I(finite) + " excepciones=" + I(ex));
			}

			// ---- L9 D-030
			{
				// rejilla
				int sumThr = 0; bool grid = true;
				for (int r = 0; r < 11; r++) sumThr += NQCE3_Rules.ThresholdCount(r);
				grid &= NQCE3_Rules.ThresholdCount(3) == 5 && NQCE3_Rules.ThresholdCount(6) == 5 && NQCE3_Rules.ThresholdCount(4) == 5 && NQCE3_Rules.ThresholdCount(7) == 5 && NQCE3_Rules.ThresholdCount(5) == 10 && NQCE3_Rules.ThresholdCount(8) == 10;
				grid &= Math.Abs(NQCE3_Rules.ThresholdValue(3, 0) - 0.05) < 1e-12 && Math.Abs(NQCE3_Rules.ThresholdValue(3, 4) - 0.25) < 1e-12 && Math.Abs(NQCE3_Rules.ThresholdValue(4, 0) - 0.30) < 1e-12
					&& Math.Abs(NQCE3_Rules.ThresholdValue(4, 4) - 0.50) < 1e-12 && Math.Abs(NQCE3_Rules.ThresholdValue(5, 0) - 0.55) < 1e-12 && Math.Abs(NQCE3_Rules.ThresholdValue(5, 9) - 1.00) < 1e-12
					&& Math.Abs(NQCE3_Rules.ThresholdValue(3, 2) - 0.15) < 1e-12 && Math.Abs(NQCE3_Rules.ThresholdValue(4, 1) - 0.35) < 1e-12 && Math.Abs(NQCE3_Rules.ThresholdValue(5, 1) - 0.60) < 1e-12
					&& double.IsNaN(NQCE3_Rules.ThresholdValue(0, 0));
				Chk("L9.1 rejilla de umbrales: 45 combinaciones, extremos y valores historicos 0,15/0,35/0,60 presentes", sumThr == 45 && grid, "combinaciones=" + I(sumThr));
				// bandas disjuntas
				HashSet<string> seen = new HashSet<string>(); bool disj = true;
				for (int r = 3; r <= 5; r++) for (int k = 0; k < NQCE3_Rules.ThresholdCount(r); k++) { if (!seen.Add(NQCE3_Rules.ThresholdValue(r, k).ToString("0.00", CultureInfo.InvariantCulture))) disj = false; }
				Chk("L9.2 las bandas de las reglas 3/4/5 no se solapan (sin condiciones duplicadas)", disj && seen.Count == 20, "valores=" + I(seen.Count));
				// reglas con umbral explicito a mano
				double[][] cs = new double[][] {
					new double[] { 0.06, 0, 0, 3, 0.05, 1 }, new double[] { 0.05, 0, 0, 3, 0.05, 0 }, new double[] { -0.26, 0, 0, 3, 0.25, -1 },
					new double[] { 0.51, 0, 0, 4, 0.50, 1 }, new double[] { 0.30, 0, 0, 4, 0.30, 0 }, new double[] { 1.01, 0, 0, 5, 1.00, 1 }, new double[] { 0.99, 0, 0, 5, 1.00, 0 },
					new double[] { 0.30, 0.25, 0, 6, 0.25, 1 }, new double[] { 0.30, 0.26, 0, 6, 0.25, 0 }, new double[] { 0.55, 0.50, 0, 7, 0.50, 1 },
					new double[] { -0.5, -0.6, 0, 8, 0.55, 1 }, new double[] { 0.5, 0.6, 0, 8, 0.55, -1 }, new double[] { -0.6, -0.6, 0, 8, 0.55, 0 },
					new double[] { 0.1, 0, 0, 0, 0.05, 1 }, new double[] { 0.1, 0.1, 0, 2, 0.05, 0 } };
				int bad = 0;
				for (int k = 0; k < cs.Length; k++)
					if (NQCE3_Rules.Resolve(cs[k][0], cs[k][1], cs[k][2], (int)cs[k][3], cs[k][4]) != (int)cs[k][5]) bad++;
				Chk("L9.3 reglas 0,2,3-8 con umbral explicito (" + I(cs.Length) + " casos a mano)", bad == 0, "malos=" + I(bad));
				// Create con periodo y umbral, Describe y empaquetado
				NQCE3_Library lb9 = new NQCE3_Library();
				int idR3 = 6 * 11 + 3 + 1;     // familia 6, regla 3 (umbral bajo)
				int idR0 = 6 * 11 + 0 + 1;     // familia 6, regla 0 (sin umbral)
				Chk("L9.4 niveles: regla 3 -> 5, regla 4 -> 5, regla 5 -> 10, regla 0 -> 1; 27 periodos", lb9.ThresholdLevels(idR3) == 5 && lb9.ThresholdLevels(6 * 11 + 4 + 1) == 5 && lb9.ThresholdLevels(6 * 11 + 5 + 1) == 10 && lb9.ThresholdLevels(idR0) == 1 && lb9.PeriodLevels(idR3) == 27, "r3=" + I(lb9.ThresholdLevels(idR3)));
				INQCE_Condition c1 = lb9.Create(idR3, 3, 2, 14, 1), c2 = lb9.Create(idR3, 3, 4, 14, 1), c3 = lb9.Create(idR0, 3, 0, 14, 1);
				Chk("L9.5 Describe incluye periodo y umbral (n=7, thr=0.15 / 0.25); la regla 0 no lleva umbral", c1.Describe().Contains("n=7") && c1.Describe().Contains("thr=0.15") && c2.Describe().Contains("thr=0.25") && !c3.Describe().Contains("thr="), c1.Describe() + " | " + c3.Describe());
				bool thrErr = false; try { lb9.Create(idR0, 0, 1, 14, 1); } catch (ArgumentOutOfRangeException) { thrErr = true; }
				bool thrErr2 = false; try { lb9.Create(idR3, 0, 5, 14, 1); } catch (ArgumentOutOfRangeException) { thrErr2 = true; }
				Chk("L9.6 Create rechaza nivel de umbral fuera de rango", thrErr && thrErr2, "ok");
				int pk = NQCE3_Genome.Pack(lb9, idR3, 7, 3), pl, tl; NQCE3_Genome.Unpack(lb9, idR3, pk, out pl, out tl);
				Chk("L9.7 empaquetado del gen (periodo,umbral) ida y vuelta; rango = 27 * 5", pk == 7 * 5 + 3 && pl == 7 && tl == 3 && NQCE3_Genome.ParamRange(lb9, idR3) == 135 && NQCE3_Genome.ParamRange(lb9, idR0) == 27 && NQCE3_Genome.ParamRange(lb9, 6 * 11 + 5 + 1) == 270, "gen=" + I(pk));
				// TrialId distingue el umbral y el periodo, y es estable
				NQCE3_Genome g1 = new NQCE3_Genome(); g1.G[0] = idR3; g1.G[1] = NQCE3_Genome.Pack(lb9, idR3, 3, 2); g1.G[17] = 14; g1.G[18] = 4; g1.G[19] = 0;
				NQCE3_Genome g2 = g1.Clone(); g2.G[1] = NQCE3_Genome.Pack(lb9, idR3, 3, 3);
				NQCE3_Genome g3 = g1.Clone(); g3.G[1] = NQCE3_Genome.Pack(lb9, idR3, 4, 2);
				Chk("L9.8 TrialId: distinto umbral => distinto id; distinto periodo => distinto id; igual => igual", g1.TrialId(lb9) != g2.TrialId(lb9) && g1.TrialId(lb9) != g3.TrialId(lb9) && g1.TrialId(lb9) == g1.Clone().TrialId(lb9), g1.TrialId(lb9).Substring(0, 12));
				// operadores: siempre validos
				NQCE_Rng rg = new NQCE_Rng(12345L); NQCE3_Params pr = new NQCE3_Params(); int badG = 0, tot = 0; bool thrMoved = false, perMoved = false;
				for (int k = 0; k < 3000; k++)
				{
					NQCE3_Genome ga = NQCE3_Operators.RandomGenome(rg, lb9, pr), gb = NQCE3_Operators.RandomGenome(rg, lb9, pr);
					NQCE3_Genome ch = NQCE3_Operators.Crossover(rg, ga, gb, lb9); NQCE3_Genome before = ch.Clone();
					NQCE3_Params hi = new NQCE3_Params(); hi.MutationRate = 0.5; NQCE3_Operators.Mutate(rg, ch, hi, lb9);
					NQCE3_Genome[] all = new NQCE3_Genome[] { ga, gb, ch };
					for (int q = 0; q < 3; q++)
					{
						tot++; NQCE3_Genome g = all[q]; bool ok = true;
						for (int sl = 0; sl < 8; sl++)
						{
							int cc = g.G[2 * sl], pp = g.G[2 * sl + 1];
							if (cc < 0 || cc > 1320 || (cc != 0 && !lb9.IsValidCondition(cc))) ok = false;
							else if (cc == 0 && pp != 0) ok = false;
							else if (cc > 0 && (pp < 0 || pp >= NQCE3_Genome.ParamRange(lb9, cc))) ok = false;
						}
						if (!ok) badG++;
					}
					for (int sl = 0; sl < 8; sl++)
						if (before.G[2 * sl] != 0 && before.G[2 * sl] == ch.G[2 * sl] && before.G[2 * sl + 1] != ch.G[2 * sl + 1])
						{
							int c0 = ch.G[2 * sl], p0, t0, p1, t1; NQCE3_Genome.Unpack(lb9, c0, before.G[2 * sl + 1], out p0, out t0); NQCE3_Genome.Unpack(lb9, c0, ch.G[2 * sl + 1], out p1, out t1);
							if (p0 != p1) perMoved = true; if (t0 != t1) thrMoved = true;
						}
				}
				Chk("L9.9 RandomGenome/Crossover/Mutate: genes siempre validos (9000 genomas); la mutacion mueve periodo y umbral", badG == 0 && thrMoved && perMoved, "invalidos=" + I(badG) + "/" + I(tot) + " umbral_movido=" + thrMoved + " periodo_movido=" + perMoved);
				// tamano del espacio por ranura
				long pairs = 0; for (int k = 0; k < lb9.ConditionCount; k++) pairs += NQCE3_Genome.ParamRange(lb9, lb9.ConditionId(k));
				Chk("L9.10 espacio por ranura (condicion,periodo,umbral) = 113 x 1.215 = 137.295 (antes 145.800 con 120 familias)", pairs == 137295, "pares=" + I((int)pairs));
				// decodificacion: miembro con la condicion de umbral
				NQCE_Member mem = g1.ToMember(lb9, "t");
				Chk("L9.11 ToMember decodifica periodo y umbral (GenomeText incluye 'thr=0.15')", mem != null && mem.GenomeText().Contains("thr=0.15") && mem.GenomeText().Contains("n=7"), mem == null ? "null" : mem.GenomeText());
			}

			// ---- L10 volumen, calentamiento, cache
			{
				int N = 1500; double[] o = new double[N], h = new double[N], l = new double[N], c = new double[N], v = new double[N];
				Walk(99L, N, 0, o, h, l, c, v); NQCE_Bars full = Make(o, h, l, c, N);
				NQCE3_Series sN = new NQCE3_Series(full, null); int exc = 0;
				for (int f = 113; f < 120; f++) { try { sN.Feature(f, 12, 14, 1200); } catch (Exception) { exc++; } }
				Chk("L10.1 sin volumen las familias 113-119 evaluan sin excepcion (SumVolume protegido)", exc == 0, "excepciones=" + I(exc));
				bool badVol = false; try { double[] vb = (double[])v.Clone(); vb[10] = double.NaN; new NQCE3_Series(full, vb); } catch (ArgumentException) { badVol = true; }
				bool badPx = false; try { double[] cb = (double[])c.Clone(); cb[5] = double.NaN; new NQCE3_Series(Make(o, h, l, cb, N), v); } catch (ArgumentException) { badPx = true; }
				Chk("L10.2 la serie rechaza OHLC/volumen no finitos (sin Math.Sign(NaN))", badVol && badPx, "volumen=" + badVol + " precio=" + badPx);
				bool noVolErr = false; try { new NQCE3_Fitness(full, null, new NQCE_Config(), new NQCE3_Library()); } catch (ArgumentException) { noVolErr = true; }
				Chk("L10.3 NQCE3_Fitness exige volumen enlazado (error explicito)", noVolErr, "ok");
				// presupuesto LRU: con presupuesto para ~3 series, la cache no crece y no se vacia de golpe
				long old = NQCE3_Series.MemoBudgetBytes; NQCE3_Series.MemoBudgetBytes = 8L * 124 * 3; int maxCount = 0;
				NQCE3_Series sL = new NQCE3_Series(full, v);
				double a0 = sL.Feature(0, 10, 14, 600);
				for (int f = 1; f < 8; f++) { sL.Feature(f, 10, 14, 600); if (sL.MemoCount > maxCount) maxCount = sL.MemoCount; }
				double a1 = sL.Feature(0, 10, 14, 600);
				NQCE3_Series.MemoBudgetBytes = old;
				Chk("L10.4 cache LRU: respeta el presupuesto (<=3 series), no se vacia de golpe y el valor recalculado es identico", maxCount == 3 && (a0 == a1 || (double.IsNaN(a0) && double.IsNaN(a1))), "max=" + I(maxCount));
				// MACD memorizado == directo
				NQCE3_Series sM = new NQCE3_Series(full, v);
				bool same = true;
				foreach (int at2 in new int[] { 575, 600, 650 })
					same &= sM.MacdMemo(6, 12, at2) == NQCE3_M.EMA(sM, at2, 6) - NQCE3_M.EMA(sM, at2, 12) && sM.MacdMemo(6, 12, at2) == NQCE3_M.EMA(sM, at2, 6) - NQCE3_M.EMA(sM, at2, 12);
				Chk("L10.5 MACD memorizado == calculo directo", same, "ok");
			}

			// ---- L11 D-031(1)(2)(3)
			{
				int N = 3000; double[] o = new double[N], h = new double[N], l = new double[N], c = new double[N], v = new double[N];
				Walk(31L, N, 0, o, h, l, c, v); NQCE_Bars full = Make(o, h, l, c, N); NQCE3_Series s = new NQCE3_Series(full, v);
				List<int> atl = new List<int>(); for (int q = 300; q < 2990; q += 11) atl.Add(q); int[] at11 = atl.ToArray();
				// L11.1 el ATR es el de Wilder del ejecutor (mismo arreglo), con el periodo p del gen de salida: familia 26 = (C-C[n])/ATR_p
				bool atrOk = true; int[] pp = new int[] { 5, 14, 100 };
				foreach (int p in pp)
					foreach (int at in at11)
					{
						double exp26 = (c[at] - c[at - 12]) / Math.Max(full.Atr(p)[at], 1e-12);
						if (Math.Abs(s.Feature(26, 12, p, at) - exp26) > 1e-9 * Math.Max(1.0, Math.Abs(exp26))) atrOk = false;
					}
				bool differs = Math.Abs(s.Feature(26, 12, 5, 2040) - s.Feature(26, 12, 100, 2040)) > 1e-6;
				Chk("L11.1 ATR de Wilder (NQCE_Bars.Atr) con el periodo del gen de salida: familia 26 == (C-C[n])/ATR_p, p=5/14/100; p cambia el valor", atrOk && differs && object.ReferenceEquals(s.Src.Atr(14), full.Atr(14)), "ok");
				// L11.2 clasificacion AtrKind verificada: '0' independiente de p y de la unidad; '1' == numerador/ATR_p; '2' no cumple ninguna
				int[] nn = new int[] { 5, 12, 26 }; int[] qq = new int[] { 7, 40 }; int bad0 = 0, bad1 = 0, bad2 = 0, c0 = 0, c1 = 0, c2 = 0; string first = "";
				for (int f = 0; f < 120; f++)
				{
					char kd = NQCE3_Catalog.AtrKindOf(f); bool ind = true, dec = true;
					foreach (int n in nn) foreach (int at in at11)
					{
						double u = s.Direct(f, n, 7, at, true), d7 = s.Direct(f, n, 7, at, false), d40 = s.Direct(f, n, 40, at, false);
						double a7 = Math.Max(full.Atr(7)[at], 1e-12), a40 = Math.Max(full.Atr(40)[at], 1e-12);
						if (!Near(d7, u) || !Near(d40, u)) ind = false;
						if (!Near(d7, u / a7) || !Near(d40, u / a40)) dec = false;
					}
					if (kd == '0') { c0++; if (!ind) { bad0++; if (first.Length == 0) first = "f" + I(f) + "!=0"; } }
					else if (kd == '1') { c1++; if (!dec || ind) { bad1++; if (first.Length == 0) first = "f" + I(f) + "!=1"; } }
					else { c2++; if (ind || dec) { bad2++; if (first.Length == 0) first = "f" + I(f) + "!=2"; } }
				}
				Chk("L11.2 AtrKind verificado numericamente sobre 120 familias (245 muestras c/u): independientes=" + I(c0) + ", numerador/ATR=" + I(c1) + ", no descomponibles=" + I(c2), bad0 == 0 && bad1 == 0 && bad2 == 0 && c2 == 9 && c0 + c1 + c2 == 120, "malas=" + I(bad0 + bad1 + bad2) + " " + first);
				// L11.3 cache: una familia '1' comparte el numerador entre periodos de ATR; una '2' usa una entrada por (n,p); una '0' una sola
				NQCE3_Series sc = new NQCE3_Series(full, v); int at0 = 650;
				sc.Feature(0, 10, 5, at0); sc.Feature(0, 10, 60, at0); int m1 = sc.MemoCount;
				sc.Feature(78, 10, 5, at0); sc.Feature(78, 10, 60, at0); int m2 = sc.MemoCount - m1;
				sc.Feature(24, 10, 5, at0); sc.Feature(24, 10, 60, at0); int m3 = sc.MemoCount - m1 - m2;
				Chk("L11.3 cache: 1 entrada para dos periodos de ATR en una familia '1', 2 en una '2', 1 en una '0'", m1 == 1 && m2 == 2 && m3 == 1, "entradas=" + I(m1) + "/" + I(m2) + "/" + I(m3));
				// L11.4 duplicados exactos eliminados == conservados (n x p x barras)
				int[][] pairs = new int[][] { new int[] { 9, 0 }, new int[] { 18, 11 }, new int[] { 52, 5 }, new int[] { 76, 34 }, new int[] { 81, 7 }, new int[] { 104, 20 }, new int[] { 108, 20 } };
				int badDup = 0, totDup = 0; string firstDup = "";
				foreach (int[] pr in pairs)
					foreach (int n in new int[] { 4, 12, 30 }) foreach (int p in new int[] { 5, 14, 60 }) foreach (int at in at11)
					{
						double a = s.Feature(pr[0], n, p, at), b = s.Feature(pr[1], n, p, at); totDup++;
						if (!Near(a, b)) { badDup++; if (firstDup.Length == 0) firstDup = "f" + I(pr[0]) + " vs f" + I(pr[1]) + " n=" + I(n) + " " + F(a) + "/" + F(b); }
					}
				bool allRemoved = true; foreach (int[] pr in pairs) allRemoved &= !NQCE3_Catalog.IsActiveFamily(pr[0]) && NQCE3_Catalog.IsActiveFamily(pr[1]) && pr[1] < pr[0];
				Chk("L11.4 los 7 duplicados exactos eliminados (9,18,52,76,81,104,108) == la conservada de id menor (" + I(totDup) + " comparaciones)", badDup == 0 && allRemoved, "distintos=" + I(badDup) + " " + firstDup);
				// L11.5 casi duplicados (NO eliminados, solo informativo): 100~111, 72~50
				double d100 = 0, d72 = 0;
				foreach (int at in at11) { d100 = Math.Max(d100, Math.Abs(s.Feature(100, 12, 14, at) - s.Feature(111, 12, 14, at))); d72 = Math.Max(d72, Math.Abs(s.Feature(72, 12, 14, at) - s.Feature(50, 12, 14, at))); }
				Chk("L11.5 casi duplicados 100~111 y 72~50 siguen ACTIVOS (D-031: no se eliminan, solo se documentan)", NQCE3_Catalog.IsActiveFamily(100) && NQCE3_Catalog.IsActiveFamily(111) && NQCE3_Catalog.IsActiveFamily(72) && NQCE3_Catalog.IsActiveFamily(50),
					"max|f100-f111|=" + d100.ToString("E2", CultureInfo.InvariantCulture) + " max|f72-f50|=" + d72.ToString("E2", CultureInfo.InvariantCulture));
				// L11.6 R5: las 16 familias de excepcion D-031(1) estan marcadas 'X'
				int[] exc = new int[] { 10, 17, 24, 32, 33, 42, 59, 60, 64, 68, 69, 71, 85, 99, 103, 106, 110, 115, 118, 119 }; bool xo = true;
				foreach (int f in exc) xo &= NQCE3_Catalog.UnitOf(f) == 'X';
				Chk("L11.6 excepcion R5 (D-031(1)): las 20 familias adimensionales (16 de D-031 + 4 de D-032) marcadas X en UnitClass", xo, "ok");
				// L11.7 determinismo: dos series independientes dan el mismo valor (cache vs sin cache y repetido)
				NQCE3_Series sa = new NQCE3_Series(full, v), sb2 = new NQCE3_Series(full, v); bool det = true;
				foreach (int f in new int[] { 0, 26, 63, 78, 94, 118 }) foreach (int at in new int[] { 650, 700, 2040 })
					det &= BitEq(sa.Feature(f, 12, 33, at), sb2.Feature(f, 12, 33, at)) && BitEq(sa.Feature(f, 12, 33, at), sa.Feature(f, 12, 33, at));
				Chk("L11.7 determinismo: series independientes y relecturas dan valores identicos bit a bit", det, "ok");
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
