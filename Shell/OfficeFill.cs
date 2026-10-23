using System;
using System.Windows;
using System.Windows.Controls;
using KillerPDF.Controls;
using KillerPDF.Services;

namespace KillerPDF
{
    public partial class MainWindow
    {
        // Text the next Text-tool click drops into its new box - a date or a check mark - so those
        // inherit the text annotation's drag, resize, restyle and undo instead of being a new kind of
        // object. Taken (and cleared) by the viewer's PlaceTextBox through IViewerHost; any tool
        // change drops it.
        private string? _officeTextPreset;

        private void InsertCheckmark_Click(object sender, RoutedEventArgs e) => BeginOfficeText("✓");

        // "Highlight fields": tint fillable form fields, or show the page exactly as printed.
        private void ToggleFormHints_Click(object sender, RoutedEventArgs e)
        {
            PdfViewer.FormHintsEnabled = !PdfViewer.FormHintsEnabled;
            Viewer.RefreshVisibleFormFieldsExt();
            ViewerB.RefreshVisibleFormFieldsExt();
            SetStatus(Loc(PdfViewer.FormHintsEnabled ? "Str_FormHints_On" : "Str_FormHints_Off"));
        }

        private void BeginOfficeText(string text)
        {
            if (_doc is null) { KillerDialog.Show(this, Loc("Str_Msg_OpenFirst")); return; }
            // Re-selecting the active Text tool would tuck its settings bar away instead.
            if (_currentTool != EditTool.Text) SetTool(EditTool.Text);
            else CommitActiveTextBox();
            _officeTextPreset = text;
            SetStatus(Loc("Str_Office_ClickToPlace"));
        }

        private string? TakeOfficeTextPreset()
        {
            string? text = _officeTextPreset;
            _officeTextPreset = null;
            return text;
        }

        // Right-click placement: the text goes straight to the clicked spot, no second click.
        private void PlaceOfficeTextAt(string text, Point pos, int pageIdx)
        {
            BeginOfficeText(text);
            if (_officeTextPreset is null) return;   // no document
            _activeCanvas = CanvasForPage(pageIdx);
            PlaceTextBox(pos, pageIdx);
        }

        private void InsertDate_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null) { KillerDialog.Show(this, Loc("Str_Msg_OpenFirst")); return; }
            if (PromptOfficeDate() is string text) BeginOfficeText(text);
        }

        // Gregorian or ROC date picker; returns the formatted text, or null when cancelled. The ROC
        // choice is remembered, since an office tends to use one style for all its forms.
        private string? PromptOfficeDate()
        {
            string? result = null;
            var win = new Window { Width = 390, SizeToContent = SizeToContent.Height, Title = Loc("Str_Lbl_Date") };
            DialogChrome.Configure(win, this);
            var body = new StackPanel { Margin = new Thickness(16) };
            var date = new DatePicker { SelectedDate = DateTime.Today, MinHeight = 32, FontSize = 14 };
            var roc = UiKit.CheckBox(Loc("Str_Office_RocDate"));
            roc.Margin = new Thickness(0, 12, 0, 12);
            roc.IsChecked = App.GetSetting("OfficeDateRoc") == "1";
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var insert = UiKit.Make(Loc("Str_Office_Insert"), true);
            insert.IsDefault = true;
            insert.Click += (_, _) =>
            {
                bool useRoc = roc.IsChecked == true;
                if (date.SelectedDate is not DateTime value
                    || !OfficeDateText.TryFormat(value, useRoc, out string text))
                {
                    date.Focus();
                    return;
                }
                App.SetSetting("OfficeDateRoc", useRoc ? "1" : "0");
                result = text;
                win.Close();
            };
            var cancel = UiKit.Make(Loc("Str_Btn_Cancel"), false);
            cancel.Margin = new Thickness(8, 0, 0, 0);
            cancel.Click += (_, _) => win.Close();
            buttons.Children.Add(insert);
            buttons.Children.Add(cancel);
            body.Children.Add(date);
            body.Children.Add(roc);
            body.Children.Add(buttons);
            win.Content = DialogChrome.Frame(win, this, win.Title, () => win.Close(), body);
            win.ShowDialog();
            return result;
        }
    }
}
