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
                Log.Info(Tag, $"[{_player.Name}] Prepared 时长={_player.DurationMs}ms");
                if (_wantPlaying) _player.Play();
            };
            _player.ErrorOccurred += (_, message) =>
            {
                Log.Error(Tag, $"[{_player.Name}] {message}");
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

        bool IsCoverMode => !string.Equals(_config.FillMode, "Fit", StringComparison.OrdinalIgnoreCase);

        public override void OnSurfaceCreated(ISurfaceHolder holder)
        {
            base.OnSurfaceCreated(holder);
            _surfaceReady = true;
            Log.Info(Tag, "OnSurfaceCreated");
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
            _player.SetVolume(_config.Volume); // 音量即改即生效，无需重装载

            // 裁剪区间变化需要重新装载源（ClippingConfiguration 在 SetDataSource 时应用）
            if (_player.TrimStartMs != _config.TrimStartMs || _player.TrimEndMs != _config.TrimEndMs)
            {
                _player.TrimStartMs = _config.TrimStartMs;
                _player.TrimEndMs = _config.TrimEndMs;
                _loadedPath = null; // 强制下次 EnsurePlayback 重新装载
            }
        }

        /// <summary>统一的播放保障：装载最新配置视频 → 必要时 Prepare → 可见即 Play</summary>
        void EnsurePlayback()
        {
            if (!_surfaceReady) return;
            if (LoadVideoIfChanged()) _needPrepare = true;
            if (_needPrepare)
            {
                _player.Prepare(); // 就绪后经 Prepared 事件按 _wantPlaying 自动 Play
                _needPrepare = false;
            }
            else if (_wantPlaying)
            {
                _player.Play();
            }
        }

        bool LoadVideoIfChanged()
        {
            string path = ResolveVideoPath();
            if (string.IsNullOrEmpty(path))
            {
                Log.Warn(Tag, $"无可播放视频（配置={_config.VideoPath}，默认也不存在）");
                return false;
            }
            if (_loadedPath == path) return false; // 未变化不重载

            _loadedPath = path;
            _player.Reset();
            _player.SetDataSource(_service, Uri.FromFile(new Java.IO.File(path)));
            _player.SetVolume(_config.Volume);
            Log.Info(Tag, $"已装载视频: {path}");
            return true;
        }

        /// <summary>配置路径失效时回退默认素材（default.mp4 缺失则从 Assets 重新释放）</summary>
        string ResolveVideoPath()
        {
            if (File.Exists(_config.VideoPath)) return _config.VideoPath;

            Log.Warn(Tag, $"配置视频不存在: {_config.VideoPath}，回退默认");
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
