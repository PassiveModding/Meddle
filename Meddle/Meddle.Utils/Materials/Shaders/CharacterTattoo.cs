using System.Numerics;
using Meddle.Utils.Export;
using Meddle.Utils.Models;
using SharpGLTF.Materials;
/*
namespace Meddle.Utils.Materials.Shaders;

public static class CharacterTattoo
{
    // Material and customization data structures
    struct MaterialParameter
    {
        public Vector4[] Values; // Array of 19 float4 values
    }
    
    struct CharacterCustomization
    {
        public Vector4 SkinColor;
        public Vector4 SkinFresnelValue0;
        public Vector4 LipColor;
        public Vector4 MainColor;
        public Vector3 HairFresnelValue0;
        public Vector4 MeshColor;
        public Vector4 LeftColor;
        public Vector4 RightColor;
        public Vector3 OptionColor;
    }
    
    struct DecalColor
    {
        public Vector4 Color;
    }

    public static Vector3 xyz(this Vector4 v) => new Vector3(v.X, v.Y, v.Z);
    public static Vector3 xyz(this Vector3 v) => new Vector3(v.X, v.Y, v.Z);
    public static Vector4 sqrt(this Vector4 v) => new Vector4(System.MathF.Sqrt(v.X), System.MathF.Sqrt(v.Y), System.MathF.Sqrt(v.Z), System.MathF.Sqrt(v.W));
    public static Vector3 sqrt(this Vector3 v) => new Vector3(System.MathF.Sqrt(v.X), System.MathF.Sqrt(v.Y), System.MathF.Sqrt(v.Z));
    public static float saturate(float v) => System.MathF.Max(0.0f, System.MathF.Min(1.0f, v));
    public static float clamp(float v, float min, float max) => System.MathF.Max(min, System.MathF.Min(max, v));
    public static Vector4 mix(Vector4 a, Vector4 b, float t) => a * (1.0f - t) + b * t;
    public static Vector3 mix(Vector3 a, Vector3 b, float t) => a * (1.0f - t) + b * t;
    public static Vector4 mix(Vector3 a, Vector4 b, float t) => new Vector4(a, 1.0f) * (1.0f - t) + b * t;
    public static float dot(Vector3 a, Vector3 b) => Vector3.Dot(a, b);

    static MaterialBuilder BuildCharacterTattoo(
        Material material, string name, MaterialUtility.MaterialParameters parameters)
    {
        var normal = material.GetTexture(TextureUsage.g_SamplerNormal).ToTexture();
        var baseTexture = new SKTexture(normal.Width, normal.Height);
        for (var x = 0; x < baseTexture.Width; x++)
        for (var y = 0; y < baseTexture.Height; y++)
        {
            var normalSample = normal[x, y].ToVector4();
            var meshColor = new Vector4(parameters.SkinColor, normalSample.W);
            var decalColor = parameters.DecalColor ?? new Vector4(1,1,1, normalSample.W);
            
            var finalColor = meshColor.xyz() * decalColor.xyz();
            baseTexture[x, y] = new Vector4(finalColor, normalSample.W).ToSkColor();
        }
        
        var output = new MaterialBuilder(name)
            .WithBaseColor(MaterialUtility.BuildImage(baseTexture, name, "diffuse"))
            .WithNormal(MaterialUtility.BuildImage(normal, name, "normal"));
        
        var doubleSided = (material.ShaderFlags & 0x1) == 0;
        output.WithDoubleSide(doubleSided);
        
        return output;
    }

    // Shader program function with input and output vectors
    static Vector4 FragmentShader1(Vector4 position, Vector4 color, Vector2 uv0,
                           MaterialParameter materialParams, CharacterCustomization customization)
    {
        // Sample normal map using uv0 and sampler
        Vector4 normalSample = SampleTexture2D(normalSampler, uv0, materialParams.Values[13]);

        // Calculate lighting based on normal sample and other factors
        float lightingFactor = saturate(dot(normalSample.xyz(), -position.xyz()) + customization.OptionColor.X);
        lightingFactor = clamp(lightingFactor, -0.001f, 0.000f); // Discard fragment if outside range

        // Calculate final color based on lighting, material, and customization
        Vector3 finalColor = -position.xyz() * customization.MeshColor.xyz()
                             + lightingFactor * (position.xyz() * materialParams.Values[0].xyz());
        //finalColor.xyz = sqrt(finalColor.xyz()); // Assuming some lighting calculation
        finalColor = finalColor.sqrt();

        // Combine final color with additional color and set alpha from w component of input color
        Vector4 outputColor = new Vector4(finalColor, color.W);
        return outputColor;
    }

    // Shader program function with input and output vectors
    static Vector4 FragmentShader2(Vector4 position, Vector4 color, Vector2 uv0, Vector3 uv2, 
                           MaterialParameter materialParams, DecalColor decalColor, CharacterCustomization customization)
    {
      // Sample normal map using uv0 and sampler
      Vector4 normalSample = SampleTexture2D(normalSampler, uv0, materialParams.Values[13]);

      // Calculate lighting based on normal sample and other factors
      float lightingFactor = saturate(dot(normalSample.xyz(), -position.xyz()) + customization.OptionColor.X);
      lightingFactor = clamp(lightingFactor, -0.001f, 0.000f); // Discard fragment if outside range

      // Sample decal texture with weight and decal color
      Vector4 decalSample = SampleTexture2D(decalSampler, uv2, decalColor.Color) * customization.DecalWeight;

      // Calculate final color based on lighting, material, customization, and decal
      Vector3 finalColor = -position.xyz() * customization.MeshColor.xyz()
                          + lightingFactor * (position.xyz() * materialParams.Values[0].xyz());
      finalColor = sqrt(finalColor.xyz()); // Assuming some lighting calculation

      // Combine final color with decal, additional color, and set alpha from w component of input color
      Vector4 blendedColor = mix(finalColor, decalSample, decalSample.W);
      Vector4 outputColor = blendedColor with { W = color.W };
      return outputColor;
    }


    // Shader program function with input and output vectors
    static Vector4 FragmentShader3(Vector2 screenUV, Vector4 color, Vector2 uv0, Vector3 uv1,
                                  MaterialParameter materialParams, CharacterCustomization customization)
    {
      // Sample dither texture with screenUV and discard if below threshold
      float ditherValue = SampleTexture2D(ditherSampler, screenUV).x;
      if (ditherValue < 0.0f)
      {
        discard;
      }

      // Sample normal map using uv0 and sampler
      Vector4 normalSample = SampleTexture2D(normalSampler, uv0, materialParams.Values[13]);

      // Calculate lighting based on normal sample and other factors
      float lightingFactor = saturate(dot(normalSample.xyz(), -uv1) + customization.OptionColor.X);
      lightingFactor = clamp(lightingFactor, -0.001f, 0.000f); // Discard fragment if outside range

      // Calculate final color based on lighting, material, and customization
      Vector3 finalColor = -uv1 * customization.MeshColor.xyz()
                          + lightingFactor * (uv1 * materialParams.Values[0].xyz());
      finalColor = sqrt(finalColor.xyz()); // Assuming some lighting calculation

      // Combine final color, additional color, and set alpha from w component of input color
      Vector4 outputColor = new Vector4(finalColor, color.W);
      return outputColor;
    }

    // Shader program function with input and output vectors
    static Vector4 FragmentShader4(Vector2 screenUV, Vector4 color, Vector4 uv0, Vector3 uv1, Vector3 uv2, 
                                  MaterialParameter materialParams, DecalColor decalColor, CharacterCustomization customization)
    {
      // Sample dither texture with screenUV and discard if below threshold
      float ditherValue = SampleTexture2D(ditherSampler, screenUV).x;
      if (ditherValue < 0.0f)
      {
        discard;
      }

      // Sample normal map using uv0 and sampler
      Vector4 normalSample = SampleTexture2D(normalSampler, uv0, materialParams.Values[13]);

      // Calculate lighting based on normal sample and other factors
      float lightingFactor = saturate(dot(normalSample.xyz(), -uv1) + customization.OptionColor.X);
      lightingFactor = clamp(lightingFactor, -0.001f, 0.000f); // Discard fragment if outside range

      // Sample decal texture with weight and decal color
      Vector4 decalSample = SampleTexture2D(decalSampler, uv2, decalColor.Color) * customization.DecalWeight;

      // Calculate final color based on lighting, material, customization, and decal
      Vector3 finalColor = -uv1 * customization.MeshColor.xyz()
                          + lightingFactor * (uv1 * materialParams.Values[0].xyz());
      finalColor = sqrt(finalColor.xyz()); // Assuming some lighting calculation
 
      // Combine final color with decal, additional color, and set alpha from w component of input color
      Vector3 blendedColor = mix(finalColor, decalSample.xyz(), decalSample.W);
      Vector4 outputColor = new Vector4(blendedColor, color.W);
      return outputColor;
    }
}
*/
