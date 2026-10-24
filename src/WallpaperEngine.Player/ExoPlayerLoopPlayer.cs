using Android.Content;
using Android.Media;
using Android.OS;
using Android.Views;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer;
using Uri = Android.Net.Uri;

namespace WallpaperEngine.Player;

/// <summary>
/// Media3 ExoPlayer 实现：V1 正式内核（方案 D3/D4，阶段二真机验证通过）。
/// 原生循环 = BasePlayer.InterfaceConsts.RepeatModeOne。
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

    public long TrimStartMs { get; set; }
    public long TrimEndMs { get; set; }

    public event EventHandler? Prepared;
    public event EventHandler<long>? ManualRestart;
    public event EventHandler<string>? ErrorOccurred;

    public ExoPlayerLoopPlayer(Context context)
    {
        _exo = new ExoPlayerBuilder(context).Build();
        _exo.RepeatMode = _nativeLoop ? BasePlayer.InterfaceConsts.RepeatModeOne : BasePlayer.InterfaceConsts.RepeatModeOff;
        _exo.VideoScalingMode = (int)VideoScalingMode.ScaleToFitWithCropping; // 默认 Cover，保持比例
        _exo.AddListener(this);
    }

    // ----- IPlayerListener（默认接口方法，仅覆写需要的回调） -----

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
        var builder = new MediaItem.Builder().SetUri(uri);

        if (TrimStartMs > 0 || TrimEndMs > 0)
        {
            // 循环区间裁剪：与 RepeatModeOne 组合 = 只在选定片段内循环
            var clip = new MediaItem.ClippingConfiguration.Builder()
                .SetStartPositionMs(TrimStartMs);
            if (TrimEndMs > TrimStartMs) clip = clip.SetEndPositionMs(TrimEndMs);
            builder.SetClippingConfiguration(clip.Build());
        }

        _exo?.SetMediaItem(builder.Build());
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

    public void SetVolume(float volume) { if (_exo != null) _exo.Volume = volume; }

    public void SetScaleMode(bool cover)
    {
        if (_exo != null)
            _exo.VideoScalingMode = (int)(cover ? VideoScalingMode.ScaleToFitWithCropping : VideoScalingMode.ScaleToFit);
    }

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
