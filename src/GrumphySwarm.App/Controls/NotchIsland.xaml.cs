using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GrumphySwarm.App.Rendering;
using GrumphySwarm.App.ViewModels;
using GrumphySwarm.Core.Layout;
using GrumphySwarm.Core.State;

namespace GrumphySwarm.App.Controls;

/// <summary>
/// The Dynamic Island visual: one capsule silhouette (fill, content clip,
/// shadow layer and hit-testing share a single cached geometry) with four
/// content layers. The transition controller is the single owner of every
/// animated property — island Width/Height and the layer opacities — and each
/// new target replaces the previous animation deterministically.
///
/// Animation ownership rules:
///   - Width/Height: exclusively the controller below (To-only animations,
///     SnapshotAndReplace; a replaced animation never fires Completed).
///   - Layer opacities: only the same controller.
///   - The silhouette derives from the current size; nothing animates it.
/// </summary>
public partial class NotchIsland : UserControl
{
    private static readonly TimeSpan ExpandSizeDuration = TimeSpan.FromMilliseconds(240);
    private static readonly TimeSpan CollapseSizeDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan FastFadeDuration = TimeSpan.FromMilliseconds(90);
    private static readonly TimeSpan SlowFadeDuration = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan FadeDelay = TimeSpan.FromMilliseconds(70);

    /// <summary>Which content layer the island is showing.</summary>
    private enum IslandMode
    {
        Pill,
        Panel,
        Media,
        Battery,
    }

