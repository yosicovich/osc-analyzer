using System;

namespace LpgSys
{
    public class LpgSysCalcs
    {
        public LpgSysCalcs()
        {
            //
            // TODO: Add constructor logic here
            //
        }
        public const double c_vap_R = 8.314f;
        public const double c_MAir = 29;
        public const double c_CZero = 273.15f;
        public const double c_PetLambda = 14.7f;
        public const double c_LpgLambda = 15.5f;
        public const double c_LpgAdiabat = 1.12f;
        public const double c_MLpg = 0.5f * 44 + 0.5f * 58;// propane 50% butane 50%
        public const double c_NozzleFactor = 0.97f;//2
        //public const double c_NozzleFactor = 0.671f;//1
        public const double c_PetDensity = 0.75f;
        // critical relation for propane/butane is 1.72(0.581)
        public const double c_LpgCriticalFactor = 1.72f;
        public const double c_TamonaRefTemp = 40; 

        public static double CtoK(double C)
        {
            return C + 273.15f;
        }

        public static double calcAirMass(double press, double vol, double tempr)
        {
            return ((press * 1000) * vol * c_MAir / 1000) / (c_vap_R * CtoK(tempr));
        }

        public static double calcCircleArea(double d)
        {
            return d * d * Math.PI / 4;
        }

        public static double calcCircleDiameter(double S)
        {
            return Math.Sqrt(4 * S / Math.PI);
        }

        // totalVol in cc
        public static double calcPistonDispacement(double totalVol, short pistons)
        {
            return totalVol / pistons / 1000000;
        }

        public static double calcAreafromLpgMassFlow(double flow, double press, double tempr)
        {
            // return (flow * Math.Sqrt(c_vap_R * CtoK(tempr) / c_LpgAdiabat * c_MLpg) / (getAConst() * press * 1000) * c_NozzleFactor);//?
            //return flow / (getAConst() * press * 1000 * c_NozzleFactor * Math.Sqrt(c_LpgAdiabat * (c_MLpg/1000) / (c_vap_R * CtoK(tempr))));//1
            return (flow * Math.Sqrt(c_vap_R * CtoK(tempr)) / (getAConst() * press * 1000 * c_NozzleFactor));//2
        }

        public static double calcLpgMassFlowfromArea(double S, double press, double tempr)
        {
            //return  getAConst() * press * 1000 * c_NozzleFactor * S * Math.Sqrt(c_LpgAdiabat * (c_MLpg/1000) / (c_vap_R * CtoK(tempr)));//1
            return (getAConst() * press * 1000 * c_NozzleFactor * S) / Math.Sqrt(c_vap_R * CtoK(tempr));//2
        }

        private static double getAConst()
        {
            //return Math.Sqrt(Math.Pow(2 / (c_LpgAdiabat + 1), (c_LpgAdiabat + 1) / (c_LpgAdiabat - 1)));//1
            return Math.Sqrt(2 * (c_LpgAdiabat / (c_LpgAdiabat + 1)) * (Math.Pow(2 / (c_LpgAdiabat + 1), 2 / (c_LpgAdiabat - 1))) * c_MLpg / 1000);//2
        }

        public static double calcPetMassFlowfromArea(double S, double press)
        {
            return c_NozzleFactor * S * Math.Sqrt(2 * press / c_PetDensity);
        }

        public static double calcAirVol(double vol)
        {
            return vol / (1 + (c_MAir / (c_MLpg * c_LpgLambda)));
        }

        public static double calcLpgOffset(double rD, double totalVol, short pistons, double tAir, double tLpg, double manPress, double lpgPress, double lpgTime, bool fullGroup)
        {
            double rS = LpgSysCalcs.calcCircleArea(rD);
            double vAir = LpgSysCalcs.calcAirVol(LpgSysCalcs.calcPistonDispacement(totalVol, pistons));
            double mAir = LpgSysCalcs.calcAirMass(manPress, vAir, tAir);
            double mLpg = mAir / LpgSysCalcs.c_LpgLambda;
            double rFlow = LpgSysCalcs.calcLpgMassFlowfromArea(rS, lpgPress, tLpg);
            if (fullGroup)
                rFlow = rFlow * 2;
            double rTime = mLpg * 1000 / rFlow;
            rTime = lpgTime - rTime;
            return rTime;
        }
        public static short calcTamonaPress(double calibrVal, short src)
        {
            double d = src;
            d = d / 100;
            d = d * calibrVal;
            d = d / 5;
            d = d - 0.04f;
            d = d / 0.00225f;
            return (short)System.Convert.ToInt32(Math.Round(d));
        }

        public static bool calcSuggPressRange(ref double minPress, ref double maxPress, double lpgOffset, double lpgOpenT, double idleManP, double maxManP, double minInjT, double maxInjCycleT, double curT, double refT, double maxT)
        {
            maxInjCycleT = maxInjCycleT - lpgOpenT;
            minInjT = (minInjT - lpgOffset) * Math.Sqrt(CtoK(refT) / CtoK(curT));
            double injFactor = maxInjCycleT / minInjT;
            double engFactor = maxManP / idleManP;
            double pressFactor = engFactor / injFactor;
            double maxPressFactor = Math.Sqrt(CtoK(refT) / CtoK(maxT)) * pressFactor;
            maxPress = 150; // instead of any
            if (maxPressFactor > 1)
            {
                maxPress = (maxPressFactor * idleManP - maxManP) / (1 - maxPressFactor);
            }
            minPress = (LpgSysCalcs.c_LpgCriticalFactor - 1) * maxManP;
            if (minPress < 90)
                minPress = 90;
            if (maxPress > 150)
                maxPress = 150;
            return (maxPress >= minPress);
        }
        public static double adjustFactor(double factor, double fTemp, double fPress, double refTemp, double refPress)
        {
            factor = factor * fPress / refPress;
            factor = factor * Math.Sqrt(CtoK(refTemp) / CtoK(fTemp));
            return factor;
        }

        public static double adjustCycleTime(double man0, double man, double cycleTime)
        {
            return cycleTime * (man / man0);
        }
    }
}
