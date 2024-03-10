using System.Numerics;
using Dalamud.Plugin.Services;
using Lumina.Data.Parsing;
using Meddle.Plugin.Models;
using Penumbra.GameData.Files;
using SharpGLTF.Materials;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Meddle.Plugin.Services;

public class MaterialService
{
    private readonly IPluginLog _log;

    public MaterialService(IPluginLog log)
    {
        _log = log;
    }
    
    /// <summary> Dependency-less material configuration, for use when no material data can be resolved. </summary>
    public static readonly MaterialBuilder Unknown = new MaterialBuilder("UNKNOWN")
        .WithMetallicRoughnessShader()
        .WithDoubleSide(true)
        .WithBaseColor(Vector4.One);

    private static string GetMaterialName(Material material, string name)
    {
        return $"{name}_{material.Mtrl.ShaderPackage.Name.Replace(".shpk", "")}";
    }

    private MaterialBuilder BuildCharacterMaterialCommon(Material material, string name)
    {
        if (material is not CharacterMaterial && material is not CharacterGlassMaterial)
        {
            throw new ArgumentException("Material must be a character material.");
        }
        
        name = GetMaterialName(material, name);
        
        // Build the textures from the color table.
        var table = material.Mtrl.Table;

        var normal = material.Textures[TextureUsage.SamplerNormal];

        var operation = new ProcessCharacterNormalOperation(normal, table);
        ParallelRowIterator.IterateRows(SixLabors.ImageSharp.Configuration.Default, normal.Bounds, in operation);

        // Check if full textures are provided, and merge in if available.
        var baseColor = operation.BaseColor;
        if (material.Textures.TryGetValue(TextureUsage.SamplerDiffuse, out var diffuse))
        {
            MultiplyOperation.Execute(diffuse, operation.BaseColor);
            baseColor = diffuse;
        }

        Image specular = operation.Specular;
        if (material.Textures.TryGetValue(TextureUsage.SamplerSpecular, out var specularTexture))
        {
            MultiplyOperation.Execute(specularTexture, operation.Specular);
            specular = specularTexture;
        }

        // Pull further information from the mask.
        if (material.Textures.TryGetValue(TextureUsage.SamplerMask, out var maskTexture))
        {
            // Extract the red channel for "ambient occlusion".
            maskTexture.Mutate(context => context.Resize(baseColor.Width, baseColor.Height));
            maskTexture.ProcessPixelRows(baseColor, (maskAccessor, baseColorAccessor) =>
            {
                for (var y = 0; y < maskAccessor.Height; y++)
                {
                    var maskSpan      = maskAccessor.GetRowSpan(y);
                    var baseColorSpan = baseColorAccessor.GetRowSpan(y);

                    for (var x = 0; x < maskSpan.Length; x++)
                        baseColorSpan[x].FromVector4(baseColorSpan[x].ToVector4() * new Vector4(maskSpan[x].R / 255f));
                }
            });
            // TODO: handle other textures stored in the mask?
        }

        // Specular extension puts colour on RGB and factor on A. We're already packing like that, so we can reuse the texture.
        var specularImage = BuildImage(specular, name, "specular");

        return BuildSharedBase(material, name)
            .WithBaseColor(BuildImage(baseColor,         name, "basecolor"))
            .WithNormal(BuildImage(operation.Normal,     name, "normal"))
            .WithEmissive(BuildImage(operation.Emissive, name, "emissive"), Vector3.One, 1)
            .WithSpecularFactor(specularImage, 1)
            .WithSpecularColor(specularImage);
    }

    public MaterialBuilder BuildCharacterMaterial(CharacterMaterial material, string name)
    {
        return BuildCharacterMaterialCommon(material, name).WithAlpha(AlphaMode.MASK, 0.5f);
    }
    
    public MaterialBuilder BuildCharacterGlassMaterial(CharacterGlassMaterial material, string name)
    {
        return BuildCharacterMaterialCommon(material, name).WithAlpha(AlphaMode.BLEND);
    }

