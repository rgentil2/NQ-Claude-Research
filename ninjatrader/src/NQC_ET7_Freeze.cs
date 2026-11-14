using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

//==============================================================================
// NQC_ET7_Freeze - Congelado del portafolio (D-023) y verificacion que ET8 llama ANTES de abrir el OOS. Contrato NQC 2.0.0. SOLO DESARROLLO. Biblioteca PURA.
// PORTFOLIO_FROZEN.txt (esquema NQC_PORTFOLIO_FROZEN/1), lineas clave=valor:
//   schema, contract, commit (hex 7..40), frozen_utc (lo da el llamador: sin reloj interno, determinista), spec_file, spec_sha256,
//   engine.<nombre.cs>=sha256 (uno por archivo del motor), engine_set_sha256 (hash de las lineas "nombre=sha" ordenadas), seal_sha256 (hash de todas las lineas anteriores).
// Los hashes son de los BYTES del archivo (sin normalizar saltos de linea: un cambio CRLF/LF cambia el hash y falla cerrado; fijar .gitattributes eol=lf).
// Archivos del motor minimos que ET8 debe exigir: NQC_ET1_Core.cs, NQC_ET1C_AccountSim.cs, NQC_ET3_Genome.cs, NQC_ET3_Library.cs, NQC_ET3_LibCore.cs,
//   NQC_ET3_LibFeatures.cs, NQC_ET3_LibMath.cs, NQC_ET3_Fitness.cs (el ejecutor vive en Core; Genome construye el ejecutor desde el spec).
// VERIFICACION (falla cerrada): cualquier error de lectura/analisis, archivo faltante, archivo no listado o listado de mas, hash distinto, commit distinto o sello roto => Ok=false.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	public sealed class NQCE7_VerifyResult
	{
		public bool Ok; public List<string> Errors = new List<string>();
		public void Bad(string s) { Ok = false; Errors.Add(s); }
	}

	public static class NQCE7_Freeze
	{
		public const string Schema = "NQC_PORTFOLIO_FROZEN/1";
		public static readonly string[] MinEngineFiles = new string[] { "NQC_ET1_Core.cs", "NQC_ET1C_AccountSim.cs", "NQC_ET3_Genome.cs", "NQC_ET3_Library.cs", "NQC_ET3_LibCore.cs", "NQC_ET3_LibFeatures.cs", "NQC_ET3_LibMath.cs", "NQC_ET3_Fitness.cs" };

		public static string Sha256File(string path)
		{
			using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create()) return NQCE_Const.Hex(sha.ComputeHash(File.ReadAllBytes(path)));
		}
		public static bool IsHex(string s, int min, int max)
		{
			if (s == null || s.Length < min || s.Length > max) return false;
			foreach (char c in s) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
			return true;
		}
		private static string EngineSetHash(SortedDictionary<string, string> eng)
		{
			StringBuilder b = new StringBuilder(); foreach (KeyValuePair<string, string> kv in eng) b.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
			return NQCE_Const.Sha256Text(b.ToString());
		}

		// Construye el texto de PORTFOLIO_FROZEN.txt. Lanza excepcion si falta algun archivo minimo o el commit no es hexadecimal.
		public static string Build(string specPath, IList<string> enginePaths, string commit, string frozenUtc)
		{
			if (!IsHex(commit, 7, 40)) throw new ArgumentException("commit invalido");
			if (string.IsNullOrEmpty(frozenUtc) || frozenUtc.IndexOf('\n') >= 0) throw new ArgumentException("frozen_utc invalido");
			SortedDictionary<string, string> eng = new SortedDictionary<string, string>(StringComparer.Ordinal);
			foreach (string p in enginePaths) { string nm = Path.GetFileName(p); if (eng.ContainsKey(nm)) throw new ArgumentException("motor duplicado " + nm); eng[nm] = Sha256File(p); }
			foreach (string req in MinEngineFiles) if (!eng.ContainsKey(req)) throw new ArgumentException("falta archivo del motor " + req);
			StringBuilder b = new StringBuilder();
			b.Append("schema=").Append(Schema).Append('\n');
			b.Append("contract=").Append(NQCE_Const.ContractVersion).Append('\n');
			b.Append("commit=").Append(commit.ToLowerInvariant()).Append('\n');
			b.Append("frozen_utc=").Append(frozenUtc).Append('\n');
			b.Append("spec_file=").Append(Path.GetFileName(specPath)).Append('\n');
			b.Append("spec_sha256=").Append(Sha256File(specPath)).Append('\n');
			foreach (KeyValuePair<string, string> kv in eng) b.Append("engine.").Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
			b.Append("engine_set_sha256=").Append(EngineSetHash(eng)).Append('\n');
			string body = b.ToString();
			return body + "seal_sha256=" + NQCE_Const.Sha256Text(body) + "\n";
		}

		// Verificacion para ET8. enginePaths = archivos del motor tal como estan HOY; commit = git rev-parse HEAD de hoy.
		public static NQCE7_VerifyResult Verify(string frozenText, string specPath, IList<string> enginePaths, string currentCommit)
		{
			NQCE7_VerifyResult R = new NQCE7_VerifyResult(); R.Ok = true;
			try
			{
				if (string.IsNullOrEmpty(frozenText)) { R.Bad("PORTFOLIO_FROZEN vacio"); return R; }
				string[] lines = frozenText.Split('\n');
				int last = lines.Length - 1; if (lines[last].Length != 0) { R.Bad("sin salto de linea final"); return R; }
				if (last < 2 || !lines[last - 1].StartsWith("seal_sha256=", StringComparison.Ordinal)) { R.Bad("sin sello"); return R; }
				int cut = frozenText.LastIndexOf("seal_sha256=", StringComparison.Ordinal);
				string seal = lines[last - 1].Substring(12);
				if (NQCE_Const.Sha256Text(frozenText.Substring(0, cut)) != seal) R.Bad("sello roto (archivo congelado alterado)");
				Dictionary<string, string> kv = new Dictionary<string, string>(); SortedDictionary<string, string> frozenEng = new SortedDictionary<string, string>(StringComparer.Ordinal);
				for (int i = 0; i < last - 1; i++)
				{
					int eq = lines[i].IndexOf('='); if (eq <= 0) { R.Bad("linea invalida " + (i + 1).ToString(CultureInfo.InvariantCulture)); continue; }
					string k = lines[i].Substring(0, eq), v = lines[i].Substring(eq + 1);
					if (k.StartsWith("engine.", StringComparison.Ordinal)) { string nm = k.Substring(7); if (frozenEng.ContainsKey(nm)) R.Bad("motor repetido " + nm); frozenEng[nm] = v; }
					else { if (kv.ContainsKey(k)) R.Bad("clave repetida " + k); kv[k] = v; }
				}
				if (!kv.ContainsKey("schema") || kv["schema"] != Schema) R.Bad("esquema distinto");
				foreach (string req in new string[] { "contract", "commit", "frozen_utc", "spec_file", "spec_sha256", "engine_set_sha256" }) if (!kv.ContainsKey(req)) R.Bad("falta " + req);
				if (!R.Ok) return R;
				if (kv["contract"] != NQCE_Const.ContractVersion) R.Bad("contrato distinto");
				if (!IsHex(currentCommit, 7, 40)) R.Bad("commit actual invalido");
				else if (!string.Equals(kv["commit"], currentCommit, StringComparison.OrdinalIgnoreCase)) R.Bad("commit actual " + currentCommit + " distinto del congelado " + kv["commit"]);
				if (!File.Exists(specPath)) R.Bad("spec no encontrado");
				else { if (Path.GetFileName(specPath) != kv["spec_file"]) R.Bad("nombre de spec distinto"); if (Sha256File(specPath) != kv["spec_sha256"]) R.Bad("hash del spec distinto"); }
				foreach (string req in MinEngineFiles) if (!frozenEng.ContainsKey(req)) R.Bad("congelado sin archivo del motor " + req);
				if (EngineSetHash(frozenEng) != kv["engine_set_sha256"]) R.Bad("engine_set_sha256 incoherente");
				HashSet<string> seen = new HashSet<string>();
				foreach (string p in enginePaths)
				{
					string nm = Path.GetFileName(p); seen.Add(nm);
					string h; if (!frozenEng.TryGetValue(nm, out h)) { R.Bad("archivo del motor no congelado: " + nm); continue; }
					if (!File.Exists(p)) { R.Bad("archivo del motor ausente: " + nm); continue; }
					if (Sha256File(p) != h) R.Bad("hash del motor distinto: " + nm);
				}
				foreach (string nm in frozenEng.Keys) if (!seen.Contains(nm)) R.Bad("archivo congelado no entregado a la verificacion: " + nm);
			}
			catch (Exception e) { R.Bad("excepcion en la verificacion: " + e.GetType().Name); }
			return R;
		}
	}
}
