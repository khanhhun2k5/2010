using System.IO.Pipes;

namespace MathTypeX.Editing.Tests;

/// <summary>Giao thức add-in ↔ editor: JSON theo dòng qua named pipe, dùng DataContractJsonSerializer ở cả hai phía.</summary>
public class ProtocolTests
{
    [Fact]
    public void EditRequestRoundTrips()
    {
        var request = new EditRequest
        {
            Latex = "x^2 \\text{\"có\"}\n",
            Display = true,
            FontSizePt = 13,
            Caret = new ScreenRect { Left = 10, Top = 20, Width = 1, Height = 18 },
            OwnerWindow = 0x1234,
            Notice = null,
        };
        string json = EditorProtocol.Serialize(request);
        Assert.DoesNotContain('\n', json); // một thông điệp = một dòng
        var back = EditorProtocol.Deserialize<EditRequest>(json);
        Assert.Equal(request.Latex, back.Latex);
        Assert.Equal(13, back.FontSizePt);
        Assert.Equal(18, back.Caret!.Height);
        Assert.Equal(0x1234, back.OwnerWindow);
        Assert.Null(back.Notice);
    }

    [Fact]
    public void PipeNameIsSanitized()
    {
        Assert.Equal("MathTypeX.Editor.v1.Nguy_n_V_n_A", EditorProtocol.PipeName("Nguyễn Văn A").Replace("ễ", "_").Replace("ă", "_"));
        Assert.DoesNotContain(' ', EditorProtocol.PipeName("a b"));
    }

    [Fact]
    public async Task RequestResponseOverNamedPipe()
    {
        string name = "mtx-test-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(TestContext.Current.CancellationToken);
            using var channel = new RpcChannel(server);
            await channel.ServeAsync(req =>
            {
                var edit = EditorProtocol.Deserialize<EditRequest>(req.Payload!);
                var outcome = EquationComposer.Compose(edit.Latex, new ComposeOptions { Display = edit.Display });
                return Task.FromResult(new RpcEnvelope { Method = req.Method, Payload = EditorProtocol.Serialize(outcome.Result!) });
            });
        }, TestContext.Current.CancellationToken);

        using var clientStream = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await clientStream.ConnectAsync(5000, TestContext.Current.CancellationToken);
        using var client = new RpcClient(clientStream);
        var first = await client.InvokeAsync<EditRequest, EditResult>(RpcMethods.Edit, new EditRequest { Latex = @"\sqrt{2}" });
        var second = await client.InvokeAsync<EditRequest, EditResult>(RpcMethods.Edit, new EditRequest { Latex = @"\alpha+\beta", Display = true });

        Assert.Contains("m:rad", first.FlatOpc);
        Assert.True(second.Display);
        Assert.Equal(@"\alpha+\beta", second.NormalizedLatex);

        clientStream.Dispose();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ServerErrorsAreReturnedAsExceptions()
    {
        string name = "mtx-test-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(TestContext.Current.CancellationToken);
            using var channel = new RpcChannel(server);
            await channel.ServeAsync(_ => throw new InvalidOperationException("hỏng"));
        }, TestContext.Current.CancellationToken);
        using var clientStream = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await clientStream.ConnectAsync(5000, TestContext.Current.CancellationToken);
        using var client = new RpcClient(clientStream);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.InvokeAsync<string, string>(RpcMethods.Ping, "x"));
        Assert.Equal("hỏng", ex.Message);
        clientStream.Dispose();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}
