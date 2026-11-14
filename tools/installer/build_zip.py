#!/usr/bin/env python3
"""Genera el ZIP de instalacion de NinjaScript desde ninjatrader/src/ (D-013).
Uso: build_zip.py <nombre_zip_sin_ext> <archivo1.cs> [archivo2.cs ...] [--leeme "texto"] [--out dir]
Escribe <out>/<nombre>.zip con los .cs, INSTALAR.cmd (multi), RUTA_NT8.txt, HASH_SHA256.txt y LEEME.txt."""
import sys, os, hashlib, zipfile
root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
args = sys.argv[1:]
out = os.path.join(root, "dist"); leeme = ""
if "--out" in args: i = args.index("--out"); out = args[i+1]; del args[i:i+2]
if "--leeme" in args: i = args.index("--leeme"); leeme = args[i+1]; del args[i:i+2]
name, files = args[0], args[1:]
src = os.path.join(root, "ninjatrader", "src"); inst = os.path.join(root, "ninjatrader", "installer")
hashes = []
for f in files:
    h = hashlib.sha256(open(os.path.join(src, f), "rb").read()).hexdigest()
    hashes.append("%s  %s" % (h, f))
os.makedirs(out, exist_ok=True)
path = os.path.join(out, name + ".zip")
with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
    for f in files: z.write(os.path.join(src, f), f)
    z.write(os.path.join(inst, "INSTALAR_MULTI.cmd"), "INSTALAR.cmd")
    z.write(os.path.join(inst, "RUTA_NT8.txt"), "RUTA_NT8.txt")
    z.writestr("HASH_SHA256.txt", "\r\n".join(hashes) + "\r\n")
    z.writestr("LEEME.txt", "Paquete %s\r\n\r\n1. Descomprime.\r\n2. Doble clic en INSTALAR.cmd.\r\n3. En NinjaTrader: NinjaScript Editor, F5 (debe decir Compile successful).\r\n\r\n%s\r\nHashes SHA256:\r\n%s\r\n" % (name, leeme, "\r\n".join(hashes)))
print(path); print("\n".join(hashes))
