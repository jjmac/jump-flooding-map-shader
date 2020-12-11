Shader "PostfallEditor/MapColorOverlay"
{
    Properties
    {
        _NoiseMap("Noise Map", 2D) = "black" {}
        _NoiseMultiplier("UV Distortion Strength", Range(0, 0.01)) = 0.005
        _NoiseFactor("UV Distortion Factor", Range(0, 100)) = 10
        _DistanceTransformMap ("Distance Field Map", 2D) = "black" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 5)) = 1.0
        _ColorDisplayMap ("Color Display Map", 2D) = "black" {}
        _LookupMap ("Lookup Map", 2D) = "black" {}
        _BaseOpacity ("Base Opacity", Range(0,1)) = 0.2
        _EdgeOpacity ("Edge Opacity", Range(0,1)) = 1.0
        _BorderPower ("Border Power", Range(0, 1)) = 0.25
        _BorderReserveEdge ("Border Reserve Edge", Range(0, 1)) = 1.0
        _BorderReserveFade ("Border Reserve Fade", Range(0, 1)) = 1.0
        _HardEdgeWidth ("Hard Edge Width", Range(1, 0)) = 0.9
        _EdgeBlur("Edge Blur", Range(0, 1)) = 0.5
        _EdgeBlurRadius("Edge Blur Radius", Int) = 1
        _ColBrightness("Base Brightness", Range(0,1)) = 1
        _ColSaturation("Base Saturation", Range(0,1)) = 1
        _ColContrast("Base Contrast", Range(0, 1)) = 1
        _EdgeBrightness("Edge Brightness", Range(0,5)) = 1
        _EdgeSaturation("Edge Saturation", Range(0,5)) = 1
        _EdgeContrast("Edge Contrast", Range(0, 5)) = 1
        _ColGain ("Gain", Range(0,2)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 200
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        CGPROGRAM
        // Physically based Standard lighting model, and enable shadows on all light types
        #pragma surface surf Standard fullforwardshadows alpha

        // Use shader model 3.0 target, to get nicer looking lighting
        #pragma target 3.0

        sampler2D _NoiseMap;
        sampler2D _LookupMap;
        sampler2D _ColorDisplayMap;
        sampler2D _DistanceTransformMap;
        sampler2D _NormalMap;
        half _NormalStrength;

        float4 _LookupMap_TexelSize;

        struct Input
        {
            float2 uv_LookupMap;
            float2 uv_ColorMap;
        };

        half _BaseOpacity;
        half _EdgeOpacity;
        half _BorderPower;
        half _BorderReserveEdge;
        half _BorderReserveFade;
        half _HighEndRolloffStrength;
        half _HardEdgeWidth;
        half _NoiseMultiplier;
        half _NoiseFactor;

        half _EdgeBlur;
        int _EdgeBlurRadius;

        half _EdgeBrightness;
        half _EdgeSaturation;
        half _EdgeContrast;

        half _ColBrightness;
        half _ColSaturation;
        half _ColContrast;
        half _ColGain;

        // Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
        // See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
        // #pragma instancing_options assumeuniformscaling
        UNITY_INSTANCING_BUFFER_START(Props)
            // put more per-instance properties here
        UNITY_INSTANCING_BUFFER_END(Props)

        float map(float OldMin, float OldMax, float NewMin, float NewMax, float OldValue){
        
            float OldRange = (OldMax - OldMin);
            float NewRange = (NewMax - NewMin);
            float NewValue = (((OldValue - OldMin) * NewRange) / OldRange) + NewMin;
        
            return(NewValue);
        }

        float invLerp(float from, float to, float value) {
            return (value - from) / (to - from);
        }

        float4 invLerp(float4 from, float4 to, float4 value) {
            return (value - from) / (to - from);
        }

        float3 BrightnessSaturationContrast(float3 color, float brightness, float saturation, float contrast)
        {
            // adjust these values to adjust R, G, B colors separately
            float avgLumR = 0.5;
            float avgLumG = 0.5;
            float avgLumB = 0.5;

            // luminance coefficient for getting luminance from the image
            float3 luminanceCoeff = float3(0.2125, 0.7154, 0.0721);

            // Brightness calculation
            float3 avgLum = float3(avgLumR, avgLumG, avgLumB);
            float3 brightnessColor = color * brightness;
            float intensityf = dot(brightnessColor, luminanceCoeff);
            float3 intensity = float3(intensityf, intensityf, intensityf);

            // Saturation calculation
            float3 saturationColor = lerp(intensity, brightnessColor, saturation);

            // Contrast calculation
            float3 contrastColor = lerp(avgLum, saturationColor, contrast);

            return contrastColor;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float4 noiseSampleA = tex2D(_NoiseMap, (IN.uv_LookupMap * _NoiseFactor));
            float4 noiseSampleB = tex2D(_NoiseMap, (IN.uv_LookupMap * _NoiseFactor) + 0.1);

            float4 randomValRGBA0 = noiseSampleA - 0.5;
            float randomVal0 = randomValRGBA0.x * _NoiseMultiplier * 0.5;

            float4 randomValRGBA1 = noiseSampleB - 0.5;
            float randomVal1 = randomValRGBA1.y * _NoiseMultiplier;

            float2 distortedUV = float2(IN.uv_LookupMap.x + randomVal0, IN.uv_LookupMap.y + randomVal1);
            
            //fixed2 lookup = tex2D (_LookupMap, distortedUV).xy + (0.5 * _LookupMap_TexelSize);
            //float4 col = tex2D(_ColorDisplayMap, lookup);

            float4 c = float4(0,0,0,0);

            float edgeBlurPow = _EdgeBlur * _EdgeBlur;

            int numNeighbors = _EdgeBlurRadius + _EdgeBlurRadius + 1;
            numNeighbors *= numNeighbors;

            for (int y = -_EdgeBlurRadius; y <= _EdgeBlurRadius; y++) 
            {
                for (int x = -_EdgeBlurRadius; x <= _EdgeBlurRadius; x++)
                {
                    float2 nUv = float2(distortedUV.x + (x * _LookupMap_TexelSize.x * edgeBlurPow), distortedUV.y + (y * _LookupMap_TexelSize.y * edgeBlurPow));
                    fixed2 nLookup = tex2D(_LookupMap, nUv).xy;
                    float4 nC = tex2D(_ColorDisplayMap, nLookup);

                    c += nC;
                }
            }

            c /= numNeighbors;

            float4 d = tex2D(_DistanceTransformMap, IN.uv_LookupMap);

            float distPower = pow(d.a, _BorderPower);
            float reserve = lerp(distPower, d.a, smoothstep(_BorderReserveEdge, _BorderReserveEdge - (_BorderReserveEdge * _BorderReserveFade), d.a));
            float a = lerp(_BaseOpacity, 1, 1 - reserve) * _ColGain;

            //float edgeOpacity = lerp(0, _BaseOpacity, _EdgeOpacity);
            float hardEdge = step(d.a, _HardEdgeWidth);
            a = lerp(a, _EdgeOpacity, hardEdge);

            float4 cEdge = float4(BrightnessSaturationContrast(c.rgb, _EdgeBrightness, _EdgeSaturation, _EdgeContrast), 0);
            c = lerp(c, cEdge, hardEdge);

            fixed3 normal = UnpackNormal(tex2D(_NormalMap, IN.uv_LookupMap));
            normal.xy *= _NormalStrength;

            float3 outputCol = BrightnessSaturationContrast(c.rgb, _ColBrightness, _ColSaturation, _ColContrast);
            float outputAlpha = a;
    
            o.Albedo = outputCol;
            o.Normal = normalize(normal);
            o.Emission = 0;       
            o.Metallic = 0;
            o.Smoothness = 0;
            o.Alpha = outputAlpha;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
