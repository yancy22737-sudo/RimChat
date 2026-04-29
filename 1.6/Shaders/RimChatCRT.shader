Shader "RimChat/CRT"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _Distortion ("Barrel Distortion", Range(0, 0.5)) = 0.18
        _ScanlineIntensity ("Scanline Intensity", Range(0, 0.5)) = 0.10
        _ScanlineCount ("Scanline Count", Float) = 600.0
        _VignetteIntensity ("Vignette Intensity", Range(0, 1)) = 0.35
        _GreenTint ("Green Tint", Range(0, 1)) = 0.65
        _ChromaticAberration ("Chromatic Aberration", Range(0, 5)) = 1.5
        _NoiseIntensity ("Noise Intensity", Range(0, 0.3)) = 0.05
    }

    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Distortion;
            float _ScanlineIntensity;
            float _ScanlineCount;
            float _VignetteIntensity;
            float _GreenTint;
            float _ChromaticAberration;
            float _NoiseIntensity;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            // Pseudo-random hash
            float hash(float2 p)
            {
                float h = dot(p, float2(127.1, 311.7));
                return frac(sin(h) * 43758.5453123);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float2 center = uv - 0.5;
                float dist = dot(center, center);
                float distortionFactor = 1.0 + dist * _Distortion;

                // Barrel distortion
                float2 distortedUV = center * distortionFactor + 0.5;

                // Out-of-bounds check (black border for CRT edge)
                if (distortedUV.x < 0.0 || distortedUV.x > 1.0 ||
                    distortedUV.y < 0.0 || distortedUV.y > 1.0)
                {
                    return fixed4(0, 0, 0, i.color.a);
                }

                // Chromatic aberration: offset R and B channels
                float2 caOffset = center * _ChromaticAberration / _ScreenParams.xy;
                float r = tex2D(_MainTex, distortedUV + caOffset).r;
                float g = tex2D(_MainTex, distortedUV).g;
                float b = tex2D(_MainTex, distortedUV - caOffset).b;
                float a = tex2D(_MainTex, distortedUV).a;

                // Green phosphor tint
                float3 greenPhosphor = float3(r * 0.2, g * 0.9, b * 0.3);
                float3 color = lerp(float3(r, g, b), greenPhosphor, _GreenTint);

                // Scanlines
                float screenY = distortedUV.y * _ScanlineCount;
                float scanline = 1.0 - _ScanlineIntensity * (0.5 + 0.5 * sin(screenY * 3.14159));
                color *= scanline;

                // Vignette
                float vignette = 1.0 - dist * _VignetteIntensity * 2.5;
                vignette = saturate(vignette);
                color *= vignette;

                // Noise
                float noise = hash(distortedUV * _Time.y) * _NoiseIntensity;
                color += noise;

                return fixed4(color, a * i.color.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
