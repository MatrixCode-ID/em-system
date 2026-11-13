using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Em.Editors;
using Em.Ui.Wpf.Controls;
using Xunit;

namespace Em.Ui.Wpf.Controls.Tests.Wpf;

[Collection("WPF desktop")]
public sealed class TextEditorTests
{
    [Fact]
    public async Task Standalone_editor_loads_its_template_binds_and_validates_in_both_themes()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(() =>
            {
                try
                {
                    CheckEditor();
                    completion.SetResult();
                }
                catch (Exception error)
                {
                    completion.SetException(error);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static void CheckEditor()
    {
        var model = new FormModel { Text = "Initial" };
        var editor = new TextEditor
        {
            Settings = new TextSettings { Validate = value => Equals(value, "Invalid") ? "Choose another value." : null }
        };
        editor.SetBinding(TextEditor.ValueProperty, new Binding(nameof(FormModel.Text))
        {
            Source = model,
            Mode = BindingMode.TwoWay
        });
        var window = new Window { Content = editor, Width = 300, SizeToContent = SizeToContent.Height, ShowInTaskbar = false };
        window.Show();
        try
        {
            Assert.NotNull(editor.Template);
            var typeface = new Typeface(editor.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            Assert.True(typeface.TryGetGlyphTypeface(out var glyph));
            Assert.Contains("Roboto", glyph.FamilyNames.Values);
            var input = Assert.IsType<TextBox>(editor.Template.FindName("PART_Input", editor));
            Assert.Equal("Initial", input.Text);

            foreach (var (mode, red) in new[] { (GridThemeMode.Light, (byte)0xF9), (GridThemeMode.Dark, (byte)0x11) })
            {
                GridTheme.SetMode(window, mode);
                window.UpdateLayout();
                Assert.Equal(red, Assert.IsType<SolidColorBrush>(editor.Background).Color.R);
                input.Text = "Invalid";
                Assert.False(editor.Commit());
                Assert.NotNull(editor.Error);
                Assert.True(editor.HasError);
                Assert.NotEqual("Invalid", model.Text);
                editor.Cancel();
                Assert.Equal(model.Text, input.Text);

                input.Text = mode.ToString();
                Assert.True(editor.Commit());
                Assert.Equal(mode.ToString(), model.Text);
            }
        }
        finally
        {
            window.Close();
        }
    }

    public sealed class FormModel
    {
        public string? Text { get; set; }
    }
}
