using System.Numerics;
using System.Text.RegularExpressions;
using Meddle.Utils.Constants;
using Meddle.Utils.Files;

namespace Meddle.Utils;

/// <summary>Calculates deformations from a PBD file.</summary>
public partial class RaceDeformer(PbdFile pbd, IReadOnlyList<BoneNodeBuilder> boneMap)
{
    public PbdFile PbdFile { get; } = pbd;
    private IReadOnlyList<BoneNodeBuilder> BoneMap { get; } = boneMap;

    private PbdFile.DeformMatrix4X4? ResolveDeformation(PbdFile.Deformer deformer, string name)
    {
        // Try and fetch it from the PBD
        var boneNames = deformer.BoneNames;
        var boneIdx = Array.FindIndex(boneNames, x => x == name);
        if (boneIdx != -1)
        {
            return deformer.DeformMatrices[boneIdx];
        }

        // Try and get it from the parent
        var boneNode = BoneMap.First(b => b.BoneName.Equals(name, StringComparison.Ordinal));
        if (boneNode.Parent != null)
        {
            var parent = boneNode.Parent as BoneNodeBuilder ??
                         throw new InvalidOperationException("Parent isn't a bone node");
            return ResolveDeformation(deformer, parent.BoneName);
        }

        // No deformation, just use identity
        return PbdFile.Deformer.Identity();
    }

    /// <summary>Deforms a vertex using a deformer.</summary>
    /// <param name="deformer">The deformer to use.</param>
    /// <param name="nameIndex">The index of the bone name in the deformer's bone name list.</param>
    /// <param name="origPos">The original position of the vertex.</param>
    /// <returns>The deformed position of the vertex.</returns>
    public Vector3? DeformVertex(PbdFile.Deformer deformer, int nameIndex, Vector3 origPos)
    {
        var matrix = ResolveDeformation(deformer, BoneMap[nameIndex].BoneName);
        if (matrix != null)
        {
            return PbdFile.Deformer.TransformCoordinate(origPos, matrix.Value);
        }

        return null;
    }
    
    //[GeneratedRegex(@"c(?'racecode'\d{4})", RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
    // match cXXXX where character before c is not a letter
    [GeneratedRegex(@"(?<!\p{L})c(?'racecode'\d{4})", RegexOptions.ExplicitCapture)]
    public static partial Regex RaceCodeParser();

    public static GenderRace ParseRaceCode(string path)
    {
        var match = RaceCodeParser().Match(path);

        if (match.Success)
        {
            var raceCode = ushort.Parse(match.Groups["racecode"].Value);
            return (GenderRace)raceCode;
        }

        return GenderRace.Unknown;
    }
}
