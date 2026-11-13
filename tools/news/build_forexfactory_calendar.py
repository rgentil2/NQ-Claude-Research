#!/usr/bin/env python3
"""
NQC - Calendario de noticias USD (High/Medium) de Forex Factory.

Basado en tools/build_forexfactory_calendar.py de MES-Trading-Research (corrida del
2026-10-05: 2.856 eventos 2020-2024). Lee las paginas mensuales del calendario,
filtra USD con impacto High o Medium y hora programada, convierte GMT a hora de
Nueva York y escribe el CSV, el JSON de eventos sin hora y NEWS_MANIFEST.txt.

Regla del blackout (docs/NEWS_BLACKOUT.md): fuera del mercado desde T-5 min hasta
T+5 min. Las barras NO se eliminan: el calendario solo bloquea operaciones.

Uso:  python build_forexfactory_calendar.py --start-year 2020 --end-year 2026
Requiere: pip install requests beautifulsoup4 tzdata
Aviso: lee un sitio de terceros; revisa sus terminos de uso. El script usa
cabeceras normales y una pausa entre peticiones, y se detiene si es bloqueado.
No intenta eludir protecciones.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
import sys
import time
from collections import Counter, defaultdict
from datetime import datetime, date, timedelta
from pathlib import Path
from zoneinfo import ZoneInfo

import requests
from bs4 import BeautifulSoup

SCRIPT_VERSION = "NEWS-1.0.0"
MONTHS = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"]
HEADERS = {
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36",
    "Accept-Language": "en-US,en;q=0.9",
}
COOKIES = {"fftimezone": "Etc/GMT", "fftimeformat": "1"}
IMPACT_COLOR_MAP = {"red": "High", "ora": "Medium", "yel": "Low", "gry": "Holiday"}
IMPACT_LEVEL_MAP = {"high": "High", "medium": "Medium", "low": "Low", "none": "Holiday"}
ET = ZoneInfo("America/New_York")
UTC = ZoneInfo("UTC")

# Calendario T1 2026 de MFFU (imagen oficial), horas ET. Control cruzado.
T1_2026 = {
    "08:30": [
        "2026-01-09", "2026-02-06", "2026-03-06", "2026-04-03", "2026-05-08", "2026-06-05",
        "2026-07-02", "2026-08-07", "2026-09-04", "2026-10-02", "2026-11-06", "2026-12-04",
        "2026-01-13", "2026-02-11", "2026-03-11", "2026-04-10", "2026-05-12", "2026-06-10",
        "2026-07-14", "2026-08-12", "2026-09-11", "2026-10-14", "2026-11-10", "2026-12-10",
    ],
    "14:00": [
        "2026-01-28", "2026-03-18", "2026-04-29", "2026-06-17", "2026-07-29", "2026-09-16", "2026-12-09",
        "2026-02-18", "2026-04-08", "2026-05-20", "2026-07-08", "2026-08-19", "2026-10-07", "2026-11-18", "2026-12-30",
    ],
}


def clean(x):
    return " ".join(x.split()) if x else ""


def parse_impact(tr):
    cell = tr.select_one("td.calendar__impact")
    if not cell:
        return ""
    icon = cell.select_one("[title]")
    if icon and icon.get("title"):
        t = icon["title"].replace("Impact Expected", "").strip()
        if t.startswith("Med"):
            return "Medium"
        if t.startswith("High"):
            return "High"
        if t.startswith("Low"):
            return "Low"
        if t:
            return t
    classes = set(cell.get("class", []))
    for el in cell.select("[class]"):
        classes.update(el.get("class", []))
    s = " ".join(classes).lower()
    for k, v in IMPACT_COLOR_MAP.items():
        if f"impact-{k}" in s:
            return v
    for k, v in IMPACT_LEVEL_MAP.items():
        if f"--{k}" in s:
            return v
    return ""


class Blocked(RuntimeError):
    pass


def fetch_month(year, mon, cache_dir: Path, from_cache_only=False):
    url = f"https://www.forexfactory.com/calendar?month={mon}.{year}"
    cache = cache_dir / f"{year}-{MONTHS.index(mon) + 1:02d}.html"
    if cache.exists():
        return url, cache.read_text(encoding="utf-8")
    if from_cache_only:
        raise RuntimeError(f"Falta {cache} y --from-cache-only esta activo")
    last = None
    for attempt in range(4):
        r = requests.get(url, headers=HEADERS, cookies=COOKIES, timeout=45)
        last = r.status_code
        if r.status_code == 200:
            cache.write_text(r.text, encoding="utf-8")
            return url, r.text
        if r.status_code in (403, 401):
            raise Blocked(f"HTTP {r.status_code} en {url}: el sitio bloquea la peticion. Se detiene; no se eluden protecciones.")
        time.sleep(5 * (attempt + 1))
    raise Blocked(f"HTTP {last} repetido en {url}")


def parse_month(html, nominal_year, nominal_month_idx):
    soup = BeautifulSoup(html, "html.parser")
    table = soup.select_one("table.calendar__table")
    if not table:
        raise RuntimeError("Calendar table not found")
    current_date = ""
    current_time = ""
    out = []
    for tr in table.select("tr.calendar__row"):
        event_cell = tr.select_one("td.calendar__event")
        if not event_cell:
            continue
        date_cell = tr.select_one("td.calendar__date")
        if date_cell:
            d = clean(date_cell.get_text(" "))
            if d:
                m = re.search(r"\b(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)\b", d, re.I)
                if m:
                    idx = MONTHS.index(m.group(1).lower()) + 1
                    # La vista mensual puede mostrar dias de meses adyacentes.
                    if nominal_month_idx == 1 and idx == 12:
                        row_year = nominal_year - 1
                    elif nominal_month_idx == 12 and idx == 1:
                        row_year = nominal_year + 1
                    else:
                        row_year = nominal_year
                else:
                    row_year = nominal_year
                current_date = f"{d} {row_year}"
        time_cell = tr.select_one("td.calendar__time")
        if time_cell:
            t = clean(time_cell.get_text(" "))
            if t:
                current_time = t

        def cell(sel):
            el = tr.select_one(sel)
            return clean(el.get_text(" ")) if el else ""

        out.append({
            "date_raw": current_date,
            "time_raw": current_time,
            "currency": cell("td.calendar__currency"),
            "impact": parse_impact(tr),
            "event": clean(event_cell.get_text(" ")),
        })
    return out


def parse_dt(date_raw, time_raw):
    # Ejemplos: "Thu Jan 2 2024" / "13:30". Se omiten All Day / Tentative / vacio.
    if not date_raw or not time_raw:
        return None
    t = time_raw.strip().lower()
    if "all day" in t or "tentative" in t or "day " in t:
        return None
    t = t.replace("am", " AM").replace("pm", " PM")
    for fmt in ("%a %b %d %Y %H:%M", "%a %b %d %Y %I:%M %p"):
        try:
            dt = datetime.strptime(date_raw + " " + t, fmt)
            return dt.replace(tzinfo=UTC)
        except ValueError:
            pass
    return None


def build_rows(raw, start_year, end_year):
    keep, skipped = [], []
    for r in raw:
        if r["currency"] != "USD" or r["impact"] not in {"High", "Medium"}:
            continue
        dt_utc = parse_dt(r["date_raw"], r["time_raw"])
        if dt_utc is None:
            skipped.append(r)
            continue
        dt_et = dt_utc.astimezone(ET)
        if not (start_year <= dt_et.year <= end_year):
            continue
        keep.append({
            "event_utc": dt_utc.isoformat(),
            "event_et": dt_et.isoformat(),
            "date_et": dt_et.date().isoformat(),
            "time_et": dt_et.strftime("%H:%M:%S"),
            "currency": r["currency"],
            "impact": r["impact"],
            "event": r["event"],
            "blackout_start_et": (dt_et - timedelta(minutes=5)).isoformat(),
            "blackout_end_et": (dt_et + timedelta(minutes=5)).isoformat(),
            "source_url": r["source_url"],
        })
    uniq = {}
    for r in keep:
        uniq[(r["event_utc"], r["currency"], r["impact"], r["event"])] = r
    keep = sorted(uniq.values(), key=lambda r: (r["event_utc"], r["event"]))
    return keep, skipped


def window_stats(keep, start_year, end_year, w0=(9, 30), w1=(11, 30)):
    """Efecto del blackout sobre la ventana 09:30-11:30 ET."""
    by_day = defaultdict(list)
    flat_events = 0
    for r in keep:
        t = datetime.fromisoformat(r["event_et"])
        lo, hi = t - timedelta(minutes=5), t + timedelta(minutes=5)
        d = t.date()
        w_start = datetime(d.year, d.month, d.day, *w0, tzinfo=ET)
        w_end = datetime(d.year, d.month, d.day, *w1, tzinfo=ET)
        a, b = max(lo, w_start), min(hi, w_end)
        if a < b:
            by_day[d].append((a, b))
        if w_start <= lo < w_end:
            flat_events += 1
    blocked_minutes = {}
    for d, iv in by_day.items():
        iv.sort()
        total, cur_a, cur_b = 0.0, iv[0][0], iv[0][1]
        for a, b in iv[1:]:
            if a <= cur_b:
                cur_b = max(cur_b, b)
            else:
                total += (cur_b - cur_a).total_seconds() / 60
                cur_a, cur_b = a, b
        total += (cur_b - cur_a).total_seconds() / 60
        blocked_minutes[d] = total
    weekdays = 0
    d = date(start_year, 1, 1)
    last = date(end_year, 12, 31)
    while d <= last:
        if d.weekday() < 5:
            weekdays += 1
        d += timedelta(days=1)
    tot_blocked = sum(blocked_minutes.values())
    return {
        "days_with_blackout_in_window": len(blocked_minutes),
        "weekdays_in_range": weekdays,
        "pct_weekdays_affected": round(100.0 * len(blocked_minutes) / weekdays, 2) if weekdays else 0.0,
        "mean_blocked_minutes_per_affected_day": round(tot_blocked / len(blocked_minutes), 2) if blocked_minutes else 0.0,
        "pct_of_window_minutes_blocked": round(100.0 * tot_blocked / (weekdays * 120), 2) if weekdays else 0.0,
        "events_forcing_flat_inside_window": flat_events,
    }


def sanity(keep):
    out = {}
    def frac(match, hhmm):
        sel = [r for r in keep if match(r["event"])]
        if not sel:
            return {"n": 0, "pct_at": None}
        ok = sum(1 for r in sel if r["time_et"].startswith(hhmm))
        return {"n": len(sel), "pct_at": round(100.0 * ok / len(sel), 2)}
    out["non_farm_at_0830"] = frac(lambda e: "non-farm employment change" in e.lower(), "08:30")
    out["cpi_at_0830"] = frac(lambda e: re.search(r"\bcpi\b", e.lower()) is not None, "08:30")
    out["fomc_statement_at_1400"] = frac(lambda e: "fomc statement" in e.lower(), "14:00")
    # DST: mismo control separando verano e invierno
    dst = {"summer": [0, 0], "winter": [0, 0]}
    for r in keep:
        if "non-farm employment change" in r["event"].lower():
            t = datetime.fromisoformat(r["event_et"])
            key = "summer" if t.utcoffset() == timedelta(hours=-4) else "winter"
            dst[key][1] += 1
            if r["time_et"].startswith("08:30"):
                dst[key][0] += 1
    out["non_farm_0830_by_dst"] = {k: {"ok": v[0], "n": v[1]} for k, v in dst.items()}
    # T1 2026 de MFFU: control cruzado (pasado y futuro por separado)
    years = {r["date_et"][:4] for r in keep}
    idx = defaultdict(set)
    for r in keep:
        if r["impact"] == "High":
            idx[r["date_et"]].add(r["time_et"][:5])
    today = date.today().isoformat()
    checked, miss_past, miss_future = 0, [], []
    for hhmm, days in T1_2026.items():
        for d in days:
            if d[:4] not in years:
                continue
            checked += 1
            if hhmm not in idx.get(d, set()):
                (miss_past if d <= today else miss_future).append(f"{d} {hhmm}")
    out["t1_2026_checked"] = checked
    out["t1_2026_missing_past"] = sorted(miss_past)
    out["t1_2026_missing_future"] = sorted(miss_future)
    return out


def monthly_counts(keep):
    c = Counter(r["date_et"][:7] for r in keep)
    return dict(sorted(c.items()))


def write_outputs(keep, skipped, start_year, end_year, out_dir: Path):
    out_dir.mkdir(parents=True, exist_ok=True)
    stem = f"usd_high_medium_{start_year}_{end_year}"
    csv_path = out_dir / f"{stem}.csv"
    fields = list(keep[0]) if keep else ["event_utc"]
    with csv_path.open("w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=fields, lineterminator="\n")
        w.writeheader()
        w.writerows(keep)
    (out_dir / f"unscheduled_{stem}.json").write_text(json.dumps(skipped, ensure_ascii=False, indent=2), encoding="utf-8")
    sha = hashlib.sha256(csv_path.read_bytes()).hexdigest()
    by_year = Counter(r["date_et"][:4] for r in keep)
    by_impact = Counter(r["impact"] for r in keep)
    mc = monthly_counts(keep)
    vals = sorted(mc.values())
    med = vals[len(vals) // 2] if vals else 0
    weak = {m: n for m, n in mc.items() if med and n < 0.5 * med}
    st = window_stats(keep, start_year, end_year)
    sn = sanity(keep)
    lines = [
        "NQC NEWS_MANIFEST",
        f"script_version     : {SCRIPT_VERSION}",
        f"source             : Forex Factory (paginas mensuales)",
        f"timezone_scraped   : UTC/GMT via cookie fftimezone",
        f"timezone_output    : America/New_York",
        f"rule               : USD High/Medium; fuera del mercado de T-5 a T+5; barras sin eliminar",
        f"range              : {start_year}-01-01 .. {end_year}-12-31",
        f"events_scheduled   : {len(keep)}",
        f"events_unscheduled : {len(skipped)}",
        f"by_impact          : {dict(by_impact)}",
        f"by_year            : {dict(sorted(by_year.items()))}",
        f"csv_file           : {csv_path.name}",
        f"csv_sha256         : {sha}",
        "",
        "[Meses con pocos eventos (< 50% de la mediana mensual)]",
        f"median_monthly     : {med}",
        f"weak_months        : {weak}",
        "",
        "[Zona horaria]",
        f"non_farm_at_0830   : {sn['non_farm_at_0830']}",
        f"non_farm_by_dst    : {sn['non_farm_0830_by_dst']}",
        f"cpi_at_0830        : {sn['cpi_at_0830']}",
        f"fomc_stmt_at_1400  : {sn['fomc_statement_at_1400']}",
        "",
        "[Control cruzado T1 2026 de MFFU]",
        f"checked            : {sn['t1_2026_checked']}",
        f"missing_past       : {sn['t1_2026_missing_past']}",
        f"missing_future     : {sn['t1_2026_missing_future']}",
        "",
        "[Efecto sobre la ventana 09:30-11:30 ET]",
    ] + [f"{k:<38}: {v}" for k, v in st.items()]
    (out_dir / "NEWS_MANIFEST.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    return csv_path, sha, lines


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--start-year", type=int, default=2020)
    ap.add_argument("--end-year", type=int, default=2026)
    ap.add_argument("--out-dir", default="NQC_News_Out")
    ap.add_argument("--delay", type=float, default=2.1)
    ap.add_argument("--from-cache-only", action="store_true", help="No hace peticiones; usa el HTML guardado")
    args = ap.parse_args()
    out_dir = Path(args.out_dir)
    cache_dir = out_dir / "raw_html"
    cache_dir.mkdir(parents=True, exist_ok=True)

    raw = []
    for y in range(args.start_year, args.end_year + 1):
        for mon in MONTHS:
            cached = (cache_dir / f"{y}-{MONTHS.index(mon) + 1:02d}.html").exists()
            try:
                url, html = fetch_month(y, mon, cache_dir, args.from_cache_only)
            except Blocked as e:
                print("DETENIDO:", e)
                print("Se conservan los meses ya guardados en", cache_dir, "; vuelve a ejecutar para continuar.")
                sys.exit(2)
            rows = parse_month(html, y, MONTHS.index(mon) + 1)
            print(y, mon, "rows", len(rows), "(cache)" if cached else "")
            for r in rows:
                r["source_url"] = url
                raw.append(r)
            if not cached:
                time.sleep(args.delay)

    keep, skipped = build_rows(raw, args.start_year, args.end_year)
    if not keep:
        print("No se obtuvieron eventos USD High/Medium con hora.")
        sys.exit(3)
    csv_path, sha, lines = write_outputs(keep, skipped, args.start_year, args.end_year, out_dir)
    print("\n".join(lines))
    print("\nListo:", csv_path)


if __name__ == "__main__":
    main()
