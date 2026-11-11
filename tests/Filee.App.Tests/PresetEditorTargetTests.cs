// The preset editor's target list offers only formats Filee can write: read-only FON (#177) is a source, never a target.

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.Views;
using Filee.Core.Localization;
using Filee.Core.Settings;

namespace Filee.App.Tests;

public class PresetEditorTargetTests
{
    [AvaloniaFact]
    public void Preset_editor_target_box_excludes_fon_and_shows_ttf()
    {
        TestServices.EnsureInitialized("en");
        var store = AppHost.Get<UserDataStore>();
        var editor = new PresetEditorViewModel(store.FindPreset("to-ttf")!, AppHost.Get<ILocalizer>());
        var window = new PresetEditorWindow { DataContext = editor, Height = 900 };
        window.Show();
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        try
        {
            var box = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "TargetBox");
            var targets = box.Items.OfType<Choice<string>>().Select(c => c.Value).ToList();
            Assert.NotEmpty(targets);
            Assert.DoesNotContain("fon", targets);
            Assert.Single(targets, t => t == "ttf");
            Assert.Equal("ttf", ((Choice<string>)box.SelectedItem!).Value);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Web_slices_show_and_save_the_page_width_and_resolution_scale()
    {
        TestServices.EnsureInitialized("en");
        var store = AppHost.Get<UserDataStore>();
        var preset = store.FindPreset("web-png-16-9")!.Clone();
        var editor = new PresetEditorViewModel(preset, AppHost.Get<ILocalizer>());

        Assert.True(editor.ShowWeb);
        Assert.Equal("16:9", editor.WebRatio);
        Assert.Equal(1440, editor.WebWidth);
        Assert.Equal(1440, editor.WebOutputWidth);
        Assert.Equal(100, editor.WebZoomPercent);
        Assert.Null(editor.WebRatioError);

        editor.WebScale = 2;
        editor.WebZoomPercent = 125;
        Assert.Equal(2880, editor.WebOutputWidth);
        Assert.True(editor.Apply());
        Assert.Equal(2, preset.Web.DeviceScale);
        Assert.Equal(1.25, preset.Web.PageZoom);
        Assert.Equal(1440, preset.Web.ViewportWidth);

        // A plain PNG preset has no web card, and an invalid ratio blocks the save.
        Assert.False(new PresetEditorViewModel(store.FindPreset("to-png")!.Clone(), AppHost.Get<ILocalizer>()).ShowWeb);
        editor.WebRatio = "nonsense";
        Assert.NotNull(editor.WebRatioError);
        Assert.False(editor.Apply());
    }
}
