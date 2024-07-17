using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using Meddle.Utils.Skeletons.Havok.Models;

namespace Meddle.Utils.Skeletons.Havok;

public class HavokCCUtils
{
 public static HavokSkeleton ParseHavokXml(string xml)
    {
        var document = new XmlDocument();
        document.LoadXml( xml );

        var skeletons = document.SelectNodes( "/hkpackfile/hksection/hkobject[@class='hkaSkeleton']" )!
                                .Cast< XmlElement >()
                                .Select(ParsePartialSkeletonXml).ToArray();

        var mappings = document.SelectNodes( "/hkpackfile/hksection/hkobject[@class='hkaSkeletonMapper']" )!
                               .Cast< XmlElement >()
                               .Select(ParseSkeletonMappingXml).ToArray();

        var animationContainer = document.SelectSingleNode( "/hkpackfile/hksection/hkobject[@class='hkaAnimationContainer']" )!;
        var animationSkeletons = animationContainer
            .SelectSingleNode( "hkparam[@name='skeletons']" )!;

        // A recurring theme in Havok XML is that IDs start with a hash
        // If you see a string[1..], that's probably what it is
        var mainSkeletonStr = animationSkeletons.ChildNodes[ 0 ]!.InnerText.Split('\n').Select(x => x.Trim()).First(x => !string.IsNullOrWhiteSpace(x));
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
        var id = int.Parse( element.GetAttribute( "name" )[ 1.. ] );

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
        var referencePose = element.SelectSingleNode("hkparam[@name='referencePose']")!.InnerText;
        var lines = referencePose.Split('\n').Select(x => x.Trim())
                                 .Where(x => !string.IsNullOrWhiteSpace(x))
                                 .ToArray();
        

        var referencePoseArr = new float[lines.Length][];
        for (int i = 0; i < lines.Length; i++)
        {
            referencePoseArr[i] = ParseVec12(lines[i]);
        }

        return referencePoseArr;
    }
    
    private static int[] ReadParentIndices( XmlElement element ) {
        var parentIndices = element.SelectSingleNode("hkparam[@name='parentIndices']")!.InnerText;
        
        var lines = parentIndices.Split('\n').Select(x => x.Trim())
                                 .Where(x => !string.IsNullOrWhiteSpace(x))
                                 .SelectMany(x => x.Split(' '))
                                 .Select(int.Parse)
                                 .ToArray();
        
        return lines;
    }

    private static string[] ReadBoneNames( XmlElement element ) {
        var bones = element.SelectSingleNode("hkparam[@name='bones']")!;
        var boneNames = bones.SelectNodes("hkobject")!
                             .Cast<XmlElement>()
                             .Select(x => x.SelectSingleNode("hkparam[@name='name']")!.InnerText)
                             .ToArray();

        return boneNames;
    }
    
    public static HavokSkeletonMapping ParseSkeletonMappingXml( XmlElement element ) {
        var mapping = element.SelectSingleNode( "hkparam[@name='mapping']" )!;
        var id = int.Parse( element.GetAttribute( "name" )[ 1.. ] );

        var skeletonANode = mapping.SelectSingleNode( "hkobject/hkparam[@name='skeletonA']" )!;
        var skeletonA = int.Parse( skeletonANode.InnerText[ 1.. ] );

        var skeletonBNode = mapping.SelectSingleNode( "hkobject/hkparam[@name='skeletonB']" )!;
        var skeletonB = int.Parse( skeletonBNode.InnerText[ 1.. ] );

        var simpleMappings = mapping.SelectSingleNode( "hkobject/hkparam[@name='simpleMappings']" )!;
        
        var children = simpleMappings.SelectNodes( "hkobject" )!;
        var boneMappings = new HavokSkeletonMapping.BoneMapping[children.Count];

        for( var i = 0; i < children.Count; i++ ) {
            var child = children[ i ]!;
            var boneA     = int.Parse( child.SelectSingleNode( "hkparam[@name='boneA']" )?.InnerText ?? "0" );
            var boneB     = int.Parse( child.SelectSingleNode( "hkparam[@name='boneB']" )?.InnerText ?? "0" );
            var transform = ParseVec12( child.SelectSingleNode( "hkparam[@name='aFromBTransform']" )!.InnerText );

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
    
    /// <summary>Parses a vec12 from Havok XML.</summary>
    /// <param name="innerText">The inner text of the vec12 node.</param>
    /// <returns>An array of floats.</returns>
    public static float[] ParseVec12(string innerText)
    {
        // (0.000000 1.613955 0.043956)(0.707107 0.000000 0.707107 0.000000)(1.000000 1.000000 1.000000)

        var buf = new float[12];
        var floats = new List<float>();
        var matches = Regex.Matches(innerText, @"-?\d+\.\d+");
        foreach (Match match in matches)
        {
            floats.Add(float.Parse(match.Value, CultureInfo.InvariantCulture));
        }
        
        buf[0] = floats[0];
        buf[1] = floats[1];
        buf[2] = floats[2];
        
        buf[4] = floats[3];
        buf[5] = floats[4];
        buf[6] = floats[5];
        buf[7] = floats[6];
        
        buf[8] = floats[7];
        buf[9] = floats[8];
        buf[10] = floats[9];
        
        return buf;
    }
}
