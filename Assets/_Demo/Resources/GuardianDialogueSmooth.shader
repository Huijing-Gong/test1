Shader "UI/Guardian Dialogue Smooth"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 premulSample(float2 uv)
            {
                fixed4 sampleColor = tex2D(_MainTex, uv);
                sampleColor.rgb *= sampleColor.a;
                return sampleColor;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                // A narrow five-tap filter softens the GIF's stair-stepped alpha
                // outline without blurring the entire panel or the live text.
                float2 d = _MainTex_TexelSize.xy * 0.45;
                fixed4 color = premulSample(input.uv) * 4.0;
                color += premulSample(input.uv + float2(d.x, 0));
                color += premulSample(input.uv - float2(d.x, 0));
                color += premulSample(input.uv + float2(0, d.y));
                color += premulSample(input.uv - float2(0, d.y));
                color *= 0.125;
                color.rgb *= input.color.rgb * input.color.a;
                color.a *= input.color.a;
                return color;
            }
            ENDCG
        }
    }
}
