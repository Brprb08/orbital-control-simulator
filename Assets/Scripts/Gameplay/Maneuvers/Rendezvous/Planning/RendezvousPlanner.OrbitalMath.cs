using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using Burn = TrajectoryMatchedPredictor.EncounterBurn;
using Timeline = TrajectoryMatchedPredictor.EncounterTimeline;

public static partial class RendezvousPlanner
{
    private static bool TrySolveLambert(double3 r1v, double3 r2v, double mu, double time,
        bool longWay, out double3 v1, out double3 v2)
    {
        v1=v2=default;
        double r1=math.length(r1v),r2=math.length(r2v);
        double cos=math.clamp(math.dot(r1v,r2v)/(r1*r2),-1,1);
        double sin=Math.Sqrt(Math.Max(0,1-cos*cos))*(longWay?-1:1);
        if(Math.Abs(1-cos)<1e-10||Math.Abs(sin)<1e-10) return false;
        double A=sin*Math.Sqrt(r1*r2/(1-cos));
        bool TimeAt(double z,out double value,out double y)
        {
            double c=StumpffC(z),s=StumpffS(z); value=0;y=0;
            if(!(c>0)) return false;
            y=r1+r2+A*(z*s-1)/Math.Sqrt(c);
            if(!(y>0)) return false;
            value=(Math.Pow(y/c,1.5)*s+A*Math.Sqrt(y))/Math.Sqrt(mu);
            return double.IsFinite(value);
        }
        double lower=-4*Math.PI*Math.PI,upper=4*Math.PI*Math.PI;
        bool bracketed=false; double previousZ=lower,previousF=0; bool havePrevious=false;
        for(int i=0;i<=256;i++)
        {
            double z=lower+(upper-lower)*i/256.0;
            if(!TimeAt(z,out double t,out _)) continue;
            double f=t-time;
            if(havePrevious&&Math.Sign(f)!=Math.Sign(previousF))
            {lower=previousZ;upper=z;bracketed=true;break;}
            previousZ=z;previousF=f;havePrevious=true;
        }
        if(!bracketed) return false;
        for(int i=0;i<64;i++)
        {
            double mid=(lower+upper)*.5;
            if(!TimeAt(mid,out double t,out _)){lower=mid;continue;}
            if(t>time) upper=mid; else lower=mid;
        }
        if(!TimeAt((lower+upper)*.5,out _,out double finalY)) return false;
        double fLag=1-finalY/r1,g=A*Math.Sqrt(finalY/mu),gDot=1-finalY/r2;
        if(Math.Abs(g)<1e-12) return false;
        v1=(r2v-fLag*r1v)/g; v2=(gDot*r2v-r1v)/g;
        return math.all(math.isfinite(v1))&&math.all(math.isfinite(v2));
    }

    private static double TransferArcPenalty(double3 startPosition, double3 transferVelocity,
        double mu, double flightTime, double targetRadius)
    {
        double endpointMaximum = Math.Max(math.length(startPosition), targetRadius);
        // Low-orbit phasing sometimes needs a visibly elliptical arc. At GEO scale,
        // the same percentage means thousands of kilometers of pointless overshoot.
        double overshootFactor = endpointMaximum > 2000 ? 1.08 : 10.0;
        double maximumAllowed = endpointMaximum * overshootFactor + 10;
        double minimumAllowed = PhysicsConstants.EarthRadiusUnits + 2; // 20 km safety floor.
        for (int i = 1; i < 32; i++)
        {
            if (!KeplerPropagator.TryPropagateUniversal(startPosition, transferVelocity, mu,
                    flightTime * i / 32.0, out var position, out _)) return 1000;
            double radius = math.length(position);
            if (radius < minimumAllowed) return 1000 + minimumAllowed - radius;
            if (radius > maximumAllowed)
                return Math.Max(0, radius / maximumAllowed - 1);
        }
        return 0;
    }

    private static bool TimelineWithinTransferBounds(Timeline timeline, double end)
    {
        double controlledInitial = 0, controlledMaximum = 0, targetMaximum = 0;
        for (int i = 0; i <= 128; i++)
        {
            if (!timeline.TrySample(end * i / 128.0, out var sample)) return false;
            controlledMaximum = Math.Max(controlledMaximum, math.length(sample.A));
            targetMaximum = Math.Max(targetMaximum, math.length(sample.B));
            if (i == 0) controlledInitial = math.length(sample.A);
            if (math.length(sample.A) < PhysicsConstants.EarthRadiusUnits + 2) return false;
        }
        double endpointMaximum = Math.Max(controlledInitial, targetMaximum);
        double overshootFactor = endpointMaximum > 2000 ? 1.08 : 10.0;
        return controlledMaximum <= endpointMaximum * overshootFactor + 10;
    }

    private static double StumpffC(double z)
    {
        if(z>1e-8){double q=Math.Sqrt(z);return(1-Math.Cos(q))/z;}
        if(z< -1e-8){double q=Math.Sqrt(-z);return(Math.Cosh(q)-1)/(-z);}
        return .5-z/24+z*z/720-z*z*z/40320;
    }
    private static double StumpffS(double z)
    {
        if(z>1e-8){double q=Math.Sqrt(z);return(q-Math.Sin(q))/(q*q*q);}
        if(z< -1e-8){double q=Math.Sqrt(-z);return(Math.Sinh(q)-q)/(q*q*q);}
        return 1.0/6-z/120+z*z/5040-z*z*z/362880;
    }

