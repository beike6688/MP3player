using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Mp3Player.Controls;

/// <summary>
/// 细长滚动条：滑块高度由代码控制（最小 42px），不受 WPF Track 比例布局限制。
/// </summary>
public class SlimScrollBar : ScrollBar
{
    private const double MinThumbLength = 42;
    private Thumb? _thumb;
    private ScrollViewer? _scrollViewer;
    private bool _attachPending;

    static SlimScrollBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SlimScrollBar),
            new FrameworkPropertyMetadata(typeof(SlimScrollBar)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_thumb != null)
            _thumb.DragDelta -= OnThumbDrag;
        _thumb = GetTemplateChild("PART_SlimThumb") as Thumb;
        if (_thumb != null)
            _thumb.DragDelta += OnThumbDrag;
        UpdateThumb();
        AttachScrollViewer();
    }

    private void AttachScrollViewer()
    {
        var sv = FindScrollViewer(this);
        if (sv == null)
        {
            if (_attachPending) return;
            _attachPending = true;
            Loaded += (_, _) =>
            {
                _attachPending = false;
                AttachScrollViewer();
            };
            return;
        }
        if (ReferenceEquals(sv, _scrollViewer)) return;
        if (_scrollViewer != null)
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        _scrollViewer = sv;
        _scrollViewer.ScrollChanged += OnScrollChanged;
        UpdateFromScrollViewer();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateFromScrollViewer();
    }

    private void UpdateFromScrollViewer()
    {
        if (_scrollViewer == null) return;
        double max = Orientation == Orientation.Horizontal
            ? _scrollViewer.ScrollableWidth
            : _scrollViewer.ScrollableHeight;
        double offset = Orientation == Orientation.Horizontal
            ? _scrollViewer.HorizontalOffset
            : _scrollViewer.VerticalOffset;
        ViewportSize = Orientation == Orientation.Horizontal
            ? _scrollViewer.ViewportWidth
            : _scrollViewer.ViewportHeight;
        Maximum = Math.Max(1, max);
        double newVal = Math.Clamp(offset, 0, Maximum);
        if (Math.Abs(Value - newVal) > 0.05)
            Value = newVal;
        else
            UpdateThumb();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ValueProperty)
            UpdateThumb();
        if (e.Property == ValueProperty
            || e.Property == ViewportSizeProperty
            || e.Property == MaximumProperty
            || e.Property == MinimumProperty)
        {
            UpdateThumb();
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateThumb();
    }

    private void UpdateThumb()
    {
        if (_thumb == null) return;
        double trackLen = Math.Max(1, Orientation == Orientation.Horizontal ? ActualWidth : ActualHeight);
        double scrollable = Maximum;
        double ratio = scrollable <= 0 ? 1 : ViewportSize / (scrollable + ViewportSize);
        double thumbLen = Math.Clamp(Math.Max(MinThumbLength, trackLen * ratio), 0, trackLen);
        double range = Math.Max(1, Maximum - Minimum);
        double pos = (trackLen - thumbLen) * ((Value - Minimum) / range);

        if (Orientation == Orientation.Horizontal)
        {
            _thumb.Width = thumbLen;
            _thumb.Margin = new Thickness(pos, 0, 0, 0);
        }
        else
        {
            _thumb.Height = thumbLen;
            _thumb.Margin = new Thickness(0, pos, 0, 0);
        }
    }

    private void OnThumbDrag(object sender, DragDeltaEventArgs e)
    {
        double trackLen = Math.Max(1, Orientation == Orientation.Horizontal ? ActualWidth : ActualHeight);
        double thumbLen = _thumb is null ? MinThumbLength : (Orientation == Orientation.Horizontal ? _thumb.ActualWidth : _thumb.ActualHeight);
        double usable = Math.Max(1, trackLen - thumbLen);
        double range = Math.Max(1, Maximum - Minimum);
        double delta = Orientation == Orientation.Horizontal ? e.HorizontalChange : e.VerticalChange;
        double newVal = Value + delta * range / usable;
        Value = Math.Clamp(newVal, Minimum, Maximum);
        ScrollToView();
    }

    private void ScrollToView()
    {
        if (_scrollViewer == null) return;
        double range = Math.Max(1, Maximum - Minimum);
        double ratio = (Value - Minimum) / range;
        if (Orientation == Orientation.Horizontal)
            _scrollViewer.ScrollToHorizontalOffset(ratio * _scrollViewer.ScrollableWidth);
        else
            _scrollViewer.ScrollToVerticalOffset(ratio * _scrollViewer.ScrollableHeight);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? node)
    {
        while (node != null)
        {
            node = VisualTreeHelper.GetParent(node);
            if (node is ScrollViewer sv) return sv;
        }
        return null;
    }

}
