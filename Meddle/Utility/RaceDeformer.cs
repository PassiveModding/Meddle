using System.Numerics;
using Meddle.Plugin.Files;
using Meddle.Plugin.Models;

namespace Meddle.Plugin.Utility;


/// <summary>Calculates deformations from a PBD file.</summary>
public class RaceDeformer
{
    private readonly PbdFile.Deformer[] _deformers;
    private readonly GltfSkeleton _skeleton;

    /// <summary>Calculates deformations from a PBD file.</summary>
    public RaceDeformer(PbdFile pbd, GltfSkeleton skeleton, ushort from, ushort to)
    {
        _skeleton = skeleton;
        _deformers = GetDeformers(pbd, from, to).ToArray();
    }

    private static IEnumerable<PbdFile.Deformer> GetDeformers(PbdFile pbdFile, ushort from, ushort to)
    {
        if (from == to)
            return Array.Empty<PbdFile.Deformer>();
        
        var     deformSteps = new List<ushort>();
        ushort? current     = to;
        while (current != null)
        {
            deformSteps.Add(current.Value);
            current = GetParent(current.Value);
            if (current == from) break;
        }

        deformSteps.Reverse();
        var deformers = new PbdFile.Deformer[deformSteps.Count];
        for (var i = 0; i < deformSteps.Count; i++)
        {
            var raceCode = deformSteps[i];
            var deformer = pbdFile.GetDeformerFromRaceCode(raceCode);
            deformers[i] = deformer;
        }

        return deformers;
    }

    /// <summary>Gets the parent of a given race code.</summary>
    public static ushort? GetParent( ushort raceCode ) {
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
    public static ushort? RaceCodeFromPath( string path ) {
        var fileName = Path.GetFileNameWithoutExtension( path );
        if( fileName[ 0 ] != 'c' ) return null;

        return ushort.Parse( fileName[ 1..5 ] );
    }

    private float[]? ResolveDeformation( PbdFile.Deformer deformer, string boneName ) {
        // Try and fetch it from the PBD
        var matrix = deformer.GetDeformMatrix( boneName );
        if (matrix != null)
        {
            return matrix; 
        }

        // Try and get it from the parent
        var boneNode = _skeleton.Joints.First( x => x.Name == boneName );
        if (boneNode.Parent != null)
        {
            return ResolveDeformation( deformer, boneNode.Parent.Name ); 
        }

        // No deformation, just use identity
        return
        [
            0, 0, 0, 0, // Translation (vec3 + unused)
            0, 0, 0, 1, // Rotation (vec4)
            1, 1, 1, 0, // Scale (vec3 + unused)
        ];
    }

    /// <summary>Deforms a vertex using a deformer.</summary>
    /// <param name="deformer">The deformer to use.</param>
    /// <param name="boneNameIndex">The index of the bone name in the deformer's bone name list.</param>
    /// <param name="origPos">The original position of the vertex.</param>
    /// <returns>The deformed position of the vertex.</returns>
    public Vector3? DeformVertex( PbdFile.Deformer deformer, int boneNameIndex, Vector3 origPos ) {
        var boneNames = _skeleton.Joints.Select( x => x.Name ).ToArray();
        var boneName  = boneNames[ boneNameIndex ];
        return DeformVertex(deformer, boneName, origPos);
    }

    public Vector3? DeformVertex(PbdFile.Deformer deformer, string boneName, Vector3 origPos)
    {
        var matrix = ResolveDeformation(deformer, boneName);
        if (matrix != null)
        {
            return MatrixTransform(origPos, matrix);
        }
        
        return null;
    }

    public Vector3? DeformVertex(Vector3 origPos, IReadOnlyList<(int jointIndex, float weight)> boneMap)
    {
        var position = origPos;
        foreach (var deformer in _deformers)
        {
            var deformedPos = Vector3.Zero;
            foreach (var (jointIndex, weight) in boneMap)
            {
                if (weight == 0) continue;
                    
                var deformed = DeformVertex(deformer, jointIndex, position);
                if (deformed != null)
                {
                    deformedPos += deformed.Value * weight;
                }
            }

            position = deformedPos;
        }
        
        return position;
    }

    // Literally ripped directly from xivModdingFramework because I am lazy
    private static Vector3 MatrixTransform( Vector3 vector, IReadOnlyList<float> transform ) => new(
        vector.X * transform[ 0 ] + vector.Y * transform[ 1 ] + vector.Z * transform[ 2 ] + 1.0f * transform[ 3 ],
        vector.X * transform[ 4 ] + vector.Y * transform[ 5 ] + vector.Z * transform[ 6 ] + 1.0f * transform[ 7 ],
        vector.X * transform[ 8 ] + vector.Y * transform[ 9 ] + vector.Z * transform[ 10 ] + 1.0f * transform[ 11 ]
    );
}
