using Meddle.Plugin.Files.Utility;

namespace Meddle.Plugin.Files;

// Xande PbdFile parsing reworked to use SpanBinaryReader
public class PbdFile
{
    public readonly Header[]                          Headers;
    public readonly (int offset, Deformer deformer)[] Deformers;
    
    public PbdFile(byte[] data)
        : this((ReadOnlySpan<byte>)data)
    { }

    public PbdFile(ReadOnlySpan<byte> data)
    {
        var reader = new SpanBinaryReader(data);
        var entryCount = reader.ReadInt32();

        Headers = new Header[entryCount];
        Deformers = new (int, Deformer)[entryCount];
        
        for (var i = 0; i < entryCount; i++)
        {
            Headers[i] = reader.Read<Header>();
        }
        
        // No idea what this is
        var unkSize = entryCount * 8;
        reader.SliceFromHere(unkSize);
            
        // First deformer (101) seems... strange, just gonna skip it for now
        for (var i = 1; i < entryCount; i++)
        {
            var header = Headers[i];
            var offset = header.Offset;
            var pos = reader.Position;
            reader.Read<byte>(offset - pos);

            Deformers[i] = (offset, Deformer.Read(reader));
        }
    }

    public struct Header {
        public ushort Id;
        public ushort DeformerId;
        public int    Offset;
        public float  Unk2;
    }

    public struct Deformer
    {
        public int        BoneCount;
        public string[]   BoneNames;
        public float[]?[] DeformMatrices;
        
        public static Deformer Read(SpanBinaryReader reader)
        {
            var start = reader.Position;
            var boneCount      = reader.ReadInt32();
            //var offsetStartPos = reader.Position;

            var boneNames = new string[boneCount];
            var offsets   = reader.Read<short>(boneCount);

            for (var i = 0; i < boneCount; i++)
            {
                var offset       = offsets[i];
                var str = reader.ReadString(start + offset);
                boneNames[i] = str;
            }

            var padding = boneCount * 2 % 4;
            reader.Read<byte>(padding);

            var deformMatrices = new float[boneCount][];
            for (var i = 0; i < boneCount; i++)
            {
                var deformMatrix = reader.Read<float>(12);
                deformMatrices[i] = deformMatrix.ToArray();
            }

            return new Deformer
            {
                BoneCount      = boneCount,
                BoneNames      = boneNames,
                DeformMatrices = deformMatrices
            };
        }
        
        public float[]? GetDeformMatrix( string boneName ) {
            var boneIdx = Array.FindIndex( BoneNames, x => x == boneName );
            if (boneIdx != -1)
            {
                return DeformMatrices[ boneIdx ];
            }
            
            return null;
        }
    }
    
    public Deformer GetDeformerFromRaceCode( ushort raceCode ) {
        var header = Headers.First( h => h.Id == raceCode );
        return Deformers.First( d => d.offset == header.Offset ).deformer;
    }
}
