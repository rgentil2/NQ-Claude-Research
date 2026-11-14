#region Using declarations
using System;
using System.Collections.Generic;
#endregion

//==============================================================================
// NQC_ET3_LibFeatures - ET3 WP1: formulas de las 120 familias de la biblioteca D-018 (port causal).
// Procedencia: ver cabecera de NQC_ET3_Library.cs. SOLO DESARROLLO, sin compilar.
// Cada funcion Compute(sr, at, f, n) lee unicamente barras con indice <= at (cerradas) y devuelve el
// valor de la familia f con periodo n. Las reglas (11) estan en NQCE3_Rules (NQC_ET3_Library.cs).
// Familias: 0..11 FeatBase, 12..35 FeatExt, 36..56 FeatTrend, 57..77 FeatMom, 78..98 FeatVola, 99..119 FeatVolAct.
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	internal static class NQCE3_FeatBase
	{
		private static double AverageClose(NQCE3_Series sr,int at,int n)
		{
			double x=0;for(int i=at-n+1;i<=at;i++)x+=sr.C[i];
			return x/n;
		}
		private static double EmaClose(NQCE3_Series sr,int at,int n,int period)
		{
			// Fixed-length warm-up relative to each completed sample; not
			// a NinjaTrader EMA parity claim.
			int start=at-(3*period+5);
			if(start<0)start=0;
			double x=sr.C[start],k=2.0/(n+1);
			for(int i=start+1;i<=at;i++)x+=k*(sr.C[i]-x);
			return x;
		}
		private static double Atr(NQCE3_Series sr,int at,int n)
		{
			double x=0;
			for(int i=at-n+1;i<=at;i++)
			{
				double prev=i==0?sr.C[i]:sr.C[i-1];
				x+=Math.Max(sr.H[i]-sr.L[i],
					Math.Max(Math.Abs(sr.H[i]-prev),Math.Abs(sr.L[i]-prev)));
			}
			return x/n;
		}
		private static double Typical(NQCE3_Bar b)
		{ return (b.High+b.Low+b.Close)/3.0; }
		private static double Feature(NQCE3_Series sr,int at,int f,int n)
		{
			double close=sr.C[at];
			double atr=Math.Max(Atr(sr,at,n),1e-12);
			if(f==0)return (close-AverageClose(sr,at,n))/atr;
			if(f==1)return (close-EmaClose(sr,at,n,n))/atr;
			if(f==2)return (AverageClose(sr,at,n)-EmaClose(sr,at,2*n,n))/atr;
			if(f==3)
			{
				double num=0,den=n*(n+1)*0.5;
				for(int j=0;j<n;j++)num+=(n-j)*sr.C[at-j];
				return (close-num/den)/atr;
			}
			if(f==4)
			{
				// Wilder RSI with 3*n rolling differences, centered [-1,+1].
				int start=at-3*n;
				double gain=0,loss=0;
				for(int i=start+1;i<=start+n;i++)
				{
					double diff=sr.C[i]-sr.C[i-1];
					gain+=Math.Max(diff,0);loss+=Math.Max(-diff,0);
				}
				gain/=n;loss/=n;
				for(int i=start+n+1;i<=at;i++)
				{
					double diff=sr.C[i]-sr.C[i-1];
					gain=(gain*(n-1)+Math.Max(diff,0))/n;
					loss=(loss*(n-1)+Math.Max(-diff,0))/n;
				}
				if(gain+loss<1e-12)return 0;
				return (gain-loss)/(gain+loss);
			}
			if(f==5)
			{
				double mean=0;
				for(int i=at-n+1;i<=at;i++)mean+=Typical(sr.Bar(i));
				mean/=n;
				double dev=0;
				for(int i=at-n+1;i<=at;i++)
					dev+=Math.Abs(Typical(sr.Bar(i))-mean);
				dev/=n;
				return dev<1e-12?0:(Typical(sr.Bar(at))-mean)/(0.015*dev*150);
			}
			if(f==6)
				return (close-sr.C[at-n])/(atr*Math.Sqrt(n));
			if(f==7)
			{
				double hi=Double.NegativeInfinity,lo=Double.PositiveInfinity;
				for(int i=at-n+1;i<=at;i++)
				{hi=Math.Max(hi,sr.H[i]);lo=Math.Min(lo,sr.L[i]);}
				return hi-lo<1e-12?0:2*(close-lo)/(hi-lo)-1;
			}
			if(f==8)
				return (EmaClose(sr,at,n,n)-EmaClose(sr,at,2*n,n))/atr;
			if(f==9)
			{
				double avg=AverageClose(sr,at,n),v=0;
				for(int i=at-n+1;i<=at;i++)
					v+=Math.Pow(sr.C[i]-avg,2);
				double sd=Math.Sqrt(v/n);
				return sd<1e-12?0:(close-avg)/(2*sd);
			}
			if(f==10)
			{
				double plus=0,minus=0;
				for(int i=at-n+1;i<=at;i++)
				{
					double prev=Typical(sr.Bar(i-1)),cur=Typical(sr.Bar(i));
					double flow=cur*sr.V[i];
					if(cur>prev)plus+=flow;
					if(cur<prev)minus+=flow;
				}
				return plus+minus<1e-12?0:(plus-minus)/(plus+minus);
			}
			if(f==11)
			{
				double num=0,den=0;
				for(int i=at-n+1;i<=at;i++)
				{
					double range=sr.H[i]-sr.L[i];
					double clv=range<1e-12?0:
						(2*sr.C[i]-sr.H[i]-sr.L[i])/range;
					num+=clv*sr.V[i];den+=sr.V[i];
				}
				return den<1e-12?0:num/den;
			}
			throw new ArgumentOutOfRangeException("family");
		}
		public static double Compute(NQCE3_Series sr,int at,int f,int n){return Feature(sr,at,f,n);}
	}

	internal static class NQCE3_FeatExt
	{
		private static bool Finite(double x)
		{return !Double.IsNaN(x)&&!Double.IsInfinity(x);}
		private static double Div(double n,double d)
		{return Math.Abs(d)<1e-12?0:n/d;}
		private static double TrueRange(NQCE3_Series sr,int i)
		{
			double p=sr.C[Math.Max(0,i-1)];
			return Math.Max(sr.H[i]-sr.L[i],
				Math.Max(Math.Abs(sr.H[i]-p),Math.Abs(sr.L[i]-p)));
		}
		private static double Atr(NQCE3_Series sr,int at,int n)
		{
			double sum=0;
			for(int k=at-n+1;k<=at;k++)sum+=TrueRange(sr,k);
			return Math.Max(sum/n,1e-12);
		}
		private static double Sma(NQCE3_Series sr,int at,int n)
		{
			double a=0;
			for(int j=at-n+1;j<=at;j++)a+=sr.C[j];
			return a/n;
		}
		private static double Weighted(NQCE3_Series sr,int at,int n)
		{
			double s=0,total=n*(n+1)/2.0;
			for(int j=0;j<n;j++)s+=(n-j)*sr.C[at-j];
			return s/total;
		}
		private static double EMA(NQCE3_Series sr,int at,int n,
								  Func<int,double> value)
		{
			int start=Math.Max(0,at-3*n-5);
			double mean=value(start),a=2.0/(n+1);
			for(int k=start+1;k<=at;k++)
				mean+=a*(value(k)-mean);
			return mean;
		}
		private static double Dema(NQCE3_Series sr,int at,int n)
		{
			int start=Math.Max(0,at-3*n-5);
			double a=2.0/(n+1),e1=sr.C[start],e2=e1;
			for(int k=start+1;k<=at;k++)
			{
				e1+=a*(sr.C[k]-e1);
				e2+=a*(e1-e2);
			}
			return 2*e1-e2;
		}
		private static double Hma(NQCE3_Series sr,int at,int n)
		{
			int half=Math.Max(2,n/2),root=Math.Max(2,(int)Math.Sqrt(n));
			double sum=0,den=0;
			for(int j=0;j<root;j++)
			{
				int index=at-j;
				double v=2*Weighted(sr,index,half)-Weighted(sr,index,n);
				sum+=(root-j)*v;den+=root-j;
			}
			return sum/den;
		}
		private static double Clv(NQCE3_Bar b)
		{
			return Div(2*b.Close-b.High-b.Low,b.High-b.Low);
		}
		private static double RSIFlux(NQCE3_Series sr,int at,int n,
									  bool plus)
		{
			double s=0;
			for(int k=at-n+1;k<=at;k++)
			{
				double d=sr.C[k]-sr.C[k-1];
				if(plus)s+=Math.Max(0,d);
				else s+=Math.Max(0,-d);
			}
			return s;
		}
		private static double Feature(NQCE3_Series sr,int at,int family,int n)
		{
			double close=sr.C[at],atr=Atr(sr,at,n);
			int start=at-n+1;
			switch(family)
			{
				case 12:
				{
					// OLS slope normalized by mean true range, dimensionless.
					double meanX=(n-1)*0.5,meanY=Sma(sr,at,n);
					double num=0,den=0;
					for(int j=0;j<n;j++)
					{
						double x=j-meanX;
						num+=x*(sr.C[start+j]-meanY);den+=x*x;
					}
					return Div(num,den*atr);
				}
				case 13:
				{
					double p=0,v=0;
					for(int j=start;j<=at;j++)
					{
						double typical=(sr.H[j]+sr.L[j]+sr.C[j])/3.0;
						p+=typical*sr.V[j];v+=sr.V[j];
					}
					return v==0?0:(close-p/v)/atr;
				}
				case 14: return (close-Hma(sr,at,n))/atr;
				case 15:
				{
					int lag=Math.Max(1,(n-1)/2);
					double z=EMA(sr,at,n,k=>2*sr.C[k]-
						sr.C[Math.Max(0,k-lag)]);
					return (close-z)/atr;
				}
				case 16: return (close-Dema(sr,at,n))/atr;
				case 17:
				{
					double signed=0,volume=0;
					for(int k=start;k<=at;k++)
					{
						signed+=Math.Sign(sr.C[k]-sr.C[k-1])*
							sr.V[k];
						volume+=sr.V[k];
					}
					return Div(signed,volume);
				}
				case 18:
				{
					double flow=0,v=0;
					for(int k=start;k<=at;k++)
					{
						flow+=Clv(sr.Bar(k))*sr.V[k];
						v+=sr.V[k];
					}
					return Div(flow,v);
				}
				case 19:
				{
					// ADL slope uses volume flow; unlike CMF it divides
					// by a rolling mean volume, keeping direction.
					double current=0,first=0,vol=0;
					for(int k=start;k<=at;k++)
					{
						current+=Clv(sr.Bar(k))*sr.V[k];
						vol+=sr.V[k];
						if(k==start)first=current;
					}
					return Div(current-first,vol);
				}
				case 20:
				{
					double sum=0,volume=0;
					for(int k=start;k<=at;k++)
					{
						sum+=(sr.C[k]-sr.C[k-1])*sr.V[k];
						volume+=sr.V[k];
					}
					return Div(sum,volume*atr);
				}
				case 21:
				{
					// EOM: midpoint displacement per normalized volume
					// and range. Zero volume is treated as neutral.
					double meanV=0;
					for(int k=start;k<=at;k++)meanV+=sr.V[k];
					meanV/=n;
					double sum=0;
					for(int k=start;k<=at;k++)
					{
						if(sr.V[k]<=0)continue;
						double mid=(sr.H[k]+sr.L[k])/2;
						double prev=(sr.H[k-1]+sr.L[k-1])/2;
						sum+=(mid-prev)/atr*
							(sr.H[k]-sr.L[k])/atr*
							Div(meanV,sr.V[k]);
					}
					return sum/n;
				}
				case 22:
				case 23:
				{
					double p=0,m=0,tr=0,dx=0;
					for(int k=start;k<=at;k++)
					{
						double u=sr.H[k]-sr.H[k-1];
						double d=sr.L[k-1]-sr.L[k];
						p+=u>d && u>0?u:0;
						m+=d>u && d>0?d:0;
						tr+=TrueRange(sr,k);
					}
					double dmi=Div(p-m,tr);
					if(family==22)return dmi;
					// Wilder/ADX-inspired finite window signed strength
					// term: DX magnitude applied to prevailing DI sign.
					for(int k=start;k<=at;k++)
					{
						double u=sr.H[k]-sr.H[k-1];
						double d=sr.L[k-1]-sr.L[k];
						dx+=Div(Math.Abs((u>d?Math.Max(0,u):0)-
							(d>u?Math.Max(0,d):0)),
							TrueRange(sr,k));
					}
					return dmi*dx/n;
				}
				case 24:
				{
					int hiAge=0,loAge=0;
					double hi=Double.NegativeInfinity,
						   lo=Double.PositiveInfinity;
					for(int j=0;j<n;j++)
					{
						int k=at-j;
						if(sr.H[k]>hi){hi=sr.H[k];hiAge=j;}
						if(sr.L[k]<lo){lo=sr.L[k];loAge=j;}
					}
					return (loAge-hiAge)/(double)n;
				}
				case 25:
				{
					double plus=0,minus=0,tr=0;
					for(int k=start;k<=at;k++)
					{
						plus+=Math.Abs(sr.H[k]-sr.L[k-1]);
						minus+=Math.Abs(sr.L[k]-sr.H[k-1]);
						tr+=TrueRange(sr,k);
					}
					return Div(plus-minus,tr);
				}
				case 26:
				{
					double changes=0;
					for(int k=start;k<=at;k++)
						changes+=Math.Abs(sr.C[k]-sr.C[k-1]);
					return Div(close-sr.C[at-n],changes);
				}
				case 27:
				{
					double gain=RSIFlux(sr,at,n,true);
					double loss=RSIFlux(sr,at,n,false);
					return Div(gain-loss,gain+loss);
				}
				case 28:
				{
					// TSI from two cascaded EMA smoothings of momenta
					// using the SAME n, denominator uses abs momentum.
					int from=Math.Max(1,at-3*n-5);
					double alpha=2.0/(n+1);
					double s=sr.C[from]-sr.C[from-1];
					double a=Math.Abs(s),d=s,e=a;
					for(int k=from+1;k<=at;k++)
					{
						double change=sr.C[k]-sr.C[k-1];
						s+=alpha*(change-s);
						a+=alpha*(Math.Abs(change)-a);
						d+=alpha*(s-d);
						e+=alpha*(a-e);
					}
					return Div(d,e);
				}
				case 29:
				{
					double alpha=2.0/(n+1);
					int from=Math.Max(0,at-3*n-5);
					double e1=sr.C[from],e2=e1,e3=e2,prior=e3;
					for(int k=from+1;k<=at;k++)
					{
						prior=e3;
						e1+=alpha*(sr.C[k]-e1);
						e2+=alpha*(e1-e2);
						e3+=alpha*(e2-e3);
					}
					return Div(e3-prior,atr);
				}
				case 30:
				{
					// DPO with a fixed n/2+1 retrospective lag;
					// only historical closes, never centered/future bars.
					int lag=n/2+1;
					int k=at-lag;
					return (sr.C[k]-Sma(sr,k,n))/atr;
				}
				case 31:
				{
					// MACD histogram: EMA(n)-EMA(2n)-EMA(n) of their
					// past MACD sequence; normalized by ATR.
					double signal=0,a=2.0/(n+1);
					int from=at-n+1;
					for(int k=from;k<=at;k++)
					{
						double macd=EMA(sr,k,n,j=>sr.C[j])-
							EMA(sr,k,2*n,j=>sr.C[j]);
						if(k==from)signal=macd;
						else signal+=a*(macd-signal);
					}
					double last=EMA(sr,at,n,k=>sr.C[k])-
						EMA(sr,at,2*n,k=>sr.C[k]);
					return (last-signal)/atr;
				}
				case 32:
				{
					double pvt=0,normalized=0;
					for(int k=start;k<=at;k++)
					{
						double change=Div(sr.C[k]-sr.C[k-1],
										  sr.C[k-1]);
						pvt+=change*sr.V[k];
						normalized+=sr.V[k];
					}
					return Div(pvt*close,normalized*atr);
				}
				case 33:
				{
					double hi=Double.NegativeInfinity,lo=Double.PositiveInfinity;
					for(int k=start;k<=at;k++)
					{
						hi=Math.Max(hi,sr.H[k]);
						lo=Math.Min(lo,sr.L[k]);
					}
					double x=Div(2*close-hi-lo,hi-lo);
					x=Math.Max(-0.98,Math.Min(0.98,x));
					return Math.Log((1+x)/(1-x))/2.0;
				}
				case 34:
				{
					double num=0,den=0;
					for(int k=start;k<=at;k++)
					{
						num+=sr.C[k]-sr.O[k];
						den+=sr.H[k]-sr.L[k];
					}
					return Div(num,den);
				}
				case 35:
				{
					double expansion=0,direction=0;
					for(int k=start;k<=at;k++)
					{
						double range=sr.H[k]-sr.L[k];
						double prev=sr.H[k-1]-sr.L[k-1];
						double impulse=Math.Max(0,range-prev);
						expansion+=impulse;
						direction+=Math.Sign(sr.C[k]-sr.C[k-1])*
							impulse;
					}
					return Div(direction,expansion);
				}
			}
			throw new ArgumentOutOfRangeException("family");
		}
		public static double Compute(NQCE3_Series sr,int at,int f,int n){return Feature(sr,at,f,n);}
	}

	internal static class NQCE3_FeatTrend
	{

		public static double Compute(NQCE3_Series sr,int at,int n,int f)
		{
			double p=sr.C[at],atr=NQCE3_M.ATR(sr,at,n);
			int half=Math.Max(2,n/2),slow=Math.Max(3,2*n);
			switch(f)
			{
				case 36: // fast / slow simple-average spread
					return (NQCE3_M.SMA(sr,at,half)-NQCE3_M.SMA(sr,at,n))/atr;
				case 37: // dual EMA spread
					return (NQCE3_M.EMA(sr,at,half)-NQCE3_M.EMA(sr,at,n))/atr;
				case 38: // dual WMA spread
					return (NQCE3_M.WMA(sr,at,half)-NQCE3_M.WMA(sr,at,n))/atr;
				case 39: // Wilder smoothed close displacement
					return (p-NQCE3_M.SMMA(sr,at,n))/atr;
				case 40: // Wilder smoother slope
					return (NQCE3_M.SMMA(sr,at,n)-NQCE3_M.SMMA(sr,at-1,n))/atr;
				case 41: // residual relative to least-squares fit at t
					return (p-NQCE3_M.Fit(sr,at,n))/atr;
				case 42: // signed regression fit quality, R-squared
				{
					double mu=NQCE3_M.SMA(sr,at,n),ssT=0,ssE=0;
					double slope=NQCE3_M.LinSlope(sr,at,n);
					for(int j=0;j<n;j++)
					{
						double obs=sr.C[at-n+1+j];
						double pred=mu+slope*(j-(n-1)/2.0);
						ssT+=(obs-mu)*(obs-mu);
						ssE+=(obs-pred)*(obs-pred);
					}
					return Math.Sign(slope)*NQCE3_M.Clip(1-NQCE3_M.Div(ssE,ssT),0,1);
				}
				case 43: // change in linear-regression slope
					return (NQCE3_M.LinSlope(sr,at,n)-
							NQCE3_M.LinSlope(sr,at-n/2,n))/atr;
				case 44: // KAMA recursive smoothed trend displacement
				{
					int from=at-2*n;
					double kama=sr.C[from];
					for(int k=from+1;k<=at;k++)
					{
						int t=Math.Max(from,k-n),diffs=0;
						double travel=0;
						for(int j=t+1;j<=k;j++)
						{
							travel+=Math.Abs(sr.C[j]-sr.C[j-1]);
							diffs++;
						}
						double er=diffs==0?0:
							Math.Abs(NQCE3_M.Div(sr.C[k]-sr.C[t],travel));
						double a=Math.Pow(er*(2.0/3-2.0/31)+2.0/31,2);
						kama+=a*(sr.C[k]-kama);
					}
					return (p-kama)/atr;
				}
				case 45: // FRAMA fractal adaptive EMA
				{
					double frama=sr.C[at-2*n];
					int h=Math.Max(2,n/2);
					for(int k=at-2*n+1;k<=at;k++)
					{
						if(k<n)continue;
						double hi1=NQCE3_M.Highest(sr,k,h,2),
							   lo1=NQCE3_M.Lowest(sr,k,h,3),
							   hi2=NQCE3_M.Highest(sr,k-h,h,2),
							   lo2=NQCE3_M.Lowest(sr,k-h,h,3),
							   hi=NQCE3_M.Highest(sr,k,2*h,2),
							   lo=NQCE3_M.Lowest(sr,k,2*h,3);
						double d1=(hi1-lo1)/h,d2=(hi2-lo2)/h,
							   d3=(hi-lo)/(2.0*h);
						double dimension=d1+d2<=NQCE3_M.Eps||d3<=NQCE3_M.Eps?
							1:Math.Log((d1+d2)/d3)/Math.Log(2);
						double alpha=NQCE3_M.Clip(Math.Exp(-4.6*
							(dimension-1)),0.01,1);
						frama+=alpha*(sr.C[k]-frama);
					}
					return (p-frama)/atr;
				}
				case 46: // McGinley Dynamic adaptivity using price ratio
				{
					double dynamic=sr.C[at-2*n];
					for(int k=at-2*n+1;k<=at;k++)
					{
						double ratio=NQCE3_M.Div(sr.C[k],dynamic);
						double denom=n*Math.Pow(NQCE3_M.Clip(ratio,0.5,2),4);
						dynamic+=NQCE3_M.Div(sr.C[k]-dynamic,
							Math.Max(1,denom));
					}
					return (p-dynamic)/atr;
				}
				case 47: // Tillson T3, six EMA cascades, fixed vfactor=.7
				{
					double alpha=2.0/(n+1),v=0.7;
					double e1=sr.C[at-3*n],e2=e1,e3=e1,
						e4=e1,e5=e1,e6=e1;
					for(int k=at-3*n+1;k<=at;k++)
					{
						e1+=alpha*(sr.C[k]-e1);
						e2+=alpha*(e1-e2);e3+=alpha*(e2-e3);
						e4+=alpha*(e3-e4);e5+=alpha*(e4-e5);
						e6+=alpha*(e5-e6);
					}
					double c1=-v*v*v,c2=3*v*v+3*v*v*v;
					double c3=-6*v*v-3*v-3*v*v*v;
					double c4=1+3*v+3*v*v+v*v*v;
					return (p-(c1*e6+c2*e5+c3*e4+c4*e3))/atr;
				}
				case 48: // double smoothed SMA, triangular moving average
				{
					int a=(n+1)/2,c=n-a+1;
					double sum=0;
					for(int i=0;i<c;i++)
						sum+=NQCE3_M.SMA(sr,at-i,a);
					return (p-sum/c)/atr;
				}
				case 49: // median (high+low)/2 EMA displacement
					return (p-NQCE3_M.EMA(sr,at,n,6))/atr;
				case 50: // typical (high+low+close)/3 EMA displacement
					return (p-NQCE3_M.EMA(sr,at,n,5))/atr;
				case 51: // weighted-close (H+L+2C)/4 EMA displacement
					return (p-NQCE3_M.EMA(sr,at,n,7))/atr;
				case 52: // typical price versus SMA of typical prices
					return (NQCE3_M.P(sr,at,5)-NQCE3_M.SMA(sr,at,n,5))/atr;
				case 53: // sine weighted moving-average displacement
				{
					double sum=0,den=0;
					for(int j=0;j<n;j++)
					{
						double w=Math.Sin(Math.PI*(n-j)/(n+1));
						sum+=w*sr.C[at-j];den+=w;
					}
					return (p-NQCE3_M.Div(sum,den))/atr;
				}
				case 54: // VIDYA via magnitude of Chande momentum
				{
					double vidya=sr.C[at-2*n];
					for(int k=at-2*n+1;k<=at;k++)
					{
						int look=Math.Min(n,k-(at-2*n));
						double gain=0,loss=0;
						for(int j=k-look+1;j<=k;j++)
						{
							double d=sr.C[j]-sr.C[j-1];
							gain+=Math.Max(0,d);loss+=Math.Max(0,-d);
						}
						double cmo=Math.Abs(NQCE3_M.Div(gain-loss,gain+loss));
						vidya+=2.0/(n+1)*cmo*(sr.C[k]-vidya);
					}
					return (p-vidya)/atr;
				}
				case 55: // Zero-lag LSMA: 2*LSMA(n)-LSMA(n) lagged half
					return (p-(2*NQCE3_M.Fit(sr,at,n)-
						NQCE3_M.Fit(sr,at-half,n)))/atr;
				case 56: // EMA-smoothed momentum, n-lag
				{
					int start=at-2*n,lag=Math.Max(2,n/2);
					double alpha=2.0/(n+1);
					double m=sr.C[start]-sr.C[start-lag];
					for(int k=start+1;k<=at;k++)
						m+=alpha*((sr.C[k]-sr.C[k-lag])-m);
					return m/atr;
				}
			}
			throw new ArgumentOutOfRangeException("family");
		}
	}

	internal static class NQCE3_FeatMom
	{

		private static double Stoch(NQCE3_Series sr,int at,int n)
		{
			double hi=NQCE3_M.Highest(sr,at,n,2),
				   lo=NQCE3_M.Lowest(sr,at,n,3);
			return NQCE3_M.Div(2*sr.C[at]-hi-lo,hi-lo);
		}
		private static double MACD(NQCE3_Series sr,int at,int n)
		{return NQCE3_M.EMA(sr,at,Math.Max(2,n/2))-
				NQCE3_M.EMA(sr,at,Math.Max(3,2*n));}
		private static double TRIX(NQCE3_Series sr,int at,int n)
		{
			int from=Math.Max(0,at-3*n-4);
			double alpha=2.0/(n+1),e1=sr.C[from],e2=e1,e3=e2,
				   prior=e3;
			for(int k=from+1;k<=at;k++)
			{
				prior=e3;
				e1+=alpha*(sr.C[k]-e1);
				e2+=alpha*(e1-e2);
				e3+=alpha*(e2-e3);
			}
			return NQCE3_M.Div(e3-prior,Math.Abs(e3));
		}
		private static double SMI(NQCE3_Series sr,int at,int n)
		{
			int from=at-n+1;
			double alpha=2.0/(Math.Max(2,n/2)+1);
			double d=0,r=0,d2=0,r2=0;
			for(int k=from;k<=at;k++)
			{
				int look=Math.Min(n,k+1);
				double hi=NQCE3_M.Highest(sr,k,look,2),
					   lo=NQCE3_M.Lowest(sr,k,look,3);
				double x=sr.C[k]-(hi+lo)/2;
				double half=(hi-lo)/2;
				if(k==from){d=x;r=half;d2=d;r2=r;}
				else
				{
					d+=alpha*(x-d);r+=alpha*(half-r);
					d2+=alpha*(d-d2);r2+=alpha*(r-r2);
				}
			}
			return NQCE3_M.Div(d2,r2);
		}
		public static double Compute(NQCE3_Series sr,int at,int n,int f)
		{
			double p=sr.C[at],atr=NQCE3_M.ATR(sr,at,n);
			int half=Math.Max(2,n/2),start=at-n+1;
			switch(f)
			{
				case 57: // Williams accumulation/distribution (WAD)
				{
					double ad=0,scale=0;
					for(int k=start;k<=at;k++)
					{
						double prev=sr.C[k-1];
						double reference=sr.C[k]>prev?
							Math.Min(sr.L[k],prev):
							sr.C[k]<prev?Math.Max(sr.H[k],prev):
							sr.C[k];
						ad+=sr.C[k]-reference;
						scale+=NQCE3_M.TR(sr,k);
					}
					return NQCE3_M.Div(ad,scale);
				}
				case 58: // Ultimate oscillator with 7/14/28 scaled by n
				{
					double value=0;
					for(int group=0;group<3;group++)
					{
						int lag=group==0?half:group==1?n:2*n;
						double bp=0,tr=0;
						for(int k=at-lag+1;k<=at;k++)
						{
							double prev=sr.C[k-1];
							bp+=sr.C[k]-Math.Min(sr.L[k],prev);
							tr+=Math.Max(sr.H[k],prev)-
								Math.Min(sr.L[k],prev);
						}
						value+=(group==0?4:group==1?2:1)*
							NQCE3_M.Div(bp,tr);
					}
					return 2*value/7-1;
				}
				case 59: // stochastic RSI with rolling RSI extrema
				{
					double lo=Double.PositiveInfinity,hi=Double.NegativeInfinity;
					for(int k=start;k<=at;k++)
					{
						double x=NQCE3_M.RSI(sr,k,n);
						lo=Math.Min(lo,x);hi=Math.Max(hi,x);
					}
					return NQCE3_M.Div(2*NQCE3_M.RSI(sr,at,n)-hi-lo,hi-lo);
				}
				case 60: // Connors RSI: RSI3 + streak RSI2 + percent rank ROC1
				{
					double streak=0,prev=0,gain=0,loss=0;
					for(int k=at-n+1;k<=at;k++)
					{
						double diff=sr.C[k]-sr.C[k-1];
						streak=diff>0?Math.Max(1,streak+1):
							diff<0?Math.Min(-1,streak-1):0;
						if(k>start)
						{
							double d=streak-prev;
							gain+=Math.Max(0,d);loss+=Math.Max(0,-d);
						}
						prev=streak;
					}
					double percent=0,current=NQCE3_M.ROC(sr,at,1);
					for(int k=start;k<=at;k++)
						if(NQCE3_M.ROC(sr,k,1)<current)percent++;
					return (NQCE3_M.RSI(sr,at,3)+NQCE3_M.Div(gain-loss,gain+loss)+
						2*percent/n-1)/3;
				}
				case 61: // RMI: RSI based on lagged momentum, not one-bar
				{
					double up=0,down=0;
					for(int k=start;k<=at;k++)
					{
						double d=sr.C[k]-sr.C[k-half];
						up+=Math.Max(d,0);down+=Math.Max(-d,0);
					}
					return NQCE3_M.Div(up-down,up+down);
				}
				case 62: // Difference between short and long RSI, signed
					return NQCE3_M.RSI(sr,at,half)-NQCE3_M.RSI(sr,at,n);
				case 63: // Percentage price oscillator
					return NQCE3_M.Div(MACD(sr,at,n),
						NQCE3_M.EMA(sr,at,2*n));
				case 64: // Percentage volume oscillator
					return NQCE3_M.Div(NQCE3_M.EMA(sr,at,half,4)-
						NQCE3_M.EMA(sr,at,n,4),NQCE3_M.EMA(sr,at,n,4));
				case 65: // KST: 4 ROC averages at lag 1..4 × half
				{
					double kst=0;
					for(int level=1;level<=4;level++)
					{
						int lag=level*half;
						double avg=0;
						for(int j=0;j<half;j++)
							avg+=NQCE3_M.ROC(sr,at-j,lag);
						kst+=level*avg/half;
					}
					return kst/10;
				}
				case 66: // KST first derivative (signal-line spread)
				{
					double current=0,previous=0;
					for(int level=1;level<=4;level++)
					{
						int lag=level*half;
						double a=0,z=0;
						for(int j=0;j<half;j++)
						{
							a+=NQCE3_M.ROC(sr,at-j,lag);
							z+=NQCE3_M.ROC(sr,at-j-1,lag);
						}
						current+=level*a/half;
						previous+=level*z/half;
					}
					return (current-previous)/10;
				}
				case 67: // TRIX momentum acceleration
					return (TRIX(sr,at,n)-TRIX(sr,at-1,n))*100;
				case 68: // Inverse Fisher transform of RSI
					return Math.Tanh(2.5*NQCE3_M.RSI(sr,at,n));
				case 69: // STC-like double stochastic normalization MACD
				{
					double lo=Double.PositiveInfinity,hi=Double.NegativeInfinity;
					for(int k=start;k<=at;k++)
					{
						double m=MACD(sr,k,n);
						lo=Math.Min(lo,m);hi=Math.Max(hi,m);
					}
					double stoch=NQCE3_M.Div(MACD(sr,at,n)-lo,hi-lo);
					double mean=0;
					for(int k=start;k<=at;k++)
					{
						double min=Double.PositiveInfinity,
							   max=Double.NegativeInfinity;
						for(int j=Math.Max(start,k-half+1);j<=k;j++)
						{
							double v=MACD(sr,j,n);
							min=Math.Min(min,v);max=Math.Max(max,v);
						}
						mean+=NQCE3_M.Div(MACD(sr,k,n)-min,max-min);
					}
					return 2*(0.5*stoch+0.5*mean/n)-1;
				}
				case 70: // Double-smoothed stochastic momentum index
					return SMI(sr,at,n);
				case 71: // Choppiness index with directional trend sign
				{
					double sum=0;
					for(int k=start;k<=at;k++)sum+=NQCE3_M.TR(sr,k);
					double range=NQCE3_M.Highest(sr,at,n,2)-
						NQCE3_M.Lowest(sr,at,n,3);
					double chop=range<=NQCE3_M.Eps?1:
						NQCE3_M.Clip(Math.Log10(Math.Max(1,sum/range))/
						  Math.Log10(n),0,1);
					return NQCE3_M.Direction(sr,at,n)*(1-chop);
				}
				case 72: // CCI of typical price, EMA center versus SMA
				{
					double t=NQCE3_M.P(sr,at,5),mean=NQCE3_M.EMA(sr,at,n,5),dev=0;
					for(int k=start;k<=at;k++)
						dev+=Math.Abs(NQCE3_M.P(sr,k,5)-mean);
					return NQCE3_M.Div(t-mean,0.015*dev/n*150);
				}
				case 73: // Ehlers center of gravity weighted-price oscillator
				{
					double weighted=0,total=0;
					for(int j=0;j<n;j++)
					{
						double x=sr.C[at-j];
						weighted+=(n-j)*x;total+=x;
					}
					return 2*NQCE3_M.Div(weighted,total)/(n+1)-1;
				}
				case 74: // Laguerre RSI 4-filter, gamma 0.5
				{
					double l0=sr.C[at-n],l1=l0,l2=l0,l3=l0;
					for(int k=at-n+1;k<=at;k++)
					{
						double x=sr.C[k];
						double a=l0,bb=l1,c=l2,d=l3;
						l0=0.5*a+0.5*x;
						l1=-0.5*l0+a+0.5*bb;
						l2=-0.5*l1+bb+0.5*c;
						l3=-0.5*l2+c+0.5*d;
					}
					double cu=Math.Max(0,l0-l1)+Math.Max(0,l1-l2)+
						Math.Max(0,l2-l3);
					double cd=Math.Max(0,l1-l0)+Math.Max(0,l2-l1)+
						Math.Max(0,l3-l2);
					return NQCE3_M.Div(cu-cd,cu+cd);
				}
				case 75: // Coppock: WMA of dual ROC
				{
					double sum=0,weight=0;
					for(int j=0;j<n;j++)
					{
						int k=at-j;double w=n-j;
						sum+=w*(NQCE3_M.ROC(sr,k,half)+NQCE3_M.ROC(sr,k,n));
						weight+=w;
					}
					return NQCE3_M.Div(sum,weight);
				}
				case 76: // Balance of Power, SMA normalized candle body/range
				{
					double sum=0;
					for(int k=start;k<=at;k++)
						sum+=NQCE3_M.Div(sr.C[k]-sr.O[k],
								   sr.H[k]-sr.L[k]);
					return sum/n;
				}
				case 77: // Elder bull/bear power, full range above/below EMA
				{
					double ema=NQCE3_M.EMA(sr,at,n);
					return (sr.H[at]+sr.L[at]-2*ema)/(2*atr);
				}
			}
			throw new ArgumentOutOfRangeException("family");
		}
	}

	internal static class NQCE3_FeatVola
	{

		private static double LogReturn(NQCE3_Series sr,int at)
		{return Math.Log(sr.C[at]/sr.C[at-1]);}
		private static double StdLog(NQCE3_Series sr,int at,int n)
		{
			double mean=0,s=0;
			for(int k=at-n+1;k<=at;k++)mean+=LogReturn(sr,k);
			mean/=n;
			for(int k=at-n+1;k<=at;k++)
			{
				double d=LogReturn(sr,k)-mean;s+=d*d;
			}
			return Math.Sqrt(s/n);
		}
		private static double Parkinson(NQCE3_Series sr,int at,int n)
		{
			double v=0;
			for(int k=at-n+1;k<=at;k++)
			{
				double x=Math.Log(sr.H[k]/sr.L[k]);
				v+=x*x;
			}
			return Math.Sqrt(v/(4*n*Math.Log(2)));
		}
		private static double GarmanKlass(NQCE3_Series sr,int at,int n)
		{
			double v=0;
			for(int k=at-n+1;k<=at;k++)
			{
				double hl=Math.Log(sr.H[k]/sr.L[k]),
					co=Math.Log(sr.C[k]/sr.O[k]);
				v+=0.5*hl*hl-(2*Math.Log(2)-1)*co*co;
			}
			return Math.Sqrt(Math.Max(0,v/n));
		}
		private static double RogersSatchell(NQCE3_Series sr,int at,int n)
		{
			double v=0;
			for(int k=at-n+1;k<=at;k++)
			{
				var c=sr.Bar(k);
				v+=Math.Log(c.High/c.Close)*Math.Log(c.High/c.Open)+
					Math.Log(c.Low/c.Close)*Math.Log(c.Low/c.Open);
			}
			return Math.Sqrt(Math.Max(0,v/n));
		}
		private static double YangZhang(NQCE3_Series sr,int at,int n)
		{
			double meanOvernight=0,meanDay=0,overnight=0,day=0,rs=0;
			for(int k=at-n+1;k<=at;k++)
			{
				meanOvernight+=Math.Log(sr.O[k]/sr.C[k-1]);
				meanDay+=Math.Log(sr.C[k]/sr.O[k]);
			}
			meanOvernight/=n;meanDay/=n;
			for(int k=at-n+1;k<=at;k++)
			{
				double o=Math.Log(sr.O[k]/sr.C[k-1])-meanOvernight;
				double d=Math.Log(sr.C[k]/sr.O[k])-meanDay;
				overnight+=o*o;day+=d*d;
				var c=sr.Bar(k);
				rs+=Math.Log(c.High/c.Close)*Math.Log(c.High/c.Open)+
					Math.Log(c.Low/c.Close)*Math.Log(c.Low/c.Open);
			}
			double w=0.34/(1.34+(n+1.0)/Math.Max(1,n-1));
			return Math.Sqrt(Math.Max(0,
				overnight/n+w*day/n+(1-w)*rs/n));
		}
		public static double Compute(NQCE3_Series sr,int at,int n,int f)
		{
			double p=sr.C[at],atr=NQCE3_M.ATR(sr,at,n);
			double trend=NQCE3_M.Div(p-sr.C[at-n],n*atr);
			int h=Math.Max(2,n/2),start=at-n+1;
			switch(f)
			{
				case 78: // Keltner breakout beyond EMA +/- 1.5 ATR
				{
					double deviation=NQCE3_M.Div(p-NQCE3_M.EMA(sr,at,n),atr);
					return Math.Sign(deviation)*
						Math.Max(0,Math.Abs(deviation)-1.5);
				}
				case 79: // Donchian centerline slope, price units / ATR
				{
					double old=(NQCE3_M.Highest(sr,at-n/2,n,2)+
						NQCE3_M.Lowest(sr,at-n/2,n,3))/2;
					double now=(NQCE3_M.Highest(sr,at,n,2)+
						NQCE3_M.Lowest(sr,at,n,3))/2;
					return (now-old)/atr;
				}
				case 80: // breakout beyond prior n bars, no current bar
				{
					double hi=NQCE3_M.Highest(sr,at-1,n,2),
						lo=NQCE3_M.Lowest(sr,at-1,n,3);
					if(p>hi)return (p-hi)/atr;
					if(p<lo)return (p-lo)/atr;
					return 0;
				}
				case 81: // close - Donchian high/low midpoint
					return (p-(NQCE3_M.Highest(sr,at,n,2)+
						NQCE3_M.Lowest(sr,at,n,3))/2)/atr;
				case 82: // adaptive ATR band angle / smoothed center
				{
					double w=atr/NQCE3_M.ATR(sr,at-1,n);
					return NQCE3_M.Div(NQCE3_M.EMA(sr,at,n)-NQCE3_M.EMA(sr,at-1,n),
						atr)*w;
				}
				case 83: // Bollinger bandwidth regime weighted by trend
					return trend*NQCE3_M.Div(4*NQCE3_M.Std(sr,at,n),p);
				case 84: // signed change in realized price deviation
					return NQCE3_M.Direction(sr,at,h)*
						(NQCE3_M.Std(sr,at,n)-NQCE3_M.Std(sr,at-1,n))/atr;
				case 85: // realized log-volatility expansion
					return NQCE3_M.Direction(sr,at,h)*
						NQCE3_M.Div(StdLog(sr,at,n)-StdLog(sr,at-1,n),
							  Math.Max(StdLog(sr,at-1,n),NQCE3_M.Eps));
				case 86: // normalized ATR change in price-normalized units
					return NQCE3_M.Direction(sr,at,h)*
						(atr/p-NQCE3_M.ATR(sr,at-1,n)/sr.C[at-1]);
				case 87: // Parkinson high-low volatility signed by trend
					return Math.Sign(trend)*Parkinson(sr,at,n);
				case 88: // Garman-Klass OHLC volatility signed by trend
					return Math.Sign(trend)*GarmanKlass(sr,at,n);
				case 89: // Rogers-Satchell stochastic OHLC volatility
					return Math.Sign(trend)*RogersSatchell(sr,at,n);
				case 90: // Yang-Zhang overnight + intrabar volatility
					return Math.Sign(trend)*YangZhang(sr,at,n);
				case 91: // Chaikin volatility of EMA high-low spread
				{
					double current=NQCE3_M.EMA(sr,at,n,9);
					double old=NQCE3_M.EMA(sr,at-h,n,9);
					return NQCE3_M.Direction(sr,at,h)*NQCE3_M.Div(current-old,old);
				}
				case 92: // downside ulcer drawdown scaled by trend
				{
					double ulcer=0;
					for(int k=start;k<=at;k++)
					{
						double hi=NQCE3_M.Highest(sr,k,Math.Min(n,k+1),0);
						double dd=NQCE3_M.Div(sr.C[k]-hi,hi);
						ulcer+=dd*dd;
					}
					return NQCE3_M.Div(trend,
						Math.Max(Math.Sqrt(ulcer/n),NQCE3_M.Eps));
				}
				case 93: // Chande-Kroll trailing stop envelope breakout
				{
					// Unlike a symmetric midpoint (which cancels ATR and
					// duplicates Donchian center), the LONG stop is the
					// maximum of historical ATR-adjusted highs; the SHORT
					// stop is the minimum of ATR-adjusted lows.
					double longStop=Double.NegativeInfinity,
						shortStop=Double.PositiveInfinity;
					for(int k=at-h+1;k<=at;k++)
					{
						double a=NQCE3_M.ATR(sr,k,n);
						longStop=Math.Max(longStop,
							NQCE3_M.Highest(sr,k,n,2)-2*a);
						shortStop=Math.Min(shortStop,
							NQCE3_M.Lowest(sr,k,n,3)+2*a);
					}
					if(p>longStop)return (p-longStop)/atr;
					if(p<shortStop)return (p-shortStop)/atr;
					return 0;
				}
				case 94: // Supertrend stop-band signed close distance
				{
					// Sequential prior bands; never shift a band forward.
					double lower=Double.NaN,upper=Double.NaN;
					bool up=true;
					for(int k=at-2*n+1;k<=at;k++)
					{
						double local=NQCE3_M.ATR(sr,k,Math.Min(n,k));
						double mid=(sr.H[k]+sr.L[k])/2;
						double lo=mid-2*local,hi=mid+2*local;
						if(Double.IsNaN(lower)){lower=lo;upper=hi;}
						else
						{
							if(sr.C[k-1]>upper)up=true;
							if(sr.C[k-1]<lower)up=false;
							lower=sr.C[k-1]>lower?
								Math.Max(lower,lo):lo;
							upper=sr.C[k-1]<upper?
								Math.Min(upper,hi):hi;
						}
					}
					return up?(p-lower)/atr:(p-upper)/atr;
				}
				case 95: // Ichimoku tenkan versus kijun CURRENT lines
				{
					double tenkan=(NQCE3_M.Highest(sr,at,h,2)+
						NQCE3_M.Lowest(sr,at,h,3))/2;
					double kijun=(NQCE3_M.Highest(sr,at,n,2)+
						NQCE3_M.Lowest(sr,at,n,3))/2;
					return (tenkan-kijun)/atr;
				}
				case 96: // Ichimoku non-forward projected cloud spread
				{
					double tenkan=(NQCE3_M.Highest(sr,at,h,2)+
						NQCE3_M.Lowest(sr,at,h,3))/2,
						kijun=(NQCE3_M.Highest(sr,at,n,2)+
						NQCE3_M.Lowest(sr,at,n,3))/2,
						spanA=(tenkan+kijun)/2,
						spanB=(NQCE3_M.Highest(sr,at,2*n,2)+
						NQCE3_M.Lowest(sr,at,2*n,3))/2;
					return (spanA-spanB)/atr;
				}
				case 97: // DeMark-style pivot from prior completed n window
				{
					double high=NQCE3_M.Highest(sr,at-1,n,2),
						low=NQCE3_M.Lowest(sr,at-1,n,3),
						close=sr.C[at-1],open=sr.O[at-1];
					double x=close<open?high+2*low+close:
						close>open?2*high+low+close:
						high+low+2*close;
					return (p-x/4)/atr;
				}
				case 98: // Bollinger-vs-Keltner squeeze directional momentum
				{
					double band=2*NQCE3_M.Std(sr,at,n);
					double channel=1.5*atr;
					double ratio=NQCE3_M.Div(band,channel);
					return trend*(ratio<1?1.0/Math.Max(0.1,ratio):
						Math.Min(ratio,5));
				}
			}
			throw new ArgumentOutOfRangeException("family");
		}
	}

	internal static class NQCE3_FeatVolAct
	{

		private static double Vema(NQCE3_Series sr,int at,int n)
		{
			// EMA of price*volume / EMA of volume. The zero-volume
			// edge case uses close, never a future quote.
			double a=NQCE3_M.EMA(sr,at,n,4);
			double alpha=2.0/(n+1);
			int from=Math.Max(0,at-3*n-5);
			double numerator=sr.C[from]*sr.V[from];
			for(int k=from+1;k<=at;k++)
				numerator+=alpha*(sr.C[k]*sr.V[k]-numerator);
			return a>NQCE3_M.Eps?numerator/a:sr.C[at];
		}
		private static double Flow(NQCE3_Series sr,int k)
		{
			double previous=NQCE3_M.P(sr,k-1,5),now=NQCE3_M.P(sr,k,5);
			return Math.Sign(now-previous)*sr.V[k];
		}
		public static double Compute(NQCE3_Series sr,int at,int n,int f)
		{
			int start=at-n+1,half=Math.Max(2,n/2);
			double price=sr.C[at],atr=NQCE3_M.ATR(sr,at,n);
			double volume=Math.Max(NQCE3_M.SumVolume(sr,at,n),NQCE3_M.Eps);
			switch(f)
			{
				case 99: // volume oscillator × signed price direction
					return NQCE3_M.Direction(sr,at,half)*NQCE3_M.Div(
						NQCE3_M.SMA(sr,at,half,4)-NQCE3_M.SMA(sr,at,n,4),
						NQCE3_M.SMA(sr,at,n,4));
				case 100: // relative volume surprise weighted price ROC
					return NQCE3_M.ROC(sr,at,half)*
						NQCE3_M.Div(sr.V[at],NQCE3_M.SMA(sr,at,n,4));
				case 101: // NVI: cumulative return on declining volume days
				{
					double value=0;
					for(int k=start;k<=at;k++)
						if(sr.V[k]<sr.V[k-1])
							value+=NQCE3_M.ROC(sr,k,1);
					return value;
				}
				case 102: // PVI: cumulative return on rising volume days
				{
					double value=0;
					for(int k=start;k<=at;k++)
						if(sr.V[k]>sr.V[k-1])
							value+=NQCE3_M.ROC(sr,k,1);
					return value;
				}
				case 103: // VFI: capped typical change times traded volume
				{
					double flow=0,total=0,vola=0;
					for(int k=start;k<=at;k++)
					{
						double pct=NQCE3_M.Div(NQCE3_M.P(sr,k,5)-NQCE3_M.P(sr,k-1,5),
							NQCE3_M.P(sr,k-1,5));
						vola+=pct*pct;
					}
					double cutoff=0.2*Math.Sqrt(vola/n);
					double average=NQCE3_M.SMA(sr,at,n,4);
					for(int k=start;k<=at;k++)
					{
						double diff=NQCE3_M.Div(NQCE3_M.P(sr,k,5)-NQCE3_M.P(sr,k-1,5),
							NQCE3_M.P(sr,k-1,5));
						double capped=Math.Min(sr.V[k],2.5*average);
						flow+=diff>cutoff?capped:
							diff< -cutoff?-capped:0;
						total+=capped;
					}
					return NQCE3_M.Div(flow,total);
				}
				case 104: // Accumulated volume-weighted price change
				{
					double a=0;
					for(int k=start;k<=at;k++)
						a+=NQCE3_M.ROC(sr,k,1)*sr.V[k];
					return NQCE3_M.Div(a,volume);
				}
				case 105: // PVT smoothed momentum, not raw PVT
				{
					int from=at-2*n,alphaN=n;
					double alpha=2.0/(alphaN+1),raw=0,smoothed=0;
					for(int k=from+1;k<=at;k++)
					{
						raw+=NQCE3_M.ROC(sr,k,1)*sr.V[k];
						smoothed+=alpha*(raw-smoothed);
					}
					return NQCE3_M.Div(raw-smoothed,volume);
				}
				case 106: // Klinger-like dual EMA volume-force oscillator
				{
					int from=Math.Max(1,at-3*n-5);
					double eShort=Flow(sr,from),eLong=eShort;
					double ashort=2.0/(half+1),along=2.0/(n+1);
					for(int k=from+1;k<=at;k++)
					{
						double force=Flow(sr,k);
						eShort+=ashort*(force-eShort);
						eLong+=along*(force-eLong);
					}
					return NQCE3_M.Div(eShort-eLong,
						Math.Max(1,NQCE3_M.SMA(sr,at,n,4)));
				}
				case 107: // Twiggs-like smoothed money flow
				{
					int from=at-2*n;
					double numerator=0,den=0,alpha=2.0/(n+1);
					for(int k=from;k<=at;k++)
					{
						var x=sr.Bar(k);
						double spread=Math.Max(x.High,
							sr.C[Math.Max(0,k-1)])-
							Math.Min(x.Low,sr.C[Math.Max(0,k-1)]);
						double mf=NQCE3_M.Div(2*x.Close-
							Math.Max(x.High,sr.C[k-1])-
							Math.Min(x.Low,sr.C[k-1]),spread)*
							x.Volume;
						if(k==from){numerator=mf;den=x.Volume;}
						else
						{
							numerator+=alpha*(mf-numerator);
							den+=alpha*(x.Volume-den);
						}
					}
					return NQCE3_M.Div(numerator,den);
				}
				case 108: // volume-weighted RSI of signed price changes
				{
					double up=0,down=0;
					for(int k=start;k<=at;k++)
					{
						double d=sr.C[k]-sr.C[k-1];
						up+=Math.Max(0,d)*sr.V[k];
						down+=Math.Max(0,-d)*sr.V[k];
					}
					return NQCE3_M.Div(up-down,up+down);
				}
				case 109: // VEMA fast-slow volume-adjusted MACD / ATR
					return (Vema(sr,at,half)-Vema(sr,at,n))/atr;
				case 110: // price-candle up-volume minus down-volume
				{
					double up=0,down=0;
					for(int k=start;k<=at;k++)
					{
						if(sr.C[k]>sr.O[k])up+=sr.V[k];
						if(sr.C[k]<sr.O[k])down+=sr.V[k];
					}
					return NQCE3_M.Div(up-down,up+down);
				}
				case 111: // signed momentum weighted by volume anomaly
					return NQCE3_M.Div(price-sr.C[at-half],atr)*
						NQCE3_M.Div(sr.V[at],
							Math.Max(1,NQCE3_M.SMA(sr,at,n,4)));
				case 112: // tick-free proportional body/volume delta
				{
					double delta=0;
					for(int k=start;k<=at;k++)
						delta+=NQCE3_M.Div(sr.C[k]-sr.O[k],
							sr.H[k]-sr.L[k])*sr.V[k];
					return NQCE3_M.Div(delta,volume);
				}
				case 113: // Heikin Ashi causal candle body strength
				{
					int from=at-n+1;
					double haOpen=(sr.O[from]+sr.C[from])/2;
					double haClose=(sr.O[from]+sr.H[from]+
						sr.L[from]+sr.C[from])/4;
					for(int k=from+1;k<=at;k++)
					{
						double prevOpen=haOpen,prevClose=haClose;
						haOpen=(prevOpen+prevClose)/2;
						haClose=(sr.O[k]+sr.H[k]+
							sr.L[k]+sr.C[k])/4;
					}
					return NQCE3_M.Div(haClose-haOpen,atr);
				}
				case 114: // body-to-upper/lower-wick pressure imbalance
				{
					double body=price-sr.O[at];
					double upper=sr.H[at]-Math.Max(price,sr.O[at]);
					double lower=Math.Min(price,sr.O[at])-sr.L[at];
					return NQCE3_M.Div(body+lower-upper,
						sr.H[at]-sr.L[at]);
				}
				case 115: // two-candle engulfing body pattern
				{
					var prev=sr.Bar(at-1);var curr=sr.Bar(at);
					bool bull=prev.Close<prev.Open&&curr.Close>curr.Open&&
						curr.Open<=prev.Close&&curr.Close>=prev.Open;
					bool bear=prev.Close>prev.Open&&curr.Close<curr.Open&&
						curr.Open>=prev.Close&&curr.Close<=prev.Open;
					return bull?1:bear?-1:0;
				}
				case 116: // inside / outside bar directional breakout
				{
					var x=sr.Bar(at);var prev=sr.Bar(at-1);
					bool inside=x.High<prev.High&&x.Low>prev.Low;
					bool outside=x.High>prev.High&&x.Low<prev.Low;
					if(outside)return NQCE3_M.Div(x.Close-x.Open,atr);
					if(inside)return NQCE3_M.Div(x.Close-prev.Close,2*atr);
					return 0;
				}
				case 117: // NR7 narrow-range compression / breakout
				{
					int lag=7;
					double range=sr.H[at-1]-sr.L[at-1];
					bool min=true;
					for(int k=at-lag;k<at-1;k++)
						if(range>=sr.H[k]-sr.L[k]){min=false;break;}
					if(!min)return 0;
					if(price>sr.H[at-1])
						return (price-sr.H[at-1])/atr;
					if(price<sr.L[at-1])
						return (price-sr.L[at-1])/atr;
					return 0;
				}
				case 118: // polarized fractal efficiency, signed path ratio
				{
					double path=0;
					for(int k=start;k<=at;k++)
					{
						double delta=sr.C[k]-sr.C[k-1];
						path+=Math.Sqrt(delta*delta+1);
					}
					double net=price-sr.C[at-n];
					double direct=Math.Sqrt(net*net+n*n);
					return Math.Sign(net)*NQCE3_M.Div(direct,path);
				}
				case 119: // Hurst rescaled-range exponent × signed trend
				{
					double mean=0,variance=0,cumulative=0;
					double hi=Double.NegativeInfinity,
						lo=Double.PositiveInfinity;
					for(int k=start;k<=at;k++)
						mean+=sr.C[k]-sr.C[k-1];
					mean/=n;
					for(int k=start;k<=at;k++)
					{
						double d=sr.C[k]-sr.C[k-1]-mean;
						variance+=d*d;
						cumulative+=d;
						hi=Math.Max(hi,cumulative);
						lo=Math.Min(lo,cumulative);
					}
					double s=Math.Sqrt(variance/n);
					double hurst=s<=NQCE3_M.Eps?0.5:
						Math.Log(Math.Max(1,(hi-lo)/s))/Math.Log(n);
					return NQCE3_M.Direction(sr,at,n)*
						(NQCE3_M.Clip(hurst,0,1)-0.5)*2;
				}
			}
			throw new ArgumentOutOfRangeException("family");
		}
	}

}
