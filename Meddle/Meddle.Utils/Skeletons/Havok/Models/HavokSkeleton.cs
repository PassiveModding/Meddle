namespace Meddle.Utils.Skeletons.Havok.Models;

public class HavokSkeleton
{
    public HavokPartialSkeleton[] Skeletons  { get; init; }
    public HavokSkeletonMapping[]  Mappings  { get; init; }
    public int           MainSkeleton  { get; init; }
    
    /// <summary>
    /// Gets the "main" skeleton from the XML file.
    /// This assumes the skeleton represented in the animation container is the main skeleton.
    /// </summary>
    public HavokPartialSkeleton GetMainSkeleton() {
        return GetSkeletonById( MainSkeleton );
    }

    /// <summary>Gets a skeleton by its ID.</summary>
    public HavokPartialSkeleton GetSkeletonById( int id ) {
        return Skeletons.First( x => x.Id == id );
    }
}
