using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Interop;
using BingLan.App.Interop;
using BingLan.Core.Services;
using BingLan.Core.TopBar;

namespace BingLan.App.TopBar;

internal sealed record EverythingSearchResult(bool Available, IReadOnlyList<TopBarSearchCandidate> Candidates)
{
    internal static EverythingSearchResult Unavailable { get; } = new(false, []);
}

/// <summary>
/// Queries the running local Everything instance over its documented Unicode IPC.
/// Uses the existing window's message loop; no DLL, index, worker loop or process.
/// Protocol: https://www.voidtools.com/support/everything/sdk/ipc/
/// </summary>
internal sealed class EverythingSearchClient : IDisposable
{
    // A launcher shows only 24 results. Add on-demand IPC paging if a full browser
    // is ever required, rather than keeping Everything's full index in this app.
    internal const int MaximumCandidates = 128;
    private const int MaximumReplyBytes = 2 * 1024 * 1024;
    private const int WmCopyData = 0x004A;
    private const int QueryUnicode = 2;
    private const int RegexSearchFlag = 8;
    private static int _nextReplyId = 0x424C0000;
    private readonly HwndSource _source;
    private TaskCompletionSource<byte[]>? _pending;
    private uint _replyId;
    private bool _disposed;

    internal EverythingSearchClient(HwndSource source)
    {
        _source = source;
        source.AddHook(OnMessage);
    }

    internal async Task<EverythingSearchResult> SearchAsync(string query, CancellationToken cancellationToken, nint? serverWindow = null)
    {
        _source.Dispatcher.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed || string.IsNullOrWhiteSpace(query) || query.Length > 256)
        {
            return EverythingSearchResult.Unavailable;
        }
        var server = serverWindow ?? NativeMethods.FindWindowW("EVERYTHING_TASKBAR_NOTIFICATION", null);
        if (server == 0 && serverWindow is null)
        {
            server = NativeMethods.FindWindowW("EVERYTHING_TASKBAR_NOTIFICATION_(1.5a)", null);
        }
        if (server == 0)
        {
            return EverythingSearchResult.Unavailable;
        }

        _pending?.TrySetCanceled();
        var pending = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = pending;
        _replyId = unchecked((uint)Interlocked.Increment(ref _nextReplyId));
        var request = CreateRequest(_source.Handle, _replyId, query);
        var replyWindow = _source.Handle;
        // At most one request per debounce. Each native send is bounded; stale replies
        // carry a different ID and cannot replace the current query's results.
        _ = Task.Run(() =>
        {
            if (cancellationToken.IsCancellationRequested || !SendQuery(server, replyWindow, request))
            {
                pending.TrySetResult([]);
            }
        });
        try
        {
            var response = await pending.Task.WaitAsync(TimeSpan.FromMilliseconds(600), cancellationToken);
            return await Task.Run(() => ParseResponse(response), cancellationToken);
        }
        catch (TimeoutException)
        {
            return EverythingSearchResult.Unavailable;
        }
        finally
        {
            if (ReferenceEquals(_pending, pending))
            {
                _pending = null;
            }
        }
    }

    internal static byte[] CreateRequest(nint replyWindow, uint replyId, string query)
    {
        // Literal regex tokens prevent input such as content: from starting a file
        // content scan. Each word must occur in the filename, in any order.
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(16);
        var pattern = "^" + string.Concat(tokens.Select(token => "(?=.*" + Regex.Escape(token) + ")")) + ".*";
        var text = Encoding.Unicode.GetBytes(pattern + '\0');
        var request = new byte[20 + text.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(request, unchecked((uint)replyWindow));
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(4), replyId);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(8), RegexSearchFlag);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(16), MaximumCandidates);
        text.CopyTo(request, 20);
        return request;
    }

    private static bool SendQuery(nint server, nint replyWindow, byte[] request)
    {
        var pinned = GCHandle.Alloc(request, GCHandleType.Pinned);
        try
        {
            var data = new EverythingNativeMethods.CopyData { Id = QueryUnicode, Length = request.Length, Data = pinned.AddrOfPinnedObject() };
            return EverythingNativeMethods.SendMessageTimeout(server, WmCopyData, replyWindow, ref data,
                TopBarNativeMethods.SmtoAbortIfHung, 150, out var accepted) != 0 && accepted != 0;
        }
        finally
        {
            pinned.Free();
        }
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != WmCopyData || _pending is not { } pending || lParam == 0)
        {
            return 0;
        }
        var data = Marshal.PtrToStructure<EverythingNativeMethods.CopyData>(lParam);
        if ((nuint)data.Id != _replyId)
        {
            return 0;
        }
        handled = true;
        if (data.Length < 28 || data.Length > MaximumReplyBytes || data.Data == 0)
        {
            pending.TrySetResult([]);
            return 1;
        }
        var bytes = new byte[data.Length];
        Marshal.Copy(data.Data, bytes, 0, bytes.Length);
        pending.TrySetResult(bytes);
        return 1;
    }

    internal static EverythingSearchResult ParseResponse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 28 || bytes.Length > MaximumReplyBytes)
        {
            return EverythingSearchResult.Unavailable;
        }
        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..]);
        if (count > MaximumCandidates || 28L + count * 12L > bytes.Length)
        {
            return EverythingSearchResult.Unavailable;
        }
        var candidates = new List<TopBarSearchCandidate>((int)count);
        var stringsStart = 28 + (int)count * 12;
        for (var index = 0; index < count; index++)
        {
            var item = bytes.Slice(28 + index * 12, 12);
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(item);
            var name = ReadString(bytes, BinaryPrimitives.ReadUInt32LittleEndian(item[4..]), stringsStart);
            var directory = ReadString(bytes, BinaryPrimitives.ReadUInt32LittleEndian(item[8..]), stringsStart);
            if (name is null || directory is null || name.IndexOfAny(['\\', '/']) >= 0)
            {
                return EverythingSearchResult.Unavailable;
            }
            var path = Path.Combine(directory, name);
            if (!Path.IsPathFullyQualified(path))
            {
                continue;
            }
            candidates.Add(new TopBarSearchCandidate(
                (flags & 1) != 0 ? TopBarSearchItemKind.Folder : TopBarSearchItemKind.File,
                FileMappingService.GetDisplayName(path), name, path));
        }
        return new EverythingSearchResult(true, candidates);
    }

    private static string? ReadString(ReadOnlySpan<byte> bytes, uint offset, int stringsStart)
    {
        if (offset < stringsStart || offset > bytes.Length - 2 || (offset & 1) != 0)
        {
            return null;
        }
        var start = (int)offset;
        for (var end = start; end + 1 < bytes.Length; end += 2)
        {
            if (bytes[end] == 0 && bytes[end + 1] == 0)
            {
                return Encoding.Unicode.GetString(bytes[start..end]);
            }
        }
        return null;
    }

    public void Dispose()
    {
        _disposed = true;
        _pending?.TrySetCanceled();
        _pending = null;
        _source.RemoveHook(OnMessage);
    }

}
