#region Using declarations
using System;
using System.Collections.Generic;
#endregion

//==============================================================================
// NQC_ET3_LibMath - ET3 WP1: matematica comun (SMA/EMA/ATR simple/RSI/regresion...) de la biblioteca D-018.
// Procedencia: ver cabecera de NQC_ET3_Library.cs. SOLO DESARROLLO, sin compilar.
// Cada funcion Compute(sr, at, f, n) lee unicamente barras con indice <= at (cerradas) y devuelve el
// valor de la familia f con periodo n. Las reglas (11) estan en NQCE3_Rules (NQC_ET3_Library.cs).
//==============================================================================
namespace NinjaTrader.NinjaScript.Strategies
{
	internal static class NQCE3_M
	{

		internal const double Eps=1e-12;
		internal static double Div(double a,double b)
		{return Math.Abs(b)<Eps?0:a/b;}
		internal static double Clip(double a,double lo,double hi)
		{return Math.Min(hi,Math.Max(lo,a));}
		internal static double Price(NQCE3_Bar b,int kind)
		{
			switch(kind)
			{
				case 0:return b.Close;
				case 1:return b.Open;
				case 2:return b.High;
				case 3:return b.Low;
				case 4:return b.Volume;
				case 5:return (b.High+b.Low+b.Close)/3;
				case 6:return (b.High+b.Low)/2;
				case 7:return (b.High+b.Low+2*b.Close)/4;
				case 8:return b.Close-b.Open;
				case 9:return b.High-b.Low;
				default:throw new ArgumentOutOfRangeException("kind");
			}
		}
		internal static double P(NQCE3_Series sr,int at,int kind=0)
		{return Price(sr.Bar(at),kind);}
		internal static double SMA(NQCE3_Series sr,int at,int n,int kind=0)
		{
			double s=0;for(int k=at-n+1;k<=at;k++)s+=P(sr,k,kind);
			return s/n;
		}
		internal static double WMA(NQCE3_Series sr,int at,int n,int kind=0)
		{
			double s=0,den=n*(n+1)/2.0;
			for(int j=0;j<n;j++)s+=(n-j)*P(sr,at-j,kind);
			return s/den;
		}
		internal static double EMA(NQCE3_Series sr,int at,int n,int kind=0)
		{
			int from=Math.Max(0,at-3*n-5);
			double alpha=2.0/(n+1),x=P(sr,from,kind);
			for(int k=from+1;k<=at;k++)
				x+=alpha*(P(sr,k,kind)-x);
			return x;
		}
		internal static double SMMA(NQCE3_Series sr,int at,int n,int kind=0)
		{
			int from=Math.Max(0,at-3*n-5);
			double x=P(sr,from,kind);
			for(int k=from+1;k<=at;k++)
				x+=(P(sr,k,kind)-x)/n;
			return x;
		}
		internal static double TR(NQCE3_Series sr,int at)
		{
			var c=sr.Bar(at);double p=sr.C[Math.Max(0,at-1)];
			return Math.Max(c.High-c.Low,Math.Max(
				Math.Abs(c.High-p),Math.Abs(c.Low-p)));
		}
		internal static double ATR(NQCE3_Series sr,int at,int n)
		{
			double s=0;
			for(int k=at-n+1;k<=at;k++)s+=TR(sr,k);
			return Math.Max(s/n,Eps);
		}
		internal static double Highest(NQCE3_Series sr,int at,int n,
									   int kind)
		{
			double x=Double.NegativeInfinity;
			for(int k=at-n+1;k<=at;k++)x=Math.Max(x,P(sr,k,kind));
			return x;
		}
		internal static double Lowest(NQCE3_Series sr,int at,int n,
									  int kind)
		{
			double x=Double.PositiveInfinity;
			for(int k=at-n+1;k<=at;k++)x=Math.Min(x,P(sr,k,kind));
			return x;
		}
		internal static double Std(NQCE3_Series sr,int at,int n,
								   int kind=0)
		{
			double mean=SMA(sr,at,n,kind),s=0;
			for(int k=at-n+1;k<=at;k++)
			{
				double d=P(sr,k,kind)-mean;
				s+=d*d;
			}
			return Math.Sqrt(s/n);
		}
		internal static double SumVolume(NQCE3_Series sr,int at,int n)
		{
			if(sr.V==null)return 0;   // sin volumen enlazado (familias 113-119 no lo usan)
			double s=0;for(int k=at-n+1;k<=at;k++)s+=sr.V[k];
			return s;
		}
		internal static double Direction(NQCE3_Series sr,int at,int lag)
		{return Math.Sign(sr.C[at]-sr.C[at-lag]);}
		internal static double ROC(NQCE3_Series sr,int at,int lag,
								   int kind=0)
		{return Div(P(sr,at,kind)-P(sr,at-lag,kind),P(sr,at-lag,kind));}
		internal static double Momentum(NQCE3_Series sr,int at,int lag)
		{return sr.C[at]-sr.C[at-lag];}
		internal static double RSI(NQCE3_Series sr,int at,int n,
								  int kind=0)
		{
			double gain=0,loss=0;
			for(int k=at-n+1;k<=at;k++)
			{
				double diff=P(sr,k,kind)-P(sr,k-1,kind);
				gain+=Math.Max(0,diff);loss+=Math.Max(0,-diff);
			}
			return Div(gain-loss,gain+loss);
		}
		internal static double Efficiency(NQCE3_Series sr,int at,int n)
		{
			double sum=0;
			for(int k=at-n+1;k<=at;k++)
				sum+=Math.Abs(sr.C[k]-sr.C[k-1]);
			return Div(sr.C[at]-sr.C[at-n],sum);
		}
		internal static double LinSlope(NQCE3_Series sr,int at,int n,
										int kind=0)
		{
			double mean=(n-1)/2.0,mu=SMA(sr,at,n,kind);
			double num=0,den=0;
			for(int j=0;j<n;j++)
			{
				double x=j-mean;
				num+=x*(P(sr,at-n+1+j,kind)-mu);
				den+=x*x;
			}
			return Div(num,den);
		}
		internal static double Fit(NQCE3_Series sr,int at,int n,
								   int kind=0)
		{return SMA(sr,at,n,kind)+LinSlope(sr,at,n,kind)*(n-1)/2.0;}
		internal static double Clv(NQCE3_Bar b)
		{return Div(2*b.Close-b.High-b.Low,b.High-b.Low);}
		internal static double Typical(NQCE3_Bar b)
		{return (b.High+b.Low+b.Close)/3.0;}
		internal static double Vwap(NQCE3_Series sr,int at,int n)
		{
			double a=0,v=0;
			for(int k=at-n+1;k<=at;k++)
			{
				v+=sr.V[k];a+=Typical(sr.Bar(k))*sr.V[k];
			}
			return v>0?a/v:sr.C[at];
		}
		internal static double EmaSeries(int from,int to,int n,
										  Func<int,double> value)
		{
			double a=2.0/(n+1),s=value(from);
			for(int k=from+1;k<=to;k++)
				s+=a*(value(k)-s);
			return s;
		}
	}
}
