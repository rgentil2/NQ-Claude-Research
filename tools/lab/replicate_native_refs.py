#!/usr/bin/env python3
"""
Replica fuera de linea de las referencias nativas N1-N9 (ET1 parte A2).
Herramienta de analisis del asistente; NO forma parte del flujo del usuario (D-002).

Entradas (no estan en el repo):
  --bars   BARS_WINDOW.csv   exportado por NQC_ET1A_BarsExport (barras 09:29-11:32 y valores NT8)
  --news   usd_high_medium_2020_2026.csv   calendario aprobado
  --books  carpeta con las 9 carpetas ET1A_N*/TRADES_NATIVE.tsv
Uso: python replicate_native_refs.py --bars B.csv --news N.csv --books ./ACUMULADO
Convenciones de NT8 que reproduce (100% de 8.110 operaciones): ver docs/ET1_REFERENCES.md.
"""
import argparse, glob, os
import numpy as np, pandas as pd

EXCL = set("2020-01-01,2020-03-09,2020-03-12,2020-03-16,2020-06-03,2020-10-23,2021-04-02,2021-08-24,2022-08-03,2022-08-30,2022-12-26,2023-01-02,2023-04-07,2023-12-25,2024-01-01,2024-07-19,2024-07-25,2024-08-05,2024-12-25,2025-01-01,2025-01-09,2025-06-18,2025-12-25,2026-01-01,2026-04-03".split(','))
CFG = {
    'N1': dict(atr='atr14', ksl=1.5, ktp=3.0), 'N2': dict(atr='atr20', ksl=2.0, ktp=4.0),
    'N3': dict(atr='atr14', ksl=2.0, ktp=None, maxd=99), 'N4': dict(atr='atr14', ksl=2.0, ktp=None),
    'N5': dict(atr='atr5', ksl=0.3, ktp=0.3), 'N6': dict(atr='atr14', ksl=3.0, ktp=None),
    'N7': dict(atr='atr50', ksl=6.0, ktp=None, timex=630), 'N8': dict(atr='atr14', ksl=1.5, ktp=3.0, maxd=2),
    'N9': dict(atr='atr14', ksl=1.5, ktp=0.5),
}

def rt(x):  # redondeo al tick de 0,25
    return np.round(x * 4) / 4

class Lab:
    def __init__(self, bars_path, news_path):
        b = pd.read_csv(bars_path, parse_dates=['time'])
        b['day'] = b.time.dt.strftime('%Y-%m-%d')
        self.days = {d: g.reset_index(drop=True) for d, g in b.groupby('day')}
        n = pd.read_csv(news_path, usecols=['date_et', 'time_et'])
        self.ev = np.array(sorted(pd.to_datetime(n.date_et + ' ' + n.time_et)), dtype='datetime64[ns]')

    def in_blk(self, c):  # c en [t-5, t+5) de algun evento
        c = np.datetime64(c, 'ns')
        i = np.searchsorted(self.ev, c - np.timedelta64(5, 'm'), side='right')
        return i < len(self.ev) and self.ev[i] <= c + np.timedelta64(5, 'm')

def signal(ref, stamp, row, st):
    if ref == 'N8':
        return (1, 1.5, 3.0) if stamp == 575 else ((-1, 2.5, 1.0) if stamp == 578 else (0, None, None))
    if ref in ('N1', 'N5', 'N9'): return (1 if stamp == 575 else 0), None, None
    if ref == 'N2': return (-1 if stamp == 580 else 0), None, None
    if ref == 'N3': return (1 if (row.ema5 > row.ema13 and row.ema5_prev <= row.ema13_prev) else 0), None, None
    if ref == 'N4':
        if stamp <= 585 or st['rh'] is None: return 0, None, None
        return (1 if row.close > st['rh'] else 0), None, None
    if ref == 'N6': return (1 if stamp == 689 else 0), None, None
    if ref == 'N7': return (1 if stamp == 580 else 0), None, None

