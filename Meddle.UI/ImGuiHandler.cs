using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;

namespace Meddle.UI;

// stolen from https://github.com/mellinoe/ImGui.NET/blob/master/src/ImGui.NET.SampleProgram/ImGuiController.cs
public class ImGuiHandler : IDisposable {
    private readonly Sdl2Window window;
    private readonly GraphicsDevice graphicsDevice;

    private DeviceBuffer vertexBuffer = null!;
    private DeviceBuffer indexBuffer = null!;
    private DeviceBuffer projMatrixBuffer = null!;

    private Shader vertexShader = null!;
    private Shader fragmentShader = null!;

    private ResourceLayout layout = null!;
    private ResourceLayout textureLayout = null!;

    private Pipeline pipeline = null!;
    private ResourceSet mainResourceSet = null!;
    private ResourceSet fontTextureResourceSet = null!;

    private const nint FontAtlasId = 1;
    private Texture fontTexture = null!;
    private TextureView fontTextureView = null!;

    private readonly Dictionary<TextureView, (nint, ResourceSet)> setsByView = new();
    private readonly Dictionary<Texture, TextureView> autoViewsByTexture = new();
    private readonly Dictionary<nint, (nint, ResourceSet)> viewsById = new();
    private readonly List<IDisposable> ownedResources = new();
    private int lastAssignedId = 100;

    private bool frameBegun;

    public ImGuiHandler(Sdl2Window window, GraphicsDevice graphicsDevice) {
        this.window = window;
        this.graphicsDevice = graphicsDevice;

        ImGui.CreateContext();

        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.DisplayFramebufferScale = new Vector2(Services.Configuration.DisplayScale);

        io.Fonts.AddFontDefault();
        io.Fonts.Build();
        this.RecreateFontDeviceTexture();

        this.CreateDeviceResources();
        this.SetPerFrameImGuiData(1f / 60f);

        ImGui.NewFrame();
        this.frameBegun = true;
    }

    public void Update(float delta, InputSnapshot snapshot) {
        if (this.frameBegun) ImGui.Render();

        this.SetPerFrameImGuiData(delta);
        this.UpdateImGuiInput(snapshot);

        this.frameBegun = true;
        ImGui.NewFrame();
    }

    public void Render(CommandList cl) {
        if (!this.frameBegun) return;

        this.frameBegun = false;
        ImGui.Render();
        this.RenderImDrawData(ImGui.GetDrawData(), cl);
    }

    public void Dispose() {
        this.vertexBuffer.Dispose();
        this.indexBuffer.Dispose();
        this.projMatrixBuffer.Dispose();

        this.vertexShader.Dispose();
        this.fragmentShader.Dispose();

        this.layout.Dispose();
        this.textureLayout.Dispose();

        this.pipeline.Dispose();
        this.mainResourceSet.Dispose();
        this.fontTextureResourceSet.Dispose();

        this.fontTexture.Dispose();
        this.fontTextureView.Dispose();

        foreach (var res in this.ownedResources) {
            res.Dispose();
        }
    }

    // HELL FOLLOWS
    private void CreateDeviceResources() {
        var gd = this.graphicsDevice;
        var outputDescription = gd.SwapchainFramebuffer.OutputDescription;
        var factory = gd.ResourceFactory;

        this.vertexBuffer =
            factory.CreateBuffer(new BufferDescription(10000, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        this.vertexBuffer.Name = "Alpha ImGui Vertex Buffer";

        this.indexBuffer =
            factory.CreateBuffer(new BufferDescription(2000, BufferUsage.IndexBuffer | BufferUsage.Dynamic));
        this.indexBuffer.Name = "Alpha ImGui Index Buffer";

        this.RecreateFontDeviceTexture();

        this.projMatrixBuffer =
            factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        this.projMatrixBuffer.Name = "Alpha Projection Buffer";

        var vertexShaderBytes = this.LoadEmbeddedShaderCode(gd.ResourceFactory, "imgui-vertex");
        var fragmentShaderBytes = this.LoadEmbeddedShaderCode(gd.ResourceFactory, "imgui-frag");

        this.vertexShader = factory.CreateShader(
            new ShaderDescription(ShaderStages.Vertex, vertexShaderBytes,
                                  gd.BackendType == GraphicsBackend.Metal ? "VS" : "main")
        );

        this.fragmentShader = factory.CreateShader(
            new ShaderDescription(ShaderStages.Fragment, fragmentShaderBytes,
                                  gd.BackendType == GraphicsBackend.Metal ? "FS" : "main")
        );

        VertexLayoutDescription[] vertexLayouts = {
            new(
                new VertexElementDescription("in_position", VertexElementSemantic.Position, VertexElementFormat.Float2),
                new VertexElementDescription("in_texCoord", VertexElementSemantic.TextureCoordinate,
                                             VertexElementFormat.Float2),
                new VertexElementDescription("in_color", VertexElementSemantic.Color, VertexElementFormat.Byte4_Norm)
            )
        };

        this.layout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription(
                    "ProjectionMatrixBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex
                ),
                new ResourceLayoutElementDescription(
                    "MainSampler", ResourceKind.Sampler, ShaderStages.Fragment
                )
            ));


        this.textureLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(new ResourceLayoutElementDescription(
                                              "MainTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment
                                          )));