    private readonly NotchSilhouetteBuilder _silhouette = new();
    private SilhouetteSpec _lastSpec;

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State),
        typeof(NotchState),
        typeof(NotchIsland),
        new FrameworkPropertyMetadata(NotchState.Collapsed, OnVisualPropertyChanged));

    public static readonly DependencyProperty ReducedMotionProperty = DependencyProperty.Register(
        nameof(ReducedMotion),
        typeof(bool),
        typeof(NotchIsland),
        new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty SelectedTabProperty = DependencyProperty.Register(
        nameof(SelectedTab),
        typeof(NotchTab),
        typeof(NotchIsland),
        new PropertyMetadata(NotchTab.Nook, OnVisualPropertyChanged));

    public static readonly DependencyProperty IsBatteryVisibleProperty = DependencyProperty.Register(
        nameof(IsBatteryVisible),
        typeof(bool),
        typeof(NotchIsland),
        new PropertyMetadata(false, OnVisualPropertyChanged));

    public NotchState State
    {
        get => (NotchState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public bool ReducedMotion
    {
        get => (bool)GetValue(ReducedMotionProperty);
        set => SetValue(ReducedMotionProperty, value);
    }

    public NotchTab SelectedTab
    {
        get => (NotchTab)GetValue(SelectedTabProperty);
        set => SetValue(SelectedTabProperty, value);
    }

    public bool IsBatteryVisible
    {
        get => (bool)GetValue(IsBatteryVisibleProperty);
        set => SetValue(IsBatteryVisibleProperty, value);
    }

    public NotchIsland()
    {
        InitializeComponent();
        Width = NotchDimensions.PillWidth;
        Height = NotchDimensions.PillHeight;
        SizeChanged += (_, _) => RebuildSilhouette();
        Loaded += (_, _) => RebuildSilhouette();
        DataContextChanged += OnDataContextChanged;

        // Island interactions: single click opens the media player when a
        // session is active, double click toggles the earbud battery capsule.
        // Repositioning is Shift+drag, handled by the host window's global
        // hook (the island itself is click-through while Shift is held).
        // Clicks on real controls (buttons) pass through untouched.
        PreviewMouseLeftButtonDown += OnIslandMouseDown;
    }

    private NotchViewModel? ViewModel => DataContext as NotchViewModel;

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var island = (NotchIsland)d;
        island.AnimateTo(island.CurrentMode());
        if (e.Property == SelectedTabProperty)
        {
            island.ApplyTab(island.SelectedTab);
        }
    }

    /// <summary>Precise hit test against the actual silhouette (used by WM_NCHITTEST).</summary>
    public bool HitTestSilhouette(Point pointInIsland)
    {
        if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return false;
        }

        return _silhouette.Contains(CurrentSilhouette(), pointInIsland);
    }

    private IslandMode CurrentMode()
    {
        if (IsBatteryVisible)
        {
            return IslandMode.Battery;
        }

        return State switch
        {
            NotchState.Collapsed => IslandMode.Pill,
            _ => SelectedTab == NotchTab.Media ? IslandMode.Media : IslandMode.Panel,
        };
    }

    // ------------------------------------------------------- transition system

    private void AnimateTo(IslandMode mode)
    {
        var (width, height, radius) = mode switch
        {
            IslandMode.Pill => (NotchDimensions.PillWidth, NotchDimensions.PillHeight, NotchDimensions.PillRadius),
            IslandMode.Panel => (NotchDimensions.PanelWidth, NotchDimensions.PanelHeight, NotchDimensions.PanelRadius),
            IslandMode.Media => (NotchDimensions.MediaWidth, NotchDimensions.MediaHeight, NotchDimensions.MediaRadius),
            IslandMode.Battery => (NotchDimensions.BatteryWidth, NotchDimensions.BatteryHeight, NotchDimensions.BatteryRadius),
            _ => (NotchDimensions.PillWidth, NotchDimensions.PillHeight, NotchDimensions.PillRadius),
        };

        if (ReducedMotion)
        {
            CancelSizeAnimations();
            Width = width;
            Height = height;
            RebuildSilhouette();
            ApplyLayerState(mode, instant: true);
            return;
        }

        var duration = mode == IslandMode.Pill ? CollapseSizeDuration : ExpandSizeDuration;
        AnimateSize(WidthProperty, width, duration);
        AnimateSize(HeightProperty, height, duration);
        ApplyLayerState(mode, instant: false);
    }

    private void AnimateSize(DependencyProperty property, double target, TimeSpan duration)
    {
        var animation = new DoubleAnimation(target, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        animation.Completed += (_, _) =>
        {
            // Deterministic settle: release the animation and pin the final
            // local value so no residual animated state survives.
            BeginAnimation(property, null);
            SetValue(property, target);
            RebuildSilhouette();
        };

        BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void CancelSizeAnimations()
    {
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);
    }

    private void ApplyLayerState(IslandMode mode, bool instant)
    {
        SetLayer(CollapsedContent, mode == IslandMode.Pill, instant, mode != IslandMode.Pill);
        SetLayer(PanelContent, mode == IslandMode.Panel, instant, false);
        SetLayer(MediaContent, mode == IslandMode.Media, instant, false);
        SetLayer(BatteryContent, mode == IslandMode.Battery, instant, false);
    }

    private void SetLayer(UIElement layer, bool visible, bool instant, bool collapsedLayer)
    {
        layer.IsHitTestVisible = visible;

        if (instant)
        {
            layer.BeginAnimation(OpacityProperty, null);
            layer.Opacity = visible ? 1 : 0;
            return;
        }

        var targetOpacity = visible ? 1.0 : 0.0;
        var delay = collapsedLayer ? TimeSpan.Zero : FadeDelay;
        layer.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(targetOpacity, visible ? SlowFadeDuration : FastFadeDuration)
            {
                BeginTime = delay,
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    // --------------------------------------------------------- silhouette

    private StreamGeometry CurrentSilhouette()
    {
        var spec = NotchSilhouetteMath.Build(ActualWidth, ActualHeight, _lastSpec.TopRadius, _lastSpec.BottomRadius);
        return _silhouette.GetGeometry(spec);
    }

    /// <summary>Re-applies the shared silhouette to fill, shadow and clip.</summary>
    private void RebuildSilhouette()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        // Radius follows the current mode so the capsule morphs with the size.
        var (top, bottom) = CurrentMode() switch
        {
            IslandMode.Pill => (NotchDimensions.PillRadius, NotchDimensions.PillRadius),
            IslandMode.Panel => (NotchDimensions.PanelRadius, NotchDimensions.PanelRadius),
            IslandMode.Media => (NotchDimensions.MediaRadius, NotchDimensions.MediaRadius),
            IslandMode.Battery => (NotchDimensions.BatteryRadius, NotchDimensions.BatteryRadius),
            _ => (NotchDimensions.PillRadius, NotchDimensions.PillRadius),
        };

        var spec = NotchSilhouetteMath.Build(ActualWidth, ActualHeight, top, bottom);
        _lastSpec = spec;
        var geometry = _silhouette.GetGeometry(spec);

        if (!ReferenceEquals(BodyPath.Data, geometry))
        {
            BodyPath.Data = geometry;
            ShadowPath.Data = geometry;
            ContentHost.Clip = geometry;
        }
    }

    // --------------------------------------------------------- island clicks

    private void OnIslandMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Let real controls (transport buttons, tab chips, pin) handle their own
        // clicks; only bare-island clicks open the player / battery capsule.
        if (IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            ViewModel?.ToggleBattery();
            e.Handled = true;
        }
        else if (e.ClickCount == 1)
        {
            ViewModel?.HandleIslandClick();
        }
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase)
            {
                return true;
            }

            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }

    // --------------------------------------------------------- tab switching

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is NotchViewModel old)
        {
            old.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is NotchViewModel newValue)
        {
            newValue.PropertyChanged += OnViewModelPropertyChanged;
            ApplyTab(newValue.SelectedTab);
            AnimateTo(CurrentMode());
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotchViewModel.SelectedTab)
            && sender is NotchViewModel viewModel)
        {
            ApplyTab(viewModel.SelectedTab);
        }
    }

    private void ApplyTab(NotchTab tab)
    {
        NookTab.Visibility = tab == NotchTab.Nook ? Visibility.Visible : Visibility.Collapsed;
        AgentsTab.Visibility = tab == NotchTab.Agents ? Visibility.Visible : Visibility.Collapsed;
    }
}
