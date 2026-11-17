#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
#endregion

//==============================================================================
// NQC_ET3_FitnessV2Test (D-039) - prueba SINTETICA de la aptitud v2: mundo nulo (barras aleatorias M1, ~3 anos) con la biblioteca real.
// Mide la distribucion del mejor t y del numero de operaciones de los top-K bajo T/100, T/250+cobertura, T_SHRUNK/100 y T_SHRUNK/250+cobertura.
// NO es mercado ni evidencia de NT8. Uso: NQC_ET3_FitnessV2Test.Run(days, worldSeed, gaSeed, pop, gen, threads) devuelve la tabla.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public static class NQC_ET3_FitnessV2Test
	{
		private static string F(double v, string f) { return double.IsNaN(v) ? "NA" : v.ToString(f, CultureInfo.InvariantCulture); }
		private static double Q(List<double> s, double q) { if (s.Count == 0) return double.NaN; s.Sort(); int i = (int)Math.Ceiling(q * s.Count) - 1; if (i < 0) i = 0; return s[i]; }

		// Comprobaciones de cableado (sin evaluar barras). Devuelve lineas "PASS|FAIL nombre".
		public static string Wiring()
		{
			StringBuilder sb = new StringBuilder(); string e;
			NQCRUN_Params d = new NQCRUN_Params();
			NQCRUN_Params q = NQCRUN_Params.Parse("et3.fitness_mode=t\net3.min_trades=100\net3.min_months_covered=0\net6.pool_balance=1\n", out e);
			NQCRUN_Params l = NQCRUN_Params.Parse("et3.fitness_mode=T\net3.min_trades=100\net3.min_months_covered=0\n", out e);
			NQCRUN_Params bad = NQCRUN_Params.Parse("et3.fitness_mode=XX\n", out e); string be = bad == null ? "" : bad.Validate();
			NQCE3_Params gp = new NQCE3_Params(); NQCRUN_Params z = new NQCRUN_Params(); z.Fitness = new NQCE3_FitnessSpec();
			Chk(sb, "defecto de la repeticion = T_SHRUNK/250/24", d.Fitness.Mode == "T_SHRUNK" && d.Fitness.MinTrades == 250 && d.Fitness.MinMonths == 24);
			Chk(sb, "legado (T/100/0) no cambia el texto canonico (hashes anteriores)", l != null && !l.CanonicalText(gp).Contains("et3_fitness") && l.CanonicalText(gp) == z.CanonicalText(gp));
			Chk(sb, "T_SHRUNK y pool_balance cambian y sellan el texto canonico", d.CanonicalText(gp).Contains("et3_fitness: mode=T_SHRUNK min_trades=250 min_months=24") && q != null && q.Et6PoolBalance && q.CanonicalText(gp).Contains("et6_pool_balance=1") && d.CanonicalText(gp) != l.CanonicalText(gp));
			Chk(sb, "fitness_mode invalido se rechaza en Validate", bad != null && bad.Validate().Contains("fitness_mode"));
			string m = "batch=W\ncampaign id=A transform=N1INJ tseed=1 gaseed=2 target=60 tol=5\ncampaign id=B transform=N1INJ tseed=3 gaseed=4\ncampaign id=C transform=N1 tseed=5 gaseed=6 target=20\n";
			NQCRUN_BatchManifest m1 = NQCRUN_BatchManifest.Parse(m.Replace("campaign id=C transform=N1 tseed=5 gaseed=6 target=20\n", ""), out e);
			NQCRUN_BatchManifest m2 = NQCRUN_BatchManifest.Parse(m, out e); string e2 = e;
			Chk(sb, "target/tol por campana: A=60/5, B hereda el global; target en transform distinto de N1INJ se rechaza", m1 != null && m1.Campaigns[0].Target == 60 && m1.Campaigns[0].Tol == 5 && double.IsNaN(m1.Campaigns[1].Target) && m1.Campaigns[0].Spec(m1.Target, m1.Tol).Contains("target=60;tol=5") && m1.Campaigns[1].Spec(m1.Target, m1.Tol).Contains("target=20;tol=2") && m2 == null && e2.Contains("N1INJ"));
			return sb.ToString();
		}
		private static void Chk(StringBuilder sb, string n, bool ok) { sb.Append(ok ? "PASS " : "FAIL ").Append(n).Append('\n'); }

		public static string Run(int days, long worldSeed, long gaSeed, int pop, int gen, int threads)
		{
			double[] vol; NQCE_Bars bars = NQCE3_SynthM1.Make(days, worldSeed, out vol);
			StringBuilder sb = new StringBuilder();
			sb.Append("mundo=NULO_SINTETICO dias=").Append(days).Append(" barras=").Append(bars.Count).Append(" world_seed=").Append(worldSeed).Append(" ga_seed=").Append(gaSeed).Append(" pop=").Append(pop).Append(" gen=").Append(gen).Append('\n');
			sb.Append("celda\tevals\tvalidos_top\tbest_fit\tbest_tRaw\tmed_tRaw_top\tmax_tRaw_top\tmed_trades\tP5_trades\tpct_lt150\tpct_lt250\tmed_mean_net\tmed_meses\n");
			string[] names = { "T/100", "T/250+24m", "T_SHRUNK/100", "T_SHRUNK/250+24m" };
			for (int cell = 0; cell < 4; cell++)
			{
				NQCE3_FitnessSpec sp = new NQCE3_FitnessSpec(); sp.Mode = cell >= 2 ? "T_SHRUNK" : "T"; sp.MinTrades = (cell % 2 == 1) ? 250 : 100; sp.MinMonths = (cell % 2 == 1) ? 24 : 0;
				NQCE3_Library lib = new NQCE3_Library(); NQCE3_Fitness fit = new NQCE3_Fitness(bars, vol, new NQCE_Config(), lib); fit.Spec = sp;
				NQCE3_Params gp = new NQCE3_Params(); gp.PopSize = pop; gp.Generations = gen; gp.TopK = 200; gp.Seed = gaSeed;
				NQCE3_Campaign c = new NQCE3_Campaign(); c.Name = "V2"; c.Threads = threads; c.MaxEvals = pop * gen; c.Run(fit, lib, gp, null);
				List<NQCE3_Eval> top = c.Bank.Top(); List<double> tr = new List<double>(), nt = new List<double>(), mn = new List<double>(), mo = new List<double>(); double best = double.NaN, bestT = double.NaN; int l150 = 0, l250 = 0;
				foreach (NQCE3_Eval e in top) { tr.Add(e.TRaw); nt.Add(e.Trades); mn.Add(e.MeanNet); mo.Add(e.MonthsCovered); if (e.Trades < 150) l150++; if (e.Trades < 250) l250++; if (double.IsNaN(best) || e.Fitness > best) { best = e.Fitness; bestT = e.TRaw; } }
				double mx = double.NaN; foreach (double v in tr) if (double.IsNaN(mx) || v > mx) mx = v;
				int n = top.Count;
				sb.Append(names[cell]).Append('\t').Append(fit.FreshEvaluations).Append('\t').Append(n).Append('\t').Append(F(best, "0.00")).Append('\t').Append(F(bestT, "0.00")).Append('\t').Append(F(Q(tr, 0.5), "0.00")).Append('\t').Append(F(mx, "0.00")).Append('\t')
				  .Append(F(Q(nt, 0.5), "0")).Append('\t').Append(F(Q(nt, 0.05), "0")).Append('\t').Append(n == 0 ? "NA" : F(100.0 * l150 / n, "0")).Append('\t').Append(n == 0 ? "NA" : F(100.0 * l250 / n, "0")).Append('\t').Append(F(Q(mn, 0.5), "0.0")).Append('\t').Append(F(Q(mo, 0.5), "0")).Append('\n');
			}
			return sb.ToString();
		}
	}
}