    private static bool BuildVectorBurns(double[] x, Propulsion engine, double epoch,
        Settings settings,
        out Burn[] burns,out double used,out double totalDv,out double[] deltaVs)
    {
        int count=(x.Length-1)/4;
        if(count < (settings.Goal.Mode == ArrivalMode.Intercept ? 1 : 2) || count>4 ||
            x.Length != count*4 + (settings.Goal.Mode == ArrivalMode.Intercept ? 2 : 1))
        {burns=Array.Empty<Burn>();deltaVs=Array.Empty<double>();used=totalDv=0;return false;}
        burns=new Burn[count];deltaVs=new double[count];used=0;totalDv=0;
        double mass=engine.DryMass+engine.FuelMass;
        for(int i=0;i<count;i++)
        {
            double3 vector=new double3(x[i*3],x[i*3+1],x[i*3+2]);
            double requested=math.length(vector);
            if(!(requested>1e-4)||requested>settings.MaxBurnDeltaV) return false;
            double consumed=engine.Finite?mass*(1-Math.Exp(-requested/(engine.Isp*9.80665))):0;
            if(consumed>engine.FuelMass-used) return false;
            double duration=engine.Finite?consumed*engine.Isp*9.80665/engine.Thrust:requested*mass/engine.Thrust;
            if(!(duration>0)||duration>settings.MaxBurnDuration) return false;
            double start=x[count*3+i]-duration*.5;
            float absoluteStart=(float)(epoch+start),absoluteEnd=absoluteStart+(float)duration;
            if(!float.IsFinite(absoluteStart)||!float.IsFinite(absoluteEnd))return false;
            if(absoluteEnd<=absoluteStart)absoluteEnd=math.asfloat(math.asuint(absoluteStart)+1u);
            start=(double)absoluteStart-epoch;duration=(double)absoluteEnd-absoluteStart;
            if(start<10||(i>0&&start<burns[i-1].End+10))return false;
            consumed=engine.Finite?duration*engine.Thrust/(engine.Isp*9.80665):0;
            if(consumed>engine.FuelMass-used)return false;
            deltaVs[i]=engine.Finite?engine.Isp*9.80665*Math.Log(mass/(mass-consumed)):
                duration*engine.Thrust/mass;
            if (!double.IsFinite(deltaVs[i]) || deltaVs[i] > settings.MaxBurnDeltaV ||
                duration > settings.MaxBurnDuration) return false;
            totalDv+=deltaVs[i];
            burns[i]=new Burn(start,duration,vector,engine.Thrust,engine.DryMass,
                engine.FuelMass,engine.Isp,engine.Finite);
            used+=consumed;mass-=consumed;
        }
        return totalDv <= settings.MaxVectorDeltaV;
    }

    private static double VectorBudget(double[] x)
    {
        int count=(x.Length-1)/4;
        double total=0;
        for(int i=0;i<count;i++) total+=Math.Sqrt(x[i*3]*x[i*3]+x[i*3+1]*x[i*3+1]+x[i*3+2]*x[i*3+2]);
        return total;
    }
    private static double PositionErrorMeters(Trial t)=>Math.Sqrt(
        t.Residual[0]*t.Residual[0]+t.Residual[1]*t.Residual[1]+t.Residual[2]*t.Residual[2]);
    private static double SpeedErrorMetersPerSecond(Trial t)=>Math.Sqrt(
        t.Residual[3]*t.Residual[3]+t.Residual[4]*t.Residual[4]+t.Residual[5]*t.Residual[5])/300;

    private static bool Orbit(TrajectoryMatchedPredictionWorkItem body, out double axis, out double period, out double eccentricity)
    {
        axis = period = eccentricity = 0;
        double r = math.length(body.StartPosition), inverse = 2 / r - math.lengthsq(body.StartVelocity) / body.Mu;
        if (!(inverse > 0) || !double.IsFinite(inverse)) return false;
        axis = 1 / inverse; period = 2 * Math.PI * Math.Sqrt(axis * axis * axis / body.Mu);
        double3 h = math.cross(body.StartPosition, body.StartVelocity);
        eccentricity = math.length(math.cross(body.StartVelocity, h) / body.Mu - body.StartPosition / r);
        return double.IsFinite(period) && double.IsFinite(eccentricity);
    }

    private static bool SolveLinear(double[,] a, out double[] answer)
    {
        int dimension = a.GetLength(0);
        answer = new double[dimension];
        for (int col = 0; col < dimension; col++)
        {
            int pivot = col; for (int i = col + 1; i < dimension; i++) if (Math.Abs(a[i, col]) > Math.Abs(a[pivot, col])) pivot = i;
            if (Math.Abs(a[pivot, col]) < 1e-12) return false;
            for (int j = col; j <= dimension; j++) { double temp = a[col, j]; a[col, j] = a[pivot, j]; a[pivot, j] = temp; }
            double divisor = a[col, col]; for (int j = col; j <= dimension; j++) a[col, j] /= divisor;
            for (int i = 0; i < dimension; i++) if (i != col) { double factor = a[i, col]; for (int j = col; j <= dimension; j++) a[i, j] -= factor * a[col, j]; }
        }
        for (int i = 0; i < dimension; i++) { answer[i] = a[i, dimension]; if (!double.IsFinite(answer[i])) return false; }
        return true;
    }
}
