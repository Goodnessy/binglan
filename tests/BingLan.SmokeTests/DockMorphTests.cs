using BingLan.Core.Dock;
using BingLan.Core.Models;

namespace BingLan.SmokeTests;

/// <summary>
/// The motion spec of the dock↔handle morph: gating, durations, scales and the
/// pixel→element geometry the shrinking row depends on.
/// </summary>
internal static class DockMorphTests
{
    internal static void Run()
    {
        // 门控：系统"显示动画"开关一票否决；Off 档完全瞬切；Reduced 保留形变但不去位移。
        if (DockMorphMotion.IsAnimated(MotionLevel.Standard, systemAnimationsEnabled: false))
        {
            throw new InvalidOperationException("系统动画关闭时任何形变都不应运行");
        }
        if (!DockMorphMotion.IsAnimated(MotionLevel.Standard, systemAnimationsEnabled: true)
            || !DockMorphMotion.IsAnimated(MotionLevel.Reduced, systemAnimationsEnabled: true))
        {
            throw new InvalidOperationException("Standard 与 Reduced 在系统动画开启时都应允许形变");
        }
        if (DockMorphMotion.IsAnimated(MotionLevel.Off, systemAnimationsEnabled: true))
        {
            throw new InvalidOperationException("Off 档不应有任何形变");
        }
        if (!DockMorphMotion.IsTravelEnabled(MotionLevel.Standard)
            || DockMorphMotion.IsTravelEnabled(MotionLevel.Reduced)
            || DockMorphMotion.IsTravelEnabled(MotionLevel.Off))
        {
            throw new InvalidOperationException("只有 Standard 档应把整行吸向悬浮图标");
        }

        // 时长：展开 200ms、收起 160ms、淡入 120ms；Reduced 全部收短。
        if (DockMorphMotion.ExpandDuration(MotionLevel.Standard) != TimeSpan.FromMilliseconds(200)
            || DockMorphMotion.CollapseDuration(MotionLevel.Standard) != TimeSpan.FromMilliseconds(160)
            || DockMorphMotion.FadeInDuration(MotionLevel.Standard) != TimeSpan.FromMilliseconds(120))
        {
            throw new InvalidOperationException("Standard 档形变时长应为 200/160/120ms");
        }
        if (DockMorphMotion.ExpandDuration(MotionLevel.Reduced) != TimeSpan.FromMilliseconds(100)
            || DockMorphMotion.CollapseDuration(MotionLevel.Reduced) != TimeSpan.FromMilliseconds(100)
            || DockMorphMotion.FadeInDuration(MotionLevel.Reduced) != TimeSpan.FromMilliseconds(80))
        {
            throw new InvalidOperationException("Reduced 档形变时长应为 100/100/80ms");
        }
        if (Math.Abs(DockMorphMotion.MorphScale(MotionLevel.Standard) - 0.15d) > 0.001d
            || Math.Abs(DockMorphMotion.MorphScale(MotionLevel.Reduced) - 0.9d) > 0.001d)
        {
            throw new InvalidOperationException("形变端点缩放应为 Standard 0.15、Reduced 0.9");
        }

        // 像素点→元素坐标：按 DPI 换算，且允许落在表面之外（悬浮图标中心在窗口外）。
        var outside = DockMorphMotion.ToElementPoint(
            1230,
            1046,
            new PixelRect(100, 1000, 1100, 1080),
            1.5d);
        if (Math.Abs(outside.X - (1230 - 100) / 1.5d) > 0.001d
            || Math.Abs(outside.Y - (1046 - 1000) / 1.5d) > 0.001d)
        {
            throw new InvalidOperationException($"远端点换算错误：{outside.X},{outside.Y}");
        }

        // 非法 DPI 回退为 1，而不是除零。
        var fallback = DockMorphMotion.ToElementPoint(200, 50, new PixelRect(100, 20, 300, 120), 0d);
        if (Math.Abs(fallback.X - 100d) > 0.001d || Math.Abs(fallback.Y - 30d) > 0.001d)
        {
            throw new InvalidOperationException($"非法 DPI 应回退为 1：{fallback.X},{fallback.Y}");
        }

        // 行程包围盒：条带与远处把手的并集覆盖整条收缩路径。
        var travel = DockMorphMotion.TravelBounds(
            new PixelRect(500, 1000, 1500, 1080),
            new PixelRect(2500, 950, 2544, 994));
        if (travel != new PixelRect(500, 950, 2544, 1080))
        {
            throw new InvalidOperationException($"行程包围盒应为两矩形并集：{travel.Left},{travel.Top},{travel.Right},{travel.Bottom}");
        }
    }
}
