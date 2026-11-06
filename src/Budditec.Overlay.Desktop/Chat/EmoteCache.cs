using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Budditec.Overlay.Desktop.Chat;

/// <summary>
/// Decoded emote frames packed into one sprite sheet (a grid of frames), so the GPU holds one texture per
/// emote rather than one per frame: small textures are padded to 64 KB each, which made per-frame bitmaps
/// cost ~20x their pixel size. Reference counted: the cache holds one reference and each EmoteImage on
/// screen holds one; the sheet is freed when the last is released.
/// </summary>
public sealed class EmoteFrames
{
    private static long _liveBytes;

    private readonly Bitmap _sheet;
    private readonly int _columns;
    private readonly IReadOnlyList<int> _durationsMs;
    private readonly int _totalMs;
    private int _refs = 1;

    public EmoteFrames(Bitmap sheet, PixelSize frameSize, int columns, IReadOnlyList<int> durationsMs)
    {
        _sheet = sheet;
        _columns = columns;
        _durationsMs = durationsMs;
        _totalMs = durationsMs.Sum();
        Size = frameSize;
        Bytes = (long)sheet.PixelSize.Width * sheet.PixelSize.Height * 4;
        Interlocked.Add(ref _liveBytes, Bytes);
    }

    /// <summary>Pixel bytes of every sheet not yet freed, cached or on screen.</summary>
    public static long LiveBytes => Interlocked.Read(ref _liveBytes);

    public PixelSize Size { get; }
    public long Bytes { get; }
    public bool IsAnimated => _durationsMs.Count > 1;
    public int FrameCount => _durationsMs.Count;
    public bool IsDisposed => Volatile.Read(ref _refs) <= 0;

    public void AddRef() => Interlocked.Increment(ref _refs);

    public void Release()
    {
        if (Interlocked.Decrement(ref _refs) != 0) return;
        _sheet.Dispose();
        Interlocked.Add(ref _liveBytes, -Bytes);
    }

    public void Draw(DrawingContext context, int index, Rect destination) =>
        context.DrawImage(_sheet,
            new Rect(index % _columns * Size.Width, index / _columns * Size.Height, Size.Width, Size.Height),
            destination);

    public int IndexAt(long elapsedMs)
    {
        if (!IsAnimated) return 0;
        var t = elapsedMs % _totalMs;
        for (var i = 0; i < _durationsMs.Count; i++)
        {
            t -= _durationsMs[i];
            if (t < 0) return i;
        }
        return _durationsMs.Count - 1;
    }
}

/// <summary>
/// Downloads and decodes emotes at the pixel height they're drawn at. Skia decodes PNG, GIF and animated
/// WebP, which covers Twitch, 7TV, BTTV and FFZ. Decoded frames are kept within a byte budget, least
/// recently used first out; LRU bookkeeping (Use/Trim) happens on the UI thread only.
/// </summary>
public sealed class EmoteCache(HttpClient http, long budgetBytes = 32 * 1024 * 1024)
{
    private readonly ConcurrentDictionary<(Uri Url, int Height), Lazy<Task<EmoteFrames?>>> _cache = new();
    private readonly LinkedList<(Uri Url, int Height)> _lru = new();
    private readonly Dictionary<(Uri Url, int Height), (LinkedListNode<(Uri, int)> Node, EmoteFrames Frames)> _used = new();
    private long _usedBytes;
    private int _usedFrames;

    /// <summary>Starts (or joins) the download and decode; safe from any thread.</summary>
    public Task<EmoteFrames?> GetAsync(Uri url, int height) =>
        _cache.GetOrAdd((url, height), key => new Lazy<Task<EmoteFrames?>>(() => LoadAsync(key.Url, key.Height))).Value;

