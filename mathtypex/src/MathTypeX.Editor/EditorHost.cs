using System.IO;
using System.IO.Pipes;
using System.Windows.Threading;
using MathTypeX.Interop;

namespace MathTypeX.Editor;

/// <summary>Pipe server cho add-in Word/PowerPoint (chỉ người dùng hiện tại kết nối được).</summary>
internal sealed class EditorHost
{
    private readonly Dispatcher _dispatcher;
    private readonly EditorWindow _window;

    public EditorHost(Dispatcher dispatcher, EditorWindow window)
    {
        _dispatcher = dispatcher;
        _window = window;
    }

    public void Start() => Task.Run(AcceptLoopAsync);

    private async Task AcceptLoopAsync()
    {
        string name = EditorProtocol.PipeName(Environment.UserName);
        while (true)
        {
            var server = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await server.WaitForConnectionAsync().ConfigureAwait(false);
                _ = Task.Run(() => ServeAsync(server));
            }
            catch (IOException)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                await Task.Delay(500).ConfigureAwait(false);
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream stream)
    {
        using var channel = new RpcChannel(stream);
        try
        {
            await channel.ServeAsync(HandleAsync).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Add-in đóng (Word thoát) — bình thường.
        }
    }

    private async Task<RpcEnvelope> HandleAsync(RpcEnvelope request)
    {
        switch (request.Method)
        {
            case RpcMethods.Ping:
                return new RpcEnvelope { Method = request.Method, Payload = EditorProtocol.Serialize("pong") };
            case RpcMethods.Edit:
            {
                var edit = EditorProtocol.Deserialize<EditRequest>(request.Payload ?? "{}");
                var result = await _dispatcher.InvokeAsync(() => _window.EditAsync(edit)).Task.Unwrap().ConfigureAwait(false);
                return new RpcEnvelope { Method = request.Method, Payload = EditorProtocol.Serialize(result) };
            }
            default:
                return new RpcEnvelope { Method = request.Method, Error = "Phương thức không hỗ trợ: " + request.Method };
        }
    }
}
