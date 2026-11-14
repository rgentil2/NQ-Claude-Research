#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
#endregion

//==============================================================================
// NQC_ET3_Genome - WP2 codificacion del genoma de 20 genes (ET3). SOLO DESARROLLO, sin compilar.
// Biblioteca PURA (sin API de NinjaTrader). Depende de NQC_ET1_Core.cs.
//
// Genes (indice 0..19), todos enteros:
//   0..15  8 ranuras de entrada: gen 2s = condicion (0 = ranura vacia, 1..N = id de la biblioteca),
//          gen 2s+1 = nivel de parametro (0..ParamLevels-1; en multiplos de ATR segun la biblioteca, D-018/R5)
//   16     direccion: 0 = corto (-1), 1 = largo (+1)
//   17     periodo ATR: 5..100 (D-019a)
//   18     k_sl: indice 0..22 -> 0,5 + 0,25*i (0,5..6,0)
//   19     k_tp: 0 = ausente, 1..31 -> 0,5 + 0,25*(i-1) (0,5..8,0)   [0,5..8,0 paso 0,25 son 31 valores]
// MaxPerDay=1 y TimeExitStamp=-1 fijos (no evolucionan). Las ranuras se combinan con AND.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	// Interfaz para enchufar la biblioteca de condiciones D-018 (120 formulas / 1.320 condiciones).
	// La implementacion NO existe todavia (WP1 bloqueado: ver docs/stages/ET3.md).
	public interface INQCE3_Library
	{
		string Version { get; }                 // identificador/hash de la biblioteca, va al ledger
		int ConditionCount { get; }             // N: ids validos 1..N
		int ParamLevels(int condId);            // niveles discretos del umbral (>=1) de la condicion
		INQCE_Condition Create(int condId, int paramLevel, int atrPeriod);   // funcion pura; umbrales en ATR
	}

	public sealed class NQCE3_Genome
	{
		public const int Slots = 8, GeneCount = 20;
		public const int GDir = 16, GAtr = 17, GKsl = 18, GKtp = 19;
		public const int AtrMin = 5, AtrMax = 100, KslLevels = 23, KtpLevels = 32;   // KtpLevels incluye "ausente"
		public int[] G = new int[GeneCount];

		public NQCE3_Genome Clone() { NQCE3_Genome g = new NQCE3_Genome(); Array.Copy(G, g.G, GeneCount); return g; }

		public static double KslValue(int idx) { return 0.5 + 0.25 * idx; }
		public static double KtpValue(int idx) { return idx == 0 ? double.NaN : 0.5 + 0.25 * (idx - 1); }

		// Limite de cada gen: numero de valores validos (0..n-1).
		public static int GeneRange(int gene, INQCE3_Library lib)
		{
			if (gene < 16) { return (gene % 2 == 0) ? lib.ConditionCount + 1 : 0; }   // gen impar depende de la condicion
			if (gene == GDir) return 2;
			if (gene == GAtr) return AtrMax - AtrMin + 1;
			if (gene == GKsl) return KslLevels;
			return KtpLevels;
		}

		// Reparacion de valores invalidos: acota, vacia ranuras sin condicion, elimina condiciones duplicadas.
		public void Repair(INQCE3_Library lib)
		{
			for (int s = 0; s < Slots; s++)
			{
				int c = G[2 * s]; if (c < 0) c = 0; if (c > lib.ConditionCount) c = lib.ConditionCount;
				for (int p = 0; p < s; p++) if (c != 0 && G[2 * p] == c) { c = 0; break; }
				G[2 * s] = c;
				if (c == 0) { G[2 * s + 1] = 0; continue; }
				int lv = lib.ParamLevels(c); if (lv < 1) lv = 1;
				if (G[2 * s + 1] < 0) G[2 * s + 1] = 0;
				if (G[2 * s + 1] > lv - 1) G[2 * s + 1] = lv - 1;
			}
			G[GDir] = G[GDir] == 0 ? 0 : 1;
			if (G[GAtr] < AtrMin) G[GAtr] = AtrMin; if (G[GAtr] > AtrMax) G[GAtr] = AtrMax;
			if (G[GKsl] < 0) G[GKsl] = 0; if (G[GKsl] > KslLevels - 1) G[GKsl] = KslLevels - 1;
			if (G[GKtp] < 0) G[GKtp] = 0; if (G[GKtp] > KtpLevels - 1) G[GKtp] = KtpLevels - 1;
		}

		public int ActiveSlots() { int n = 0; for (int s = 0; s < Slots; s++) if (G[2 * s] != 0) n++; return n; }

		// Decodifica a NQCE_Member. Se asume Repair() ya aplicado. Devuelve null si no hay condiciones.
		public NQCE_Member ToMember(INQCE3_Library lib, string id)
		{
			if (ActiveSlots() == 0) return null;
			NQCE_Member m = new NQCE_Member();
			m.Id = id; m.Dir = G[GDir] == 1 ? 1 : -1; m.AtrPeriod = G[GAtr];
			m.Ksl = KslValue(G[GKsl]); m.Ktp = KtpValue(G[GKtp]);
			m.MaxPerDay = 1; m.TimeExitStamp = -1;
			for (int s = 0; s < Slots; s++)
				if (G[2 * s] != 0) m.Conditions.Add(lib.Create(G[2 * s], G[2 * s + 1], m.AtrPeriod));
			return m;
		}

		// Texto canonico: ranuras ordenadas por id de condicion (el AND es conmutativo) para que
		// genomas equivalentes compartan hash. Incluye la version de la biblioteca.
		public string CanonicalText(INQCE3_Library lib)
		{
			List<string> parts = new List<string>();
			for (int s = 0; s < Slots; s++)
				if (G[2 * s] != 0) parts.Add(G[2 * s].ToString("D5", CultureInfo.InvariantCulture) + ":" + G[2 * s + 1].ToString(CultureInfo.InvariantCulture));
			parts.Sort(StringComparer.Ordinal);
			StringBuilder sb = new StringBuilder();
			sb.Append("lib=").Append(lib.Version).Append("|dir=").Append(G[GDir].ToString(CultureInfo.InvariantCulture))
			  .Append("|atr=").Append(G[GAtr].ToString(CultureInfo.InvariantCulture))
			  .Append("|ksl=").Append(G[GKsl].ToString(CultureInfo.InvariantCulture))
			  .Append("|ktp=").Append(G[GKtp].ToString(CultureInfo.InvariantCulture)).Append("|if=");
			for (int k = 0; k < parts.Count; k++) { if (k > 0) sb.Append(","); sb.Append(parts[k]); }
			return sb.ToString();
		}

		// Id de ensayo: SHA256 del GenomeText del miembro decodificado (D: ledger) + biblioteca.
		public string TrialId(INQCE3_Library lib)
		{
			NQCE_Member m = ToMember(lib, "x");
			string txt = m == null ? "EMPTY" : m.GenomeText();
			return NQCE_Const.Sha256Text("lib=" + lib.Version + "|" + txt);
		}
	}
}
