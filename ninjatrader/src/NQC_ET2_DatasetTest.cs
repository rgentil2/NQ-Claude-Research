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
// NQC_ET2_DatasetTest - Prueba TECNICA SINTETICA de NQC_ET2_Datasets / EdgeInjector / Calibration. SIN ordenes, barras generadas
// por NQCE_Rng (semilla 777), NO usa ni reporta datos ni economia de mercado. Requiere NQC_ET1_Core, NQC_ET1C_AccountSim,
// NQC_ET2_IfaceCopy (o NQC_RUN), NQC_ET2_Datasets, NQC_ET2_EdgeInjector, NQC_ET2_Calibration.
// Mundo: 500 sesiones habiles desde 2021-01-04, sellos 569..692 (124 barras M1), paseo aleatorio con volatilidad por sesion,
// hueco nocturno aleatorio, volumen. La sesion 100 pierde la barra de sello 600 (firma unica) y el dia de la sesion 50 es "excluido".
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public class NQC_ET2_DatasetTest : Strategy
	{
		private const string ScriptVersion = "ET2DATASETTEST-1.0.0";
		private bool started, done; private StringBuilder rep = new StringBuilder(); private int pass, fail;
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults) { Name = "NQC_ET2_DatasetTest"; Calculate = Calculate.OnBarClose; BarsRequiredToTrade = 0; IsExitOnSessionCloseStrategy = false; TraceOrders = false; }
			else if (State == State.DataLoaded) started = true;
			else if (State == State.Terminated) { if (!started || done) return; done = true; RunAll(); }
		}
		protected override void OnBarUpdate() { }
		private static string F(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }
		private static string F2(double v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }
		private void Chk(string name, bool ok, string info) { if (ok) pass++; else fail++; rep.Append(ok ? "[PASS] " : "[FALLA] ").Append(name).Append(" | ").Append(info).Append("\n"); }

		private static NQCE_Bars MakeWorld(out double[] vol, out int excludedDay, out int irregIdx)
		{
			List<long> T = new List<long>(); List<double> O = new List<double>(), H = new List<double>(), L = new List<double>(), C = new List<double>(), V = new List<double>();
			NQCE_Rng r = new NQCE_Rng(777); double prev = 15000; DateTime d = new DateTime(2021, 1, 4); int k = 0; excludedDay = 0; irregIdx = 100;
			while (k < 500)
			{
				if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) { d = d.AddDays(1); continue; }
				double sig = 1.0 + 3.0 * r.NextDouble(), gap = NQCE_Const.Rt((r.NextDouble() - 0.5) * 80.0), sf = 0.5 + r.NextDouble();
				double px = NQCE_Const.Rt(prev + gap);
				if (k == 50) excludedDay = d.Year * 10000 + d.Month * 100 + d.Day;
				for (int q = 0; q < 124; q++)
				{
					int stamp = 569 + q; if (k == irregIdx && stamp == 600) continue;
					double step = NQCE_Const.Rt((r.NextDouble() + r.NextDouble() + r.NextDouble() - 1.5) * 2.0 * sig);
					double o = px, c = NQCE_Const.Rt(px + step);
					double h = NQCE_Const.Rt(Math.Max(o, c) + r.NextDouble() * sig), l = NQCE_Const.Rt(Math.Min(o, c) - r.NextDouble() * sig);
					T.Add(d.AddMinutes(stamp).Ticks); O.Add(o); H.Add(h); L.Add(l); C.Add(c); V.Add(Math.Round(100 + 400 * r.NextDouble() * sf));
					px = c;
				}
				prev = px; d = d.AddDays(1); k++;
			}
			vol = V.ToArray();
			return new NQCE_Bars(T.ToArray(), O.ToArray(), H.ToArray(), L.ToArray(), C.ToArray());
		}

		// Huella canonica de cada sesion: offsets respecto del cierre previo propio + volumen (independiente del nivel).
		private static List<string> SessionHashes(NQCE_Bars b, double[] v)
		{
			List<NQCET2_Sess> ss = NQCET2_Core.Split(b); List<string> l = new List<string>();
			for (int k = 0; k < ss.Count; k++)
			{
				double p = NQCET2_Core.PrevClose(b, ss, k); StringBuilder sb = new StringBuilder();
				for (int i = ss[k].Start; i <= ss[k].End; i++) sb.Append(F(b.O[i] - p)).Append(',').Append(F(b.H[i] - p)).Append(',').Append(F(b.L[i] - p)).Append(',').Append(F(b.C[i] - p)).Append(',').Append(F(v[i])).Append(';');
				l.Add(NQCE_Const.Sha256Text(sb.ToString()));
			}
			return l;
		}
		private static double[] Trs(NQCE_Bars b)
		{
			double[] t = new double[b.Count]; t[0] = b.H[0] - b.L[0];
			for (int i = 1; i < b.Count; i++) t[i] = Math.Max(Math.Abs(b.L[i] - b.C[i - 1]), Math.Max(b.H[i] - b.L[i], Math.Abs(b.H[i] - b.C[i - 1])));
			return t;
		}
		private static NQCE_Result RunRules1(NQCE_Bars b, NQCE_Member m, NQCE_Config c, NQCET2_SigHook h) { List<NQCE_Member> l = new List<NQCE_Member>(); l.Add(m); return NQCET2_EdgeInjector.RunRules(b, l, c, h); }
		private static double Mean(double[] a) { double s = 0; foreach (double x in a) s += x; return s / a.Length; }
		private static bool SameT(NQCE_Bars a, NQCE_Bars b) { if (a.Count != b.Count) return false; for (int i = 0; i < a.Count; i++) if (a.T[i] != b.T[i]) return false; return true; }
		private static bool SameVol(double[] a, double[] b) { for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }

		private void RunAll()
		{
			double[] vol; int exDay, irreg; NQCE_Bars w = MakeWorld(out vol, out exDay, out irreg);
			HashSet<int> excl = new HashSet<int>(); excl.Add(exDay);
			NQCE_Config cfg = new NQCE_Config(); cfg.Excluded = excl;       // sin noticias, sin capa protectora, costo 24,68
			string h0 = NQCET2_Core.Hash(w, vol);
			Chk("S0 mundo valido: 500 sesiones, 61.999 barras, costo 24,68", NQCET2_Core.Validate(w) == "" && NQCET2_Core.Split(w).Count == 500 && w.Count == 499 * 124 + 123 && Math.Abs(cfg.Cost() - 24.68) < 1e-12, "barras=" + w.Count + " hash=" + h0);

			// ---------------- N1
			NQCET2_N1 n1 = new NQCET2_N1(20260001, excl); NQCE_Bars b1; double[] v1; n1.Apply(w, vol, out b1, out v1);
			NQCET2_N1 n1b = new NQCET2_N1(20260001, excl); NQCE_Bars b1b; double[] v1b; n1b.Apply(w, vol, out b1b, out v1b);
			NQCET2_N1 n1c = new NQCET2_N1(20260002, excl); NQCE_Bars b1c; double[] v1c; n1c.Apply(w, vol, out b1c, out v1c);
			string hN1 = NQCET2_Core.Hash(b1, v1), hN1b = NQCET2_Core.Hash(b1b, v1b), hN1c = NQCET2_Core.Hash(b1c, v1c);
			Chk("N1a barras validas (OHLC, rejilla de tick), calendario (T) identico", NQCET2_Core.Validate(b1) == "" && SameT(w, b1), "valida=" + NQCET2_Core.Validate(b1));
			List<string> sa = SessionHashes(w, vol), sb2 = SessionHashes(b1, v1); List<string> sas = new List<string>(sa), sbs = new List<string>(sb2); sas.Sort(StringComparer.Ordinal); sbs.Sort(StringComparer.Ordinal);
			bool multi = sas.Count == sbs.Count; for (int i = 0; multi && i < sas.Count; i++) if (sas[i] != sbs[i]) multi = false;
			Chk("N1b conserva el multiconjunto de sesiones (forma + hueco + volumen)", multi, "sesiones=" + sa.Count);
			int changed = 0; for (int i = 0; i < sa.Count; i++) if (sa[i] != sb2[i]) changed++;
			bool fixedOk = sa[0] == sb2[0] && sa[irreg] == sb2[irreg] && sa[50] == sb2[50];
			Chk("N1c cambia el orden (>=95% de las ranuras libres) y deja fijas: ranura 0, dia excluido, sesion de firma unica", changed >= 0.95 * (sa.Count - 3) && fixedOk && n1.MovedSlots > 0, "movidas=" + n1.MovedSlots + "/" + n1.FreeSlots + " clases=" + n1.Classes + " cambiadas=" + changed);
			Chk("N1d determinista: misma semilla = mismo hash; otra semilla = otro", hN1 == hN1b && hN1 != hN1c && hN1 != h0, "N1(sem 20260001)=" + hN1 + " repetido=" + hN1b + " N1(sem 20260002)=" + hN1c);
			double[] tr0 = Trs(w), tr1 = Trs(b1); double[] s0 = (double[])tr0.Clone(), s1 = (double[])tr1.Clone(); Array.Sort(s0); Array.Sort(s1);
			double dmax = 0; for (int i = 0; i < s0.Length; i++) dmax = Math.Max(dmax, Math.Abs(s0[i] - s1[i]));
			double a0 = Mean(w.Atr(14)), a1 = Mean(b1.Atr(14));
			Chk("N1e empalme: el multiconjunto de TR es identico (sin saltos nuevos); ATR(14) medio casi igual; cierre final identico", dmax == 0 && Math.Abs(a1 / a0 - 1) < 0.01 && w.C[w.Count - 1] == b1.C[b1.Count - 1], "maxdifTR=" + F(dmax) + " ATR14 base=" + F2(a0) + " N1=" + F2(a1) + " cierre=" + F2(w.C[w.Count - 1]));
			Chk("N1f volumen viaja con la sesion (suma total igual, permutado)", Math.Abs(Mean(vol) - Mean(v1)) < 1e-9 && !SameVol(vol, v1), "media=" + F2(Mean(v1)));

			// ---------------- N2
			NQCET2_N2 n2 = new NQCET2_N2(20260101); NQCE_Bars b2; double[] v2; n2.Apply(w, vol, out b2, out v2);
			NQCET2_N2 n2b = new NQCET2_N2(20260101); NQCE_Bars b2b; double[] v2b; n2b.Apply(w, vol, out b2b, out v2b);
			NQCET2_N2 n2c = new NQCET2_N2(20260102); NQCE_Bars b2c; double[] v2c; n2c.Apply(w, vol, out b2c, out v2c);
			string hN2 = NQCET2_Core.Hash(b2, v2), hN2b = NQCET2_Core.Hash(b2b, v2b), hN2c = NQCET2_Core.Hash(b2c, v2c);
			Chk("N2a barras validas, calendario y volumen identicos", NQCET2_Core.Validate(b2) == "" && SameT(w, b2) && SameVol(vol, v2), "valida=" + NQCET2_Core.Validate(b2));
			double rmax = 0, cmax = 0; for (int i = 0; i < w.Count; i++) { rmax = Math.Max(rmax, Math.Abs((w.H[i] - w.L[i]) - (b2.H[i] - b2.L[i]))); if (i > 0) cmax = Math.Max(cmax, Math.Abs(Math.Abs(w.C[i] - w.C[i - 1]) - Math.Abs(b2.C[i] - b2.C[i - 1]))); }
			double[] tr2 = Trs(b2); double tmax = 0; for (int i = 0; i < tr2.Length; i++) tmax = Math.Max(tmax, Math.Abs(tr2[i] - tr0[i]));
			double amax = 0; double[] aa = w.Atr(14), ab = b2.Atr(14); for (int i = 0; i < aa.Length; i++) amax = Math.Max(amax, Math.Abs(aa[i] - ab[i]));
			Chk("N2b conserva volatilidad y forma: rango de cada barra, |retorno cierre-a-cierre|, TR y ATR(14) identicos", rmax == 0 && cmax == 0 && tmax == 0 && amax < 1e-9, "max dif rango=" + F(rmax) + " |ret|=" + F(cmax) + " TR=" + F(tmax) + " ATR=" + F(amax));
			List<NQCET2_Sess> ss = NQCET2_Core.Split(w); int okInv = 0, flips = 0; double sumIn = 0, sumOut = 0;
			for (int k = 0; k < ss.Count; k++)
			{
				double netIn = w.C[ss[k].End] - NQCET2_Core.PrevClose(w, ss, k), netOut = b2.C[ss[k].End] - NQCET2_Core.PrevClose(b2, ss, k); sumIn += netIn; sumOut += netOut;
				if (netOut == netIn) okInv++; else if (netOut == -netIn) { okInv++; flips++; }
			}
			Chk("N2c invierte la direccion: cada sesion conserva o invierte exactamente su cambio de cierre previo a cierre; ~mitad invertidas", okInv == ss.Count && n2.Flipped >= flips && n2.Flipped - flips <= 3 && flips > 200 && flips < 300, "invertidas=" + flips + "/" + ss.Count + " deriva total base=" + F2(sumIn) + " N2=" + F2(sumOut));
			Chk("N2d determinista: misma semilla = mismo hash; otra semilla = otro", hN2 == hN2b && hN2 != hN2c && hN2 != h0, "N2(sem 20260101)=" + hN2 + " repetido=" + hN2b + " N2(sem 20260102)=" + hN2c);

			// ---------------- Inyector
			double rate0; List<NQCE_Member> rule = NQCET2_EdgeInjector.ChooseRules(b1, cfg, out rate0);
			NQCE_Result rBase = NQCET2_EdgeInjector.RunRules(b1, rule, cfg, null);
			NQCET2_InjectedN1 inj = new NQCET2_InjectedN1(20260001, cfg, NQCET2_EdgeInjector.RefNetPerTrade, 2.0); NQCE_Bars bi; double[] vi; inj.Apply(w, vol, out bi, out vi);
			NQCET2_InjectedN1 injb = new NQCET2_InjectedN1(20260001, cfg, NQCET2_EdgeInjector.RefNetPerTrade, 2.0); NQCE_Bars bib; double[] vib; injb.Apply(w, vol, out bib, out vib);
			string hI = NQCET2_Core.Hash(bi, vi), hIb = NQCET2_Core.Hash(bib, vib);
			NQCE_Result rInj = NQCET2_EdgeInjector.RunRules(bi, inj.Rules, cfg, null);      // medicion independiente con el ejecutor central
			double mu = NQCET2_EdgeInjector.MeanNet(rInj), muBase = NQCET2_EdgeInjector.MeanNet(rBase), rate = (double)rInj.Trades.Count / rInj.Sessions.Count;
			Chk("I1 el par espejo (candidato mas cercano a 0,5 ops/sesion en el N1 sin inyectar) tiene 0,3..0,8 ops/sesion conjuntas", rate0 >= 0.3 && rate0 <= 0.8 && rate >= 0.3 && rate <= 0.8, "par=" + new NQCET2_EdgeInjector(rule, cfg, 0).RulesText + " tasa N1=" + F2(rate0) + " tasa inyectado=" + F2(rate));
			Chk("I2 expectativa neta del par medida por NQCE_Executor (costo 24,68) = +US$20 +-3 en el inyectado (la del N1 sin inyectar es mucho menor)", Math.Abs(mu - 20.0) <= 3.0 && muBase < mu - 15, "neto inyectado=" + F2(mu) + " base N1=" + F2(muBase) + " ops=" + rInj.Trades.Count + " a(ATR)=" + F(inj.A) + " t_regla=" + F2(inj.TPair));
			Chk("I3 el punto fijo converge y las barras siguen siendo validas (OHLC, rejilla, T)", inj.Converged && inj.Ok && NQCET2_Core.Validate(bi) == "" && SameT(w, bi), "valida=" + NQCET2_Core.Validate(bi) + " params=" + inj.ParamsText);

			// causalidad: solo cambian las barras posteriores a la senal ejecutada, dentro de su sesion, y solo hacia el lado de la regla
			NQCET2_SigHook hk = new NQCET2_SigHook(); NQCET2_EdgeInjector.RunRules(bi, inj.Rules, cfg, hk);
			HashSet<int> sigIdx = new HashSet<int>(); Dictionary<int, int> sigDir = new Dictionary<int, int>(); for (int q = 0; q < hk.Ticks.Count; q++) { int ix0 = Array.BinarySearch(bi.T, hk.Ticks[q]); sigIdx.Add(ix0); sigDir[ix0] = hk.Members[q].StartsWith("INJ_L") ? 1 : -1; }
			List<NQCET2_Sess> sI = NQCET2_Core.Split(b1); bool causal = true, after = false; int nChanged = 0, nSess = 0;
			foreach (NQCET2_Sess s in sI)
			{
				int sg = -1, nIn = 0, dsg = 0; foreach (int ix in sigIdx) if (ix >= s.Start && ix <= s.End) { nIn++; if (sg < 0 || ix < sg) { sg = ix; dsg = sigDir[ix]; } }
				for (int j = s.Start; j <= s.End; j++)
				{
					double dif = bi.C[j] - b1.C[j], difO = bi.O[j] - b1.O[j], difH = bi.H[j] - b1.H[j], difL = bi.L[j] - b1.L[j];
					bool same = dif == 0 && difO == 0 && difH == 0 && difL == 0;
					if (sg < 0 || j <= sg) { if (!same) causal = false; }
					else { if (difO != dif || difH != dif || difL != dif || (nIn == 1 && dif * dsg < 0)) causal = false; if (!same) after = true; }
				}
				if (sg >= 0) { nSess++; for (int j = sg + 1; j <= s.End; j++) if (bi.C[j] != b1.C[j]) { nChanged++; break; } }
			}
			Chk("I4 causal: barras <= senal y sesiones sin senal intactas; despues de la senal solo se suma deriva (signo de la regla) igual a O,H,L,C; sin persistencia entre sesiones", causal && after && nChanged == nSess && nSess <= rInj.Trades.Count && nSess >= rInj.Trades.Count - 5, "sesiones con senal=" + nSess + " con cambios=" + nChanged);
			double aRatio = Mean(bi.Atr(14)) / Mean(b1.Atr(14));
			NQCE_Member allLong = new NQCE_Member(); allLong.Id = "ALL_L"; allLong.Dir = 1; allLong.Conditions.Add(new NQCE_CondStamp(NQCE_Const.StampFirstEntry)); allLong.AtrPeriod = 14; allLong.Ksl = 6.0; allLong.MaxPerDay = 1;
			NQCE_Member allShort = new NQCE_Member(); allShort.Id = "ALL_S"; allShort.Dir = -1; allShort.Conditions.Add(new NQCE_CondStamp(NQCE_Const.StampFirstEntry)); allShort.AtrPeriod = 14; allShort.Ksl = 6.0; allShort.MaxPerDay = 1;
			double lB = NQCET2_EdgeInjector.MeanNet(RunRules1(b1, allLong, cfg, null)), lI = NQCET2_EdgeInjector.MeanNet(RunRules1(bi, allLong, cfg, null));
			double sB = NQCET2_EdgeInjector.MeanNet(RunRules1(b1, allShort, cfg, null)), sI2 = NQCET2_EdgeInjector.MeanNet(RunRules1(bi, allShort, cfg, null));
			Chk("I5 efecto en otras reglas: ATR(14) medio +-1%; exposicion NO condicional neutra: 'siempre largo' y 'siempre corto' (09:31-11:30, stop ancho) cambian < US$15 por operacion", Math.Abs(aRatio - 1) < 0.01 && Math.Abs(lI - lB) < 15.0 && Math.Abs(sI2 - sB) < 15.0, "ATR ratio=" + F(aRatio) + " largo base=" + F2(lB) + " largo inyectado=" + F2(lI) + " corto base=" + F2(sB) + " corto inyectado=" + F2(sI2));
			// I7 curva de edges (D-040): +60 y +100 con tolerancia por nivel; barras validas, causalidad por construccion (mismo Build), ATR y exposicion no condicional
			bool curveOk = true; string curveInfo = "";
			foreach (double tg in new double[] { 60.0, 100.0 })
			{
				double tl = NQCET2_EdgeInjector.TolFor(tg); NQCET2_InjectedN1 ic = new NQCET2_InjectedN1(20284101, cfg, tg, tl); NQCE_Bars bc; double[] vc; ic.Apply(w, vol, out bc, out vc);
				NQCE_Result rc = NQCET2_EdgeInjector.RunRules(bc, ic.Rules, cfg, null); double muc = NQCET2_EdgeInjector.MeanNet(rc);
				NQCET2_N1 nn = new NQCET2_N1(20284101, excl); NQCE_Bars bb; double[] vb; nn.Apply(w, vol, out bb, out vb);
				double ar = Mean(bc.Atr(14)) / Mean(bb.Atr(14)); double l2 = NQCET2_EdgeInjector.MeanNet(RunRules1(bc, allLong, cfg, null)), s2 = NQCET2_EdgeInjector.MeanNet(RunRules1(bc, allShort, cfg, null));
				double lb2 = NQCET2_EdgeInjector.MeanNet(RunRules1(bb, allLong, cfg, null)), sb3 = NQCET2_EdgeInjector.MeanNet(RunRules1(bb, allShort, cfg, null));
				bool okc = ic.Ok && ic.Converged && NQCET2_Core.Validate(bc) == "" && SameT(w, bc) && Math.Abs(muc - tg) <= tl && Math.Abs(ar - 1) < 0.01 && (l2 - lb2) < 0.35 * tg + 15 && (s2 - sb3) < 0.35 * tg + 15 && ic.A < NQCET2_EdgeInjector.AMax / 2;
				if (!okc) curveOk = false;
				curveInfo += "[+" + F2(tg) + " tol " + F2(tl) + ": neto=" + F2(muc) + " a=" + F(ic.A) + " t_par=" + F2(ic.TPair) + " ops=" + rc.Trades.Count + " ATR=" + F(ar) + " siempre-largo " + F2(lb2) + "->" + F2(l2) + " siempre-corto " + F2(sb3) + "->" + F2(s2) + "] ";
			}
			Chk("I7 curva de edges: +60 (+-5) y +100 (+-8) convergen por biseccion, barras validas, ATR +-1%, deriva a < AMax/2, el sesgo no condicional (siempre largo/corto) crece < 35% del edge + 15", curveOk, curveInfo);
			Chk("I6 determinista: dos corridas completas (N1 -> regla -> calibrar -> inyectar) dan el mismo hash y los mismos parametros", hI == hIb && inj.ParamsText == injb.ParamsText && hI != hN1, "INJ=" + hI + " repetido=" + hIb);

			// ---------------- Estadistica de calibracion
			double lo, hi;
			NQCET2_Stats.ClopperPearson(0, 5, 0.05, out lo, out hi); bool c1 = Math.Abs(lo) < 1e-9 && Math.Abs(hi - (1 - Math.Pow(0.025, 0.2))) < 1e-6;
			double lo2, hi2; NQCET2_Stats.ClopperPearson(8, 10, 0.05, out lo2, out hi2); bool c2 = Math.Abs(lo2 - 0.4439) < 5e-4 && Math.Abs(hi2 - 0.9748) < 5e-4;
			double lo3, hi3; NQCET2_Stats.ClopperPearson(10, 10, 0.05, out lo3, out hi3);
			Chk("C1 Clopper-Pearson exacto: 0/5 -> (0; 0,5218); 8/10 -> (0,4439; 0,9748); 10/10 -> inferior 0,6915", c1 && c2 && Math.Abs(lo3 - Math.Pow(0.025, 0.1)) < 1e-6, "0/5=(" + F(lo) + "; " + F(hi) + ") 8/10=(" + F(lo2) + "; " + F(hi2) + ") 10/10 inf=" + F(lo3));
			Chk("C2 resolucion de FP: 0 FP en n campanas -> cota unilateral 95%: n=5 -> 45,1%; n=20 -> 13,9%; hacen falta 59 para demostrar <=5%", Math.Abs(NQCET2_Stats.UpperOneSided(0, 5, 0.05) - 0.4507) < 5e-4 && Math.Abs(NQCET2_Stats.UpperOneSided(0, 20, 0.05) - 0.1391) < 5e-4 && NQCET2_Stats.MinNForZeroFp(0.05, 0.05) == 59, "n5=" + F(NQCET2_Stats.UpperOneSided(0, 5, 0.05)) + " n20=" + F(NQCET2_Stats.UpperOneSided(0, 20, 0.05)) + " nmin=" + NQCET2_Stats.MinNForZeroFp(0.05, 0.05));
			double[] nul = new double[20]; for (int i = 0; i < 20; i++) nul[i] = i; double[] injv = new double[] { 25, 30, 15, 5, 40 };
			double fp, pw; double tau = NQCET2_Stats.ChooseThreshold(nul, injv, 0.05, out fp, out pw);
			Chk("C3 percentil nulo (D-036: nulo < x, n>=20) y eleccion de umbral con FP<=5% y su potencia", NQCET2_Stats.NullPercentile(nul, 19.5) == 100.0 && NQCET2_Stats.NullPercentile(nul, 10) == 50.0 && double.IsNaN(NQCET2_Stats.NullPercentile(new double[19], 1)) && tau == 19 && fp == 0.05 && pw == 0.6 && NQCET2_Stats.Quantile(nul, 0.95) == 18, "tau=" + F(tau) + " fp=" + F(fp) + " potencia=" + F(pw));
			NQCET2_CostModel cm = new NQCET2_CostModel();
			Chk("C4 estimador de costo (supuestos declarados, a medir en el paso 0 del runbook)", cm.CampaignMs() > 0, cm.Table(20, 10));

			string text = rep.ToString() + "\nresultado: " + pass + " PASS / " + fail + " FALLA\n";
			text += "\nHASHES\nmundo_sintetico=" + h0 + "\nN1_sem20260001=" + hN1 + "\nN2_sem20260101=" + hN2 + "\nINJ_sem20260001=" + hI + "\nregla_inyectada=" + inj.RuleText + "\nparams_inyector=" + inj.ParamsText + "\n";
			string outDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NQC_Results", "ET2DATASETTEST_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
			Directory.CreateDirectory(outDir); UTF8Encoding enc = new UTF8Encoding(false);
			File.WriteAllText(Path.Combine(outDir, "DATASET_TEST_REPORT.txt"), text, enc);
			File.WriteAllText(Path.Combine(outDir, "SUMMARY.txt"), "NQC ET2 DATASET TEST - SUMMARY\nscript_version : " + ScriptVersion + "\ncore : " + NQCET2_Core.Version + "\ncontract : " + NQCE_Const.ContractVersion + "\nresultado : " + pass + " PASS / " + fail + " FALLA\nreport_sha256 : " + NQCE_Const.Sha256Text(text) + "\nmundo_sha256 : " + h0 + "\nN1_sha256 : " + hN1 + "\nN2_sha256 : " + hN2 + "\nINJ_sha256 : " + hI + "\n", enc);
			Print(text);
		}
	}
}