def run(lab, ref, gap='stop'):
    cfg = CFG[ref]; out = []
    for day, g in lab.days.items():
        if day in EXCL: continue
        pos = None; pending = None; trades_today = 0; rh = None; st = {'rh': None}
        for j in range(len(g)):
            row = g.iloc[j]; c = row.time; stamp = c.hour * 60 + c.minute
            o, h, l = row.open, row.high, row.low
            # 1) ejecuciones dentro de la barra j
            if pending and pending[0] == 'ENTER':
                _, d, ksl, ktp, sa, stime = pending; pending = None
                stop = rt(o - ksl * sa) if d == 1 else rt(o + ksl * sa)
                tp = (rt(o + ktp * sa) if d == 1 else rt(o - ktp * sa)) if ktp else None
                pos = dict(dir=d, entry=o, stop=stop, tp=tp, sig=stime)
            elif pending and pending[0] == 'EXIT' and pos:
                out.append((ref, pos['sig'], pos['dir'], pos['entry'], pos['stop'], pos['tp'], pending[1], c, o)); pos = None; pending = None
            if pos:
                d, stop, tp = pos['dir'], pos['stop'], pos['tp']
                sl_hit = (l <= stop) if d == 1 else (h >= stop)          # el stop se activa al tocar
                tp_hit = False if tp is None else ((h > tp) if d == 1 else (l < tp))   # el limite exige atravesar
                if sl_hit or tp_hit:
                    if sl_hit and tp_hit:   # extremo mas cercano a la apertura primero; empate: el maximo primero
                        hi_first = abs(h - o) <= abs(l - o)
                        first = ('TP' if hi_first else 'SL') if d == 1 else ('SL' if hi_first else 'TP')
                    else:
                        first = 'SL' if sl_hit else 'TP'
                    if first == 'SL':
                        gapped = (o < stop) if d == 1 else (o > stop)
                        px = o if (gap == 'open' and gapped) else stop
                    else:
                        px = tp
                    out.append((ref, pos['sig'], d, pos['entry'], stop, tp, first, c, px)); pos = None
            # 2) logica al cierre de la barra j
            if 571 <= stamp <= 585:
                rh = h if rh is None else max(rh, h); st['rh'] = rh
            inb = lab.in_blk(c)
            if pos:
                if not pending:
                    why = 'TIME' if stamp == 690 else ('TIME' if cfg.get('timex') and stamp == cfg['timex'] else ('NEWS' if inb else None))
                    if why: pending = ('EXIT', why)
                continue
            if pending or stamp < 571 or stamp > 689 or inb or trades_today >= cfg.get('maxd', 1): continue
            s, ksl, ktp = signal(ref, stamp, row, st)
            if s == 0: continue
            trades_today += 1
            pending = ('ENTER', s, ksl if ksl is not None else cfg['ksl'], ktp if ksl is not None else cfg['ktp'], row[cfg['atr']], c)
    return pd.DataFrame(out, columns=['ref', 'signalTime', 'dir', 'entryPrice', 'stopPrice', 'tpPrice', 'exitName', 'exitExecTime', 'exitPrice'])

def load_books(folder):
    tr = {}
    for f in sorted(glob.glob(os.path.join(folder, '*', 'TRADES_NATIVE.tsv'))):
        r = os.path.basename(os.path.dirname(f)).split('_')[1]
        d = pd.read_csv(f, sep='\t')
        for c in ['signalTime', 'entryExecTime', 'exitExecTime']: d[c] = pd.to_datetime(d[c])
        tr[r] = d
    return tr

def compare(nat, sim):
    m = nat.sort_values('signalTime').merge(sim.sort_values('signalTime'), on='signalTime', how='outer', suffixes=('_n', '_s'), indicator=True)
    both = m[m._merge == 'both']
    eq = lambda a, b: float(((both[a] == both[b]) | (both[a].isna() & both[b].isna())).mean())
    return dict(nat=len(nat), sim=len(sim), solo_nat=int((m._merge == 'left_only').sum()), solo_sim=int((m._merge == 'right_only').sum()),
                dir=eq('dir_n', 'dir_s'), entry=eq('entryPrice_n', 'entryPrice_s'), stop=eq('stopPrice_n', 'stopPrice_s'),
                name=eq('exitName_n', 'exitName_s'), time=eq('exitExecTime_n', 'exitExecTime_s'), px=eq('exitPrice_n', 'exitPrice_s'))

if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--bars', required=True); ap.add_argument('--news', required=True); ap.add_argument('--books', required=True)
    a = ap.parse_args()
    lab = Lab(a.bars, a.news); books = load_books(a.books)
    for ref in CFG:
        print(ref, compare(books[ref], run(lab, ref)))