    public MaterialBuilder BuildHairMaterial(HairMaterial material, string name)
    {
        name = GetMaterialName(material, name);
        // Trust me bro.
        const uint categoryHairType = 0x24826489;
        const uint valueFace        = 0x6E5B8F10;
    
        var isFace = material.Mtrl.ShaderPackage.ShaderKeys
            .Any(key => key is { Category: categoryHairType, Value: valueFace });

        var normal = material.Textures[TextureUsage.SamplerNormal];
        var mask   = material.Textures[TextureUsage.SamplerMask];

        mask.Mutate(context => context.Resize(normal.Width, normal.Height));

        var baseColor = new Image<Rgba32>(normal.Width, normal.Height);
        normal.ProcessPixelRows(mask, baseColor, (normalAccessor, maskAccessor, baseColorAccessor) =>
        {
            for (var y = 0; y < normalAccessor.Height; y++)
            {
                var normalSpan    = normalAccessor.GetRowSpan(y);
                var maskSpan      = maskAccessor.GetRowSpan(y);
                var baseColorSpan = baseColorAccessor.GetRowSpan(y);

                for (var x = 0; x < normalSpan.Length; x++)
                {
                    var color = Vector4.Lerp(material.Color, material.HighlightColor, maskSpan[x].A / 255f);
                    baseColorSpan[x].FromVector4(color * new Vector4(maskSpan[x].R / 255f));
                    baseColorSpan[x].A = normalSpan[x].A;

                    normalSpan[x].A = byte.MaxValue;
                }
            }
        });

        return BuildSharedBase(material, name)
            .WithBaseColor(BuildImage(baseColor, name, "basecolor"))
            .WithNormal(BuildImage(normal, name, "normal"))
            .WithAlpha(isFace ? AlphaMode.BLEND : AlphaMode.MASK, 0.5f);
    }
    
    /// <summary> Build a material following the semantics of iris.shpk. </summary>
    // NOTE: This is largely the same as the hair material, but is also missing a few features that would cause it to diverge. Keeping separate for now.
    public MaterialBuilder BuildIrisMaterial(IrisMaterial material, string name)
    {
        name = GetMaterialName(material, name);
        var normal = material.Textures[TextureUsage.SamplerNormal];
        var mask   = material.Textures[TextureUsage.SamplerMask];

        mask.Mutate(context => context.Resize(normal.Width, normal.Height));

        var baseColor = new Image<Rgba32>(normal.Width, normal.Height);
        normal.ProcessPixelRows(mask, baseColor, (normalAccessor, maskAccessor, baseColorAccessor) =>
        {
            for (var y = 0; y < normalAccessor.Height; y++)
            {
                var normalSpan    = normalAccessor.GetRowSpan(y);
                var maskSpan      = maskAccessor.GetRowSpan(y);
                var baseColorSpan = baseColorAccessor.GetRowSpan(y);

                for (var x = 0; x < normalSpan.Length; x++)
                {
                    // TODO: Secondary color
                    baseColorSpan[x].FromVector4((material.PrimaryColor) * new Vector4(maskSpan[x].R / 255f));
                    baseColorSpan[x].A = normalSpan[x].A;

                    normalSpan[x].A = byte.MaxValue;
                }
            }
        });   
        
        return BuildSharedBase(material, name)
            .WithBaseColor(BuildImage(baseColor, name, "basecolor"))
            .WithNormal(BuildImage(normal,       name, "normal"));
    }
    
