using Android.App;
using Android.Content;
using Android.Content.PM;
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

    TextView _deskChip = null!, _lockChip = null!, _trimInfoText = null!;
    TextView _desktopNameText = null!, _desktopStatusText = null!, _lockNameText = null!, _lockStatusText = null!;
    Button _pickDesktopButton = null!, _pickLockButton = null!, _resetDesktopButton = null!, _resetLockButton = null!;
    VideoTarget _pickTarget;
    bool _editLock;
    SeekBar _volumeBar = null!, _trimStartBar = null!, _trimEndBar = null!;
    Switch _coverSwitch = null!, _editLockSwitch = null!;
    LinearLayout _sheetRow = null!, _sheetPanel = null!, _favRow = null!;
    TextView _favLabel = null!, _historyLabel = null!;
    FrameLayout _sheetOverlay = null!;
    View _sheetScrim = null!;
    TextView _historyToggleButton = null!;
    ImageView _desktopThumb = null!, _lockThumb = null!;
    WallpaperConfig _cfg = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Title = "壁纸引擎";
        EnsureDefaultVideo();
        _cfg = WallpaperConfig.LoadOrDefault(FilesDir!.AbsolutePath);

        SetContentView(BuildUi());
        RefreshUi();
        RequestMediaReadIfNeeded();
    }

    const int PermMediaRequest = 101;

    /// <summary>双状态卡直读系统壁纸需要媒体读权限（Android 13+ 为 READ_MEDIA_IMAGES）</summary>
    void RequestMediaReadIfNeeded()
    {
        string perm = Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            ? Android.Manifest.Permission.ReadMediaImages
            : Android.Manifest.Permission.ReadExternalStorage;
        if (CheckSelfPermission(perm) == Permission.Granted) return;
        RequestPermissions(new[] { perm }, PermMediaRequest);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == PermMediaRequest)
        {
            Log.Info(Tag, $"媒体读权限: {(grantResults.Length > 0 && grantResults[0] == Permission.Granted ? "已授予" : "被拒绝（卡片显示回退为记录模式）")}");
            RefreshUi();
        }
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

    /// <summary>构建单张壁纸状态卡（标题 + 缩略图 + 名称 + 状态）</summary>
    View BuildWallpaperCard(string title, out ImageView thumb, out TextView nameText, out TextView statusText)
    {
        var card = Ui.MakeCard(this);
        card.SetPadding(Ui.Dp(this, 14), Ui.Dp(this, 12), Ui.Dp(this, 14), Ui.Dp(this, 12));

        var label = Ui.Label(this, title);
        label.TextSize = 13.5f;
        card.AddView(label);

        thumb = new ImageView(this);
        thumb.SetScaleType(ImageView.ScaleType.CenterCrop!);
        thumb.OutlineProvider = new RoundOutlineProvider(Ui.Dp(this, 10));
        thumb.ClipToOutline = true;
        thumb.SetBackgroundColor(new Color(45, 49, 62));
        var thumbLp = new LinearLayout.LayoutParams(-1, Ui.Dp(this, 150));
        thumbLp.SetMargins(0, Ui.Dp(this, 8), 0, Ui.Dp(this, 8));
        thumb.LayoutParameters = thumbLp;
        card.AddView(thumb);

        nameText = new TextView(this) { TextSize = 12f };
        nameText.SetTextColor(Ui.TextPrimary);
        nameText.SetSingleLine(true);
        nameText.Ellipsize = TextUtils.TruncateAt.End!;
        card.AddView(nameText);

        statusText = new TextView(this) { TextSize = 11f };
        statusText.SetTextColor(Ui.TextSecondary);
        statusText.SetPadding(0, Ui.Dp(this, 2), 0, 0);
        card.AddView(statusText);
        return card;
    }

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

        // —— 双引擎状态芯片（桌面/锁屏分别显示绑定与运行状态，点击进入绑定流程） ——
        var chipRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var chipRowLp = new LinearLayout.LayoutParams(-1, -2);
        chipRowLp.SetMargins(Ui.Dp(this, 16), Ui.Dp(this, 6), Ui.Dp(this, 16), Ui.Dp(this, 4));
        chipRow.LayoutParameters = chipRowLp;

        _deskChip = new TextView(this) { TextSize = 13.5f, Gravity = GravityFlags.Center };
        _deskChip.SetTextColor(Color.White);
        _deskChip.SetPadding(0, Ui.Dp(this, 14), 0, Ui.Dp(this, 14));
        var deskChipLp = new LinearLayout.LayoutParams(0, -2, 1f);
        deskChipLp.SetMargins(0, 0, Ui.Dp(this, 6), 0);
        _deskChip.LayoutParameters = deskChipLp;
        _deskChip.Click += (_, _) => RequestSetWallpaper();
        chipRow.AddView(_deskChip);

        _lockChip = new TextView(this) { TextSize = 13.5f, Gravity = GravityFlags.Center };
        _lockChip.SetTextColor(Color.White);
        _lockChip.SetPadding(0, Ui.Dp(this, 14), 0, Ui.Dp(this, 14));
        var lockChipLp = new LinearLayout.LayoutParams(0, -2, 1f);
        lockChipLp.SetMargins(Ui.Dp(this, 6), 0, 0, 0);
        _lockChip.LayoutParameters = lockChipLp;
        _lockChip.Click += (_, _) => RequestSetWallpaper();
        chipRow.AddView(_lockChip);
        content.AddView(chipRow);

        // —— 双壁纸状态卡（桌面 | 锁屏） ——
        var cardsRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var cardsLp = new LinearLayout.LayoutParams(-1, -2);
        cardsLp.SetMargins(Ui.Dp(this, 16), Ui.Dp(this, 4), Ui.Dp(this, 16), Ui.Dp(this, 6));
        cardsRow.LayoutParameters = cardsLp;

        var deskCard = BuildWallpaperCard("桌面壁纸", out _desktopThumb, out _desktopNameText, out _desktopStatusText);
        var deskLp = new LinearLayout.LayoutParams(0, -2, 1f);
        deskLp.SetMargins(0, 0, Ui.Dp(this, 6), 0);
        cardsRow.AddView(deskCard, deskLp);

        var lockCard = BuildWallpaperCard("锁屏壁纸", out _lockThumb, out _lockNameText, out _lockStatusText);
        var lockLp = new LinearLayout.LayoutParams(0, -2, 1f);
        lockLp.SetMargins(Ui.Dp(this, 6), 0, 0, 0);
        cardsRow.AddView(lockCard, lockLp);
        content.AddView(cardsRow);

        // —— 操作按钮 2×2（桌面/锁屏各自的选择与恢复） ——
        var pickRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var pickRowLp = new LinearLayout.LayoutParams(-1, -2);
        pickRowLp.SetMargins(Ui.Dp(this, 16), Ui.Dp(this, 2), Ui.Dp(this, 16), Ui.Dp(this, 6));
        pickRow.LayoutParameters = pickRowLp;

        _pickDesktopButton = Ui.PrimaryButton(this, "选择桌面壁纸");
        _pickDesktopButton.LayoutParameters = new LinearLayout.LayoutParams(0, Ui.Dp(this, 50), 1f);
        _pickDesktopButton.Click += (_, _) => PickMedia(VideoTarget.Desktop);
        pickRow.AddView(_pickDesktopButton);

        var pickGap = new View(this);
        pickGap.LayoutParameters = new LinearLayout.LayoutParams(Ui.Dp(this, 12), 1);
        pickRow.AddView(pickGap);

        _pickLockButton = Ui.PrimaryButton(this, "选择锁屏壁纸");
        _pickLockButton.LayoutParameters = new LinearLayout.LayoutParams(0, Ui.Dp(this, 50), 1f);
        _pickLockButton.Click += (_, _) => PickMedia(VideoTarget.Lock);
        pickRow.AddView(_pickLockButton);
        content.AddView(pickRow);

        var resetRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var resetRowLp = new LinearLayout.LayoutParams(-1, -2);
        resetRowLp.SetMargins(Ui.Dp(this, 16), Ui.Dp(this, 2), Ui.Dp(this, 16), Ui.Dp(this, 6));
        resetRow.LayoutParameters = resetRowLp;

        _resetDesktopButton = Ui.SecondaryButton(this, "恢复桌面默认");
        _resetDesktopButton.LayoutParameters = new LinearLayout.LayoutParams(0, Ui.Dp(this, 48), 1f);
        _resetDesktopButton.Click += (_, _) => ResetSlotDefault(VideoTarget.Desktop);
        resetRow.AddView(_resetDesktopButton);

        var resetGap = new View(this);
        resetGap.LayoutParameters = new LinearLayout.LayoutParams(Ui.Dp(this, 12), 1);
        resetRow.AddView(resetGap);

        _resetLockButton = Ui.SecondaryButton(this, "恢复锁屏默认");
        _resetLockButton.LayoutParameters = new LinearLayout.LayoutParams(0, Ui.Dp(this, 48), 1f);
        _resetLockButton.Click += (_, _) => ResetSlotDefault(VideoTarget.Lock);
        resetRow.AddView(_resetLockButton);
        content.AddView(resetRow);

        // —— 播放设置卡片（含编辑对象切换 + 循环区间） ——
        var tuneCard = Ui.MakeCard(this);
        tuneCard.AddView(Ui.Label(this, "播放设置"));

        var targetLabel = Ui.Caption(this, "编辑对象");
        targetLabel.SetPadding(0, Ui.Dp(this, 12), 0, 0);
        tuneCard.AddView(targetLabel);
        _editLockSwitch = new Switch(this) { Checked = _editLock };
        _editLockSwitch.Text = "正在编辑：桌面";
        Ui.StyleSwitch(_editLockSwitch);
        _editLockSwitch.CheckedChange += (_, e) =>
        {
            _editLock = e.IsChecked;
            _editLockSwitch.Text = _editLock ? "正在编辑：锁屏" : "正在编辑：桌面";
            RefreshUi(); // 重载该槽的设置值
        };
        tuneCard.AddView(_editLockSwitch);

        var fillLabel = Ui.Caption(this, "保持比例裁剪铺满（关闭 = 拉伸变形）");
        fillLabel.SetPadding(0, Ui.Dp(this, 12), 0, 0);
        tuneCard.AddView(fillLabel);
        _coverSwitch = new Switch(this) { Checked = _cfg.FillMode != "Stretch" };
        Ui.StyleSwitch(_coverSwitch);
        _coverSwitch.CheckedChange += (_, _) => SaveSettings();
        tuneCard.AddView(_coverSwitch);

        var volLabel = Ui.Caption(this, "音量（默认静音）");
        volLabel.SetPadding(0, Ui.Dp(this, 12), 0, 0);
        tuneCard.AddView(volLabel);
        _volumeBar = new SeekBar(this) { Max = 100 };
        Ui.StyleSeek(_volumeBar);
        _volumeBar.StopTrackingTouch += (_, _) => SaveSettings();
        tuneCard.AddView(_volumeBar);

        var trimLabel = Ui.Caption(this, "循环区间（裁掉片头 / 片尾）");
        trimLabel.SetPadding(0, Ui.Dp(this, 16), 0, Ui.Dp(this, 6));
        tuneCard.AddView(trimLabel);
        _trimStartBar = MakeTrimBar();
        tuneCard.AddView(_trimStartBar);
        _trimEndBar = MakeTrimBar();
        tuneCard.AddView(_trimEndBar);
        _trimInfoText = Ui.Caption(this, "");
        _trimInfoText.SetPadding(0, Ui.Dp(this, 4), 0, 0);
        tuneCard.AddView(_trimInfoText);
        content.AddView(tuneCard);

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
        _favLabel = Ui.Label(this, "收藏");
        content.AddView(_favLabel);
        var favCaption = Ui.Caption(this, "长按历史中的壁纸可加入收藏");
        favCaption.SetPadding(0, Ui.Dp(this, 4), 0, Ui.Dp(this, 8));
        content.AddView(favCaption);
        var favScroll = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false };
        _favRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _favRow.SetPadding(0, 0, 0, Ui.Dp(this, 8));
        favScroll.AddView(_favRow);
        content.AddView(favScroll);

        // —— 历史壁纸区块 ——
        _historyLabel = Ui.Label(this, "历史");
        _historyLabel.SetPadding(0, Ui.Dp(this, 8), 0, 0);
        content.AddView(_historyLabel);
        var caption = Ui.Caption(this, "点击切换 · 长按更多操作");
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
        // —— 双引擎状态芯片：按槽直读系统实际状态（引擎身份 = 官方 WallpaperFlags；播放异常单独标示） ——
        var engineStatus = VideoWallpaperService.GetActiveEngineStatus();
        bool deskLive = engineStatus.Any(s => s.Flags == WallpaperManagerFlags.System);
        bool lockLive = engineStatus.Any(s => s.Flags == WallpaperManagerFlags.Lock);
        bool deskError = engineStatus.Any(s => s.Flags == WallpaperManagerFlags.System && s.Error != null);
        bool lockError = engineStatus.Any(s => s.Flags == WallpaperManagerFlags.Lock && s.Error != null);
        bool deskHasStatic = TryGetActualWallpaperBitmap(WallpaperManagerFlags.System, out _);
        bool lockHasStatic = TryGetActualWallpaperBitmap(WallpaperManagerFlags.Lock, out _);

        SetChip(_deskChip, "桌面",
            deskLive && deskError ? "播放异常" : deskLive ? "动态运行中" : deskHasStatic ? "静态壁纸" : "未设置",
            deskLive && deskError ? Ui.Destructive : deskLive ? Ui.Ok : deskHasStatic ? new Color(52, 64, 92) : Ui.Warn);
        SetChip(_lockChip, "锁屏",
            lockLive && lockError ? "播放异常" : lockLive ? "动态运行中" : lockHasStatic ? "静态画面" : deskLive ? "跟随桌面" : "未设置",
            lockLive && lockError ? Ui.Destructive : lockLive ? Ui.Ok : lockHasStatic ? new Color(52, 64, 92) : deskLive ? Ui.OkDark : Ui.Warn);

        // —— 双状态卡：直读系统实际壁纸（GetWallpaperFile），静态槽缩略图用系统真实画面 ——
        bool engineRunning = VideoWallpaperService.IsEngineRunning;
        bool hasVideo = !string.IsNullOrEmpty(_cfg.VideoPath) && File.Exists(_cfg.VideoPath);

        // 桌面卡
        bool deskStatic = TryGetActualWallpaperBitmap(WallpaperManagerFlags.System, out var deskBmp);
        if (deskStatic && deskBmp != null)
        {
            _desktopThumb.SetImageBitmap(deskBmp);
            _desktopThumb.SetBackgroundColor(Color.Transparent);
            _desktopNameText.Text = !string.IsNullOrEmpty(_cfg.DesktopStaticName) ? _cfg.DesktopStaticName : "静态壁纸";
            _desktopStatusText.Text = "静态图片（系统当前）";
        }
        else if (engineRunning && hasVideo)
        {
            _desktopNameText.Text = _cfg.OriginalName;
            _desktopStatusText.Text = "动态视频";
            SetCardThumb(_desktopThumb, _cfg.VideoPath);
        }
        else
        {
            _desktopNameText.Text = hasVideo ? _cfg.OriginalName : "系统壁纸";
            _desktopStatusText.Text = hasVideo ? "待激活（点横幅设置）" : "未托管";
            _desktopThumb.SetImageDrawable(null!);
            _desktopThumb.SetBackgroundColor(new Color(45, 49, 62));
        }

        // 锁屏卡（锁屏槽动态时显示锁屏槽自己的视频）
        bool lockStatic = TryGetActualWallpaperBitmap(WallpaperManagerFlags.Lock, out var lockBmp);
        if (lockStatic && lockBmp != null)
        {
            _lockThumb.SetImageBitmap(lockBmp);
            _lockThumb.SetBackgroundColor(Color.Transparent);
            _lockNameText.Text = !string.IsNullOrEmpty(_cfg.LockFrameName) ? _cfg.LockFrameName : "静态画面";
            _lockStatusText.Text = "静态画面（系统当前）";
        }
        else if (engineRunning && hasVideo)
        {
            bool hasOwnLockVideo = !string.IsNullOrEmpty(_cfg.LockVideoPath) && File.Exists(_cfg.LockVideoPath);
            _lockNameText.Text = hasOwnLockVideo ? _cfg.LockOriginalName : _cfg.OriginalName;
            _lockStatusText.Text = hasOwnLockVideo ? "动态视频（独立）" : "动态视频（同桌面）";
            SetCardThumb(_lockThumb, hasOwnLockVideo ? _cfg.LockVideoPath : _cfg.VideoPath);
        }
        else
        {
            _lockNameText.Text = "系统壁纸";
            _lockStatusText.Text = "未托管";
            _lockThumb.SetImageDrawable(null!);
            _lockThumb.SetBackgroundColor(new Color(45, 49, 62));
        }

        // —— 播放设置：按当前编辑对象（桌面/锁屏）载入对应槽的值 ——
        // 该槽当前为静态渲染时，音量/循环区间不适用 → 置灰不可交互
        bool editSlotStatic = _editLock ? lockHasStatic : deskHasStatic;

        _volumeBar.Progress = (int)(EditVolume * 100);
        _volumeBar.Enabled = !editSlotStatic;
        _volumeBar.Alpha = editSlotStatic ? 0.4f : 1f;
        _coverSwitch.Checked = EditFill != "Stretch";

        bool trimmable = EditDuration > 0 && !editSlotStatic;
        _trimStartBar.Enabled = trimmable;
        _trimEndBar.Enabled = trimmable;
        _trimStartBar.Alpha = editSlotStatic ? 0.4f : 1f;
        _trimEndBar.Alpha = editSlotStatic ? 0.4f : 1f;
        if (trimmable)
        {
            _trimStartBar.Progress = (int)(EditTrimStart * 1000 / EditDuration);
            _trimEndBar.Progress = (int)((EditTrimEnd > 0 ? EditTrimEnd : EditDuration) * 1000 / EditDuration);
        }
        _editSlotStatic = editSlotStatic;
        UpdateTrimPreview();
        RefreshHistoryUi();
    }

    /// <summary>状态芯片着色与文案</summary>
    void SetChip(TextView chip, string title, string status, Color c)
    {
        chip.Text = $"{title} · {status}";
        chip.SetBackgroundDrawable(Ui.RoundBg(c, this, 14));
    }

    /// <summary>
    /// 直读系统某目标槽的当前静态壁纸图（live wallpaper 时返回 false）。
    /// 双状态卡以此为准——本地跟踪只提供名称，画面以系统真实状态显示。
    /// </summary>
    bool TryGetActualWallpaperBitmap(WallpaperManagerFlags flag, out Bitmap? bmp)
    {
        bmp = null;
        try
        {
            string perm = Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
                ? Android.Manifest.Permission.ReadMediaImages
                : Android.Manifest.Permission.ReadExternalStorage;
            if (CheckSelfPermission(perm) != Permission.Granted) return false; // 无权限：卡片回退记录模式

            var wm = WallpaperManager.GetInstance(this)!;
            using var pfd = wm.GetWallpaperFile(flag);
            if (pfd == null) return false;
            bmp = BitmapFactory.DecodeFileDescriptor(pfd.FileDescriptor);
            return bmp != null;
        }
        catch (Exception ex)
        {
            Log.Info(Tag, $"GetWallpaperFile({flag}) 不可用: {ex.Message}");
            return false;
        }
    }

    /// <summary>卡片缩略图：取媒体 hash 对应的历史快照（缺失即时补生成）</summary>
    void SetCardThumb(ImageView thumb, string? mediaPath)
    {
        try
        {
            string id = TryHashFile(mediaPath);
            string thumbPath = string.IsNullOrEmpty(id) ? "" : System.IO.Path.Combine(FilesDir!.AbsolutePath, "thumbs", $"{id}.jpg");
            if (!string.IsNullOrEmpty(id) && !File.Exists(thumbPath) && mediaPath != null)
            {
                GenerateThumbnail(mediaPath, id);
            }
            var bmp = File.Exists(thumbPath) ? BitmapFactory.DecodeFile(thumbPath) : null;
            if (bmp != null) thumb.SetImageBitmap(bmp);
            else thumb.SetImageDrawable(null!);
            thumb.SetBackgroundColor(bmp != null ? Color.Transparent : new Color(45, 49, 62));
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"卡片缩略图失败: {ex.Message}");
        }
    }

    // 当前编辑槽的设置值与时长（音量/填充/裁剪按 _editLock 读写对应槽字段）
    float EditVolume => _editLock ? _cfg.LockVolume : _cfg.Volume;
    string EditFill => _editLock ? _cfg.LockFillMode : _cfg.FillMode;
    long EditTrimStart => _editLock ? _cfg.LockTrimStartMs : _cfg.TrimStartMs;
    long EditTrimEnd => _editLock ? _cfg.LockTrimEndMs : _cfg.TrimEndMs;
    long EditDuration => _editLock ? _cfg.LockDurationMs : _cfg.DurationMs;
    bool _editSlotStatic;

    void UpdateTrimPreview()
    {
        if (_editSlotStatic)
        {
            _trimInfoText.Text = "当前为静态画面，无循环设置";
            return;
        }
        if (EditDuration <= 0)
        {
            _trimInfoText.Text = "（未知视频时长，裁剪不可用）";
            return;
        }
        long start = _trimStartBar.Progress * EditDuration / 1000;
        long end = _trimEndBar.Progress * EditDuration / 1000;
        if (end - start < 500) end = Math.Min(EditDuration, start + 500);
        _trimInfoText.Text = $"循环片段：{FormatMs(start)} → {FormatMs(end)}（{FormatMs(end - start)}）";
    }

    void SaveSettings()
    {
        _cfg = WallpaperConfig.LoadOrDefault(FilesDir!.AbsolutePath);
        float vol = _volumeBar.Progress / 100f;
        string fill = _coverSwitch.Checked ? "Cover" : "Stretch";
        long trimStart = 0, trimEnd = 0;
        if (EditDuration > 0)
        {
            trimStart = _trimStartBar.Progress * EditDuration / 1000;
            trimEnd = _trimEndBar.Progress * EditDuration / 1000;
            if (trimEnd - trimStart < 500) trimEnd = Math.Min(EditDuration, trimStart + 500);
            trimStart = Math.Max(0, trimStart);
            trimEnd = trimEnd >= EditDuration ? 0 : trimEnd;
        }

        if (_editLock)
        {
            _cfg.LockVolume = vol;
            _cfg.LockFillMode = fill;
            _cfg.LockTrimStartMs = trimStart;
            _cfg.LockTrimEndMs = trimEnd;
        }
        else
        {
            _cfg.Volume = vol;
            _cfg.FillMode = fill;
            _cfg.TrimStartMs = trimStart;
            _cfg.TrimEndMs = trimEnd;
        }

        _cfg.Save(FilesDir!.AbsolutePath);
        VideoWallpaperService.NotifyConfigChanged();
        RefreshUi();
        Log.Info(Tag, $"设置已保存[({(_editLock ? "Lock" : "System")})] 音量={vol} 填充={fill} 裁剪={trimStart}~{trimEnd}");
    }

    static string FormatMs(long ms) => $"{ms / 1000.0:F1}s";

    // ---------- 历史壁纸 ----------

    void RefreshHistoryUi()
    {
        _sheetRow.RemoveAllViews();
        _favRow.RemoveAllViews();
        var history = WallpaperHistory.Load(FilesDir!.AbsolutePath);
        var favorites = history.Entries.Where(e => e.IsFavorite).ToList();

        // 区块标题带容量计数（收藏为历史条目的标记子集，容量随历史上限）
        _favLabel.Text = $"收藏 ({favorites.Count}/{WallpaperHistory.MaxEntries})";
        _historyLabel.Text = $"历史 ({history.Entries.Count}/{WallpaperHistory.MaxEntries})";

        // 使用中判定以"引擎实际渲染/系统实际静态"为准（官方 WallpaperFlags 身份），角标区分桌面/锁屏
        var activeFlags = VideoWallpaperService.GetActiveEngineFlags();
        bool deskLive = activeFlags.Contains(WallpaperManagerFlags.System);
        bool lockLive = activeFlags.Contains(WallpaperManagerFlags.Lock);
        bool deskHasStatic = TryGetActualWallpaperBitmap(WallpaperManagerFlags.System, out _);
        bool lockHasStatic = TryGetActualWallpaperBitmap(WallpaperManagerFlags.Lock, out _);

        string? InUseLabel(WallpaperHistoryEntry e)
        {
            // 动态槽：路径匹配 + 该端引擎正在渲染；静态槽：路径匹配 + 该端当前为静态渲染
            bool d = (e.VideoPath == _cfg.VideoPath && deskLive)
                     || (!string.IsNullOrEmpty(_cfg.DesktopStaticPath) && e.VideoPath == _cfg.DesktopStaticPath && deskHasStatic);
            bool l = (e.VideoPath == _cfg.LockVideoPath && lockLive)
                     || (!string.IsNullOrEmpty(_cfg.LockFramePath) && e.VideoPath == _cfg.LockFramePath && lockHasStatic);
            if (d && l) return "使用中";
            if (d) return "桌面使用中";
            if (l) return "锁屏使用中";
            return null;
        }
        bool IsDeskDefault(WallpaperHistoryEntry e) =>
            !string.IsNullOrEmpty(_cfg.DefaultVideoPath) && e.VideoPath == _cfg.DefaultVideoPath;
        bool IsLockDefault(WallpaperHistoryEntry e) =>
            !string.IsNullOrEmpty(_cfg.LockDefaultVideoPath) && e.VideoPath == _cfg.LockDefaultVideoPath;
        // "默认"角标按槽区分：桌面默认 / 锁屏默认 / 默认（双槽）
        string? DefaultLabel(WallpaperHistoryEntry e)
        {
            bool d = IsDeskDefault(e);
            bool l = IsLockDefault(e);
            if (d && l) return "默认";
            if (d) return "桌面默认";
            if (l) return "锁屏默认";
            return null;
        }

        // 收藏区
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
                _favRow.AddView(MakeHistoryItem(entry, InUseLabel(entry), DefaultLabel(entry), true));
            }
        }

        // 历史区
        if (history.Entries.Count == 0)
        {
            var empty = Ui.Caption(this, "暂无历史，选择壁纸后自动记录");
            empty.SetPadding(Ui.Dp(this, 4), Ui.Dp(this, 10), 0, Ui.Dp(this, 10));
            _sheetRow.AddView(empty);
            return;
        }
        foreach (var entry in history.Entries)
        {
            _sheetRow.AddView(MakeHistoryItem(entry, InUseLabel(entry), DefaultLabel(entry), entry.IsFavorite));
        }
    }

    View MakeHistoryItem(WallpaperHistoryEntry entry, string? currentLabel, string? defaultLabel, bool isFavorite = false)
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

        if (currentLabel != null)
        {
            var border = new View(this);
            border.SetBackgroundDrawable(Ui.RoundBg(Color.Transparent, this, 14, Ui.Accent, 2));
            frame.AddView(border, new FrameLayout.LayoutParams(-1, -1));
            var tag = new TextView(this) { Text = currentLabel, TextSize = 10f, Gravity = GravityFlags.Center };
            tag.SetTextColor(Color.White);
            tag.SetBackgroundColor(Color.Argb(200, 64, 196, 255));
            frame.AddView(tag, new FrameLayout.LayoutParams(-1, Ui.Dp(this, 20), GravityFlags.Top));
        }

        if (defaultLabel != null)
        {
            var defaultTag = new TextView(this) { Text = defaultLabel, TextSize = 10f, Gravity = GravityFlags.Center };
            defaultTag.SetTextColor(Color.Black);
            defaultTag.SetBackgroundColor(Color.Argb(210, 255, 213, 79));
            defaultTag.SetPadding(Ui.Dp(this, 5), 0, Ui.Dp(this, 5), 0);
            frame.AddView(defaultTag, new FrameLayout.LayoutParams(-2, Ui.Dp(this, 18), GravityFlags.Bottom | GravityFlags.Right));
        }

        if (isFavorite)
        {
            // 金色星标（左上角小圆标，不与顶部状态条重叠）
            var favStar = new TextView(this) { Text = "★", TextSize = 12f, Gravity = GravityFlags.Center };
            favStar.SetTextColor(Ui.Gold);
            favStar.SetBackgroundColor(Color.Argb(175, 10, 12, 16));
            var favLp = new FrameLayout.LayoutParams(Ui.Dp(this, 22), Ui.Dp(this, 22), GravityFlags.Top | GravityFlags.Left);
            favLp.SetMargins(Ui.Dp(this, 4), Ui.Dp(this, 4), 0, 0);
            frame.AddView(favStar, favLp);
        }

        frame.Click += (_, _) => ApplyHistoryEntry(entry);
        frame.LongClick += (_, _) => ShowHistoryItemMenu(entry);
        return frame;
    }

    void ShowHistoryItemMenu(WallpaperHistoryEntry entry)
    {
        string name = string.IsNullOrEmpty(entry.OriginalName) ? "未命名媒体" : entry.OriginalName;

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

        // 管理操作（默认项按槽动态：设为/取消 × 桌面/锁屏）；应用位置在点击缩略图时的"设置为"菜单中选择
        bool isDeskDefault = !string.IsNullOrEmpty(_cfg.DefaultVideoPath) && entry.VideoPath == _cfg.DefaultVideoPath;
        bool isLockDefault = !string.IsNullOrEmpty(_cfg.LockDefaultVideoPath) && entry.VideoPath == _cfg.LockDefaultVideoPath;
        panel.AddView(MakeMenuItem(dialog, isDeskDefault ? "取消桌面默认" : "设为桌面默认", Ui.Gold, () =>
        {
            if (isDeskDefault) ClearDefault(VideoTarget.Desktop);
            else SetAsDefault(entry, VideoTarget.Desktop);
        }));
        panel.AddView(MakeMenuItem(dialog, isLockDefault ? "取消锁屏默认" : "设为锁屏默认", Ui.Gold, () =>
        {
            if (isLockDefault) ClearDefault(VideoTarget.Lock);
            else SetAsDefault(entry, VideoTarget.Lock);
        }));
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

    /// <summary>取消默认标记：此后"恢复默认"将还原系统原生壁纸</summary>
    void ClearDefault()
    {
        _cfg = WallpaperConfig.LoadOrDefault(FilesDir!.AbsolutePath);
        _cfg.DefaultVideoPath = "";
        _cfg.DefaultOriginalName = "";
        _cfg.Save(FilesDir!.AbsolutePath);
        Log.Info(Tag, "默认壁纸标记已取消");
        Toast.MakeText(this, "已取消默认", ToastLength.Short)?.Show();
        RefreshUi();
    }

    /// <summary>设置指定槽的默认壁纸</summary>
    void SetAsDefault(WallpaperHistoryEntry entry, VideoTarget target)
    {
        try
        {
            if (!File.Exists(entry.VideoPath))
            {
                Toast.MakeText(this, "该视频文件已丢失", ToastLength.Short)?.Show();
                return;
            }
            _cfg = WallpaperConfig.LoadOrDefault(FilesDir!.AbsolutePath);
            if (target == VideoTarget.Desktop)
            {
                _cfg.DefaultVideoPath = entry.VideoPath;
                _cfg.DefaultOriginalName = entry.OriginalName;
                _cfg.DefaultMediaType = entry.MediaType;
            }
            else
            {
                _cfg.LockDefaultVideoPath = entry.VideoPath;
                _cfg.LockDefaultOriginalName = entry.OriginalName;
            }
            _cfg.Save(FilesDir!.AbsolutePath);
            Log.Info(Tag, $"默认壁纸已设置[{target}]: {entry.OriginalName}");
            RefreshUi();
            Toast.MakeText(this, $"已将「{entry.OriginalName}」设为{(target == VideoTarget.Desktop ? "桌面" : "锁屏")}默认", ToastLength.Short)?.Show();
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"设置默认失败: {ex}");
            Toast.MakeText(this, $"设置失败: {ex.Message}", ToastLength.Long)?.Show();
        }
    }

    /// <summary>取消指定槽的默认标记（恢复该槽默认将还原系统壁纸）</summary>
    void ClearDefault(VideoTarget target)
    {
        _cfg = WallpaperConfig.LoadOrDefault(FilesDir!.AbsolutePath);
        if (target == VideoTarget.Desktop)
        {
            _cfg.DefaultVideoPath = "";
            _cfg.DefaultOriginalName = "";
        }
        else
        {
            _cfg.LockDefaultVideoPath = "";
            _cfg.LockDefaultOriginalName = "";
        }
        _cfg.Save(FilesDir!.AbsolutePath);
        Log.Info(Tag, $"默认标记已取消[{target}]");
        Toast.MakeText(this, $"已取消{(target == VideoTarget.Desktop ? "桌面" : "锁屏")}默认", ToastLength.Short)?.Show();
        RefreshUi();
    }

    void ConfirmDeleteHistoryEntry(WallpaperHistoryEntry entry)
    {
        string name = string.IsNullOrEmpty(entry.OriginalName) ? "未命名媒体" : entry.OriginalName;
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
            if (entry.VideoPath == cfg.VideoPath || entry.VideoPath == cfg.LockVideoPath)
            {
                Toast.MakeText(this, "正在使用中，请先切换到其他壁纸", ToastLength.Short)?.Show();
                return;
            }
            if (entry.VideoPath == cfg.DefaultVideoPath)
            {
                Toast.MakeText(this, "桌面默认壁纸不能删除，请先更换", ToastLength.Short)?.Show();
                return;
            }
            if (entry.VideoPath == cfg.LockDefaultVideoPath)
            {
                Toast.MakeText(this, "锁屏默认壁纸不能删除，请先更换", ToastLength.Short)?.Show();
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

    /// <summary>
    /// 视频：三选目标菜单（桌面/锁屏/两者）——双槽架构下三个选项产生三种不同结果，
    /// 引擎绑定就位时立即生效；绑定缺失的槽预写视频后经系统确认页补齐（预览即所点视频）。
    /// </summary>
    void ApplyHistoryEntry(WallpaperHistoryEntry entry)
    {
        if (!File.Exists(entry.VideoPath))
        {
            Toast.MakeText(this, "该文件已丢失", ToastLength.Short)?.Show();
            return;
        }

        if (entry.MediaType == "Image")
        {
            ShowImageTargetMenu(entry); // 图片：静态提交路径（无法走动态确认页）
            return;
        }

        ShowVideoTargetMenu(entry);
    }

    enum VideoTarget { Desktop, Lock, Both }

    /// <summary>视频应用目标菜单（三选）：各槽独立写配置，引擎按 WallpaperFlags 身份各读各槽</summary>
    void ShowVideoTargetMenu(WallpaperHistoryEntry entry)
    {
        var dialog = new Dialog(this);
        dialog.RequestWindowFeature((int)WindowFeatures.NoTitle);

        var wrapper = new FrameLayout(this);
        wrapper.SetPadding(Ui.Dp(this, 16), 0, Ui.Dp(this, 16), Ui.Dp(this, 32));

        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        panel.SetBackgroundDrawable(Ui.RoundBg(new Color(26, 29, 38), this, 22, Ui.CardStroke, 1));
        panel.SetPadding(0, Ui.Dp(this, 14), 0, Ui.Dp(this, 8));

        var title = new TextView(this) { Text = "设置为", TextSize = 15f };
        title.SetTextColor(Ui.TextPrimary);
        title.Typeface = Typeface.Create(Typeface.DefaultBold, TypefaceStyle.Bold);
        title.SetPadding(Ui.Dp(this, 20), 0, Ui.Dp(this, 20), Ui.Dp(this, 8));
        panel.AddView(title);

        var caption = Ui.Caption(this, "桌面与锁屏可分别设置不同视频");
        caption.SetPadding(Ui.Dp(this, 20), 0, Ui.Dp(this, 20), Ui.Dp(this, 8));
        panel.AddView(caption);

        panel.AddView(MakeMenuItem(dialog, "桌面壁纸", Ui.Accent, () => { dialog.Dismiss(); ApplyVideoToTarget(entry, VideoTarget.Desktop); }));
        panel.AddView(MakeMenuItem(dialog, "锁屏壁纸", Ui.Accent, () => { dialog.Dismiss(); ApplyVideoToTarget(entry, VideoTarget.Lock); }));
        panel.AddView(MakeMenuItem(dialog, "桌面和锁屏壁纸", Ui.Accent, () => { dialog.Dismiss(); ApplyVideoToTarget(entry, VideoTarget.Both); }));

        wrapper.AddView(panel, new FrameLayout.LayoutParams(-1, -2));
        dialog.SetContentView(wrapper);

        var window = dialog.Window!;
        window.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
        window.SetGravity(GravityFlags.Bottom);
        window.SetLayout(-1, -2);
        dialog.Show();
    }

    /// <summary>
    /// 按目标应用视频（双槽架构）：桌面槽=VideoPath，锁屏槽=LockVideoPath，
    /// 引擎按官方 WallpaperFlags 身份各读各槽——两端可设置不同视频。
    /// 绑定缺失时预写槽配置后拉起系统确认页补齐（预览即所点视频）；
    /// 不支持分槽的系统（Android 13-）锁屏跟随桌面并如实提示。
    /// </summary>
    void ApplyVideoToTarget(WallpaperHistoryEntry entry, VideoTarget target)
    {
        if (!File.Exists(entry.VideoPath))
        {
            Toast.MakeText(this, "该视频文件已丢失", ToastLength.Short)?.Show();
            return;
        }

        var (duration, width, height) = ReadVideoMetadata(entry.VideoPath);
        long dur = duration > 0 ? duration : entry.DurationMs;
        bool flagsApi = Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake;

        _cfg = WallpaperConfig.LoadOrDefault(FilesDir!.AbsolutePath);
        switch (target)
        {
            case VideoTarget.Desktop:
                _cfg.VideoPath = entry.VideoPath;
                _cfg.OriginalName = entry.OriginalName;
                _cfg.DurationMs = dur;
                _cfg.VideoWidth = width;
                _cfg.VideoHeight = height;
                break;
            case VideoTarget.Lock:
                _cfg.LockVideoPath = entry.VideoPath;
                _cfg.LockOriginalName = entry.OriginalName;
                _cfg.LockDurationMs = dur;
                break;
            case VideoTarget.Both:
                _cfg.VideoPath = entry.VideoPath;
                _cfg.OriginalName = entry.OriginalName;
                _cfg.DurationMs = dur;
                _cfg.VideoWidth = width;
                _cfg.VideoHeight = height;
                _cfg.LockVideoPath = entry.VideoPath;
                _cfg.LockOriginalName = entry.OriginalName;
                _cfg.LockDurationMs = dur;
                break;
        }
        _cfg.Save(FilesDir!.AbsolutePath);
        RecordUsage(entry.VideoPath, entry.OriginalName, dur);
        VideoWallpaperService.NotifyConfigChanged();

        bool engineRunning = VideoWallpaperService.IsEngineRunning;
        bool deskIsStatic = TryGetActualWallpaperBitmap(WallpaperManagerFlags.System, out _);
        bool lockIsStatic = TryGetActualWallpaperBitmap(WallpaperManagerFlags.Lock, out _);

        bool needConfirmPage = false;
        string doneMsg = "";

        switch (target)
        {
            case VideoTarget.Desktop:
                if (engineRunning && !deskIsStatic) doneMsg = "桌面壁纸已更新（动态播放）";
                else needConfirmPage = true;
                break;

            case VideoTarget.Lock:
                if (!flagsApi)
                    doneMsg = "当前系统不支持锁屏独立动态壁纸，锁屏将跟随桌面";
                else if (engineRunning && !lockIsStatic) doneMsg = "锁屏壁纸已更新（动态播放）";
                else needConfirmPage = true;
                break;

            case VideoTarget.Both:
                if (engineRunning && !deskIsStatic && (!flagsApi || !lockIsStatic)) doneMsg = "桌面和锁屏已更新（动态播放）";
                else needConfirmPage = true;
                break;
        }

        if (needConfirmPage)
        {
            Toast.MakeText(this, "请在系统确认页完成设置，视频将动态播放", ToastLength.Long)?.Show();
            RequestSetWallpaper();
        }
        else
        {
            Toast.MakeText(this, doneMsg, ToastLength.Short)?.Show();
        }
        RefreshUi();
    }

    /// <summary>图片静态目标选择（底部深色菜单）：桌面静态带退出警示</summary>
    /// <summary>图片静态目标菜单（三选，与视频菜单同风格）：桌面/锁屏/桌面和锁屏</summary>
    void ShowImageTargetMenu(WallpaperHistoryEntry entry)
    {
        var dialog = new Dialog(this);
        dialog.RequestWindowFeature((int)WindowFeatures.NoTitle);

        var wrapper = new FrameLayout(this);
        wrapper.SetPadding(Ui.Dp(this, 16), 0, Ui.Dp(this, 16), Ui.Dp(this, 32));

        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        panel.SetBackgroundDrawable(Ui.RoundBg(new Color(26, 29, 38), this, 22, Ui.CardStroke, 1));
        panel.SetPadding(0, Ui.Dp(this, 14), 0, Ui.Dp(this, 8));

        var title = new TextView(this) { Text = "设置为", TextSize = 15f };
        title.SetTextColor(Ui.TextPrimary);
        title.Typeface = Typeface.Create(Typeface.DefaultBold, TypefaceStyle.Bold);
        title.SetPadding(Ui.Dp(this, 20), 0, Ui.Dp(this, 20), Ui.Dp(this, 6));
        panel.AddView(title);

        var caption = Ui.Caption(this, "图片以静态画面应用");
        caption.SetPadding(Ui.Dp(this, 20), 0, Ui.Dp(this, 20), Ui.Dp(this, 8));
        panel.AddView(caption);

        panel.AddView(MakeMenuItem(dialog, "桌面壁纸", Ui.Accent, () => ApplyImageAsStatic(entry, WallpaperManagerFlags.System)));
        panel.AddView(MakeMenuItem(dialog, "锁屏壁纸", Ui.Accent, () => ApplyImageAsStatic(entry, WallpaperManagerFlags.Lock)));
        panel.AddView(MakeMenuItem(dialog, "桌面和锁屏壁纸", Ui.Accent, () =>
        {
            ApplyImageAsStatic(entry, WallpaperManagerFlags.System);
            ApplyImageAsStatic(entry, WallpaperManagerFlags.Lock);
        }));

        wrapper.AddView(panel, new FrameLayout.LayoutParams(-1, -2));
        dialog.SetContentView(wrapper);

        var window = dialog.Window!;
        window.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
        window.SetGravity(GravityFlags.Bottom);
        window.SetLayout(-1, -2);
        dialog.Show();
    }

    /// <summary>图片静态提交：解码（限边 2048）→ SetBitmap → 异常全上报</summary>
    void ApplyImageAsStatic(WallpaperHistoryEntry entry, WallpaperManagerFlags flag)
    {
        string flagName = flag == WallpaperManagerFlags.Lock ? "FLAG_LOCK" : "FLAG_SYSTEM";
        try
        {
            var opts = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(entry.VideoPath, opts);
            var load = new BitmapFactory.Options { InSampleSize = Math.Max(1, Math.Max(opts.OutWidth, opts.OutHeight) / 2048) };
            using Bitmap? bmp = BitmapFactory.DecodeFile(entry.VideoPath, load);
            if (bmp == null)
            {
                Toast.MakeText(this, "图片解码失败", ToastLength.Short)?.Show();
                return;
            }

            var wm = WallpaperManager.GetInstance(this)!;
            wm.SetBitmap(bmp, null, true, flag);
            Log.Info(Tag, $"图片 SetBitmap({flagName}) 成功 {opts.OutWidth}x{opts.OutHeight}");

            // 状态跟踪（双卡与角标显示用）
            _cfg = WallpaperConfig.LoadOrDefault(FilesDir!.AbsolutePath);
            if (flag == WallpaperManagerFlags.Lock) { _cfg.LockFramePath = entry.VideoPath; _cfg.LockFrameName = entry.OriginalName; }
            else { _cfg.DesktopStaticPath = entry.VideoPath; _cfg.DesktopStaticName = entry.OriginalName; }
            _cfg.Save(FilesDir!.AbsolutePath);
            Toast.MakeText(this, (flag == WallpaperManagerFlags.Lock ? "锁屏壁纸" : "桌面壁纸") + "已设置（静态）", ToastLength.Short)?.Show();
            RefreshUi();
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"图片 SetBitmap({flagName}) 失败: {ex}");
            Toast.MakeText(this, "失败: " + ex.GetType().Name + ": " + ex.Message, ToastLength.Long)?.Show();
        }
    }

    /// <summary>
    /// 记录一次使用（内容 hash 去重）：重复媒体删除新副本、用既有路径并置顶；
    /// 新媒体生成缩略图、入历史，超限淘汰最旧（连带清理文件）。返回最终使用的路径。
    /// </summary>
    string RecordUsage(string newVideoPath, string originalName, long durationMs, string mediaType = "Video")
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
                history.Record(new WallpaperHistoryEntry { Id = id, VideoPath = existing.VideoPath, OriginalName = originalName, DurationMs = durationMs, MediaType = existing.MediaType, IsFavorite = existing.IsFavorite });
                history.Save(FilesDir!.AbsolutePath);
                return existing.VideoPath;
            }

            GenerateThumbnail(newVideoPath, id);
            var evicted = history.Record(new WallpaperHistoryEntry { Id = id, VideoPath = newVideoPath, OriginalName = originalName, DurationMs = durationMs, MediaType = mediaType });
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
            if (evicted.VideoPath == cfg.VideoPath || evicted.VideoPath == cfg.LockVideoPath) return;
            if (evicted.VideoPath == cfg.DefaultVideoPath || evicted.VideoPath == cfg.LockDefaultVideoPath) return; // 用户默认不可被淘汰清理
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

    void GenerateThumbnail(string mediaPath, string id)
    {
        var thumbsDir = new Java.IO.File(FilesDir, "thumbs");
        if (!thumbsDir.Exists()) thumbsDir.Mkdirs();
        var target = new Java.IO.File(thumbsDir, $"{id}.jpg");
        if (target.Exists()) return;

        string ext = System.IO.Path.GetExtension(mediaPath).ToLowerInvariant();
        Bitmap? source = null;
        if (ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp")
        {
            // 图片：直接解码缩放
            var opts = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(mediaPath, opts);
            var load = new BitmapFactory.Options { InSampleSize = Math.Max(1, Math.Max(opts.OutWidth, opts.OutHeight) / 480) };
            source = BitmapFactory.DecodeFile(mediaPath, load);
        }
        else
        {
            // 视频：取首帧
            using var retriever = new MediaMetadataRetriever();
            retriever.SetDataSource(mediaPath);
            source = retriever.GetFrameAtTime(0);
        }
        if (source == null) return;

        int width = 240;
        int height = Math.Max(1, source.Height * width / source.Width);
        using Bitmap scaled = Bitmap.CreateScaledBitmap(source, width, height, true)!;
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

    // ---------- 选择壁纸（视频+图片；SAF → 复制 → 元数据 → 去重记录 → 按目标槽应用） ----------

    void PickMedia(VideoTarget target)
    {
        _pickTarget = target;
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        intent.PutExtra(Intent.ExtraMimeTypes, new[] { "video/*", "image/*" });
        StartActivityForResult(intent, PickVideoRequest);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickVideoRequest || resultCode != Result.Ok || data?.Data == null) return;

        var sourceUri = data.Data!;
        try
        {
            string mime = ContentResolver?.GetType(sourceUri) ?? "";
            if (mime.StartsWith("image/"))
            {
                ImportAndRecordImage(sourceUri, _pickTarget);
            }
            else
            {
                ImportAndRecordVideo(sourceUri, _pickTarget);
            }
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"导入失败: {ex}");
            Toast.MakeText(this, $"导入失败: {ex.Message}", ToastLength.Long)?.Show();
        }
        _cfg = WallpaperConfig.LoadOrDefault(FilesDir!.AbsolutePath);
        RefreshUi();
    }

    void ImportAndRecordVideo(Uri sourceUri, VideoTarget target)
    {
        string copiedPath = ImportVideo(sourceUri);
        var (duration, width, height) = ReadVideoMetadata(copiedPath);
        string name = sourceUri.LastPathSegment ?? "自定义视频";
        string finalPath = RecordUsage(copiedPath, name, duration, "Video");

        var entry = new WallpaperHistoryEntry
        {
            VideoPath = finalPath,
            OriginalName = name,
            DurationMs = duration,
            MediaType = "Video",
        };
        ApplyVideoToTarget(entry, target); // 写对应槽 + 绑定检查 + 提示
    }

    /// <summary>图片流程：入历史后按目标槽静态提交（无菜单，直达）</summary>
    void ImportAndRecordImage(Uri sourceUri, VideoTarget target)
    {
        string copiedPath = ImportImage(sourceUri);
        var (width, height) = ReadImageDimensions(copiedPath);
        string name = sourceUri.LastPathSegment ?? "自定义图片";
        string finalPath = RecordUsage(copiedPath, name, 0, "Image");

        var entry = new WallpaperHistoryEntry
        {
            Id = TryHashFile(finalPath),
            VideoPath = finalPath,
            OriginalName = name,
            MediaType = "Image",
        };
        // 按按钮目标直接静态提交：桌面 → FLAG_SYSTEM；锁屏 → FLAG_LOCK
        ApplyImageAsStatic(entry, target == VideoTarget.Desktop ? WallpaperManagerFlags.System : WallpaperManagerFlags.Lock);
    }

    string ImportImage(Uri sourceUri)
    {
        var imagesDir = new Java.IO.File(FilesDir, "images");
        if (!imagesDir.Exists()) imagesDir.Mkdirs();

        string ext = System.IO.Path.GetExtension(sourceUri.LastPathSegment ?? "") is { Length: > 1 } e ? e.ToLowerInvariant() : ".jpg";
        var target = new Java.IO.File(imagesDir, $"img_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
        using var input = ContentResolver!.OpenInputStream(sourceUri)!;
        using var output = System.IO.File.Create(target.AbsolutePath);
        input.CopyTo(output);
        output.Flush();
        return target.AbsolutePath;
    }

    (int width, int height) ReadImageDimensions(string path)
    {
        try
        {
            var opts = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(path, opts);
            return (opts.OutWidth, opts.OutHeight);
        }
        catch { return (0, 0); }
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
    /// 按槽恢复默认：标记过默认 → 将默认视频应用到该槽（ApplyVideoToTarget 走绑定检查）；
    /// 未标记 → 还原该槽的系统原生壁纸（桌面 Clear(System)=引擎退场；锁屏 Clear(Lock)）。
    /// </summary>
    void ResetSlotDefault(VideoTarget target)
    {
        try
        {
            _cfg = WallpaperConfig.LoadOrDefault(FilesDir!.AbsolutePath);
            string defPath = target == VideoTarget.Desktop ? _cfg.DefaultVideoPath : _cfg.LockDefaultVideoPath;
            string defName = target == VideoTarget.Desktop ? _cfg.DefaultOriginalName : _cfg.LockDefaultOriginalName;
            bool hasDefault = !string.IsNullOrEmpty(defPath) && File.Exists(defPath);
            if (hasDefault)
            {
                // 按默认条目的媒体类型路由：图片默认 → 静态提交；视频默认 → 引擎播放
                bool defaultIsImage = _cfg.DefaultMediaType == "Image";
                if (defaultIsImage)
                {
                    var imgEntry = new WallpaperHistoryEntry
                    {
                        VideoPath = defPath,
                        OriginalName = defName,
                        MediaType = "Image",
                    };
                    ApplyImageAsStatic(imgEntry, target == VideoTarget.Desktop ? WallpaperManagerFlags.System : WallpaperManagerFlags.Lock);
                    return;
                }

                string slotPath = target == VideoTarget.Desktop ? _cfg.VideoPath : _cfg.LockVideoPath;
                // "已是默认"须同时满足：配置匹配 且 该槽当前确实在动态渲染（配置残留不算——桌面可能正被静态图覆盖）
                bool slotIsStaticNow = TryGetActualWallpaperBitmap(
                    target == VideoTarget.Desktop ? WallpaperManagerFlags.System : WallpaperManagerFlags.Lock, out _);
                bool slotEngineLive = VideoWallpaperService.GetActiveEngineFlags()
                    .Contains(target == VideoTarget.Desktop ? WallpaperManagerFlags.System : WallpaperManagerFlags.Lock);
                if (defPath == slotPath && slotEngineLive && !slotIsStaticNow)
                {
                    Toast.MakeText(this, target == VideoTarget.Desktop ? "桌面已是默认壁纸" : "锁屏已是默认壁纸", ToastLength.Short)?.Show();
                    return;
                }
                var (duration, width, height) = ReadVideoMetadata(defPath);
                var entry = new WallpaperHistoryEntry
                {
                    VideoPath = defPath,
                    OriginalName = defName,
                    DurationMs = duration,
                    MediaType = "Video",
                };
                ApplyVideoToTarget(entry, target);
                return;
            }

            // 未设置默认：还原该槽的系统原生壁纸
            var wm = WallpaperManager.GetInstance(this)!;
            if (target == VideoTarget.Desktop)
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.P) wm.Clear(WallpaperManagerFlags.System);
                _cfg.DesktopStaticPath = ""; _cfg.DesktopStaticName = "";
                Log.Info(Tag, "恢复桌面默认：未设置用户默认，已还原系统壁纸（引擎退场）");
                Toast.MakeText(this, "桌面已还原系统壁纸", ToastLength.Long)?.Show();
            }
            else
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.P) wm.Clear(WallpaperManagerFlags.Lock);
                _cfg.LockFramePath = ""; _cfg.LockFrameName = "";
                _cfg.LockVideoPath = ""; _cfg.LockOriginalName = "";
                _cfg.LockDefaultVideoPath = ""; _cfg.LockDefaultOriginalName = "";
                Log.Info(Tag, "恢复锁屏默认：未设置用户默认，已还原系统壁纸");
                Toast.MakeText(this, "锁屏已还原系统壁纸", ToastLength.Long)?.Show();
            }
            _cfg.Save(FilesDir!.AbsolutePath);
            RefreshUi();
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"恢复默认失败: {ex}");
            Toast.MakeText(this, $"恢复默认失败: {ex.Message}", ToastLength.Long)?.Show();
        }
    }
}
