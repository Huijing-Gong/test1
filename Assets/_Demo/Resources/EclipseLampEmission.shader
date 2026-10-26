Shader "KingOfGraphic/Eclipse Lamp Emission Mask"
{
    Properties { _MainTex("Lamp texture",2D)="white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 frag(v2f_img i):SV_Target
            {
                float3 color=tex2D(_MainTex,i.uv).rgb;
                float lightness=dot(color,float3(.2126,.7152,.0722));
                float saturation=max(color.r,max(color.g,color.b))-min(color.r,min(color.g,color.b));
                float glass=smoothstep(.72,.92,lightness)*(1-smoothstep(.08,.22,saturation));
                return float4(glass,glass,glass,1);
            }
            ENDCG
        }
    }
}
