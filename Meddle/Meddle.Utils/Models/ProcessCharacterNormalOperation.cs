using System.Numerics;
using Meddle.Utils.Files.Structs.Material;

namespace Meddle.Utils.Models;


public class ProcessCharacterNormalOperation(SKTexture normal, ColorTable table)
{
    public SKTexture Normal    { get; } = normal.Copy();
    public SKTexture BaseColor { get; } = new(normal.Width, normal.Height);
    public SKTexture Specular  { get; } = new(normal.Width, normal.Height);
    public SKTexture Emissive  { get; } = new(normal.Width, normal.Height);
        
    public ProcessCharacterNormalOperation Run()
    {
        for (var y = 0; y < normal.Height; y++)
        for (var x = 0; x < normal.Width; x++)
        {
            var normalPixel = Normal[x, y].ToVector4();
            
            // Table row data (.a)
            var (prevRow, nextRow, row) = table.Lookup(normalPixel.W);
            
            // Base colour (table, .b)
            var lerpedDiffuse = Vector3.Lerp(prevRow.Diffuse, nextRow.Diffuse, row.Weight);
            BaseColor[x, y] = new Vector4(lerpedDiffuse.X, lerpedDiffuse.Y, lerpedDiffuse.Z, normalPixel.Z).ToSkColor();
            
            // Specular (table)
            var lerpedSpecularColor = Vector3.Lerp(prevRow.Specular, nextRow.Specular, row.Weight);
            var lerpedSpecularFactor = float.Lerp(prevRow.SpecularStrength, nextRow.SpecularStrength, row.Weight);
            Specular[x, y] = new Vector4(lerpedSpecularColor.X, lerpedSpecularColor.Y, lerpedSpecularColor.Z, lerpedSpecularFactor).ToSkColor();
            
            // Emissive (table)
            var lerpedEmissive = Vector3.Lerp(prevRow.Emissive, nextRow.Emissive, row.Weight);
            Emissive[x, y] = lerpedEmissive.ToSkColor();
            
            // Normal (.rg)
            Normal[x, y] = new Vector4(normalPixel.X, normalPixel.Y, 1, normalPixel.Z).ToSkColor();
        }
            
        return this;
    }
}
