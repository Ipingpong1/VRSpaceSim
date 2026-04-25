Shader "Reach/Skybox"
{
    Properties
    {
        _StarDensity ("Star Density", Range(0, 200)) = 80
        _StarBrightness ("Star Brightness", Range(0, 5)) = 1.5
        _StarSizeBias ("Star Size Bias", Range(0.9, 0.9999)) = 0.997
        _NebulaIntensity ("Nebula Intensity", Range(0, 1)) = 0.15
        _NebulaColorA ("Nebula Color A", Color) = (0.05, 0.02, 0.15, 1)
        _NebulaColorB ("Nebula Color B", Color) = (0.1, 0.05, 0.2, 1)
        _Velocity ("Velocity (0-1, fraction of c)", Range(0, 0.99)) = 0
        _VelocityDir ("Velocity Direction", Vector) = (0, 0, 1, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _StarDensity;
            float _StarBrightness;
            float _StarSizeBias;
            float _NebulaIntensity;
            float4 _NebulaColorA;
            float4 _NebulaColorB;
            float _Velocity;
            float4 _VelocityDir;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 viewDir : TEXCOORD0;
            };

            // 3D hash function — converts a position to a pseudo-random value
            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            // 3D hash returning vec3
            float3 hash33(float3 p)
            {
                p = float3(
                    dot(p, float3(127.1, 311.7, 74.7)),
                    dot(p, float3(269.5, 183.3, 246.1)),
                    dot(p, float3(113.5, 271.9, 124.6))
                );
                return frac(sin(p) * 43758.5453123);
            }

            // Simple value noise for nebula
            float valueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = hash13(i + float3(0,0,0));
                float n100 = hash13(i + float3(1,0,0));
                float n010 = hash13(i + float3(0,1,0));
                float n110 = hash13(i + float3(1,1,0));
                float n001 = hash13(i + float3(0,0,1));
                float n101 = hash13(i + float3(1,0,1));
                float n011 = hash13(i + float3(0,1,1));
                float n111 = hash13(i + float3(1,1,1));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);

                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);

                return lerp(nxy0, nxy1, f.z);
            }

            // Multi-octave noise for richer nebula structure
            float fbm(float3 p)
            {
                float total = 0.0;
                float amplitude = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    total += valueNoise(p) * amplitude;
                    p *= 2.0;
                    amplitude *= 0.5;
                }
                return total;
            }

            // Procedural starfield: cell-based, each cell may contain a star
            float3 stars(float3 dir)
            {
                float3 p = dir * _StarDensity;
                float3 cell = floor(p);
                float3 cellPos = frac(p);

                float3 starColor = float3(0, 0, 0);

                // Check this cell and neighbors for stars
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int z = -1; z <= 1; z++)
                        {
                            float3 offset = float3(x, y, z);
                            float3 neighborCell = cell + offset;
                            float3 starOffset = hash33(neighborCell);
                            float3 starPos = offset + starOffset;
                            float dist = length(starPos - cellPos);

                            // Star existence threshold (most cells empty)
                            float starExists = step(_StarSizeBias, hash13(neighborCell + 17.0));

                            // Star brightness falls off with distance
                            float intensity = pow(saturate(1.0 - dist), 12.0) * starExists;

                            // Slight color variation per star
                            float3 tint = lerp(
                                float3(1.0, 0.95, 0.9),  // warm white
                                float3(0.85, 0.9, 1.0),  // cool blue-white
                                hash13(neighborCell + 42.0)
                            );

                            starColor += tint * intensity * _StarBrightness;
                        }
                    }
                }

                return starColor;
            }

            // Simple Doppler-style color shift based on angle to velocity direction
            float3 applyDoppler(float3 color, float3 dir, float3 velDir, float beta)
            {
                float cosTheta = dot(normalize(dir), normalize(velDir));
                // Stars ahead (cosTheta > 0): blue shift
                // Stars behind (cosTheta < 0): red shift
                float shift = cosTheta * beta;

                // Cheap color rotation: shift toward blue or red
                float3 shifted = color;
                shifted.b += shift * 0.5;       // boost blue ahead
                shifted.r -= shift * 0.3;       // reduce red ahead
                shifted.r += -shift * 0.4;      // boost red behind
                shifted.b -= -shift * 0.3;      // reduce blue behind

                // Brightness boost ahead, dim behind (relativistic beaming, simplified)
                float beaming = 1.0 + shift * 1.5;
                shifted *= max(beaming, 0.1);

                return max(shifted, 0);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.viewDir = input.positionOS.xyz; // skybox: object pos = direction
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.viewDir);

                // Base: deep black space
                float3 color = float3(0, 0, 0);

                // Nebula layer (subtle, large-scale)
                float n1 = fbm(dir * 2.0);
                float n2 = fbm(dir * 4.0 + 100.0);
                float nebulaMask = saturate(n1 * n2 * 2.0 - 0.4);
                float3 nebulaColor = lerp(_NebulaColorA.rgb, _NebulaColorB.rgb, n2);
                color += nebulaColor * nebulaMask * _NebulaIntensity;

                // Star layer
                color += stars(dir);

                // Apply relativistic Doppler shift if moving
                if (_Velocity > 0.001)
                {
                    color = applyDoppler(color, dir, _VelocityDir.xyz, _Velocity);
                }

                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}