#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
#endregion

//==============================================================================
// NQC_ET3_Reservoir - Muestreo de RESERVORIO (algoritmo R de Vitter) de series diarias de P&L neto de los ensayos evaluados (D-038). Contrato NQC 2.0.0.
// SOLO DESARROLLO. Sin ordenes. Cambio ADITIVO de ET3 (NQCE3_Fitness.Reservoir, nulo por defecto: sin reservorio nada cambia).
//
// QUE HACE: cada evaluacion NUEVA (ensayo del ledger, no los aciertos de cache) ofrece su serie diaria (NQCE_Result.DailyNet) al reservorio. Despues de n ofertas el
//   reservorio guarda k = min(K, n) series y cada ensayo ofrecido tiene EXACTAMENTE la misma probabilidad k/n de estar en la muestra (uniforme, sin sesgo hacia el top-K).
//   Es la muestra que necesita el estimador de riqueza (Chao1) de ET7 para acotar el N_eff de TODA la poblacion de ensayos (un top-K NO sirve: no es aleatorio).
// AZAR: NQCE_Rng PROPIO sembrado con  semilla_del_GA XOR SeedMix  (no toca el generador de la campana: el GA, el ledger y el databank son identicos con o sin reservorio).
//   Tras llenarse, cada oferta consume exactamente una llamada a NextInt(n_vistos): determinista.
// EXCLUSION: un genoma vacio (sin condiciones) no ejecuta el motor y no ofrece serie; no se cuenta en Seen (su numero se informa aparte como ensayos sin serie).
//
// COSTO DE MEMORIA: K x (T x 8 bytes + ~100 bytes de cabecera de arreglo e id) con T = sesiones del IS (~780 para 2020-2022; ver DailyNet). K = 5.000 -> ~31 MB gestionados
//   (K = 20.000 -> ~125 MB). Las series desplazadas se liberan; no se copian (se guarda la referencia al arreglo que el ejecutor creo para ese ensayo).
//   En disco: archivo binario ET3_NEFF_SAMPLE.bin = 40 bytes de cabecera + K x (64 + 8T) bytes (~31 MB para K = 5.000); se hashea en el checkpoint de ET3.
// FORMATO .bin (little-endian): "NQCNEFF1"(8) | int32 count | int32 T | int64 seen | int64 seed | luego count x { id ascii 64 bytes | T x float64 }.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public sealed class NQCE3_Reservoir
	{
		public const long SeedMix = 0x5DEECE66DL;
		public const int DefaultK = 5000;
		public readonly int K; public readonly long GaSeed;
		public long Seen;                                        // ensayos con serie ofrecidos
		private NQCE_Rng rng; private List<string> ids = new List<string>(); private List<double[]> data = new List<double[]>();

		public NQCE3_Reservoir(int k, long gaSeed)
		{
			if (k < 1) throw new ArgumentOutOfRangeException("k");
			K = k; GaSeed = gaSeed; rng = new NQCE_Rng(gaSeed ^ SeedMix);
		}
		public int Count { get { return ids.Count; } }
		public string Id(int i) { return ids[i]; }
		public double[] Series(int i) { return data[i]; }

		public void Offer(string trialId, double[] daily)
		{
			if (daily == null) throw new ArgumentNullException("daily");
			Seen++;
			if (ids.Count < K) { ids.Add(trialId); data.Add(daily); return; }
			int j = rng.NextInt((int)Seen);                      // uniforme en [0, Seen)
			if (j < K) { ids[j] = trialId; data[j] = daily; }
		}

		// Series que no varian (todas ceros, p. ej. sin operaciones): el agrupamiento por correlacion no las puede comparar (correlacion NaN).
		public int ConstantSeries()
		{
			int c = 0;
			foreach (double[] s in data) { bool k = true; for (int i = 1; i < s.Length; i++) if (s[i] != s[0]) { k = false; break; } if (k) c++; }
			return c;
		}

		public void Save(string path)
		{
			string dir = Path.GetDirectoryName(path); if (dir != null && dir.Length > 0) Directory.CreateDirectory(dir);
			int T = data.Count == 0 ? 0 : data[0].Length;
			using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
			using (BinaryWriter w = new BinaryWriter(fs))
			{
				w.Write(Encoding.ASCII.GetBytes("NQCNEFF1")); w.Write(data.Count); w.Write(T); w.Write(Seen); w.Write(GaSeed);
				for (int i = 0; i < data.Count; i++)
				{
					if (data[i].Length != T) throw new InvalidOperationException("reservorio: series de longitud distinta");
					byte[] id = new byte[64]; byte[] b = Encoding.ASCII.GetBytes(ids[i]); Array.Copy(b, id, Math.Min(64, b.Length)); w.Write(id);
					for (int t = 0; t < T; t++) w.Write(data[i][t]);
				}
			}
		}

		public static double[][] Load(string path, out string[] trialIds, out long seen, out long gaSeed)
		{
			using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
			using (BinaryReader r = new BinaryReader(fs))
			{
				string magic = Encoding.ASCII.GetString(r.ReadBytes(8)); if (magic != "NQCNEFF1") throw new InvalidDataException("reservorio: formato desconocido");
				int n = r.ReadInt32(), T = r.ReadInt32(); seen = r.ReadInt64(); gaSeed = r.ReadInt64();
				if (n < 0 || T < 0) throw new InvalidDataException("reservorio: cabecera invalida");
				double[][] s = new double[n][]; trialIds = new string[n];
				for (int i = 0; i < n; i++) { trialIds[i] = Encoding.ASCII.GetString(r.ReadBytes(64)).TrimEnd('\0'); s[i] = new double[T]; for (int t = 0; t < T; t++) s[i][t] = r.ReadDouble(); }
				return s;
			}
		}

		// Memoria gestionada estimada (bytes) de una muestra de K series de T sesiones.
		public static long EstimateBytes(int k, int sessions) { return (long)k * ((long)sessions * 8L + 100L); }
	}
}
