using System.Text;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Uri = Android.Net.Uri;

namespace WallpaperPoc;

/// <summary>
/// PoC 主界面：全屏 SurfaceView 模拟壁纸 Surface，验证方案 4.1/4.2 节的
/// 生命周期对齐与循环策略对比。UI 全部代码构建，不依赖 axml。
/// </summary>
[Activity(Label = "壁纸引擎PoC", MainLauncher = true)]
public class MainActivity : Activity, ISurfaceHolderCallback
{
    const int PickVideoRequest = 42;
    const long WrapThresholdMs = 800; // 位置回绕超过此值判定为新循环
    const string Tag = "WEPoc";
    const string DefaultVideoName = "default.mp4"; // 应用外部私有目录内的默认素材

    // 内核注册表：默认使用 ExoPlayer + 原生循环（方案 D3/D4 的 V1 正式组合）
    static readonly (string Label, Func<Context, IVideoLoopPlayer> Factory)[] PlayerRegistry =
    {
        ("MediaPlayer", ctx => new MediaPlayerLoopPlayer()),
        ("ExoPlayer", ctx => new ExoPlayerLoopPlayer(ctx)),
    };

    SurfaceView _surfaceView = null!;
    TextView _statusText = null!;
    Button _pickButton = null!, _playerButton = null!, _loopButton = null!, _playPauseButton = null!;

    IVideoLoopPlayer _player = null!;
    int _playerIndex = 1; // 默认 ExoPlayer

    Uri? _videoUri;
    bool _surfaceReady;
    bool _hasSource;
    bool _userPaused;   // 用户主动暂停
    bool _wantPlaying = true; // 期望播放态（自动播放与恢复依据）

    int _loopCount;
    long _lastPosMs = -1;
    long _lastRestartMs = -1;
    string _lastError = "-";
    Handler? _poll;
    int _tickCount;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window.AddFlags(WindowManagerFlags.KeepScreenOn);
        Title = "壁纸引擎 PoC";

        BuildUi();
        _player = CreatePlayer();
        UpdateButtons();
        TryLoadDefaultVideo();

