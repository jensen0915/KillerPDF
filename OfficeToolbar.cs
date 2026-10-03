using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace KillerPDF
{
    public partial class MainWindow
    {
        private int _officeTask;
        private bool _officeToolbarInitialized;
        private readonly List<ToolBar> _officeTaskBars = new();
        private readonly List<Button> _officeTaskTabs = new();
        private readonly List<Button> _officeDocumentButtons = new();
        private readonly Dictionary<EditTool, List<Button>> _officeToolButtons = new();
        private bool? _officeHasDocument;

        private void InitializeOfficeToolbar()
        {
            if (_officeToolbarInitialized) return;
            _officeToolbarInitialized = true;
            ToolbarGrid.Visibility = Visibility.Collapsed;
            OfficeToolbarHost.Visibility = Visibility.Visible;
            var stack = new StackPanel { Margin = new Thickness(8, 2, 8, 4) };
            OfficeToolbarHost.Children.Add(stack);
            var files = OfficeBar();
            var fileMenu = OfficeButton("Str_Office_File", OfficeFileMenu, false);
            files.Items.Add(fileMenu);
            files.Items.Add(OfficeButton("Str_Lbl_Open", Open_Click, false, "Ctrl+O"));
            files.Items.Add(OfficeButton("Str_Lbl_Save", Save_Click, true, "Ctrl+S"));
            files.Items.Add(OfficeButton("Str_Menu_SaveAs", SaveAs_Click, true, "Ctrl+Shift+S"));
            files.Items.Add(OfficeButton("Str_Lbl_Print", Print_Click, true, "Ctrl+P"));
            files.Items.Add(OfficeButton("Str_Lbl_Undo", Undo_Click, true, "Ctrl+Z"));
            foreach (UIElement item in files.Items) ToolBar.SetOverflowMode(item, OverflowMode.Never);
            stack.Children.Add(files);
            var tabs = new WrapPanel { Margin = new Thickness(2, 4, 0, 2) };
            string[] keys = { "Str_Office_Read", "Str_ToolbarGroup_FillSign", "Str_ToolbarGroup_Annotate", "Str_ToolbarGroup_Organize" };
            for (int i = 0; i < keys.Length; i++)
            {
                int group = i;
                var tab = OfficeButton(keys[i], (_, _) =>
                {
                    CommitActiveTextBox(); SetTool(EditTool.Select); ShowOfficeTask(group);
                }, false);
                _officeTaskTabs.Add(tab); tabs.Children.Add(tab);
            }
            stack.Children.Add(tabs);
            for (int i = 0; i < 4; i++)
            {
                var bar = OfficeBar(); _officeTaskBars.Add(bar); stack.Children.Add(bar);
            }
            var read = _officeTaskBars[0];
            OfficeTool(read, "Str_Lbl_Select", EditTool.Select);
            read.Items.Add(OfficeButton("Str_Lbl_Search", Search_Click, true, "Ctrl+F"));
            read.Items.Add(OfficeButton("Str_Zoom_FitWidth", (_, _) => FitToWidth()));
            read.Items.Add(OfficeButton("Str_Zoom_FitPage", (_, _) => FitToPage()));
            read.Items.Add(OfficeButton("Str_Office_View", OfficeViewMenu));
            read.Items.Add(OfficeButton("Str_Lbl_Ocr", OcrMenu_Click));
            var fill = _officeTaskBars[1];
            OfficeTool(fill, "Str_Office_AddText", EditTool.Text);
            fill.Items.Add(OfficeButton("Str_Lbl_Date", InsertDate_Click));
            fill.Items.Add(OfficeButton("Str_Lbl_Checkmark", InsertCheckmark_Click));
            fill.Items.Add(OfficeButton("Str_Lbl_Signature", ToolSignature_Click));
            fill.Items.Add(OfficeButton("Str_Lbl_FormHints", ToggleFormHints_Click));
            fill.Items.Add(OfficeButton("Str_Lbl_DigitalSig", (_, _) => OpenSignDialog()));
            var note = _officeTaskBars[2];
            OfficeTool(note, "Str_Lbl_Highlight", EditTool.Highlight);
            OfficeTool(note, "Str_Lbl_Underline", EditTool.Underline);
            OfficeTool(note, "Str_Lbl_Strike", EditTool.Strikethrough);
            OfficeTool(note, "Str_Lbl_Draw", EditTool.Draw);
            OfficeTool(note, "Str_Lbl_Line", EditTool.Line);
            OfficeTool(note, "Str_Lbl_Image", EditTool.Image);
            note.Items.Add(OfficeButton("Str_Lbl_Clear", ClearAllAnnotations_Click));
            var pages = _officeTaskBars[3];
            pages.Items.Add(OfficeButton("Str_Lbl_Merge", Merge_Click, false));
            pages.Items.Add(OfficeButton("Str_Lbl_Extract", Split_Click));
            pages.Items.Add(OfficeButton("Str_Lbl_Rotate", ToolRotate_Click));
            OfficeTool(pages, "Str_Lbl_Crop", EditTool.Crop);
            pages.Items.Add(OfficeButton("Str_Lbl_Delete", Delete_Click));
            pages.Items.Add(OfficeButton("Str_Lbl_MoveUp", MoveUp_Click));
            pages.Items.Add(OfficeButton("Str_Lbl_MoveDown", MoveDown_Click));
            pages.Items.Add(OfficeButton("Str_Lbl_Stamp", ToolStamp_Click));
            ShowOfficeTask(_officeTask);
            UpdateOfficeTaskForTool(_currentTool);
            UpdateOfficeDocumentState();
            OfficeToolbarHost.LayoutUpdated += OfficeToolbarLayoutUpdated;
        }

        private void OfficeToolbarLayoutUpdated(object? sender, EventArgs e) => UpdateOfficeDocumentState();

        private ToolBar OfficeBar()
        {
            var bar = new ToolBar { Padding = new Thickness(0), BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 36 };
            bar.SetResourceReference(Control.BackgroundProperty, "BgSidebar");
            bar.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
            bar.SetResourceReference(Control.TemplateProperty, "OfficeToolbarTemplate");
            // Native ToolBarPanel measures available width and puts only excess commands in overflow.
            bar.SetBinding(FrameworkElement.WidthProperty, new Binding("ActualWidth") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(StackPanel), 1) });
            bar.Loaded += (_, _) =>
            {
                if (bar.Template.FindName("OverflowButton", bar) is ToggleButton more)
                {
                    more.Focusable = true; more.IsTabStop = true; more.ToolTip = Loc("Str_Office_More");
                    AutomationProperties.SetName(more, Loc("Str_Office_More"));
                }
            };
            return bar;
        }

        private Button OfficeButton(string key, RoutedEventHandler action, bool document = true, string shortcut = "")
        {
            string label = Loc(key).TrimEnd('.');
            var button = UiKit.Make(label, accent: false);
            string glyph = key switch
            {
                "Str_Lbl_Open" => "\uE8E5", "Str_Lbl_Save" => "\uE74E",
                "Str_Menu_SaveAs" => "\uE792", "Str_Lbl_Print" => "\uE749",
                "Str_Lbl_Undo" => "\uE7A7", _ => ""
            };
            if (glyph.Length > 0)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(new TextBlock { Text = glyph, FontFamily = UiKit.IconFont,
                    Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                content.Children.Add(new TextBlock { Text = label });
                button.Content = content;
            }
            button.MinHeight = 36; button.FontSize = 14;
            button.Padding = new Thickness(8, 4, 8, 4); button.Margin = new Thickness(1, 0, 1, 0);
            button.ToolTip = label + (shortcut.Length == 0 ? "" : " (" + shortcut + ")");
            button.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
            AutomationProperties.SetName(button, label);
            button.Click += action;
            button.MouseLeave += (_, _) => { ShowOfficeTask(_officeTask); UpdateOfficeToolHighlight(); };
            if (document) _officeDocumentButtons.Add(button);
            return button;
        }

        private void OfficeTool(ToolBar bar, string key, EditTool tool)
        {
            var button = OfficeButton(key, (_, _) => SetTool(tool));
            if (!_officeToolButtons.TryGetValue(tool, out var buttons)) _officeToolButtons[tool] = buttons = new();
            buttons.Add(button); bar.Items.Add(button);
        }

        private void ShowOfficeTask(int task)
        {
            _officeTask = task;
            for (int i = 0; i < _officeTaskBars.Count; i++)
            {
                _officeTaskBars[i].Visibility = i == task ? Visibility.Visible : Visibility.Collapsed;
                _officeTaskTabs[i].SetResourceReference(Control.BackgroundProperty, i == task ? "SelectionBg" : "BgSidebar");
                _officeTaskTabs[i].SetResourceReference(Control.ForegroundProperty, i == task ? "SelectionFg" : "TextPrimary");
            }
        }

        private void UpdateOfficeTaskForTool(EditTool tool)
        {
            if (!_officeToolbarInitialized) return;
            if (tool is EditTool.Text or EditTool.Signature) ShowOfficeTask(1);
            else if (tool is EditTool.Highlight or EditTool.Underline or EditTool.Strikethrough or EditTool.Draw or EditTool.Line or EditTool.Image) ShowOfficeTask(2);
            else if (tool is EditTool.Rotate or EditTool.Crop) ShowOfficeTask(3);
            UpdateOfficeToolHighlight();
        }

        private void UpdateOfficeToolHighlight()
        {
            foreach (var pair in _officeToolButtons)
                foreach (var button in pair.Value)
                {
                    button.SetResourceReference(Control.BackgroundProperty, pair.Key == _currentTool ? "SelectionBg" : "BgPanel");
                    button.SetResourceReference(Control.ForegroundProperty, pair.Key == _currentTool ? "SelectionFg" : "TextPrimary");
                }
        }

        private void UpdateOfficeDocumentState()
        {
            bool hasDocument = _doc != null;
            if (_officeHasDocument == hasDocument) return;
            _officeHasDocument = hasDocument;
            foreach (var button in _officeDocumentButtons) button.IsEnabled = hasDocument;
            UpdateOfficeSaveStatus();
        }

        private void UpdateOfficeSaveStatus()
        {
            if (OfficeSaveStatus != null)
                OfficeSaveStatus.Text = _doc == null ? "" : Loc(_isDirty ? "Str_Office_Unsaved" : "Str_Office_Saved");
        }

        private void RefreshOfficeToolbarLanguage()
        {
            if (!_officeToolbarInitialized) return;
            OfficeToolbarHost.LayoutUpdated -= OfficeToolbarLayoutUpdated;
            OfficeToolbarHost.Children.Clear(); _officeTaskBars.Clear(); _officeTaskTabs.Clear();
            _officeDocumentButtons.Clear(); _officeToolButtons.Clear(); _officeHasDocument = null;
            _officeToolbarInitialized = false; InitializeOfficeToolbar(); UpdateOfficeSaveStatus();
            PageList.Items.Refresh();
        }

        private void OpenOfficeMenu(Button button, params MenuItem[] items)
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
            foreach (var item in items) menu.Items.Add(item);
            menu.Closed += (_, _) => button.Focus();
            menu.Opened += (_, _) => menu.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            menu.IsOpen = true;
        }

        private void OfficeFileMenu(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var compatible = MakeMenuItem(Loc("Str_Lbl_Flatten"), SaveFlattened_Click);
            compatible.IsEnabled = _doc != null;
            var info = MakeMenuItem(Loc("Str_Info_Title"), DocInfo_Click); info.IsEnabled = _doc != null;
            OpenOfficeMenu(button, MakeMenuItem(Loc("Str_Lbl_New"), New_Click, "Ctrl+N"),
                MakeMenuItem(Loc("Str_Lbl_Recent"), (_, _) => OpenRecent_Click(button, new RoutedEventArgs())),
                compatible, info, MakeMenuItem(Loc("Str_InstallBtn"), Install_Click),
                MakeMenuItem(Loc("Str_Lbl_Close"), CloseFile_Click, "Ctrl+W"));
        }

        private void OfficeViewMenu(object sender, RoutedEventArgs e) => OpenOfficeMenu((Button)sender,
            MakeMenuItem(Loc("Str_KS_FullScreen"), (_, _) => ToggleFullScreen(), "F11"),
            MakeMenuItem(Loc("Str_View_Single"), (_, _) => SelectViewMode(ViewMode.Single)),
            MakeMenuItem(Loc("Str_View_Continuous"), (_, _) => SelectViewMode(ViewMode.Continuous)),
            MakeMenuItem(Loc("Str_View_TwoPage"), (_, _) => SelectViewMode(ViewMode.TwoPage)),
            MakeMenuItem(Loc("Str_View_Grid"), (_, _) => SelectViewMode(ViewMode.Grid)));
    }

}
