using System.Runtime.InteropServices;
using Meddle.Plugin.Services;
using Meddle.Utils.Export;
using Microsoft.Extensions.Logging;
using OtterTex;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Format = Vortice.DXGI.Format;
using MapFlags = Vortice.Direct3D11.MapFlags;
using Texture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;

namespace Meddle.Plugin.Utils;

public unsafe class DXHelper : IService
{
    private readonly ILogger<DXHelper> log;

    public readonly Result WasStillDrawing = new(0x887A000A);

    public DXHelper(ILogger<DXHelper> log)
    {
        this.log = log;
    }


    public (TextureResource Resource, int RowPitch) ExportTextureResource(Texture* kernelTexture)
    {
        if (kernelTexture->D3D11Texture2D == null)
            throw new ArgumentException("Texture's DX data is null");

        using var tex = new ID3D11Texture2D1((nint)kernelTexture->D3D11Texture2D);
        tex.AddRef();

        var ret = GetResourceData(tex,
                                  CloneResource,
                                  GetData);

        return ret;
    }

    /*public byte[] ExportVertexBuffer(VertexBuffer* buffer)
    {
        if (buffer->DxPtr1 == nint.Zero)
            throw new ArgumentException("Buffer's DX data is null");

        using var res = new ID3D11Buffer(buffer->DxPtr1);
        res.AddRef();

        var ret = GetResourceData(res,
                      CloneBuffer,
                  (r, map) =>
                  {
                      var ret = new byte[r.Description.ByteWidth];
                      Marshal.Copy(map.DataPointer, ret, 0, ret.Length);
                      return ret;
                  });

        return ret;
    }

    private ID3D11Buffer CloneBuffer(ID3D11Buffer r)
    {
        var desc = r.Description with
        {
            Usage = ResourceUsage.Staging, BindFlags = 0, CPUAccessFlags = CpuAccessFlags.Read, MiscFlags = 0,
        };

        return r.Device.CreateBuffer(desc);
    }*/

    private (TextureResource Resource, int RowPitch) GetData(ID3D11Texture2D1 r, MappedSubresource map)
    {
        var desc = r.Description1;
        if (desc.Format is Format.BC1_UNorm or Format.BC2_UNorm or Format.BC3_UNorm or Format.BC4_UNorm
            or Format.BC5_UNorm or Format.BC7_UNorm)
        {
            var blockHeight = Math.Max(1, (desc.Height + 3) / 4);
            if (map.RowPitch * blockHeight != map.DepthPitch)
            {
                throw new InvalidDataException(
                    $"Invalid/unknown texture size for {desc.Format}: RowPitch = {map.RowPitch}; Height = {desc.Height}; Block Height = {blockHeight}; DepthPitch = {map.DepthPitch}");
            }
        }
        else
        {
            if (map.RowPitch * desc.Height != map.DepthPitch)
            {
                throw new InvalidDataException(
                    $"Invalid/unknown texture size for {desc.Format}: RowPitch = {map.RowPitch}; Height = {desc.Height}; DepthPitch = {map.DepthPitch}");
            }
        }

        var buf = new byte[map.DepthPitch];
        Marshal.Copy(map.DataPointer, buf, 0, buf.Length);

        var bufCopy = new byte[buf.Length];
        buf.CopyTo(bufCopy, 0);

        // Vortice and OtterTex use different enums but the values *should* be the same so just cast
        var rowPitch = map.RowPitch;
        var resource = new TextureResource
        {
            Format = (DXGIFormat)desc.Format,
            Width = desc.Width,
            Height = desc.Height,
            MipLevels = desc.MipLevels,
            ArraySize = desc.ArraySize,
            Dimension = (TexDimension)r.Dimension,
            MiscFlags = (D3DResourceMiscFlags)desc.MiscFlags,
            Data = bufCopy
        };

        return (resource, rowPitch);
    }

    private ID3D11Texture2D1 CloneResource(ID3D11Texture2D1 r)
    {
        var desc = r.Description1 with
        {
            Usage = ResourceUsage.Staging, BindFlags = 0, CPUAccessFlags = CpuAccessFlags.Read, MiscFlags = 0
        };

        return r.Device.As<ID3D11Device3>().CreateTexture2D1(desc);
    }

    // It's a safer bet to clone the resource and map the clone instead.

    //private static RetT GetResourceDataUncloned<T, RetT>(T res, Func<T, T> cloneResource, Func<T, MappedSubresource, RetT> getData) where T : ID3D11Resource
    //{
    //    var code = res.Device.ImmediateContext.Map(res, 0, MapMode.Read, MapFlags.DoNotWait, out var mapInfo);

    //    if (code == Result.InvalidArg)
    //    {
    //        PluginLog.Debug($"Resource couldn't be mapped. Cloning to a new resource. {code.Description}");
    //        return GetResourceData(res, cloneResource, getData);
    //    }
    //    else if (code == WasStillDrawing)
    //    {
    //        PluginLog.Debug($"GPU was still busy and couldn't do a non-blocking map. Attempting a blocking map. {code.Description}");
    //        res.Device.ImmediateContext.Map(res, 0, MapMode.Read, MapFlags.None, out mapInfo).CheckError();
    //    }
    //    else
    //        code.CheckError();

    //    using var _unmap = new DisposeRaii(() => res.Device.ImmediateContext.Unmap(res, 0));

    //    return getData(res, mapInfo);
    //}

    // https://github.com/microsoft/graphics-driver-samples/blob/de4a2161991eda254013da6c18226f5ea06e4a9c/render-only-sample/rostest/util.cpp#L244
    private RetT GetResourceData<T, RetT>(
        T res, Func<T, T> cloneResource, Func<T, MappedSubresource, RetT> getData) where T : ID3D11Resource
    {
        using var stagingRes = cloneResource(res);

        res.Device.ImmediateContext.CopyResource(stagingRes, res);

        var code = stagingRes.Device.ImmediateContext.Map(stagingRes, 0, MapMode.Read, MapFlags.DoNotWait,
                                                          out var mapInfo);
        if (code == WasStillDrawing)
        {
            log.LogTrace($"Could not do a non-blocking map. Attempting a blocking map. {code.Description}");
            stagingRes.Device.ImmediateContext.Map(stagingRes, 0, MapMode.Read, MapFlags.None, out mapInfo)
                      .CheckError();
        }

        using var unmap = new DisposeRaii(() => stagingRes.Device.ImmediateContext.Unmap(stagingRes, 0));

        return getData(stagingRes, mapInfo);
    }

    private readonly struct DisposeRaii : IDisposable
    {
        private Action OnDispose { get; }

        public DisposeRaii(Action onDispose)
        {
            OnDispose = onDispose;
        }

        public void Dispose()
        {
            OnDispose();
        }
    }
}
