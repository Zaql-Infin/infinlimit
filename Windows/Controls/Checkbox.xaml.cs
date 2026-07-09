using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace InfinLimit.Controls
{
    public partial class Checkbox : UserControl
    {
        // Kept for API compatibility — no longer rendered, toggle replaces icon
        public Geometry Geometry { get; set; }

        public bool Checked { get; private set; } = false;

        private static readonly Duration AnimDur = new Duration(TimeSpan.FromMilliseconds(160));

        public Checkbox()
        {
            InitializeComponent();
        }

        public void SetState(bool enabled)
        {
            Checked = enabled;

            var targetColor = enabled
                ? ((SolidColorBrush)Application.Current.FindResource("AccentColor")).Color
                : ((SolidColorBrush)Application.Current.FindResource("InactiveColor")).Color;

            // StaticResource brushes are frozen — replace with a mutable copy on first call
            var trackBrush = Track.Background as SolidColorBrush;
            if (trackBrush == null || trackBrush.IsFrozen)
            {
                trackBrush = new SolidColorBrush(trackBrush?.Color ?? Colors.Transparent);
                Track.Background = trackBrush;
            }
            trackBrush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(targetColor, AnimDur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

            // Fade checkmark in/out
            Thumb.BeginAnimation(OpacityProperty,
                new DoubleAnimation(enabled ? 1.0 : 0.0, AnimDur)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        private void Track_MouseEnter(object sender, MouseEventArgs e)
        {
            var fade = new DoubleAnimation(Track.Opacity, 0.85, new Duration(TimeSpan.FromMilliseconds(120)));
            Track.BeginAnimation(OpacityProperty, fade);
        }

        private void Track_MouseLeave(object sender, MouseEventArgs e)
        {
            var fade = new DoubleAnimation(Track.Opacity, 1.0, new Duration(TimeSpan.FromMilliseconds(120)));
            Track.BeginAnimation(OpacityProperty, fade);
        }

        public event RoutedEventHandler Click;

        private void Track_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                SetState(!Checked);
                Click?.Invoke(this, e);
            }
        }
    }
}
