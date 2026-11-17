#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
#endregion

//==============================================================================
// NQC_ET2_EdgeInjector - Control positivo de ET2 (docs/STATISTICS_PLAN.md §4, D-020). Contrato NQC 2.0.0.
// SOLO DESARROLLO / sintetico. Sin ordenes.
//
// OBJETIVO: sobre un dataset N1, que una regla simple de la familia (condicion + salidas ATR) tenga expectativa NETA de
// +US$20 por operacion (referencia [B], costo 24,68 incluido) a ~0,5 operaciones por sesion.
//
// HALLAZGO DE DISENO (primera version, descartada): con UNA regla larga y deriva hasta las 11:30, cualquier posicion larga
// abierta en la ventana (p.ej. "siempre largo 09:31->11:30", stop ancho) captura la deriva en los dias con senal y obtiene
// ~+US$20 netos por operacion con el DOBLE de operaciones: un edge direccional no condicional, lookahead explotable por reglas
// ajenas. Correccion: la regla de referencia es un PAR ESPEJO (largo sobre ruptura alcista del rango de apertura, corto sobre
// ruptura bajista); la deriva sigue el signo de la senal, de modo que la exposicion NO condicional se anula en promedio.
//
// MECANISMO (causal, deriva condicionada a la senal):
//  1. El par R = {L, S} (NQCE_Member, MaxPerDay=1 cada uno) corre en el EJECUTOR sobre el dataset actual; los hooks recogen las
//     senales EXECUTED (ya filtradas por ventana, noticias, dias excluidos, ocupado).
//  2. En cada senal ejecutada (barra i, cierre de i) se suma al precio de las barras i+1..fin de sesion una DERIVA lineal de
//     monto total  M = Rt(dir * a * ATR[i])  (a en multiplos de ATR: invariante de escala, R5): en la barra j <= e
//     (e = barra de sello 690, la de la orden de plano) se suma Rt(M*(j-i)/(e-i)); de e en adelante, Rt(M). Se suman las 4
//     cotizaciones de la barra (O,H,L,C) por igual: OHLC sigue coherente y en rejilla. La entrada (apertura de i+1) ya incluye
//     el primer escalon; la salida de las 11:30 (apertura de 691) captura M completo.
//  3. NEUTRALIDAD DE NIVEL: la deriva NO persiste: la sesion siguiente parte del nivel base (el empalme absorbe -M en su hueco
//     nocturno, FUERA de la ventana operable). La serie no acumula tendencia. Costo: la primera barra de la sesion siguiente a
//     una senal tiene un TR distinto en ~|M| puntos (~1-2 puntos frente a huecos de decenas); el efecto sobre el ATR medio se
//     mide en la prueba (< 1%).
//  4. CAUSALIDAD: lo que se modifica en la barra j > i depende solo de (senal en i, ATR en i); las barras <= i de la misma
//     sesion NO cambian. Como las senales se detectan sobre el dataset ya modificado, se itera a PUNTO FIJO (senales y montos
//     iguales entre dos iteraciones); si no converge en MaxIter se marca Converged=false y el dataset NO se usa.
//  5. EXPLOTABILIDAD RESIDUAL (documentada): toda regla que comparta la senal (ruptura del rango en otras horas/anchos) captura
//     deriva: es el "o una equivalente" de la definicion de potencia. Variantes con stop ancho capturan MAS que la regla de
//     referencia (se reporta el techo: neto de la variante ksl=6 sin objetivo). La exposicion no condicional larga/corta queda ~0.
//  6. CALIBRACION de a: biseccion sobre a en [0, AMax] de modo que la expectativa neta MEDIDA POR NQCE_Executor (mismo cfg que
//     el embudo, costo 24,68) sobre las operaciones del par sea 20 +- Tol (por defecto +-2). Se registra a, la expectativa, N de
//     operaciones, ops por sesion y el t del par (para saber si el edge es detectable POR DISENO).
//  El par se elige de una lista fija de rangos (hasta el sello X, rupturas despues de X) como el de frecuencia ejecutada
//  conjunta mas cercana a 0,5 por sesion en el N1 ANTES de inyectar (solo mira datos nulos).
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	// Condicion espejo de NQCE_CondCloseAboveRange: cierre < minimo de los sellos [from,to] del dia, solo con sello > after.
	public sealed class NQCET2_CondCloseBelowRange : INQCE_Condition
	{
		private int from, to, after;
		public NQCET2_CondCloseBelowRange(int fromStamp, int toStamp, int afterStamp) { from = fromStamp; to = toStamp; after = afterStamp; }
		public bool Eval(NQCE_Ctx c)
		{
			if (c.Stamp <= after) return false;
			double rl = double.NaN;
			for (int k = 0; k < c.DayIdx.Count; k++)
			{
				int ix = c.DayIdx[k]; int st = c.B.Stamp[ix];
				if (st >= from && st <= to) rl = double.IsNaN(rl) ? c.B.L[ix] : Math.Min(rl, c.B.L[ix]);
			}
			if (double.IsNaN(rl)) return false;
			return c.B.C[c.I] < rl;
		}
		public string Describe() { return "CIERRE<MIN(" + from.ToString(CultureInfo.InvariantCulture) + ".." + to.ToString(CultureInfo.InvariantCulture) + ")@>" + after.ToString(CultureInfo.InvariantCulture); }
	}

	public sealed class NQCET2_SigHook : NQCE_NullHooks
	{
		public List<long> Ticks = new List<long>(); public List<string> Members = new List<string>();
		public override void OnSignal(int dayKey, long tick, string member, string outcome) { if (outcome == "EXECUTED") { Ticks.Add(tick); Members.Add(member); } }
	}

	public sealed class NQCET2_EdgeInjector : NQCRUN_IDatasetTransform
	{
		public const double RefNetPerTrade = 20.0;   // D-020 [B]
		public const double RefRate = 0.5;           // D-020 [B]
		public const double AMax = 8.0;              // techo de la biseccion (deriva total en ATR); D-040: +100 exige ~1,9 ATR en el mundo sintetico (ver STATISTICS/ET2.md); 8 deja margen para ATR/ruido reales
		// Tolerancia por nivel de edge (D-040): max(2, 8% del objetivo) -> +20: 2; +60: 5 (4,8); +100: 8.
		public static double TolFor(double target) { return Math.Max(2.0, Math.Round(0.08 * target)); }
		public List<NQCE_Member> Rules; public NQCE_Config Cfg; public double A; public int MaxIter = 6;
		public bool Converged; public int Iterations, Signals;

		public NQCET2_EdgeInjector(List<NQCE_Member> rules, NQCE_Config cfg, double a)
		{
			Rules = rules; Cfg = cfg; A = a;
			foreach (NQCE_Member m in rules) if (m.MaxPerDay != 1) throw new ArgumentException("las reglas inyectadas exigen MaxPerDay=1");
		}
		public string Name { get { return "INJ"; } }
		public string RulesText { get { StringBuilder sb = new StringBuilder(); for (int k = 0; k < Rules.Count; k++) { if (k > 0) sb.Append(" || "); sb.Append(Rules[k].GenomeText()); } return sb.ToString(); } }
		public string ParamsText { get { return "a=" + A.ToString("0.0000", CultureInfo.InvariantCulture) + ";rules=" + RulesText + ";converged=" + (Converged ? 1 : 0) + ";iters=" + Iterations.ToString(CultureInfo.InvariantCulture) + ";signals=" + Signals.ToString(CultureInfo.InvariantCulture); } }

		// Par espejo para un rango de apertura hasta el sello rangeTo (rupturas despues de rangeTo). Salidas ATR fijas: ATR 14, ksl 2,0, ktp 3,0.
		public static List<NQCE_Member> MakePair(int rangeTo)
		{
			List<NQCE_Member> l = new List<NQCE_Member>();
			NQCE_Member a = new NQCE_Member(); a.Id = "INJ_L" + rangeTo.ToString(CultureInfo.InvariantCulture); a.Dir = 1;
			a.Conditions.Add(new NQCE_CondCloseAboveRange(NQCE_Const.StampFirstEntry, rangeTo, rangeTo));
			NQCE_Member s = new NQCE_Member(); s.Id = "INJ_S" + rangeTo.ToString(CultureInfo.InvariantCulture); s.Dir = -1;
			s.Conditions.Add(new NQCET2_CondCloseBelowRange(NQCE_Const.StampFirstEntry, rangeTo, rangeTo));
			foreach (NQCE_Member m in new NQCE_Member[] { a, s }) { m.AtrPeriod = 14; m.Ksl = 2.0; m.Ktp = 3.0; m.MaxPerDay = 1; l.Add(m); }
			return l;
		}
		public static readonly int[] CandidateRangeTo = new int[] { 585, 600, 615, 630, 645, 660, 675 };

		public static NQCE_Result RunRules(NQCE_Bars b, List<NQCE_Member> rules, NQCE_Config cfg, NQCET2_SigHook hook)
		{
			NQCE_Portfolio pf = new NQCE_Portfolio(); pf.Name = "INJ"; pf.Members.AddRange(rules);
			return NQCE_Executor.Run(b, pf, cfg, hook);
		}

		public static List<NQCE_Member> ChooseRules(NQCE_Bars b, NQCE_Config cfg, out double rate)
		{
			List<NQCE_Member> best = null; rate = 0; double bd = double.MaxValue;
			foreach (int to in CandidateRangeTo)
			{
				List<NQCE_Member> m = MakePair(to); NQCE_Result r = RunRules(b, m, cfg, null);
				double q = r.Sessions.Count == 0 ? 0 : (double)r.Trades.Count / r.Sessions.Count;
				if (Math.Abs(q - RefRate) < bd - 1e-12) { bd = Math.Abs(q - RefRate); best = m; rate = q; }
			}
			return best;
		}

		public static double MeanNet(NQCE_Result r) { if (r.Trades.Count == 0) return double.NaN; double s = 0; foreach (NQCE_Trade t in r.Trades) s += t.Net; return s / r.Trades.Count; }
		public static double TStat(NQCE_Result r)
		{
			int n = r.Trades.Count; if (n < 2) return double.NaN; double mu = MeanNet(r), v = 0;
			foreach (NQCE_Trade t in r.Trades) v += (t.Net - mu) * (t.Net - mu); v /= (n - 1);
			return v <= 0 ? double.NaN : mu / Math.Sqrt(v / n);
		}

		public void Apply(NQCE_Bars bars, double[] volume, out NQCE_Bars outBars, out double[] outVolume)
		{
			outVolume = volume == null ? null : (double[])volume.Clone();
			outBars = Inject(bars);
		}

		// Inyeccion a punto fijo sobre las barras base (no modifica la entrada).
		public NQCE_Bars Inject(NQCE_Bars baseBars)
		{
			List<NQCET2_Sess> ss = NQCET2_Core.Split(baseBars);
			Dictionary<string, int> dirOf = new Dictionary<string, int>(); foreach (NQCE_Member m in Rules) dirOf[m.Id] = m.Dir;
			NQCE_Bars cur = baseBars; List<long> usedT = new List<long>(); List<double> usedM = new List<double>();
			Converged = false; Iterations = 0; Signals = 0;
			for (int it = 0; it < MaxIter; it++)
			{
				Iterations = it + 1;
				NQCET2_SigHook hk = new NQCET2_SigHook(); RunRules(cur, Rules, Cfg, hk);
				List<long> sigT = new List<long>(); List<double> amt = new List<double>();
				for (int q = 0; q < hk.Ticks.Count; q++)
				{
					int i = Array.BinarySearch(cur.T, hk.Ticks[q]); if (i < 0) continue;
					NQCE_Member mem = null; foreach (NQCE_Member m in Rules) if (m.Id == hk.Members[q]) mem = m;
					sigT.Add(hk.Ticks[q]); amt.Add(NQCE_Const.Rt(mem.Dir * A * cur.Atr(mem.AtrPeriod)[i]));
				}
				Signals = sigT.Count;
				if (SameList(sigT, usedT, amt, usedM)) { Converged = true; break; }
				usedT = sigT; usedM = amt;
				cur = Build(baseBars, ss, sigT, amt);
			}
			return cur;
		}

		private static bool SameList(List<long> a, List<long> b, List<double> x, List<double> y)
		{
			if (a.Count != b.Count) return false;
			for (int k = 0; k < a.Count; k++) if (a[k] != b[k] || x[k] != y[k]) return false;
			return true;
		}

		private NQCE_Bars Build(NQCE_Bars b, List<NQCET2_Sess> ss, List<long> sigT, List<double> amt)
		{
			double[] O = (double[])b.O.Clone(), H = (double[])b.H.Clone(), L = (double[])b.L.Clone(), C = (double[])b.C.Clone();
			for (int q = 0; q < sigT.Count; q++)
			{
				int i = Array.BinarySearch(b.T, sigT[q]); double m = amt[q];
				NQCET2_Sess s = null; for (int k = 0; k < ss.Count; k++) if (i >= ss[k].Start && i <= ss[k].End) { s = ss[k]; break; }
				if (s == null || i >= s.End) continue;
				int e = s.End; for (int j = i + 1; j <= s.End; j++) if (b.Stamp[j] == NQCE_Const.StampFlat) { e = j; break; }
				int n = Math.Max(1, e - i);
				for (int j = i + 1; j <= s.End; j++)
				{
					double sh = j <= e ? NQCE_Const.Rt(m * (j - i) / n) : NQCE_Const.Rt(m);
					O[j] += sh; H[j] += sh; L[j] += sh; C[j] += sh;
				}
			}
			return new NQCE_Bars((long[])b.T.Clone(), O, H, L, C);
		}

		// Biseccion de a para que la expectativa neta medida por el ejecutor sea target +- tol. Usa el mismo cfg del embudo.
		public static double Calibrate(NQCE_Bars baseBars, List<NQCE_Member> rules, NQCE_Config cfg, double target, double tol, out double measured, out int trades, out bool ok, out bool converged)
		{
			double lo = 0, hi = AMax; double bestA = 0, bestM = double.NaN, bestD = double.MaxValue; int bestN = 0; bool bestC = false;
			for (int it = 0; it < 18; it++)
			{
				double a = it == 0 ? hi : (lo + hi) / 2.0;
				NQCET2_EdgeInjector inj = new NQCET2_EdgeInjector(rules, cfg, a);
				NQCE_Bars d = inj.Inject(baseBars); NQCE_Result r = RunRules(d, rules, cfg, null); double mu = MeanNet(r);
				if (!double.IsNaN(mu) && Math.Abs(mu - target) < bestD && inj.Converged) { bestD = Math.Abs(mu - target); bestA = a; bestM = mu; bestN = r.Trades.Count; bestC = inj.Converged; }
				if (it == 0 && (double.IsNaN(mu) || mu < target - tol)) break;     // no alcanzable
				if (!double.IsNaN(mu) && Math.Abs(mu - target) <= tol * 0.25) break;
				if (it > 0) { if (mu < target) lo = a; else hi = a; }
			}
			measured = bestM; trades = bestN; converged = bestC; ok = bestD <= tol;
			return bestA;
		}
	}

	// Transformacion compuesta del control positivo: N1 -> elegir par -> calibrar a -> inyectar.
	public sealed class NQCET2_InjectedN1 : NQCRUN_IDatasetTransform
	{
		private long seed; private NQCE_Config cfg; private double target, tol; private NQCET2_N1 n1;
		public List<NQCE_Member> Rules; public double A, Measured, Rate, TPair, NetLong, NetShort; public int Trades; public bool Ok, Converged; public string RuleText = "";
		public NQCET2_InjectedN1(long seed, NQCE_Config cfg, double target, double tol) { this.seed = seed; this.cfg = cfg; this.target = target; this.tol = tol; n1 = new NQCET2_N1(seed, cfg.Excluded); }
		public string Name { get { return "N1+INJ"; } }
		public string ParamsText { get { return n1.ParamsText + ";target_net=" + target.ToString("0.00", CultureInfo.InvariantCulture) + ";tol=" + tol.ToString("0.00", CultureInfo.InvariantCulture) + ";a=" + A.ToString("0.0000", CultureInfo.InvariantCulture) + ";measured_net=" + Measured.ToString("0.00", CultureInfo.InvariantCulture) + ";net_long=" + NetLong.ToString("0.00", CultureInfo.InvariantCulture) + ";net_short=" + NetShort.ToString("0.00", CultureInfo.InvariantCulture) + ";trades=" + Trades.ToString(CultureInfo.InvariantCulture) + ";rate_per_session=" + Rate.ToString("0.000", CultureInfo.InvariantCulture) + ";t_pair=" + TPair.ToString("0.00", CultureInfo.InvariantCulture) + ";ok=" + (Ok ? 1 : 0) + ";rules=" + RuleText; } }

		public void Apply(NQCE_Bars bars, double[] volume, out NQCE_Bars outBars, out double[] outVolume)
		{
			NQCE_Bars nb; double[] nv; n1.Apply(bars, volume, out nb, out nv);
			double r0; Rules = NQCET2_EdgeInjector.ChooseRules(nb, cfg, out r0);
			NQCET2_EdgeInjector inj = new NQCET2_EdgeInjector(Rules, cfg, 0); RuleText = inj.RulesText;
			A = NQCET2_EdgeInjector.Calibrate(nb, Rules, cfg, target, tol, out Measured, out Trades, out Ok, out Converged);
			inj = new NQCET2_EdgeInjector(Rules, cfg, A);
			outBars = inj.Inject(nb); outVolume = nv; Converged = inj.Converged;
			NQCE_Result r = NQCET2_EdgeInjector.RunRules(outBars, Rules, cfg, null);
			Measured = NQCET2_EdgeInjector.MeanNet(r); Trades = r.Trades.Count; Rate = r.Sessions.Count == 0 ? 0 : (double)Trades / r.Sessions.Count; TPair = NQCET2_EdgeInjector.TStat(r);
			double sl = 0, ss2 = 0; int nl = 0, ns = 0; foreach (NQCE_Trade t in r.Trades) { if (t.Dir == 1) { sl += t.Net; nl++; } else { ss2 += t.Net; ns++; } }
			NetLong = nl == 0 ? double.NaN : sl / nl; NetShort = ns == 0 ? double.NaN : ss2 / ns;
			Ok = Ok && Converged && !double.IsNaN(Measured) && Math.Abs(Measured - target) <= tol;
		}
	}
}
