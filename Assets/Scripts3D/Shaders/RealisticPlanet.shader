// Реалистичная планета для встроенного рендера (работает и в WebGL):
// день/ночь с огнями городов, облака с тенями, рельеф по normal map, блики на океанах,
// атмосферное свечение по краю с закатным оттенком на терминаторе.
Shader "SpaceShooter/RealisticPlanet"
{
    Properties
    {
        _MainTex ("Day Map", 2D) = "white" {}
        _NightTex ("Night Lights", 2D) = "black" {}
        _CloudTex ("Clouds (R)", 2D) = "black" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _SpecMap ("Ocean Specular Mask (R)", 2D) = "black" {}

        _HasNight ("Has Night", Float) = 0
        _HasClouds ("Has Clouds", Float) = 0
        _HasNormal ("Has Normal", Float) = 0
        _HasSpec ("Has Specular", Float) = 0

        _NightBoost ("Night Lights Brightness", Float) = 1.6
        _CloudSpeed ("Cloud Drift Speed", Float) = 0.004
        _CloudOpacity ("Cloud Opacity", Float) = 0.9
        _NormalStrength ("Normal Strength", Float) = 1.0
        _SpecColor ("Specular Color", Color) = (1, 0.95, 0.85, 1)
        _Shininess ("Shininess", Float) = 60
        _AtmoColor ("Atmosphere Color", Color) = (0.35, 0.6, 1, 1)
        _AtmoPower ("Atmosphere Falloff", Float) = 3.5
        _AtmoStrength ("Atmosphere Strength", Float) = 1.2
        _SunsetColor ("Terminator Tint", Color) = (1, 0.45, 0.2, 1)
        _Brightness ("Day Brightness", Float) = 1.15
        _Ambient ("Ambient", Float) = 0.025
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex, _NightTex, _CloudTex, _NormalMap, _SpecMap;
            float4 _MainTex_ST;
            float _HasNight, _HasClouds, _HasNormal, _HasSpec;
            float _NightBoost, _CloudSpeed, _CloudOpacity, _NormalStrength, _Shininess;
            float4 _SpecColor, _AtmoColor, _SunsetColor;
            float _AtmoPower, _AtmoStrength, _Brightness, _Ambient;
            float4 _LightColor0;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 n : TEXCOORD2;
                float3 t : TEXCOORD3;
                float3 b : TEXCOORD4;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.n = UnityObjectToWorldNormal(v.normal);
                o.t = UnityObjectToWorldDir(v.tangent.xyz);
                o.b = cross(o.n, o.t) * v.tangent.w * unity_WorldTransformParams.w;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 ng = normalize(i.n);                       // геометрическая нормаль
                float3 L = normalize(_WorldSpaceLightPos0.xyz);   // направление на солнце
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);

                // рельеф
                float3 n = ng;
                if (_HasNormal > 0.5)
                {
                    float3 tn = UnpackNormal(tex2D(_NormalMap, i.uv));
                    tn.xy *= _NormalStrength;
                    float3x3 tbn = float3x3(normalize(i.t), normalize(i.b), ng);
                    n = normalize(mul(tn, tbn));
                }

                float ndlGeo = dot(ng, L);
                float ndl = saturate(dot(n, L));
                float dayMask = smoothstep(-0.12, 0.25, ndlGeo);  // мягкий терминатор

                float3 day = tex2D(_MainTex, i.uv).rgb * _Brightness;
                float3 col = day * (ndl * _LightColor0.rgb) + day * _Ambient;

                // тени и сами облака
                if (_HasClouds > 0.5)
                {
                    float2 cuv = i.uv + float2(_Time.y * _CloudSpeed, 0);
                    float cloud = tex2D(_CloudTex, cuv).r * _CloudOpacity;
                    float shadow = tex2D(_CloudTex, cuv + L.xz * 0.002).r * _CloudOpacity;
                    col *= 1.0 - shadow * 0.45 * dayMask;
                    float cloudLight = saturate(ndlGeo * 1.1 + 0.05);
                    col = lerp(col, cloudLight * _LightColor0.rgb + _Ambient, cloud);
                }

                // блики солнца на океанах
                if (_HasSpec > 0.5)
                {
                    float mask = tex2D(_SpecMap, i.uv).r;
                    float3 h = normalize(L + V);
                    float spec = pow(saturate(dot(n, h)), _Shininess) * mask * dayMask;
                    col += spec * _SpecColor.rgb * _LightColor0.rgb;
                }

                // огни городов на ночной стороне
                if (_HasNight > 0.5)
                {
                    float nightMask = 1.0 - smoothstep(-0.25, 0.1, ndlGeo);
                    float3 lights = tex2D(_NightTex, i.uv).rgb;
                    if (_HasClouds > 0.5)
                        lights *= 1.0 - tex2D(_CloudTex, i.uv + float2(_Time.y * _CloudSpeed, 0)).r * 0.7;
                    col += lights * nightMask * _NightBoost;
                }

                // атмосфера: свечение по краю диска, ярче на дневной стороне, закат на терминаторе
                float rim = pow(1.0 - saturate(dot(ng, V)), _AtmoPower);
                float lit = saturate(ndlGeo * 0.8 + 0.35);
                float terminator = saturate(1.0 - abs(ndlGeo) * 4.0);
                float3 atmo = lerp(_AtmoColor.rgb, _SunsetColor.rgb, terminator * 0.6);
                col += atmo * rim * lit * _AtmoStrength;
                col += _AtmoColor.rgb * dayMask * 0.04 * _AtmoStrength; // лёгкая дымка на освещённой стороне

                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
