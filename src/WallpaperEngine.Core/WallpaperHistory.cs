using System.Text.Json;

namespace WallpaperEngine.Core;

public class WallpaperHistoryEntry
{
    /// <summary>视频内容 hash（去重键），同时是缩略图文件名</summary>
    public string Id { get; set; } = "";
    public string VideoPath { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public long DurationMs { get; set; }
    public DateTime UsedAt { get; set; } = DateTime.Now;
    /// <summary>用户收藏标记（豁免超限淘汰）</summary>
    public bool IsFavorite { get; set; }
    /// <summary>媒体类型：Video（引擎动态）/ Image（静态提交）</summary>
    public string MediaType { get; set; } = "Video";
}

/// <summary>
/// 历史壁纸记录：上限 10 条，同内容视频只刷新使用时间（置顶），新条目插入头部。
/// </summary>
public class WallpaperHistory
{
    public const int MaxEntries = 25;
    public List<WallpaperHistoryEntry> Entries { get; set; } = new();

    public static string GetFilePath(string dir) => Path.Combine(dir, "wallpaper_history.json");

    public static WallpaperHistory Load(string dir)
    {
        try
        {
            var path = GetFilePath(dir);
            if (File.Exists(path))
            {
                var h = JsonSerializer.Deserialize<WallpaperHistory>(File.ReadAllText(path));
                if (h != null) return h;
            }
        }
        catch (Exception)
        {
            // 损坏时回退空历史
        }
        return new WallpaperHistory();
    }

    public void Save(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(GetFilePath(dir), JsonSerializer.Serialize(this));
    }

    public WallpaperHistoryEntry? Find(string id) =>
        Entries.FirstOrDefault(e => e.Id == id);

    /// <summary>移除指定条目（不删文件，文件清理由调用方决策）。返回被移除的条目。</summary>
    public WallpaperHistoryEntry? Remove(string id)
    {
        var entry = Find(id);
        if (entry != null) Entries.Remove(entry);
        return entry;
    }

    /// <summary>
    /// 记录一次使用：已存在则刷新时间与最新路径并置顶；否则插入头部。
    /// 超限时淘汰最旧的非收藏条目（收藏豁免；全豁免时软性溢出不淘汰）。
    /// 不负责持久化——调用方随后 Save。
    /// </summary>
    public WallpaperHistoryEntry? Record(WallpaperHistoryEntry entry)
    {
        var existing = Find(entry.Id);
        if (existing != null)
        {
            existing.UsedAt = DateTime.Now;
            existing.VideoPath = entry.VideoPath;
            existing.OriginalName = entry.OriginalName;
            existing.DurationMs = entry.DurationMs;
            Entries.Remove(existing);
            Entries.Insert(0, existing);
            return null;
        }

        Entries.Insert(0, entry);
        WallpaperHistoryEntry? evicted = null;
        if (Entries.Count > MaxEntries)
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (!Entries[i].IsFavorite)
                {
                    evicted = Entries[i];
                    Entries.RemoveAt(i);
                    break;
                }
            }
            // 全部收藏时软性溢出：保留所有条目
        }
        return evicted;
    }
}
