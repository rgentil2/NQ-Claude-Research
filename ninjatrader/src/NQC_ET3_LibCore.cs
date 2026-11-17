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
		// Cache de numeradores. THREAD-SAFE (NQCRUN-1.3.0): el diccionario y la expulsion LRU van bajo memoLock; los ELEMENTOS se llenan de forma perezosa
		// (NaN = no calculado) y cada fallo se resuelve bajo un candado de pool (stripe) que depende de (clave, posicion): dos hilos NUNCA calculan el mismo
		// elemento a la vez (el segundo espera y lee el valor del primero). Los valores son identicos a los del calculo directo (mismo camino, mismo orden de
		// operaciones), asi que el resultado no depende del numero de hilos ni del orden de llegada.
		internal sealed class Entry { public double[] A; public long Used; public int Key; public long Bytes; }
		private int[] pos, posAux; private int winCount, auxCount; private long tick, memoBytes;
		private readonly object memoLock = new object();
		private Dictionary<int, Entry> memo = new Dictionary<int, Entry>();
		private const int StripeCount = 4096;
		private static readonly object[] StripesF = MakeStripes(), StripesM = MakeStripes();
		private static object[] MakeStripes() { object[] a = new object[StripeCount]; for (int i = 0; i < a.Length; i++) a[i] = new object(); return a; }
		public static int MemoReserveEntries = 0;     // entradas de cache en vuelo (arreglos que un hilo usa aunque ya se hayan expulsado): con varios hilos el lote lo fija en ~10 por hilo y se descuentan del presupuesto
		public const int AuxMarginBars = 64;          // ventana de los memos auxiliares (por barra, independientes de `at`): 64 barras antes de la carga (las familias miran hasta ~2n+h atras)
		public const int PosMarginBars = 2;           // la ventana memorizada incluye 2 barras antes de la carga: la senal de 09:29/09:30 lee at-1 y at-2 (antes se calculaban sin cache)
		private static readonly double NonFiniteMark = double.NegativeInfinity;   // Clean() nunca devuelve infinitos: marca "calculado, no finito"
		// ATR de Wilder por periodo: tabla directa (lectura sin candado; la primera vez bajo memoLock). El ejecutor lee bars.Atr(p) del diccionario de NQCE_Bars:
		// el lote llama PrepareAtr(p) en el hilo principal ANTES de lanzar los hilos, asi el diccionario solo se LEE en paralelo.
		private double[][] atrTab = new double[257][];
		// estado del ATR de la llamada Direct en curso: POR HILO (antes campos de instancia: no era seguro con varios hilos)
		[ThreadStatic] private static bool tsAtrUnit; [ThreadStatic] private static double[] tsAtrArr;
		// ultima entrada MACD usada por este hilo (evita candado + diccionario en los bucles internos de las familias 31 y 69)
		[ThreadStatic] private static NQCE3_Series tsMacdOwner; [ThreadStatic] private static int tsMacdKey; [ThreadStatic] private static Entry tsMacdEntry;

		public NQCE3_Series(NQCE_Bars b, double[] volume)
		{
			Src = b; O = b.O; H = b.H; L = b.L; C = b.C; Count = b.Count;
			if (volume != null && volume.Length != Count) throw new ArgumentException("volumen: longitud distinta de las barras");
			for (int i = 0; i < Count; i++)
				if (!Fin(O[i]) || !Fin(H[i]) || !Fin(L[i]) || !Fin(C[i]) || (volume != null && (!Fin(volume[i]) || volume[i] < 0)))
					throw new ArgumentException("barra " + i.ToString(CultureInfo.InvariantCulture) + ": valor no finito (OHLC) o volumen negativo/no finito");
			V = volume; HasVolume = volume != null;
			DayStart = new int[Count]; pos = new int[Count]; posAux = new int[Count]; winCount = 0; auxCount = 0;
			for (int i = 0; i < Count; i++)
			{
				DayStart[i] = (i > 0 && b.DayKey[i] == b.DayKey[i - 1]) ? DayStart[i - 1] : i;
				int st = b.Stamp[i];
				if (st >= NQCE_Const.StampLoadFrom - PosMarginBars && st <= NQCE_Const.StampLoadTo) pos[i] = winCount++; else pos[i] = -1;
				if (st >= NQCE_Const.StampLoadFrom - AuxMarginBars && st <= NQCE_Const.StampLoadTo) posAux[i] = auxCount++; else posAux[i] = -1;
			}
		}
		private static bool Fin(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }

		public NQCE3_Bar Bar(int k)
		{
			NQCE3_Bar r; r.Open = O[k]; r.High = H[k]; r.Low = L[k]; r.Close = C[k]; r.Volume = (V == null) ? 0.0 : V[k];
			return r;
		}

		// Entrada de cache de la clave (crea si falta; expulsa por LRU una a una). Bajo memoLock: seguro entre hilos.
		internal Entry GetEntry(int key)
		{
			lock (memoLock)
			{
				Entry e;
				if (memo.TryGetValue(key, out e)) { e.Used = ++tick; return e; }
				int len = key >= AuxKeyBase ? auxCount : winCount;
				long bytes = 8L * Math.Max(1, len);
				long budget = MemoBudgetBytes - MemoReserveEntries * 8L * Math.Max(1, winCount);
				while (memo.Count > 0 && memoBytes + bytes > budget)
				{
					int lru = 0; long best = long.MaxValue;
					foreach (KeyValuePair<int, Entry> kv in memo) if (kv.Value.Used < best) { best = kv.Value.Used; lru = kv.Key; }
					memoBytes -= memo[lru].Bytes; memo.Remove(lru);    // LRU: se expulsa la menos usada, no se vacia todo
				}
				e = new Entry(); e.A = new double[len]; e.Key = key; e.Bytes = bytes; for (int i = 0; i < len; i++) e.A[i] = double.NaN;
				e.Used = ++tick; memo[key] = e; memoBytes += bytes; return e;
			}
		}
		public const int AuxKeyBase = 3000000;
		private static readonly object[] StripesA = MakeStripes();
		[ThreadStatic] private static NQCE3_Series[] tsAuxOwner; [ThreadStatic] private static int[] tsAuxKey; [ThreadStatic] private static Entry[] tsAuxEntry;
		// Memo AUXILIAR por barra: cantidad g(k) de una familia que NO depende de la barra de senal `at` (p. ej. el alpha de FRAMA en la barra k). Mismo valor que el
		// calculo directo (NQCE3_Aux.Compute), calculado una vez por barra y reutilizado por las ~2n barras siguientes. Ventana propia (AuxMarginBars antes de la carga).
		internal Entry AuxEntry(int which, int n)
		{
			int key = AuxKeyBase + which * 64 + n; int slot = key & 3;
			if (tsAuxEntry == null) { tsAuxOwner = new NQCE3_Series[4]; tsAuxKey = new int[4]; tsAuxEntry = new Entry[4]; }
			if (ReferenceEquals(tsAuxOwner[slot], this) && tsAuxKey[slot] == key && tsAuxEntry[slot] != null) return tsAuxEntry[slot];
			Entry e = GetEntry(key); tsAuxOwner[slot] = this; tsAuxKey[slot] = key; tsAuxEntry[slot] = e; return e;
		}
		internal double AuxAt(Entry e, int which, int k, int n)
		{
			int ps = posAux[k];
			if (ps < 0 || DebugNoAux) return NQCE3_Aux.Compute(this, which, k, n);
			double[] arr = e.A; double v = arr[ps];
			if (double.IsNaN(v))
			{
				lock (StripesA[(unchecked((uint)(e.Key * 7919 + ps)) * 2654435761u) % StripeCount])
				{
					v = arr[ps];
					if (double.IsNaN(v)) { v = NQCE3_Aux.Compute(this, which, k, n); arr[ps] = v; }   // un NaN real se recalcula cada vez (correcto, solo mas lento)
				}
			}
			return v;
		}
		public int MemoCount { get { lock (memoLock) { return memo.Count; } } }
		public long MemoBytes { get { lock (memoLock) { return memoBytes; } } }     // bytes de la cache (sin las entradas expulsadas que algun hilo aun usa)
		public static bool DebugNoAux;    // SOLO pruebas: desactiva el memo auxiliar (cada llamada recalcula la expresion original) para comprobar que memorizar no cambia ningun valor

		// ---- D-031(3): ATR de Wilder (el MISMO arreglo que usa el ejecutor: NQCE_Bars.Atr(p) = AtrOwn) con el periodo p del gen de salida.
		// AtrOwn arranca con r[0]=H-L y MEDIA CRECIENTE hasta completar p barras (calentamiento): en las primeras ~3p barras de la serie
		// completa el valor es una media parcial, no un Wilder asentado. Como la serie incluye pre-mercado (ETH desde 00:00), a las 09:31
		// de cualquier dia del IS salvo los primeros del dataset el ATR ya esta asentado; la biblioteca NO anade otro requisito.
		public const double AtrEps = 1e-12;
		public void SetAtr(int p, bool unit)
		{
			tsAtrUnit = unit; tsAtrArr = unit ? null : AtrArr(p);
		}
		// Arreglo ATR de Wilder de periodo p (el mismo objeto que NQCE_Bars.Atr(p)). Lectura sin candado tras la primera vez.
		public double[] AtrArr(int p)
		{
			if (p < 0 || p >= atrTab.Length) lock (memoLock) return Src.Atr(p);
			double[] a = atrTab[p];
			if (a == null) lock (memoLock) { a = Src.Atr(p); atrTab[p] = a; }
			return a;
		}
		// Hilo principal, antes de lanzar hilos: deja el arreglo ATR(p) creado en NQCE_Bars y en la tabla (el ejecutor lo lee del diccionario sin candado).
		public void PrepareAtr(int p) { AtrArr(p); }
		// ATR (Wilder, periodo SetAtr) en la barra k. En modo unitario devuelve 1: las formulas "numerador/ATR" devuelven entonces el numerador en puntos.
		public double AtrAt(int k)
		{
			if (tsAtrUnit) return 1.0;
			double[] a = tsAtrArr;
			if (a == null) throw new InvalidOperationException("ATR no fijado (SetAtr)");
			return Math.Max(a[k], AtrEps);
		}
		private double WilderAt(int p, int at) { return Math.Max(AtrArr(p)[at], AtrEps); }

		// Calculo directo (sin cache) de la familia f, periodo n, ATR de Wilder p, en la barra at; unit=true => ATR = 1 (numerador).
		public double Direct(int f, int n, int p, int at, bool unit)
		{
			SetAtr(p, unit);
			try { return NQCE3_Features.Compute(this, at, f, n); }
			finally { tsAtrArr = null; tsAtrUnit = false; }
		}

		internal static int KeyOf(char kind, int f, int n, int p) { return kind == '2' ? 2000000 + (f * 64 + n) * 128 + p : f * 64 + n; }

		// Valor de la familia con la entrada de cache ya resuelta (e puede ser null si la barra esta fuera de la ventana memorizada).
		// kind/f/n/p: clave; atrA: arreglo ATR de Wilder de periodo p (solo para kind '1'). NaN si no es finito.
		internal double FeatureE(Entry e, char kind, int f, int n, int p, double[] atrA, int at)
		{
			int ps = pos[at];
			if (ps < 0 || e == null)
			{
				if (kind == '2') return Clean(Direct(f, n, p, at, false));
				double v0 = Clean(Direct(f, n, p, at, true));
				return (kind == '1' && !double.IsNaN(v0)) ? Clean(v0 / Math.Max(atrA[at], AtrEps)) : v0;
			}
			double[] arr = e.A;
			double v = arr[ps];
			if (double.IsNaN(v))
			{
				lock (StripesF[(unchecked((uint)(e.Key * 7919 + ps)) * 2654435761u) % StripeCount])
				{
					v = arr[ps];
					if (double.IsNaN(v)) { v = Clean(Direct(f, n, p, at, kind != '2')); arr[ps] = double.IsNaN(v) ? NonFiniteMark : v; }
					else if (double.IsNegativeInfinity(v)) v = double.NaN;
				}
			}
			else if (double.IsNegativeInfinity(v)) v = double.NaN;
			if (kind == '1' && !double.IsNaN(v)) v = Clean(v / Math.Max(atrA[at], AtrEps));
			return v;
		}

		// Valor de la familia f (periodo n, ATR de Wilder de periodo p) en la barra at. NaN si no es finito.
		// Descomposicion D-031(3) segun NQCE3_Catalog.AtrKind(f):
		//   '0' independiente del ATR: se cachea por (f,n);
		//   '1' numerador en puntos (cacheado por (f,n), independiente de p) dividido por el ATR de Wilder (96 series, una por periodo del gen de salida);
		//   '2' no descomponible (ATR en una no linealidad o evaluado en otras barras): cache propia por (f,n,p) con el mismo tope LRU.
		public double Feature(int f, int n, int p, int at)
		{
			char kind = NQCE3_Catalog.AtrKindOf(f);
			Entry e = pos[at] < 0 ? null : GetEntry(KeyOf(kind, f, n, p));
			return FeatureE(e, kind, f, n, p, kind == '1' ? AtrArr(p) : null, at);
		}

		// Tres lecturas (at, at-1, at-2) de una condicion con la entrada ya resuelta (la usa NQCE3_Cond: sin candado ni diccionario por barra).
		internal Entry Acquire(int f, int n, int p) { return GetEntry(KeyOf(NQCE3_Catalog.AtrKindOf(f), f, n, p)); }

		// MACD EMA(fast)-EMA(slow) de cierres, MISMOS valores que el calculo directo (ventana fija 3n+5 por barra),
		// pero memorizado por barra: evita recalcularlo ~n*n/2 veces en las familias 31 y 69.
		public double MacdMemo(int fast, int slow, int at)
		{
			int p = pos[at];
			if (p < 0) return NQCE3_M.EMA(this, at, fast) - NQCE3_M.EMA(this, at, slow);
			int key = 1000000 + fast * 1000 + slow;
			Entry e;
			if (ReferenceEquals(tsMacdOwner, this) && tsMacdKey == key && tsMacdEntry != null) e = tsMacdEntry;
			else { e = GetEntry(key); tsMacdOwner = this; tsMacdKey = key; tsMacdEntry = e; }
			double[] arr = e.A;
			double v = arr[p];
			if (double.IsNaN(v))
			{
				lock (StripesM[(unchecked((uint)(key * 7919 + p)) * 2654435761u) % StripeCount])
				{
					v = arr[p];
					if (double.IsNaN(v)) { v = NQCE3_M.EMA(this, at, fast) - NQCE3_M.EMA(this, at, slow); arr[p] = v; }
				}
			}
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
		// Espacio de ids ESTABLE (trazabilidad con NQCL-2.0.0): condId = 11*familia + regla + 1, 1..1320, con huecos.
		// D-031(2): 7 familias eliminadas (duplicados exactos): quedan 113 familias x 11 = 1.243 condiciones; ConditionCount = 1.243.
		public const int Families = 120, Rules = 11, IdSpace = Families * Rules;   // 1.320 ids posibles
		// 1 = familia eliminada por D-031(2) (duplicado EXACTO de otra de id menor): 9(=0), 18(=11), 52(=5), 76(=34), 81(=7), 104(=20), 108(=20).
		public static readonly int[] RemovedFamilies = new int[] { 9, 18, 52, 76, 81, 104, 108 };
		private static bool IsRemoved(int family) { return Array.IndexOf(RemovedFamilies, family) >= 0; }
		public static readonly int[] ActiveIds = BuildActiveIds();
		public static readonly int Count = ActiveIds.Length;      // 1.243
		public static readonly int ActiveFamilies = CountActiveFamilies();
		private static int[] BuildActiveIds()
		{
			List<int> l = new List<int>();
			for (int id = 1; id <= IdSpace; id++) if (!IsRemoved((id - 1) / Rules)) l.Add(id);
			return l.ToArray();
		}
		private static int CountActiveFamilies() { int k = 0; for (int f = 0; f < Families; f++) if (!IsRemoved(f)) k++; return k; }
		public static bool IsActiveFamily(int family) { return family >= 0 && family < Families && !IsRemoved(family); }
		public static bool IsActiveId(int condId) { return condId >= 1 && condId <= IdSpace && !IsRemoved((condId - 1) / Rules); }
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
		//  A = magnitud en multiplos de ATR (100 familias antes de D-031(2); 93 activas tras eliminar 7 duplicados exactos);
		//  X = EXCEPCION R5 ACEPTADA (D-031(1), 2026-11-14): razon/indicador adimensional acotado SIN escala de precio, no reexpresable en ATR
		//      (16 familias D-031 + 4 de D-032 = 20, se MANTIENEN): 10,17,24,32,33,42,59,60,64,68,69,71,85,99,103,106,110,115,118,119. Su umbral es una fraccion, NO un multiplo de ATR.
		//  E = dudosa: 0 familias desde D-032 (2026-11-14): 32 VPT, 60 Connors RSI, 85 expansion de volatilidad log y 103 VFI pasan de E a X
		//      (excepcion R5 ampliada a 20 familias activas: A=93, X=20, E=0). Cambia UnitClass y por tanto el hash de la biblioteca (NQCL-3.1.0).
		//  Las familias eliminadas por D-031(2) (ver RemovedFamilies) conservan aqui su letra original; el conteo de clases se hace sobre las activas.
		public const string UnitClass =
			"AAAAAAAAAAXA"+	// 0..11
			"AAAAAXAAAAAA"+	// 12..23
			"XAAAAAAAXXAA"+	// 24..35
			"AAAAAAXAAAAA"+	// 36..47
			"AAAAAAAAAAAX"+	// 48..59
			"XAAAXAAAXXAX"+	// 60..71
			"AAAAAAAAAAAA"+	// 72..83
			"AXAAAAAAAAAA"+	// 84..95
			"AAAXAAAXAAXA"+	// 96..107
			"AAXAAAAXAAXX";	// 108..119

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

		// D-031(3): como depende cada familia del ATR de Wilder (verificado numericamente por NQC_ET3_LibraryTest L11):
		//  '0' independiente del ATR (razones/fracciones/estimadores sin escala de precio); '1' = numerador en puntos / ATR(at) exactamente;
		//  Las de tipo '2' (9): 78 Keltner (umbral 1,5 ATR), 82 (cociente de ATR de barras distintas), 83 (ATR al cuadrado), 86 (ATR en dos barras), 92 Ulcer (piso 1e-12 del
		//  denominador cuando no hay caidas), 93 Chande-Kroll,
		//  94 Supertrend (ATR de cada barra), 98 squeeze Bollinger-Keltner, 118 fractal polarizado (raiz de ATR^2+delta^2).
		//  '2' NO descomponible (el ATR entra en una no linealidad, en otras barras o con potencia distinta de 1): cache propia por (f,n,p).
		public const string AtrKind =
			"111111111101" +	// 0..11
			"111110111111" +	// 12..23
			"011111111011" +	// 24..35
			"111111011111" +	// 36..47
			"111111111110" +	// 48..59
			"011101110010" +	// 60..71
			"111111211122" +	// 72..83
			"102111112221" +	// 84..95
			"112011101101" +	// 96..107
			"110111101120";	// 108..119
		public static bool DebugForceDirect;   // SOLO medicion de costo (NQC_ET3_GaTest): trata las '1' como '2' para comparar contra no descomponer
		public static char AtrKindOf(int family) { char k = AtrKind[family]; return (DebugForceDirect && k == '1') ? '2' : k; }
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
