using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using KillerPDF.Controls;

namespace KillerPDF
{
    // Fork (jensen0915/KillerPDF): the office toolbar. File commands and four task tabs on the
    // first row - Read, Fill & sign, Annotate, Organize pages - and the active task's commands
    // below, captioned rather than icon-only, so someone who handles a form once a month can find
    // the command by name. Commands that do not fit move into "More". The original toolbar stays
    // in the window and comes back with the "Use the original toolbar" switch.
    public partial class MainWindow
    {
        private const string OfficeToolbarSetting = "OfficeToolbar";

        private int _officeTask;
        private bool _officeToolbarBuilt;
        private readonly List<ToolBar> _officeTaskBars = [];
        private readonly List<Button> _officeTaskTabs = [];
        private readonly List<Button> _officeDocumentButtons = [];
        private readonly Dictionary<EditTool, List<Button>> _officeToolButtons = [];
        // Date and check mark ride on the Text tool, so they are lit from their own state rather
        // than from the current tool.
        private Button? _officeDateButton;
        private Button? _officeCheckButton;
        private TextBlock? _officeSaveStatus;
        private bool? _officeHasDocument;
        private int _officeShownStatus = -1;   // 0 no document, 1 saved, 2 unsaved

        private static bool OfficeToolbarEnabled => App.GetSetting(OfficeToolbarSetting) != "0";

        private void InitializeOfficeToolbar()
        {
            bool office = OfficeToolbarEnabled;
            ToolbarGrid.Visibility = office ? Visibility.Collapsed : Visibility.Visible;
            OfficeToolbarHost.Visibility = office ? Visibility.Visible : Visibility.Collapsed;
            if (office) BuildOfficeToolbar();
        }

        private void SetOfficeToolbarEnabled(bool enabled)
        {
            App.SetSetting(OfficeToolbarSetting, enabled ? "1" : "0");
            ClearOfficeToolbar();
            InitializeOfficeToolbar();
            // Deferred: this can run from a click on one of ToolbarMenu's own items.
            Dispatcher.BeginInvoke(new Action(BuildToolbarMenu));
        }

