using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Service.Wallpaper;
using Android.Util;
using Android.Views;
using WallpaperEngine.Core;
using WallpaperEngine.Player;
using Uri = Android.Net.Uri;

namespace WallpaperEngine.App;

/// <summary>
/// 动态壁纸服务（方案 4.1 生命周期矩阵的正式实现）。
/// 系统经 BIND_WALLPAPER 绑定本服务，Engine 收到 Surface 与可见性回调后驱动播放内核。
/// </summary>
[Service(
    Label = "壁纸引擎视频壁纸",
    Name = "com.wallpaperengine.app.VideoWallpaperService",
    Permission = "android.permission.BIND_WALLPAPER",
    Exported = true)]
[IntentFilter(new[] { Android.Service.Wallpaper.WallpaperService.ServiceInterface })]
[MetaData(Android.Service.Wallpaper.WallpaperService.ServiceMetaData, Resource = "@xml/video_wallpaper")]
public class VideoWallpaperService : WallpaperService
{
    const string Tag = "WESvc";

    public override Engine? OnCreateEngine()
    {
        Log.Info(Tag, "OnCreateEngine");
        return new VideoEngine(this);
    }

    /// <summary>换视频后通知引擎重载配置（引擎未运行时为空操作）</summary>
    public static void NotifyConfigChanged()
    {
        Log.Info(Tag, $"NotifyConfigChanged → 通知 {_engines.Count} 个活跃引擎");
        VideoEngine[] snapshot;
        lock (EnginesLock) snapshot = _engines.ToArray();
        foreach (var engine in snapshot) engine.ReloadFromConfig();
    }

    // ColorOS「桌面和锁屏」会各绑定一个引擎实例：多引擎并存，
    // 单实例跟踪会被后建引擎覆盖、又被其销毁误清（曾致横幅"绿→黄"翻转且状态恒假）
    static readonly object EnginesLock = new();
    static readonly List<VideoEngine> _engines = new();

    /// <summary>
    /// 壁纸是否激活：任一引擎实例存活即激活。
    /// ColorOS 13 的 WallpaperManager.WallpaperInfo 恒返回 null，标准 API 不可用（见阶段四日志）。
    /// </summary>
    public static bool IsEngineRunning
    {
        get { lock (EnginesLock) return _engines.Count > 0; }
    }

    /// <summary>引擎上线/下线时触发，管理界面据此实时刷新状态</summary>
    public static event Action? EngineStateChanged;

    /// <summary>当前活跃引擎的状态：目标槽 + 播放异常 + 是否已就绪（就绪=有可播放时长）</summary>
    public record EngineStatus(WallpaperManagerFlags Flags, string? Error, bool Ready);

    /// <summary>当前活跃引擎的状态列表（去重按槽，保留异常信息）</summary>
    public static List<EngineStatus> GetActiveEngineStatus()
    {
        lock (EnginesLock)
        {
            return _engines
                .Select(e => new EngineStatus(e.CurrentFlags, e.LastError, e.IsReady))
                .GroupBy(s => s.Flags)
                .Select(g => g.First())
                .ToList();
        }
    }

    /// <summary>当前活跃引擎的服务目标集合（兜底路径：getWallpaperInfo 不可用时据此路由待应用视频）</summary>
    public static List<WallpaperManagerFlags> GetActiveEngineFlags()
    {
        return GetActiveEngineStatus().Select(s => s.Flags).ToList();
    }

    static void RegisterEngine(VideoEngine engine)
    {
        lock (EnginesLock) _engines.Add(engine);
        EngineStateChanged?.Invoke();
    }

    static void UnregisterEngine(VideoEngine engine)
    {
        lock (EnginesLock) _engines.Remove(engine);
        EngineStateChanged?.Invoke();
    }

