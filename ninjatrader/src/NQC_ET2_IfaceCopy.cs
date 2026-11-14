using System;
using System.Collections.Generic;

//==============================================================================
// NQC_ET2_IfaceCopy - copia identica de NQC_RUN; unificar.
// Interfaz de transformacion de dataset que define el runner NQC_RUN_Funnel (agente ET3) y que
// implementan los generadores de ET2. Esta copia existe SOLO porque ET2 compila sin NQC_RUN.
// Cuando NQC_RUN declare NQCRUN_IDatasetTransform en el repositorio, BORRAR este archivo
// (si no, habra un tipo duplicado). SOLO DESARROLLO.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public interface NQCRUN_IDatasetTransform
	{
		string Name { get; }
		string ParamsText { get; }
		void Apply(NQCE_Bars bars, double[] volume, out NQCE_Bars outBars, out double[] outVolume);
	}
}
