// Provisional terrain shader: vertex-color landcover tint with single-directional
// lambert + fixed ambient. Replaced later in M2 by the real procedural landcover
// material (slope/altitude/class blending, CLAUDE.md §5). Written for URP; if URP
// is not yet assigned the pass fails and the Fallback renders untinted — the
// streamer also falls back to a Lit material if this shader is missing entirely.
Shader "Cirrus/TerrainVertexColor"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float4 color       : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                Light mainLight = GetMainLight();
                half lambert = saturate(dot(normalize(input.normalWS), mainLight.direction));
                half3 lighting = mainLight.color.rgb * lambert + half3(0.24h, 0.27h, 0.32h);
                return half4(input.color.rgb * lighting, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback "Diffuse"
}
