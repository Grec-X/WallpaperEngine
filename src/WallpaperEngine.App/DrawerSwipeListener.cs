using Android.Views;

namespace WallpaperEngine.App;

/// <summary>
/// 抽屉右滑关闭手势：按下记录起点，移动时面板跟手横移，松手按位移阈值决定关闭或弹回。
/// 只处理横向拖动；纵向触摸透传（点击等由子 View 正常处理）。
/// </summary>
public class DrawerSwipeListener : Java.Lang.Object, View.IOnTouchListener
{
    readonly View _panel;
    readonly Action _close;
    float _startX = -1, _startY;
    float _baseTx;
    bool _tracking;
    readonly int _touchSlop;
    readonly int _closeThreshold;

    public DrawerSwipeListener(View panel, Action close)
    {
        _panel = panel;
        _close = close;
        _touchSlop = (int)(8 * panel.Resources!.DisplayMetrics!.Density);
        _closeThreshold = (int)(96 * panel.Resources.DisplayMetrics.Density);
    }

    public bool OnTouch(View? v, MotionEvent? e)
    {
        if (e == null) return false;
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _startX = e.RawX;
                _startY = e.RawY;
                _baseTx = _panel.TranslationX;
                _tracking = false;
                return false; // 不消费，让子 View（缩略图滚动等）有机会

            case MotionEventActions.Move:
                float dx = e.RawX - _startX;
                float dy = e.RawY - _startY;
                if (!_tracking)
                {
                    // 横向意图明显（右滑且幅度超 slop、水平位移大于垂直）才接管
                    if (dx > _touchSlop && dx > Math.Abs(dy) * 1.5f)
                        _tracking = true;
                }
                if (_tracking)
                {
                    _panel.TranslationX = Math.Max(0f, _baseTx + dx);
                    return true;
                }
                return false;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                if (_tracking)
                {
                    _tracking = false;
                    if (_panel.TranslationX > _closeThreshold)
                        _close();
                    else
                        _panel.Animate().TranslationX(0f).SetDuration(180).Start();
                    return true;
                }
                return false;

            default:
                return false;
        }
    }
}
