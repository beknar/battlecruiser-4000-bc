// The player's ship paint (URP): URP/Lit, with the hull's own colour map repainted in a scheme
// (ShipPaint.cs) and a logo and lettering on each side. It takes the hull material's textures
// and settings as they are (ShipPaint copies them across), so only the colour changes:
//   - the pattern: one of 100 (10 families x 10 variants), worked out here from where the
//     point is on the ship (in the ship's own space, given per renderer as _PaintToShip), not
//     from the hull's UVs. It picks between the primary, secondary and trim colours;
//   - the paint keeps the hull texture's light and dark (its panel lines and grime): each
//     colour is multiplied by how bright the texture is there against its average;
//   - the logo (from the logo atlas) and the lettering (the font atlas), each on both sides of
//     the hull, projected across the ship (along its x axis) into the boxes ShipPaint places.
// Lit's own passes do the rest (shadows, depth, normals), from Lit's own include files. The
// paint's values aren't in the per-material buffer, so the SRP batcher leaves these out: only
// the players' ships wear it.
Shader "Carrier/ShipPaint"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _Smoothness ("Smoothness", Range(0, 1)) = 0.35
        _Metallic ("Metallic", Range(0, 1)) = 0.25
        _MetallicGlossMap ("Metallic", 2D) = "white" {}
        _SpecColor ("Specular", Color) = (0.2, 0.2, 0.2)
        _SpecGlossMap ("Specular", 2D) = "white" {}
        _BumpScale ("Normal scale", Float) = 1
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _Parallax ("Scale", Range(0.005, 0.08)) = 0.005
        _ParallaxMap ("Height Map", 2D) = "black" {}
        _OcclusionStrength ("Occlusion", Range(0, 1)) = 1
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0)
        _EmissionMap ("Emission", 2D) = "white" {}
        _DetailMask ("Detail Mask", 2D) = "white" {}
        _DetailAlbedoMapScale ("Scale", Range(0, 2)) = 1
        _DetailAlbedoMap ("Detail Albedo x2", 2D) = "linearGrey" {}
        _DetailNormalMapScale ("Scale", Range(0, 2)) = 1
        [Normal] _DetailNormalMap ("Normal Map", 2D) = "bump" {}
        [HideInInspector] _ClearCoatMask ("_ClearCoatMask", Float) = 0
        [HideInInspector] _ClearCoatSmoothness ("_ClearCoatSmoothness", Float) = 0
        [HideInInspector] _Surface ("__surface", Float) = 0
        _Cull ("Cull", Float) = 2

        // the paint (ShipPaint sets these)
        _PaintA ("Primary", Color) = (0.55, 0.58, 0.62, 1)
        _PaintB ("Secondary", Color) = (0.3, 0.33, 0.37, 1)
        _PaintC ("Trim", Color) = (0.85, 0.65, 0.25, 1)
        _PaintD ("Logo and lettering", Color) = (1, 1, 1, 1)
        _PaintPattern ("Pattern (0-99)", Float) = 0
        _PaintScale ("Pattern scale", Float) = 1
        _PaintAngle ("Pattern angle (radians)", Float) = 0
        _PaintDetail ("Hull detail kept", Range(0, 1)) = 0.85
        _PaintDecals ("Logo and lettering here", Float) = 0
        _PaintBoundsMin ("Ship bounds min", Vector) = (-30, -30, -90, 0)
        _PaintBoundsMax ("Ship bounds max", Vector) = (30, 30, 90, 0)
        _LogoRect ("Logo: z, y, half size, index", Vector) = (0, 0, 10, -1)
        _TextRect ("Lettering: z, y, height, count", Vector) = (0, 0, 6, 0)
        _Text0 ("Letters 0-3", Vector) = (0, 0, 0, 0)
        _Text1 ("Letters 4-7", Vector) = (0, 0, 0, 0)
        _Text2 ("Letters 8-11", Vector) = (0, 0, 0, 0)
        [NoScaleOffset] _LogoTex ("Logo atlas (4 x 3)", 2D) = "black" {}
        [NoScaleOffset] _FontTex ("Font atlas (8 x 5)", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "True" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            // (WebGL 2 is target 3.5: fwidth, textureGrad and dynamic loops are all there)
            #pragma target 3.5
            #pragma vertex LitPassVertex
            #pragma fragment PaintPassFragment

            #pragma shader_feature_local _NORMALMAP

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            #include "ShipPaintPatterns.hlsl"

            void PaintPassFragment(Varyings input, out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                SurfaceData surfaceData;
                InitializeStandardLitSurfaceData(input.uv, surfaceData);

                // where this is on the ship, and which way the surface faces there
                float3 posOS = TransformWorldToObject(input.positionWS);
                float3 nOS = TransformWorldToObjectNormal(input.normalWS);
                float3 p = mul(_PaintToShip, float4(posOS, 1.0)).xyz;
                float3 n = normalize(mul((float3x3)_PaintToShip, nOS));
                surfaceData.albedo = PaintAlbedo(surfaceData.albedo, input.uv, p, n);

                InputData inputData;
                InitializeInputData(input, surfaceData.normalTS, inputData);
                SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));
            #if defined(_DBUFFER)
                ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
            #endif
                InitializeBakedGIData(input, inputData);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0;
                outColor = color;
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma shader_feature_local _NORMALMAP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