    /// <summary> Build a material following the semantics of skin.shpk. </summary>
    public MaterialBuilder BuildSkinMaterial(SkinMaterial material, string name)
    {
        name = GetMaterialName(material, name);
        // Trust me bro.
        const uint categorySkinType = 0x380CAED0;
        const uint valueFace        = 0xF5673524;

        // Face is the default for the skin shader, so a lack of skin type category is also correct.
        var isFace = !material.Mtrl.ShaderPackage.ShaderKeys
            .Any(key => key.Category == categorySkinType && key.Value != valueFace);

        // TODO: There's more nuance to skin than this, but this should be enough for a baseline reference.
        // TODO: Specular?
        var diffuse = material.Textures[TextureUsage.SamplerDiffuse];
        var normal  = material.Textures[TextureUsage.SamplerNormal];
        var mask    = material.Textures[TextureUsage.SamplerMask];

        // Create a copy of the normal that's the same size as the diffuse for purposes of copying the opacity across.
        var resizedNormal = normal.Clone(context => context.Resize(diffuse.Width, diffuse.Height));
        diffuse.ProcessPixelRows(resizedNormal, (diffuseAccessor, normalAccessor) =>
        {
            for (var y = 0; y < diffuseAccessor.Height; y++)
            {
                var diffuseSpan = diffuseAccessor.GetRowSpan(y);
                var normalSpan  = normalAccessor.GetRowSpan(y);

                for (var x = 0; x < diffuseSpan.Length; x++)
                    diffuseSpan[x].A = normalSpan[x].B;
            }
        });
        
        
        // Clear the blue channel out of the normal now that we're done with it.
        normal.ProcessPixelRows(normalAccessor =>
        {
            for (var y = 0; y < normalAccessor.Height; y++)
            {
                var normalSpan = normalAccessor.GetRowSpan(y);

                for (var x = 0; x < normalSpan.Length; x++)
                    normalSpan[x].B = byte.MaxValue;
            }
        });
        
        // Masking
        var resizedMask = mask.Clone(context => context.Resize(diffuse.Width, diffuse.Height));
        if (material.PrimaryColor.HasValue || material.SecondaryColor.HasValue)
        {
            resizedMask.ProcessPixelRows(diffuse, (maskAccessor, diffuseAccessor) =>
            {
                for (var y = 0; y < maskAccessor.Height; y++)
                {
                    var maskSpan    = maskAccessor.GetRowSpan(y);
                    var diffuseSpan = diffuseAccessor.GetRowSpan(y);

                    for (var x = 0; x < maskSpan.Length; x++)
                    {
                        // Skin
                        if (material.PrimaryColor.HasValue)
                        {
                            var intensity = maskSpan[x].R / 255f;
                            // TODO: Confirm the cutoff. For AuRa face tex, the scales sit ~128 and skin sits at 255. We don't want to apply this to the scaled areas.
                            if (intensity == 1)
                            {
                                var baseColor = material.PrimaryColor.Value;
                                var color = diffuseSpan[x].ToVector4();
                                var lerpCol = Vector4.Lerp(color, baseColor, intensity);
                                diffuseSpan[x].FromVector4(lerpCol);
                            }
                        }

                        // Lips
                        if (isFace && material.SecondaryColor.HasValue)
                        {
                            var lipIntensity = maskSpan[x].B / 255f;
                            var highlight = material.SecondaryColor.Value;
                            var color = diffuseSpan[x].ToVector4();
                            // highlight may have alpha also, but since we are modifying the diffuse
                            var lerpCol = Vector4.Lerp(color, highlight, lipIntensity * highlight.W);
                            lerpCol.W = color.W;
                            diffuseSpan[x].FromVector4(lerpCol);
                        }
                    }
                }
            });
        }
        
        return BuildSharedBase(material, name)
            .WithBaseColor(BuildImage(diffuse, name, "basecolor"))
            .WithNormal(BuildImage(normal,     name, "normal"))
            .WithOcclusion(BuildImage(mask, name, "mask"))
            .WithAlpha(isFace ? AlphaMode.MASK : AlphaMode.OPAQUE, 0.5f);
    }
    
    /// <summary> Build a material from a source with unknown semantics. </summary>
    /// <remarks> Will make a loose effort to fetch common / simple textures. </remarks>
    public MaterialBuilder BuildFallbackMaterial(Material material, string name)
    {
        var materialBuilder = BuildSharedBase(material, name)
            .WithMetallicRoughnessShader()
            .WithBaseColor(Vector4.One);

        if (material.Textures.TryGetValue(TextureUsage.SamplerDiffuse, out var diffuse))
            materialBuilder.WithBaseColor(BuildImage(diffuse, name, "basecolor"));

        if (material.Textures.TryGetValue(TextureUsage.SamplerNormal, out var normal))
            materialBuilder.WithNormal(BuildImage(normal, name, "normal"));

        return materialBuilder;
    }
    
    /// <summary> Build a material pre-configured with settings common to all XIV materials/shaders. </summary>
    private static MaterialBuilder BuildSharedBase(Material material, string name)
    {
        // TODO: Move this and potentially the other known stuff into MtrlFile?
        const uint backfaceMask  = 0x1;
        var        showBackfaces = (material.Mtrl.ShaderPackage.Flags & backfaceMask) == 0;

        return new MaterialBuilder(name)
            .WithDoubleSide(showBackfaces);
    }
    
