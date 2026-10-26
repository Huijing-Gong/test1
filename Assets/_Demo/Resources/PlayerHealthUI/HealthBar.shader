Shader "Wayfarer/Player Health Artwork"
{
    Properties
    {
        [PerRendererData] _MainTex ("Original artwork", 2D) = "white" {}
        _Health ("Health", Range(0,1)) = 1
        _Trail ("Damage trail", Range(0,1)) = 1
        _Flash ("Damage flash", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
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
            float _Health, _Trail, _Flash;
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 c=tex2D(_MainTex,i.uv);
                // Only the red reservoir changes. Border and compass stay static;
                // the live number is drawn in the integrated lower plaque.
                bool reservoir = i.uv.x >= 60.0/570.0 && i.uv.x <= 473.0/570.0 &&
                    c.r > .10 && c.r > c.g*1.35 && c.r > c.b*1.2;
                if (reservoir)
                {
                    float position=saturate((i.uv.x-60.0/570.0)/(413.0/570.0));
                    if (_Health<=0 || position>_Health)
                        c.rgb=position<=_Trail && _Trail>_Health ? fixed3(.88,.65,.32) : fixed3(.075,.20,.18);
                    else c.rgb=lerp(c.rgb,fixed3(1,.84,.68),_Flash*.45);
                }
                return c*i.color;
            }
            ENDCG
        }
    }
}
