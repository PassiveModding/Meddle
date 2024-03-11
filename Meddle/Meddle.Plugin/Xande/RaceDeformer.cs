using System.Numerics;
using System.Text.RegularExpressions;
using Meddle.Plugin.Xande.Enums;
using Meddle.Plugin.Xande.Files;
using SharpGLTF.Scenes;

namespace Meddle.Plugin.Xande;

public class BoneNodeBuilder : NodeBuilder {
    public string BoneName { get; set; }
    private int? suffix;
    public int? Suffix {
        get => suffix;
        set {
            suffix = value;
            Name =
                suffix is int val ?
                    $"{BoneName}_{val}" :
                    BoneName;
        }
    }

    public BoneNodeBuilder( string name ) : base( name ) {
        BoneName = name;
    }

    public void SetSuffixRecursively( int? suffix ) {
        Suffix = suffix;
        foreach( var child in VisualChildren ) {
            if( child is BoneNodeBuilder boneChild )
                boneChild.SetSuffixRecursively( suffix );
        }
    }
}

/// <summary>Calculates deformations from a PBD file.</summary>
public partial class RaceDeformer {
    public PbdFile PbdFile { get; }
    private IReadOnlyList<BoneNodeBuilder> BoneMap { get; }

    public RaceDeformer( PbdFile pbd, IReadOnlyList<BoneNodeBuilder> boneMap ) {
        PbdFile  = pbd;
        BoneMap = boneMap;
    }

    /// <summary>Gets the parent of a given race code.</summary>
    public ushort? GetParent( ushort raceCode ) {
        // TODO: npcs
        // Annoying special cases
        if( raceCode == 1201 ) return 1101; // Lalafell F -> Lalafell M
        if( raceCode == 0201 ) return 0101; // Midlander F -> Midlander M
        if( raceCode == 1001 ) return 0201; // Roegadyn F -> Midlander F
        if( raceCode == 0101 ) return null; // Midlander M has no parent

        // First two digits being odd or even can tell us gender
        var isMale = raceCode / 100 % 2 == 1;

        // Midlander M / Midlander F
        return ( ushort )( isMale ? 0101 : 0201 );
    }

    /// <summary>Parses a model path to obtain its race code.</summary>
    public ushort? RaceCodeFromPath( string path ) {
        var fileName = Path.GetFileNameWithoutExtension( path );
        if( fileName[ 0 ] != 'c' ) return null;

        return ushort.Parse( fileName[ 1..5 ] );
    }

    private float[]? ResolveDeformation( PbdFile.Deformer deformer, string name ) {
        // Try and fetch it from the PBD
        var boneNames = deformer.BoneNames;
        if( boneNames != null ) {
            var boneIdx = Array.FindIndex( boneNames, x => x == name );
            if( boneIdx != -1 ) {
                return deformer.DeformMatrices[ boneIdx ];
            }
        }


        // Try and get it from the parent
        var boneNode = BoneMap.First(b => b.BoneName.Equals(name, StringComparison.Ordinal));
        if( boneNode.Parent != null ) {
            var parent = boneNode.Parent as BoneNodeBuilder ??
                throw new InvalidOperationException("Parent isn't a bone node");
            return ResolveDeformation(deformer, parent.BoneName);
        }

        // No deformation, just use identity
        return new float[] {
            0, 0, 0, 0, // Translation (vec3 + unused)
            0, 0, 0, 1, // Rotation (vec4)
            1, 1, 1, 0  // Scale (vec3 + unused)
        };
    }

    /// <summary>Deforms a vertex using a deformer.</summary>
    /// <param name="deformer">The deformer to use.</param>
    /// <param name="nameIndex">The index of the bone name in the deformer's bone name list.</param>
    /// <param name="origPos">The original position of the vertex.</param>
    /// <returns>The deformed position of the vertex.</returns>
    public Vector3? DeformVertex( PbdFile.Deformer deformer, int nameIndex, Vector3 origPos ) {
        var matrix    = ResolveDeformation( deformer, BoneMap[nameIndex].BoneName );
        if( matrix != null ) { return MatrixTransform( origPos, matrix ); }

        return null;
    }

    // Literally ripped directly from xivModdingFramework because I am lazy
    private static Vector3 MatrixTransform( Vector3 vector, float[] transform ) => new(
        vector.X * transform[ 0 ] + vector.Y * transform[ 1 ] + vector.Z * transform[ 2 ] + 1.0f * transform[ 3 ],
        vector.X * transform[ 4 ] + vector.Y * transform[ 5 ] + vector.Z * transform[ 6 ] + 1.0f * transform[ 7 ],
        vector.X * transform[ 8 ] + vector.Y * transform[ 9 ] + vector.Z * transform[ 10 ] + 1.0f * transform[ 11 ]
    );

    [GeneratedRegex(@"c(?'racecode'\d{4})", RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
    public static partial Regex RaceCodeParser();

    public static GenderRace ParseRaceCode(string path)
    {
        var match = RaceCodeParser().Match(path);

        if( match.Success ) {
            var raceCode = ushort.Parse(match.Groups["racecode"].Value);
            return ( GenderRace )raceCode;
        }

        return GenderRace.Unknown;
    }
}
