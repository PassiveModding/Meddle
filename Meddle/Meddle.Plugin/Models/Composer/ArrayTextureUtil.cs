using Meddle.Plugin.Utils;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Helpers;

namespace Meddle.Plugin.Models.Composer;

public static class ArrayTextureUtil
{
    private static string GetOutDir(string cacheDir)
    {
        var outDir = Path.Combine(cacheDir, "array_textures");
        Directory.CreateDirectory(outDir);
        return outDir;
    }
    
    public static void SaveSphereTextures(SqPack pack, string cacheDir)
    {
        var outDir = GetOutDir(cacheDir);
        var catchlight = pack.GetFileOrReadFromDisk("chara/common/texture/sphere_d_array.tex");
        if (catchlight == null) throw new Exception("Failed to load catchlight texture");
        var catchLightTex = new TexFile(catchlight);
        var catchlightOutDir = Path.Combine(outDir, "chara/common/texture/sphere_d_array");
        Directory.CreateDirectory(catchlightOutDir);
        for (int i = 0; i < catchLightTex.Header.CalculatedArraySize; i++)
        {
            var img = ImageUtils.GetTexData(catchLightTex, i, 0, 0);
            var texture = img.ImageAsPng();
            File.WriteAllBytes(Path.Combine(catchlightOutDir, $"sphere_d_array.{i}.png"), texture.ToArray());
        }
    }

    public static void SaveTileTextures(SqPack pack, string cacheDir)
    {
        var outDir = GetOutDir(cacheDir);
        var tileNorm = pack.GetFileOrReadFromDisk("chara/common/texture/tile_norm_array.tex");
        if (tileNorm == null) throw new Exception("Failed to load tile norm texture");
        var tileNormTex = new TexFile(tileNorm);
        var tileNormOutDir = Path.Combine(outDir, "chara/common/texture/tile_norm_array");
        Directory.CreateDirectory(tileNormOutDir);
        for (int i = 0; i < tileNormTex.Header.CalculatedArraySize; i++)
        {
            var img = ImageUtils.GetTexData(tileNormTex, i, 0, 0);
            var texture = img.ImageAsPng();
            File.WriteAllBytes(Path.Combine(tileNormOutDir, $"tile_norm_array.{i}.png"), texture.ToArray());
        }
        
        var tileOrb = pack.GetFileOrReadFromDisk("chara/common/texture/tile_orb_array.tex");
        if (tileOrb == null) throw new Exception("Failed to load tile orb texture");
        var tileOrbTex = new TexFile(tileOrb);
        var tileOrbOutDir = Path.Combine(outDir, "chara/common/texture/tile_orb_array");
        Directory.CreateDirectory(tileOrbOutDir);
        for (int i = 0; i < tileOrbTex.Header.CalculatedArraySize; i++)
        {
            var img = ImageUtils.GetTexData(tileOrbTex, i, 0, 0);
            var texture = img.ImageAsPng();
            File.WriteAllBytes(Path.Combine(tileOrbOutDir, $"tile_orb_array.{i}.png"), texture.ToArray());
        }
    }
    
    public static void SaveBgSphereTextures(SqPack pack, string cacheDir)
    {
        var outDir = GetOutDir(cacheDir);
        var catchlight = pack.GetFileOrReadFromDisk("bgcommon/texture/sphere_d_array.tex");
        if (catchlight == null) throw new Exception("Failed to load catchlight texture");
        var catchLightTex = new TexFile(catchlight);
        var catchlightOutDir = Path.Combine(outDir, "bgcommon/texture/sphere_d_array");
        Directory.CreateDirectory(catchlightOutDir);

        for (int i = 0; i < catchLightTex.Header.CalculatedArraySize; i++)
        {
            var img = ImageUtils.GetTexData(catchLightTex, i, 0, 0);
            var texture = img.ImageAsPng();
            File.WriteAllBytes(Path.Combine(catchlightOutDir, $"sphere_d_array.{i}.png"), texture.ToArray());
        }
    }

    public static void SaveBgDetailTextures(SqPack pack, string cacheDir)
    {
        var outDir = GetOutDir(cacheDir);
        var detailD = pack.GetFileOrReadFromDisk("bgcommon/nature/detail/texture/detail_d_array.tex");
        if (detailD == null) throw new Exception("Failed to load detail diffuse texture");
        var detailDTex = new TexFile(detailD);
        var detailDOutDir = Path.Combine(outDir, "bgcommon/nature/detail/texture/detail_d_array");
        Directory.CreateDirectory(detailDOutDir);

        for (int i = 0; i < detailDTex.Header.CalculatedArraySize; i++)
        {
            var img = ImageUtils.GetTexData(detailDTex, i, 0, 0);
            var texture = img.ImageAsPng();
            File.WriteAllBytes(Path.Combine(detailDOutDir, $"detail_d_array.{i}.png"), texture.ToArray());
        }

        var detailN = pack.GetFileOrReadFromDisk("bgcommon/nature/detail/texture/detail_n_array.tex");
        if (detailN == null) throw new Exception("Failed to load tile orb texture");
        var detailNTex = new TexFile(detailN);
        var detailNOutDir = Path.Combine(outDir, "bgcommon/nature/detail/texture/detail_n_array");
        Directory.CreateDirectory(detailNOutDir);

        for (int i = 0; i < detailNTex.Header.CalculatedArraySize; i++)
        {
            var img = ImageUtils.GetTexData(detailNTex, i, 0, 0);
            var texture = img.ImageAsPng();
            File.WriteAllBytes(Path.Combine(detailNOutDir, $"detail_n_array.{i}.png"), texture.ToArray());
        }
    }
}
