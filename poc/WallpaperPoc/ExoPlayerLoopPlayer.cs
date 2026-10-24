using Android.Content;
using Android.OS;
using Android.Views;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer;
using Uri = Android.Net.Uri;

namespace WallpaperPoc;

/// <summary>
/// Media3 ExoPlayer 实现：正式方案的播放内核（方案 D3 决策）。
/// 原生循环 = IPlayer.RepeatModeOne；手动模式监听 STATE_ENDED 后 SeekTo(0)+Play 并上报重启耗时。
/// </summary>
public class ExoPlayerLoopPlayer : Java.Lang.Object, IVideoLoopPlayer, IPlayerListener
{
    IExoPlayer? _exo;
    bool _nativeLoop = true;
    bool _preparedNotified;

    public string Name => "ExoPlayer";

    public bool NativeLoop
    {
        get => _nativeLoop;
        set
        {
            _nativeLoop = value;
            if (_exo != null)
                _exo.RepeatMode = value ? BasePlayer.InterfaceConsts.RepeatModeOne : BasePlayer.InterfaceConsts.RepeatModeOff;
        }
    }

    public bool IsPlaying => _exo?.IsPlaying ?? false;
    public long PositionMs => _exo?.CurrentPosition ?? 0;
    public long DurationMs => _exo?.Duration ?? 0;

    public event EventHandler? Prepared;
    public event EventHandler<long>? ManualRestart;
    public event EventHandler<string>? ErrorOccurred;

    public ExoPlayerLoopPlayer(Context context)
    {
        _exo = new ExoPlayerBuilder(context).Build();
        _exo.RepeatMode = _nativeLoop ? BasePlayer.InterfaceConsts.RepeatModeOne : BasePlayer.InterfaceConsts.RepeatModeOff;
        _exo.AddListener(this);
    }

    // ----- IPlayerListener（依赖默认接口方法，仅覆写需要的回调） -----

    public void OnPlaybackStateChanged(int playbackState)
    {
        if (_exo == null) return;

        if (playbackState == BasePlayer.InterfaceConsts.StateReady && !_preparedNotified)
        {
            _preparedNotified = true;
            Prepared?.Invoke(this, EventArgs.Empty);
        }
        else if (playbackState == BasePlayer.InterfaceConsts.StateEnded && !_nativeLoop)
        {
            long t0 = SystemClock.UptimeMillis();
            _exo.SeekTo(0);
            _exo.Play();
            ManualRestart?.Invoke(this, SystemClock.UptimeMillis() - t0);
        }
    }

    public void OnPlayerError(PlaybackException? error)
    {
        ErrorOccurred?.Invoke(this, $"ExoPlayer error code={error?.ErrorCode} {error?.Message}");
    }

    // ----- IVideoLoopPlayer -----

    public void SetSurface(Surface? surface) => _exo?.SetVideoSurface(surface);

    public void SetDataSource(Context context, Uri uri)
    {
        _preparedNotified = false;
        _exo?.SetMediaItem(MediaItem.FromUri(uri));
    }

    public void Reset()
    {
        _preparedNotified = false;
        _exo?.Stop();
        _exo?.ClearMediaItems();
    }

    public void Prepare() => _exo?.Prepare();

    public void Play() => _exo?.Play();

    public void Pause() => _exo?.Pause();

    public void Release()
    {
        _exo?.RemoveListener(this);
        _exo?.Release();
        _exo = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Release();
        base.Dispose(disposing);
    }
}
