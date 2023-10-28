using System.Numerics;
using Meddle.Lumina.Models;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using Xande.Files;
using Xande.Models.Export;

namespace Meddle.Xande;

public class MeshBuilder {
    private readonly Mesh                 _mesh;
    private readonly List< object >       _geometryParamCache  = new();
    private readonly List< object >       _materialParamCache  = new();
    private readonly List< (int, float) > _skinningParamCache  = new();
    private readonly object[]             _vertexBuilderParams = new object[3];

    private readonly IReadOnlyDictionary< int, int > _jointMap;
    private readonly MaterialBuilder                 _materialBuilder;
    private readonly RaceDeformer                    _raceDeformer;

    private readonly Type _geometryT;
    private readonly Type _materialT;
    private readonly Type _skinningT;
    private readonly Type _vertexBuilderT;
    private readonly Type _meshBuilderT;

    private List< PbdFile.Deformer > _deformers = new();

    private readonly List< IVertexBuilder > _vertices;

    public MeshBuilder(
        Mesh mesh,
        bool useSkinning,
        IReadOnlyDictionary< int, int > jointMap,
        MaterialBuilder materialBuilder,
        RaceDeformer raceDeformer
    ) {
        _mesh            = mesh;
        _jointMap        = jointMap;
        _materialBuilder = materialBuilder;
        _raceDeformer    = raceDeformer;

        _geometryT      = GetVertexGeometryType( _mesh.Vertices );
        _materialT      = GetVertexMaterialType( _mesh.Vertices );
        _skinningT      = useSkinning ? typeof( VertexJoints4 ) : typeof( VertexEmpty );
        _vertexBuilderT = typeof( VertexBuilder< ,, > ).MakeGenericType( _geometryT, _materialT, _skinningT );
        _meshBuilderT   = typeof( MeshBuilder< ,,, > ).MakeGenericType( typeof( MaterialBuilder ), _geometryT, _materialT, _skinningT );
        _vertices       = new List< IVertexBuilder >( _mesh.Vertices.Length );
    }

    /// <summary>Calculates the deformation steps from two given races.</summary>
    /// <param name="from">The current race of the mesh.</param>
    /// <param name="to">The target race of the mesh.</param>
    public void SetupDeformSteps( ushort from, ushort to ) {
        // Nothing to do
        if( from == to ) return;

        var     deformSteps = new List< ushort >();
        ushort? current     = to;

        while( current != null ) {
            deformSteps.Add( current.Value );
            current = _raceDeformer.GetParent( current.Value );
            if( current == from ) break;
        }

        // Reverse it to the right order
        deformSteps.Reverse();

        // Turn these into deformers
        var pbd       = _raceDeformer.PbdFile;
        var deformers = new PbdFile.Deformer[deformSteps.Count];
        for( var i = 0; i < deformSteps.Count; i++ ) {
            var raceCode = deformSteps[ i ];
            var deformer = pbd.GetDeformerFromRaceCode( raceCode );
            deformers[ i ] = deformer;
        }

        _deformers = deformers.ToList();
    }

    /// <summary>Builds the vertices. This must be called before building meshes.</summary>
    public void BuildVertices() {
        _vertices.Clear();
        _vertices.AddRange( _mesh.Vertices.Select( BuildVertex ) );
    }

    /// <summary>Creates a mesh from the given submesh.</summary>
    public IMeshBuilder< MaterialBuilder > BuildSubmesh( Submesh submesh ) {
        var ret       = ( IMeshBuilder< MaterialBuilder > )Activator.CreateInstance( _meshBuilderT, string.Empty )!;
        var primitive = ret.UsePrimitive( _materialBuilder );

        for( var triIdx = 0; triIdx < submesh.IndexNum; triIdx += 3 ) {
            var triA = _vertices[ _mesh.Indices[ triIdx + ( int )submesh.IndexOffset + 0 ] ];
            var triB = _vertices[ _mesh.Indices[ triIdx + ( int )submesh.IndexOffset + 1 ] ];
            var triC = _vertices[ _mesh.Indices[ triIdx + ( int )submesh.IndexOffset + 2 ] ];
            primitive.AddTriangle( triA, triB, triC );
        }

        return ret;
    }

    /// <summary>Creates a mesh from the entire mesh.</summary>
    public IMeshBuilder< MaterialBuilder > BuildMesh() {
        var ret       = ( IMeshBuilder< MaterialBuilder > )Activator.CreateInstance( _meshBuilderT, string.Empty )!;
        var primitive = ret.UsePrimitive( _materialBuilder );

        for( var triIdx = 0; triIdx < _mesh.Indices.Length; triIdx += 3 ) {
            var triA = _vertices[ _mesh.Indices[ triIdx + 0 ] ];
            var triB = _vertices[ _mesh.Indices[ triIdx + 1 ] ];
            var triC = _vertices[ _mesh.Indices[ triIdx + 2 ] ];
            primitive.AddTriangle( triA, triB, triC );
        }

        return ret;
    }