    /// <summary>检查当前壁纸是否为本服务（管理界面状态显示用）</summary>
    public static bool IsCurrentWallpaper(Context context)
    {
        const string expected = "com.wallpaperengine.app.VideoWallpaperService";
        var info = WallpaperManager.GetInstance(context)?.WallpaperInfo;
        if (info == null)
        {
            Log.Info(Tag, "IsCurrentWallpaper: WallpaperInfo=null（当前非动态壁纸）");
            return false;
        }

        var cn = info.Component;
        bool match =
            cn?.ClassName == expected ||
            info.ServiceInfo?.Name == expected ||
            (cn?.PackageName == "com.wallpaperengine.app" &&
             cn?.ClassName?.EndsWith("VideoWallpaperService", StringComparison.Ordinal) == true);
        Log.Info(Tag, $"IsCurrentWallpaper: pkg={cn?.PackageName} class={cn?.ClassName} svc={info.ServiceInfo?.Name} => {match}");
        return match;
    }

    // 固定 ACW 类名（与 Debug 运行时一致）；Release 构建由 fix-acw.ps1 将生成器输出的
    // 非法顶层桥接改写为 Service 内部类（Java 语法要求：内部类子类必须声明在外围类内）
    [Register("com.wallpaperengine.app.VideoWallpaperService$VideoWallpaperService_VideoEngine")]
    class VideoEngine : WallpaperService.Engine
    {
        const string Tag = VideoWallpaperService.Tag + ".Engine";

        readonly WallpaperService _service;
        readonly IVideoLoopPlayer _player;
        WallpaperConfig _config = new();
        string? _loadedPath;        // 当前已装载的视频路径（与配置比对决定是否重载）
        bool _surfaceReady;
        bool _wantPlaying = true;   // 壁纸语义：可见即播放
        bool _needPrepare;          // 装载了新源待 Prepare
        bool _errorRetried;         // 源错误重试只做一次（防死循环）
        int _prepareRetries;        // 准备超时重试计数（最多 2 次）

        // JNI 重建构造（绑定要求；同时使 Release 的 Java 桥接正确生成）
        public VideoEngine(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
            _service = null!; // JNI 重建路径不用于业务创建，字段由 OnCreate 上下文兜底
        }

        public VideoEngine(WallpaperService service) : base(service)
        {
            _service = service;
            Log.Info(Tag, $"Java 层类名 = {Class.Name}");
            _player = new ExoPlayerLoopPlayer(service);
            _player.NativeLoop = true;
            _player.SetVolume(0f); // 壁纸静音
            _player.Prepared += (_, _) =>
            {
                _errorRetried = false;
                _prepareRetries = 0;
                LastError = null; // 播放恢复，清除异常状态
                Log.Info(Tag, $"[{_player.Name}] Prepared 时长={_player.DurationMs}ms");
                if (_wantPlaying) _player.Play();
            };
            _player.ErrorOccurred += (_, message) =>
            {
                Log.Error(Tag, $"[{_player.Name}] {message}");
                LastError = message; // 芯片显示"播放异常"（见 MainActivity）
                // 源错误恢复：丢弃装载记录，1 秒后重试一次（ResolveVideoPath 会回退默认素材），防死循环
                if (_errorRetried) return;
                _errorRetried = true;
                _loadedPath = null;
                new Handler(Looper.MainLooper!).PostDelayed(() =>
                {
                    Log.Info(Tag, "源错误后自动重试");
                    EnsurePlayback();
                }, 1000);
            };
            _player.ManualRestart += (_, ms) => Log.Info(Tag, $"[{_player.Name}] 手动重启 延迟={ms}ms");
        }

        // ----- 方案 4.1 生命周期矩阵 -----

        public override void OnCreate(ISurfaceHolder surfaceHolder)
        {
            base.OnCreate(surfaceHolder);
            RegisterEngine(this);
            _config = WallpaperConfig.LoadOrDefault(_service.FilesDir!.AbsolutePath);
            _player.SetScaleMode(IsCoverMode); // V1 默认 Cover：保持比例裁剪铺满，避免拉伸变形
            Log.Info(Tag, $"OnCreate 视频路径={_config.VideoPath}");
        }

