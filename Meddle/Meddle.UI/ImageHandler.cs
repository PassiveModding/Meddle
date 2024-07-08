using System.Runtime.InteropServices;
using OtterTex;
using Veldrid;

namespace Meddle.UI;

public class ImageHandler : IDisposable
{
    private readonly GraphicsDevice graphicsDevice;
    private readonly ImGuiHandler imGuiHandler;
    public ImageHandler(GraphicsDevice graphicsDevice, ImGuiHandler imGuiHandler)
    {
        this.graphicsDevice = graphicsDevice;
        this.imGuiHandler = imGuiHandler;
    }
    
    private readonly List<IDisposable> disposables = [];
    private readonly List<nint> pointers = new();

    public nint DrawTexData(Image img)
    {
        var desc = TextureDescription.Texture2D(
            (uint)img.Width,
            (uint)img.Height,
            mipLevels: 1,
            arrayLayers: 1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Sampled
        );
        
        Texture texture = graphicsDevice.ResourceFactory.CreateTexture(desc);
        var dpt = Marshal.AllocHGlobal(img.Span.Length);
        Marshal.Copy(img.Span.ToArray(), 0, dpt, img.Span.Length);

        graphicsDevice.UpdateTexture(
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

        var binding = imGuiHandler.GetOrCreateImGuiBinding(graphicsDevice.ResourceFactory, texture);

        disposables.Add(texture);
        pointers.Add(dpt);

        return binding;
    }

    public void Dispose()
    {
        imGuiHandler.DisposeAllTextures();
        foreach (var disposable in this.disposables)
        {
            disposable.Dispose();
        }

        foreach (var pointer in pointers)
        {
            Marshal.FreeHGlobal(pointer);
        }
        
        pointers.Clear();
    }
}
