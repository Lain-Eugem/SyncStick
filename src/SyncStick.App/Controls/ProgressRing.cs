using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SyncStick.App.Controls;

public enum ProgressRingState
{
    Idle,
    Ready,
    Busy,
    Done,
    Error
}

/// <summary>
/// A crisp circular progress ring with a dashed idle track, a progress head
/// dot, and a slowly orbiting satellite while busy.
/// </summary>
public sealed class ProgressRing : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(ProgressRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State),
        typeof(ProgressRingState),
        typeof(ProgressRing),
        new FrameworkPropertyMetadata(
            ProgressRingState.Idle,
            FrameworkPropertyMetadataOptions.AffectsRender,
            OnStateChanged));

    public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
        nameof(RingThickness),
        typeof(double),
        typeof(ProgressRing),
        new FrameworkPropertyMetadata(6d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Brush),
        typeof(ProgressRing),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RingBrushProperty = DependencyProperty.Register(
        nameof(RingBrush),
        typeof(Brush),
        typeof(ProgressRing),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DoneBrushProperty = DependencyProperty.Register(
        nameof(DoneBrush),
        typeof(Brush),
        typeof(ProgressRing),
        new FrameworkPropertyMetadata(Brushes.SeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ErrorBrushProperty = DependencyProperty.Register(
        nameof(ErrorBrush),
        typeof(Brush),
        typeof(ProgressRing),
        new FrameworkPropertyMetadata(Brushes.IndianRed, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly DispatcherTimer _spinTimer;
    private double _spinAngle;

    public ProgressRing()
    {
        SnapsToDevicePixels = true;
        _spinTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(34)
        };
        _spinTimer.Tick += (_, _) =>
        {
            _spinAngle = (_spinAngle + 2.8) % 360;
            InvalidateVisual();
        };
        Loaded += (_, _) => UpdateTimer();
        Unloaded += (_, _) => _spinTimer.Stop();
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public ProgressRingState State
    {
        get => (ProgressRingState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public double RingThickness
    {
        get => (double)GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush RingBrush
    {
        get => (Brush)GetValue(RingBrushProperty);
        set => SetValue(RingBrushProperty, value);
    }

    public Brush DoneBrush
    {
        get => (Brush)GetValue(DoneBrushProperty);
        set => SetValue(DoneBrushProperty, value);
    }

    public Brush ErrorBrush
    {
        get => (Brush)GetValue(ErrorBrushProperty);
        set => SetValue(ErrorBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= RingThickness * 2)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - RingThickness) / 2 - 2;
        var activeBrush = State switch
        {
            ProgressRingState.Done => DoneBrush,
            ProgressRingState.Error => ErrorBrush,
            _ => RingBrush
        };

        var trackPen = new Pen(TrackBrush, RingThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            DashStyle = State == ProgressRingState.Idle ? new DashStyle(new double[] { 1.5, 3.2 }, 0) : DashStyles.Solid
        };
        drawingContext.DrawEllipse(null, trackPen, center, radius, radius);

        if (State == ProgressRingState.Idle)
        {
            return;
        }

        var fraction = State is ProgressRingState.Ready or ProgressRingState.Done or ProgressRingState.Error
            ? 1d
            : Math.Clamp(Value / 100d, 0, 1);
        var sweep = Math.Max(2.5, fraction * 360d);
        var startAngle = -90d;
        var endAngle = startAngle + sweep;

        var activePen = new Pen(activeBrush, RingThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        if (sweep >= 359.5)
        {
            drawingContext.DrawEllipse(null, activePen, center, radius, radius);
        }
        else
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var start = PointOnCircle(center, radius, startAngle);
                var end = PointOnCircle(center, radius, endAngle);
                context.BeginFigure(start, isFilled: false, isClosed: false);
                context.ArcTo(
                    end,
                    new Size(radius, radius),
                    rotationAngle: 0,
                    isLargeArc: sweep > 180,
                    sweepDirection: SweepDirection.Clockwise,
                    isStroked: true,
                    isSmoothJoin: true);
            }

            geometry.Freeze();
            drawingContext.DrawGeometry(null, activePen, geometry);
        }

        var head = PointOnCircle(center, radius, endAngle);
        var headRadius = Math.Max(3.2, RingThickness * 0.72);
        drawingContext.DrawEllipse(activeBrush, null, head, headRadius, headRadius);

        if (State == ProgressRingState.Busy)
        {
            var satellite = PointOnCircle(center, radius, _spinAngle);
            var satelliteBrush = activeBrush.Clone();
            satelliteBrush.Opacity = 0.55;
            satelliteBrush.Freeze();
            drawingContext.DrawEllipse(
                satelliteBrush,
                null,
                satellite,
                Math.Max(2, RingThickness * 0.42),
                Math.Max(2, RingThickness * 0.42));
        }
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var angle = angleDegrees * Math.PI / 180d;
        return new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ProgressRing ring)
        {
            ring.UpdateTimer();
        }
    }

    private void UpdateTimer()
    {
        if (State == ProgressRingState.Busy && IsLoaded)
        {
            if (!_spinTimer.IsEnabled)
            {
                _spinTimer.Start();
            }
        }
        else
        {
            _spinTimer.Stop();
        }

        InvalidateVisual();
    }
}
