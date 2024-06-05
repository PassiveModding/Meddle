using System.Xml;

// ReSharper disable NotAccessedField.Global
// ReSharper disable MemberCanBePrivate.Global

namespace Meddle.Utils.Havok;

public class HavokXml {
    public readonly XmlSkeleton[] Skeletons;
    public readonly XmlMapping[]  Mappings;
    public readonly int           MainSkeleton;

    /// <summary>Constructs a new HavokXml object from the given XML string.</summary>
    /// <param name="xml">The XML data.</param>
    public HavokXml( string xml ) {
        var document = new XmlDocument();
        document.LoadXml( xml );

        Skeletons = document.SelectNodes( "/hktagfile/object[@type='hkaSkeleton']" )!
            .Cast< XmlElement >()
            .Select( x => new XmlSkeleton( x ) ).ToArray();

        Mappings = document.SelectNodes( "/hktagfile/object[@type='hkaSkeletonMapper']" )!
            .Cast< XmlElement >()
            .Select( x => new XmlMapping( x ) ).ToArray();

        var animationContainer = document.SelectSingleNode( "/hktagfile/object[@type='hkaAnimationContainer']" )!;
        var animationSkeletons = animationContainer
            .SelectNodes( "array[@name='skeletons']" )!
            .Cast< XmlElement >()
            .First();

        // A recurring theme in Havok XML is that IDs start with a hash
        // If you see a string[1..], that's probably what it is
        var mainSkeleton = animationSkeletons.ChildNodes[ 0 ]!.InnerText;
        MainSkeleton = int.Parse( mainSkeleton[ 1.. ] );
    }

    /// <summary>
    /// Gets the "main" skeleton from the XML file.
    /// This assumes the skeleton represented in the animation container is the main skeleton.
    /// </summary>
    public XmlSkeleton GetMainSkeleton() {
        return GetSkeletonById( MainSkeleton );
    }

    /// <summary>Gets a skeleton by its ID.</summary>
    public XmlSkeleton GetSkeletonById( int id ) {
        return Skeletons.First( x => x.Id == id );
    }
}