        GraphicsPipelineDescription pd = new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend,
            new DepthStencilStateDescription(false, false, ComparisonKind.Always),
            new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, false, true),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(vertexLayouts, new[] {this.vertexShader, this.fragmentShader}),
            new[] {this.layout, this.textureLayout},
            outputDescription,
            ResourceBindingModel.Default
        );
        this.pipeline = factory.CreateGraphicsPipeline(ref pd);

        this.mainResourceSet =
            factory.CreateResourceSet(new ResourceSetDescription(this.layout, this.projMatrixBuffer,
                                                                 gd.PointSampler));

        this.fontTextureResourceSet =
            factory.CreateResourceSet(new ResourceSetDescription(this.textureLayout, this.fontTextureView));
    }

    private byte[] LoadEmbeddedShaderCode(ResourceFactory factory, string name) => factory.BackendType switch {
        GraphicsBackend.Direct3D11 => this.GetEmbeddedResourceBytes($"{name}.hlsl.bytes"),
        GraphicsBackend.OpenGL => this.GetEmbeddedResourceBytes($"{name}.glsl"),
        GraphicsBackend.Vulkan => this.GetEmbeddedResourceBytes($"{name}.spv"),
        GraphicsBackend.Metal => this.GetEmbeddedResourceBytes($"{name}.metallib"),
        _ => throw new NotImplementedException()
    };

    private byte[] GetEmbeddedResourceBytes(string resourceName) {
        var assembly = Assembly.GetExecutingAssembly();
        using Stream s = assembly.GetManifestResourceStream(resourceName)!;

        var ret = new byte[s.Length];

        var readBytes = 0;
        while (readBytes < s.Length) {
            readBytes += s.Read(ret, readBytes, (int) s.Length - readBytes);
        }

        return ret;
    }

    private void RecreateFontDeviceTexture() {
        var gd = this.graphicsDevice;
        var io = ImGui.GetIO();

        io.Fonts.GetTexDataAsRGBA32(out nint pixels, out var width, out var height, out var bytesPerPixel);
        io.Fonts.SetTexID(FontAtlasId);

        this.fontTexture = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                                                                 (uint) width,
                                                                 (uint) height,
                                                                 1,
                                                                 1,
                                                                 PixelFormat.R8_G8_B8_A8_UNorm,
                                                                 TextureUsage.Sampled
                                                             ));
        this.fontTexture.Name = "Alpha Font Texture";

        gd.UpdateTexture(
            this.fontTexture,
            pixels,
            (uint) (bytesPerPixel * width * height),
            0,
            0,
            0,
            (uint) width,
            (uint) height,
            1,
            0,
            0
        );
        this.fontTextureView = gd.ResourceFactory.CreateTextureView(this.fontTexture);

        io.Fonts.ClearTexData();
    }

    private void SetPerFrameImGuiData(float deltaSeconds) {
        var io = ImGui.GetIO();
        var scale = io.DisplayFramebufferScale.X;
        io.DisplaySize = new Vector2(this.window.Width / scale, this.window.Height / scale);
        io.DeltaTime = deltaSeconds;
    }

    private void UpdateImGuiInput(InputSnapshot snapshot) {
        var io = ImGui.GetIO();
        var scale = io.DisplayFramebufferScale.X;

        io.AddMousePosEvent(snapshot.MousePosition.X / scale, snapshot.MousePosition.Y / scale);
        io.AddMouseButtonEvent(0, snapshot.IsMouseDown(MouseButton.Left));
        io.AddMouseButtonEvent(1, snapshot.IsMouseDown(MouseButton.Right));
        io.AddMouseButtonEvent(2, snapshot.IsMouseDown(MouseButton.Middle));
        io.AddMouseWheelEvent(0, snapshot.WheelDelta);

        foreach (var t in snapshot.KeyCharPresses) {
            io.AddInputCharacter(t);
        }

        foreach (var keyEvent in snapshot.KeyEvents) {
            if (this.TryMapKey(keyEvent.Key, out var imguiKey)) {
                io.AddKeyEvent(imguiKey, keyEvent.Down);
            }
        }
    }

    private void RenderImDrawData(ImDrawDataPtr drawData, CommandList cl) {
        var gd = this.graphicsDevice;

        var vertexOffsetInVertices = 0;
        var indexOffsetInElements = 0;

        if (drawData.CmdListsCount == 0) return;

        var totalVbSize = drawData.TotalVtxCount * Unsafe.SizeOf<ImDrawVert>();
        if (totalVbSize > this.vertexBuffer.SizeInBytes) {
            gd.DisposeWhenIdle(this.vertexBuffer);
            this.vertexBuffer = gd.ResourceFactory.CreateBuffer(
                new BufferDescription((uint) (totalVbSize * 1.5f), BufferUsage.VertexBuffer | BufferUsage.Dynamic)
            );
        }

        var totalIbSize = drawData.TotalIdxCount * sizeof(ushort);
        if (totalIbSize > this.indexBuffer.SizeInBytes) {
            gd.DisposeWhenIdle(this.indexBuffer);
            this.indexBuffer = gd.ResourceFactory.CreateBuffer(
                new BufferDescription((uint) (totalIbSize * 1.5f), BufferUsage.IndexBuffer | BufferUsage.Dynamic)
            );
        }

        for (var i = 0; i < drawData.CmdListsCount; i++) {
            var cmdList = drawData.CmdLists[i];

            cl.UpdateBuffer(
                this.vertexBuffer,
                (uint) (vertexOffsetInVertices * Unsafe.SizeOf<ImDrawVert>()),
                cmdList.VtxBuffer.Data,
                (uint) (cmdList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>())
            );

            cl.UpdateBuffer(
                this.indexBuffer,
                (uint) (indexOffsetInElements * sizeof(ushort)),
                cmdList.IdxBuffer.Data,
                (uint) (cmdList.IdxBuffer.Size * sizeof(ushort))
            );

            vertexOffsetInVertices += cmdList.VtxBuffer.Size;
            indexOffsetInElements += cmdList.IdxBuffer.Size;
        }

        var io = ImGui.GetIO();
        var mvp = Matrix4x4.CreateOrthographicOffCenter(
            0f,
            io.DisplaySize.X,
            io.DisplaySize.Y,
            0f,
            -1f,
            1f
        );

        this.graphicsDevice.UpdateBuffer(this.projMatrixBuffer, 0, ref mvp);

        cl.SetVertexBuffer(0, this.vertexBuffer);
        cl.SetIndexBuffer(this.indexBuffer, IndexFormat.UInt16);
        cl.SetPipeline(this.pipeline);
        cl.SetGraphicsResourceSet(0, this.mainResourceSet);

        drawData.ScaleClipRects(io.DisplayFramebufferScale);

        var vtxOffset = 0;
        var idxOffset = 0;

        for (var n = 0; n < drawData.CmdListsCount; n++) {
            var cmdList = drawData.CmdLists[n];

            for (var cmdI = 0; cmdI < cmdList.CmdBuffer.Size; cmdI++) {
                var pcmd = cmdList.CmdBuffer[cmdI];
                if (pcmd.TextureId != IntPtr.Zero) {
                    if (pcmd.TextureId == FontAtlasId) {
                        cl.SetGraphicsResourceSet(1, this.fontTextureResourceSet);
                    } else {
                        var imageResourceSet = this.GetImageResourceSet(pcmd.TextureId);
                        if (imageResourceSet != null) cl.SetGraphicsResourceSet(1, imageResourceSet);
                    }
                }

                cl.SetScissorRect(
                    0,
                    (uint) pcmd.ClipRect.X,
                    (uint) pcmd.ClipRect.Y,
                    (uint) (pcmd.ClipRect.Z - pcmd.ClipRect.X),
                    (uint) (pcmd.ClipRect.W - pcmd.ClipRect.Y)
                );

                cl.DrawIndexed(
                    pcmd.ElemCount,
                    1,
                    (uint) (pcmd.IdxOffset + idxOffset),
                    (int) (pcmd.VtxOffset + vtxOffset),
                    0
                );
            }

            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }
    }

    private bool TryMapKey(Key key, out ImGuiKey result) {
        ImGuiKey KeyToImGuiKeyShortcut(Key keyToConvert, Key startKey1, ImGuiKey startKey2) {
            int changeFromStart1 = (int) keyToConvert - (int) startKey1;
            return startKey2 + changeFromStart1;
        }

        result = key switch {
            >= Key.F1 and <= Key.F12 => KeyToImGuiKeyShortcut(key, Key.F1, ImGuiKey.F1),
            >= Key.Keypad0 and <= Key.Keypad9 => KeyToImGuiKeyShortcut(key, Key.Keypad0, ImGuiKey.Keypad0),
            >= Key.A and <= Key.Z => KeyToImGuiKeyShortcut(key, Key.A, ImGuiKey.A),
            >= Key.Number0 and <= Key.Number9 => KeyToImGuiKeyShortcut(key, Key.Number0, ImGuiKey._0),
            Key.ShiftLeft or Key.ShiftRight => ImGuiKey.ModShift,
            Key.ControlLeft or Key.ControlRight => ImGuiKey.ModCtrl,
            Key.AltLeft or Key.AltRight => ImGuiKey.ModAlt,
            Key.WinLeft or Key.WinRight => ImGuiKey.ModSuper,
            Key.Menu => ImGuiKey.Menu,
            Key.Up => ImGuiKey.UpArrow,
            Key.Down => ImGuiKey.DownArrow,
            Key.Left => ImGuiKey.LeftArrow,
            Key.Right => ImGuiKey.RightArrow,
            Key.Enter => ImGuiKey.Enter,
            Key.Escape => ImGuiKey.Escape,
            Key.Space => ImGuiKey.Space,
            Key.Tab => ImGuiKey.Tab,
            Key.BackSpace => ImGuiKey.Backspace,
            Key.Insert => ImGuiKey.Insert,
            Key.Delete => ImGuiKey.Delete,
            Key.PageUp => ImGuiKey.PageUp,
            Key.PageDown => ImGuiKey.PageDown,
            Key.Home => ImGuiKey.Home,
            Key.End => ImGuiKey.End,
            Key.CapsLock => ImGuiKey.CapsLock,
            Key.ScrollLock => ImGuiKey.ScrollLock,
            Key.PrintScreen => ImGuiKey.PrintScreen,
            Key.Pause => ImGuiKey.Pause,
            Key.NumLock => ImGuiKey.NumLock,
            Key.KeypadDivide => ImGuiKey.KeypadDivide,
            Key.KeypadMultiply => ImGuiKey.KeypadMultiply,
            Key.KeypadSubtract => ImGuiKey.KeypadSubtract,
            Key.KeypadAdd => ImGuiKey.KeypadAdd,
            Key.KeypadDecimal => ImGuiKey.KeypadDecimal,
            Key.KeypadEnter => ImGuiKey.KeypadEnter,
            Key.Tilde => ImGuiKey.GraveAccent,
            Key.Minus => ImGuiKey.Minus,
            Key.Plus => ImGuiKey.Equal,
            Key.BracketLeft => ImGuiKey.LeftBracket,
            Key.BracketRight => ImGuiKey.RightBracket,
            Key.Semicolon => ImGuiKey.Semicolon,
            Key.Quote => ImGuiKey.Apostrophe,
            Key.Comma => ImGuiKey.Comma,
            Key.Period => ImGuiKey.Period,
            Key.Slash => ImGuiKey.Slash,
            Key.BackSlash or Key.NonUSBackSlash => ImGuiKey.Backslash,
            _ => ImGuiKey.None
        };

        return result != ImGuiKey.None;
    }

    public nint GetOrCreateImGuiBinding(ResourceFactory factory, TextureView textureView) {
        if (!this.setsByView.TryGetValue(textureView, out var rsi)) {
            var resourceSet = factory.CreateResourceSet(new ResourceSetDescription(this.textureLayout, textureView));
            rsi = (this.GetNextImGuiBindingId(), resourceSet);

            this.setsByView.Add(textureView, rsi);
            this.viewsById.Add(rsi.Item1, rsi);
            this.ownedResources.Add(resourceSet);
        }

        return rsi.Item1;
    }

    public IntPtr GetOrCreateImGuiBinding(ResourceFactory factory, Texture texture) {
        if (!this.autoViewsByTexture.TryGetValue(texture, out var textureView)) {
            textureView = factory.CreateTextureView(texture);
            this.autoViewsByTexture.Add(texture, textureView);
            this.ownedResources.Add(textureView);
        }

        return this.GetOrCreateImGuiBinding(factory, textureView);
    }

    public ResourceSet? GetImageResourceSet(IntPtr imGuiBinding) {
        if (!this.viewsById.TryGetValue(imGuiBinding, out var tvi)) {
            return null;
        }

        return tvi.Item2;
    }

    private IntPtr GetNextImGuiBindingId() {
        return this.lastAssignedId++;
    }

    public void DisposeAllTextures() {
        foreach (var d in this.ownedResources) {
            d.Dispose();
        }

        this.ownedResources.Clear();
        this.autoViewsByTexture.Clear();
        this.setsByView.Clear();
        this.viewsById.Clear();

        this.lastAssignedId = 100;
    }
}
