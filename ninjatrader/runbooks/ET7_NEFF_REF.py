import math, hashlib
from statistics import NormalDist
M=(1<<64)-1
class R:
    def __init__(s,seed): s.s=seed&M
    def nxt(s):
        s.s=(s.s+0x9E3779B97F4A7C15)&M; z=s.s
        z=((z^(z>>30))*0xBF58476D1CE4E5B9)&M; z=((z^(z>>27))*0x94D049BB133111EB)&M
        return z^(z>>31)
    def nint(s,n): return s.nxt()%n
Z=1.959963984540054
def chao(counts,N):
    n=sum(counts); f1=counts.count(1); f2=counts.count(2); so=len(counts)
    a=f1*(f1-1)/(2*(f2+1)); d=f2+1
    v=a+f1*(2*f1-1)**2/(4*d*d)+f1*f1*f2*(f1-1)**2/(4*d**4)
    est=min(so+a,N); T=est-so
    if T>0:
        K=math.exp(Z*math.sqrt(math.log(1+v/(T*T)))); lo=so+T/K; hi=so+T*K
    else: lo=so; hi=so+Z*math.sqrt(v)
    return so,f1,f2,est,max(so,min(lo,est)),min(N,max(hi,est)),1-f1/n,v
def world(G,floor,expo,N):
    w=[g**-expo for g in range(1,G+1)]; sw=sum(w)
    a=[floor+int(x/sw*(N-floor*G)) for x in w]; a[0]+=N-sum(a); return a
def rep(ab,k,seed):
    lab=[]
    for g,c in enumerate(ab): lab+= [g]*c
    N=len(lab); r=R(seed)
    for i in range(k):
        j=i+r.nint(N-i); lab[i],lab[j]=lab[j],lab[i]
    cnt={}
    for x in lab[:k]: cnt[x]=cnt.get(x,0)+1
    return list(cnt.values())
def scen(name,G,floor,expo,N,k,reps,seed0):
    ab=world(G,floor,expo,N); h=hashlib.sha256(); cov=0; rows=[]
    for r in range(reps):
        c=rep(ab,k,seed0+r); so,f1,f2,est,lo,hi,gt,v=chao(c,N)
        cov+= lo<=G<=hi
        rows.append((so,f1,f2,est,lo,hi))
        q=lambda x:int(math.floor(x*10000+0.5)); h.update(("%d,%d,%d,%d,%d,%d;"%(so,f1,f2,q(est),q(lo),q(hi))).encode())
    print(name,"G",G,"min",min(ab),"max",ab[0],"sum",sum(ab),"cov",cov,"/",reps,"sha",h.hexdigest(),"first",rows[0], "meanSobs",sum(r[0] for r in rows)/reps,"meanEst",sum(r[3] for r in rows)/reps)
scen("A",200,15,1.3,100000,5000,50,777)
scen("B",3000,3,1.1,100000,5000,50,888)
# DSR effect
nd=NormalDist(); g=0.5772156649015329
def sr0(V,N): return math.sqrt(V)*((1-g)*nd.inv_cdf(1-1/N)+g*nd.inv_cdf(1-1/(N*math.e)))
T=400
x=[((t*37)%101-48)*1.0+(3.0 if t%7==0 else 0.0)+ (0.8 if t%2==0 else 0.0) for t in range(T)]
m=sum(x)/T; m2=sum((v-m)**2 for v in x)/T; m3=sum((v-m)**3 for v in x)/T; m4=sum((v-m)**4 for v in x)/T
sr=m/math.sqrt(m2*T/(T-1)); sk=m3/m2**1.5; ku=m4/m2**2
for N in (150,200,260):
    s0=sr0(0.0004,N); den=1-sk*sr+(ku-1)/4*sr*sr
    print("dsr",N,repr(s0),repr(nd.cdf((sr-s0)*math.sqrt(T-1)/math.sqrt(den))))
# casos puntuales
for c,N in (([1]*5+[2]*3+[10]*20,5000),([5]*10,1000),([1]*4+[2]*0+[30]*10,100000),([1]*9+[2]*1+[40]*20,10**5)):
    print(c[:3],N,[repr(v) for v in chao(c,N)])
ab=world(3000,3,1.1,100000)
rows=[]
for r in range(50):
    c=rep(ab,5000,888+r); so,f1,f2,est,lo,hi,gt,v=chao(c,100000); rows.append("%d,%d,%d,%.4f,%.4f,%.4f;"%(so,f1,f2,est,lo,hi))
