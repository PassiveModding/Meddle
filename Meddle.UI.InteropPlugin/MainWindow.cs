using System.Text;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Meddle.UI.InteropPlugin;

public class MainWindow(string name, ImGuiWindowFlags flags = ImGuiWindowFlags.None, bool forceMainWindow = false)
    : Window(name, flags, forceMainWindow), IDisposable
{
    private int port = 5000;
    private HttpServer? httpServer;
    public override void Draw()
    {
        ImGui.Text("Hello, world!");
        ImGui.InputInt("Port", ref port);
        
        if (ImGui.Button("Open websocket"))
        {
            ConfigureHttpServer();
        }

        if (httpServer?.IsListening == true)
        {
            ImGui.Text("Server is running");
        }
        else
        {
            ImGui.Text("Server is not running");
        }
        
        if (ImGui.Button("Close websocket"))
        {
            httpServer?.Stop();
            httpServer = null;
        }
    }
    
    private void ConfigureHttpServer()
    {
        httpServer = new HttpServer(port);
        
        httpServer.OnPost += HandlePostRequest;
        
        httpServer.Start();
        Plugin.Services.Log.Info($"Http server started on port {port}");
    }

    private async Task HandlePostRequestAsync(object? sender, HttpRequestEventArgs e)
    {
        try
        {
            Plugin.Services.Log.Info($"Received request: {e.Request.RawUrl}");
            if (e.Request.RawUrl == "/parsesklb")
            {
                var bytes = new List<byte>();
                var content = e.Request.InputStream;
                while (true)
                {
                    var b = content.ReadByte();
                    if (b == -1)
                        break;
                    bytes.Add((byte)b);
                }
                
                var tempPath = Encoding.UTF8.GetString(bytes.ToArray());
                Plugin.Services.Log.Info($"Received file: {tempPath}");

                var resultXml = await Plugin.Services.Framework.RunOnTick(() =>
                {
                    var xml = HkUtil.HkxToXml(tempPath);
                    return xml;
                });

                e.Response.StatusCode = 200;
                var responseBytes = Encoding.UTF8.GetBytes(resultXml);
                e.Response.ContentType = "text/xml";
                e.Response.ContentEncoding = Encoding.UTF8;
                e.Response.WriteContent(responseBytes);
            }
            else
            {
                e.Response.StatusCode = 404;
                var responseBytes = "Not Found"u8.ToArray();
                e.Response.WriteContent(responseBytes);
            }
        }
        catch (Exception ex)
        {
            Plugin.Services.Log.Error(ex, "Failed to handle request");
        }
    }
    
    private void HandlePostRequest(object? sender, HttpRequestEventArgs e)
    {
        HandlePostRequestAsync(sender, e).Wait();
        e.Response.Close();
    }

    public void Dispose()
    {
        httpServer?.Stop();
        Plugin.Services.Log.Info($"Http server stopped on port {port}");
    }
}
