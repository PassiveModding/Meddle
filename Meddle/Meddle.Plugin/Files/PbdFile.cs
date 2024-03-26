using System.Runtime.InteropServices;
using Xande;

namespace Meddle.Plugin.Files;

/// <summary>Parses a human.pbd file to deform models. This file is located at <c>chara/xls/boneDeformer/human.pbd</c> in the game's data files.</summary>
public class PbdFile
{
    public readonly Header[]                  Headers;
    public readonly Link[]                    Links;     // header.deformerId -> link
    public readonly Dictionary<int, Deformer> Deformers; // offset -> deformer

    public PbdFile(byte[] data) : this((ReadOnlySpan<byte>)data) { }

    public PbdFile(ReadOnlySpan<byte> data)
    {
        var reader = new SpanBinaryReader(data);
        var entryCount = reader.ReadInt32();

        Headers = reader.Read<Header>(entryCount).ToArray();
        Links = reader.Read<Link>(entryCount).ToArray();
        Deformers = new Dictionary<int, Deformer>(entryCount);
        
        for (var i = 0; i < entryCount; i++)
        {
            var header = Headers[i];
            var offset = header.Offset;
            if (offset != 0)
            {
                reader.Seek(offset, SeekOrigin.Begin);
                var deformer = Deformer.Read(reader);
                Deformers[offset] = deformer;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Header {
        public ushort Id;
        public ushort DeformerId;
        public int    Offset;
        public float  Unk2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Link
    {
        // ushort.MaxValue = null
        public ushort ParentLinkIdx;
        public ushort FirstChildLinkIdx;
        public ushort NextSiblingLinkIdx;
        public ushort HeaderIdx;
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
                var matrix = reader.Read<float>(12);
                deformMatrices[i] = matrix.ToArray();
            }

            return new Deformer
            {
                BoneCount      = boneCount,
                BoneNames      = boneNames,
                DeformMatrices = deformMatrices,
            };
        }
    }

    public IEnumerable<Deformer> GetDeformers(ushort from, ushort to)
    {
        if (from == to)
            return Array.Empty<Deformer>();

        var deformerList = new List<Deformer>();
        var currentRaceCode = to;
        do
        {
            // raceCode (header) -> link -> parentLink -> raceCode (parentHeader)
            var header = Headers.First(h => h.Id == currentRaceCode);
            if (!Deformers.TryGetValue(header.Offset, out var deformer))
            {
                throw new InvalidOperationException($"Deformer does not exist for {currentRaceCode}");
            }

            deformerList.Add(deformer);

            var link = Links[header.DeformerId];
            if (link.ParentLinkIdx == ushort.MaxValue)
            {
                throw new InvalidOperationException($"Parent link does not exist for {currentRaceCode}");
            }
            
            var parentLink = Links[link.ParentLinkIdx];
            var parentHeader = Headers[parentLink.HeaderIdx];
            currentRaceCode = parentHeader.Id;
        }
        while (currentRaceCode != from);
        
        deformerList.Reverse();
        
        return deformerList;
    }
}
