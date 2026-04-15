using System;

namespace KSPCommunityFixes
{
    public class FIUpdateRadiation : BasePatch
    {
        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches()
        {
            AddPatch(PatchType.Override, typeof(FlightIntegrator), "UpdateRadiation");
        }

        /// <summary>
        /// UpdateRadiation method applies incoming radiation fluxes and outgoing black body radiation.
        /// The problem with original code is that ptd.expFlux and ptd.unexpFlux get mutated during every thermal integration pass.
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="ptd"></param>
        static void FlightIntegrator_UpdateRadiation_Override(FlightIntegrator __instance, PartThermalData ptd)
        {
            double cacheStefanBoltzmanConstant = __instance.cacheStefanBoltzmanConstant;

            Part part = ptd.part;
            if (!part.ShieldedFromAirstream)
            {
                double skinTemperature = part.skinTemperature;
                double skinUnexposedTemperature = part.skinUnexposedTemperature;
                double exposedArea = part.radiativeArea * part.skinExposedAreaFrac;             // Exposed is the fraction of radiative area subject to convection
                double unexposedArea = part.radiativeArea * (1.0 - part.skinExposedAreaFrac);   // Unexposed is the remainder

                // ptd.expFlux and ptd.unexpFlux are precalculated incoming fluxes, summed together from sun and body fluxes.
                // ptd.brtExposed and ptd.brtUnexposed signify incoming background radiation flux.

                if (skinUnexposedTemperature > 0.0 && unexposedArea > 0.0)
                {
                    double brtUnexposed = ptd.brtUnexposed;
                    brtUnexposed *= brtUnexposed;
                    brtUnexposed *= brtUnexposed;
                    skinUnexposedTemperature *= skinUnexposedTemperature;
                    skinUnexposedTemperature *= skinUnexposedTemperature;
                    ptd.unexpRadiationFlux += ptd.unexpFlux - (skinUnexposedTemperature - brtUnexposed) * cacheStefanBoltzmanConstant * ptd.emissScalar * unexposedArea;
                }
                if (skinTemperature > 0.0 && exposedArea > 0.0)
                {
                    double brtExposed = ptd.brtExposed;
                    brtExposed *= brtExposed;
                    brtExposed *= brtExposed;
                    skinTemperature *= skinTemperature;
                    skinTemperature *= skinTemperature;
                    ptd.radiationFlux += ptd.expFlux - (skinTemperature - brtExposed) * cacheStefanBoltzmanConstant * ptd.emissScalar * exposedArea;
                }
            }
        }
    }
}