        public override void OnSurfaceCreated(ISurfaceHolder holder)
        {
            base.OnSurfaceCreated(holder);
            _surfaceReady = true;
            Log.Info(Tag, "OnSurfaceCreated");
            // 注意：不可对 Surface 用 LockCanvas 画占位帧——会与 MediaCodec 争抢缓冲区，
            // 导致解码器无法输出（实测表现为"播放异常 + 画面停在缩略图"）。
            LoadVideoIfChanged();
            _player.SetSurface(holder.Surface);
            _needPrepare = true; // 冷启动装载后必须准备
            EnsurePlayback();
        }

        public override void OnSurfaceChanged(ISurfaceHolder holder, Format format, int width, int height)
        {
            base.OnSurfaceChanged(holder, format, width, height);
            Log.Info(Tag, $"OnSurfaceChanged {width}x{height}");
        }

        public override void OnVisibilityChanged(bool visible)
        {
            base.OnVisibilityChanged(visible);
            Log.Info(Tag, $"OnVisibilityChanged visible={visible}");
            _wantPlaying = visible;
            if (visible)
            {
                RefreshConfig(); // 自愈：回桌面必重读配置文件，内存副本可能落后于管理界面的写入
                EnsurePlayback();
            }
            else
            {
                _player.Pause();
            }
        }

        public override void OnSurfaceDestroyed(ISurfaceHolder holder)
        {
            base.OnSurfaceDestroyed(holder);
            // 硬性约束：必须在返回前暂停并解除 Surface 引用
            _surfaceReady = false;
            _player.Pause();
            _player.SetSurface(null);
            Log.Info(Tag, "OnSurfaceDestroyed（已暂停并解除 Surface）");
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            _player.Release();
            UnregisterEngine(this);
            Log.Info(Tag, "OnDestroy（内核已释放）");
        }

        public override void OnWallpaperFlagsChanged(WallpaperManagerFlags which)
        {
            base.OnWallpaperFlagsChanged(which);
            Log.Info(Tag, $"OnWallpaperFlagsChanged → {which}（槽位重识别）");
            _loadedPath = null; // 目标可能变化，重新按槽装载
            if (_surfaceReady) EnsurePlayback();
        }

        // ----- 配置重载（管理界面换视频后调用） -----

        public void ReloadFromConfig()
        {
            RefreshConfig();
            Log.Info(Tag, $"ReloadFromConfig 路径={_config.VideoPath}");
            EnsurePlayback();
        }

        void RefreshConfig()
        {
            _config = WallpaperConfig.LoadOrDefault(_service.FilesDir!.AbsolutePath);
            _player.SetScaleMode(IsCoverMode);
            _player.SetVolume(SlotVolume); // 音量即改即生效，无需重装载

            // 裁剪区间变化需要重新装载源（ClippingConfiguration 在 SetDataSource 时应用）
            if (_player.TrimStartMs != SlotTrimStart || _player.TrimEndMs != SlotTrimEnd)
            {
                _player.TrimStartMs = SlotTrimStart;
                _player.TrimEndMs = SlotTrimEnd;
                _loadedPath = null; // 强制下次 EnsurePlayback 重新装载
            }
        }

        // ----- 按槽设置（引擎身份决定读桌面槽还是锁屏槽字段） -----

        float SlotVolume => IsLockSlot ? _config.LockVolume : _config.Volume;
        string SlotFillMode => IsLockSlot ? _config.LockFillMode : _config.FillMode;
        long SlotTrimStart => IsLockSlot ? _config.LockTrimStartMs : _config.TrimStartMs;
        long SlotTrimEnd => IsLockSlot ? _config.LockTrimEndMs : _config.TrimEndMs;

        bool IsCoverMode => !string.Equals(SlotFillMode, "Stretch", StringComparison.OrdinalIgnoreCase);

        /// <summary>统一的播放保障：装载最新配置视频 → 必要时 Prepare → 可见即 Play</summary>
        void EnsurePlayback()
        {
            if (!_surfaceReady) return;
            if (LoadVideoIfChanged()) _needPrepare = true;
            if (_needPrepare)
            {
                _player.Prepare(); // 就绪后经 Prepared 事件按 _wantPlaying 自动 Play
                _needPrepare = false;
                StartPrepareWatchdog();
            }
            else if (_wantPlaying)
            {
                _player.Play();
            }
        }

