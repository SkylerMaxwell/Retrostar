// Retrostar tray icon invert shader (pixel shader 2.0).
//
// This is the only shader file: its source and its compile command live here, so there is no separate script.
// The compiled output is RetroBar\Resources\shader_invert.ps, a WPF Resource loaded by RetroBar/Utilities/InvertEffect.cs.
// To rebuild it, run this from the repository root (adjust the Windows SDK version to the one installed):
//
//   "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\fxc.exe" /T ps_2_0 /E main /FoRetroBar\Resources\shader_invert.ps shader_invert.hlsl

sampler2D input : register(s0);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 color = tex2D(input, uv);
    float alpha = color.a;

    if (!(color.r == color.g && color.g == color.b))
    {
        return color;
    }

    float3 notAlpha = color.rgb * (1.0 / max(0.0001, alpha));
    notAlpha = 1 - notAlpha;
    color.rgb = notAlpha * alpha;

    return color;
}
