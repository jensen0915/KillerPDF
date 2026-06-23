using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Win32;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using KillerPDF.Services;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;

namespace KillerPDF
{
    public partial class MainWindow
    {
        private void ToggleSearchBar()
        {
            if (_searchBar is not null && _searchBar.Visibility == Visibility.Visible)
            {
                CloseSearchBar();
                return;
            }
            ShowSearchBar();
        }

        private void ShowSearchBar()
        {
            if (_searchBar is null)
            {
                // Build search bar programmatically and inject into the preview area grid
                _searchBox = new TextBox
                {
                    Width = 200,
                    Height = 26,
                    FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI, Nirmala UI"),
                    FontSize = 13,
                    SelectionBrush = AccentBrush(),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                // Live (DynamicResource-style) brushes so the box recolors on a theme switch while the
                // bar is open, instead of baking colors in at build time. Background uses the dark
                // toolbar/titlebar tone (BgSidebar).
                _searchBox.SetResourceReference(Control.BackgroundProperty, "BgSidebar");
                _searchBox.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
                _searchBox.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "TextPrimary");
                _searchBox.SetResourceReference(Control.BorderBrushProperty, "BorderDim");
                _searchBox.KeyDown += SearchBox_KeyDown;
                _searchBox.TextChanged += SearchBox_TextChanged;

                // Custom template so the default WPF blue focus/hover border never shows; keep our themed border.
                var tbTemplate = new ControlTemplate(typeof(TextBox));
                var tbBorder = new FrameworkElementFactory(typeof(Border));
                tbBorder.SetValue(Border.BackgroundProperty, new System.Windows.TemplateBindingExtension(Control.BackgroundProperty));
                tbBorder.SetValue(Border.BorderBrushProperty, new System.Windows.TemplateBindingExtension(Control.BorderBrushProperty));
                tbBorder.SetValue(Border.BorderThicknessProperty, new System.Windows.TemplateBindingExtension(Control.BorderThicknessProperty));
                tbBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
                var tbHost = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
                tbHost.SetValue(ScrollViewer.PaddingProperty, new System.Windows.TemplateBindingExtension(Control.PaddingProperty));
                tbHost.SetValue(ScrollViewer.VerticalAlignmentProperty, VerticalAlignment.Center);
                tbBorder.AppendChild(tbHost);
                tbTemplate.VisualTree = tbBorder;
                _searchBox.Template = tbTemplate;
                _searchBox.FocusVisualStyle = null;

                // Fixed width + centered so the result count never resizes the bar.
                _searchStatus = new TextBlock
                {
                    FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI, Nirmala UI"),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Width = 56,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(2, 0, 2, 0)
                };
                _searchStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");

                // Small VSCode-style prev / next / close buttons. Hover tooltips carry the shortcuts.
                Button SearchNavBtn(string glyph, string tip, Action onClick)
                {
                    var b = new Button
                    {
                        Content    = glyph,
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize   = 12,
                        Width = 26, Height = 24,
                        Padding    = new Thickness(0),   // ToolbarButton's 10,6 padding clips the glyph in a 26px button
                        Style      = (Style)FindResource("ToolbarButton"),
                        ToolTip    = tip
                    };
                    b.Click += (_, _) => onClick();
                    return b;
                }
                var prevBtn  = SearchNavBtn("", "Previous Match (Shift+Enter)", SearchPrevResult); // ChevronUp
                var nextBtn  = SearchNavBtn("", "Next Match (Enter)", SearchNextResult);            // ChevronDown
                var closeBtn = SearchNavBtn("", "Close (Esc)", CloseSearchBar);                     // Cancel

                var searchIcon = new TextBlock
                {
                    Text = "",  // Segoe MDL2 Search / magnifying glass
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                    IsHitTestVisible = false
                };
                searchIcon.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");

                var panel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 6, 8, 6)
                };
                panel.Children.Add(searchIcon);
                panel.Children.Add(_searchBox);
                panel.Children.Add(_searchStatus);
                panel.Children.Add(prevBtn);
                panel.Children.Add(nextBtn);
                panel.Children.Add(closeBtn);

