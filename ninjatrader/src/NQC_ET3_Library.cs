#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
#endregion

//==============================================================================
// NQC_ET3_Library - ET3 WP1/D-018b/D-030: biblioteca de condiciones D-018 (113 familias activas x 11 reglas = 1.243 condiciones (ids 1..1320 con huecos))
// que implementa INQCE3_Library (NQC_ET3_Genome.cs). Contrato NQC 2.0.0. SOLO DESARROLLO, sin compilar.
//
// PROCEDENCIA (solo informativa; material de terceros leido en SOLO LECTURA, nada ejecutado):
//   repositorio historico mes-trading-research, commit 80ac30b, carpeta research/nq/ninjatrader/
//   NQNativeConditionCatalogV19.cs   b8689fd61a3b777c72af183d12c67a775dfd8c4010282de4d1878b0c9350cd41
//   NQNativeConditionCatalogV18.cs   3ae0174da2b6c3cf4785f085a157c7aefe261ed4628634a599210a03f5771f2b
//   NQNativeConditionsV11.cs         2ba47cb00b6c1161f01dc40df599ae7e66928464b3928333e527a6c59a052fe5
//   NQFeatureMathV19.cs              96346530811821ed5eb28f58eecd5921a4a85ce45ed1b1b5fbcfdb3dbfa30f66
//   NQTrendFeaturesV19.cs            fbebd0f6dca3d3860b0f9e46adfb4b15494ef5d0a9a63cdec7639fa4ebce86e6
//   NQMomentumFeaturesV19.cs         29d530be45bceb032015748ad5ef9abed6af21961d313ed8a72745bc1d0911f0
//   NQVolatilityFeaturesV19.cs       13fc78ab26315e29da05e2c8e6f54e1d0d29a9447589d586e73b48afb9182812
//   NQVolumeActionFeaturesV19.cs     228a0c61354ef2f68d856e80cf1c1b0f25e05e3a2e7c95f94f9cb5523d52fcec
// Port mecanico de las formulas (mismo calculo, nombres NQCE3_*, sin las clases del repo historico).
//
// Mapa (estable): condId 1..1320 -> tipo = condId-1; familia = tipo/11; regla = tipo%11 (mismos ids 0..1319 de la referencia, +1).
// Parametros geneticos por condicion (D-030): periodo n = 4 + nivelPeriodo (27 niveles, 4..30) y umbral en multiplos de ATR
// (nivelUmbral; 1/5/5/10 niveles segun la regla, ver NQCE3_Rules). El gen unico de parametro del genoma empaqueta ambos:
// gen = nivelPeriodo * ThresholdLevels(cond) + nivelUmbral (ver NQCE3_Genome).
// Direccion: la condicion dispara cuando el predicado de la regla es alcista (dir=+1) o bajista (dir=-1).
// ATR (D-031(3), reemplaza la decision M4): el ATR de TODAS las condiciones es el ATR de WILDER del gen de salida del miembro
// (NQCE_Bars.Atr(p) = AtrOwn, periodo 5..100, el MISMO arreglo que usa el ejecutor). El periodo n de la condicion ya no define el ATR.
// Para no multiplicar por 96 el costo, cada familia se descompone (NQCE3_Catalog.AtrKind): '1' numerador en puntos cacheado por (familia,n)
// y dividido por el ATR de Wilder; '0' independiente del ATR; '2' no descomponible, cache propia por (familia,n,p) con el tope LRU.
// D-031(2): 7 familias eliminadas (9,18,52,76,81,104,108): 113 familias, 1.243 condiciones; los ids conservan el mapa 11*familia+regla+1.
// VOLUMEN (D-018b): el runner DEBE llamar lib.Bind(bars, volumen) con volumen[i] alineado con bars (mismo Count, M1 completo desde
// 00:00, contratos por barra). NQCE_Bars (ET1 Core) no se modifica. Sin Bind las 24 familias de volumen no dan senal y NQCE3_Fitness lo rechaza.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public sealed class NQCE3_Library : INQCE3_Library
	{
		public const string LibName = "NQCL-3.1.0";
		public const int WarmSameDay = 0, WarmAnyHistory = 1;
		public int WarmupMode = WarmSameDay;       // defecto: todas las barras de recursion en el mismo dia calendario
		// Contadores de condiciones sin senal posible (diagnostico; se reinician con ResetRejects).
		// Totales (diagnostico). Con varios hilos NO se incrementan en la ruta caliente: cada hilo cuenta en variables de hilo (ThreadRejects) y el lote de
		// NQCE3_Fitness suma al terminar cada evaluacion (Interlocked), en orden de indice; asi el delta por evaluacion es exacto y el total no depende de los hilos.
		public long RejectWarmup, RejectVolume, RejectNonFinite;
		[ThreadStatic] private static long tWarm, tVol, tNon;
		internal static void NoteReject(int why) { if (why == 1) tWarm++; else if (why == 2) tVol++; else if (why == 3) tNon++; }
		// Contadores acumulados POR HILO (solo crecen): el delta alrededor de una evaluacion es el de esa evaluacion.
		public static void ThreadRejects(out long warm, out long vol, out long nonFinite) { warm = tWarm; vol = tVol; nonFinite = tNon; }
		internal void AddRejects(long w, long v, long n)
		{
			if (w != 0) System.Threading.Interlocked.Add(ref RejectWarmup, w);
			if (v != 0) System.Threading.Interlocked.Add(ref RejectVolume, v);
			if (n != 0) System.Threading.Interlocked.Add(ref RejectNonFinite, n);
		}
		private string version;
		private List<NQCE3_Series> cache = new List<NQCE3_Series>(); private const int MaxSeries = 4;
		private NQCE_Bars pendingBars; private double[] pendingVolume;

		public NQCE3_Library() { version = null; }

		// Enlaza las barras (y el volumen alineado). Sin volumen, las 24 familias con volumen no dan senal.
		public void Bind(NQCE_Bars bars, double[] volume)
		{
			pendingBars = bars; pendingVolume = volume;
			NQCE3_Series s = new NQCE3_Series(bars, volume);
			cache.RemoveAll(delegate(NQCE3_Series x) { return object.ReferenceEquals(x.Src, bars); });
			cache.Add(s); while (cache.Count > MaxSeries) cache.RemoveAt(0);
		}
		public bool VolumeBound(NQCE_Bars b) { NQCE3_Series s = SeriesFor(b); return s.HasVolume; }
		public void ResetRejects() { RejectWarmup = 0; RejectVolume = 0; RejectNonFinite = 0; }

		// Serie de barras b (varias a la vez, p. ej. IS y OOS; M5). Una serie no enlazada se crea sin volumen.
		public NQCE3_Series SeriesFor(NQCE_Bars b)
		{
			for (int i = 0; i < cache.Count; i++) if (object.ReferenceEquals(cache[i].Src, b)) return cache[i];
			NQCE3_Series s = new NQCE3_Series(b, object.ReferenceEquals(pendingBars, b) ? pendingVolume : null);
			cache.Add(s); while (cache.Count > MaxSeries) cache.RemoveAt(0);
			return s;
		}

		public string Version
		{
			get
			{
				if (version == null)
				{
					StringBuilder sb = new StringBuilder(LibName);
					sb.Append("|req=7n+10|periods=4..30|atr=wilder-exit|removed=9,18,52,76,81,104,108|rules=").Append(NQCE3_Rules.Count.ToString(CultureInfo.InvariantCulture));
					for (int r = 0; r < NQCE3_Rules.Count; r++)
						sb.Append("|thr").Append(r.ToString(CultureInfo.InvariantCulture)).Append("=").Append(NQCE3_Rules.ThresholdCount(r).ToString(CultureInfo.InvariantCulture))
						  .Append("@").Append(NQCE3_Rules.ThresholdCount(r) > 1 ? NQCE3_Rules.ThresholdValue(r, 0).ToString("0.00", CultureInfo.InvariantCulture) : "-");
					for (int f = 0; f < NQCE3_Catalog.Families; f++) sb.Append("|").Append(NQCE3_Catalog.FamilyNames[f]);
					for (int r = 0; r < NQCE3_Rules.Count; r++) sb.Append("|").Append(NQCE3_Rules.Names[r]);
					sb.Append("|").Append(NQCE3_Catalog.UnitClass).Append("|").Append(NQCE3_Catalog.VolumeDep).Append("|").Append(NQCE3_Catalog.AtrKind);
					version = LibName + ":" + NQCE_Const.Sha256Text(sb.ToString()).Substring(0, 16);
				}
				return version;
			}
		}
		public int ConditionCount { get { return NQCE3_Catalog.Count; } }               // 1.243 (D-031(2))
		public int ConditionId(int index) { return NQCE3_Catalog.ActiveIds[index]; }       // indice 0..ConditionCount-1 -> id estable (con huecos)
		public bool IsValidCondition(int condId) { return NQCE3_Catalog.IsActiveId(condId); }
		public int PeriodLevels(int condId) { return NQCE3_Catalog.PeriodLevels; }
		public int ThresholdLevels(int condId) { return NQCE3_Rules.ThresholdCount(NQCE3_Catalog.RuleOf(condId)); }

		public INQCE_Condition Create(int condId, int periodLevel, int thresholdLevel, int atrPeriod, int dir)
		{
			if (!NQCE3_Catalog.IsActiveId(condId)) throw new ArgumentOutOfRangeException("condId");
			if (periodLevel < 0 || periodLevel >= NQCE3_Catalog.PeriodLevels) throw new ArgumentOutOfRangeException("periodLevel");
			if (thresholdLevel < 0 || thresholdLevel >= ThresholdLevels(condId)) throw new ArgumentOutOfRangeException("thresholdLevel");
			if (dir != 1 && dir != -1) throw new ArgumentOutOfRangeException("dir");
			if (atrPeriod < 1) throw new ArgumentOutOfRangeException("atrPeriod");
			return new NQCE3_Cond(this, condId, NQCE3_Catalog.MinPeriod + periodLevel, thresholdLevel, atrPeriod, dir);
		}
	}

	public sealed class NQCE3_Cond : INQCE_Condition
	{
		private NQCE3_Library lib; private int condId, n, atrP, dir, family, rule; private double thr;
		// Estado por condicion (NQCE3_Cond la usa UN solo hilo a la vez: el lote crea un miembro por genoma): serie y entrada de cache resueltas una vez,
		// de modo que la evaluacion por barra no toca candados ni diccionarios.
		private NQCE_Bars hb; private NQCE3_Series hs; private NQCE3_Series.Entry he; private double[] hatr; private char hkind; private bool needsVol;

		public NQCE3_Cond(NQCE3_Library library, int id, int period, int thresholdLevel, int atrPeriod, int direction)
		{
			lib = library; condId = id; n = period; atrP = atrPeriod; dir = direction;
			family = NQCE3_Catalog.FamilyOf(id); rule = NQCE3_Catalog.RuleOf(id);
			thr = NQCE3_Rules.ThresholdCount(rule) > 1 ? NQCE3_Rules.ThresholdValue(rule, thresholdLevel) : double.NaN;
			needsVol = NQCE3_Catalog.NeedsVolume(family);
		}

		// p = periodo del ATR de Wilder (gen de salida del miembro), D-031(3).
		public static int Direction(NQCE3_Series s, int family, int rule, int n, int p, int at, int warmupMode)
		{
			return Direction(s, family, rule, n, p, NQCE3_Rules.DefaultThreshold(rule), at, warmupMode);
		}
		public static int Direction(NQCE3_Series s, int family, int rule, int n, int p, double thr, int at, int warmupMode)
		{
			int why; return DirectionWhy(s, family, rule, n, p, thr, at, warmupMode, out why);
		}

		// Devuelve 1/-1/0 del predicado en la barra at, o -2 si no hay senal posible. why: 0 ok, 1 calentamiento, 2 volumen ausente, 3 no finito.
		public static int DirectionWhy(NQCE3_Series s, int family, int rule, int n, int p, double thr, int at, int warmupMode, out int why)
		{
			why = 1;
			if (at < 2) return -2;
			int oldest = at - NQCE3_Catalog.RequiredBars(n) + 1;   // cota (conservadora) de la barra mas antigua que se toca
			if (oldest < 0) return -2;
			if (warmupMode == NQCE3_Library.WarmSameDay && s.DayStart[at] > oldest) return -2;
			why = 2;
			if (NQCE3_Catalog.NeedsVolume(family) && !s.HasVolume) return -2;
			double x = s.Feature(family, n, p, at);
			double y = s.Feature(family, n, p, at - 1);
			double z = s.Feature(family, n, p, at - 2);
			int r = NQCE3_Rules.Resolve(x, y, z, rule, thr);
			why = r == -2 ? 3 : 0;
			return r;
		}

		// Misma logica y mismas salidas que DirectionWhy (mismos valores bit a bit), con la entrada de cache resuelta una vez por condicion.
		public bool Eval(NQCE_Ctx c)
		{
			NQCE3_Series s = hs;
			if (s == null || !object.ReferenceEquals(hb, c.B)) { s = hs = lib.SeriesFor(c.B); hb = c.B; he = null; }
			int at = c.I, why = 1, d = -2;
			if (at >= 2)
			{
				int oldest = at - NQCE3_Catalog.RequiredBars(n) + 1;
				if (oldest >= 0 && !(lib.WarmupMode == NQCE3_Library.WarmSameDay && s.DayStart[at] > oldest))
				{
					why = 2;
					if (!(needsVol && !s.HasVolume))
					{
						if (he == null) { hkind = NQCE3_Catalog.AtrKindOf(family); he = s.Acquire(family, n, atrP); hatr = hkind == '1' ? s.AtrArr(atrP) : null; }
						double x = s.FeatureE(he, hkind, family, n, atrP, hatr, at);
						double y = s.FeatureE(he, hkind, family, n, atrP, hatr, at - 1);
						double z = s.FeatureE(he, hkind, family, n, atrP, hatr, at - 2);
						d = NQCE3_Rules.Resolve(x, y, z, rule, thr);
						why = d == -2 ? 3 : 0;
					}
				}
			}
			if (why != 0) NQCE3_Library.NoteReject(why);
			return d == dir;
		}

		public string Describe()
		{
			return "NQCL" + condId.ToString("D4", CultureInfo.InvariantCulture) + "[" + NQCE3_Catalog.CondName(condId) + "]n=" + n.ToString(CultureInfo.InvariantCulture)
				+ (double.IsNaN(thr) ? "" : " thr=" + thr.ToString("0.00", CultureInfo.InvariantCulture)) + (dir == 1 ? "+" : "-");
		}
	}
}
