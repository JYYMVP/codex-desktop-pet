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
    private readonly GitHubGistPetShareService _shareService = new();

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

    private void ImportChatGptSpriteSheet_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 ChatGPT Pets v1/v2 精灵表",
            Filter = "宠物精灵表 (*.png;*.webp)|*.png;*.webp|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        TryAction(() =>
        {
            var imported = _library.ImportChatGptSpriteSheet(dialog.FileName);
            RefreshList(imported.Id);
            MessageBox.Show(this,
                $"已导入“{imported.Name}”。\n\n待机、工作、等待输入、失败和检查动画已自动映射。" +
                "请只导入和导出你有权使用的角色素材。",
                "ChatGPT 精灵表", MessageBoxButton.OK, MessageBoxImage.Information);
        });
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

    private async void SharePublic_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPet is not { IsBuiltIn: false } pet)
            return;
        if (_shareService.FindShare(pet.Id) is { } existing)
        {
            TryCopy(existing.Url);
            MessageBox.Show(this, $"这个宠物已经公开分享，链接已复制：\n{existing.Url}",
                "公网分享", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var confirmation =
            $"将“{pet.Name}”的当前快照公开发布到你的 GitHub Gist。\n\n" +
            "任何获得链接的人都能下载素材；链接不包含你的本地路径或 Codex 对话，但 Gist 会显示你的 GitHub 用户名。\n\n" +
            "请确认你有权公开这些角色素材，并确认公开受众。是否继续？";
        if (MessageBox.Show(this, confirmation, "创建公网宠物分享",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await TryActionAsync(async () =>
        {
            var record = await _shareService.ShareAsync(_library, pet);
            TryCopy(record.Url);
            MessageBox.Show(this,
                $"公网链接已创建并复制：\n{record.Url}\n\n可随时用“撤销公网链接”使其失效。",
                "公网分享", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async void UnsharePublic_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPet is not { IsBuiltIn: false } pet)
            return;
        var record = _shareService.FindShare(pet.Id);
        if (record is null)
        {
            MessageBox.Show(this, "这个宠物没有由本机创建的公网分享链接。", "撤销公网分享",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this,
                $"永久删除对应的 GitHub Gist 并使链接失效？\n\n{record.Url}",
                "撤销公网分享", MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
            return;

        await TryActionAsync(async () =>
        {
            await _shareService.UnshareAsync(record);
            MessageBox.Show(this, "公网分享已撤销。", "撤销公网分享",
                MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async void AdoptPublic_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PetShareLinkWindow { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        await TryActionAsync(async () =>
        {
            var adopted = await _shareService.AdoptAsync(_library, dialog.ShareUrl);
            RefreshList(adopted.Id);
            MessageBox.Show(this, $"已从公网链接领养“{adopted.Name}”。", "宠物库",
                MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPet is not { IsBuiltIn: false } pet)
            return;
        if (_shareService.FindShare(pet.Id) is { } activeShare)
        {
            MessageBox.Show(this,
                $"这个宠物仍有公网分享链接，请先撤销再删除本地文件：\n{activeShare.Url}",
                "删除宠物", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
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

    private async Task TryActionAsync(Func<Task> action)
    {
        try
        {
            IsEnabled = false;
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            await action();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Pet sharing operation failed: {ex}");
            MessageBox.Show(this, ex.Message, "宠物分享", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            System.Windows.Input.Mouse.OverrideCursor = null;
            IsEnabled = true;
        }
    }

    private static void TryCopy(string value)
    {
        try { System.Windows.Clipboard.SetText(value); }
        catch (Exception ex) { AppLog.Write($"Copy pet share URL failed: {ex.Message}"); }
    }
}
