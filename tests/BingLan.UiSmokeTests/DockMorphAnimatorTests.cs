using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BingLan.App.Dock;
using BingLan.App.Windows;
using BingLan.Core.Models;

/// <summary>
/// Drives the dock morph animator directly: convergence centre, completion, and the
/// interruption contract — a replaced flight must never fire its completion, and
/// everything must restore the resting surface.
/// </summary>
internal static class DockMorphAnimatorTests
{
    internal static void Run(Action<Window> show, Action pump, Action<Func<bool>, string> pumpUntil)
    {
        var surface = new Border { Width = 400, Height = 80, Background = Brushes.CornflowerBlue };
        var host = new Window
        {
            Content = surface,
            Width = 440,
            Height = 140,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent
        };
        show(host);
        var previousMotion = IceBlueDockWindow.Motion;
        IceBlueDockWindow.Motion = MotionLevel.Standard;
        try
        {
            var animator = new DockMorphAnimator(surface);
            var collapsed = 0;
            animator.CollapseCompleted += () => collapsed++;
            var scale = (ScaleTransform)surface.RenderTransform;

            // 收敛中心原样写进缩放变换，允许是表面之外的远端点（悬浮图标在窗口外）。
            animator.BeginCollapse(new Point(430, 62), MotionLevel.Standard);
            if (scale.CenterX != 430 || scale.CenterY != 62)
            {
                throw new InvalidOperationException("收敛中心没有写入缩放变换");
            }
            if (!animator.IsCollapsing || animator.ActiveKind != DockMorphKind.Collapse)
            {
                throw new InvalidOperationException("收起开始后应处于 Collapse 状态");
            }
            pumpUntil(() => collapsed == 1, "收起动画没有在期限内完成");
            if (animator.ActiveKind != DockMorphKind.None)
            {
                throw new InvalidOperationException("收起完成后应回到 None 状态");
            }
            if (Math.Abs(scale.ScaleX - 1d) > 0.001d || Math.Abs(scale.ScaleY - 1d) > 0.001d
                || Math.Abs(surface.Opacity - 1d) > 0.001d)
            {
                throw new InvalidOperationException("收起完成后表面应恢复到满尺寸满透明度基准值");
            }

            // 打断：收起进行中直接反向展开，被替换的收起不得触发完成事件。
            animator.BeginCollapse(new Point(430, 62), MotionLevel.Standard);
            animator.BeginExpand(new Point(430, 62), MotionLevel.Standard);
            pumpUntil(() => animator.ActiveKind == DockMorphKind.None, "反向展开没有完成");
            if (collapsed != 1)
            {
                throw new InvalidOperationException($"被打断的收起不应再触发完成事件（{collapsed} 次）");
            }

            // 瞬清：取消进行中的收起并立即复位，完成事件不得再触发。
            animator.BeginCollapse(new Point(430, 62), MotionLevel.Standard);
            animator.Snap();
            pump();
            pump();
            if (collapsed != 1 || animator.IsCollapsing)
            {
                throw new InvalidOperationException("Snap 后收起应被取消且不再触发完成事件");
            }
            if (Math.Abs(scale.ScaleX - 1d) > 0.001d || Math.Abs(surface.Opacity - 1d) > 0.001d)
            {
                throw new InvalidOperationException("Snap 后表面应立即复位");
            }

            // 条带重现走纯淡入：不触碰缩放，结束后透明度回满。
            animator.BeginFadeIn(MotionLevel.Standard);
            pumpUntil(() => animator.ActiveKind == DockMorphKind.None, "淡入没有完成");
            if (Math.Abs(surface.Opacity - 1d) > 0.001d)
            {
                throw new InvalidOperationException("淡入结束后透明度应回到 1");
            }

            RunHandlePulseTests(show, pump, pumpUntil);
        }
        finally
        {
            IceBlueDockWindow.Motion = previousMotion;
            host.Close();
            pump();
        }
    }

    private static void RunHandlePulseTests(Action<Window> show, Action pump, Action<Func<bool>, string> pumpUntil)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            Console.WriteLine("信息：系统关闭了窗口动画，跳过落地脉冲检查");
            return;
        }

        // Hit-testing off keeps the real pointer out: a stray MouseEnter would run the
        // hover zoom and attach its own animation, polluting the pulse assertions.
        var idle = new DockHandleWindow { IsHitTestVisible = false };
        show(idle);
        var pulsing = new DockHandleWindow { IsHitTestVisible = false };
        show(pulsing);
        var previousMotion = IceBlueDockWindow.Motion;
        try
        {
            IceBlueDockWindow.Motion = MotionLevel.Off;
            idle.Pulse();
            pump();
            if (idle.HandleGlyphScale.HasAnimatedProperties)
            {
                throw new InvalidOperationException("Off 档不应有落地脉冲动画");
            }

            IceBlueDockWindow.Motion = MotionLevel.Standard;
            pulsing.Pulse();
            pumpUntil(
                () => pulsing.HandleGlyphScale.ScaleX > 1.05d,
                "落地脉冲应把图标弹起到 1.15");
            pumpUntil(
                () => Math.Abs(pulsing.HandleGlyphScale.ScaleX - 1d) < 0.001d,
                "落地脉冲结束后应回到 1");
        }
        finally
        {
            IceBlueDockWindow.Motion = previousMotion;
            idle.Close();
            pulsing.Close();
            pump();
        }
    }
}
