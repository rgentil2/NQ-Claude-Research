#!/usr/bin/env python3
"""Recalcula el PBO (CSCV, 12 trimestres, 924 particiones) y la correlacion entre mitades de los candidatos
de cada campana a partir de ET4/ET4_REJECTS.tsv (columna q_net). Uso: python3 -I analisis_pbo.py <carpeta del lote>"""
import csv,glob,itertools,math,os,statistics as st,sys
base=sys.argv[1]
def load(d):
    return [[float(v) for v in x["q_net"].split(",")] for x in csv.DictReader(open(os.path.join(d,"ET4","ET4_REJECTS.tsv")),delimiter="\t") if x["status"]=="PASS" and x["q_net"]]
def pbo_corr(M):
    N=len(M); cnt=tot=0; cs=[]
    for A in itertools.combinations(range(12),6):
        B=[q for q in range(12) if q not in A]
        sa=[sum(r[q] for q in A) for r in M]; sb=[sum(r[q] for q in B) for r in M]
        best=max(range(N),key=lambda i:(sa[i],-i))
        rank=sum(1 for v in sb if v<sb[best])+0.5*sum(1 for v in sb if v==sb[best])
        w=min(max(rank/(N+1),1e-9),1-1e-9); cnt+=(math.log(w/(1-w))<=0); tot+=1
        ma,mb=st.mean(sa),st.mean(sb); den=math.sqrt(sum((a-ma)**2 for a in sa)*sum((b-mb)**2 for b in sb))
        if den>0: cs.append(sum((a-ma)*(b-mb) for a,b in zip(sa,sb))/den)
    return cnt/tot, st.mean(cs)
for d in sorted(glob.glob(os.path.join(base,"B*"))):
    if not os.path.isfile(os.path.join(d,"ET4","ET4_REJECTS.tsv")): continue
    M=load(d); p,c=pbo_corr(M); tot=[sum(r) for r in M]
    print("%s\tn=%d\tPBO=%.3f\tcorr_mitades=%.3f\tneto_total_min/med/max=%d/%d/%d"%(os.path.basename(d),len(M),p,c,min(tot),st.median(tot),max(tot)))
