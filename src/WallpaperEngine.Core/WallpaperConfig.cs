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
