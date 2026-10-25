using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Media;
using Android.OS;
using Android.Provider;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Widget;
using WallpaperEngine.Core;
using Uri = Android.Net.Uri;
using Orientation = Android.Widget.Orientation;

namespace WallpaperEngine.App;

/// <summary>
/// 管理页：品牌头部 + 激活横幅 + 历史壁纸（缩略图快照，上限 10，内容去重）
/// + 设置项（音量/填充/循环区间裁剪）。
/// </summary>
[Activity(Label = "壁纸引擎", MainLauncher = true)]
public class MainActivity : Activity
{
    const string Tag = "WEApp";
    const int PickVideoRequest = 43;
    const string DefaultVideoName = "default.mp4";

    TextView _banner = null!, _statusText = null!, _trimInfoText = null!;
    Button _pickButton = null!, _resetButton = null!;
    SeekBar _volumeBar = null!, _trimStartBar = null!, _trimEndBar = null!;
    Switch _coverSwitch = null!;
    LinearLayout _sheetRow = null!, _sheetPanel = null!, _favRow = null!;
    FrameLayout _sheetOverlay = null!;
    View _sheetScrim = null!;
    TextView _historyToggleButton = null!;
    ImageView _currentThumb = null!;
    WallpaperConfig _cfg = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Title = "壁纸引擎";
        EnsureDefaultVideo();
        _cfg = WallpaperConfig.LoadOrDefault(FilesDir!.AbsolutePath);