                _searchBar = new Border
                {
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    CornerRadius = new CornerRadius(0, 0, 4, 4),
                    Padding = new Thickness(4),
                    Child = GrainWrap(panel),
                    Margin = new Thickness(0, 0, 16, 0),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = 0.55 }
                };
                _searchBar.SetResourceReference(Border.BackgroundProperty, "BgPanel");
                _searchBar.SetResourceReference(Border.BorderBrushProperty, "AccentBorder");

                // Add to the preview area grid (parent of ScrollViewer)
                var previewGrid = PagePreviewPanel.Parent as Grid;
                if (previewGrid is not null)
                {
                    Panel.SetZIndex(_searchBar, 100);
                    previewGrid.Children.Add(_searchBar);
                    // Keep the whole bar on screen: when the preview area is resized, shrink the
                    // text box (not the buttons) so the bar never overflows the window.
                    previewGrid.SizeChanged += (_, _) => FitSearchBox();
                }
            }

            _searchBar.Visibility = Visibility.Visible;
            _searchBox!.Text = "";
            if (_searchStatus != null) _searchStatus.Text = "";
            FitSearchBox();
            _searchBox.Focus();
            Keyboard.Focus(_searchBox);
        }

        // Sizes the search text box to whatever width is left after the icon, status, and nav
        // buttons, capped at a comfortable 200px, so the full bar always fits the preview area.
        private void FitSearchBox()
        {
            if (_searchBar is null || _searchBox is null) return;
            double avail = (PagePreviewPanel.Parent as Grid)?.ActualWidth ?? 0;
            const double reserved = 210;   // icon + status + 3 buttons + paddings/margins
            _searchBox.Width = Math.Max(60, Math.Min(200, avail - reserved));
        }

        private void CloseSearchBar()
        {
            if (_searchBar is not null)
                _searchBar.Visibility = Visibility.Collapsed;
            ClearSearchHighlights();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CloseSearchBar();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                    SearchPrevResult();
                else
                    SearchNextResult();
                e.Handled = true;
            }
        }

        private System.Windows.Threading.DispatcherTimer? _searchDebounce;

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = _searchBox?.Text ?? "";
            if (text.Length < 2)
            {
                _searchDebounce?.Stop();
                ClearSearchHighlights();
                _allSearchRects.Clear();
                _searchResultPages.Clear();
                _searchPageCursor = -1;
                return;
            }
            // Debounce: wait for a brief pause in typing before searching, so the first keystrokes
            // on a large document don't lock the UI while it searches partial queries.
            if (_searchDebounce is null)
            {
                _searchDebounce = new System.Windows.Threading.DispatcherTimer
                    { Interval = TimeSpan.FromMilliseconds(250) };
                _searchDebounce.Tick += (_, _) =>
                {
                    _searchDebounce!.Stop();
                    var q = _searchBox?.Text ?? "";
                    if (q.Length >= 2) RunSearch(q);
                };
            }
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        private readonly SearchService _searchService = new();

        private void RunSearch(string query)
        {
            ClearSearchHighlights();
            _allSearchRects.Clear();
            _searchResultPages.Clear();
            _searchPageCursor = -1;

            if (string.IsNullOrWhiteSpace(query) || _currentFile is null)
            {
                if (_searchStatus != null) _searchStatus.Text = "";
                return;
            }

            try
            {
                var sr = _searchService.Search(_currentFile, query);

                foreach (var kvp in sr.PageRects)
                    _allSearchRects[kvp.Key] = kvp.Value;
                _searchResultPages.AddRange(sr.ResultPages);

                if (_searchResultPages.Count == 0)
                {
                    if (_searchStatus != null) _searchStatus.Text = "No matches";
                    return;
                }

                int startPage = PageList.SelectedIndex;
                _searchPageCursor = _searchResultPages.FindIndex(p => p >= startPage);
                if (_searchPageCursor < 0) _searchPageCursor = 0;

                _searchTotalHits = sr.TotalHits;
                UpdateSearchStatus();

                int targetPage = _searchResultPages[_searchPageCursor];
                if (PageList.SelectedIndex != targetPage)
                    PageList.SelectedIndex = targetPage;
                else
                    HighlightSearchResultsOnCurrentPage();
            }
            catch
            {
                if (_searchStatus != null) _searchStatus.Text = "Search error";
            }
        }

        private void HighlightSearchResultsOnCurrentPage()
        {
            ClearSearchHighlights();
            int curPage = PageList.SelectedIndex;
            if (!_allSearchRects.ContainsKey(curPage)) return;
            if (!_renderDims.ContainsKey(curPage)) return;

            var (renderW, renderH) = _renderDims[curPage];

            try
            {
                using var pigDoc = PdfPigDoc.Open(_currentFile!);
                var page = pigDoc.GetPage(curPage + 1);
                double pdfW = page.Width;
                double pdfH = page.Height;
                double sx = renderW / pdfW;
                double sy = renderH / pdfH;

                foreach (var (left, bottom, right, top) in _allSearchRects[curPage])
                    AddSearchHighlight(left, bottom, right, top, sx, sy, renderH);
            }
            catch { }
        }

        private int _searchTotalHits;

        // Compact VSCode-style count ("2 / 5"); full detail lives in the tooltip.
        private void UpdateSearchStatus()
        {
            if (_searchStatus is null) return;
            if (_searchResultPages.Count == 0)
            {
                _searchStatus.Text = "No matches";
                _searchStatus.ToolTip = null;
                return;
            }
            _searchStatus.Text = $"{_searchPageCursor + 1} / {_searchResultPages.Count}";
            _searchStatus.ToolTip = $"{_searchTotalHits} match{(_searchTotalHits != 1 ? "es" : "")} on {_searchResultPages.Count} page{(_searchResultPages.Count != 1 ? "s" : "")}";
        }

        private void SearchNextResult()
        {
            if (_searchResultPages.Count == 0) return;
            _searchPageCursor = (_searchPageCursor + 1) % _searchResultPages.Count;
            UpdateSearchStatus();
            int targetPage = _searchResultPages[_searchPageCursor];
            if (PageList.SelectedIndex != targetPage)
                PageList.SelectedIndex = targetPage;
            else
                HighlightSearchResultsOnCurrentPage();
        }

        private void SearchPrevResult()
        {
            if (_searchResultPages.Count == 0) return;
            _searchPageCursor = (_searchPageCursor - 1 + _searchResultPages.Count) % _searchResultPages.Count;
            UpdateSearchStatus();
            int targetPage = _searchResultPages[_searchPageCursor];
            if (PageList.SelectedIndex != targetPage)
                PageList.SelectedIndex = targetPage;
            else
                HighlightSearchResultsOnCurrentPage();
        }

        private void AddSearchHighlight(double left, double bottom, double right, double top,
            double sx, double sy, double renderH)
        {
            double cx = left  * sx;
            double cy = renderH - (top * sy);
            double cw = (right - left) * sx;
            double ch = (top - bottom) * sy;
            var rect = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(80, 255, 165, 0)),
                Stroke = new SolidColorBrush(Color.FromArgb(160, 255, 165, 0)),
                StrokeThickness = 1,
                Width = Math.Max(cw, 4),
                Height = Math.Max(ch, 4),
                IsHitTestVisible = false,
                Tag = "SearchHighlight"
            };
            Canvas.SetLeft(rect, cx);
            Canvas.SetTop(rect, cy);
            _annotationCanvas.Children.Add(rect);
        }

        private void ClearSearchHighlights()
        {
            var toRemove = _annotationCanvas.Children.OfType<Rectangle>()
                .Where(r => r.Tag is string s && s == "SearchHighlight").ToList();
            foreach (var r in toRemove)
                _annotationCanvas.Children.Remove(r);
            if (_searchStatus is not null)
                _searchStatus.Text = "";
        }
    }
}
