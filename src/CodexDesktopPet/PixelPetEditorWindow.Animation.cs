using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexDesktopPet.Controls;
using CodexDesktopPet.Models;
using CodexDesktopPet.Services;
using MessageBox = System.Windows.MessageBox;
using WpfColor = System.Windows.Media.Color;

namespace CodexDesktopPet;

public partial class PixelPetEditorWindow
{
    private static readonly ActionOption[] ActionOptions =
    [
        new(PetState.Idle, "待机"),
        new(PetState.Capturing, "读取 / 捕获"),
        new(PetState.Thinking, "工作中"),
        new(PetState.Ready, "已完成"),
        new(PetState.NeedsInput, "等待输入"),
        new(PetState.Error, "阻塞 / 错误"),
        new(PetState.Waving, "挥手"),
        new(PetState.Jumping, "跳跃"),
        new(PetState.Running, "奔跑"),
        new(PetState.RunningRight, "向右移动"),
        new(PetState.RunningLeft, "向左移动"),
        new(PetState.Waiting, "等待"),
        new(PetState.Failed, "失败"),
        new(PetState.Review, "检查")
    ];

    private readonly Dictionary<PetState, List<PixelFrame>> _states = [];
    private readonly DispatcherTimer _previewTimer = new();
    private PetState _currentState = PetState.Idle;
    private PixelTool _currentTool = PixelTool.Pencil;
    private bool _loadingFrame;
    private int _previewFrameIndex;

    private PixelFrame? SelectedFrame => FrameList.SelectedItem as PixelFrame;

    private void InitializeAnimationEditor()
    {
        _states[PetState.Idle] = [PixelFrame.Blank(32)];
        _previewTimer.Tick += (_, _) => AdvancePreview();
        ActionBox.ItemsSource = ActionOptions;
        ActionBox.SelectedIndex = 0;
        RefreshTimeline();
    }