        SetContentView(BuildUi());
        RefreshUi();
    }

    protected override void OnResume()
    {
        base.OnResume();
        _cfg = WallpaperConfig.LoadOrDefault(FilesDir!.AbsolutePath);
        VideoWallpaperService.EngineStateChanged += OnEngineStateChanged;
        RefreshUi();
    }

    protected override void OnPause()
    {
        VideoWallpaperService.EngineStateChanged -= OnEngineStateChanged;
        base.OnPause();
    }

    void OnEngineStateChanged() => RunOnUiThread(RefreshUi);

    // ---------- UI 构建 ----------

    View BuildUi()
    {
        var root = new FrameLayout(this);
        // Android 15+ 强制 edge-to-edge：系统栏高度转为 padding，防止标题/按钮被状态栏遮挡
        root.SetOnApplyWindowInsetsListener(new SystemBarsInsetListener());

        var main = new LinearLayout(this) { Orientation = Orientation.Vertical };
        main.SetBackgroundColor(Ui.Bg);

        // 品牌头部（左标题 + 右侧历史开关按钮）
        var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);
        header.SetPadding(Ui.Dp(this, 20), Ui.Dp(this, 20), Ui.Dp(this, 16), Ui.Dp(this, 6));
        var titleCol = new LinearLayout(this) { Orientation = Orientation.Vertical };
        titleCol.LayoutParameters = new LinearLayout.LayoutParams(0, -2, 1f);
        var title = new TextView(this) { Text = "壁纸引擎", TextSize = 26f };
        title.SetTextColor(Ui.TextPrimary);
        title.Typeface = Typeface.Create(Typeface.DefaultBold, TypefaceStyle.Bold);
        titleCol.AddView(title);
        var subtitle = Ui.Caption(this, "视频动态壁纸 · 无缝循环播放");
        subtitle.SetPadding(0, Ui.Dp(this, 2), 0, 0);
        titleCol.AddView(subtitle);
        header.AddView(titleCol);

        _historyToggleButton = new TextView(this)
        {
            Text = "☰",
            TextSize = 18f,
            Gravity = GravityFlags.Center,
        };
        _historyToggleButton.SetTextColor(Ui.TextPrimary);
        _historyToggleButton.SetBackgroundDrawable(Ui.RoundBg(Ui.Card, this, 12, Ui.CardStroke, 1));
        var toggleLp = new LinearLayout.LayoutParams(Ui.Dp(this, 42), Ui.Dp(this, 42));
        _historyToggleButton.LayoutParameters = toggleLp;
        _historyToggleButton.Click += (_, _) => ShowHistorySheet();
        header.AddView(_historyToggleButton);
        main.AddView(header);

        var scroll = new ScrollView(this) { LayoutParameters = new LinearLayout.LayoutParams(-1, 0, 1f) };
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.SetPadding(0, Ui.Dp(this, 4), 0, Ui.Dp(this, 32));
        scroll.AddView(content);
        main.AddView(scroll);

        // —— 激活横幅 ——
        _banner = new TextView(this) { TextSize = 16f, Gravity = GravityFlags.Center };
        _banner.SetTextColor(Color.White);
        _banner.Typeface = Typeface.Create(Typeface.DefaultBold, TypefaceStyle.Bold);
        _banner.SetPadding(0, Ui.Dp(this, 18), 0, Ui.Dp(this, 18));
        var bannerLp = new LinearLayout.LayoutParams(-1, -2);
        bannerLp.SetMargins(Ui.Dp(this, 16), Ui.Dp(this, 6), Ui.Dp(this, 16), Ui.Dp(this, 4));
        _banner.LayoutParameters = bannerLp;
        _banner.Click += (_, _) => RequestSetWallpaper();
        content.AddView(_banner);

        // —— 当前壁纸卡片（信息 + 右侧缩略图） ——
        var statusCard = Ui.MakeCard(this);
        var statusRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        statusRow.SetGravity(GravityFlags.CenterVertical);

        var statusCol = new LinearLayout(this) { Orientation = Orientation.Vertical };
        statusCol.LayoutParameters = new LinearLayout.LayoutParams(0, -2, 1f);
        statusCol.AddView(Ui.Label(this, "当前壁纸"));
        _statusText = new TextView(this) { TextSize = 13.5f };
        _statusText.SetTextColor(Ui.TextSecondary);
        _statusText.SetPadding(0, Ui.Dp(this, 8), Ui.Dp(this, 12), 0);
        statusCol.AddView(_statusText);
        statusRow.AddView(statusCol);

        _currentThumb = new ImageView(this);
        _currentThumb.SetScaleType(ImageView.ScaleType.CenterCrop!);
        _currentThumb.OutlineProvider = new RoundOutlineProvider(Ui.Dp(this, 12));
        _currentThumb.ClipToOutline = true;
        statusRow.AddView(_currentThumb, new LinearLayout.LayoutParams(Ui.Dp(this, 78), Ui.Dp(this, 139)));
        statusCard.AddView(statusRow);
        content.AddView(statusCard);

        // —— 操作按钮行（紧贴当前壁纸卡片，两钮等宽） ——
        var buttonRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var rowLp = new LinearLayout.LayoutParams(-1, -2);
        rowLp.SetMargins(Ui.Dp(this, 16), Ui.Dp(this, 2), Ui.Dp(this, 16), Ui.Dp(this, 6));
        buttonRow.LayoutParameters = rowLp;

        _pickButton = Ui.PrimaryButton(this, "选择壁纸");
        _pickButton.LayoutParameters = new LinearLayout.LayoutParams(0, Ui.Dp(this, 54), 1f);
        _pickButton.Click += (_, _) => PickVideo();
        buttonRow.AddView(_pickButton);

        var gap = new View(this);
        gap.LayoutParameters = new LinearLayout.LayoutParams(Ui.Dp(this, 12), 1);
        buttonRow.AddView(gap);

        _resetButton = Ui.SecondaryButton(this, "恢复默认");
        _resetButton.LayoutParameters = new LinearLayout.LayoutParams(0, Ui.Dp(this, 54), 1f);
        _resetButton.Click += (_, _) => ResetToDefault();
        buttonRow.AddView(_resetButton);
        content.AddView(buttonRow);

        // —— 播放设置卡片 ——
        var tuneCard = Ui.MakeCard(this);
        tuneCard.AddView(Ui.Label(this, "播放设置"));

        var volLabel = Ui.Caption(this, "音量（壁纸默认静音）");
        volLabel.SetPadding(0, Ui.Dp(this, 12), 0, 0);
        tuneCard.AddView(volLabel);
        _volumeBar = new SeekBar(this) { Max = 100, Progress = (int)(_cfg.Volume * 100) };
        Ui.StyleSeek(_volumeBar);
        _volumeBar.StopTrackingTouch += (_, _) => SaveSettings();
        tuneCard.AddView(_volumeBar);

        var fillLabel = Ui.Caption(this, "保持比例裁剪铺满（关闭 = 拉伸变形）");
        fillLabel.SetPadding(0, Ui.Dp(this, 8), 0, 0);
        tuneCard.AddView(fillLabel);
        _coverSwitch = new Switch(this) { Checked = _cfg.FillMode != "Stretch" };
        Ui.StyleSwitch(_coverSwitch);
        _coverSwitch.CheckedChange += (_, _) => SaveSettings();
        tuneCard.AddView(_coverSwitch);
        content.AddView(tuneCard);

        // —— 循环区间卡片 ——
        var trimCard = Ui.MakeCard(this);
        trimCard.AddView(Ui.Label(this, "循环区间"));
        var trimCaption = Ui.Caption(this, "裁掉片头 / 片尾，只循环选定片段");
        trimCaption.SetPadding(0, Ui.Dp(this, 4), 0, Ui.Dp(this, 6));
        trimCard.AddView(trimCaption);
        _trimStartBar = MakeTrimBar();
        trimCard.AddView(_trimStartBar);
        _trimEndBar = MakeTrimBar();
        trimCard.AddView(_trimEndBar);
        _trimInfoText = Ui.Caption(this, "");
        _trimInfoText.SetPadding(0, Ui.Dp(this, 4), 0, 0);
        trimCard.AddView(_trimInfoText);
        content.AddView(trimCard);

        root.AddView(main, new FrameLayout.LayoutParams(-1, -1));

        // —— 历史壁纸底部弹出层（覆盖在主界面之上） ——
        BuildHistorySheet(root);

        return root;
    }

    void BuildHistorySheet(FrameLayout root)
    {
        _sheetOverlay = new FrameLayout(this);
        _sheetOverlay.Visibility = ViewStates.Gone;

        // 半透明遮罩，点击收起
        _sheetScrim = new View(this);
        _sheetScrim.SetBackgroundColor(Color.Argb(170, 0, 0, 0));
        _sheetScrim.Clickable = true;
        _sheetScrim.Click += (_, _) => HideHistorySheet();
        _sheetOverlay.AddView(_sheetScrim, new FrameLayout.LayoutParams(-1, -1));

        // 右侧抽屉：设置中心，未来区块在此面板内从上到下扩展
        _sheetPanel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _sheetPanel.SetBackgroundDrawable(Ui.RoundBg(new Color(26, 29, 38), this, 24, Ui.CardStroke, 1));
        _sheetPanel.SetPadding(Ui.Dp(this, 20), Ui.Dp(this, 20), Ui.Dp(this, 16), Ui.Dp(this, 24));
        _sheetPanel.SetOnTouchListener(new DrawerSwipeListener(_sheetPanel, HideHistorySheet));

        var titleRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        titleRow.SetGravity(GravityFlags.CenterVertical);
        var sheetTitle = Ui.Label(this, "设置");
        sheetTitle.LayoutParameters = new LinearLayout.LayoutParams(0, -2, 1f);
        titleRow.AddView(sheetTitle);
        var closeBtn = new TextView(this) { Text = "✕", TextSize = 16f, Gravity = GravityFlags.Center };
        closeBtn.SetTextColor(Ui.TextSecondary);
        closeBtn.SetBackgroundDrawable(Ui.RoundBg(Ui.Card, this, 12, Ui.CardStroke, 1));
        closeBtn.LayoutParameters = new LinearLayout.LayoutParams(Ui.Dp(this, 38), Ui.Dp(this, 38));
        closeBtn.Click += (_, _) => HideHistorySheet();
        titleRow.AddView(closeBtn);
        _sheetPanel.AddView(titleRow);

        var scroll = new ScrollView(this);
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        scroll.AddView(content);
        _sheetPanel.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1f));

        // —— 使用说明入口 ——
        var guideRow = MakeMenuItemRow("使用说明", () => StartActivity(typeof(UserGuideActivity)));
        content.AddView(guideRow);
        content.AddView(MakeDivider());

        // —— 收藏壁纸区块 ——
        content.AddView(Ui.Label(this, "收藏"));
        var favCaption = Ui.Caption(this, "长按历史中的壁纸可加入收藏");
        favCaption.SetPadding(0, Ui.Dp(this, 4), 0, Ui.Dp(this, 8));
        content.AddView(favCaption);
        var favScroll = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false };
        _favRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _favRow.SetPadding(0, 0, 0, Ui.Dp(this, 8));
        favScroll.AddView(_favRow);
        content.AddView(favScroll);

        // —— 历史壁纸区块 ——
        var historyLabel = Ui.Label(this, "历史");
        historyLabel.SetPadding(0, Ui.Dp(this, 8), 0, 0);
        content.AddView(historyLabel);
        var caption = Ui.Caption(this, "点击切换 · 长按更多操作 · 最多 10 个");
        caption.SetPadding(0, Ui.Dp(this, 4), 0, Ui.Dp(this, 8));
        content.AddView(caption);
        var hsv = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false };
        _sheetRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _sheetRow.SetPadding(0, 0, 0, Ui.Dp(this, 4));
        hsv.AddView(_sheetRow);
        content.AddView(hsv);

        // 抽屉宽度约 78% 屏宽，右侧贴边
        int width = (int)(Resources!.DisplayMetrics!.WidthPixels * 0.78f);
        var panelLp = new FrameLayout.LayoutParams(width, -1, GravityFlags.Right);
        _sheetOverlay.AddView(_sheetPanel, panelLp);
        root.AddView(_sheetOverlay, new FrameLayout.LayoutParams(-1, -1));
    }

    void ShowHistorySheet()
    {
        RefreshHistoryUi();
        _sheetOverlay.Visibility = ViewStates.Visible;
        _sheetScrim.Alpha = 0f;
        _sheetScrim.Animate().Alpha(1f).SetDuration(180).Start();
        _sheetPanel.TranslationX = _sheetPanel.Width > 0 ? _sheetPanel.Width : Resources!.DisplayMetrics!.WidthPixels;
        _sheetPanel.Animate().TranslationX(0f).SetDuration(240).Start();
        _historyToggleButton.Text = "✕";
        _historyToggleButton.SetBackgroundDrawable(Ui.RoundBg(Ui.AccentDark, this, 12, Ui.Accent, 1));
    }

    void HideHistorySheet()
    {
        if (_sheetOverlay.Visibility != ViewStates.Visible) return;
        _sheetScrim.Animate().Alpha(0f).SetDuration(180).Start();
        _sheetPanel.Animate()
            .TranslationX(_sheetPanel.Width > 0 ? _sheetPanel.Width : Resources!.DisplayMetrics!.WidthPixels)
            .SetDuration(220)
            .WithEndAction(new Java.Lang.Runnable(() => _sheetOverlay.Visibility = ViewStates.Gone))
            .Start();
        _historyToggleButton.Text = "☰";
        _historyToggleButton.SetBackgroundDrawable(Ui.RoundBg(Ui.Card, this, 12, Ui.CardStroke, 1));
    }

    public override void OnBackPressed()
    {
        if (_sheetOverlay.Visibility == ViewStates.Visible)
        {
            HideHistorySheet();
            return;
        }
        base.OnBackPressed();
    }

    SeekBar MakeTrimBar()
    {
        var bar = new SeekBar(this) { Max = 1000 };
        Ui.StyleSeek(bar);
        bar.ProgressChanged += (_, _) => UpdateTrimPreview();
        bar.StopTrackingTouch += (_, _) => SaveSettings();
        return bar;
    }

    // ---------- 状态与历史刷新 ----------

    void RefreshUi()
    {
        bool active = VideoWallpaperService.IsEngineRunning;
        _banner.Text = active ? "✓  壁纸引擎已激活" : "⚠  未激活 · 点此设置壁纸";
        _banner.SetBackgroundDrawable(active
            ? Ui.GradientBg(Ui.OkDark, Ui.Ok, this, 18)
            : Ui.GradientBg(Ui.WarnDark, Ui.Warn, this, 18));

        _statusText.Text =
            $"视频：{_cfg.OriginalName}\n" +
            $"时长：{FormatMs(_cfg.DurationMs)}   分辨率：{(_cfg.VideoWidth > 0 ? $"{_cfg.VideoWidth}x{_cfg.VideoHeight}" : "-")}\n" +
            $"默认：{(string.IsNullOrEmpty(_cfg.DefaultVideoPath) ? "未设置（长按历史可设）" : _cfg.DefaultOriginalName)}\n" +
            $"填充：{(_cfg.FillMode == "Stretch" ? "拉伸填满" : "保比例裁剪")}   音量：{(_cfg.Volume <= 0 ? "静音" : $"{_cfg.Volume:P0}")}\n" +
            $"循环：{FormatMs(_cfg.TrimStartMs)} → {(_cfg.TrimEndMs > 0 ? FormatMs(_cfg.TrimEndMs) : "片尾")}";

        _volumeBar.Progress = (int)(_cfg.Volume * 100);
        _coverSwitch.Checked = _cfg.FillMode != "Stretch";

        // 当前壁纸缩略图（复用历史快照；缺失时即时补生成）
        RefreshCurrentThumb();

        bool trimmable = _cfg.DurationMs > 0;
        _trimStartBar.Enabled = trimmable;
        _trimEndBar.Enabled = trimmable;
        if (trimmable)
        {
            _trimStartBar.Progress = (int)(_cfg.TrimStartMs * 1000 / _cfg.DurationMs);
            _trimEndBar.Progress = (int)((_cfg.TrimEndMs > 0 ? _cfg.TrimEndMs : _cfg.DurationMs) * 1000 / _cfg.DurationMs);
        }
        UpdateTrimPreview();
        RefreshHistoryUi();
    }

    void UpdateTrimPreview()
    {
        if (_cfg.DurationMs <= 0)
        {
            _trimInfoText.Text = "（未知视频时长，裁剪不可用）";
            return;
        }
        long start = _trimStartBar.Progress * _cfg.DurationMs / 1000;
        long end = _trimEndBar.Progress * _cfg.DurationMs / 1000;
        if (end - start < 500) end = Math.Min(_cfg.DurationMs, start + 500);
        _trimInfoText.Text = $"循环片段：{FormatMs(start)} → {FormatMs(end)}（{FormatMs(end - start)}）";
    }

    void SaveSettings()
    {
        _cfg.Volume = _volumeBar.Progress / 100f;
        _cfg.FillMode = _coverSwitch.Checked ? "Cover" : "Stretch";

        if (_cfg.DurationMs > 0)
        {
            long start = _trimStartBar.Progress * _cfg.DurationMs / 1000;
            long end = _trimEndBar.Progress * _cfg.DurationMs / 1000;
            if (end - start < 500) end = Math.Min(_cfg.DurationMs, start + 500);
            _cfg.TrimStartMs = Math.Max(0, start);
            _cfg.TrimEndMs = end >= _cfg.DurationMs ? 0 : end;
        }

        _cfg.Save(FilesDir!.AbsolutePath);
        VideoWallpaperService.NotifyConfigChanged();
        RefreshUi();
        Log.Info(Tag, $"设置已保存 音量={_cfg.Volume} 填充={_cfg.FillMode} 裁剪={_cfg.TrimStartMs}~{_cfg.TrimEndMs}");
    }

    void RefreshCurrentThumb()
    {
        try
        {
            string id = TryHashFile(_cfg.VideoPath);
            string thumbPath = System.IO.Path.Combine(FilesDir!.AbsolutePath, "thumbs", $"{id}.jpg");
            if (!File.Exists(thumbPath) && id.Length > 0)
            {
                GenerateThumbnail(_cfg.VideoPath, id);
            }
            var bmp = File.Exists(thumbPath) ? BitmapFactory.DecodeFile(thumbPath) : null;
            if (bmp != null) _currentThumb.SetImageBitmap(bmp);
            else _currentThumb.SetImageDrawable(null!);
            _currentThumb.SetBackgroundColor(bmp != null ? Color.Transparent : new Color(45, 49, 62));
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"缩略图刷新失败: {ex.Message}");
        }
    }

    static string FormatMs(long ms) => $"{ms / 1000.0:F1}s";

    // ---------- 历史壁纸 ----------

    void RefreshHistoryUi()
    {
        _sheetRow.RemoveAllViews();
        _favRow.RemoveAllViews();
        var history = WallpaperHistory.Load(FilesDir!.AbsolutePath);

        string currentId = TryHashFile(_cfg.VideoPath);
        string defaultId = TryHashFile(_cfg.DefaultVideoPath);

        // 收藏区
        var favorites = history.Entries.Where(e => e.IsFavorite).ToList();
        if (favorites.Count == 0)
        {
            var favEmpty = Ui.Caption(this, "暂无收藏");
            favEmpty.SetPadding(Ui.Dp(this, 4), Ui.Dp(this, 10), 0, Ui.Dp(this, 10));
            _favRow.AddView(favEmpty);
        }
        else
        {
            foreach (var entry in favorites)
            {
                _favRow.AddView(MakeHistoryItem(entry, entry.Id == currentId, entry.Id == defaultId && entry.Id.Length > 0, true));
            }
        }

        // 历史区
        if (history.Entries.Count == 0)
        {
            var empty = Ui.Caption(this, "暂无历史，选择视频后自动记录");
            empty.SetPadding(Ui.Dp(this, 4), Ui.Dp(this, 10), 0, Ui.Dp(this, 10));
            _sheetRow.AddView(empty);
            return;
        }
        foreach (var entry in history.Entries)
        {
            _sheetRow.AddView(MakeHistoryItem(entry, entry.Id == currentId, entry.Id == defaultId && entry.Id.Length > 0, entry.IsFavorite));
        }
    }

    View MakeHistoryItem(WallpaperHistoryEntry entry, bool isCurrent, bool isDefault, bool isFavorite = false)
    {
        int w = Ui.Dp(this, 104), h = Ui.Dp(this, 185), radius = Ui.Dp(this, 14);
        var frame = new FrameLayout(this);
        var lp = new LinearLayout.LayoutParams(w, h);
        lp.RightMargin = Ui.Dp(this, 10);
        frame.LayoutParameters = lp;

        var img = new ImageView(this);
        img.SetScaleType(ImageView.ScaleType.CenterCrop!);
        string thumbPath = System.IO.Path.Combine(FilesDir!.AbsolutePath, "thumbs", $"{entry.Id}.jpg");
        var bmp = BitmapFactory.DecodeFile(thumbPath);
        if (bmp != null) img.SetImageBitmap(bmp);
        else img.SetBackgroundColor(new Color(45, 49, 62));
        img.OutlineProvider = new RoundOutlineProvider(radius);
        img.ClipToOutline = true;
        frame.AddView(img, new FrameLayout.LayoutParams(-1, -1));

        // 底部名称条
        var nameStrip = new TextView(this) { Text = entry.OriginalName, TextSize = 10.5f, Gravity = GravityFlags.Center };
        nameStrip.SetTextColor(Color.White);
        nameStrip.SetBackgroundColor(Color.Argb(190, 0, 0, 0));
        nameStrip.SetPadding(Ui.Dp(this, 4), Ui.Dp(this, 4), Ui.Dp(this, 4), Ui.Dp(this, 4));
        nameStrip.SetSingleLine(true);
        nameStrip.Ellipsize = TextUtils.TruncateAt.End!;
        frame.AddView(nameStrip, new FrameLayout.LayoutParams(-1, -2, GravityFlags.Bottom));

        if (isCurrent)
        {
            var border = new View(this);
            border.SetBackgroundDrawable(Ui.RoundBg(Color.Transparent, this, 14, Ui.Accent, 2));
            frame.AddView(border, new FrameLayout.LayoutParams(-1, -1));
            var tag = new TextView(this) { Text = "使用中", TextSize = 10f, Gravity = GravityFlags.Center };
            tag.SetTextColor(Color.White);
            tag.SetBackgroundColor(Color.Argb(200, 64, 196, 255));
            frame.AddView(tag, new FrameLayout.LayoutParams(-1, Ui.Dp(this, 20), GravityFlags.Top));
        }

        if (isDefault)
        {
            var defaultTag = new TextView(this) { Text = "默认", TextSize = 10f, Gravity = GravityFlags.Center };
            defaultTag.SetTextColor(Color.Black);
            defaultTag.SetBackgroundColor(Color.Argb(210, 255, 213, 79));
            frame.AddView(defaultTag, new FrameLayout.LayoutParams(Ui.Dp(this, 44), Ui.Dp(this, 18), GravityFlags.Bottom | GravityFlags.Right));
        }

        if (isFavorite)
        {
            var favTag = new TextView(this) { Text = "收藏", TextSize = 10f, Gravity = GravityFlags.Center };
            favTag.SetTextColor(Color.Black);
            favTag.SetBackgroundColor(Color.Argb(210, 255, 213, 79));
            frame.AddView(favTag, new FrameLayout.LayoutParams(Ui.Dp(this, 40), Ui.Dp(this, 18), GravityFlags.Top | GravityFlags.Left));
        }

        frame.Click += (_, _) => ApplyHistoryEntry(entry);
        frame.LongClick += (_, _) => ShowHistoryItemMenu(entry);
        return frame;
    }

    void ShowHistoryItemMenu(WallpaperHistoryEntry entry)
    {
        string name = string.IsNullOrEmpty(entry.OriginalName) ? "未命名视频" : entry.OriginalName;

        // 深色底部菜单卡：与历史面板同一设计语言
        var dialog = new Dialog(this);
        dialog.RequestWindowFeature((int)WindowFeatures.NoTitle);

        var wrapper = new FrameLayout(this);
        wrapper.SetPadding(Ui.Dp(this, 16), 0, Ui.Dp(this, 16), Ui.Dp(this, 32));

        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        panel.SetBackgroundDrawable(Ui.RoundBg(new Color(26, 29, 38), this, 22, Ui.CardStroke, 1));
        panel.SetPadding(0, Ui.Dp(this, 14), 0, Ui.Dp(this, 8));

        var title = new TextView(this) { Text = name, TextSize = 15f };
        title.SetTextColor(Ui.TextPrimary);
        title.Typeface = Typeface.Create(Typeface.DefaultBold, TypefaceStyle.Bold);
        title.SetSingleLine(true);
        title.Ellipsize = TextUtils.TruncateAt.End!;
        title.SetPadding(Ui.Dp(this, 20), 0, Ui.Dp(this, 20), Ui.Dp(this, 10));
        panel.AddView(title);

        panel.AddView(MakeMenuItem(dialog, "设为默认壁纸", Ui.Gold, () => SetAsDefault(entry)));
        panel.AddView(MakeMenuItem(dialog, entry.IsFavorite ? "取消收藏" : "收藏", Ui.Accent, () => ToggleFavorite(entry)));
        panel.AddView(MakeMenuItem(dialog, "从历史中删除", Ui.Destructive, () => ConfirmDeleteHistoryEntry(entry)));

        wrapper.AddView(panel, new FrameLayout.LayoutParams(-1, -2));
        dialog.SetContentView(wrapper);

        var window = dialog.Window!;
        window.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
        window.SetGravity(GravityFlags.Bottom);
        window.SetLayout(-1, -2);
        dialog.Show();
    }

    /// <summary>设置抽屉内的一行菜单项（文字 + 右箭头）</summary>
    View MakeMenuItemRow(string text, Action onClick)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(Ui.Dp(this, 4), Ui.Dp(this, 13), Ui.Dp(this, 4), Ui.Dp(this, 13));

        var label = new TextView(this) { Text = text, TextSize = 15f };
        label.SetTextColor(Ui.TextPrimary);
        label.LayoutParameters = new LinearLayout.LayoutParams(0, -2, 1f);
        row.AddView(label);

        var arrow = new TextView(this) { Text = "›", TextSize = 18f };
        arrow.SetTextColor(Ui.TextSecondary);
        row.AddView(arrow);

        row.Click += (_, _) => onClick();
        return row;
    }

    View MakeDivider()
    {
        var line = new View(this);
        line.SetBackgroundColor(Ui.CardStroke);
        line.LayoutParameters = new LinearLayout.LayoutParams(-1, 1);
        return line;
    }

    View MakeMenuItem(Dialog dialog, string text, Color textColor, Action onClick)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(Ui.Dp(this, 20), Ui.Dp(this, 13), Ui.Dp(this, 20), Ui.Dp(this, 13));

        var label = new TextView(this) { Text = text, TextSize = 15f };
        label.SetTextColor(textColor);
        row.AddView(label);

        row.Click += (_, _) =>
        {
            dialog.Dismiss();
            onClick();
        };
        return row;
    }

    void ToggleFavorite(WallpaperHistoryEntry entry)
    {
        try
        {
            var history = WallpaperHistory.Load(FilesDir!.AbsolutePath);
            var target = history.Find(entry.Id);
            if (target == null) return;

            target.IsFavorite = !target.IsFavorite;
            history.Save(FilesDir!.AbsolutePath);
            Log.Info(Tag, $"收藏标记: {entry.OriginalName} → {target.IsFavorite}");
            RefreshUi();
            Toast.MakeText(this, target.IsFavorite ? "已加入收藏" : "已取消收藏", ToastLength.Short)?.Show();
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"收藏切换失败: {ex}");
            Toast.MakeText(this, $"操作失败: {ex.Message}", ToastLength.Long)?.Show();
        }
    }

    void SetAsDefault(WallpaperHistoryEntry entry)
    {
        try
        {
            if (!File.Exists(entry.VideoPath))
            {
                Toast.MakeText(this, "该视频文件已丢失", ToastLength.Short)?.Show();
                return;
            }
            _cfg.DefaultVideoPath = entry.VideoPath;
            _cfg.DefaultOriginalName = entry.OriginalName;
            _cfg.Save(FilesDir!.AbsolutePath);
            Log.Info(Tag, $"默认壁纸已设置: {entry.OriginalName}");
            RefreshUi();
            Toast.MakeText(this, $"已将「{entry.OriginalName}」设为默认壁纸", ToastLength.Short)?.Show();
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"设置默认失败: {ex}");
            Toast.MakeText(this, $"设置失败: {ex.Message}", ToastLength.Long)?.Show();
        }
    }

    void ConfirmDeleteHistoryEntry(WallpaperHistoryEntry entry)
    {
        string name = string.IsNullOrEmpty(entry.OriginalName) ? "未命名视频" : entry.OriginalName;
        ShowConfirmDialog("删除历史壁纸", $"从历史中删除「{name}」？\n对应的视频文件和缩略图也会被清理。", "删除", Ui.Destructive, () => DeleteHistoryEntry(entry));
    }

    /// <summary>深色风格确认对话框（与整体设计语言一致）</summary>
    void ShowConfirmDialog(string title, string message, string okText, Color okColor, Action onOk)
    {
        var dialog = new Dialog(this);
        dialog.RequestWindowFeature((int)WindowFeatures.NoTitle);

        var wrapper = new FrameLayout(this);
        wrapper.SetPadding(Ui.Dp(this, 24), 0, Ui.Dp(this, 24), 0);

        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        panel.SetBackgroundDrawable(Ui.RoundBg(new Color(26, 29, 38), this, 22, Ui.CardStroke, 1));
        panel.SetPadding(Ui.Dp(this, 22), Ui.Dp(this, 18), Ui.Dp(this, 22), Ui.Dp(this, 16));

        var titleView = new TextView(this) { Text = title, TextSize = 16f };
        titleView.SetTextColor(Ui.TextPrimary);
        titleView.Typeface = Typeface.Create(Typeface.DefaultBold, TypefaceStyle.Bold);
        panel.AddView(titleView);

        var msgView = new TextView(this) { Text = message, TextSize = 13.5f };
        msgView.SetTextColor(Ui.TextSecondary);
        msgView.SetPadding(0, Ui.Dp(this, 10), 0, Ui.Dp(this, 18));
        panel.AddView(msgView);

        var buttonRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var cancel = Ui.SecondaryButton(this, "取消");
        cancel.LayoutParameters = new LinearLayout.LayoutParams(0, Ui.Dp(this, 46), 1f);
        cancel.Click += (_, _) => dialog.Dismiss();
        buttonRow.AddView(cancel);

        var gap = new View(this);
        gap.LayoutParameters = new LinearLayout.LayoutParams(Ui.Dp(this, 10), 1);
        buttonRow.AddView(gap);

        var ok = Ui.PrimaryButton(this, okText);
        ok.LayoutParameters = new LinearLayout.LayoutParams(0, Ui.Dp(this, 46), 1f);
        ok.SetBackgroundDrawable(Ui.RoundBg(okColor, this, 16));
        ok.Click += (_, _) =>
        {
            dialog.Dismiss();
            onOk();
        };
        buttonRow.AddView(ok);
        panel.AddView(buttonRow);

        wrapper.AddView(panel, new FrameLayout.LayoutParams(-1, -2));
        dialog.SetContentView(wrapper);

        var window = dialog.Window!;
        window.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
        window.SetLayout(-1, -2);
        dialog.Show();
    }

    void DeleteHistoryEntry(WallpaperHistoryEntry entry)
    {
        try
        {
            var cfg = WallpaperConfig.LoadOrDefault(FilesDir!.AbsolutePath);
            if (entry.VideoPath == cfg.VideoPath)
            {
                Toast.MakeText(this, "正在使用中，请先切换到其他壁纸", ToastLength.Short)?.Show();
                return;
            }
            if (entry.VideoPath == cfg.DefaultVideoPath)
            {
                Toast.MakeText(this, "默认壁纸不能删除，请先更换默认", ToastLength.Short)?.Show();
                return;
            }

            var history = WallpaperHistory.Load(FilesDir!.AbsolutePath);
            if (history.Remove(entry.Id) == null) return;

            // 清理视频文件与缩略图（失败不阻断记录删除）
            try { File.Delete(entry.VideoPath); } catch { /* 引擎可能仍在释放 */ }
            try { File.Delete(System.IO.Path.Combine(FilesDir!.AbsolutePath, "thumbs", $"{entry.Id}.jpg")); } catch { }

            history.Save(FilesDir!.AbsolutePath);
            Log.Info(Tag, $"历史删除: {entry.OriginalName}");
            RefreshUi();
            Toast.MakeText(this, "已删除", ToastLength.Short)?.Show();
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"历史删除失败: {ex}");
            Toast.MakeText(this, $"删除失败: {ex.Message}", ToastLength.Long)?.Show();
        }
    }

    void ApplyHistoryEntry(WallpaperHistoryEntry entry)
    {
        if (!File.Exists(entry.VideoPath))
        {
            Toast.MakeText(this, "该视频文件已丢失", ToastLength.Short)?.Show();
            return;
        }

        var (duration, width, height) = ReadVideoMetadata(entry.VideoPath);
        long dur = duration > 0 ? duration : entry.DurationMs;

        _cfg = WallpaperConfig.LoadOrDefault(FilesDir!.AbsolutePath);
        _cfg.VideoPath = entry.VideoPath;
        _cfg.OriginalName = entry.OriginalName;
        _cfg.DurationMs = dur;
        _cfg.VideoWidth = width;
        _cfg.VideoHeight = height;
        _cfg.Save(FilesDir!.AbsolutePath);

        RecordUsage(entry.VideoPath, entry.OriginalName, dur);
        VideoWallpaperService.NotifyConfigChanged();
        NotifyChangedVideo();
        RefreshUi();
    }

    /// <summary>
    /// 记录一次使用（内容 hash 去重）：重复视频删除新副本、用既有路径并置顶；
    /// 新视频生成缩略图、入历史，超限淘汰最旧（连带清理文件）。返回最终使用的视频路径。
    /// </summary>
    string RecordUsage(string newVideoPath, string originalName, long durationMs)
    {
        try
        {
            string id = HashFile(newVideoPath);
            var history = WallpaperHistory.Load(FilesDir!.AbsolutePath);

            var existing = history.Find(id);
            if (existing != null)
            {
                if (newVideoPath != existing.VideoPath)
                {
                    try { File.Delete(newVideoPath); } catch { /* 忽略清理失败 */ }
                }
                history.Record(new WallpaperHistoryEntry { Id = id, VideoPath = existing.VideoPath, OriginalName = originalName, DurationMs = durationMs });
                history.Save(FilesDir!.AbsolutePath);
                return existing.VideoPath;
            }

            GenerateThumbnail(newVideoPath, id);
            var evicted = history.Record(new WallpaperHistoryEntry { Id = id, VideoPath = newVideoPath, OriginalName = originalName, DurationMs = durationMs });
            if (evicted != null) CleanupEvicted(evicted);
            history.Save(FilesDir!.AbsolutePath);
            return newVideoPath;
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"历史记录失败（不影响使用）: {ex.Message}");
            return newVideoPath;
        }
    }

    void CleanupEvicted(WallpaperHistoryEntry evicted)
    {
        try
        {
            var cfg = WallpaperConfig.LoadOrDefault(FilesDir!.AbsolutePath);
            if (evicted.VideoPath == cfg.VideoPath) return;
            if (evicted.VideoPath == cfg.DefaultVideoPath) return; // 用户默认不可被淘汰清理
            File.Delete(evicted.VideoPath);
            File.Delete(System.IO.Path.Combine(FilesDir!.AbsolutePath, "thumbs", $"{evicted.Id}.jpg"));
            Log.Info(Tag, $"历史淘汰并清理: {evicted.OriginalName}");
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"清理淘汰项失败: {ex.Message}");
        }
    }

    static string HashFile(string path)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(md5.ComputeHash(stream))[..16].ToLowerInvariant();
    }

    static string TryHashFile(string? path)
    {
        try { return path != null && File.Exists(path) ? HashFile(path) : ""; }
        catch { return ""; }
    }

    void GenerateThumbnail(string videoPath, string id)
    {
        var thumbsDir = new Java.IO.File(FilesDir, "thumbs");
        if (!thumbsDir.Exists()) thumbsDir.Mkdirs();
        var target = new Java.IO.File(thumbsDir, $"{id}.jpg");
        if (target.Exists()) return;

        using var retriever = new MediaMetadataRetriever();
        retriever.SetDataSource(videoPath);
        using Bitmap? frame = retriever.GetFrameAtTime(0);
        if (frame == null) return;

        int width = 240;
        int height = Math.Max(1, frame.Height * width / frame.Width);
        using Bitmap scaled = Bitmap.CreateScaledBitmap(frame, width, height, true)!;
        using var output = System.IO.File.Create(target.AbsolutePath);
        scaled.Compress(Bitmap.CompressFormat.Jpeg!, 82, output);
    }

    // ---------- 设为壁纸 ----------

    void RequestSetWallpaper()
    {
        var component = new ComponentName(this, Java.Lang.Class.FromType(typeof(VideoWallpaperService)));
        var intent = new Intent(WallpaperManager.ActionChangeLiveWallpaper)
            .PutExtra(WallpaperManager.ExtraLiveWallpaperComponent, component);
        try
        {
            StartActivity(intent);
        }
        catch (ActivityNotFoundException)
        {
            Log.Warn(Tag, "ACTION_CHANGE_LIVE_WALLPAPER 不可用，退回通用选择器");
            try { StartActivity(new Intent(WallpaperManager.ActionLiveWallpaperChooser)); }
            catch (ActivityNotFoundException) { Toast.MakeText(this, "未找到动态壁纸选择器，请在系统设置中手动设置", ToastLength.Long)?.Show(); }
        }
    }

    /// <summary>换视频/恢复默认后的统一反馈：引擎未激活时明确引导</summary>
    void NotifyChangedVideo()
    {
        if (VideoWallpaperService.IsEngineRunning)
            Toast.MakeText(this, "已更换视频，回桌面查看", ToastLength.Short)?.Show();
        else
            Toast.MakeText(this, "视频已保存，但壁纸引擎未激活：请先点上方横幅设为壁纸", ToastLength.Long)?.Show();
    }

    // ---------- 选择视频（SAF → 复制 → 元数据 → 去重记录 → 配置 → 通知） ----------

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

        var sourceUri = data.Data!;
        try
        {
            string copiedPath = ImportVideo(sourceUri);
            var (duration, width, height) = ReadVideoMetadata(copiedPath);
            string name = sourceUri.LastPathSegment ?? "自定义视频";
            string finalPath = RecordUsage(copiedPath, name, duration);

            _cfg = WallpaperConfig.LoadOrDefault(FilesDir!.AbsolutePath);
            _cfg.VideoPath = finalPath;
            _cfg.OriginalName = name;
            _cfg.DurationMs = duration;
            _cfg.VideoWidth = width;
            _cfg.VideoHeight = height;
            _cfg.Save(FilesDir.AbsolutePath);

            VideoWallpaperService.NotifyConfigChanged();
            NotifyChangedVideo();
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"导入失败: {ex}");
            Toast.MakeText(this, $"导入失败: {ex.Message}", ToastLength.Long)?.Show();
        }
        _cfg = WallpaperConfig.LoadOrDefault(FilesDir!.AbsolutePath);
        RefreshUi();
    }

    string ImportVideo(Uri sourceUri)
    {
        var videosDir = new Java.IO.File(FilesDir, "videos");
        if (!videosDir.Exists()) videosDir.Mkdirs();

        var target = new Java.IO.File(videosDir, $"wallpaper_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        using var input = ContentResolver!.OpenInputStream(sourceUri)!;
        using var output = System.IO.File.Create(target.AbsolutePath);
        input.CopyTo(output);
        output.Flush();
        return target.AbsolutePath;
        // 旧文件清理由历史淘汰机制接管（CleanupEvicted），此处不再即时删除
    }

    (long duration, int width, int height) ReadVideoMetadata(string path)
    {
        try
        {
            using var retriever = new MediaMetadataRetriever();
            retriever.SetDataSource(path);
            long duration = 0;
            long.TryParse(retriever.ExtractMetadata(MetadataKey.Duration) ?? "", out duration);
            int width = 0, height = 0;
            int.TryParse(retriever.ExtractMetadata(MetadataKey.VideoWidth) ?? "", out width);
            int.TryParse(retriever.ExtractMetadata(MetadataKey.VideoHeight) ?? "", out height);
            return (duration, width, height);
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"元数据读取失败（不影响导入）: {ex.Message}");
            return (0, 0, 0);
        }
    }

    // ---------- 默认素材 ----------

    /// <summary>返回默认素材路径（不存在则从 Assets 重新释放），并读取元数据回填</summary>
    string GetDefaultVideoPath()
    {
        var target = new Java.IO.File(new Java.IO.File(FilesDir, "videos"), DefaultVideoName);
        if (!target.Exists())
        {
            var videosDir = target.ParentFile!;
            if (!videosDir.Exists()) videosDir.Mkdirs();
            using var input = Assets!.Open(DefaultVideoName);
            using var output = System.IO.File.Create(target.AbsolutePath);
            input.CopyTo(output);
            output.Flush();
        }
        return target.AbsolutePath;
    }

    void EnsureDefaultVideo()
    {
        var cfg = WallpaperConfig.LoadOrDefault(FilesDir!.AbsolutePath);
        if (!string.IsNullOrEmpty(cfg.VideoPath) && File.Exists(cfg.VideoPath)) return;

        string path = GetDefaultVideoPath();
        var (duration, width, height) = ReadVideoMetadata(path);
        new WallpaperConfig { VideoPath = path, OriginalName = "灰原哀（默认）", DurationMs = duration, VideoWidth = width, VideoHeight = height }
            .Save(FilesDir!.AbsolutePath);
        Log.Info(Tag, $"默认配置已生成: {path}");
    }

    /// <summary>
    /// 恢复默认（三层语义）：标记过默认→切换到该视频；
    /// 未标记→还原系统原生壁纸（引擎退场）；标记失效（文件丢失）→同样还原系统壁纸。
    /// </summary>
    void ResetToDefault()
    {
        try
        {
            bool hasDefault = !string.IsNullOrEmpty(_cfg.DefaultVideoPath) && File.Exists(_cfg.DefaultVideoPath);
            if (hasDefault)
            {
                if (_cfg.DefaultVideoPath == _cfg.VideoPath)
                {
                    Toast.MakeText(this, "当前已是默认壁纸", ToastLength.Short)?.Show();
                    return;
                }
                var entry = new WallpaperHistoryEntry
                {
                    VideoPath = _cfg.DefaultVideoPath,
                    OriginalName = _cfg.DefaultOriginalName,
                };
                ApplyHistoryEntry(entry); // 内部会弹应用范围选择
                return;
            }

            // 未设置默认：还原系统原生壁纸
            var wm = WallpaperManager.GetInstance(this)!;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
            {
                wm.Clear(WallpaperManagerFlags.System);
                wm.Clear(WallpaperManagerFlags.Lock);
            }
            else
            {
                wm.Clear();
            }
            Log.Info(Tag, "恢复默认：未设置用户默认，已还原系统壁纸");
            Toast.MakeText(this, "已恢复系统壁纸（可在历史中长按设为默认）", ToastLength.Long)?.Show();
            RefreshUi();
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"恢复默认失败: {ex}");
            Toast.MakeText(this, $"恢复默认失败: {ex.Message}", ToastLength.Long)?.Show();
        }
    }
}
