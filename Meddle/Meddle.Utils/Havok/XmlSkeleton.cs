using System.Xml;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meddle.Utils.Havok;

/// <summary>Representation of a skeleton in XML data.</summary>
public class XmlSkeleton {
    /// <summary>The ID of the skeleton.</summary>
    public readonly int Id;

    /// <summary>The reference pose of the skeleton (also known as the "resting" or "base" pose).</summary>
    public readonly float[][] ReferencePose;

    /// <summary>The parent indices of the skeleton. The root bone will have a parent index of -1.</summary>
    public readonly int[] ParentIndices;

    /// <summary>The names of the bones in the skeleton. A bone's "ID" is represented by the index it has in this array.</summary>
    public readonly string[] BoneNames;

    public XmlSkeleton( XmlElement element ) {
        Id = int.Parse( element.GetAttribute( "id" )[ 1.. ] );

        ReferencePose = ReadReferencePose( element );
        ParentIndices = ReadParentIndices( element );
        BoneNames     = ReadBoneNames( element );
    }

    private float[][] ReadReferencePose( XmlElement element ) {
        var referencePose = element.GetElementsByTagName( "array" )
            .Cast< XmlElement >()
            .Where( x => x.GetAttribute( "name" ) == "referencePose" )
            .ToArray()[ 0 ];

        var size = int.Parse( referencePose.GetAttribute( "size" ) );

        var referencePoseArr = new float[size][];

        var i = 0;
        foreach( var node in referencePose.ChildNodes.Cast< XmlElement >() ) {
            referencePoseArr[ i ] =  XmlUtils.ParseVec12( node.InnerText );
            i                     += 1;
        }

        return referencePoseArr;
    }

    private int[] ReadParentIndices( XmlElement element ) {
        var parentIndices = element.GetElementsByTagName( "array" )
            .Cast< XmlElement >()
            .Where( x => x.GetAttribute( "name" ) == "parentIndices" )
            .ToArray()[ 0 ];

        var parentIndicesArr = new int[int.Parse( parentIndices.GetAttribute( "size" ) )];

        var parentIndicesStr = parentIndices.InnerText.Split( "\n" )
            .Select( x => x.Trim() )
            .Where( x => !string.IsNullOrWhiteSpace( x ) )
            .ToArray();

        var i = 0;
        foreach( var str2 in parentIndicesStr ) {
            foreach( var str3 in str2.Split( " " ) ) {
                parentIndicesArr[ i ] = int.Parse( str3 );
                i++;
            }
        }

        return parentIndicesArr;
    }

    private string[] ReadBoneNames( XmlElement element ) {
        var bonesObj = element.GetElementsByTagName( "array" )
            .Cast< XmlElement >()
            .Where( x => x.GetAttribute( "name" ) == "bones" )
            .ToArray()[ 0 ];

        var bones = new string[int.Parse( bonesObj.GetAttribute( "size" ) )];

        var boneNames = bonesObj.GetElementsByTagName( "struct" )
            .Cast< XmlElement >()
            .Select( x => x.GetElementsByTagName( "string" )
                .Cast< XmlElement >()
                .First( y => y.GetAttribute( "name" ) == "name" ) );

        var i = 0;
        foreach( var boneName in boneNames ) {
            bones[ i ] = boneName.InnerText;
            i++;
        }

        return bones;
    }
}
