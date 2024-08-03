using System.Numerics;
using System.Runtime.InteropServices;
using FFXIVClientStructs.Havok.Common.Base.Math.QsTransform;
using FFXIVClientStructs.Havok.Common.Base.Math.Quaternion;
using FFXIVClientStructs.Havok.Common.Base.Math.Vector;

namespace Meddle.Utils.Files;

/// <summary>Parses a human.pbd file to deform models. This file is located at <c>chara/xls/boneDeformer/human.pbd</c> in the game's data files.</summary>
public class PbdFile
{
    public readonly Header[] Headers;
    public readonly Link[] Links;                        // header.deformerId -> link
    public readonly Dictionary<int, Deformer> Deformers; // offset -> deformer
    private readonly byte[] _data;
    public ReadOnlySpan<byte> RawData => _data;

    public PbdFile(byte[] data) : this((ReadOnlySpan<byte>)data) { }

    public PbdFile(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
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
    public struct Header
    {
        public ushort Id;
        public ushort DeformerId;
        public int Offset;
        public float Unk2;
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

    /*public readonly struct DeformMatrix4x4
    {
        private readonly float[] matrix;

        public float this[int index]
        {
            get => index is >= 0 and < 16 ? matrix[index] : throw new IndexOutOfRangeException($"{index}");
            set
            {
                if (index is >= 0 and < 16) matrix[index] = value;
                else throw new IndexOutOfRangeException($"{index}");
            }
        }

        public DeformMatrix4x4(ReadOnlySpan<float> matrix) : this(matrix.ToArray()) { }

        public DeformMatrix4x4(float[] matrix)
        {
            if (matrix.Length != 16)
                throw new ArgumentException("Matrix must have 16 elements", nameof(matrix));

            this.matrix = matrix;
        }

        public static DeformMatrix4x4 Identity => new([
            0, 0, 0, 0, // Translation (vec3 + unused)
            0, 0, 0, 1, // Rotation (vec4)
            1, 1, 1, 0,  // Scale (vec3 + unused)
        ]);

        // https://github.com/TexTools/xivModdingFramework/blob/459b863fdacf291ee0817feef379a18275f33010/xivModdingFramework/Models/Helpers/ModelModifiers.cs#L1202
        public Vector3 TransformCoordinate(Vector3 vector) =>
            new(
                (vector.X * matrix[0]) + (vector.Y * matrix[1]) + (vector.Z * matrix[2]) + (1.0f * matrix[3]),
                (vector.X * matrix[4]) + (vector.Y * matrix[5]) + (vector.Z * matrix[6]) + (1.0f * matrix[7]),
                (vector.X * matrix[8]) + (vector.Y * matrix[9]) + (vector.Z * matrix[10]) + (1.0f * matrix[11])
            );
    }*/

    public struct Deformer
    {
        public int BoneCount;
        public string[] BoneNames;
        public hkQsTransformf?[] DeformMatrices;

        public static Deformer Read(SpanBinaryReader reader)
        {
            var start = reader.Position;
            var boneCount = reader.ReadInt32();

            var boneNames = new string[boneCount];
            var offsets = reader.Read<short>(boneCount);

            for (var i = 0; i < boneCount; i++)
            {
                var offset = offsets[i];
                var str = reader.ReadString(start + offset);
                boneNames[i] = str;
            }

            var padding = boneCount * 2 % 4;
            reader.Read<byte>(padding);

            var matrixArray = new hkQsTransformf?[boneCount];
            for (var i = 0; i < boneCount; i++)
            {
                var matrix = reader.Read<hkQsTransformf>();
                matrixArray[i] = matrix;
            }

            return new Deformer
            {
                BoneCount = boneCount,
                BoneNames = boneNames,
                DeformMatrices = matrixArray,
            };
        }

        public static unsafe hkQsTransformf Identity()
        {
            var buf = new float[]
            {
                0, 0, 0, 0, // Translation (vec3 + unused)
                0, 0, 0, 1, // Rotation (vec4)
                1, 1, 1, 0,  // Scale (vec3 + unused)
                0, 0, 0, 1, // Affine
            };
            
            // marshal the buffer to a struct
            fixed (float* ptr = buf)
            {
                return *(hkQsTransformf*)ptr;
            }
        }
        
        public static Vector3 TransformCoordinate(Vector3 vector, hkQsTransformf matrix) =>
            new(
                (vector.X * matrix.Translation.X) + (vector.Y * matrix.Translation.Y) + (vector.Z * matrix.Translation.Z) + (1.0f * matrix.Translation.W),
                (vector.X * matrix.Rotation.X) + (vector.Y * matrix.Rotation.Y) + (vector.Z * matrix.Rotation.Z) + (1.0f * matrix.Rotation.W),
                (vector.X * matrix.Scale.X) + (vector.Y * matrix.Scale.Y) + (vector.Z * matrix.Scale.Z) + (1.0f * matrix.Scale.W)
            );
    }

    public IEnumerable<Deformer> GetDeformers(ushort from, ushort to)
    {
        if (from == to)
            return Array.Empty<Deformer>();

        var deformerList = new List<Deformer>();
        var raceCodeList = new List<ushort>();
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
            raceCodeList.Add(currentRaceCode);

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
