Shader "Custom/MipmapBlur"
{
    Properties
    {

    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
            
            struct AttributesBlur
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
            };

            struct VaryingsBlur
            {
                half4 positionCS    : SV_POSITION;
                half2 uv            : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D_X(_TempMipTexture);
            TEXTURE2D_X(_TextureWithMips);
            #define LinearSampler sampler_LinearClamp

            uniform int _BlurLevel;
			uniform int	_CurMipLevel;
            uniform int _MipCount;

            float MipBlendWeight(float2 uv)
            {
	            const float sigma2 = _BlurLevel * _BlurLevel;
	            const float c = 2.0 * PI * sigma2;
	            const float numerator = (1 << (_CurMipLevel << 2)) * log(4.0);
	            const float denominator = c * ((1 << (_CurMipLevel << 1)) + c);
	            return clamp(numerator / denominator, 0.0, 1.0);
            }

            VaryingsBlur Vertex(AttributesBlur input)
            {
                VaryingsBlur output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformFullscreenMesh(input.positionOS.xyz);
                output.uv = UnityStereoTransformScreenSpaceTex(input.uv);
                return output;
            }

            float4 Fragment(VaryingsBlur input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            	float4 color;
				if(_CurMipLevel == _MipCount)
				{
					color = SAMPLE_TEXTURE2D_X_LOD(_TextureWithMips, LinearSampler, input.uv, _MipCount).rgba;
					return color;
				}
				float3 c1 = SAMPLE_TEXTURE2D_X_LOD(_TempMipTexture, LinearSampler, input.uv, _CurMipLevel + 1).rgb;
            	float weight = MipBlendWeight(input.uv);
				float3 c2 = SAMPLE_TEXTURE2D_X_LOD(_TextureWithMips, LinearSampler, input.uv, _CurMipLevel).rgb;
            	color = float4((1 - weight) * c1 + weight * c2, 1.0f);

            	return color;
            }
            ENDHLSL
        }
    }
}
