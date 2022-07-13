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
			SetOrbitRendererActive(__instance, false);
		}

		void OnSceneLoaded(GameScenes scn)
		{
			SetAllOrbitRenderersActive(scn == GameScenes.TRACKSTATION);
		}

		void OnMapEntered()
		{
			SetAllOrbitRenderersActive(true);
		}

		void OnMapExited()
		{
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

		static void SetOrbitRendererActive(OrbitRendererBase orbitRenderer, bool active)
		{
			orbitRenderer.enabled = active;
			orbitRenderer.LateUpdate(); // force an update to get everything in the right state

			// It seems like we should be able to disable the rest of this stuff, but weird artifacts happen

			//if (orbitRenderer.objectMO) orbitRenderer.objectMO.gameObject.SetActive(active);
			//if (orbitRenderer.DescMO) orbitRenderer.DescMO.gameObject.SetActive(active);
			//if (orbitRenderer.AscMO) orbitRenderer.AscMO.gameObject.SetActive(active);
			//if (orbitRenderer.ApMO) orbitRenderer.ApMO.gameObject.SetActive(active);
			//if (orbitRenderer.PeMO) orbitRenderer.PeMO.gameObject.SetActive(active);

			//if (orbitRenderer.objectNode) orbitRenderer.objectNode.gameObject.SetActive(active);
			//if (orbitRenderer.descNode) orbitRenderer.descNode.gameObject.SetActive(active);
			//if (orbitRenderer.ascNode) orbitRenderer.ascNode.gameObject.SetActive(active);
			//if (orbitRenderer.apNode) orbitRenderer.apNode.gameObject.SetActive(active);
			//if (orbitRenderer.peNode) orbitRenderer.peNode.gameObject.SetActive(active);
		}

	}
}
