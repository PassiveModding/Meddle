using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meddle.Utils.Files.SqPack;

public static class SqPackUtil
{
    public static SqPackFile ReadFile(long offset, string datFilePath)
    {
        using var fileStream = File.OpenRead(datFilePath);
        using var br = new BinaryReader(fileStream);
        fileStream.Seek(offset, SeekOrigin.Begin);

        var header = br.Read<SqPackFileInfo>();

        switch (header.Type)
        {
            case FileType.Empty:
                throw new FileNotFoundException($"The file located at {datFilePath} at offset {offset} is empty");
            case FileType.Texture:
            {
                var data = ParseTexFile(offset, header, br);
                return new SqPackFile(header, data);
            }
            case FileType.Standard:
            {
                var buffer = ParseStandardFile(offset, header, br);
                return new SqPackFile(header, buffer);
            }
            case FileType.Model:
            {
                br.BaseStream.Position = offset;
                var data = ParseModelFile(offset, header, br);
                return new SqPackFile(header, data);
            }
            default:
                throw new InvalidDataException($"Unknown file type {header.Type}");
        }
    }

    private static ReadOnlySpan<byte> ParseStandardFile(long offset, SqPackFileInfo header, BinaryReader br)
    {
        var buffer = new byte[(int)header.RawFileSize];
        using var ms = new MemoryStream(buffer);
        var blocks = br.Read<DatStdFileBlockInfos>((int)header.NumberOfBlocks);
        foreach (var block in blocks)
        {
            var data = ReadFileBlock(offset + block.Offset + header.Size, br);
            ms.Write(data);
        }

        return buffer;
    }

