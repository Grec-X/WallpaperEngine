using Android.Content;
using Android.Media;
using Android.OS;
using Android.Views;
using Uri = Android.Net.Uri;

namespace WallpaperEngine.Player;

/// <summary>
/// 系统 MediaPlayer 实现：ExoPlayer 不可用时的备选内核（阶段二真机验证通过）。
/// </summary>
public class MediaPlayerLoopPlayer : Java.Lang.Object, IVideoLoopPlayer
{
    MediaPlayer? _mp;
    bool _nativeLoop = true;
    bool _prepared;

    public string Name => "MediaPlayer";

    public bool NativeLoop
    {
        get => _nativeLoop;
        set
        {
            _nativeLoop = value;
            if (_mp != null) _mp.Looping = value;
        }
    }

    public bool IsPlaying => _mp?.IsPlaying ?? false;
    public long PositionMs => _mp?.CurrentPosition ?? 0;
    public long DurationMs => _prepared ? (_mp?.Duration ?? 0) : 0;

    /// <summary>备选内核不支持裁剪（属性仅占位，保存后不生效）</summary>
    public long TrimStartMs { get; set; }

    /// <summary>备选内核不支持裁剪（属性仅占位，保存后不生效）</summary>
    public long TrimEndMs { get; set; }

    public event EventHandler? Prepared;
    public event EventHandler<long>? ManualRestart;
    public event EventHandler<string>? ErrorOccurred;

    public MediaPlayerLoopPlayer()
    {
        CreatePlayer();
    }

    void CreatePlayer()
    {
        _mp = new MediaPlayer();
        _mp.Looping = _nativeLoop;
        _mp.Prepared += (_, _) =>
        {
            _prepared = true;
            Prepared?.Invoke(this, EventArgs.Empty);
        };
        _mp.Completion += (_, _) =>
        {
            if (_nativeLoop) return;
            long t0 = SystemClock.UptimeMillis();
            _mp!.SeekTo(0);
            _mp.Start();
            ManualRestart?.Invoke(this, SystemClock.UptimeMillis() - t0);
        };
        _mp.Error += (_, e) =>
        {
            ErrorOccurred?.Invoke(this, $"MediaPlayer error what={e.What} extra={e.Extra}");
            e.Handled = false;
        };
    }

    public void SetSurface(Surface? surface)
    {
        if (_mp == null) return;
        _mp.SetSurface(surface);
    }

    public void SetDataSource(Context context, Uri uri)
    {
        _mp?.Reset();
        _prepared = false;
        _mp?.SetDataSource(context, uri);
    }

    public void Reset()
    {
        _mp?.Reset();
        _prepared = false;
    }

    public void Prepare() => _mp?.PrepareAsync();

    public void Play() => _mp?.Start();

    public void Pause() => _mp?.Pause();

    public void SetVolume(float volume)
    {
        _mp?.SetVolume(volume, volume);
    }

    public void SetScaleMode(bool cover)
    {
        _mp?.SetVideoScalingMode(cover ? VideoScalingMode.ScaleToFitWithCropping : VideoScalingMode.ScaleToFit);
    }

    public void Release()
    {
        _mp?.Release();
        _mp = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Release();
        base.Dispose(disposing);
    }
}
