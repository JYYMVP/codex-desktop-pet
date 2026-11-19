using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexDesktopPet.Controls;
using CodexDesktopPet.Models;
using CodexDesktopPet.Services;
using MessageBox = System.Windows.MessageBox;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace CodexDesktopPet;

public partial class PixelPetEditorWindow : Window
{
    private readonly PetLibraryService _library;

    internal PixelPetEditorWindow(PetLibraryService library)
    {
        InitializeComponent();
        _library = library;
        CanvasEditor.PixelsChanged += (_, _) => RefreshPreview();
        SetTool(PixelTool.Pencil);
        RefreshPreview();
    }

    internal PetLibraryItem? CreatedPet { get; private set; }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string value } && Enum.TryParse<PixelTool>(value, out var tool))
            SetTool(tool);
    }

    private void SetTool(PixelTool tool)
    {
        CanvasEditor.Tool = tool;
        EditorStatus.Text = tool switch
        {
            PixelTool.Pencil => "当前：画笔",
            PixelTool.Eraser => "当前：橡皮",
            PixelTool.Fill => "当前：填充",
            _ => string.Empty
        };
    }

    private void Palette_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string hex })
        {
            HexColorBox.Text = hex;
            ApplyColor(hex);
        }
    }

    private void ApplyColor_Click(object sender, RoutedEventArgs e) => ApplyColor(HexColorBox.Text);

    private void ApplyColor(string value)
    {
        try
        {
            if (WpfColorConverter.ConvertFromString(value.Trim()) is not WpfColor color)
                throw new FormatException();
            CanvasEditor.SelectedColor = color;
            CurrentColorSwatch.Background = new SolidColorBrush(color);
            HexColorBox.Text = color.A == byte.MaxValue
                ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            SetTool(PixelTool.Pencil);
        }
        catch
        {
            MessageBox.Show(this, "请输入 #RRGGBB 或 #AARRGGBB 颜色。", "颜色无效",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplySize_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CanvasSizeBox.Text, out var size) || size is < 8 or > 64)
        {
            MessageBox.Show(this, "画布尺寸必须是 8 到 64 的整数。", "尺寸无效",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        CanvasEditor.ResizeCanvas(size);
        CanvasSizeBox.Text = size.ToString();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (!CanvasEditor.HasArtwork || MessageBox.Show(this, "清空当前画布？", "清空画布",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            CanvasEditor.Clear();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(this, "请给宠物起一个名字。", "无法创建",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!CanvasEditor.HasArtwork)
        {
            MessageBox.Show(this, "先在画布上画出宠物吧。", "无法创建",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            CreatedPet = _library.CreateStaticPet(NameBox.Text, DescriptionBox.Text,
                CanvasEditor.CanvasSize, CanvasEditor.ExportBitmap());
            DialogResult = true;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Pixel pet creation failed: {ex}");
            MessageBox.Show(this, ex.Message, "创建失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshPreview() => PreviewImage.Source = CanvasEditor.ExportBitmap();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
