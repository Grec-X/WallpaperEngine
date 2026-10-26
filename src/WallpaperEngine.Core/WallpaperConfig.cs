using System.Text.Json;

namespace WallpaperEngine.Core;

/// <summary>
/// 壁纸配置（方案 3.3 字段表），JSON 持久化于应用私有目录。
/// 壁纸服务与管理界面通过此模型共享状态。
/// </summary>
public class WallpaperConfig
{
    public string VideoPath { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public long DurationMs { get; set; }
    public int VideoWidth { get; set; }
    public int VideoHeight { get; set; }
    public float Volume { get; set; } = 0f; // 壁纸默认静音
    /// <summary>Cover=保持比例裁剪铺满（默认）；Stretch=拉伸填满（变形）；Fit=留边（V2，需渲染中间层）</summary>
    public string FillMode { get; set; } = "Cover";
    /// <summary>画面压暗系数 0.2~1，1=不压暗（省电/夜间护眼；V1 未实现渲染管线，字段预留）</summary>
    public float Brightness { get; set; } = 1f;
    /// <summary>循环区间起点（毫秒），0=从片头</summary>
    public long TrimStartMs { get; set; }
    /// <summary>循环区间终点（毫秒），0=到片尾</summary>
    public long TrimEndMs { get; set; }
    /// <summary>桌面槽的"默认壁纸"（空=未设置，恢复桌面默认时还原系统壁纸）</summary>
    public string DefaultVideoPath { get; set; } = "";
    public string DefaultOriginalName { get; set; } = "";
    /// <summary>桌面默认的媒体类型（Video=引擎播放 / Image=静态提交）</summary>
    public string DefaultMediaType { get; set; } = "Video";
    /// <summary>锁屏槽的"默认壁纸"（空=未设置，恢复锁屏默认时还原系统壁纸）</summary>
    public string LockDefaultVideoPath { get; set; } = "";
    public string LockDefaultOriginalName { get; set; } = "";
    /// <summary>最近提交的锁屏静态画面（跟踪显示用；空=锁屏跟随桌面）</summary>
    public string LockFramePath { get; set; } = "";
    public string LockFrameName { get; set; } = "";
    /// <summary>最近提交的桌面静态图（跟踪显示用；非空=引擎已退出，桌面为静态）</summary>
    public string DesktopStaticPath { get; set; } = "";
    public string DesktopStaticName { get; set; } = "";
    /// <summary>桌面槽的缩略图路径（引擎冷启动时作占位帧，避免黑屏）</summary>
    public string ThumbPath { get; set; } = "";
    /// <summary>锁屏槽的缩略图路径（引擎冷启动时作占位帧，避免黑屏）</summary>
    public string LockThumbPath { get; set; } = "";
    /// <summary>锁屏槽视频（Android 14+ 引擎按 WallpaperFlags 识别身份后读取；空=跟随桌面槽）</summary>
    public string LockVideoPath { get; set; } = "";
    public string LockOriginalName { get; set; } = "";
    /// <summary>锁屏槽独立设置（音量/填充/裁剪/时长），与桌面槽互不影响</summary>
    public float LockVolume { get; set; } = 0f;
    public string LockFillMode { get; set; } = "Cover";
    public long LockTrimStartMs { get; set; }
    public long LockTrimEndMs { get; set; }
    public long LockDurationMs { get; set; }
    /// <summary>待应用视频：点击后暂存，经系统确认页确认后按实际绑定范围路由到对应槽（15 分钟过期）</summary>
    public string PendingVideoPath { get; set; } = "";
    public string PendingName { get; set; } = "";
    public long PendingDurationMs { get; set; }
    public DateTime PendingAt { get; set; } = DateTime.MinValue;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string GetFilePath(string dir) => Path.Combine(dir, "wallpaper_config.json");

    public static WallpaperConfig LoadOrDefault(string dir)
    {
        try
        {
            var path = GetFilePath(dir);
            if (File.Exists(path))
            {
                var cfg = JsonSerializer.Deserialize<WallpaperConfig>(File.ReadAllText(path));
                if (cfg != null && !string.IsNullOrEmpty(cfg.VideoPath) && File.Exists(cfg.VideoPath))
                    return cfg;
            }
        }
        catch (Exception)
        {
            // 配置损坏时回退默认（首启/异常路径）
        }
        return new WallpaperConfig();
    }

    public void Save(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(GetFilePath(dir), JsonSerializer.Serialize(this, JsonOpts));
    }
}
