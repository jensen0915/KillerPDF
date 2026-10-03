using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace KillerPDF
{
    public partial class MainWindow
    {
        private string? _officeTextPreset;

        private void InsertCheckmark_Click(object sender, RoutedEventArgs e) => BeginOfficeText("✓");

        private void BeginOfficeText(string text)
        {
            if (_doc is null) return;
            SetTool(EditTool.Text);
            _officeTextPreset = text;
            SetStatus(Loc("Str_Office_ClickToPlace"));
        }

        private void InsertDate_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null) return;
            var win = new Window { Width = 390, SizeToContent = SizeToContent.Height, Title = Loc("Str_Lbl_Date") };
            DialogChrome.Configure(win, this);
            var body = new StackPanel { Margin = new Thickness(16) };
            var date = new DatePicker { SelectedDate = DateTime.Today, MinHeight = 36, FontSize = 14 };
            var roc = UiKit.CheckBox(Loc("Str_Office_RocDate"));
            roc.Margin = new Thickness(0, 12, 0, 12);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var insert = UiKit.Make(Loc("Str_Office_Insert"), true);
            insert.Click += (_, _) =>
            {
                if (date.SelectedDate is not DateTime value) { date.Focus(); return; }
                if (roc.IsChecked == true && value.Year <= 1911) { date.Focus(); return; }
                string text = roc.IsChecked == true
                    ? string.Format(CultureInfo.InvariantCulture, "{0:000}/{1:00}/{2:00}", value.Year - 1911, value.Month, value.Day)
                    : value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
                BeginOfficeText(text); win.Close();
            };
            var cancel = UiKit.Make(Loc("Str_Sig_Cancel"), false);
            cancel.Click += (_, _) => win.Close();
            buttons.Children.Add(insert); buttons.Children.Add(cancel);
            body.Children.Add(date); body.Children.Add(roc); body.Children.Add(buttons);
            win.Content = DialogChrome.Frame(win, this, win.Title, () => win.Close(), body);
            win.ShowDialog();
        }
    }
}