    /// <summary>Builds shape keys (known as morph targets in glTF).</summary>
    public void BuildShapes( IReadOnlyList< Shape > shapes, IMeshBuilder< MaterialBuilder > builder, int subMeshStart, int subMeshEnd ) {
        var primitive  = builder.Primitives.First();
        var triangles  = primitive.Triangles;
        var vertices   = primitive.Vertices;
        var vertexList = new List< (IVertexGeometry, IVertexGeometry) >();
        var nameList   = new List< Shape >();
        for( var i = 0; i < shapes.Count; ++i ) {
            var shape = shapes[ i ];
            vertexList.Clear();
            foreach( var shapeMesh in shape.Meshes.Where( m => m.AssociatedMesh == _mesh ) ) {
                foreach( var (baseIdx, otherIdx) in shapeMesh.Values ) {
                    if( baseIdx < subMeshStart || baseIdx >= subMeshEnd ) continue; // different submesh?
                    var triIdx    = ( baseIdx - subMeshStart ) / 3;
                    var vertexIdx = ( baseIdx - subMeshStart ) % 3;

                    var triA = triangles[ triIdx ];
                    var vertexA = vertices[ vertexIdx switch {
                        0 => triA.A,
                        1 => triA.B,
                        _ => triA.C,
                    } ];

                    vertexList.Add( ( vertexA.GetGeometry(), _vertices[ otherIdx ].GetGeometry() ) );
                }
            }

            if( vertexList.Count == 0 ) continue;

            var morph = builder.UseMorphTarget( nameList.Count );
            foreach( var (a, b) in vertexList ) { morph.SetVertex( a, b ); }

            nameList.Add( shape );
        }

        var data = new ExtraDataManager();
        data.AddShapeNames( nameList );
        builder.Extras = data.Serialize();
    }

    private IVertexBuilder BuildVertex( Vertex vertex ) {
        ClearCaches();

        var skinningIsEmpty = _skinningT == typeof( VertexEmpty );
        if( !skinningIsEmpty ) {
            for( var k = 0; k < 4; k++ ) {
                var boneIndex       = vertex.BlendIndices[ k ];
                if (_jointMap == null || !_jointMap.ContainsKey( boneIndex )) continue;
                var mappedBoneIndex = _jointMap[ boneIndex ];
                var boneWeight      = vertex.BlendWeights != null ? vertex.BlendWeights.Value[ k ] : 0;

                var binding = ( mappedBoneIndex, boneWeight );
                _skinningParamCache.Add( binding );
            }
        }

        var origPos    = ToVec3( vertex.Position!.Value );
        var currentPos = origPos;

        if( _deformers.Count > 0 ) {
            foreach( var deformer in _deformers ) {
                var deformedPos = Vector3.Zero;

                foreach( var (idx, weight) in _skinningParamCache ) {
                    if( weight == 0 ) continue;

                    var deformPos                       = _raceDeformer.DeformVertex( deformer, idx, currentPos );
                    if( deformPos != null ) deformedPos += deformPos.Value * weight;
                }

                currentPos = deformedPos;
            }
        }

        _geometryParamCache.Add( currentPos );

        // Means it's either VertexPositionNormal or VertexPositionNormalTangent; both have Normal
        if( _geometryT != typeof( VertexPosition ) ) _geometryParamCache.Add( vertex.Normal!.Value );

        // Tangent W should be 1 or -1, but sometimes XIV has their -1 as 0?
        if( _geometryT == typeof( VertexPositionNormalTangent ) ) {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            _geometryParamCache.Add( vertex.Tangent1!.Value with { W = vertex.Tangent1.Value.W == 1 ? 1 : -1 } );
        }

// AKA: Has "TextureN" component
        if( _materialT != typeof( VertexColor1 ) ) _materialParamCache.Add( ToVec2( vertex.UV!.Value ) );

// AKA: Has "Color1" component
//if( _materialT != typeof( VertexTexture1 ) ) _materialParamCache.Insert( 0, vertex.Color!.Value );
        if( _materialT != typeof( VertexTexture1 ) ) _materialParamCache.Insert( 0, new Vector4( 255, 255, 255, 255 ) );


        _vertexBuilderParams[ 0 ] = Activator.CreateInstance( _geometryT, _geometryParamCache.ToArray() )!;
        _vertexBuilderParams[ 1 ] = Activator.CreateInstance( _materialT, _materialParamCache.ToArray() )!;
        _vertexBuilderParams[ 2 ] = skinningIsEmpty
                                        ? Activator.CreateInstance( _skinningT )!
                                        : Activator.CreateInstance( _skinningT, _skinningParamCache.ToArray() )!;

        return ( IVertexBuilder )Activator.CreateInstance( _vertexBuilderT, _vertexBuilderParams )!;
    }

    private void ClearCaches() {
        _geometryParamCache.Clear();
        _materialParamCache.Clear();
        _skinningParamCache.Clear();
    }

    /// <summary>Obtain the correct geometry type for a given set of vertices.</summary>
    private static Type GetVertexGeometryType( Vertex[] vertex )
    {
        if (vertex.Length == 0)
        {
            return typeof(VertexPosition);
        }
        
        if (vertex[0].Tangent1 != null)
        {
            return typeof(VertexPositionNormalTangent);
        }
        
        if (vertex[0].Normal != null)
        {
            return typeof(VertexPositionNormal);
        }
        
        return typeof(VertexPosition);
    }

    /// <summary>Obtain the correct material type for a set of vertices.</summary>
    private static Type GetVertexMaterialType( Vertex[] vertex ) {
        if (vertex.Length == 0)
        {
            return typeof(VertexColor1);
        }
        
        var hasColor = vertex[ 0 ].Color != null;
        var hasUv    = vertex[ 0 ].UV != null;

        return hasColor switch {
            true when hasUv  => typeof( VertexColor1Texture1 ),
            false when hasUv => typeof( VertexTexture1 ),
            _                => typeof( VertexColor1 ),
        };
    }

    private static Vector3 ToVec3( Vector4 v ) => new(v.X, v.Y, v.Z);
    private static Vector2 ToVec2( Vector4 v ) => new(v.X, v.Y);
}