    /// <summary>UI thread: returns a loaded emote (or null) and marks it most recently used.</summary>
    public EmoteFrames? Use(Uri url, int height)
    {
        var key = (url, height);
        if (_used.TryGetValue(key, out var entry))
        {
            _lru.Remove(entry.Node);
            _lru.AddFirst(entry.Node);
            return entry.Frames;
        }
        if (!_cache.TryGetValue(key, out var lazy) || !lazy.IsValueCreated || !lazy.Value.IsCompletedSuccessfully || lazy.Value.Result is not { } frames)
            return null;

        _used[key] = (_lru.AddFirst(key), frames);
        _usedBytes += frames.Bytes;
        _usedFrames += frames.FrameCount;
        return frames;
    }

    /// <summary>UI thread: evicts least recently used emotes over budget. On-screen copies stay alive.</summary>
    public void Trim()
    {
        while (_usedBytes > budgetBytes && _lru.Count > 1)
        {
            var key = _lru.Last!.Value;
            _lru.RemoveLast();
            var frames = _used[key].Frames;
            _used.Remove(key);
            _cache.TryRemove(key, out _);
            _usedBytes -= frames.Bytes;
            _usedFrames -= frames.FrameCount;
            frames.Release();
        }
    }

    public (int Emotes, int Frames, long Bytes) Stats => (_used.Count, _usedFrames, Interlocked.Read(ref _usedBytes));

    private async Task<EmoteFrames?> LoadAsync(Uri url, int height)
    {
        try
        {
            var bytes = await http.GetByteArrayAsync(url);
            return await Task.Run(() => Decode(bytes, height));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            System.Diagnostics.Trace.WriteLine($"[emote] {url}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Decodes every frame, scaled down to the given pixel height (never up).</summary>
    internal static EmoteFrames Decode(byte[] bytes, int height)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new InvalidOperationException("Unsupported image format");

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var target = height < info.Height
            ? info.WithSize(Math.Max(1, (int)Math.Round((double)info.Width * height / info.Height)), height)
            : info;
        var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
        var frameInfos = codec.FrameInfo;
        var count = Math.Max(1, codec.FrameCount);
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (count + columns - 1) / columns;
        using var sheet = new SKBitmap(target.WithSize(target.Width * columns, target.Height * rows));
        sheet.Erase(SKColors.Transparent);
        var fullFrames = new SKBitmap[count];
        var durations = new int[count];
        try
        {
            for (var i = 0; i < count; i++)
            {
                var frame = fullFrames[i] = new SKBitmap(info);
                var options = new SKCodecOptions(i);
                if (count > 1)
                {
                    // Frames can be deltas on top of an earlier full-size frame.
                    var required = frameInfos[i].RequiredFrame;
                    if (required >= 0)
                    {
                        CopyPixels(fullFrames[required], frame);
                        options = new SKCodecOptions(i, required);
                    }
                    durations[i] = frameInfos[i].Duration <= 10 ? 100 : frameInfos[i].Duration;
                }
                codec.GetPixels(info, frame.GetPixels(), options);

                using var scaled = target == info ? null : frame.Resize(target, sampling);
                CopyInto(scaled ?? frame, sheet, i % columns * target.Width, i / columns * target.Height);
            }
        }
        finally
        {
            foreach (var frame in fullFrames) frame?.Dispose();
        }
        var bitmap = new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, sheet.GetPixels(),
            new PixelSize(sheet.Width, sheet.Height), new Vector(96, 96), sheet.RowBytes);
        return new EmoteFrames(bitmap, new PixelSize(target.Width, target.Height), columns, durations);
    }

    private static unsafe void CopyPixels(SKBitmap from, SKBitmap to) =>
        Buffer.MemoryCopy((void*)from.GetPixels(), (void*)to.GetPixels(), to.ByteCount, from.ByteCount);

    private static unsafe void CopyInto(SKBitmap frame, SKBitmap sheet, int x, int y)
    {
        var rowBytes = frame.Width * 4;
        for (var row = 0; row < frame.Height; row++)
        {
            var from = (byte*)frame.GetPixels() + row * frame.RowBytes;
            var to = (byte*)sheet.GetPixels() + (y + row) * sheet.RowBytes + x * 4;
            Buffer.MemoryCopy(from, to, rowBytes, rowBytes);
        }
    }
}
