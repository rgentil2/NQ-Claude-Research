#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
#endregion

//==============================================================================
// NQC_ET2_Datasets - Datasets nulos N1 y N2 de ET2 (docs/STATISTICS_PLAN.md §3). Contrato NQC 2.0.0.
// SOLO DESARROLLO: no se ejecuta sobre datos de mercado en esta etapa de codigo. Sin ordenes.
//
// DEFINICIONES (todas deterministas; el azar es NQCE_Rng con la semilla declarada):
//  SESION = racha contigua de barras con el mismo DayKey (las barras ya son las de la ventana cargada 09:29..11:32).
//  CIERRE PREVIO de la sesion k = C de la ultima barra de la sesion k-1 (la serie es back-adjusted, empalme continuo);
//  para la sesion 0 se define como su primera apertura (hueco 0).
//  OFFSET de una barra = precio - cierre previo de SU sesion de origen. La sesion se guarda como offsets (O,H,L,C) + volumen.
//  Todo se reconstruye como  precio = cierre_previo_NUEVO + offset  con  cierre_previo_NUEVO = ultimo cierre de la salida.
//
//  N1 (permutacion de sesiones completas). El CALENDARIO (T de cada barra, fines de semana, dias excluidos, noticias,
//  ventana y dia de la semana) pertenece a la RANURA y NO se mueve: se mueve el contenido (offsets + volumen), que viaja
//  CON su hueco nocturno (el offset de la primera barra incluye el hueco). Consecuencias:
//   - empalme: ninguna sesion ve un salto que no existiera en los datos: el hueco que ve cada sesion es el suyo propio;
//     el multiconjunto de TR (y por tanto la distribucion del ATR de Wilder, cuyo estado depende solo de los TR) es IDENTICO;
//     solo cambia el orden de los TR (y el arranque de la media creciente, que usa la barra 0, fija). Cierre final identico
//     (suma de incrementos de sesion invariante).
//   - volumen: viaja con el contenido; su relacion con el calendario/noticias/dia de la semana se ROMPE (conservador: el
//     nulo no hereda la relacion volatilidad-noticias; las noticias siguen bloqueando las mismas franjas de calendario).
//   - solo se permutan entre si las sesiones con la MISMA firma de sellos (mismo numero de barras y mismos sellos): una sesion
//     de firma unica (incompleta) queda en su ranura. Quedan FIJAS: la ranura 0 y los dias excluidos (ET0) -> el
//     ejecutor los ignora de todos modos. Los contenidos de los dias excluidos tambien quedan en su ranura.
//   - limitacion: rompe tendencias multi-dia (documentado en STATISTICS_PLAN).
//
//  N2 (inversion aleatoria de signo por sesion). Cada sesion k recibe s_k = +1 o -1 (moneda justa de NQCE_Rng, en orden de
//  sesiones). La REFLEXION es alrededor del CIERRE PREVIO de la salida (incluye el hueco nocturno):
//     s=+1: (O,H,L,C) = P + (o,h,l,c)
//     s=-1: O' = P - o, C' = P - c, H' = P - l, L' = P - h      (H y L se intercambian: H' >= max(O',C'), L' <= min(O',C'))
//  con P = ultimo cierre de la salida y (o,h,l,c) los offsets de origen. Se conservan EXACTAMENTE: rango de cada barra,
//  |cambio de cierre a cierre| de cada barra (incluidos huecos), TR, ATR, volumen, calendario, noticias y ventana (cada
//  sesion sigue en su fecha). Se invierte el signo del retorno cierre-a-cierre de cada sesion con prob. 1/2: se destruye el
//  sesgo direccional y la dependencia de signo entre sesiones. LIMITACION: se conserva la estructura intradia de
//  continuacion/reversion (una sesion reflejada de un patron de momentum sigue siendo momentum) y la relacion volatilidad-
//  calendario; un edge intradia SIMETRICO real sobrevive a N2 (se reporta como limitacion, no como defecto).
//
//  Ambos: la rejilla de tick (0,25) se conserva exacta (diferencias y sumas de multiplos de 0,25 son exactas en double);
//  si el minimo de la serie reconstruida cae bajo MinLevel (por defecto 1.000) se desplaza TODA la serie una constante
//  multiplo de 0,25 (invariante: toda condicion es en ATR/ticks/puntos, R5).
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public sealed class NQCET2_Sess
	{
		public int Start, End, DayKey, Len; public string Sig;
	}

	public static class NQCET2_Core
	{
		public const string Version = "ET2DATASETS-1.0.0";

		public static List<NQCET2_Sess> Split(NQCE_Bars b)
		{
			List<NQCET2_Sess> l = new List<NQCET2_Sess>();
			int s = 0;
			for (int i = 1; i <= b.Count; i++)
			{
				if (i == b.Count || b.DayKey[i] != b.DayKey[s])
				{
					NQCET2_Sess x = new NQCET2_Sess(); x.Start = s; x.End = i - 1; x.Len = i - s; x.DayKey = b.DayKey[s];
					StringBuilder sb = new StringBuilder(); for (int k = s; k < i; k++) sb.Append(b.Stamp[k]).Append(',');
					x.Sig = sb.ToString(); l.Add(x); s = i;
				}
			}
			return l;
		}

		public static double PrevClose(NQCE_Bars b, List<NQCET2_Sess> ss, int k) { return k == 0 ? b.O[0] : b.C[ss[k - 1].End]; }

		// Reconstruye la salida: ranura k toma el contenido de srcOf[k] con signo sign[k] (+1/-1).
		public static NQCE_Bars Rebuild(NQCE_Bars b, double[] vol, List<NQCET2_Sess> ss, int[] srcOf, int[] sign, double minLevel, out double[] outVol)
		{
			int n = b.Count; double[] O = new double[n], H = new double[n], L = new double[n], C = new double[n];
			outVol = vol == null ? null : new double[n];
			double prev = b.O[0];
			for (int k = 0; k < ss.Count; k++)
			{
				NQCET2_Sess d = ss[k], s = ss[srcOf[k]];
				if (d.Len != s.Len) throw new InvalidOperationException("firma de sesion distinta en la ranura " + k.ToString(CultureInfo.InvariantCulture));
				double ps = PrevClose(b, ss, srcOf[k]);
				for (int q = 0; q < d.Len; q++)
				{
					int a = d.Start + q, c0 = s.Start + q;
					double o = b.O[c0] - ps, h = b.H[c0] - ps, l = b.L[c0] - ps, c = b.C[c0] - ps;
					if (sign[k] >= 0) { O[a] = prev + o; H[a] = prev + h; L[a] = prev + l; C[a] = prev + c; }
					else { O[a] = prev - o; H[a] = prev - l; L[a] = prev - h; C[a] = prev - c; }
					if (vol != null) outVol[a] = vol[c0];
				}
				prev = C[d.End];
			}
			double mn = double.MaxValue; for (int i = 0; i < n; i++) if (L[i] < mn) mn = L[i];
			double sh = 0; if (mn < minLevel) sh = Math.Ceiling((minLevel - mn) * 4.0) / 4.0;
			for (int i = 0; i < n; i++) { O[i] = NQCE_Const.Rt(O[i] + sh); H[i] = NQCE_Const.Rt(H[i] + sh); L[i] = NQCE_Const.Rt(L[i] + sh); C[i] = NQCE_Const.Rt(C[i] + sh); }
			return new NQCE_Bars((long[])b.T.Clone(), O, H, L, C);
		}

		// Validez: H>=max(O,C), L<=min(O,C), rejilla de tick, T creciente. Devuelve "" si todo bien.
		public static string Validate(NQCE_Bars b)
		{
			for (int i = 0; i < b.Count; i++)
			{
				if (b.H[i] < Math.Max(b.O[i], b.C[i]) || b.L[i] > Math.Min(b.O[i], b.C[i])) return "OHLC incoherente en " + i.ToString(CultureInfo.InvariantCulture);
				if (b.O[i] != NQCE_Const.Rt(b.O[i]) || b.H[i] != NQCE_Const.Rt(b.H[i]) || b.L[i] != NQCE_Const.Rt(b.L[i]) || b.C[i] != NQCE_Const.Rt(b.C[i])) return "fuera de la rejilla de tick en " + i.ToString(CultureInfo.InvariantCulture);
				if (i > 0 && b.T[i] <= b.T[i - 1]) return "T no creciente en " + i.ToString(CultureInfo.InvariantCulture);
			}
			return "";
		}

		// SHA256 del dataset (T, O, H, L, C, volumen) en binario IEEE little-endian.
		public static string Hash(NQCE_Bars b, double[] vol)
		{
			using (SHA256 sha = SHA256.Create())
			{
				for (int i = 0; i < b.Count; i++)
				{
					byte[] x = new byte[48 + (vol == null ? 0 : 8)];
					Buffer.BlockCopy(BitConverter.GetBytes(b.T[i]), 0, x, 0, 8);
					Buffer.BlockCopy(BitConverter.GetBytes(b.O[i]), 0, x, 8, 8);
					Buffer.BlockCopy(BitConverter.GetBytes(b.H[i]), 0, x, 16, 8);
					Buffer.BlockCopy(BitConverter.GetBytes(b.L[i]), 0, x, 24, 8);
					Buffer.BlockCopy(BitConverter.GetBytes(b.C[i]), 0, x, 32, 8);
					if (vol != null) Buffer.BlockCopy(BitConverter.GetBytes(vol[i]), 0, x, 40, 8);
					sha.TransformBlock(x, 0, x.Length, null, 0);
				}
				sha.TransformFinalBlock(new byte[0], 0, 0);
				return NQCE_Const.Hex(sha.Hash);
			}
		}
	}

	// ------------------------------------------------------------------ N1
	public sealed class NQCET2_N1 : NQCRUN_IDatasetTransform
	{
		private long seed; private HashSet<int> fix; private double minLevel;
		public int FreeSlots, MovedSlots, Classes;
		public NQCET2_N1(long seed, HashSet<int> fixedDays) { this.seed = seed; fix = fixedDays == null ? NQCE_Const.ExcludedSet() : fixedDays; minLevel = 1000.0; }
		public string Name { get { return "N1"; } }
		public string ParamsText { get { return "seed=" + seed.ToString(CultureInfo.InvariantCulture) + ";fixed_days=" + fix.Count.ToString(CultureInfo.InvariantCulture) + ";slot0_fixed=1;min_level=" + minLevel.ToString("0", CultureInfo.InvariantCulture) + ";core=" + NQCET2_Core.Version; } }

		public void Apply(NQCE_Bars bars, double[] volume, out NQCE_Bars outBars, out double[] outVolume)
		{
			List<NQCET2_Sess> ss = NQCET2_Core.Split(bars);
			int[] src = new int[ss.Count], sign = new int[ss.Count];
			Dictionary<string, List<int>> cls = new Dictionary<string, List<int>>(); List<string> order = new List<string>();
			for (int k = 0; k < ss.Count; k++)
			{
				src[k] = k; sign[k] = 1;
				if (k == 0 || fix.Contains(ss[k].DayKey)) continue;
				List<int> li; if (!cls.TryGetValue(ss[k].Sig, out li)) { li = new List<int>(); cls[ss[k].Sig] = li; order.Add(ss[k].Sig); }
				li.Add(k);
			}
			NQCE_Rng rng = new NQCE_Rng(seed); FreeSlots = 0; MovedSlots = 0; Classes = order.Count;
			foreach (string key in order)
			{
				List<int> slots = cls[key]; int m = slots.Count; int[] perm = new int[m]; for (int i = 0; i < m; i++) perm[i] = slots[i];
				for (int i = m - 1; i > 0; i--) { int j = rng.NextInt(i + 1); int t = perm[i]; perm[i] = perm[j]; perm[j] = t; }   // Fisher-Yates
				for (int i = 0; i < m; i++) { src[slots[i]] = perm[i]; FreeSlots++; if (perm[i] != slots[i]) MovedSlots++; }
			}
			outBars = NQCET2_Core.Rebuild(bars, volume, ss, src, sign, minLevel, out outVolume);
		}
	}

	// ------------------------------------------------------------------ N2
	public sealed class NQCET2_N2 : NQCRUN_IDatasetTransform
	{
		private long seed; private double minLevel;
		public int Flipped, Sessions;
		public NQCET2_N2(long seed) { this.seed = seed; minLevel = 1000.0; }
		public string Name { get { return "N2"; } }
		public string ParamsText { get { return "seed=" + seed.ToString(CultureInfo.InvariantCulture) + ";anchor=cierre_previo_de_la_salida;min_level=" + minLevel.ToString("0", CultureInfo.InvariantCulture) + ";core=" + NQCET2_Core.Version; } }

		public void Apply(NQCE_Bars bars, double[] volume, out NQCE_Bars outBars, out double[] outVolume)
		{
			List<NQCET2_Sess> ss = NQCET2_Core.Split(bars);
			int[] src = new int[ss.Count], sign = new int[ss.Count];
			NQCE_Rng rng = new NQCE_Rng(seed); Flipped = 0; Sessions = ss.Count;
			for (int k = 0; k < ss.Count; k++) { src[k] = k; sign[k] = rng.NextInt(2) == 0 ? 1 : -1; if (sign[k] < 0) Flipped++; }
			outBars = NQCET2_Core.Rebuild(bars, volume, ss, src, sign, minLevel, out outVolume);
		}
	}
}