        /// <summary>
        /// 准备看门狗：开机后系统负载极高、锁屏可见性频繁抖动时，ExoPlayer 可能长时间停不到 READY
        /// （实测 MediaCodec 已 start 但 10 秒无 Prepared，画面停在空白）。超时后重新装载，最多两次。
        /// </summary>
        void StartPrepareWatchdog()
        {
            new Handler(Looper.MainLooper!).PostDelayed(() =>
            {
                if (_player.DurationMs > 0) return;      // 已就绪
                if (_prepareRetries >= 2) return;        // 防死循环
                if (!_surfaceReady) return;              // Surface 已销毁，等下次创建

                _prepareRetries++;
                Log.Warn(Tag, $"Prepare 超时，第 {_prepareRetries} 次重新装载");
                _loadedPath = null;
                _needPrepare = true;
                EnsurePlayback();
            }, 6000);
        }

        bool LoadVideoIfChanged()
        {
            string path = ResolveVideoPath();
            if (string.IsNullOrEmpty(path))
            {
                Log.Warn(Tag, $"无可播放视频（桌面槽={_config.VideoPath} 锁屏槽={_config.LockVideoPath}，默认也不存在）");
                return false;
            }
            if (_loadedPath == path) return false; // 未变化不重载

            _loadedPath = path;
            _player.Reset();
            _player.SetDataSource(_service, Uri.FromFile(new Java.IO.File(path)));
            _player.SetVolume(_config.Volume);
            Log.Info(Tag, $"已装载视频[{(IsLockSlot ? "Lock" : "System")}]: {path}");
            return true;
        }

        /// <summary>
        /// 目标槽识别（官方 API）：Android 14+ 引擎经 WallpaperFlags 得知自己服务哪端，
        /// 按槽读取对应视频（锁屏槽空则跟随桌面槽）；Android 13- 无此 API，回落桌面槽。
        /// </summary>
        public WallpaperManagerFlags CurrentFlags =>
            Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake
                ? WallpaperFlags
                : WallpaperManagerFlags.System;

        bool IsLockSlot => CurrentFlags == WallpaperManagerFlags.Lock;

        /// <summary>本引擎最近一次播放异常（null=正常），供管理界面芯片显示"播放异常"</summary>
        public string? LastError { get; private set; }

        string SlotVideoPath()
        {
            if (IsLockSlot && !string.IsNullOrEmpty(_config.LockVideoPath))
                return _config.LockVideoPath;
            return _config.VideoPath;
        }

        /// <summary>是否已就绪（能取到时长说明解码器准备好、视频可用）</summary>
        public bool IsReady => _player.DurationMs > 0;

        /// <summary>槽路径失效或是非视频文件（如误写入的图片）时回退默认素材（default.mp4 缺失则从 Assets 重新释放）</summary>
        string ResolveVideoPath()
        {
            string slot = SlotVideoPath();
            string ext = System.IO.Path.GetExtension(slot ?? "").ToLowerInvariant();
            bool slotIsVideo = File.Exists(slot) && ext is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".gif");
            if (slotIsVideo) return slot;

            Log.Warn(Tag, $"槽视频无效[{(IsLockSlot ? "Lock" : "System")}]: {slot}，回退默认");
            var target = new Java.IO.File(new Java.IO.File(_service.FilesDir, "videos"), "default.mp4");
            if (!target.Exists())
            {
                try
                {
                    var dir = target.ParentFile!;
                    if (!dir.Exists()) dir.Mkdirs();
                    using var input = _service.Assets!.Open("default.mp4");
                    using var output = System.IO.File.Create(target.AbsolutePath);
                    input.CopyTo(output);
                    output.Flush();
                }
                catch (Exception ex)
                {
                    Log.Error(Tag, $"释放默认素材失败: {ex.Message}");
                    return "";
                }
            }
            return target.AbsolutePath;
        }
    }
}
