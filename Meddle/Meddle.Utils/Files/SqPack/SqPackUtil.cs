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
        try
        {
            fileStream.Seek(offset, SeekOrigin.Begin);

            var header = br.Read<SqPackFileInfo>();

            switch (header.Type)
            {
                case FileType.Empty:
                {
                    var data = new byte[header.RawFileSize];
                    return new SqPackFile(header, data);
                }
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
                    var data2 = ParseModelFile(offset, header, br);
                    return new SqPackFile(header, data2);
                }
                default:
                    throw new InvalidDataException($"Unknown file type {header.Type}");
            }
        }
        finally
        {
            br.Close();
            fileStream.Close();
        }
    }

    private static ReadOnlySpan<byte> ParseStandardFile(long offset, SqPackFileInfo header, BinaryReader br)
    {
        var buffer = new byte[(int)header.RawFileSize];
        using var ms = new MemoryStream(buffer);
        if (header.NumberOfBlocks == 0)
        {
            return buffer;
        }
        var blocks = br.Read<DatStdFileBlockInfos>((int)header.NumberOfBlocks);
        foreach (var block in blocks)
        {
            var data = ReadFileBlock(offset + block.Offset + header.Size, br);
            ms.Write(data);
        }

        return buffer;
    }

    private struct ChunkInfo
    {
        public uint Size;
        public uint Length;
        public uint Offset;
        public ushort BlockStart;
        public ushort NumBlocks;
    }
    
    private static unsafe ReadOnlySpan<byte> ParseModelFile(long offset, SqPackFileInfo header, BinaryReader br)
    {
        br.BaseStream.Position = offset;
        var modelBlock = br.Read<ModelBlock>();
        
        var buffer = new byte[(int)header.RawFileSize];
        using (var ms = new MemoryStream(buffer))
        {
            // we're going to write the blocks first, then the header
            var headerSize = Unsafe.SizeOf<MdlFile.ModelFileHeader>();
            ms.Seek(headerSize, SeekOrigin.Begin);
            
            // keep note of the end of the header since all blocks are relative to this
            var blockOrigin = offset + modelBlock.Size;
                        var stackChunk = new ChunkInfo
            {
                Size = modelBlock.StackSize,
                Length = modelBlock.CompressedStackMemorySize,
                Offset = modelBlock.StackOffset,
                BlockStart = modelBlock.StackBlockIndex,
                NumBlocks = modelBlock.StackBlockNum
            };
            
            var runtimeChunk = new ChunkInfo
            {
                Size = modelBlock.RuntimeSize,
                Length = modelBlock.CompressedRuntimeMemorySize,
                Offset = modelBlock.RuntimeOffset,
                BlockStart = modelBlock.RuntimeBlockIndex,
                NumBlocks = modelBlock.RuntimeBlockNum
            };
            
            var vertexChunks = new ChunkInfo[3];
            var edgeChunks = new ChunkInfo[3];
            var indexChunks = new ChunkInfo[3];
            for (var i = 0; i < 3; i++)
            {
                vertexChunks[i] = new ChunkInfo
                {
                    Size = modelBlock.VertexBufferSize[i],
                    Length = modelBlock.CompressedVertexBufferSize[i],
                    Offset = modelBlock.VertexBufferOffset[i],
                    BlockStart = modelBlock.VertexBufferBlockIndex[i],
                    NumBlocks = modelBlock.VertexBufferBlockNum[i]
                };
                edgeChunks[i] = new ChunkInfo
                {
                    Size = modelBlock.EdgeGeometryVertexBufferSize[i],
                    Length = modelBlock.CompressedEdgeGeometryVertexBufferSize[i],
                    Offset = modelBlock.EdgeGeometryVertexBufferOffset[i],
                    BlockStart = modelBlock.EdgeGeometryVertexBufferBlockIndex[i],
                    NumBlocks = modelBlock.EdgeGeometryVertexBufferBlockNum[i]
                };
                indexChunks[i] = new ChunkInfo
                {
                    Size = modelBlock.IndexBufferSize[i],
                    Length = modelBlock.CompressedIndexBufferSize[i],
                    Offset = modelBlock.IndexBufferOffset[i],
                    BlockStart = modelBlock.IndexBufferBlockIndex[i],
                    NumBlocks = modelBlock.IndexBufferBlockNum[i]
                };
            }
            
            var totalBlocks = 
                stackChunk.NumBlocks + 
                  runtimeChunk.NumBlocks + 
                  vertexChunks.Sum(x => x.NumBlocks) + 
                  edgeChunks.Sum(x => x.NumBlocks) + 
                  indexChunks.Sum(x => x.NumBlocks);
            
            var blockSizes = br.Read<ushort>(totalBlocks);

            br.BaseStream.Seek(blockOrigin + stackChunk.Offset, SeekOrigin.Begin);
            var blockIndex = 0;
            for (var i = 0; i < stackChunk.NumBlocks; i++)
            {
                var data = ReadFileBlock(br.BaseStream.Position, br);
                ms.Write(data);
                br.BaseStream.Seek(blockSizes[blockIndex++], SeekOrigin.Current);
            }
            
            br.BaseStream.Seek(blockOrigin + runtimeChunk.Offset, SeekOrigin.Begin);
            for (var i = 0; i < runtimeChunk.NumBlocks; i++)
            {
                var data = ReadFileBlock(br.BaseStream.Position, br);
                ms.Write(data);
                br.BaseStream.Seek(blockSizes[blockIndex++], SeekOrigin.Current);
            }
            
            var vertexDataOffsets = new int[3];
            var indexDataOffsets = new int[3];
            var vertexBufferSizes = new int[3];
            var indexBufferSizes = new int[3];
            for (var i = 0; i < 3; i++)
            {
                br.BaseStream.Seek(blockOrigin + vertexChunks[i].Offset, SeekOrigin.Begin);
                vertexDataOffsets[i] = (int)ms.Position;
                for (var j = 0; j < vertexChunks[i].NumBlocks; j++)
                {
                    var data = ReadFileBlock(br.BaseStream.Position, br);
                    ms.Write(data);
                    br.BaseStream.Seek(blockSizes[blockIndex++], SeekOrigin.Current);
                    vertexBufferSizes[i] += data.Length;
                }
                
                br.BaseStream.Seek(blockOrigin + edgeChunks[i].Offset, SeekOrigin.Begin);
                for (var j = 0; j < edgeChunks[i].NumBlocks; j++)
                {
                    var data = ReadFileBlock(br.BaseStream.Position, br);
                    ms.Write(data);
                    br.BaseStream.Seek(blockSizes[blockIndex++], SeekOrigin.Current);
                }
                
                br.BaseStream.Seek(blockOrigin + indexChunks[i].Offset, SeekOrigin.Begin);
                indexDataOffsets[i] = (int)ms.Position;
                for (var j = 0; j < indexChunks[i].NumBlocks; j++)
                {
                    var data = ReadFileBlock(br.BaseStream.Position, br);
                    ms.Write(data);
                    br.BaseStream.Seek(blockSizes[blockIndex++], SeekOrigin.Current);
                    indexBufferSizes[i] += data.Length;
                }
            }

            ms.Seek(0, SeekOrigin.Begin);
            var fileHeader = new MdlFile.ModelFileHeader
            {
                Version = modelBlock.Version,
                VertexDeclarationCount = modelBlock.VertexDeclarationNum,
                StackSize = modelBlock.StackSize,
                RuntimeSize = modelBlock.RuntimeSize,
                MaterialCount = modelBlock.MaterialNum,
                LodCount = modelBlock.NumLods,
                EnableIndexBufferStreaming = modelBlock.IndexBufferStreamingEnabled,
                EnableEdgeGeometry = modelBlock.EdgeGeometryEnabled,
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
        }

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
                var buffer = new byte[blockHeader.BlockDataSize];
                var blockData = br.Read<byte>((int)blockHeader.DatBlockType);
                using (var blockMs = new MemoryStream(blockData))
                using (var deflateStream = new DeflateStream(blockMs, CompressionMode.Decompress, true))
                {
                    var boff = 0;
                    int totalRead;
                    while ((totalRead = deflateStream.Read(buffer, boff, (int)blockHeader.BlockDataSize - boff)) > 0)
                    {
                        boff += totalRead;
                        if (totalRead == blockHeader.BlockDataSize)
                        {
                            break;
                        }
                    }
                }
                
                return buffer;
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
