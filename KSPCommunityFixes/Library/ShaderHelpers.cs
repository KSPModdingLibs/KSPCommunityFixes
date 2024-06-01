using PartToolsLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KSPCommunityFixes.Library
{
    internal class ShaderHelpers
    {
        private static bool shadersPopulated = false;

        private static Shader _kspDiffuse;
        private static Shader _kspSpecular;
        private static Shader _kspBumped;
        private static Shader _kspBumpedSpecular;
        private static Shader _kspEmissiveDiffuse;
        private static Shader _kspEmissiveSpecular;
        private static Shader _kspEmissiveBumpedSpecular;
        private static Shader _kspAlphaCutoff;
        private static Shader _kspAlphaCutoffBumped;
        private static Shader _kspAlphaTranslucent;
        private static Shader _kspAlphaTranslucentSpecular;
        private static Shader _kspAlphaUnlitTransparent;
        private static Shader _kspUnlit;
        private static Shader _kspParticlesAlphaBlended;
        private static Shader _kspParticulesAdditive;
        private static Shader _kspBumpedSpecularMapped;

        private static void PopulateShaders()
        {
            PopulateShader("KSP/Diffuse", ref _kspDiffuse);
            PopulateShader("KSP/Specular", ref _kspSpecular);
            PopulateShader("KSP/Bumped", ref _kspBumped);
            PopulateShader("KSP/Bumped Specular", ref _kspBumpedSpecular);
            PopulateShader("KSP/Emissive/Diffuse", ref _kspEmissiveDiffuse);
            PopulateShader("KSP/Emissive/Specular", ref _kspEmissiveSpecular);
            PopulateShader("KSP/Emissive/Bumped Specular", ref _kspEmissiveBumpedSpecular);
            PopulateShader("KSP/Alpha/Cutoff", ref _kspAlphaCutoff);
            PopulateShader("KSP/Alpha/Cutoff Bumped", ref _kspAlphaCutoffBumped);
            PopulateShader("KSP/Alpha/Translucent", ref _kspAlphaTranslucent);
            PopulateShader("KSP/Alpha/Translucent Specular", ref _kspAlphaTranslucentSpecular);
            PopulateShader("KSP/Alpha/Unlit Transparent", ref _kspAlphaUnlitTransparent);
            PopulateShader("KSP/Unlit", ref _kspUnlit);
            PopulateShader("KSP/Particles/Alpha Blended", ref _kspParticlesAlphaBlended);
            PopulateShader("KSP/Particles/Additive", ref _kspParticulesAdditive);
            PopulateShader("KSP/Bumped Specular (Mapped)", ref _kspBumpedSpecularMapped);
            shadersPopulated = true;
        }

        private static void PopulateShader(string shaderName, ref Shader shaderRef)
        {
            shaderRef = Shader.Find(shaderName);
            shaders[shaderName] = shaderRef;
        }

        private static Dictionary<string, Shader> shaders = new Dictionary<string, Shader>();

        public static Shader GetShader(ShaderType shaderType)
        {
            if (!shadersPopulated)
                PopulateShaders();

            switch (shaderType)
            {
                case ShaderType.Diffuse:
                default:
                    return _kspDiffuse;
                case ShaderType.Specular:
                    return _kspSpecular;
                case ShaderType.Bumped:
                    return _kspBumped;
                case ShaderType.BumpedSpecular:
                    return _kspBumpedSpecular;
                case ShaderType.Emissive:
                    return _kspEmissiveDiffuse;
                case ShaderType.EmissiveSpecular:
                    return _kspEmissiveSpecular;
                case ShaderType.EmissiveBumpedSpecular:
                    return _kspEmissiveBumpedSpecular;
                case ShaderType.AlphaCutout:
                    return _kspAlphaCutoff;
                case ShaderType.AlphaCutoutBumped:
                    return _kspAlphaCutoffBumped;
                case ShaderType.Alpha:
                    return _kspAlphaTranslucent;
                case ShaderType.AlphaSpecular:
                    return _kspAlphaTranslucentSpecular;
                case ShaderType.AlphaUnlit:
                    return _kspAlphaUnlitTransparent;
                case ShaderType.Unlit:
                    return _kspUnlit;
                case ShaderType.ParticleAlpha:
                    return _kspParticlesAlphaBlended;
                case ShaderType.ParticleAdditive:
                    return _kspParticulesAdditive;
                case ShaderType.BumpedSpecularMap:
                    return _kspBumpedSpecularMapped;
            }
        }

        public static Shader GetShader(string shaderName)
        {
            if (!shadersPopulated)
                PopulateShaders();

            if (!shaders.TryGetValue(shaderName, out Shader shader))
            {
                shader = Shader.Find(shaderName);
                if (shader.IsNotNullRef())
                    shaders.Add(shaderName, shader);
            }

            return shader;
        }

        public static readonly int MainTexPropId = Shader.PropertyToID("_MainTex");
        public static readonly int BumpMapPropId = Shader.PropertyToID("_BumpMap");
        public static readonly int EmissivePropId = Shader.PropertyToID("_Emissive");
        public static readonly int SpecMapPropId = Shader.PropertyToID("_SpecMap");

        public static readonly int SpecColorPropId = Shader.PropertyToID("_SpecColor");
        public static readonly int ShininessPropId = Shader.PropertyToID("_Shininess");
        public static readonly int CutoffPropId = Shader.PropertyToID("_Cutoff");
        public static readonly int GlossPropId = Shader.PropertyToID("_Gloss");
        public static readonly int ColorPropId = Shader.PropertyToID("_Color");
        public static readonly int SpecTintPropId = Shader.PropertyToID("_SpecTint");
        public static readonly int InvFadePropId = Shader.PropertyToID("_InvFade");
    }
}
