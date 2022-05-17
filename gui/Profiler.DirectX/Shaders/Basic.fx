cbuffer ConstantBuffer : register(b0)
{
    matrix View;
    matrix World;
    float4 Custom;
}

struct VS_IN
{
	float2 pos : POSITION;
	float4 col : COLOR;
	uint   id  : TEXCOORD0;
};

struct PS_IN
{
	float4 pos : SV_POSITION;
	float4 col : COLOR;
};

PS_IN VS( VS_IN input )
{
	PS_IN output = (PS_IN)0;

	float4x4 wv = mul(View, World);

	output.pos = mul(wv, float4(input.pos, 0.5f, 1.0f));
    if (Custom.x == 0 || (uint)Custom.x != input.id)
		output.col = input.col;
	else
		output.col = lerp(input.col, float4(dot(input.col.rgb,1) > 0.8*3 ? input.col.rgb*0.5 : 1, input.col.a), abs(Custom.y*2-1));
	
	return output;
}

float4 PS( PS_IN input ) : SV_Target
{
	return input.col;
}

technique10 Render
{
	pass P0
	{
		SetGeometryShader( 0 );
		SetVertexShader( CompileShader( vs_4_0, VS() ) );
		SetPixelShader( CompileShader( ps_4_0, PS() ) );
	}
}