    private static unsafe ReadOnlySpan<byte> ParseModelFile(long offset, SqPackFileInfo header, BinaryReader br)
    {
        var modelBlock = br.Read<ModelBlock>();
        var buffer = new byte[(int)header.RawFileSize];
        var ms = new MemoryStream(buffer);
        var headerSize = Unsafe.SizeOf<MdlFile.ModelFileHeader>();
        ms.Seek(headerSize, SeekOrigin.Begin);

        var origin = offset + modelBlock.Size;
        var stackBlockOffsets = br.Read<ushort>(modelBlock.StackBlockNum);
        var stackOrigin = origin + modelBlock.StackOffset;
        var stackSize = 0;
        for (var i = 0; i < modelBlock.StackBlockNum; i++)
        {
            var stackOffset = stackOrigin + (i > 0 ? stackBlockOffsets[i - 1] : 0);
            var data = ReadFileBlock(stackOffset, br);
            stackSize += data.Length;
            ms.Write(data);
        }

        var runtimeBlockOffsets = br.Read<ushort>(modelBlock.RuntimeBlockNum);
        var runtimeOrigin = origin + modelBlock.RuntimeOffset;
        var runtimeSize = 0;
        for (var i = 0; i < modelBlock.RuntimeBlockNum; i++)
        {
            var runtimeOffset = runtimeOrigin + (i > 0 ? runtimeBlockOffsets[i - 1] : 0);
            var data = ReadFileBlock(runtimeOffset, br);
            runtimeSize += data.Length;
            ms.Write(data);
        }

        var vertexDataOffsets = new int[3];
        var indexDataOffsets = new int[3];
        var vertexBufferSizes = new int[3];
        var indexBufferSizes = new int[3];

        for (var i = 0; i < 3; i++)
        {
            var vertexBufferBlockNum = modelBlock.VertexBufferBlockNum[i];
            var vertexOffsets = vertexBufferBlockNum != 0
                                    ? br.Read<ushort>(vertexBufferBlockNum)
                                    : Array.Empty<ushort>();

            var edgeGeometryVertexBufferBlockNum = modelBlock.EdgeGeometryVertexBufferBlockNum[i];
            var edgeOffsets = edgeGeometryVertexBufferBlockNum != 0
                                  ? br.Read<ushort>(edgeGeometryVertexBufferBlockNum)
                                  : Array.Empty<ushort>();

            var indexBufferBlockNum = modelBlock.IndexBufferBlockNum[i];
            var indexOffsets = indexBufferBlockNum != 0 ? br.Read<ushort>(indexBufferBlockNum) : Array.Empty<ushort>();

            var vertexOffset = origin + modelBlock.VertexBufferOffset[i];
            for (var j = 0; j < modelBlock.VertexBufferBlockNum[i]; j++)
            {
                if (j > 0)
                {
                    vertexOffset += vertexOffsets[j - 1];
                }

                var data = ReadFileBlock(vertexOffset, br);
                vertexBufferSizes[i] += data.Length;
                if (j == 0)
                {
                    vertexDataOffsets[i] = (int)ms.Position;
                }

                ms.Write(data);
            }

            var edgeOffset = origin + modelBlock.EdgeGeometryVertexBufferOffset[i];
            for (var j = 0; j < modelBlock.EdgeGeometryVertexBufferBlockNum[i]; j++)
            {
                if (j > 0)
                {
                    edgeOffset = edgeOffsets[j - 1];
                }

                var data = ReadFileBlock(edgeOffset, br);
                ms.Write(data);
            }

            var indexOffset = origin + modelBlock.IndexBufferOffset[i];
            for (var j = 0; j < modelBlock.IndexBufferBlockNum[i]; j++)
            {
                if (j > 0)
                {
                    indexOffset += indexOffsets[j - 1];
                }

                var data = ReadFileBlock(indexOffset, br);
                indexBufferSizes[i] += data.Length;
                if (j == 0)
                {
                    indexDataOffsets[i] = (int)ms.Position;
                }

                ms.Write(data);
            }
        }

        ms.Seek(0, SeekOrigin.Begin);
        var fileHeader = new MdlFile.ModelFileHeader
        {
            Version = modelBlock.Version,
            StackSize = (uint)stackSize,
            RuntimeSize = (uint)runtimeSize,
            VertexDeclarationCount = modelBlock.VertexDeclarationNum,
            MaterialCount = modelBlock.MaterialNum,
            LodCount = modelBlock.NumLods,
            EnableIndexBufferStreaming = modelBlock.IndexBufferStreamingEnabled,
            EnableEdgeGeometry = modelBlock.EdgeGeometryEnabled
        };
        
        var pVertexOffset = fileHeader.VertexOffset;
        var pIndexOffset = fileHeader.IndexOffset;
        var pVertexBufferSize = fileHeader.VertexBufferSize;
        var pIndexBufferSize = fileHeader.IndexBufferSize;
        for (var i = 0; i < 3; i++)
        {
            *pVertexOffset++ = (uint)vertexDataOffsets[i];
            *pIndexOffset++ = (uint)indexDataOffsets[i];
            *pVertexBufferSize++ = (uint)vertexBufferSizes[i];
            *pIndexBufferSize++ = (uint)indexBufferSizes[i];
        }
        
        ms.Write(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref fileHeader, 1)));
        return buffer;
    }

    private static ReadOnlySpan<byte> ReadFileBlock(long offset, BinaryReader br)
    {
        var pos = br.BaseStream.Position;
        try
        {
            br.BaseStream.Position = offset;
            var blockHeader = br.Read<DatBlockHeader>();
            if (blockHeader.DatBlockType == DatBlockType.Uncompressed)
            {
                return br.Read<byte>((int)blockHeader.BlockDataSize);
            }
            else
            {
                using var zlibStream = new DeflateStream(br.BaseStream, CompressionMode.Decompress, true);

                var ob = new byte[blockHeader.BlockDataSize];
                var totalRead = 0;
                while (totalRead < blockHeader.BlockDataSize)
                {
                    var bytesRead = zlibStream.Read(ob, totalRead, (int)blockHeader.BlockDataSize - totalRead);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    totalRead += bytesRead;
                }

                if (totalRead != (int)blockHeader.BlockDataSize)
                {
                    throw new InvalidDataException(
                        $"Failed to read block data, expected {blockHeader.BlockDataSize} bytes, got {totalRead}");
                }

                return ob;
            }
        } finally
        {
            br.BaseStream.Position = pos;
        }
    }

    private static ReadOnlySpan<byte> ParseTexFile(long offset, SqPackFileInfo header, BinaryReader br)
    {
        var buffer = new byte[(int)header.RawFileSize];
        using var ms = new MemoryStream(buffer);
        var lodBlocks = (int)header.NumberOfBlocks;
        var blocks = br.Read<LodBlock>(lodBlocks);

        var mipMapSize = blocks[0].CompressedOffset;
        if (mipMapSize != 0)
        {
            var pos = br.BaseStream.Position;
            br.BaseStream.Position = offset + header.Size;
            var mipMap = br.Read<byte>((int)mipMapSize);
            ms.Write(mipMap);
            br.BaseStream.Position = pos;
        }

        for (var i = 0; i < blocks.Length; i++)
        {
            var blockOffset = offset + header.Size + blocks[i].CompressedOffset;
            for (var j = 0; j < blocks[i].BlockCount; j++)
            {
                var data = ReadFileBlock(blockOffset, br);
                ms.Write(data);
                var size = br.ReadUInt16();
                blockOffset += size;
            }
        }

        return buffer;
    }
}
