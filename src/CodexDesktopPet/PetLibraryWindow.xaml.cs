using System.IO;
using System.Windows;
using System.Windows.Controls;
using CodexDesktopPet.Models;
using CodexDesktopPet.Services;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace CodexDesktopPet;

public partial class PetLibraryWindow : Window
{
    private readonly AppSettings _settings;
    private readonly PetLibraryService _library = new();

    internal PetLibraryWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        RefreshList();
    }

    private PetLibraryItem? SelectedPet => PetList.SelectedItem as PetLibraryItem;

    private void RefreshList(string? selectId = null)
    {
        var items = _library.ListPets();
        PetList.ItemsSource = items;
        var activeId = selectId;
        if (activeId is null)
        {
            activeId = string.IsNullOrWhiteSpace(_settings.PetPackPath)
                ? "default"
                : items.FirstOrDefault(item =>
                    !item.IsBuiltIn && string.Equals(Path.GetFullPath(item.PackPath),
                        Path.GetFullPath(_settings.PetPackPath), StringComparison.OrdinalIgnoreCase))?.Id;
        }
        PetList.SelectedItem = items.FirstOrDefault(item => item.Id == activeId) ?? items[0];
    }

    private void PetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedPet is not { } pet)
            return;
        NameBox.Text = pet.Name;
        DescriptionBox.Text = pet.Description;
        NameBox.IsEnabled = DescriptionBox.IsEnabled = !pet.IsBuiltIn;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 pet.json 或 .codexpet 分享包",
            Filter = "Codex 宠物包 (*.codexpet;pet.json;*.zip)|*.codexpet;pet.json;*.zip|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        TryAction(() =>
        {
            var imported = _library.Import(dialog.FileName);
            RefreshList(imported.Id);
            MessageBox.Show(this, $"已导入“{imported.Name}”。", "宠物库",
                MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private void CreatePixelPet_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PixelPetEditorWindow(_library) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.CreatedPet is { } pet)
            RefreshList(pet.Id);
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPet is not { } pet)
            return;
        _settings.PetPackPath = pet.IsBuiltIn ? string.Empty : pet.PackPath;
        _settings.Save();
        DialogResult = true;
    }

    private void SaveMetadata_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPet is not { } pet)
            return;
        TryAction(() =>
        {
            _library.UpdateMetadata(pet, NameBox.Text, DescriptionBox.Text);
            RefreshList(pet.Id);
        });
    }

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPet is not { } pet)
            return;
        if (pet.IsBuiltIn)
        {
            MessageBox.Show(this, "内置宠物随程序发布，无需单独校验。", "校验结果");
            return;
        }
        var result = _library.Validate(pet.PackPath);
        MessageBox.Show(this,
            result.IsValid ? "校验通过：尺寸、透明背景和动画帧均有效。" : string.Join(Environment.NewLine, result.Errors),
            "校验结果", MessageBoxButton.OK,
            result.IsValid ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPet is not { IsBuiltIn: false } pet)
            return;
        var dialog = new SaveFileDialog
        {
            Title = "导出宠物分享包",
            FileName = $"{pet.Id}.codexpet",
            DefaultExt = ".codexpet",
            Filter = "Codex 宠物包 (*.codexpet)|*.codexpet"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        TryAction(() =>
        {
            _library.Export(pet, dialog.FileName);
            MessageBox.Show(this, "宠物分享包已导出。对方可以通过“导入 / 领养”安装。",
                "宠物库", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPet is not { IsBuiltIn: false } pet)
            return;
        if (MessageBox.Show(this, $"永久删除“{pet.Name}”及其本地动画文件？",
                "删除宠物", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        TryAction(() =>
        {
            if (string.Equals(Path.GetFullPath(_settings.PetPackPath), Path.GetFullPath(pet.PackPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                _settings.PetPackPath = string.Empty;
                _settings.Save();
            }
            _library.Delete(pet);
            RefreshList();
        });
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void LibraryMoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryMoreButton.ContextMenu is null)
            return;
        LibraryMoreButton.ContextMenu.PlacementTarget = LibraryMoreButton;
        LibraryMoreButton.ContextMenu.Placement =
            System.Windows.Controls.Primitives.PlacementMode.Top;
        LibraryMoreButton.ContextMenu.IsOpen = true;
    }

    private void TryAction(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            AppLog.Write($"Pet library operation failed: {ex}");
            MessageBox.Show(this, ex.Message, "宠物库", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
