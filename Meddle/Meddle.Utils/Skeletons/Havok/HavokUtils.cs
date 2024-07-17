using System.Xml;
using Meddle.Utils.Skeletons.Havok.Models;

namespace Meddle.Utils.Skeletons.Havok;

public class HavokUtils
{
    public static HavokSkeleton ParseHavokXml(string xml)
    {
        var document = new XmlDocument();
        document.LoadXml( xml );

        var skeletons = document.SelectNodes( "/hktagfile/object[@type='hkaSkeleton']" )!
                            .Cast< XmlElement >()
                            .Select(ParsePartialSkeletonXml).ToArray();

        var mappings = document.SelectNodes( "/hktagfile/object[@type='hkaSkeletonMapper']" )!
                           .Cast< XmlElement >()
                           .Select(ParseSkeletonMappingXml).ToArray();

        var animationContainer = document.SelectSingleNode( "/hktagfile/object[@type='hkaAnimationContainer']" )!;
        var animationSkeletons = animationContainer
                                 .SelectNodes( "array[@name='skeletons']" )!
                                 .Cast< XmlElement >()
                                 .First();

        // A recurring theme in Havok XML is that IDs start with a hash
        // If you see a string[1..], that's probably what it is
        var mainSkeletonStr = animationSkeletons.ChildNodes[ 0 ]!.InnerText;
        var mainSkeleton = int.Parse( mainSkeletonStr[ 1.. ] );


        var skeleton = new HavokSkeleton
        {
            Skeletons = skeletons,
            Mappings = mappings,
            MainSkeleton = mainSkeleton
        };
        
        return skeleton;
    }
    
    public static HavokPartialSkeleton ParsePartialSkeletonXml( XmlElement element ) {
        var id = int.Parse( element.GetAttribute( "id" )[ 1.. ] );

        var referencePose = ReadReferencePose( element );
        var parentIndices = ReadParentIndices( element );
        var boneNames     = ReadBoneNames( element );
        
        var skeleton = new HavokPartialSkeleton
        {
            Id = id,
            ReferencePose = referencePose,
            ParentIndices = parentIndices,
            BoneNames = boneNames
        };
        
        return skeleton;
    }
    
    private static float[][] ReadReferencePose( XmlElement element ) {
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
    
    private static int[] ReadParentIndices( XmlElement element ) {
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

    private static string[] ReadBoneNames( XmlElement element ) {
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
    
    public static HavokSkeletonMapping ParseSkeletonMappingXml( XmlElement element ) {
        var id = int.Parse( element.GetAttribute( "id" )[ 1.. ] );

        var skeletonANode = element.SelectSingleNode( "struct/ref[@name='skeletonA']" )!;
        var skeletonA = int.Parse( skeletonANode.InnerText[ 1.. ] );

        var skeletonBNode = element.SelectSingleNode( "struct/ref[@name='skeletonB']" )!;
        var skeletonB = int.Parse( skeletonBNode.InnerText[ 1.. ] );

        var simpleMappings = ( XmlElement )element.SelectSingleNode( "struct/array[@name='simpleMappings']" )!;
        var count          = int.Parse( simpleMappings.GetAttribute( "size" ) );
        var boneMappings = new HavokSkeletonMapping.BoneMapping[count];

        for( var i = 0; i < count; i++ ) {
            var mapping   = simpleMappings.SelectSingleNode( $"struct[{i + 1}]" )!;
            var boneA     = int.Parse( mapping.SelectSingleNode( "int[@name='boneA']" )?.InnerText ?? "0" );
            var boneB     = int.Parse( mapping.SelectSingleNode( "int[@name='boneB']" )?.InnerText ?? "0" );
            var transform = XmlUtils.ParseVec12( mapping.SelectSingleNode( "vec12[@name='aFromBTransform']" )!.InnerText );

            var mappingClass = new HavokSkeletonMapping.BoneMapping( boneA, boneB, transform );
            boneMappings[ i ] = mappingClass;
        }
        
        var skMapping = new HavokSkeletonMapping
        {
            Id = id,
            SkeletonA = skeletonA,
            SkeletonB = skeletonB,
            BoneMappings = boneMappings
        };
        
        return skMapping;
    }
}
