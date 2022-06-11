using HarmonyLib;
using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace KSPCommunityFixes
{
    class StockAlarmCustomFormatterDate : BasePatch
    {
        private static MethodInfo AppUIMember_GetValue;
        private static MethodInfo AppUIMember_SetValue;

        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            foreach (MethodInfo methodInfo in AccessTools.GetDeclaredMethods(typeof(AppUIMember)))
            {
                if (methodInfo.Name == "GetValue" && !methodInfo.IsGenericMethod)
                {
                    AppUIMember_GetValue = methodInfo;
                }

                if (methodInfo.Name == "SetValue")
                {
                    AppUIMember_SetValue = methodInfo;
                }
            }

            if (AppUIMember_GetValue == null)
                throw new Exception("AppUIMember.GetValue() : method not found");

            if (AppUIMember_SetValue == null)
                throw new Exception("AppUIMember.SetValue() : method not found");

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(AppUIMemberDateTime), "OnRefreshUI"),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(AppUIMemberDateTime), "InputEdited"),
                this));
        }

        static bool AppUIMemberDateTime_OnRefreshUI_Prefix(
            AppUIMemberDateTime __instance,
            AppUIMemberDateTime.DateTimeModes ___datetimeMode,
            ref int ___year,
            ref int ___day,
            ref int ___hour,
            ref int ___min,
            ref int ___sec)
        {
            double value = (double)AppUIMember_GetValue.Invoke(__instance, null);

            if (___datetimeMode == AppUIMemberDateTime.DateTimeModes.date)
                GetDateComponents(value, true, out ___year, out ___day, out ___hour, out ___min, out ___sec);
            else
                GetDateComponents(value, false, out ___year, out ___day, out ___hour, out ___min, out ___sec);

            __instance.yInput.text = ___year.ToString("0");
            __instance.dInput.text = ___day.ToString("0");
            __instance.hInput.text = ___hour.ToString("00");
            __instance.mInput.text = ___min.ToString("00");
            __instance.sInput.text = ___sec.ToString("00");
            __instance.utInput.text = string.Format("0", value);
            return false;
        }

        static void GetDateComponents(double time, bool asDate, out int years, out int days, out int hours, out int minutes, out int seconds)
        {
            IDateTimeFormatter formatter = KSPUtil.dateTimeFormatter;
            int year_len = formatter.Year;
            int day_len = formatter.Day;
            years = (int)(time / (double)year_len);
            time -= ((double)years * (double)year_len);
            seconds = (int)time;
            minutes = seconds / 60 % 60;
            hours = seconds / 3600 % (day_len / 3600);
            days = seconds / day_len;
            seconds %= 60;
            if (asDate)
            {
                days += 1;
                years += 1;
            }
        }

        static bool AppUIMemberDateTime_InputEdited_Prefix(
            AppUIMemberDateTime __instance,
            AppUIMemberDateTime.DateTimeModes ___datetimeMode,
            AppUIMemberDateTime.DisplayModes ___displayMode)
        {
            double ut;
            switch (___displayMode)
            {
                default:
                    if (string.IsNullOrEmpty(__instance.utInput.text))
                    {
                        __instance.utInput.text = "0";
                    }
                    ut = ParseDouble(__instance.utInput.text);
                    break;
                case AppUIMemberDateTime.DisplayModes.datetime:
                    {
                        if (string.IsNullOrEmpty(__instance.yInput.text))
                        {
                            __instance.yInput.text = "0";
                        }
                        if (string.IsNullOrEmpty(__instance.dInput.text))
                        {
                            __instance.dInput.text = "0";
                        }
                        if (string.IsNullOrEmpty(__instance.hInput.text))
                        {
                            __instance.hInput.text = "0";
                        }
                        if (string.IsNullOrEmpty(__instance.mInput.text))
                        {
                            __instance.mInput.text = "0";
                        }
                        if (string.IsNullOrEmpty(__instance.sInput.text))
                        {
                            __instance.sInput.text = "0";
                        }
                        double years = ParseDouble(__instance.yInput.text);
                        double days = ParseDouble(__instance.dInput.text);
                        if (___datetimeMode == AppUIMemberDateTime.DateTimeModes.date)
                        {
                            years = Math.Max(years, 1.0);
                            days = Math.Max(days, 1.0);
                        }

                        IDateTimeFormatter formatter = KSPUtil.dateTimeFormatter;
                        ut = ParseDouble(__instance.sInput.text);
                        ut += ParseDouble(__instance.mInput.text) * (double)formatter.Minute;
                        ut += ParseDouble(__instance.hInput.text) * (double)formatter.Hour;
                        ut += days * (double)formatter.Day;
                        ut += years * (double)formatter.Year;
                        if (___datetimeMode == AppUIMemberDateTime.DateTimeModes.date)
                        {
                            ut -= (double)(formatter.Year + formatter.Day);
                        }
                        break;
                    }
            }
            ut = Math.Max(ut, 0.0);
            AppUIMember_SetValue.Invoke(__instance, new object[] { ut });
            __instance.RefreshUI();
            return false;
        }

        static double ParseDouble(string value)
        {
            if (double.TryParse(value, out double result))
                return result;

            return 0.0;
        }
    }
}
