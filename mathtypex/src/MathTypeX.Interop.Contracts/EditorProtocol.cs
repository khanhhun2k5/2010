using System.Runtime.Serialization.Json;
using System.Text;

namespace MathTypeX.Interop;

public static class EditorProtocol
{
    public const int Version = 1;

    /// <summary>Tên pipe riêng cho từng người dùng; server tạo với PipeOptions.CurrentUserOnly.</summary>
    public static string PipeName(string userName)
    {
        var safe = new StringBuilder();
        foreach (char c in userName)
            safe.Append(char.IsLetterOrDigit(c) ? c : '_');
        return $"MathTypeX.Editor.v{Version}.{safe}";
    }

    public static string Serialize<T>(T value)
    {
        var serializer = new DataContractJsonSerializer(typeof(T));
        using var ms = new MemoryStream();
        serializer.WriteObject(ms, value);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static T Deserialize<T>(string json)
    {
        var serializer = new DataContractJsonSerializer(typeof(T));
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return (T)serializer.ReadObject(ms)!;
    }
}

/// <summary>
/// Kênh JSON theo dòng trên một stream hai chiều (named pipe). Mỗi thông điệp là một <see cref="RpcEnvelope"/>
/// trên một dòng; JSON không chứa ký tự xuống dòng thô nên ranh giới luôn rõ ràng.
/// </summary>
public sealed class RpcChannel : IDisposable
{
    private readonly Stream _stream;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public RpcChannel(Stream stream)
    {
        _stream = stream;
        var utf8 = new UTF8Encoding(false);
        _reader = new StreamReader(stream, utf8, false, 4096, leaveOpen: true);
        _writer = new StreamWriter(stream, utf8, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
    }

    public async Task SendAsync(RpcEnvelope envelope)
    {
        string line = EditorProtocol.Serialize(envelope);
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(line).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Đọc thông điệp kế tiếp; null khi đầu kia đã đóng kết nối.</summary>
    public async Task<RpcEnvelope?> ReceiveAsync()
    {
        string? line = await _reader.ReadLineAsync().ConfigureAwait(false);
        if (line is null) return null;
        return EditorProtocol.Deserialize<RpcEnvelope>(line);
    }

    /// <summary>Phía server: đọc yêu cầu, gọi handler, gửi trả lời — cho tới khi client ngắt kết nối.</summary>
    public async Task ServeAsync(Func<RpcEnvelope, Task<RpcEnvelope>> handler)
    {
        while (true)
        {
            var request = await ReceiveAsync().ConfigureAwait(false);
            if (request is null) return;
            RpcEnvelope response;
            try
            {
                response = await handler(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                response = new RpcEnvelope { Id = request.Id, Method = request.Method, Error = ex.Message };
            }
            response.Id = request.Id;
            await SendAsync(response).ConfigureAwait(false);
        }
    }

    /// <summary>Không bao giờ throw: đầu kia có thể đã đóng pipe (editor tắt ngang, Word thoát).</summary>
    public void Dispose()
    {
        foreach (var dispose in new Action[] { _writer.Dispose, _reader.Dispose, _stream.Dispose })
        {
            try
            {
                dispose();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Pipe đã hỏng — không còn gì để giải phóng.
            }
        }
        _writeLock.Dispose();
    }
}

/// <summary>Phía client (add-in): mỗi lần một yêu cầu, chờ trả lời có cùng Id.</summary>
public sealed class RpcClient : IDisposable
{
    private readonly RpcChannel _channel;
    private readonly SemaphoreSlim _callLock = new(1, 1);
    private int _nextId;

    public RpcClient(Stream stream) => _channel = new RpcChannel(stream);

    public async Task<TResult> InvokeAsync<TRequest, TResult>(string method, TRequest request)
    {
        await _callLock.WaitAsync().ConfigureAwait(false);
        try
        {
            string id = Interlocked.Increment(ref _nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await _channel.SendAsync(new RpcEnvelope { Id = id, Method = method, Payload = EditorProtocol.Serialize(request) }).ConfigureAwait(false);
            while (true)
            {
                var response = await _channel.ReceiveAsync().ConfigureAwait(false)
                    ?? throw new IOException("Editor đã ngắt kết nối.");
                if (response.Id != id) continue;
                if (response.Error is not null) throw new InvalidOperationException(response.Error);
                return EditorProtocol.Deserialize<TResult>(response.Payload ?? "null");
            }
        }
        finally
        {
            _callLock.Release();
        }
    }

    public void Dispose()
    {
        _channel.Dispose();
        _callLock.Dispose();
    }
}
