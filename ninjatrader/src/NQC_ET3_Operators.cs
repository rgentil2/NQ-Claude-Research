#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
#endregion

//==============================================================================
// NQC_ET3_Operators - WP3 operadores geneticos (ET3). SOLO DESARROLLO, sin compilar.
// Todo el azar sale de NQCE_Rng con la semilla declarada; no se usa System.Random ni el reloj.
// Parametros D-019: poblacion 500, 200 generaciones, torneo 3, cruce uniforme, mutacion 5%/gen, elitismo 5%.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public sealed class NQCE3_Params
	{
		public int PopSize = 500, Generations = 200, Tournament = 3, MaxInitTries = 50;
		public double MutationRate = 0.05, EliteFrac = 0.05;
		public int InitMaxActive = 4;       // ranuras activas iniciales 1..InitMaxActive
		public long Seed = 20261114;        // C1; DEBE declararse en docs/stages/ET3.md antes de correr
	}

	public static class NQCE3_Operators
	{
		public static int RandomParamLevel(NQCE_Rng r, INQCE3_Library lib, int condId)
		{
			// D-030: periodo y umbral se sortean por separado y se empaquetan (2 NextInt por llamada, orden fijo)
			int pl = r.NextInt(Math.Max(1, lib.PeriodLevels(condId))), tl = r.NextInt(Math.Max(1, lib.ThresholdLevels(condId)));
			return NQCE3_Genome.Pack(lib, condId, pl, tl);
		}

		public static NQCE3_Genome RandomGenome(NQCE_Rng r, INQCE3_Library lib, NQCE3_Params p)
		{
			NQCE3_Genome g = new NQCE3_Genome();
			int act = 1 + r.NextInt(p.InitMaxActive);
			for (int k = 0; k < act; k++)
			{
				int s = r.NextInt(NQCE3_Genome.Slots);
				int c = 1 + r.NextInt(lib.ConditionCount);
				g.G[2 * s] = c; g.G[2 * s + 1] = RandomParamLevel(r, lib, c);
			}
			g.G[NQCE3_Genome.GDir] = r.NextInt(2);
			g.G[NQCE3_Genome.GAtr] = NQCE3_Genome.AtrMin + r.NextInt(NQCE3_Genome.AtrMax - NQCE3_Genome.AtrMin + 1);
			g.G[NQCE3_Genome.GKsl] = r.NextInt(NQCE3_Genome.KslLevels);
			g.G[NQCE3_Genome.GKtp] = r.NextInt(NQCE3_Genome.KtpLevels);
			g.Repair(lib);
			return g;
		}

		// Inicializacion con frecuencia minima de senal: acepta(g) debe ser barata y determinista.
		// Reintenta hasta MaxInitTries; si ninguno pasa, devuelve el ultimo (el fitness lo penalizara).
		public static NQCE3_Genome InitWithMinSignal(NQCE_Rng r, INQCE3_Library lib, NQCE3_Params p, Func<NQCE3_Genome, bool> accept)
		{
			NQCE3_Genome g = null;
			for (int t = 0; t < p.MaxInitTries; t++)
			{
				g = RandomGenome(r, lib, p);
				if (accept == null || accept(g)) return g;
			}
			return g;
		}

		// Torneo: devuelve el indice del mejor entre k elegidos al azar (mayor fitness; empate: menor indice).
		public static int Tournament(NQCE_Rng r, double[] fit, int k)
		{
			int best = r.NextInt(fit.Length);
			for (int i = 1; i < k; i++)
			{
				int c = r.NextInt(fit.Length);
				if (fit[c] > fit[best] || (fit[c] == fit[best] && c < best)) best = c;
			}
			return best;
		}

		// Cruce uniforme gen a gen (el orden de consumo del azar es fijo: un NextInt por gen).
		public static NQCE3_Genome Crossover(NQCE_Rng r, NQCE3_Genome a, NQCE3_Genome b, INQCE3_Library lib)
		{
			NQCE3_Genome c = new NQCE3_Genome();
			for (int i = 0; i < NQCE3_Genome.GeneCount; i++) c.G[i] = r.NextInt(2) == 0 ? a.G[i] : b.G[i];
			// el nivel de parametro viaja con su condicion: si se tomo la condicion de un padre, se toma su nivel
			for (int s = 0; s < NQCE3_Genome.Slots; s++)
			{
				if (c.G[2 * s] == a.G[2 * s]) c.G[2 * s + 1] = a.G[2 * s + 1];
				else if (c.G[2 * s] == b.G[2 * s]) c.G[2 * s + 1] = b.G[2 * s + 1];
			}
			c.Repair(lib);
			return c;
		}

		// Mutacion: cada gen con probabilidad MutationRate (un NextDouble por gen, siempre consumido).
		public static void Mutate(NQCE_Rng r, NQCE3_Genome g, NQCE3_Params p, INQCE3_Library lib)
		{
			for (int i = 0; i < NQCE3_Genome.GeneCount; i++)
			{
				double u = r.NextDouble();
				if (u >= p.MutationRate) continue;
				if (i < 16)
				{
					int s = i / 2;
					if (i % 2 == 0)
					{
						int c = r.NextInt(lib.ConditionCount + 1);     // 0 = vaciar ranura
						g.G[i] = c; g.G[i + 1] = c == 0 ? 0 : RandomParamLevel(r, lib, c);
					}
					else if (g.G[2 * s] != 0)
					{
						// D-030: un solo componente muta por vez, el periodo o el umbral (siempre 2 sorteos: dimension y valor)
						int c = g.G[2 * s], pl, tl; NQCE3_Genome.Unpack(lib, c, g.G[i], out pl, out tl);
						int dim = r.NextInt(2);
						int nvP = r.NextInt(Math.Max(1, lib.PeriodLevels(c))), nvT = r.NextInt(Math.Max(1, lib.ThresholdLevels(c)));
						if (dim == 0 || lib.ThresholdLevels(c) <= 1) pl = nvP; else tl = nvT;
						g.G[i] = NQCE3_Genome.Pack(lib, c, pl, tl);
					}
				}
				else if (i == NQCE3_Genome.GDir) g.G[i] = 1 - g.G[i];
				else if (i == NQCE3_Genome.GAtr) g.G[i] = NQCE3_Genome.AtrMin + r.NextInt(NQCE3_Genome.AtrMax - NQCE3_Genome.AtrMin + 1);
				else if (i == NQCE3_Genome.GKsl) g.G[i] = r.NextInt(NQCE3_Genome.KslLevels);
				else g.G[i] = r.NextInt(NQCE3_Genome.KtpLevels);
			}
			g.Repair(lib);
		}

		// Indices ordenados por fitness descendente (empate: menor indice) para elitismo determinista.
		public static int[] RankDesc(double[] fit)
		{
			int n = fit.Length; int[] idx = new int[n]; for (int i = 0; i < n; i++) idx[i] = i;
			Array.Sort(idx, delegate(int x, int y)
			{
				int c = fit[y].CompareTo(fit[x]); return c != 0 ? c : x.CompareTo(y);
			});
			return idx;
		}

		// Genera la siguiente poblacion: elite + hijos (torneo, cruce, mutacion).
		public static List<NQCE3_Genome> NextGeneration(NQCE_Rng r, List<NQCE3_Genome> pop, double[] fit, NQCE3_Params p, INQCE3_Library lib)
		{
			List<NQCE3_Genome> next = new List<NQCE3_Genome>();
			int[] rank = RankDesc(fit);
			int elite = (int)Math.Ceiling(p.EliteFrac * p.PopSize);
			for (int i = 0; i < elite && i < rank.Length; i++) next.Add(pop[rank[i]].Clone());
			while (next.Count < p.PopSize)
			{
				int ia = Tournament(r, fit, p.Tournament), ib = Tournament(r, fit, p.Tournament);
				NQCE3_Genome ch = Crossover(r, pop[ia], pop[ib], lib);
				Mutate(r, ch, p, lib);
				next.Add(ch);
			}
			return next;
		}
	}
}
