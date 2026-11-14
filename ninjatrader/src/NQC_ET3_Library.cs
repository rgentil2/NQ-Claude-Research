#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
#endregion

//==============================================================================
// NQC_ET3_Library - ET3 WP1: biblioteca de condiciones D-018 (120 familias x 11 reglas = 1.320 condiciones)
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
// Mapa: condId 1..1320 -> tipo = condId-1; familia = tipo/11; regla = tipo%11 (mismos ids 0..1319 de la referencia, +1).
// Parametro genetico: nivel 0..26 -> periodo n = 4 + nivel (4..30). Los umbrales de las reglas son FIJOS
// (0,15/0,35/0,60 en unidades de la familia): el gen NO los mueve (ver hallazgos en docs/stages/ET3.md).
// Direccion: la condicion dispara cuando el predicado de la regla es alcista (dir=+1) o bajista (dir=-1).
// El parametro atrPeriod de Create NO se usa: cada familia usa su propio ATR simple de n barras.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public sealed class NQCE3_Library : INQCE3_Library
	{
		public const string LibName = "NQCL-1.0.0";
		public const int WarmSameDay = 0, WarmAnyHistory = 1;
		public int WarmupMode = WarmSameDay;       // defecto: todas las barras de recursion en el mismo dia calendario
		private string version; private NQCE3_Series series; private double[] pendingVolume; private NQCE_Bars pendingBars;

		public NQCE3_Library() { version = null; }

		// Enlaza las barras (y el volumen alineado, opcional). Sin volumen, las 24 familias con volumen no dan senal.
		public void Bind(NQCE_Bars bars, double[] volume)
		{
			pendingBars = bars; pendingVolume = volume; series = new NQCE3_Series(bars, volume);
		}
		public NQCE3_Series SeriesFor(NQCE_Bars b)
		{
			if (series == null || !object.ReferenceEquals(series.Src, b))
				series = new NQCE3_Series(b, object.ReferenceEquals(pendingBars, b) ? pendingVolume : null);
			return series;
		}

		public string Version
		{
			get
			{
				if (version == null)
				{
					StringBuilder sb = new StringBuilder(LibName);
					sb.Append("|req=7n+10|periods=4..30|rules=").Append(NQCE3_Rules.Count.ToString(CultureInfo.InvariantCulture));
					for (int f = 0; f < NQCE3_Catalog.Families; f++) sb.Append("|").Append(NQCE3_Catalog.FamilyNames[f]);
					for (int r = 0; r < NQCE3_Rules.Count; r++) sb.Append("|").Append(NQCE3_Rules.Names[r]);
					sb.Append("|").Append(NQCE3_Catalog.UnitClass).Append("|").Append(NQCE3_Catalog.VolumeDep);
					version = LibName + ":" + NQCE_Const.Sha256Text(sb.ToString()).Substring(0, 16);
				}
				return version;
			}
		}
		public int ConditionCount { get { return NQCE3_Catalog.Count; } }
		public int ParamLevels(int condId) { return NQCE3_Catalog.PeriodLevels; }

		public INQCE_Condition Create(int condId, int paramLevel, int atrPeriod, int dir)
		{
			if (condId < 1 || condId > NQCE3_Catalog.Count) throw new ArgumentOutOfRangeException("condId");
			if (paramLevel < 0 || paramLevel >= NQCE3_Catalog.PeriodLevels) throw new ArgumentOutOfRangeException("paramLevel");
			if (dir != 1 && dir != -1) throw new ArgumentOutOfRangeException("dir");
			return new NQCE3_Cond(this, condId, NQCE3_Catalog.MinPeriod + paramLevel, dir);
		}
	}

	public sealed class NQCE3_Cond : INQCE_Condition
	{
		private NQCE3_Library lib; private int condId, n, dir, family, rule;

		public NQCE3_Cond(NQCE3_Library library, int id, int period, int direction)
		{
			lib = library; condId = id; n = period; dir = direction;
			family = NQCE3_Catalog.FamilyOf(id); rule = NQCE3_Catalog.RuleOf(id);
		}

		// Devuelve 1/-1/0 del predicado en la barra at, o -2 si no hay senal posible (calentamiento, volumen ausente, no finito).
		public static int Direction(NQCE3_Series s, int family, int rule, int n, int at, int warmupMode)
		{
			if (at < 2) return -2;
			int oldest = at - NQCE3_Catalog.RequiredBars(n) + 1;   // cota (conservadora) de la barra mas antigua que se toca
			if (oldest < 0) return -2;
			if (warmupMode == NQCE3_Library.WarmSameDay && s.DayStart[at] > oldest) return -2;
			if (NQCE3_Catalog.NeedsVolume(family) && !s.HasVolume) return -2;
			double x = s.Feature(family, n, at);
			double y = s.Feature(family, n, at - 1);
			double z = s.Feature(family, n, at - 2);
			return NQCE3_Rules.Resolve(x, y, z, rule);
		}

		public bool Eval(NQCE_Ctx c)
		{
			NQCE3_Series s = lib.SeriesFor(c.B);
			int d = Direction(s, family, rule, n, c.I, lib.WarmupMode);
			return d == dir;
		}

		public string Describe()
		{
			return "NQCL" + condId.ToString("D4", CultureInfo.InvariantCulture) + "[" + NQCE3_Catalog.CondName(condId) + "]n=" + n.ToString(CultureInfo.InvariantCulture)
				+ (dir == 1 ? "+" : "-");
		}
	}
}
