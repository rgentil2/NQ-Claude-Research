#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
#endregion

//==============================================================================
// NQC_ET3_LibCore - ET3 WP1: serie OHLCV de la biblioteca D-018, memoria de caracteristicas,
// catalogo (nombres, clase de unidades R5, dependencia de volumen) y las 11 reglas.
// SOLO DESARROLLO, sin compilar. Procedencia y hashes: cabecera de NQC_ET3_Library.cs.
//
// Politica de calentamiento (hallazgo ET3-WP1 d): las formulas usan recursiones de hasta 7n+6 barras
// hacia atras (familias 31 y 69; la referencia historica exigia solo 3n+22 y recortaba la recursion al
// inicio de la lista, sin avisar). Aqui RequiredBars(n) = 7n+10 y, en modo SameDay (defecto), TODAS
// esas barras deben pertenecer al mismo dia calendario que la barra de senal; si no, la condicion es
// falsa (sin senal). Modo AnyHistory: solo exige que existan 7n+10 barras previas en la serie.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public struct NQCE3_Bar
	{
		public double Open, High, Low, Close, Volume;
	}

	// Serie OHLCV (arreglos de NQCE_Bars + volumen opcional). El volumen NO esta en NQCE_Bars (ET1 Core).
	// Memoria: series (familia,periodo) y MACD auxiliares en una cache con presupuesto en bytes y expulsion LRU.
	// El umbral (D-030) NO forma parte de la clave: se compara contra la MISMA serie de la familia.
	public sealed class NQCE3_Series
	{
		public double[] O, H, L, C, V; public int Count; public bool HasVolume;
		public int[] DayStart;             // indice de la primera barra del dia calendario de cada barra
		public NQCE_Bars Src;
		public static long MemoBudgetBytes = 256L * 1024 * 1024;   // presupuesto de la cache por serie de barras
		private sealed class Entry { public double[] A; public long Used; }
		private int[] pos; private int winCount; private long tick;
		private Dictionary<int, Entry> memo = new Dictionary<int, Entry>();
		private static readonly double NonFiniteMark = double.NegativeInfinity;   // Clean() nunca devuelve infinitos: marca "calculado, no finito"

		public NQCE3_Series(NQCE_Bars b, double[] volume)
		{
			Src = b; O = b.O; H = b.H; L = b.L; C = b.C; Count = b.Count;
			if (volume != null && volume.Length != Count) throw new ArgumentException("volumen: longitud distinta de las barras");
			for (int i = 0; i < Count; i++)
				if (!Fin(O[i]) || !Fin(H[i]) || !Fin(L[i]) || !Fin(C[i]) || (volume != null && (!Fin(volume[i]) || volume[i] < 0)))
					throw new ArgumentException("barra " + i.ToString(CultureInfo.InvariantCulture) + ": valor no finito (OHLC) o volumen negativo/no finito");
			V = volume; HasVolume = volume != null;
			DayStart = new int[Count]; pos = new int[Count]; winCount = 0;
			for (int i = 0; i < Count; i++)
			{
				DayStart[i] = (i > 0 && b.DayKey[i] == b.DayKey[i - 1]) ? DayStart[i - 1] : i;
				int st = b.Stamp[i];
				if (st >= NQCE_Const.StampLoadFrom && st <= NQCE_Const.StampLoadTo) pos[i] = winCount++; else pos[i] = -1;
			}
		}
		private static bool Fin(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }

		public NQCE3_Bar Bar(int k)
		{
			NQCE3_Bar r; r.Open = O[k]; r.High = H[k]; r.Low = L[k]; r.Close = C[k]; r.Volume = (V == null) ? 0.0 : V[k];
			return r;
		}

		private double[] GetArr(int key)
		{
			Entry e;
			if (memo.TryGetValue(key, out e)) { e.Used = ++tick; return e.A; }
			long each = 8L * Math.Max(1, winCount);
			while (memo.Count > 0 && (memo.Count + 1) * each > MemoBudgetBytes)
			{
				int lru = 0; long best = long.MaxValue;
				foreach (KeyValuePair<int, Entry> kv in memo) if (kv.Value.Used < best) { best = kv.Value.Used; lru = kv.Key; }
				memo.Remove(lru);                                  // LRU: se expulsa la menos usada, no se vacia todo
			}
			e = new Entry(); e.A = new double[winCount]; for (int i = 0; i < winCount; i++) e.A[i] = double.NaN;
			e.Used = ++tick; memo[key] = e; return e.A;
		}
		public int MemoCount { get { return memo.Count; } }

		// Valor de la familia f con periodo n en la barra at (memorizado si at cae en la ventana de carga).
		// Devuelve NaN si el valor no es finito.
		public double Feature(int f, int n, int at)
		{
			int p = pos[at];
			if (p < 0) return Clean(NQCE3_Features.Compute(this, at, f, n));
			double[] arr = GetArr(f * 64 + n);
			double v = arr[p];
			if (double.IsNaN(v)) { v = Clean(NQCE3_Features.Compute(this, at, f, n)); arr[p] = double.IsNaN(v) ? NonFiniteMark : v; }
			else if (double.IsNegativeInfinity(v)) v = double.NaN;
			return v;
		}

		// MACD EMA(fast)-EMA(slow) de cierres, MISMOS valores que el calculo directo (ventana fija 3n+5 por barra),
		// pero memorizado por barra: evita recalcularlo ~n*n/2 veces en las familias 31 y 69.
		public double MacdMemo(int fast, int slow, int at)
		{
			int p = pos[at];
			if (p < 0) return NQCE3_M.EMA(this, at, fast) - NQCE3_M.EMA(this, at, slow);
			double[] arr = GetArr(1000000 + fast * 1000 + slow);
			double v = arr[p];
			if (double.IsNaN(v)) { v = NQCE3_M.EMA(this, at, fast) - NQCE3_M.EMA(this, at, slow); arr[p] = v; }
			return v;
		}
		private static double Clean(double x) { return (double.IsNaN(x) || double.IsInfinity(x)) ? double.NaN : x; }
	}

	public static class NQCE3_Features
	{
		public static double Compute(NQCE3_Series s, int at, int f, int n)
		{
			if (f < 0 || f >= NQCE3_Catalog.Families) throw new ArgumentOutOfRangeException("family");
			if (f < 12) return NQCE3_FeatBase.Compute(s, at, f, n);
			if (f < 36) return NQCE3_FeatExt.Compute(s, at, f, n);
			if (f < 57) return NQCE3_FeatTrend.Compute(s, at, n, f);
			if (f < 78) return NQCE3_FeatMom.Compute(s, at, n, f);
			if (f < 99) return NQCE3_FeatVola.Compute(s, at, n, f);
			return NQCE3_FeatVolAct.Compute(s, at, n, f);
		}
	}

	// Las 11 reglas direccionales. D-030: el umbral de las reglas 3-8 es un GEN (rejilla en multiplos de ATR).
	// Rejilla (paso 0,05 ATR; tres bandas disjuntas para que 3/4/5 (y 6/7/8) no sean la misma condicion):
	//   reglas 3 y 6 ("bajo")  : 0,05 .. 0,25  (5 niveles; contiene el 0,15 historico)
	//   reglas 4 y 7 ("medio") : 0,30 .. 0,50  (5 niveles; contiene el 0,35 historico)
	//   reglas 5 y 8 ("alto")  : 0,55 .. 1,00  (10 niveles; contiene el 0,60 historico)
	//   reglas 0,1,2,9,10 no usan umbral (1 nivel).  Combinaciones de umbral por (familia,periodo) = 45.
	public static class NQCE3_Rules
	{
		public const int Count = 11;
		public const double ThrStep = 0.05;
		public static readonly string[] Names = new string[] {
			"Positive/negative", "Rising/falling", "Zero cross", "Threshold low (0.05-0.25 ATR)", "Threshold mid (0.30-0.50 ATR)", "Threshold high (0.55-1.00 ATR)",
			"Threshold low cross", "Threshold mid cross", "Reversion from +/-high", "Positive/negative acceleration",
			"Two-bar directional persistence" };

		public static int ThresholdCount(int rule)
		{
			switch (rule) { case 3: case 6: return 5; case 4: case 7: return 5; case 5: case 8: return 10; default: return 1; }
		}
		public static double ThresholdFirst(int rule)
		{
			switch (rule) { case 3: case 6: return 0.05; case 4: case 7: return 0.30; case 5: case 8: return 0.55; default: return double.NaN; }
		}
		// Valor del umbral (multiplos de ATR) del nivel idx; NaN si la regla no usa umbral.
		public static double ThresholdValue(int rule, int idx)
		{
			if (ThresholdCount(rule) <= 1) return double.NaN;
			return Math.Round(ThresholdFirst(rule) + ThrStep * idx, 2);
		}
		// Umbral historico (0,15/0,35/0,60), usado por la sobrecarga sin umbral y por las pruebas.
		public static double DefaultThreshold(int rule)
		{
			switch (rule) { case 3: case 6: return 0.15; case 4: case 7: return 0.35; case 5: case 8: return 0.60; default: return double.NaN; }
		}

		public static int Resolve(double x, double y, double z, int rule) { return Resolve(x, y, z, rule, DefaultThreshold(rule)); }

		// Devuelve 1 (alcista), -1 (bajista), 0 (ninguna) o -2 (no finito / ambiguo: sin senal). thr en multiplos de ATR.
		public static int Resolve(double x, double y, double z, int rule, double thr)
		{
			if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z) || double.IsInfinity(x) || double.IsInfinity(y) || double.IsInfinity(z) || rule < 0 || rule >= Count) return -2;
			bool bull = false, bear = false;
			switch (rule)
			{
				case 0: bull = x > 0; bear = x < 0; break;
				case 1: bull = x > y; bear = x < y; break;
				case 2: bull = y <= 0 && x > 0; bear = y >= 0 && x < 0; break;
				case 3: case 4: case 5: bull = x > thr; bear = x < -thr; break;
				case 6: case 7: bull = y <= thr && x > thr; bear = y >= -thr && x < -thr; break;
				case 8: bull = y < -thr && x >= -thr; bear = y > thr && x <= thr; break;
				case 9: bull = x - 2 * y + z > 1e-9; bear = x - 2 * y + z < -1e-9; break;
				case 10: bull = x > 0 && y > 0; bear = x < 0 && y < 0; break;
			}
			if (bull && bear) return -2;
			return bull ? 1 : bear ? -1 : 0;
		}
	}

	public static class NQCE3_Catalog
	{
		public const int Families = 120, Rules = 11, Count = Families * Rules;   // 1.320
		public const int MinPeriod = 4, MaxPeriod = 30, PeriodLevels = MaxPeriod - MinPeriod + 1;   // 27
		// niveles de umbral por condicion = ThresholdCount(regla); parametros por (familia,periodo): 45 combinaciones de umbral

		public static int RequiredBars(int n) { return 7 * n + 10; }

		public static readonly string[] FamilyNames = new string[] {
			"Close minus SMA / ATR", "Close minus EMA / ATR", "SMA fast minus EMA slow / ATR", "Close minus WMA / ATR",
			"Wilder RSI centered", "CCI scaled", "ATR-normalized ROC", "Close stochastic position", "EMA MACD / ATR",
			"Bollinger position z", "Money flow index centered", "Volume-weighted close location",
			"Least-squares price slope / ATR", "Rolling VWAP displacement / ATR", "Hull moving-average displacement / ATR",
			"Zero-lag EMA displacement / ATR", "Double EMA displacement / ATR", "Signed on-balance volume flow", "Chaikin money flow",
			"Accumulation/distribution line momentum", "Elder force-index / ATR and volume", "Ease-of-movement scaled pressure",
			"Wilder directional movement +DI minus -DI", "Directional movement weighted by local DX (ADX proxy)", "Aroon oscillator",
			"Vortex oscillator", "Signed Kaufman efficiency ratio", "Chande momentum oscillator", "True strength index",
			"Triple exponential average rate-of-change", "Detrended price oscillator / ATR", "MACD histogram / ATR",
			"Volume price trend momentum / ATR", "Fisher transformed stochastic position", "Relative vigor index",
			"Directional range-expansion impulse",
			"Fast SMA versus slow SMA / ATR", "Fast EMA versus slow EMA / ATR", "Fast WMA versus slow WMA / ATR",
			"Wilder SMMA displacement / ATR", "Wilder SMMA slope / ATR", "Linear regression fitting residual / ATR",
			"Signed linear-regression R squared", "Linear-regression slope change / ATR", "Kaufman adaptive moving average displacement",
			"Fractal adaptive moving average displacement", "McGinley Dynamic displacement", "Tillson T3 displacement",
			"Triangular moving average displacement", "Median price exponential moving average", "Typical price exponential moving average",
			"Weighted-close exponential moving average", "Typical-price SMA divergence", "Sine-weighted moving average divergence",
			"VIDYA Chande adaptive moving average", "Zero-lag least squares regression average", "EMA-smoothed n-lag momentum",
			"Williams accumulation/distribution momentum", "Ultimate oscillator centered", "Stochastic RSI centered", "Connors RSI composite",
			"Relative momentum index (RMI)", "Short minus long RSI oscillator", "Percentage price oscillator (PPO)",
			"Percentage volume oscillator (PVO)", "Know Sure Thing weighted ROC", "Know Sure Thing first derivative", "TRIX acceleration",
			"Inverse Fisher transform RSI", "Schaff Trend Cycle stochastic MACD proxy", "Double-smoothed stochastic momentum index",
			"Choppiness index directional efficiency", "CCI of typical price relative EMA", "Center of gravity oscillator", "Laguerre RSI",
			"Coppock dual ROC weighted average", "Balance of Power moving average", "Elder bull versus bear power",
			"Keltner ATR channel breakout", "Donchian channel midpoint slope", "Prior Donchian channel breakout distance",
			"Donchian midpoint displacement / ATR", "ATR channel weighted EMA slope", "Bollinger bandwidth trend regime",
			"Historical standard deviation change", "Log-return volatility expansion", "Normalized ATR expansion",
			"Parkinson OHLC volatility signed trend", "Garman Klass range volatility signed trend", "Rogers Satchell OHLC volatility signed trend",
			"Yang Zhang open-close volatility signed trend", "Chaikin range volatility expansion", "Ulcer drawdown risk adjusted momentum",
			"Chande Kroll ATR stop-channel divergence", "Supertrend dynamic channel stop distance", "Ichimoku Tenkan versus Kijun spread",
			"Ichimoku current cloud span A/B spread", "DeMark rolling pivot deviation", "Bollinger Keltner squeeze momentum",
			"Signed volume moving average oscillator", "Relative volume surprise weighted ROC", "Negative Volume Index momentum",
			"Positive Volume Index momentum", "Volume Flow Indicator (VFI proxy)", "Accumulated relative-price volume flow",
			"PVT exponential smoothing divergence", "Klinger volume-force oscillator proxy", "Twiggs money-flow smoothed proxy",
			"Volume-weighted price RSI", "Volume-adjusted EMA MACD", "Up-volume minus down-volume ratio", "Volume surprise weighted price momentum",
			"Candle-body proportional volume delta proxy", "Causal Heikin Ashi candle strength", "Candle body versus wick pressure",
			"Two-candle body engulfing signal", "Inside-outside bar directional range", "NR7 narrow-range breakout",
			"Polarized fractal efficiency", "Hurst rescaled-range trend persistence" };

		// Auditoria R5 de la unidad de cada familia tras D-018b (120 caracteres, una por familia):
		//  A = magnitud en multiplos de ATR (100 familias: 53 originales + 17 C + EOM(21) corregidas + 29 de clase B reexpresadas);
		//  B = razon/indicador acotado SIN reexpresion honesta en ATR (16 familias, pendientes de decision del usuario):
		//      10,17,24,33,42,59,64,68,69,71,99,106,110,115,118,119;
		//  E = dudosa (4 familias, sin cambios; pendientes): 32,60,85,103.
		public const string UnitClass =
			"AAAAAAAAAABA"+	// 0..11
			"AAAAABAAAAAA"+	// 12..23
			"BAAAAAAAEBAA"+	// 24..35
			"AAAAAABAAAAA"+	// 36..47
			"AAAAAAAAAAAB"+	// 48..59
			"EAAABAAABBAB"+	// 60..71
			"AAAAAAAAAAAA"+	// 72..83
			"AEAAAAAAAAAA"+	// 84..95
			"AAABAAAEAABA"+	// 96..107
			"AABAAAABAABB";	// 108..119

		// 1 = la familia usa volumen (NT8 M1 lo entrega, pero NQCE_Bars de ET1 Core no lo guarda: hay que pasarlo a NQCE3_Library.Bind).
		public const string VolumeDep =
			"000000000011" +	// 0..11
			"010001111100" +	// 12..23
			"000000001000" +	// 24..35
			"000000000000" +	// 36..47
			"000000000000" +	// 48..59
			"000010000000" +	// 60..71
			"000000000000" +	// 72..83
			"000000000000" +	// 84..95
			"000111111111" +	// 96..107
			"111110000000";	// 108..119

		public static char UnitOf(int family) { return UnitClass[family]; }
		public static bool NeedsVolume(int family) { return VolumeDep[family] == '1'; }
		public static int FamilyOf(int condId) { return (condId - 1) / Rules; }
		public static int RuleOf(int condId) { return (condId - 1) % Rules; }
		public static string CondName(int condId)
		{
			return FamilyNames[FamilyOf(condId)] + " / " + NQCE3_Rules.Names[RuleOf(condId)];
		}
	}
}
