using System.Buffers;
using System.Runtime.InteropServices;
using ImGuiNET;
using Meddle.Utils;
using Meddle.Utils.Files;
using OtterTex;
using Veldrid;

namespace Meddle.UI;

public class ImageHandler : IDisposable
{
    private readonly List<IDisposable> disposables = new();
    private readonly List<nint> pointers = new();
    
    public nint DrawTexData(Image img, out bool cleanup)
    {
        cleanup = false;
        if (pointers.Count > 100)
        {
            foreach (var pointer in pointers)
            {
                Marshal.FreeHGlobal(pointer);
            }

            pointers.Clear();
            cleanup = true;
        }
        
        var desc = TextureDescription.Texture2D(
            (uint)img.Width,
            (uint)img.Height,
            mipLevels: 1,
            arrayLayers: 1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Sampled
        );
        
        Texture texture = Services.GraphicsDevice.ResourceFactory.CreateTexture(desc);
        var dpt = Marshal.AllocHGlobal(img.Span.Length);
        Marshal.Copy(img.Span.ToArray(), 0, dpt, img.Span.Length);

        Services.GraphicsDevice.UpdateTexture(
            texture,
            dpt,
            (uint)img.Span.Length,
            0,
            0,
            0,
            desc.Width,
            desc.Height,
            1,
            0,
            0
        );

        var binding = Services.ImGuiHandler.GetOrCreateImGuiBinding(Services.GraphicsDevice.ResourceFactory, texture);

        disposables.Add(texture);
        pointers.Add(dpt);

        return binding;
    }

    public void Dispose()
    {
        Services.ImGuiHandler.DisposeAllTextures();
        foreach (var disposable in this.disposables)
        {
            disposable.Dispose();
        }

        foreach (var pointer in pointers)
        {
            Marshal.FreeHGlobal(pointer);
        }
    }
}
