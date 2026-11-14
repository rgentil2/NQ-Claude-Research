using System;
using System.Collections.Generic;
using System.Globalization;

//==============================================================================
// NQC_ET7_MonteCarlo - Bootstrap por bloques de operaciones ejecutadas (ET7). Contrato NQC 2.0.0. SOLO DESARROLLO. Biblioteca PURA.
// Depende de NQC_ET1_Core.cs (NQCE_Rng) y NQC_ET1C_AccountSim.cs (NQC_AccountSim.Rolling).
// Metodo: la secuencia de n operaciones ejecutadas del IS se remuestrea por bloques circulares de 8 (inicio uniforme en [0,n)), recortada a n.
//   La operacion en la posicion i ocupa la sesion de la operacion original i (el calendario de sesiones se conserva; las sesiones sin operaciones quedan en 0).
//   DD por trayectoria: pico a valle de la equity cerrada por operacion, con el minimo abierto de cada operacion (min(MinOpen, Net)) sumado al balance previo.
//   P95 del DD = cuantil ceil(0,95*K)-esimo (K trayectorias). P_pass bootstrap = media de NQC_AccountSim.Rolling sobre las filas de sesion de cada trayectoria
//   (arranques rodantes, censurados excluidos; trayectorias sin decididos se omiten y se cuentan). NO corrige el sesgo de seleccion: solo mide dispersion.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public sealed class NQCE7_TradeRec
	{
		public int Session; public double Net, MinOpen;   // Session: indice 0..S-1 en el calendario de sesiones (no decreciente)
		public NQCE7_TradeRec(int session, double net, double minOpen) { Session = session; Net = net; MinOpen = minOpen; }
	}

	public sealed class NQCE7_McResult
	{
		public int Paths, Block, Trades; public long Seed;
		public double DdP95, DdMean, DdMax, DdMedian, PpassBoot; public int PpassPaths; public string Fail = "";
		public double[] Dd;
	}

	public static class NQCE7_MonteCarlo
	{
		public static NQCE7_McResult Run(List<NQCE7_TradeRec> trades, int sessions, int paths, int block, long seed, NQC_AccountProfile profile)
		{
			NQCE7_McResult R = new NQCE7_McResult(); R.Paths = paths; R.Block = block; R.Seed = seed; R.DdP95 = double.NaN; R.PpassBoot = double.NaN;
			int n = trades == null ? 0 : trades.Count; R.Trades = n;
			if (n < block * 2) { R.Fail = "operaciones insuficientes (" + n.ToString(CultureInfo.InvariantCulture) + ")"; return R; }
			if (paths < 1 || block < 1 || sessions < 1) { R.Fail = "parametros invalidos"; return R; }
			for (int i = 0; i < n; i++) { if (trades[i].Session < 0 || trades[i].Session >= sessions || (i > 0 && trades[i].Session < trades[i - 1].Session)) { R.Fail = "calendario de sesiones invalido"; return R; } }
			NQCE_Rng rng = new NQCE_Rng(seed);
			double[] dd = new double[paths]; double ppSum = 0; int ppN = 0;
			int[] idx = new int[n];
			for (int p = 0; p < paths; p++)
			{
				int k = 0;
				while (k < n) { int st = rng.NextInt(n); for (int b = 0; b < block && k < n; b++, k++) idx[k] = (st + b) % n; }
				double cum = 0, peak = 0, maxDd = 0;
				List<NQC_SessionRow> rows = new List<NQC_SessionRow>(sessions); for (int s = 0; s < sessions; s++) rows.Add(null);
				double sp = 0, smin = 0; int sn = 0; int cur = trades[0].Session;
				for (int i = 0; i < n; i++)
				{
					NQCE7_TradeRec slot = trades[i], src = trades[idx[i]];
					if (slot.Session != cur) { rows[cur] = new NQC_SessionRow(sp, smin, sn); cur = slot.Session; sp = 0; smin = 0; sn = 0; }
					double low = Math.Min(src.MinOpen, src.Net);
					double dv = peak - (cum + low); if (dv > maxDd) maxDd = dv;
					smin = Math.Min(smin, sp + low);
					cum += src.Net; sp += src.Net; sn++;
					if (cum > peak) peak = cum;
					double dc = peak - cum; if (dc > maxDd) maxDd = dc;
				}
				rows[cur] = new NQC_SessionRow(sp, smin, sn);
				for (int s = 0; s < sessions; s++) if (rows[s] == null) rows[s] = new NQC_SessionRow(0, 0, 0);
				dd[p] = maxDd;
				int dec; double pp = NQC_AccountSim.Rolling(rows, profile, out dec);
				if (!double.IsNaN(pp)) { ppSum += pp; ppN++; }
			}
			R.Dd = dd; double[] s2 = (double[])dd.Clone(); Array.Sort(s2);
			int q = (int)Math.Ceiling(0.95 * paths) - 1; if (q < 0) q = 0;
			R.DdP95 = s2[q]; R.DdMax = s2[paths - 1]; R.DdMedian = s2[(paths - 1) / 2];
			double m = 0; foreach (double v in dd) m += v; R.DdMean = m / paths;
			R.PpassPaths = ppN; R.PpassBoot = ppN == 0 ? double.NaN : ppSum / ppN;
			return R;
		}
	}
}
