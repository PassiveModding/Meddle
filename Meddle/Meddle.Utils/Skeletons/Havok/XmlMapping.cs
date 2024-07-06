using System.Xml;

namespace Meddle.Utils.Skeletons.Havok;

public class XmlMapping {
    public readonly int           Id;
    public readonly int           SkeletonA;
    public readonly int           SkeletonB;
    public readonly BoneMapping[] BoneMappings;

    public XmlMapping( XmlElement element ) {
        Id = int.Parse( element.GetAttribute( "id" )[ 1.. ] );

        var skeletonA = element.SelectSingleNode( "struct/ref[@name='skeletonA']" )!;
        SkeletonA = int.Parse( skeletonA.InnerText[ 1.. ] );

        var skeletonB = element.SelectSingleNode( "struct/ref[@name='skeletonB']" )!;
        SkeletonB = int.Parse( skeletonB.InnerText[ 1.. ] );

        var simpleMappings = ( XmlElement )element.SelectSingleNode( "struct/array[@name='simpleMappings']" )!;
        var count          = int.Parse( simpleMappings.GetAttribute( "size" ) );
        BoneMappings = new BoneMapping[count];

        for( var i = 0; i < count; i++ ) {
            var mapping   = simpleMappings.SelectSingleNode( $"struct[{i + 1}]" )!;
            var boneA     = int.Parse( mapping.SelectSingleNode( "int[@name='boneA']" )?.InnerText ?? "0" );
            var boneB     = int.Parse( mapping.SelectSingleNode( "int[@name='boneB']" )?.InnerText ?? "0" );
            var transform = XmlUtils.ParseVec12( mapping.SelectSingleNode( "vec12[@name='aFromBTransform']" )!.InnerText );

            var mappingClass = new BoneMapping( boneA, boneB, transform );
            BoneMappings[ i ] = mappingClass;
        }
    }

    public class BoneMapping {
        public readonly int     BoneA;
        public readonly int     BoneB;
        public readonly float[] Transform;

        public BoneMapping( int boneA, int boneB, float[] transform ) {
            BoneA     = boneA;
            BoneB     = boneB;
            Transform = transform;
        }
    }
}