    private void ActionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActionBox.SelectedItem is not ActionOption option)
            return;
        StopPreview();
        _currentState = option.State;
        RefreshTimeline();
    }

    private void EnableAction_Click(object sender, RoutedEventArgs e)
    {
        if (_states.ContainsKey(_currentState))
            return;
        _states[_currentState] = [new PixelFrame(ClonePixels(_states[PetState.Idle][0].Pixels))];
        RefreshTimeline();
    }

    private void RemoveAction_Click(object sender, RoutedEventArgs e)
    {
        if (_currentState == PetState.Idle || !_states.ContainsKey(_currentState))
            return;
        if (MessageBox.Show(this, "移除这个动作及其全部帧？", "移除动作",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _states.Remove(_currentState);
        RefreshTimeline();
    }

    private void AddFrame_Click(object sender, RoutedEventArgs e)
    {
        var frames = EnsureCurrentAction();
        frames.Add(PixelFrame.Blank(CanvasEditor.CanvasSize));
        RefreshTimeline(frames.Count - 1);
    }

    private void DuplicateFrame_Click(object sender, RoutedEventArgs e)
    {
        var frames = EnsureCurrentAction();
        var insertAt = Math.Max(0, FrameList.SelectedIndex) + 1;
        var source = SelectedFrame?.Pixels ?? frames[0].Pixels;
        frames.Insert(Math.Min(insertAt, frames.Count), new PixelFrame(ClonePixels(source)));
        RefreshTimeline(Math.Min(insertAt, frames.Count - 1));
    }

    private void DeleteFrame_Click(object sender, RoutedEventArgs e)
    {
        if (!_states.TryGetValue(_currentState, out var frames) || SelectedFrame is null)
            return;
        if (_currentState == PetState.Idle && frames.Count == 1)
        {
            MessageBox.Show(this, "待机动作至少需要一帧。", "无法删除",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var index = FrameList.SelectedIndex;
        frames.RemoveAt(index);
        if (frames.Count == 0)
            _states.Remove(_currentState);
        RefreshTimeline(Math.Max(0, index - 1));
    }

    private void MoveFrameLeft_Click(object sender, RoutedEventArgs e) => MoveSelectedFrame(-1);
    private void MoveFrameRight_Click(object sender, RoutedEventArgs e) => MoveSelectedFrame(1);

    private void MoveSelectedFrame(int offset)
    {
        if (!_states.TryGetValue(_currentState, out var frames))
            return;
        var current = FrameList.SelectedIndex;
        var target = current + offset;
        if (current < 0 || target < 0 || target >= frames.Count)
            return;
        (frames[current], frames[target]) = (frames[target], frames[current]);
        RefreshTimeline(target);
    }

    private void FrameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedFrame is { } frame)
            LoadFrame(frame);
    }

    private void CanvasEditor_PixelsChanged(object? sender, EventArgs e)
    {
        if (_loadingFrame || SelectedFrame is not { } frame)
            return;
        frame.Pixels = CanvasEditor.ExportPixels();
        frame.Thumbnail = PixelsToBitmap(frame.Pixels);
        FrameList.Items.Refresh();
        UpdateOnionSkin();
        if (!_previewTimer.IsEnabled)
            PreviewImage.Source = frame.Thumbnail;
        UpdateEditorStatus();
    }

    private void LoadFrame(PixelFrame frame)
    {
        _loadingFrame = true;
        try
        {
            CanvasEditor.IsEnabled = true;
            CanvasEditor.LoadPixels(frame.Pixels);
            UpdateOnionSkin();
            PreviewImage.Source = frame.Thumbnail;
        }
        finally
        {
            _loadingFrame = false;
        }
        UpdateEditorStatus();
    }

    private void RefreshTimeline(int selectedIndex = 0)
    {
        FrameList.ItemsSource = null;
        if (!_states.TryGetValue(_currentState, out var frames))
        {
            CanvasEditor.IsEnabled = false;
            _loadingFrame = true;
            try
            {
                CanvasEditor.LoadPixels(
                    new WpfColor?[CanvasEditor.CanvasSize, CanvasEditor.CanvasSize]);
                CanvasEditor.SetOnionSkin(null);
            }
            finally
            {
                _loadingFrame = false;
            }
            PreviewImage.Source = null;
        }
        else
        {
            Reindex(frames);
            FrameList.ItemsSource = frames;
            FrameList.SelectedIndex = Math.Clamp(selectedIndex, 0, frames.Count - 1);
        }
        UpdateActionControls();
        UpdateEditorStatus();
    }

    private List<PixelFrame> EnsureCurrentAction()
    {
        if (_states.TryGetValue(_currentState, out var frames))
            return frames;
        frames = [new PixelFrame(ClonePixels(_states[PetState.Idle][0].Pixels))];
        _states[_currentState] = frames;
        return frames;
    }

    private void UpdateActionControls()
    {
        var enabled = _states.ContainsKey(_currentState);
        EnableActionButton.IsEnabled = !enabled;
        RemoveActionButton.IsEnabled = enabled && _currentState != PetState.Idle;
        OnionSkinBox.IsEnabled = enabled && _states[_currentState].Count > 1;
        PreviewAnimationButton.IsEnabled = enabled && _states[_currentState].Count > 1;
    }

    private void OnionSkin_Changed(object sender, RoutedEventArgs e) => UpdateOnionSkin();

    private void UpdateOnionSkin()
    {
        if (OnionSkinBox.IsChecked != true ||
            !_states.TryGetValue(_currentState, out var frames) ||
            FrameList.SelectedIndex <= 0)
        {
            CanvasEditor.SetOnionSkin(null);
            return;
        }
        CanvasEditor.SetOnionSkin(frames[FrameList.SelectedIndex - 1].Pixels);
    }

    private void PreviewAnimation_Click(object sender, RoutedEventArgs e)
    {
        if (_previewTimer.IsEnabled)
        {
            StopPreview();
            return;
        }
        if (!_states.TryGetValue(_currentState, out var frames) || frames.Count < 2)
            return;
        _previewFrameIndex = 0;
        _previewTimer.Interval = TimeSpan.FromMilliseconds(ReadFrameDuration(showMessage: false));
        PreviewAnimationButton.Content = "停止预览";
        PreviewImage.Source = frames[0].Thumbnail;
        _previewTimer.Start();
    }

    private void AdvancePreview()
    {
        if (!_states.TryGetValue(_currentState, out var frames) || frames.Count == 0)
        {
            StopPreview();
            return;
        }
        _previewFrameIndex = (_previewFrameIndex + 1) % frames.Count;
        PreviewImage.Source = frames[_previewFrameIndex].Thumbnail;
    }

    private void StopPreview()
    {
        _previewTimer.Stop();
        PreviewAnimationButton.Content = "播放预览";
        if (SelectedFrame is { } frame)
            PreviewImage.Source = frame.Thumbnail;
    }

    private void ResizeAllFrames(int size)
    {
        if (size == CanvasEditor.CanvasSize)
            return;
        foreach (var frame in _states.SelectMany(pair => pair.Value))
        {
            frame.Pixels = ResizePixels(frame.Pixels, size);
            frame.Thumbnail = PixelsToBitmap(frame.Pixels);
        }
        RefreshTimeline(FrameList.SelectedIndex);
    }

    private bool IdleAnimationHasArtwork() =>
        _states[PetState.Idle].Any(frame => HasArtwork(frame.Pixels));

    private PetLibraryItem? CreateAnimatedPet()
    {
        var duration = ReadFrameDuration(showMessage: true);
        if (duration == 0)
            return null;
        var bitmaps = _states.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<BitmapSource>)pair.Value
                .Select(frame => (BitmapSource)PixelsToBitmap(frame.Pixels)).ToArray());
        return _library.CreatePixelPet(NameBox.Text, DescriptionBox.Text,
            CanvasEditor.CanvasSize, duration, bitmaps);
    }

    private int ReadFrameDuration(bool showMessage)
    {
        if (int.TryParse(FrameDurationBox.Text, out var duration) && duration is >= 80 and <= 2000)
            return duration;
        if (showMessage)
            MessageBox.Show(this, "动画速度必须是 80 到 2000 毫秒。", "速度无效",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        return showMessage ? 0 : 240;
    }

    private void UpdateEditorStatus()
    {
        var tool = _currentTool switch
        {
            PixelTool.Pencil => "画笔",
            PixelTool.Eraser => "橡皮",
            PixelTool.Fill => "填充",
            _ => string.Empty
        };
        var action = ActionOptions.First(option => option.State == _currentState).Label;
        var frame = FrameList.SelectedIndex >= 0
            ? $"第 {FrameList.SelectedIndex + 1}/{FrameList.Items.Count} 帧"
            : "尚未启用";
        EditorStatus.Text = $"{tool} · {action} · {frame}";
    }

    private static void Reindex(IReadOnlyList<PixelFrame> frames)
    {
        for (var index = 0; index < frames.Count; index++)
            frames[index].Label = $"帧 {index + 1}";
    }

    private static WpfColor?[,] ResizePixels(WpfColor?[,] source, int newSize)
    {
        var oldSize = source.GetLength(0);
        var resized = new WpfColor?[newSize, newSize];
        var offset = (newSize - oldSize) / 2;
        for (var y = 0; y < oldSize; y++)
        for (var x = 0; x < oldSize; x++)
        {
            var targetX = x + offset;
            var targetY = y + offset;
            if (targetX >= 0 && targetY >= 0 && targetX < newSize && targetY < newSize)
                resized[targetX, targetY] = source[x, y];
        }
        return resized;
    }

    private static WpfColor?[,] ClonePixels(WpfColor?[,] source)
    {
        var clone = new WpfColor?[source.GetLength(0), source.GetLength(1)];
        Array.Copy(source, clone, source.Length);
        return clone;
    }

    private static bool HasArtwork(WpfColor?[,] pixels) =>
        pixels.Cast<WpfColor?>().Any(color => color is { A: > 0 });

    private static BitmapSource PixelsToBitmap(WpfColor?[,] source)
    {
        var size = source.GetLength(0);
        var bytes = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            if (source[x, y] is not { } color)
                continue;
            var index = (y * size + x) * 4;
            bytes[index] = color.B;
            bytes[index + 1] = color.G;
            bytes[index + 2] = color.R;
            bytes[index + 3] = color.A;
        }
        var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, size, size), bytes, size * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    protected override void OnClosed(EventArgs e)
    {
        _previewTimer.Stop();
        base.OnClosed(e);
    }

    private sealed record ActionOption(PetState State, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed class PixelFrame(WpfColor?[,] pixels)
    {
        public WpfColor?[,] Pixels { get; set; } = pixels;
        public BitmapSource Thumbnail { get; set; } = PixelsToBitmap(pixels);
        public string Label { get; set; } = string.Empty;

        public static PixelFrame Blank(int size) => new(new WpfColor?[size, size]);
    }
}