        StartPolling();
    }

    /// <summary>加载默认素材（应用外部私有目录 default.mp4，由 adb push 或导入流程放置）</summary>
    void TryLoadDefaultVideo()
    {
        var dir = GetExternalFilesDir(null);
        if (dir == null) return;
        var file = new Java.IO.File(dir, DefaultVideoName);
        if (!file.Exists()) return;

        _videoUri = Uri.FromFile(file);
        _wantPlaying = true;
        Log.Info(Tag, $"默认素材已加载: {file} 大小={file.Length()}");
        if (_surfaceReady && !_hasSource) PrepareAndPlayCurrent();
    }

    void BuildUi()
    {
        var root = new FrameLayout(this);

        _surfaceView = new SurfaceView(this);
        _surfaceView.Holder.SetKeepScreenOn(true);
        _surfaceView.Holder.AddCallback(this);
        root.AddView(_surfaceView, new FrameLayout.LayoutParams(-1, -1));

        _statusText = new TextView(this)
        {
            Text = $"等待素材（私有目录 {DefaultVideoName} 自动加载，或点选择视频）",
            TextSize = 13f,
        };
        _statusText.SetTextColor(Color.White);
        _statusText.SetShadowLayer(4, 0, 0, Color.Black);
        var statusLp = new FrameLayout.LayoutParams(-2, -2)
        {
            Gravity = GravityFlags.Top | GravityFlags.Left,
        };
        statusLp.SetMargins(24, 48, 24, 0);
        root.AddView(_statusText, statusLp);

        var bar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _pickButton = MkButton("选择视频", (_, _) => PickVideo());
        _playerButton = MkButton("切换内核", (_, _) => SwitchPlayer());
        _loopButton = MkButton("循环模式", (_, _) => ToggleLoopMode());
        _playPauseButton = MkButton("播放/暂停", (_, _) => TogglePlayPause());
        bar.AddView(_pickButton);
        bar.AddView(_playerButton);
        bar.AddView(_loopButton);
        bar.AddView(_playPauseButton);

        var barLp = new FrameLayout.LayoutParams(-2, -2)
        {
            Gravity = GravityFlags.Bottom | GravityFlags.CenterHorizontal,
        };
        barLp.SetMargins(0, 0, 0, 48);
        root.AddView(bar, barLp);

        SetContentView(root);
    }

    Button MkButton(string text, EventHandler onClick)
    {
        var b = new Button(this) { Text = text, TextSize = 12f, LayoutParameters = new LinearLayout.LayoutParams(0, -2, 1f) };
        b.Click += onClick;
        return b;
    }

    // ---------- 播放内核管理 ----------

    IVideoLoopPlayer CreatePlayer()
    {
        var p = PlayerRegistry[_playerIndex].Factory(this);
        p.Prepared += (_, _) =>
        {
            _hasSource = true;
            Log.Info(Tag, $"[{p.Name}] Prepared 时长={p.DurationMs}ms");
            if (_wantPlaying && !_userPaused) p.Play();
        };
        p.ManualRestart += (_, latencyMs) =>
        {
            _lastRestartMs = latencyMs;
            Log.Info(Tag, $"[{p.Name}] 手动重启完成 延迟={latencyMs}ms");
        };
        p.ErrorOccurred += (_, message) =>
        {
            _lastError = message;
            Log.Error(Tag, $"[{p.Name}] {message}");
        };
        return p;
    }

    void SwitchPlayer()
    {
        bool wasPlaying = _player.IsPlaying;
        _player.Release();

        _playerIndex = (_playerIndex + 1) % PlayerRegistry.Length;
        _player = CreatePlayer();
        _loopCount = 0;
        _lastPosMs = -1;
        _lastRestartMs = -1;
        UpdateButtons();

        if (_videoUri == null || !_surfaceReady) return;
        _hasSource = false;
        _wantPlaying = wasPlaying;
        _player.SetDataSource(this, _videoUri);
        _player.SetSurface(_surfaceView.Holder.Surface);
        _player.Prepare();
    }

    void ToggleLoopMode()
    {
        if (_videoUri == null) return;
        _player.NativeLoop = !_player.NativeLoop;
        _loopCount = 0;
        _lastRestartMs = -1;
        UpdateButtons();
    }

    void TogglePlayPause()
    {
        if (!_hasSource) { PickVideo(); return; }

        if (_player.IsPlaying)
        {
            _player.Pause();
            _userPaused = true;
        }
        else
        {
            _userPaused = false;
            _wantPlaying = true;
            _player.Play();
        }
        UpdateButtons();
    }

    // ---------- SAF 视频选择（验证方案 4.5 节导入方式） ----------

    void PickVideo()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("video/*");
        StartActivityForResult(intent, PickVideoRequest);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickVideoRequest || resultCode != Result.Ok || data?.Data == null) return;

        _videoUri = data.Data!;
        try
        {
            ContentResolver.TakePersistableUriPermission(_videoUri, ActivityFlags.GrantReadUriPermission);
        }
        catch (Exception)
        {
            // 部分选择器不带 persistable 授权，PoC 直接播放即可，不影响验证
        }

        _hasSource = false;
        _userPaused = false;
        _wantPlaying = true;
        _loopCount = 0;
        _lastPosMs = -1;
        _lastRestartMs = -1;
        _lastError = "-";

        if (_surfaceReady) PrepareAndPlayCurrent();
    }

    void PrepareAndPlayCurrent()
    {
        _player.Reset();
        _player.SetDataSource(this, _videoUri!);
        _player.SetSurface(_surfaceView.Holder!.Surface);
        _player.Prepare(); // 就绪后经 Prepared 事件自动 Play
    }

    // ---------- Surface 生命周期：对齐壁纸 Engine 的回调约束（方案 4.1） ----------

    public void SurfaceCreated(ISurfaceHolder holder)
    {
        _surfaceReady = true;
        if (_videoUri == null) return;

        if (!_hasSource)
        {
            PrepareAndPlayCurrent();
        }
        else
        {
            // Surface 重建（如解锁后）场景：仅重绑 Surface 并续播
            _player.SetSurface(holder.Surface);
            if (_wantPlaying && !_userPaused) _player.Play();
        }
    }

    public void SurfaceChanged(ISurfaceHolder holder, Format format, int width, int height) { }

    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        // 壁纸 Engine 同款硬性约束：必须在返回前暂停并解除 Surface 引用
        _surfaceReady = false;
        _player.Pause();
        _player.SetSurface(null);
    }

    // ---------- Activity 生命周期：模拟壁纸可见性变化 ----------

    protected override void OnPause()
    {
        base.OnPause();
        _wantPlaying = _player.IsPlaying;
        _player.Pause();
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (_hasSource && _wantPlaying && !_userPaused) _player.Play();
    }

    protected override void OnDestroy()
    {
        _poll?.RemoveCallbacksAndMessages(null!);
        _poll = null;
        _player.Release();
        base.OnDestroy();
    }

    // ---------- 状态轮询：循环计数（位置回绕检测）+ 内存 ----------

    void StartPolling()
    {
        _poll = new Handler(Looper.MainLooper!);
        PostTick();
    }

    void PostTick() => _poll?.PostDelayed(() =>
    {
        if (IsDestroyed || IsFinishing) return; // Activity 已销毁则停止轮询链（防僵尸日志）
        Tick();
        PostTick();
    }, 500);

    void Tick()
    {
        if (_hasSource)
        {
            long pos = _player.PositionMs;
            if (_lastPosMs >= 0 && _lastPosMs - pos > WrapThresholdMs)
            {
                _loopCount++;
                Log.Info(Tag, $"[{_player.Name}] 第{_loopCount}圈（位置回绕 {_lastPosMs}→{pos}ms）");
            }
            _lastPosMs = pos;
        }

        // 每 4 秒输出一次状态行，供电脑端 logcat 远程监控
        if (++_tickCount % 8 == 0 && _hasSource)
        {
            Log.Info(Tag, $"状态 内核={_player.Name} 循环={(_player.NativeLoop ? "原生" : "手动")} 圈数={_loopCount} " +
                          $"进度={_player.PositionMs}/{_player.DurationMs}ms 播放中={_player.IsPlaying} " +
                          $"重启延迟={_lastRestartMs}ms RSS={ReadRssMb():F0}MB");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"内核: {_player.Name}   循环: {(_player.NativeLoop ? "原生" : "手动重启")}");
        sb.AppendLine($"圈数: {_loopCount}   进度: {_player.PositionMs / 1000.0:F1}s / {_player.DurationMs / 1000.0:F1}s");
        sb.AppendLine($"手动重启延迟: {(_lastRestartMs >= 0 ? _lastRestartMs + "ms" : "-")}   错误: {_lastError}");
        sb.AppendLine($"内存 RSS: {ReadRssMb():F0} MB");
        string vid = _videoUri?.ToString() ?? "-";
        sb.Append($"视频: {(vid.Length > 60 ? vid[..60] : vid)}");
        _statusText.Text = sb.ToString();

        UpdateButtons();
    }

    void UpdateButtons()
    {
        _playerButton.Text = $"内核:{_player.Name}{(PlayerRegistry.Length > 1 ? "▸" : "")}";
        _loopButton.Text = $"循环:{(_player.NativeLoop ? "原生" : "手动")}";
        _playPauseButton.Text = _player.IsPlaying ? "暂停" : "播放";
    }

    static float ReadRssMb()
    {
        try
        {
            foreach (string line in File.ReadAllLines("/proc/self/status"))
            {
                if (line.StartsWith("VmRSS:"))
                {
                    // "VmRSS:\t 123456 kB"
                    string kb = line[(line.IndexOf(':') + 1)..].Trim().Split(' ')[0];
                    return float.Parse(kb) / 1024f;
                }
            }
        }
        catch (Exception) { /* 沙箱或权限变化时忽略 */ }
        return -1;
    }
}
