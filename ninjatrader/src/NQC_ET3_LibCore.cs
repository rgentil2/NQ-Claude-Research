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
	public sealed class NQCE3_Series
	{
		public double[] O, H, L, C, V; public int Count; public bool HasVolume;
		public int[] DayStart;             // indice de la primera barra del dia calendario de cada barra
		public NQCE_Bars Src;
		public const int MaxMemoSeries = 300;       // tope de series (familia,periodo) en memoria; al superarlo se vacia
		private int[] pos; private int winCount;
		private Dictionary<int, double[]> memo = new Dictionary<int, double[]>();
		private const double Unset = double.NaN, NonFinite = double.MaxValue;

		public NQCE3_Series(NQCE_Bars b, double[] volume)
		{
			Src = b; O = b.O; H = b.H; L = b.L; C = b.C; Count = b.Count;
			if (volume != null && volume.Length != Count) throw new ArgumentException("volumen: longitud distinta de las barras");
			V = volume; HasVolume = volume != null;
			DayStart = new int[Count]; pos = new int[Count]; winCount = 0;
			for (int i = 0; i < Count; i++)
			{
				DayStart[i] = (i > 0 && b.DayKey[i] == b.DayKey[i - 1]) ? DayStart[i - 1] : i;
				int st = b.Stamp[i];
				if (st >= NQCE_Const.StampLoadFrom && st <= NQCE_Const.StampLoadTo) pos[i] = winCount++; else pos[i] = -1;
			}
		}

		public NQCE3_Bar Bar(int k)
		{
			NQCE3_Bar r; r.Open = O[k]; r.High = H[k]; r.Low = L[k]; r.Close = C[k]; r.Volume = (V == null) ? 0.0 : V[k];
			return r;
		}

		// Valor de la familia f con periodo n en la barra at (memorizado si at cae en la ventana de carga).
		// Devuelve NaN si el valor no es finito.
		public double Feature(int f, int n, int at)
		{
			int p = pos[at];
			if (p < 0) return Clean(NQCE3_Features.Compute(this, at, f, n));
			int key = f * 64 + n; double[] arr;
			if (!memo.TryGetValue(key, out arr))
			{
				if (memo.Count >= MaxMemoSeries) memo.Clear();
				arr = new double[winCount]; for (int i = 0; i < winCount; i++) arr[i] = Unset;
				memo[key] = arr;
			}
			double v = arr[p];
			if (double.IsNaN(v)) { v = Clean(NQCE3_Features.Compute(this, at, f, n)); arr[p] = double.IsNaN(v) ? NonFinite : v; }
			else if (v == NonFinite) v = double.NaN;
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

	// Las 11 reglas direccionales (identicas a la referencia). Umbrales 0,15 / 0,35 / 0,60 en unidades DE LA
	// FAMILIA (R5: son multiplos de ATR solo en las familias de clase A; ver NQCE3_Catalog.UnitClass).
	public static class NQCE3_Rules
	{
		public const int Count = 11;
		public static readonly string[] Names = new string[] {
			"Positive/negative", "Rising/falling", "Zero cross", "Threshold 0.15", "Threshold 0.35", "Threshold 0.60",
			"Threshold 0.15 cross", "Threshold 0.35 cross", "Reversion from +/-0.60", "Positive/negative acceleration",
			"Two-bar directional persistence" };

		// Devuelve 1 (alcista), -1 (bajista), 0 (ninguna) o -2 (no finito / ambiguo: sin senal).
		public static int Resolve(double x, double y, double z, int rule)
		{
			if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z) || double.IsInfinity(x) || double.IsInfinity(y) || double.IsInfinity(z) || rule < 0 || rule >= Count) return -2;
			bool bull = false, bear = false;
			switch (rule)
			{
				case 0: bull = x > 0; bear = x < 0; break;
				case 1: bull = x > y; bear = x < y; break;
				case 2: bull = y <= 0 && x > 0; bear = y >= 0 && x < 0; break;
				case 3: bull = x > 0.15; bear = x < -0.15; break;
				case 4: bull = x > 0.35; bear = x < -0.35; break;
				case 5: bull = x > 0.60; bear = x < -0.60; break;
				case 6: bull = y <= 0.15 && x > 0.15; bear = y >= -0.15 && x < -0.15; break;
				case 7: bull = y <= 0.35 && x > 0.35; bear = y >= -0.35 && x < -0.35; break;
				case 8: bull = y < -0.60 && x >= -0.60; bear = y > 0.60 && x <= 0.60; break;
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

		// Auditoria R5 de la unidad de cada familia (120 caracteres, una por familia):
		//  A = multiplos de ATR (cumple); B = razon adimensional acotada/autonormalizada, sin nivel ni porcentaje de precio (cumple;
		//      el umbral es una fraccion, no un multiplo de ATR literal);
		//  E = dudosa: se calcula con retornos/porcentajes pero el resultado final es una razon o equivale a ATR;
		//  C = INCUMPLE: unidades de porcentaje/retorno de precio (la serie es back-adjusted);  D = INCUMPLE: escala mixta/arbitraria.
		public const string UnitClass =
			"AAAABBABABBB" +	// 0..11
			"AAAAABBBADBB" +	// 12..23
			"BBBBBAAAEBBB" +	// 24..35
			"AAAAAABAAAAA" +	// 36..47
			"AAAAAAAAABBB" +	// 48..59
			"EBBCBCCCBBBB" +	// 60..71
			"BBBCBAAAAAAC" +	// 72..83
			"AECCCCCBCAAA" +	// 84..95
			"AAABCCCECCBB" +	// 96..107
			"BABABABBAADB";	// 108..119

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
