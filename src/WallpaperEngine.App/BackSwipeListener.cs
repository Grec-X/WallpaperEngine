using Android.Views;

namespace WallpaperEngine.App;

/// <summary>
/// 页面右滑返回手势（从页面任意位置右滑，整页跟手，松手按阈值决定 finish 或弹回）。
/// 与抽屉的 DrawerSwipeListener 区分：此处作用于全屏 Activity 根视图。
/// </summary>
public class BackSwipeListener : Java.Lang.Object, View.IOnTouchListener
{
    readonly View _page;
    readonly Action _finish;
    float _startX, _startY;
    float _baseTx;
    bool _tracking;
    readonly int _touchSlop;
    readonly int _finishThreshold;

    public BackSwipeListener(View page, Action finish)
    {
        _page = page;
        _finish = finish;
        _touchSlop = (int)(8 * page.Resources!.DisplayMetrics!.Density);
        _finishThreshold = (int)(110 * page.Resources.DisplayMetrics.Density);
    }

    public bool OnTouch(View? v, MotionEvent? e)
    {
        if (e == null) return false;
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _startX = e.RawX;
                _startY = e.RawY;
                _baseTx = _page.TranslationX;
                _tracking = false;
                return false;

            case MotionEventActions.Move:
                float dx = e.RawX - _startX;
                float dy = e.RawY - _startY;
                if (!_tracking && dx > _touchSlop && dx > Math.Abs(dy) * 1.5f)
                    _tracking = true;
                if (_tracking)
                {
                    _page.TranslationX = Math.Max(0f, _baseTx + dx);
                    return true;
                }
                return false;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                if (_tracking)
                {
                    _tracking = false;
                    if (_page.TranslationX > _finishThreshold)
                        _finish();
                    else
                        _page.Animate().TranslationX(0f).SetDuration(180).Start();
                    return true;
                }
                return false;

            default:
                return false;
        }
    }
}