    private readonly struct ProcessCharacterNormalOperation(Image<Rgba32> normal, MtrlFile.ColorTable table) : IRowOperation
    {
        public Image<Rgba32> Normal    { get; } = normal.Clone();
        public Image<Rgba32> BaseColor { get; } = new(normal.Width, normal.Height);
        public Image<Rgba32> Specular  { get; } = new(normal.Width, normal.Height);
        public Image<Rgb24>  Emissive  { get; } = new(normal.Width, normal.Height);

        private Buffer2D<Rgba32> NormalBuffer
            => Normal.Frames.RootFrame.PixelBuffer;

        private Buffer2D<Rgba32> BaseColorBuffer
            => BaseColor.Frames.RootFrame.PixelBuffer;

        private Buffer2D<Rgba32> SpecularBuffer
            => Specular.Frames.RootFrame.PixelBuffer;

        private Buffer2D<Rgb24> EmissiveBuffer
            => Emissive.Frames.RootFrame.PixelBuffer;

        public void Invoke(int y)
        {
            var normalSpan    = NormalBuffer.DangerousGetRowSpan(y);
            var baseColorSpan = BaseColorBuffer.DangerousGetRowSpan(y);
            var specularSpan  = SpecularBuffer.DangerousGetRowSpan(y);
            var emissiveSpan  = EmissiveBuffer.DangerousGetRowSpan(y);

            for (var x = 0; x < normalSpan.Length; x++)
            {
                ref var normalPixel = ref normalSpan[x];

                // Table row data (.a)
                var tableRow = GetTableRowIndices(normalPixel.A / 255f);
                var prevRow  = table[tableRow.Previous];
                var nextRow  = table[tableRow.Next];

                // Base colour (table, .b)
                var lerpedDiffuse = Vector3.Lerp(prevRow.Diffuse, nextRow.Diffuse, tableRow.Weight);
                baseColorSpan[x].FromVector4(new Vector4(lerpedDiffuse, 1));
                baseColorSpan[x].A = normalPixel.B;

                // Specular (table)
                var lerpedSpecularColor = Vector3.Lerp(prevRow.Specular, nextRow.Specular, tableRow.Weight);
                // float.Lerp is .NET8 ;-; #TODO
                var lerpedSpecularFactor = prevRow.SpecularStrength * (1.0f - tableRow.Weight) + nextRow.SpecularStrength * tableRow.Weight;
                specularSpan[x].FromVector4(new Vector4(lerpedSpecularColor, lerpedSpecularFactor));

                // Emissive (table)
                var lerpedEmissive = Vector3.Lerp(prevRow.Emissive, nextRow.Emissive, tableRow.Weight);
                emissiveSpan[x].FromVector4(new Vector4(lerpedEmissive, 1));

                // Normal (.rg)
                // TODO: we don't actually need alpha at all for normal, but _not_ using the existing rgba texture means I'll need a new one, with a new accessor. Think about it.
                normalPixel.B = byte.MaxValue;
                normalPixel.A = byte.MaxValue;
            }
        }
    }
    
    /// <summary> Convert an ImageSharp Image into an ImageBuilder for use with SharpGLTF. </summary>
    private static ImageBuilder BuildImage(Image image, string materialName, string suffix)
    {
        var name = materialName.Replace("/", "").Replace(".mtrl", "") + $"_{suffix}";

        byte[] textureBytes;
        using (var memoryStream = new MemoryStream())
        {
            image.Save(memoryStream, PngFormat.Instance);
            textureBytes = memoryStream.ToArray();
        }

        var imageBuilder = ImageBuilder.From(textureBytes, name);
        imageBuilder.AlternateWriteFileName = $"{name}.*";
        return imageBuilder;
    }
    
    private static TableRow GetTableRowIndices(float input)
    {
        // These calculations are ported from character.shpk.
        var smoothed = MathF.Floor(input * 7.5f % 1.0f * 2)
                       * (-input * 15 + MathF.Floor(input * 15 + 0.5f))
                       + input * 15;

        var stepped = MathF.Floor(smoothed + 0.5f);

        return new TableRow
        {
            Stepped  = (int)stepped,
            Previous = (int)MathF.Floor(smoothed),
            Next     = (int)MathF.Ceiling(smoothed),
            Weight   = smoothed % 1,
        };
    }

    private ref struct TableRow
    {
        public int   Stepped;
        public int   Previous;
        public int   Next;
        public float Weight;
    }
    
    private readonly struct MultiplyOperation
    {
        public static void Execute<TPixel1, TPixel2>(Image<TPixel1> target, Image<TPixel2> multiplier)
            where TPixel1 : unmanaged, IPixel<TPixel1>
            where TPixel2 : unmanaged, IPixel<TPixel2>
        {
            // Ensure the images are the same size
            var (small, large) = target.Width < multiplier.Width && target.Height < multiplier.Height
                ? ((Image)target, (Image)multiplier)
                : (multiplier, target);
            small.Mutate(context => context.Resize(large.Width, large.Height));

            var operation = new MultiplyOperation<TPixel1, TPixel2>(target, multiplier);
            ParallelRowIterator.IterateRows(SixLabors.ImageSharp.Configuration.Default, target.Bounds, in operation);
        }
    }
    
    private readonly struct MultiplyOperation<TPixel1, TPixel2>(Image<TPixel1> target, Image<TPixel2> multiplier) : IRowOperation
        where TPixel1 : unmanaged, IPixel<TPixel1>
        where TPixel2 : unmanaged, IPixel<TPixel2>
    {
        public void Invoke(int y)
        {
            var targetSpan     = target.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y);
            var multiplierSpan = multiplier.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y);

            for (var x = 0; x < targetSpan.Length; x++)
                targetSpan[x].FromVector4(targetSpan[x].ToVector4() * multiplierSpan[x].ToVector4());
        }
    }
}