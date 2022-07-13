using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KSPCommunityFixes.Performance
{
	class OrbitRendererSpeedBoost : BasePatch
	{
		protected override void ApplyPatches(List<PatchInfo> patches)
		{
			patches.Add(
				new PatchInfo(PatchMethodType.Postfix,
				AccessTools.Method(typeof(OrbitRendererBase), nameof(OrbitRendererBase.Start)),
				this));

			GameEvents.onLevelWasLoaded.Add(OnSceneLoaded);
			GameEvents.OnMapEntered.Add(OnMapEntered);
			GameEvents.OnMapExited.Add(OnMapExited);
		}

		static void OrbitRendererBase_Start_Postfix(OrbitRendererBase __instance)
		{
			// Debug.Log($"[OrbitRendererSpeedBoost]: {__instance.objName} Start");
			
			SetOrbitRendererActive(__instance, false);
		}

		void OnSceneLoaded(GameScenes scn)
		{
			// Debug.Log($"[OrbitRendererSpeedBoost] OnSceneLoaded {scn}");
			SetAllOrbitRenderersActive(scn == GameScenes.TRACKSTATION || scn == GameScenes.SPACECENTER);
		}

		void OnMapEntered()
		{
			// Debug.Log($"[OrbitRendererSpeedBoost] OnMapEntered");
			SetAllOrbitRenderersActive(true);
		}

		void OnMapExited()
		{
			// Debug.Log($"[OrbitRendererSpeedBoost] OnMapExited");
			SetAllOrbitRenderersActive(false);
		}

		static void SetAllOrbitRenderersActive(bool active)
		{
			foreach (var orbit in Planetarium.Orbits)
			{
				if (orbit.Renderer)
				{
					SetOrbitRendererActive(orbit.Renderer, active);
				}
			}
		}

		static void SetMapObjectActive(MapObject mapObject, bool active)
		{
			if (mapObject)
			{
				// Debug.Log($"[OrbitRendererSpeedBoost] SetMapObjectActive({active}) for {mapObject.name} (was {mapObject.enabled})");
				mapObject.enabled = active;
				mapObject.LateUpdate();
			}
		}

		static void SetOrbitRendererActive(OrbitRendererBase orbitRenderer, bool active)
		{
			// Debug.Log($"[OrbitRendererSpeedBoost] SetOrbitRendererActive({active}) for {orbitRenderer.objName} (was {orbitRenderer.enabled}");

			orbitRenderer.enabled = active;
			orbitRenderer.LateUpdate(); // force an update to get everything in the right state

			SetMapObjectActive(orbitRenderer.objectMO, active);
			SetMapObjectActive(orbitRenderer.DescMO, active);
			SetMapObjectActive(orbitRenderer.AscMO, active);
			SetMapObjectActive(orbitRenderer.ApMO, active);
			SetMapObjectActive(orbitRenderer.PeMO, active);
		}

	}
}
