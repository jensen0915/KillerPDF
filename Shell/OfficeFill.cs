using System;
using System.Windows;
using System.Windows.Controls;
using KillerPDF.Controls;
using KillerPDF.Services;

namespace KillerPDF
{
    public partial class MainWindow
    {
        // Text the next Text-tool click drops into its new box - a date - so it inherits the text
        // annotation's drag, resize, restyle and undo instead of being a new kind of object. Taken
        // (and cleared) by the viewer's PlaceTextBox through IViewerHost; any tool change drops it.
        private string? _officeTextPreset;

        // Check mark mode: every Text-tool click on the page drops a finished check mark centred on
        // the click - a form has a row of boxes to tick, and an edit box around each mark only hid
        // it. Stays on until the button is clicked again, Esc, or another tool.
        private bool _officeCheckMode;
        internal const string OfficeCheckMark = "✓";

        private bool OfficePresetActive => _officeCheckMode || _officeTextPreset is not null;

        private void ClearOfficePresets()
        {
            _officeTextPreset = null;
            _officeCheckMode = false;
            UpdateOfficeToolHighlight();
        }

        private void InsertCheckmark_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null) { KillerDialog.Show(this, Loc("Str_Msg_OpenFirst")); return; }
            if (_officeCheckMode && _currentTool == EditTool.Text)
            {
                ClearOfficePresets();
                SetStatus(Loc("Str_Office_ClickToPlace"));
                return;
            }
            if (_currentTool != EditTool.Text) SetTool(EditTool.Text);
            else CommitActiveTextBox();
            _officeTextPreset = null;
            _officeCheckMode = true;
            UpdateOfficeToolHighlight();
            SetStatus(Loc("Str_Office_CheckModeOn"));
        }

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
            _officeCheckMode = false;
            _officeTextPreset = text;
            UpdateOfficeToolHighlight();
            SetStatus(Loc("Str_Office_ClickToPlace"));
        }

        private string? TakeOfficeTextPreset()
        {
            string? text = _officeTextPreset;
            if (text is null) return null;
            _officeTextPreset = null;
            UpdateOfficeToolHighlight();   // the date is placed; Add text is the live tool again
            return text;
        }

        // Right-click placement: the text goes straight to the clicked spot, no second click. A check
        // mark is placed finished, centred on the click, like check mark mode does.
        private void PlaceOfficeTextAt(string text, Point pos, int pageIdx)
        {
            if (text == OfficeCheckMark)
            {
                if (_doc is null) { KillerDialog.Show(this, Loc("Str_Msg_OpenFirst")); return; }
                if (_currentTool != EditTool.Text) SetTool(EditTool.Text);
                else CommitActiveTextBox();
                _activeCanvas = CanvasForPage(pageIdx);
                ActiveViewer.PlaceStampTextExt(text, pos, pageIdx);
                return;
            }
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
