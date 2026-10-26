Shader "Wayfarer/Final Figure Health Artwork"
{
    Properties
    {
        _MainTex ("Boss health artwork", 2D) = "white" {}
        _Health ("Health", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off Lighting Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            sampler2D _MainTex;
            float _Health;
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 c=tex2D(_MainTex,i.uv);
                // Recolour only the inset reservoir; the supplied gold outline
                // and arrowhead ends remain pixel-for-pixel from the PNG.
                bool inside = i.uv.x > .071 && i.uv.x < .925 &&
                    i.uv.y > .461 && i.uv.y < .545 && c.a > .1;
                bool gold = c.r > c.g*1.1 && c.g > c.b*1.4 && c.g > .35;
                if (inside && !gold)
                {
                    float position=saturate((i.uv.x-.071)/.854);
                    c.rgb=position <= _Health
                        ? lerp(fixed3(.96,.30,.25),fixed3(1,.42,.36),saturate((i.uv.y-.461)/.084))
                        : fixed3(.075,.24,.19);
                }
                // Graphics.DrawTexture's vertex tint can be translucent in IMGUI.
                // Use the PNG's own alpha so the opaque bar stays readable.
                c.a=saturate(c.a*1.8);
                return c;
            }
            ENDCG
        }
    }
}
