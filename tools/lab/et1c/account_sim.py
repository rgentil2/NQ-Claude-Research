"""NQC AccountSim reference (ET1 C). Profile-driven. Session rows: (pnl, min_open_equity_rel, ntrades)
min_open_equity_rel = lowest open P&L within the session relative to session start (<=0 normally)."""
from dataclasses import dataclass
@dataclass(frozen=True)
class Profile:
    name:str; target:float; mll:float; lock_level:float; consistency:float; min_days:int
RAPID_INTRADAY_50K=Profile("RAPID_INTRADAY_50K",3000,2000,100,0.50,2)
RAPID_EOD_50K=Profile("RAPID_EOD_50K",3000,2000,100,0.30,4)

def run_from(rows,start,p):
    bal=0.0; hi=0.0; level=-p.mll; days=0; best=0.0
    for i in range(start,len(rows)):
        pnl,mn,n=rows[i]
        if n>0 and bal+mn<=level: return ("FAIL","MLL",i-start+1,bal)
        bal+=pnl
        if n>0:
            days+=1; best=max(best,pnl)
        if bal<=level and n>0: return ("FAIL","MLL_EOD",i-start+1,bal)
        hi=max(hi,bal); level=min(hi-p.mll,p.lock_level)
        if bal>=p.target and days>=p.min_days and best<=p.consistency*bal:
            return ("PASS","",i-start+1,bal)
    return ("CENSORED","",len(rows)-start,bal)

def rolling(rows,p):
    res=[run_from(rows,s,p) for s in range(len(rows))]
    dec=[r for r in res if r[0]!="CENSORED"]
    ps=sum(r[0]=="PASS" for r in dec)
    return res,(ps/len(dec) if dec else None),len(dec)

def protect(trades,pause=-500,close=-750,cushion=400,room=2000):
    """trades: list of (pnl_net, min_run_after_entry) inside ONE session. pause: realized<=pause blocks new entries; close: running equity<=close closes at that level and blocks the day; cushion: room+real<=cushion blocks new entries (room = balance - MLL level at session start)."""
    real=0.0; mn=0.0; kept=0
    for pnl,mr in trades:
        if real<=pause: break
        if room+real<=cushion: break
        if real+mr<=close:
            real=close; mn=min(mn,close); kept+=1; break
        mn=min(mn,real+mr); real+=pnl; mn=min(mn,real); kept+=1
    return real,mn,kept
