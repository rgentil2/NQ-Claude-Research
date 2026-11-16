#region Using declarations
using System;
using System.Globalization;
using System.Text;
#endregion

//==============================================================================
// NQC_RUN_NeffAdapter - Adaptador A6 (D-038) entre el reservorio de ET3 (muestra uniforme de series diarias de TODOS los ensayos evaluados) y ET7.
// Programado contra la firma acordada:
//     public static NQCE7_NeffResult NQCE7_Neff.EstimateFromSample(double[][] sampleSeries, long populationTrials, double cut)
// (NQC_ET7_Neff.cs: clusters observados, f1, f2, Chao1 con correccion de sesgo, N_eff estimado, limites inferior/superior y cobertura de la muestra).
// Este adaptador SOLO formatea el resultado; el veredicto de ET7 (NQCE7_Evidence.Evaluate) aun usa el N_eff de D-036 hasta que el orquestador integre ET7.
// SOLO DESARROLLO. Sin ordenes.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public static class NQCRUN_NeffAdapter
	{
		// Identificador del adaptador: entra en el hash de entrada de la etapa ET7 (si cambia el estimador cambia el checkpoint).
		public const string Id = "NEFF-SAMPLE-1:NQCE7_Neff.EstimateFromSample";

		private static string R(double v) { return double.IsNaN(v) ? "NaN" : (double.IsPositiveInfinity(v) ? "INF" : (double.IsNegativeInfinity(v) ? "-INF" : v.ToString("R", CultureInfo.InvariantCulture))); }

		// status: "OK" o "FALLA: <causa>" (falla cerrada del estimador). Texto determinista.
		public static string Estimate(double[][] sample, long populationTrials, double cut, out string status)
		{
			NQCE7_NeffResult e = NQCE7_Neff.EstimateFromSample(sample, populationTrials, cut);
			status = e.Falla.Length == 0 ? "OK" : "FALLA: " + e.Falla;
			StringBuilder b = new StringBuilder();
			b.Append("estimador=Chao1_sesgo_corregido corte=").Append(R(cut)).Append(" estado=").Append(status).Append('\n');
			b.Append("n_muestra=").Append(e.NMuestra.ToString(CultureInfo.InvariantCulture)).Append(" n_poblacion=").Append(e.NPoblacion.ToString(CultureInfo.InvariantCulture)).Append('\n');
			b.Append("grupos_observados=").Append(e.GruposObservados.ToString(CultureInfo.InvariantCulture)).Append(" f1=").Append(e.F1.ToString(CultureInfo.InvariantCulture)).Append(" f2=").Append(e.F2.ToString(CultureInfo.InvariantCulture)).Append('\n');
			b.Append("n_eff_estimado=").Append(R(e.NEffEstimado)).Append(" inferior=").Append(R(e.Inferior)).Append(" superior=").Append(R(e.Superior)).Append(" cobertura_muestra=").Append(R(e.CoberturaMuestra)).Append(" var_chao=").Append(R(e.VarChao)).Append('\n');
			return b.ToString();
		}
	}
}