        private void BuildOfficeToolbar()
        {
            if (_officeToolbarBuilt) return;
            _officeToolbarBuilt = true;
            var stack = new StackPanel();
            OfficeToolbarHost.Children.Add(stack);

            // Row 1: file commands, task tabs, saved / unsaved state.
            var top = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 2) };
            _officeSaveStatus = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 4, 0),
                FontSize = 13
            };
            _officeSaveStatus.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            DockPanel.SetDock(_officeSaveStatus, Dock.Right);
            top.Children.Add(_officeSaveStatus);
            var row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(OfficeButton("Str_Office_File", OfficeFileMenu, document: false, glyph: "", menu: true));
            row.Children.Add(OfficeButton("Str_Lbl_Open", Open_Click, document: false, shortcut: "Ctrl+O", glyph: ""));
            row.Children.Add(OfficeButton("Str_Lbl_Save", OfficeSave_Click, shortcut: "Ctrl+S", glyph: ""));
            row.Children.Add(OfficeButton("Str_Menu_SaveAs", SaveAs_Click, shortcut: "Ctrl+Shift+S", glyph: ""));
            row.Children.Add(OfficeButton("Str_Lbl_Print", Print_Click, shortcut: "Ctrl+P", glyph: ""));
            row.Children.Add(OfficeButton("Str_Lbl_Undo", Undo_Click, shortcut: "Ctrl+Z", glyph: ""));
            var divider = new Border { Width = 1, Margin = new Thickness(10, 6, 10, 6) };
            divider.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");
            row.Children.Add(divider);
            string[] tasks = ["Str_Office_Read", "Str_ToolbarGroup_FillSign", "Str_ToolbarGroup_Annotate", "Str_ToolbarGroup_Organize"];
            for (int i = 0; i < tasks.Length; i++)
            {
                int task = i;
                var tab = OfficeButton(tasks[i], (_, _) =>
                {
                    CommitActiveTextBox();
                    SetTool(EditTool.Select);
                    ShowOfficeTask(task);
                }, document: false);
                _officeTaskTabs.Add(tab);
                row.Children.Add(tab);
            }
            top.Children.Add(row);
            stack.Children.Add(top);

            // Row 2: the active task's commands.
            for (int i = 0; i < tasks.Length; i++)
            {
                var bar = OfficeBar();
                _officeTaskBars.Add(bar);
                stack.Children.Add(bar);
            }

            var read = _officeTaskBars[0];
            OfficeTool(read, "Str_Lbl_Select", EditTool.Select);
            read.Items.Add(OfficeButton("Str_Lbl_Search", Search_Click, shortcut: "Ctrl+F"));
            read.Items.Add(OfficeButton("Str_Zoom_FitWidth", (_, _) => FitToWidth()));
            read.Items.Add(OfficeButton("Str_Zoom_FitPage", (_, _) => FitToPage()));
            read.Items.Add(OfficeButton("Str_Office_View", OfficeViewMenu, menu: true));
            read.Items.Add(OfficeButton("Str_Lbl_Ocr", OcrMenu_Click, menu: true));
            read.Items.Add(OfficeButton("Str_TT_ComparePDFs", ComparePdf_Click));

            var fill = _officeTaskBars[1];
            // Add text after a date or check mark: back to a plain box, without tucking the text bar
            // away the way re-clicking the current tool does.
            OfficeTool(fill, "Str_Office_AddText", EditTool.Text, (_, _) =>
            {
                if (_currentTool == EditTool.Text && OfficePresetActive) ClearOfficePresets();
                else SetTool(EditTool.Text);
                SetStatus(Loc("Str_Office_ClickToPlace"));
            });
            _officeDateButton = OfficeButton("Str_Lbl_Date", InsertDate_Click);
            fill.Items.Add(_officeDateButton);
            _officeCheckButton = OfficeButton("Str_Lbl_Checkmark", InsertCheckmark_Click);
            fill.Items.Add(_officeCheckButton);
            OfficeTool(fill, "Str_Lbl_Signature", EditTool.Signature, ToolSignature_Click);
            fill.Items.Add(OfficeButton("Str_Lbl_FormHints", ToggleFormHints_Click));
            OfficeTool(fill, "Str_Office_FormFields", EditTool.FormField, ToolFormField_Click);
            fill.Items.Add(OfficeButton("Str_Lbl_DigitalSig", (_, _) => OpenSignDialog()));

            var note = _officeTaskBars[2];
            OfficeTool(note, "Str_Lbl_Highlight", EditTool.Highlight, ToolHighlight_Click);
            OfficeTool(note, "Str_Lbl_Line", EditTool.Line, ToolLine_Click);
            OfficeTool(note, "Str_Lbl_Shape", EditTool.Shape, ToolShape_Click);
            OfficeTool(note, "Str_Lbl_Draw", EditTool.Draw, ToolDraw_Click);
            OfficeTool(note, "Str_Lbl_Image", EditTool.Image, ToolImage_Click);
            OfficeTool(note, "Str_Lbl_Measure", EditTool.Measure, ToolMeasure_Click);
            note.Items.Add(OfficeButton("Str_Lbl_Clear", ClearAllAnnotations_Click));

            var pages = _officeTaskBars[3];
            pages.Items.Add(OfficeButton("Str_Lbl_Merge", Merge_Click));
            pages.Items.Add(OfficeButton("Str_Lbl_Extract", Split_Click));
            pages.Items.Add(OfficeButton("Str_Lbl_Rotate", ToolRotate_Click));
            OfficeTool(pages, "Str_Lbl_Crop", EditTool.Crop, ToolCrop_Click);
            pages.Items.Add(OfficeButton("Str_Lbl_Delete", Delete_Click));
            pages.Items.Add(OfficeButton("Str_Lbl_MoveUp", MoveUp_Click));
            pages.Items.Add(OfficeButton("Str_Lbl_MoveDown", MoveDown_Click));
            pages.Items.Add(OfficeButton("Str_Lbl_Stamp", ToolStamp_Click));

            ShowOfficeTask(_officeTask);
            UpdateOfficeTaskForTool(_currentTool);
            _officeHasDocument = null;
            _officeShownStatus = -1;
            UpdateOfficeDocumentState();
            OfficeToolbarHost.LayoutUpdated += OfficeToolbarLayoutUpdated;
        }

        private void ClearOfficeToolbar()
        {
            OfficeToolbarHost.LayoutUpdated -= OfficeToolbarLayoutUpdated;
            OfficeToolbarHost.Children.Clear();
            _officeTaskBars.Clear();
            _officeTaskTabs.Clear();
            _officeDocumentButtons.Clear();
            _officeToolButtons.Clear();
            _officeDateButton = _officeCheckButton = null;
            _officeSaveStatus = null;
            _officeToolbarBuilt = false;
        }

        // Rebuild after a language or theme change so captions and local brushes follow.
        private void RefreshOfficeToolbar()
        {
            if (!_officeToolbarBuilt) return;
            ClearOfficeToolbar();
            BuildOfficeToolbar();
        }

        // Cheap enough to run on layout: enables document commands when a document opens or closes
        // and keeps the saved / unsaved label in step with tab switches.
        private void OfficeToolbarLayoutUpdated(object? sender, EventArgs e) => UpdateOfficeDocumentState();

        private void UpdateOfficeDocumentState()
        {
            bool hasDocument = _doc is not null;
            if (_officeHasDocument != hasDocument)
            {
                _officeHasDocument = hasDocument;
                foreach (var button in _officeDocumentButtons) button.IsEnabled = hasDocument;
            }
            int status = !hasDocument ? 0 : _isDirty ? 2 : 1;
            if (_officeSaveStatus is null || _officeShownStatus == status) return;
            _officeShownStatus = status;
            _officeSaveStatus.Text = status switch
            {
                2 => Loc("Str_Office_Unsaved"),
                1 => Loc("Str_Office_Saved"),
                _ => ""
            };
        }

        private ToolBar OfficeBar()
        {
            var bar = new ToolBar
            {
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = 38,
                Template = (ControlTemplate)FindResource("OfficeToolbarTemplate")
            };
            bar.SetResourceReference(Control.BackgroundProperty, "BackgroundBrush");
            bar.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            KeyboardNavigation.SetTabNavigation(bar, KeyboardNavigationMode.Continue);
            return bar;
        }

        private Button OfficeButton(string key, RoutedEventHandler action, bool document = true,
            string shortcut = "", string glyph = "", bool menu = false)
        {
            string label = Loc(key).TrimEnd('.', '…');
            var button = UiKit.Make(label, accent: false);
            if (glyph.Length > 0 || menu)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                if (glyph.Length > 0)
                    content.Children.Add(new TextBlock
                    {
                        Text = glyph, FontFamily = UiKit.IconFont, FontSize = 14,
                        Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
                    });
                content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
                if (menu)
                    content.Children.Add(new TextBlock
                    {
                        Text = "", FontFamily = UiKit.IconFont, FontSize = 9,
                        Margin = new Thickness(6, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center
                    });
                button.Content = content;
            }
            button.MinHeight = 36;
            button.FontSize = 14;
            button.Padding = new Thickness(10, 4, 10, 4);
            button.Margin = new Thickness(1, 0, 1, 0);
            button.ToolTip = shortcut.Length == 0 ? label : $"{label} ({shortcut})";
            AutomationProperties.SetName(button, label);
            // A menu opened from a command that sits in "More" anchors to the More button: the overflow
            // popup closes on the click, leaving the original button nowhere on screen.
            button.Click += menu ? (s, e) => action(MenuAnchor(button), e) : action;
            // UiKit's hover handlers repaint the background on leave; restore the task / tool highlight.
            button.MouseLeave += (_, _) => { ShowOfficeTask(_officeTask); UpdateOfficeToolHighlight(); };
            if (document) _officeDocumentButtons.Add(button);
            return button;
        }

        // A tool button. Tools that have their own click handler (signature, crop, ...) keep its
        // behavior; the rest just switch the tool.
        private void OfficeTool(ToolBar bar, string key, EditTool tool, RoutedEventHandler? click = null)
        {
            var button = OfficeButton(key, click ?? ((_, _) =>
            {
                SetTool(tool);
                if (tool == EditTool.Text) SetStatus(Loc("Str_Office_ClickToPlace"));
            }));
            if (!_officeToolButtons.TryGetValue(tool, out var buttons)) _officeToolButtons[tool] = buttons = [];
            buttons.Add(button);
            bar.Items.Add(button);
        }

        private void ShowOfficeTask(int task)
        {
            _officeTask = task;
            for (int i = 0; i < _officeTaskBars.Count; i++)
            {
                bool active = i == task;
                _officeTaskBars[i].Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                _officeTaskTabs[i].SetResourceReference(Control.BackgroundProperty, active ? "SelectionBg" : "BackgroundBrush");
                _officeTaskTabs[i].SetResourceReference(Control.ForegroundProperty, active ? "SelectionFg" : "TextBrush");
            }
        }

        // Called from SetTool: a tool chosen elsewhere (shortcut key, context menu) brings its task forward.
        private void UpdateOfficeTaskForTool(EditTool tool)
        {
            if (!_officeToolbarBuilt) return;
            if (tool is EditTool.Text or EditTool.Signature or EditTool.FormField) ShowOfficeTask(1);
            else if (tool is EditTool.Highlight or EditTool.Line or EditTool.Shape or EditTool.Draw
                     or EditTool.Image or EditTool.Measure) ShowOfficeTask(2);
            else if (tool is EditTool.Crop) ShowOfficeTask(3);
            UpdateOfficeToolHighlight();
        }

        private void UpdateOfficeToolHighlight()
        {
            bool text = _currentTool == EditTool.Text;
            foreach (var pair in _officeToolButtons)
            {
                // While a date or check mark is pending, its own button is lit instead of Add text.
                bool active = pair.Key == _currentTool && !(text && OfficePresetActive);
                foreach (var button in pair.Value) PaintOfficeButton(button, active);
            }
            if (_officeDateButton is not null) PaintOfficeButton(_officeDateButton, text && _officeTextPreset is not null);
            if (_officeCheckButton is not null) PaintOfficeButton(_officeCheckButton, text && _officeCheckMode);
        }

        private static void PaintOfficeButton(Button button, bool active)
        {
            button.SetResourceReference(Control.BackgroundProperty, active ? "SelectionBg" : "PaneBrush");
            button.SetResourceReference(Control.ForegroundProperty, active ? "SelectionFg" : "TextBrush");
        }

        // Toolbar Save writes straight back like Ctrl+S; Save As stays one click away. The warning
        // about the same file being edited in the other pane still comes first.
        private void OfficeSave_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is not null && !string.IsNullOrEmpty(_originalFile) && OtherPaneHasDirtyCopyOf(_originalFile))
            {
                var warn = KillerDialog.Show(this,
                    string.Format(Loc("Str_Dlg_SaveOtherPaneDirty"), System.IO.Path.GetFileName(_originalFile)),
                    Loc("Str_Dlg_AppTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Warning,
                    defaultResult: MessageBoxResult.Cancel);
                if (warn != MessageBoxResult.OK) return;
            }
            SaveInPlace();
        }

        private static FrameworkElement MenuAnchor(Button button)
        {
            if (!ToolBar.GetIsOverflowItem(button) || LogicalTreeHelper.GetParent(button) is not ToolBar bar)
                return button;
            return bar.Template?.FindName("OverflowButton", bar) as FrameworkElement ?? bar;
        }

        private void OpenOfficeMenu(FrameworkElement button, params Control[] items)
        {
            var menu = MakeThemedMenu();
            foreach (var item in items) menu.Items.Add(item);
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.Closed += (_, _) => button.Focus();
            menu.Opened += (_, _) => menu.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            menu.IsOpen = true;
        }

        private void OfficeFileMenu(object sender, RoutedEventArgs e)
        {
            var button = (FrameworkElement)sender;
            bool hasDoc = _doc is not null;
            MenuItem DocItem(string key, RoutedEventHandler click, string? gesture = null)
            {
                var item = MakeMenuItem(Loc(key), click, gesture);
                item.IsEnabled = hasDoc;
                return item;
            }
            var original = new MenuItem { Header = Loc("Str_Office_UseOriginalToolbar") };
            original.Click += (_, _) => SetOfficeToolbarEnabled(false);
            OpenOfficeMenu(button,
                MakeMenuItem(Loc("Str_Lbl_New"), New_Click, "Ctrl+N"),
                // Pass the item's own click args: OpenRecent_Click marks them handled, and a bare
                // RoutedEventArgs (no RoutedEvent) throws on that.
                MakeMenuItem(Loc("Str_TT_RecentFiles"), (_, args) => OpenRecent_Click(button, args)),
                new Separator(),
                DocItem("Str_Lbl_Flatten", SaveFlattened_Click),
                DocItem("Str_Menu_ExportImages", ExportImages_Click),
                DocItem("Str_Office_DocInfo", DocInfo_Click, "F4"),
                new Separator(),
                DocItem("Str_Lbl_Close", CloseFile_Click, "Ctrl+W"),
                new Separator(),
                original);
        }

        private void OfficeViewMenu(object sender, RoutedEventArgs e) => OpenOfficeMenu((FrameworkElement)sender,
            MakeMenuItem(Loc("Str_KS_FullScreen"), (_, _) => ToggleFullScreen(), "F11"),
            new Separator(),
            MakeMenuItem(Loc("Str_View_Single"), (_, _) => SelectViewMode(ViewMode.Single)),
            MakeMenuItem(Loc("Str_View_Continuous"), (_, _) => SelectViewMode(ViewMode.Continuous)),
            MakeMenuItem(Loc("Str_View_TwoPage"), (_, _) => SelectViewMode(ViewMode.TwoPage)),
            MakeMenuItem(Loc("Str_View_Grid"), (_, _) => SelectViewMode(ViewMode.Grid)));
    }
}
