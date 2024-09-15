using System.Numerics;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using SharpGLTF.Materials;
using CustomizeParameter = Meddle.Utils.Export.CustomizeParameter;

namespace Meddle.Utils.Materials;

public static partial class MaterialUtility
{
    public static MaterialBuilder BuildSkin(Material material, string name, CustomizeParameter parameters, CustomizeData data, (TexFile tileNormArray, TexFile tileOrbArray)? tile = null)
    {
        SkinType skinType = SkinType.Face;
        if (material.ShaderKeys.Any(x => x.Category == (uint)ShaderCategory.CategorySkinType))
        {
            var key = material.ShaderKeys.First(x => x.Category == (uint)ShaderCategory.CategorySkinType);
            skinType = (SkinType)key.Value;
        }
        
        SKTexture normal = material.GetTexture(TextureUsage.g_SamplerNormal).ToTexture();
        SKTexture mask = material.GetTexture(TextureUsage.g_SamplerMask).ToTexture((normal.Width, normal.Height)); // spec, roughness, thickness
        SKTexture diffuse = material.GetTexture(TextureUsage.g_SamplerDiffuse).ToTexture((normal.Width, normal.Height));

        if (tile != null)
        {
            var tileIndex = (int)material.GetConstantOrDefault(MaterialConstant.g_TileIndex, 0);
            var tileScale = material.GetConstantOrDefault(MaterialConstant.g_TileScale, new Vector2(16.0f, 16.0f));
            var tileAlpha = material.GetConstantOrDefault(MaterialConstant.g_TileAlpha, 1.0f);
            var tileNorm = ImageUtils.GetTexData(tile.Value.tileNormArray, tileIndex, 0, 0).ToTexture();
            var timeOrb = ImageUtils.GetTexData(tile.Value.tileOrbArray, tileIndex, 0, 0).ToTexture();
        }
        
        // PART_BODY = no additional color
        // PART_FACE/default = lip color
        // PART_HRO = hairColor blend into hair highlight color

        var skinColor = parameters.SkinColor;
        var lipColor = parameters.LipColor;
        var hairColor = parameters.MainColor;
        var highlightColor = parameters.MeshColor;
        var diffuseColor = material.GetConstantOrDefault(MaterialConstant.g_DiffuseColor, Vector3.One);
        var alphaThreshold = material.GetConstantOrDefault(MaterialConstant.g_AlphaThreshold, 0.0f);
        var lipRoughnessScale = material.GetConstantOrDefault(MaterialConstant.g_LipRoughnessScale, 0.7f);
        var alphaMultiplier = alphaThreshold != 0 ? (float)(1.0f / alphaThreshold) : 1.0f;
        
        
        var diffuseTexture = new SKTexture(diffuse.Width, diffuse.Height);
        var normalTexture = new SKTexture(normal.Width, normal.Height);
        var metallicRoughness = new SKTexture(normal.Width, normal.Height);
        Parallel.For(0, normal.Width, x =>
        {
            for (int y = 0; y < normal.Height; y++)
            {
                var normalPixel = normal[x, y].ToVector4();
                var maskPixel = mask[x, y].ToVector4();
                var diffusePixel = diffuse[x, y].ToVector4();

                /*var texCoord = new Vector2((float)x / normal.Width, (float)y / normal.Height);
                var tileNormPixel = tileNorm.SampleWrap(texCoord * tileScale).ToVector4();
                var tileOrbPixel = timeOrb.SampleWrap(texCoord * tileScale).ToVector4();
                
                
                // lerp between normal and tile normal
                var nX = float.Lerp(tileNormPixel.X, normalPixel.X, normalPixel.X);
                var nY = float.Lerp(tileNormPixel.Y, normalPixel.Y, normalPixel.Y);
                var nZ = float.Lerp(tileNormPixel.Z, normalPixel.Z, normalPixel.Z);
                normalPixel = new Vector4(nX, nY, nZ, normalPixel.W);*/
                
                var diffuseAlpha = diffusePixel.W;
                var skinInfluence = normalPixel.Z;
                
                var sColor = Vector3.Lerp(diffuseColor, skinColor, skinInfluence);
                diffusePixel *= new Vector4(sColor, 1.0f);
                
                if (skinType == SkinType.Hrothgar)
                {
                    var hair = hairColor;
                    if (data.Highlights)
                    {
                        hair = Vector3.Lerp(hairColor, highlightColor, maskPixel.W);
                    }

                    // tt arbitrary darkening instead of using flow map
                    hair *= 0.4f;

                    var delta = Math.Min(Math.Max(normalPixel.W - skinInfluence, 0), 1.0f);
                    diffusePixel = Vector4.Lerp(diffusePixel, new Vector4(hair, 1.0f), delta);
                    diffuseAlpha = 1.0f;
                }
                
                var specular = maskPixel.X;
                var roughness = maskPixel.Y;
                var subsurface = maskPixel.Z;
                var metallic = 0.0f;
                var roughnessPixel = new Vector4(subsurface, roughness, metallic, specular);
                diffuseAlpha = material.ComputeAlpha(diffuseAlpha * alphaMultiplier);
                
                if (skinType == SkinType.Face)
                {
                    if (data.LipStick)
                    {
                        diffusePixel = Vector4.Lerp(diffusePixel, lipColor, normalPixel.W * lipColor.W);
                        roughnessPixel *= lipRoughnessScale;
                    }
                }
                
                diffuseTexture[x, y] = (diffusePixel with {W = diffuseAlpha}).ToSkColor();
                normalTexture[x, y] = (normalPixel with {W = 1.0f}).ToSkColor();
                metallicRoughness[x, y] = roughnessPixel.ToSkColor();
            }
        });

        var output = new MaterialBuilder(name);
        output.WithBaseColor(BuildImage(diffuseTexture, name, "diffuse"));
        output.WithNormal(BuildImage(normalTexture, name, "normal"), 0.5f);

        var mr = BuildImage(metallicRoughness, name, "sss_roughness_metallic_specular");
        output.WithMetallicRoughness(mr, 0.0f, 1.0f); // metallic
        output.WithSpecularFactor(mr, 1.0f); // specular
        output.WithMetallicRoughnessShader();
        
        if (alphaThreshold > 0)
            output.WithAlpha(AlphaMode.MASK, alphaThreshold);

        output.WithDoubleSide(material.RenderBackfaces);
        
        return output;
    }
}
