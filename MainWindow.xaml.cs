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
    public partial class MainWindow : Window
    {
        private PdfDocument? _doc;
        private string? _currentFile;
        private string? _originalFile;  // user's real file path; survives temp swaps from crop/rotate, used by Save
        private Point _dragStartPoint;

        // Zoom
        private double _zoomLevel = 1.0;
        private double _lastRenderZoom = 1.0;
        private int _renderedPrimaryPage = -1;   // primary (spread-left) page currently rasterised
        private const double ZoomMin = 0.05;
        private const double ZoomMax = 5.0;
        private const double ZoomStep = 0.15;
        private enum FitMode { None, Width, Page }
        private FitMode _fitMode = FitMode.None;
        private System.Windows.Threading.DispatcherTimer? _rerenderTimer;
        private System.Threading.CancellationTokenSource? _secondaryRenderCts;
        private enum ViewMode { Single, Continuous, TwoPage, Grid }
        private ViewMode _viewMode = ViewMode.Continuous;
        private readonly StackPanel _continuousPanel = null!;
        private System.Threading.CancellationTokenSource? _continuousRenderCts;
        private readonly List<double> _continuousTops = [];
        private int _gridScrollToPage = -1;   // page to scroll to once its grid tile streams in (-1 = none)
        private int _continuousScrollTarget = -1;  // re-scroll here once its true height is known
        private double _continuousPageW;

        // Editing
        private EditTool _currentTool = EditTool.Select;
        // Per-document state. Not readonly: tab switching swaps these by reference so each
        // open document keeps its own annotations, undo history, form values, and search hits.
        private Dictionary<int, List<PageAnnotation>> _annotations = [];
        private Dictionary<int, (int w, int h)> _renderDims = [];
        // Stores the PDF /Rotate value for each page.  The temp file used by Docnet has
        // rotation stripped to zero so FPDF_GetPageWidth/Height returns MediaBox dims and
        // the content isn't clipped; RotateBitmap is applied at render time instead.
        private Dictionary<int, int> _pageRotations = [];

        // Form filling - text/check keyed by widget object number; radio keyed by field name
        private Dictionary<int, string>    _formTextValues  = [];
        private Dictionary<int, bool>      _formCheckValues = [];
        private Dictionary<string, string> _formRadioValues = [];
        private Dictionary<int, double>    _formFontSizes   = [];   // per-field user font-size override (points)
        // Floating font-size stepper shown while a form text field is focused.
        private Border?  _formSizeBar;
        private TextBox? _activeFormTb;
        private int      _activeFormObj;
        private double   _activeFormScale = 1;
        private const string FormOverlayTag = "FormFieldOverlay";

        // Undo stack - each entry is either an annotation removal or a full document snapshot.
        // AnnotationGroup removes a specific set of annotations in one step (a text edit = cover + text).
        private enum UndoKind { Annotation, Document, StampBatch, ClearAnnotations, AnnotationGroup, PageSnapshot }
        private readonly record struct UndoEntry(UndoKind Kind, int PageIdx = -1, byte[]? DocBytes = null, bool WasDirty = false, int[]? Pages = null, PageAnnotation? Annot = null, Dictionary<int, List<PageAnnotation>>? AnnotSnapshot = null, List<PageAnnotation>? AnnotGroup = null);
        private Stack<UndoEntry> _undoStack = new();
        private bool _isDrawing;
        private Point _drawStart;
        private UIElement? _activePreview;
        private InkAnnotation? _activeInk;
        private TextBox? _activeTextBox;
        private PageAnnotation? _selectedAnnotation;
        private Border? _selectionBorder;
        // Shift+click multi-selection (Select tool): extra annotations selected alongside the
        // primary _selectedAnnotation. Each gets its own outline. Delete removes the whole set.
        private readonly List<PageAnnotation> _selectedSet = [];
        private readonly List<Border> _selectionOutlines = [];

        // Draw/Highlight settings
        private Color _drawColor = Colors.Red;
        private double _drawWidth = 3;
        private byte _drawOpacity = 255;
        private bool _lineLevel = true;   // Line tool: keep the line perfectly horizontal (default on)
        private bool _highlightErase;     // Highlight tool: drag a box to delete annotations inside it
        private bool _drawErase;          // Draw tool: brush over annotations to delete them
        private Color _highlightColor = Color.FromArgb(80, 255, 255, 0);
        // Strikethrough / underline lines: opaque red by default.
        private Color _lineAnnotColor = Color.FromArgb(255, 220, 38, 38);
        private Border? _drawSettingsBar;

        // Text (typewriter) tool settings
        private double _textFontSize = 24;
        // Current text-tool typeface and style (mirrors the text bar; carried onto each new/edited box).
        private string _textFontName = "Segoe UI";
        private bool _textBold;
        private bool _textItalic;
        private bool _textStrike;
        private bool _textUnderline;
        // Installed font-family names, sorted, computed once (the text bar rebuilds often).
        private static List<string>? _systemFontNamesCache;
        private static List<string> SystemFontNames => _systemFontNamesCache ??=
            [.. System.Windows.Media.Fonts.SystemFontFamilies
                .Select(f => f.Source).Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct().OrderBy(s => s, StringComparer.OrdinalIgnoreCase)];
        // WPF renders a given point size visually ~25% larger than the source PDF text, so scale the
        // detected size down when seeding an existing-text edit. The user can still fine-tune after.
        private const double EditTextSizeCorrection = 0.8;
        private bool _suppressSizeSync;   // guards the slider<->size-box two-way binding from feedback loops
        private TextAnnotation? _reeditOriginal;  // placed-text annotation currently being re-edited
        // The opaque cover dropped when starting an existing-text edit, awaiting its paired text commit.
        // Held so the text commit can group both into one undo, and so cancel/empty removes the cover.
        private CoverAnnotation? _pendingCover;
        // Dirty state captured before the cover was dropped, so undoing the grouped edit restores it.
        private bool _pendingEditWasDirty;
        private Color _textColor = Colors.Black;
        private byte _textOpacity = 255;          // alpha applied to placed text, like the draw tool
        private Color _textFillColor = Color.FromArgb(0, 255, 255, 255);  // text box background fill; A==0 = no fill
        private const double TextBoxDefaultWidth = 220;  // canvas-unit width of a freshly placed text box
        private Border? _textSettingsBar;

        // Signature / image resize
        private bool _isResizingSig;
        private Point _resizeSigStart;
        private double _resizeSigStartScale;
        private PlacedAnnotation? _resizeSigAnnot;
        private TextAnnotation? _resizeTextAnnot;               // text box being width-resized (height auto-fits)
        private HighlightAnnotation? _resizeHlAnnot;            // highlight/line being corner-resized (Bounds)
        private InkAnnotation? _resizeInkAnnot;                 // ink stroke being corner-resized (points scaled)
        private List<Point>? _resizeInkOrigPoints;             // snapshot of ink points at resize start
        private Rect _resizeInkOrigBounds;                      // ink bounding box at resize start
        private readonly List<Rectangle> _resizeHandles = [];   // 4 corner handles for placed annotations
        private string _resizeCorner = "SE";                    // which corner is being dragged
        private Point _resizeAnchor;                            // opposite corner, held fixed during resize

        // Mid-edit resize handles: 4 corners shown around the live editing TextBox so the user can
        // resize the box (and continue typing) without committing and re-selecting first.
        private readonly List<Rectangle> _textEditHandles = [];
        private bool _draggingTextEditHandle;
        private string _tehCorner = "SE";
        private Point _tehAnchor;
        private TextBox? _tehBox;

        // Placed annotation drag-to-move
        private bool _isDraggingAnnot;
        private Point _dragAnnotStart;

        // Middle-mouse / spacebar pan
        private bool _isPanning;
        private bool _spaceHeld;
        private Point _panStart;
        private double _panScrollH;
        private double _panScrollV;
        private Point _dragAnnotOrigPos;
        private PageAnnotation? _dragAnnot;   // placed image/signature OR typewriter text

        // Crop tool
        private Rect _cropCanvasRect;
        private Rectangle? _cropPreviewRect;
        private Rectangle? _cropPreviewRectBorder;  // unused after refactor; kept to avoid null-ref in cleanup
        private readonly List<System.Windows.Shapes.Path> _cropBrackets = []; // L-bracket corner visuals
        private Border? _cropConfirmBar;
        private readonly Button _toolCropBtn = null!;
        private readonly List<Rectangle> _cropHandles = [];
        private string? _activeCropHandleTag; // "NW" | "NE" | "SE" | "SW"
        private Point _cropHandleDragStart;
        private Rect _cropRectAtHandleDrag;
        private int _cropPageIndex = -1;   // page the crop rect was drawn on (grid/two-page aware)
        private TextBox? _cropX1Box, _cropY1Box, _cropX2Box, _cropY2Box;
        private TextBox? _cropRangeBox;
        private bool     _updatingCropInputs;
        private bool     _cropBarDragging;
        private Point    _cropBarDragOffset;

        // PDF link overlays (rendered on top of the annotation canvas)
        private readonly List<Canvas> _linkOverlays = [];

        // Sidebar + multi-page view
        private bool _sidebarCollapsed;
        private bool _sidebarRight;   // false = sidebar on the left (default), true = on the right
        private bool   _sidebarShowingOutlines;
        private bool   _outlinesFitted     = false;
        private double _savedPagesWidth    = 180;
        private double _savedOutlinesWidth = 300;
        private readonly Button _sidebarToggleBtn = null!;
        private readonly Border _sidebarBorder = null!;
        private ColumnDefinition _sidebarCol = null!;   // sized column (left or right per _sidebarRight)
        private readonly WrapPanel _pageContentPanel = null!;

        // Text selection
        private bool _isSelecting;
        private Point _selectStart;
        private Rectangle? _selectRect;
        private Rectangle? _pairedCoverOutline;   // dashed hint over a cover while its paired text is selected
        private Rectangle? _reeditCoverOutline;   // dashed hint over a cover while its paired text is being re-edited
        private string? _selectedText;

        // Search
        private Border? _searchBar;
        private TextBox? _searchBox;
        private TextBlock? _searchStatus;
        private readonly List<Rect> _searchHighlights = [];

        // Signatures
        private readonly SignatureStore _signatureStore = new();
        private SavedSignature? _pendingSignature;
        private Border? _signaturePopup;
        // Guided AcroForm signing: "pick once, reuse" - the chosen signature/initials are remembered
        // and dropped into every matching field. _pendingSignField, when set, routes the next pick from
        // the popup into that field instead of free placement.
        private SavedSignature? _activeSignatureChoice;
        private SavedSignature? _activeInitialsChoice;
        private (bool Initials, int ObjNum, int Page, double X, double Y, double W, double H)? _pendingSignField;
        // Form fields already signed, so re-clicking one offers change/remove instead of re-stamping.
        private readonly Dictionary<int, SignatureAnnotation> _signedFields = [];

        // Manual element refs (XAML codegen doesn't resolve these)
        private readonly Canvas _annotationCanvas = null!;
        // Active annotation surface. Single view: always _annotationCanvas. Continuous view:
        // set on mouse-down to the clicked page's overlay. Shared handlers target this.
        private Canvas _activeCanvas = null!;
        // The page surface a pointer gesture started on, captured on mouse-down. Kept separate
        // from _activeCanvas because RenderAllAnnotations reuses _activeCanvas as its render
        // target; in Grid view tiles stream in asynchronously and each one re-points _activeCanvas
        // mid-gesture, which previously committed annotations to the wrong page and broke
        // select/delete. Mouse-move/up resolve the gesture page and surface from these instead.
        private Canvas? _gestureCanvas;
        private int _gesturePage = -1;
        // Per-page overlay canvases for Continuous view, keyed by page index.
        private readonly Dictionary<int, Canvas> _continuousCanvases = [];
        private readonly Grid _pageContentGrid = null!;
        private readonly Button _toolSelectBtn = null!;
        private readonly Button _toolTextBtn = null!;
        private readonly Button _toolHighlightBtn = null!;
        private readonly Button _toolUnderlineBtn = null!;
        private readonly Button _toolDrawBtn = null!;
        private readonly Button _toolSignatureBtn = null!;
        private readonly Button _toolImageBtn = null!;
        private readonly Button _saveAsBtnRef = null!;
        private readonly Button _closeFileBtnRef = null!;
        private readonly ComboBox _zoomBox = null!;
        private readonly StackPanel _portableBadge = null!;
        private readonly TextBox _pageJumpBox = null!;
        private readonly TextBlock _pageTotalLabel = null!;

        // Dirty / unsaved-change tracking
        private bool _isDirty = false;

        // Whole-document search results (PDF-space rects per page)
        private Dictionary<int, List<(double left, double bottom, double right, double top)>> _allSearchRects = [];
        private List<int> _searchResultPages = [];
        private int _searchPageCursor = -1;

        public MainWindow()
        {
            InitializeComponent();
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (v != null) VersionLabel.Text = $"v{v.Major}.{v.Minor}.{v.Build}";
            _annotationCanvas = (Canvas)FindName("AnnotationCanvas")!;
            _activeCanvas = _annotationCanvas;
            // Safety net: if the window loses focus mid-drag/resize (e.g. Alt-Tab away to type elsewhere),
            // the mouse-up can be lost and the dragged annotation would stay glued to the cursor with the
            // canvas still holding mouse capture. End any in-progress gesture on deactivate so control is
            // restored the moment the user comes back.
            Deactivated += (_, _) => { if (_isDraggingAnnot || _isResizingSig) FinishStuckGesture(); };
            _pageContentGrid = (Grid)FindName("PageContentGrid")!;
            _toolSelectBtn = (Button)FindName("ToolSelectBtn")!;
            _toolTextBtn = (Button)FindName("ToolTextBtn")!;
            _toolHighlightBtn = (Button)FindName("ToolHighlightBtn")!;
            _toolUnderlineBtn = (Button)FindName("ToolUnderlineBtn")!;
            _toolDrawBtn = (Button)FindName("ToolDrawBtn")!;
            _toolSignatureBtn = (Button)FindName("ToolSignatureBtn")!;
            _toolImageBtn = (Button)FindName("ToolImageBtn")!;
            _toolCropBtn = (Button)FindName("ToolCropBtn")!;
            _sidebarToggleBtn = (Button)FindName("SidebarToggleBtn")!;
            _sidebarBorder = (Border)FindName("SidebarBorder")!;
            _sidebarCol = (ColumnDefinition)FindName("SidebarCol")!;
            _pageContentPanel = (WrapPanel)FindName("PageContentPanel")!;
            _saveAsBtnRef = (Button)FindName("SaveAsBtn")!;
            _closeFileBtnRef = (Button)FindName("CloseFileBtn")!;
            _zoomBox = (ComboBox)FindName("ZoomBox")!;
            // Read-only editable combo: hide its text-selection highlight so the displayed % never
            // looks like selected text after a pick.
            _zoomBox.Loaded += (_, _) =>
            {
                if (_zoomBox.Template?.FindName("PART_EditableTextBox", _zoomBox) is TextBox etb)
                    etb.SelectionBrush = System.Windows.Media.Brushes.Transparent;
            };
            _portableBadge = (StackPanel)FindName("PortableBadge")!;
            _pageJumpBox = (TextBox)FindName("PageJumpBox")!;
            _pageTotalLabel = (TextBlock)FindName("PageTotalLabel")!;
            _continuousPanel = (StackPanel)FindName("ContinuousPanel")!;
            PagePreviewPanel.ScrollChanged += PagePreviewPanel_ScrollChanged;
            PreviewMouseDown += SettingsDismiss_PreviewMouseDown;   // non-modal Settings: close on outside click
            // Keep the (bottom-anchored) Settings panel sized to the document area while the window
            // resizes, so it tracks smoothly instead of snapping on the next open.
            MainContentGrid.SizeChanged += (_, _) =>
            {
                if (SettingsOverlay.Visibility == Visibility.Visible) PositionSettingsPanel();
                ScheduleFadeRefresh();
            };
            // The sidebar column resizes via the splitter / collapse; track its width so the tab-strip
            // shadow gradient stays clipped to the document column.
            if (FindName("SidebarOuterGrid") is FrameworkElement sidebarOuter)
                sidebarOuter.SizeChanged += (_, _) => ScheduleFadeRefresh();
            // The footer shadow tracks the document pane's actual position; re-anchor when it (or the
            // tab strip, which shifts the document) changes size.
            DocPaneBorder.SizeChanged += (_, _) => ScheduleFadeRefresh();
            TabStripBorder.SizeChanged += (_, _) => { ScheduleFadeRefresh(); ScheduleTabReflow(); };
            // After a sidebar-splitter drag, snap fully closed if dragged too narrow, else save the width.
            SidebarSplitter.PreviewMouseLeftButtonUp += (_, _) => OnSidebarResized();
            // Grabbing the splitter while collapsed reveals the page list so it can be pulled open.
            SidebarSplitter.PreviewMouseLeftButtonDown += (_, _) => { _sidebarWantClose = false; OnSidebarSplitterPress(); };
            // Pull the splitter well past the minimum mid-drag to close (the column itself can't clip).
            SidebarSplitter.PreviewMouseMove += OnSidebarSplitterMove;
            // If a drag is interrupted (alt-tab, focus loss, taking a screenshot), finalize it so the
            // sidebar can't get stuck half-open with its content hidden.
            SidebarSplitter.LostMouseCapture += (_, _) => OnSidebarResized();
            if (Enum.TryParse<ViewMode>(App.GetSetting("ViewMode"), out var savedVm))
                _viewMode = savedVm;
            if (Enum.TryParse<ToolbarStyle>(App.GetSetting("ToolbarStyle"), out var savedTb))
                _toolbarStyle = savedTb;
            if (string.Equals(App.GetSetting("SidebarSide"), "Right", StringComparison.OrdinalIgnoreCase))
                _sidebarRight = true;
            IndexToolbarButtons();
            OutlineTree.SelectedItemChanged += OutlineTree_SelectedItemChanged;
            LoadSignatures();
            BuildContextMenu();
            SetTool(EditTool.Select);
            ApplyGrainTexture();
            ApplyToolNumberTooltips();   // append the 1-9 toolbar positions to the tool tooltips
            SourceInitialized += MainWindow_SourceInitialized;
            Closed += (_, _) => { _continuousRenderCts?.Cancel(); _doc?.Close(); App.CleanupSessionTemps(); };

            // Open a file passed via command-line / file association (e.g. double-clicking a .pdf)
            // Also show the portable badge when running outside the install location.
            bool contentRevealed = false;
            ContentRendered += (_, _) =>
            {
                Services.ThemeManager.RefreshIcons();
                // Final pass once the layout has real widths. The tab-strip / footer shadow gradients
                // were intermittently blank at startup (their feather mask + margin were computed
                // before the sidebar column had measured), and only a manual sidebar tweak forced a
                // correct re-layout. Re-running it here reproduces that fix automatically.
                UpdateTabStripFade();
                // The content is held invisible (RootClipGrid.Opacity=0 in XAML) until this final
                // positioning pass has run; fade it in once so the brief unpositioned first frame
                // (the "load deform" - shadows/toolbars snapping into place) is never visible.
                if (!contentRevealed)
                {
                    contentRevealed = true;
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, (Action)(() =>
                    {
                        var reveal = new System.Windows.Media.Animation.DoubleAnimation(0, 1,
                            new Duration(TimeSpan.FromMilliseconds(140)));
                        RootClipGrid.BeginAnimation(OpacityProperty, reveal);
                    }));
                }
            };
            Services.ThemeManager.ThemeChanged += OnThemeChanged;

            Loaded += (_, _) =>
            {
                RestoreWindowSettings();
                ApplySidebarSide();   // place the sidebar on the saved side (default left)
                if (_toolbarStyle != ToolbarStyle.SmallIcons) ApplyToolbarAppearance();

                var args = Environment.GetCommandLineArgs();
                if (args.Length > 1 && System.IO.File.Exists(args[1]))
                {
                    OpenInNewTab(args[1]);
                }
                else
                {
                    // Reopen every tab from the last session (falls back to the single LastFile for
                    // settings written before multi-tab restore existed).
                    var saved = App.GetSetting("OpenTabs");
                    string[] paths = !string.IsNullOrEmpty(saved)
                        ? saved!.Split('|')
                        : (App.GetSetting("LastFile") is { Length: > 0 } lf ? [lf] : []);
                    // Lazy restore: create a placeholder tab for each saved file but load only the
                    // focused one. The rest materialize (load + render) the first time they're clicked,
                    // so startup cost no longer scales with how many tabs were open last session.
                    _sessions.Clear();
                    bool openedAny = false;
                    foreach (var f in paths)
                        if (!string.IsNullOrEmpty(f) && System.IO.File.Exists(f))
                        {
                            _sessions.Add(new DocumentSession { OriginalFile = f, CurrentFile = f, DeferredPath = f });
                            openedAny = true;
                        }
                    if (!openedAny)
                    {
                        _active = null;
                        PopulateRecentFilesList();   // empty state: show the recent list
                        EnsureInitialSession();
                        RebuildTabStrip();
                    }
                    else
                    {
                        var wantActive = App.GetSetting("ActiveTab");
                        var activeTarget = (!string.IsNullOrEmpty(wantActive)
                                ? _sessions.FirstOrDefault(ss => string.Equals(ss.OriginalFile, wantActive, StringComparison.OrdinalIgnoreCase))
                                : null)
                            ?? _sessions[0];
                        _active = activeTarget;
                        ApplySessionState(activeTarget);
                        MaterializeDeferred(activeTarget);   // load + render only the focused tab
                        RebuildTabStrip();
                    }
                }

                if (App.IsPortable())
                    _portableBadge.Visibility = Visibility.Visible;
            };
        }

        // ============================================================
        // Maximize-respects-taskbar fix (WindowStyle=None needs WM_GETMINMAXINFO)
        // ============================================================

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
            ThemeManager.ApplyDwm(hwnd);
            // Snapping moves the window without changing WindowState, so re-evaluate the rounded vs
            // squared chrome on every move (and once now that the handle exists).
            LocationChanged += OnWindowLocationChanged;
            UpdateWindowChrome();
        }

        // ============================================================
        // Settings persistence (window size, zoom, last file)
        // ============================================================

        private void SaveWindowSettings()
        {
            try
            {
                App.SetSetting("WindowState", WindowState.ToString());
                if (WindowState == WindowState.Normal)
                {
                    App.SetSetting("WindowWidth",  ((int)ActualWidth).ToString());
                    App.SetSetting("WindowHeight", ((int)ActualHeight).ToString());
                    App.SetSetting("WindowTop",  ((int)Top).ToString());
                    App.SetSetting("WindowLeft", ((int)Left).ToString());
                }
                App.SetSetting("FitMode",   _fitMode.ToString());
                App.SetSetting("ZoomLevel", _zoomLevel.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (_currentFile is not null)
                    App.SetSetting("LastFile", _currentFile);
                else
                    App.RemoveSetting("LastFile");
                // Remember every open tab so the whole session restores next launch. Manually-closed
                // tabs are already gone from _sessions, so they won't come back (Issue #75 still holds).
                var openFiles = _sessions
                    .Select(ss => ss.OriginalFile)
                    .Where(f => !string.IsNullOrEmpty(f) && System.IO.File.Exists(f))
                    .Distinct()
                    .ToList();
                if (openFiles.Count > 0)
                    App.SetSetting("OpenTabs", string.Join("|", openFiles!));
                else
                    App.RemoveSetting("OpenTabs");
                if (_active?.OriginalFile is { Length: > 0 } af && System.IO.File.Exists(af))
                    App.SetSetting("ActiveTab", af);
                else
                    App.RemoveSetting("ActiveTab");
            }
            catch { /* best-effort */ }
        }

        private void RestoreWindowSettings()
        {
            try
            {
                if (int.TryParse(App.GetSetting("WindowWidth"),  out int w) &&
                    int.TryParse(App.GetSetting("WindowHeight"), out int h) && w > 200 && h > 200)
                {
                    Width  = w;
                    Height = h;
                }
                if (int.TryParse(App.GetSetting("WindowTop"),  out int savedTop) &&
                    int.TryParse(App.GetSetting("WindowLeft"), out int savedLeft))
                {
                    // Verify the saved position is visible on the virtual desktop
                    // (covers all monitors). Falls back to CenterScreen (XAML default)
                    // if the monitor it was on is no longer connected.
                    double vLeft   = SystemParameters.VirtualScreenLeft;
                    double vTop    = SystemParameters.VirtualScreenTop;
                    double vRight  = vLeft + SystemParameters.VirtualScreenWidth;
                    double vBottom = vTop  + SystemParameters.VirtualScreenHeight;
                    bool onScreen  = savedLeft + 100 < vRight  && savedLeft + Width  > vLeft
                                  && savedTop  + 50  < vBottom && savedTop  + Height > vTop;
                    if (onScreen)
                    {
                        Left = savedLeft;
                        Top  = savedTop;
                    }
                }
                if (Enum.TryParse<WindowState>(App.GetSetting("WindowState"), out var ws) &&
                    ws == WindowState.Maximized)
                {
                    WindowState = WindowState.Maximized;
                }
                if (double.TryParse(App.GetSetting("ZoomLevel"),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double z) && z > 0)
                {
                    _zoomLevel = Math.Max(ZoomMin, Math.Min(ZoomMax, z));
                }
                if (Enum.TryParse<FitMode>(App.GetSetting("FitMode"), out var fm))
                    _fitMode = fm;
            }
            catch { /* best-effort */ }
        }

        // ============================================================
        // Core helpers (localization, status, PDF object refs)
        // ============================================================

        /// <summary>Look up a localized string. Falls back to the key name if missing.</summary>
        private string Loc(string key)
            => Application.Current.TryFindResource(key) as string ?? key;

        private void SetStatus(string text)
        {
            StatusText.Text = text;
            CrashReporter.PushStatusMessage(text);
        }

        /// <summary>
        /// Dereferences a PdfItem if it is an indirect reference (PdfReference is internal;
        /// we detect it by looking for a public "Value" property returning PdfObject).
        /// </summary>
        private static PdfItem DerefItem(PdfItem item)
        {
            var valueProp = item.GetType().GetProperty("Value",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (valueProp?.GetValue(item) is PdfObject resolved)
                return resolved;
            return item;
        }

        /// <summary>
        /// Returns the PDF object number of a PdfItem that is an indirect reference, or -1.
        /// Handles the internal PdfReference type via reflection.
        /// </summary>
        private static int GetObjectNumber(PdfItem? item)
        {
            if (item is null) return -1;
            var prop = item.GetType().GetProperty("ObjectNumber",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return prop?.GetValue(item) is int n ? n : -1;
        }

        // ============================================================
        // Selection
        // ============================================================

        // Resolve the active theme's "SelectionAccent" color: a per-theme color picked to stay
        // readable on the white PDF page (Accent is white in several themes, and AccentBorder is a
        // pale cream that washes out on white). Falls back to brand green.
        private Color AccentColor()
            => TryFindResource("SelectionAccent") is SolidColorBrush b ? b.Color : Color.FromRgb(30, 165, 76);
        private SolidColorBrush AccentBrush(byte alpha = 255)
        {
            var c = AccentColor();
            return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }
        // A darker shade of the accent, used for a cover's selection chrome and its in-edit outline so a
        // cover reads as distinct from the lighter accent on the text box stacked over it.
        private SolidColorBrush DarkerAccentBrush(byte alpha = 255)
        {
            var c = AccentColor();
            return new SolidColorBrush(Color.FromArgb(alpha, (byte)(c.R * 0.6), (byte)(c.G * 0.6), (byte)(c.B * 0.6)));
        }

        private void SelectAllText()
        {
            if (_currentFile is null) return;
            int pageIdx = PageList.SelectedIndex;
            if (pageIdx < 0) return;

            try
            {
                using var pigDoc = PdfPigDoc.Open(_currentFile);
                if (pageIdx >= pigDoc.NumberOfPages) return;
                var page = pigDoc.GetPage(pageIdx + 1);
                _selectedText = WordsToText(page.GetWords());
                if (string.IsNullOrWhiteSpace(_selectedText))
                {
                    SetStatus("No text found on this page");
                    return;
                }
                Clipboard.SetText(_selectedText);
                // Visual feedback: highlight entire canvas
                ClearTextSelection();
                _selectRect = new Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromArgb(30, 74, 130, 255)),
                    Stroke = new SolidColorBrush(Color.FromArgb(80, 74, 130, 255)),
                    StrokeThickness = 1,
                    Width = _annotationCanvas.Width,
                    Height = _annotationCanvas.Height,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(_selectRect, 0);
                Canvas.SetTop(_selectRect, 0);
                _annotationCanvas.Children.Add(_selectRect);
                SetStatus($"Selected all text - copied to clipboard");
            }
            catch (Exception ex)
            {
                SetStatus($"Select all error: {ex.Message}");
            }
        }

        private void CopySelectedText()
        {
            if (!string.IsNullOrEmpty(_selectedText))
            {
                Clipboard.SetText(_selectedText);
                SetStatus($"Copied to clipboard");
            }
            else
            {
                SetStatus("No text selected - drag to select text");
            }
        }

        private void ClearTextSelection()
        {
            if (_selectRect is not null)
            {
                _activeCanvas.Children.Remove(_selectRect);
                _selectRect = null;
            }
            _selectedText = null;
        }

        private void ExtractTextFromRegion(int pageIdx, Rect canvasBounds)
        {
            if (_currentFile is null || pageIdx < 0) return;
            if (!_renderDims.ContainsKey(pageIdx)) return;

            try
            {
                var (renderW, renderH) = _renderDims[pageIdx];

                using var pigDoc = PdfPigDoc.Open(_currentFile);
                if (pageIdx >= pigDoc.NumberOfPages) return;
                var page = pigDoc.GetPage(pageIdx + 1); // PdfPig is 1-based

                double pdfW = page.Width;
                double pdfH = page.Height;
                double sx = pdfW / renderW;
                double sy = pdfH / renderH;

                // Convert canvas rect to PDF coordinates (flip Y - PDF origin is bottom-left)
                double pdfLeft = canvasBounds.Left * sx;
                double pdfRight = canvasBounds.Right * sx;
                double pdfTop = pdfH - (canvasBounds.Top * sy);
                double pdfBottom = pdfH - (canvasBounds.Bottom * sy);
                // pdfTop > pdfBottom because of Y flip
                double pdfMinY = Math.Min(pdfTop, pdfBottom);
                double pdfMaxY = Math.Max(pdfTop, pdfBottom);

                var words = page.GetWords()
                    .Where(w =>
                    {
                        var bb = w.BoundingBox;
                        double cx = (bb.Left + bb.Right) / 2;
                        double cy = (bb.Bottom + bb.Top) / 2;
                        return cx >= pdfLeft && cx <= pdfRight && cy >= pdfMinY && cy <= pdfMaxY;
                    })
                    .ToList();

                if (words.Count == 0)
                {
                    SetStatus("No text found in selection");
                    ClearTextSelection();
                    return;
                }

                _selectedText = WordsToText(words);

                Clipboard.SetText(_selectedText);
                int wordCount = words.Count;
                SetStatus($"Copied {wordCount} word(s) to clipboard");
            }
            catch (Exception ex)
            {
                SetStatus($"Text extraction error: {ex.Message}");
                ClearTextSelection();
            }
        }

        // ============================================================
        // Search (Ctrl+F)
        // ============================================================

        /// <summary>
        /// Converts a collection of PdfPig words to a properly ordered string.
        /// Sorts top-to-bottom then left-to-right, groups into lines using a
        /// dynamic threshold (~40% of average word height) so words at slightly
        /// different baselines still land on the correct line.
        /// </summary>
        private static string WordsToText(IEnumerable<UglyToad.PdfPig.Content.Word> source)
        {
            var words = source
                .OrderByDescending(w => w.BoundingBox.Top)
                .ThenBy(w => w.BoundingBox.Left)
                .ToList();
            if (words.Count == 0) return string.Empty;

            // Dynamic threshold: 40% of average word height, minimum 4 PDF units
            double avgH   = words.Average(w => w.BoundingBox.Height);
            double thresh = Math.Max(4.0, avgH * 0.4);

            var lines = new List<List<UglyToad.PdfPig.Content.Word>>();
            double lineY = double.MaxValue;
            foreach (var w in words)
            {
                if (Math.Abs(w.BoundingBox.Top - lineY) > thresh)
                {
                    lines.Add([]);
                    lineY = w.BoundingBox.Top;
                }
                lines[^1].Add(w);
            }

            // Re-sort each line by X in case the top-Y sort caused any grouping
            // to pull words into the wrong order within a line.
            return string.Join("\n", lines.Select(l =>
                string.Join(" ", l.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text))));
        }

        // ============================================================
        // Annotation management
        // ============================================================

        // Stamps a page number onto every page as a text annotation (so it renders, saves, and
        // flattens like any other annotation). One undo removes the whole batch.
        private void StampPageNumbers()
        {
            if (_doc is null) { SetStatus("Open a document first"); return; }

            var dlg = new StampNumbersDialog(this);
            if (dlg.ShowDialog() != true) return;

            int start    = dlg.StartNumber;
            string fmt   = dlg.Format;
            double ptSize = dlg.FontSizePt;
            int posH     = dlg.PosH;   // 0 left, 1 center, 2 right
            int posV     = dlg.PosV;   // 0 top, 2 bottom
            int n        = _doc.PageCount;
            bool wasDirty = _isDirty;
            double ppd   = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            var stamped = new List<int>();
            for (int i = 0; i < n; i++)
            {
                double pw = _doc.Pages[i].Width.Point;
                double phpt = _doc.Pages[i].Height.Point;
                if (_pageRotations.TryGetValue(i, out int rot) && (rot == 90 || rot == 270))
                    (pw, phpt) = (phpt, pw);
                double maxDim = Math.Max(1, Math.Max(pw, phpt));
                double rdW = 2048.0 * pw / maxDim;
                double rdH = 2048.0 * phpt / maxDim;

                // Point size -> render-dim units (matches PlaceTextBox so it exports as real points).
                double fontCanvas = ptSize * rdH / Math.Max(1, phpt);

                string text = fmt.Replace("{n}", (start + i).ToString())
                                 .Replace("{N}", n.ToString());
                if (string.IsNullOrWhiteSpace(text)) continue;

                var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), fontCanvas, Brushes.Black, ppd);
                double tw = ft.WidthIncludingTrailingWhitespace, th = ft.Height;
                double mx = rdW * 0.05, my = rdH * 0.04;
                double x = posH == 0 ? mx : posH == 2 ? rdW - tw - mx : (rdW - tw) / 2;
                double y = posV == 0 ? my : rdH - th - my;

                var ta = new TextAnnotation
                {
                    PageIndex = i,
                    Position  = new Point(x, y),
                    Content   = text,
                    FontSize  = fontCanvas
                };
                ta.SetColor(Colors.Black);
                if (!_annotations.TryGetValue(i, out var list)) { list = []; _annotations[i] = list; }
                list.Add(ta);
                stamped.Add(i);
            }

            if (stamped.Count == 0) { SetStatus("Nothing to stamp"); return; }

            _undoStack.Push(new UndoEntry(UndoKind.StampBatch, Pages: [.. stamped], WasDirty: wasDirty));
            MarkDirty();

            if (_viewMode == ViewMode.Continuous)
            {
                foreach (int p in stamped)
                    if (_continuousCanvases.ContainsKey(p)) RenderAllAnnotations(p);
            }
            else
            {
                int cur = PageList.SelectedIndex < 0 ? 0 : PageList.SelectedIndex;
                RenderAllAnnotations(cur);
            }
            SetStatus($"Stamped page numbers on {stamped.Count} page(s)");
        }

        // ============================================================
        // Dirty / unsaved-change tracking
        // ============================================================

        private void MarkDirty(bool dirty = true)
        {
            _isDirty = dirty;
            if (_saveAsBtnRef != null)
            {
                if (dirty)
                {
                    // Deeper orange = unsaved. The old #FFA500 washed out on the light theme's white
                    // toolbar; this reads on light and dark. A soft dark halo (ShadowDepth 0) outlines
                    // the glyph so it pops on light backgrounds and stays invisible on dark ones.
                    _saveAsBtnRef.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x73, 0x00));
                    _saveAsBtnRef.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Colors.Black, BlurRadius = 5, ShadowDepth = 0, Opacity = 0.55
                    };
                }
                else
                {
                    // Saved / clean: just a normal toolbar icon (no colour). The orange above is the only
                    // signal, reserved for "you have unsaved changes".
                    _saveAsBtnRef.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
                    _saveAsBtnRef.Effect = null;
                }
            }
        }

        // Cryptographic certificate signing (the real digital signature, not the drawn stamp tool).
        private void OpenSignDialog()
        {
            if (_doc is null || string.IsNullOrEmpty(_currentFile))
            {
                KillerDialog.Show(this, "Open a PDF first.");
                return;
            }
            // Sign the user's real document, not the temp working copy. Operations like print/crop/repair
            // repoint _currentFile at a temp (e.g. "...printfixed...") while _originalFile keeps the real
            // path - which is the name the user expects to see and the file Save targets.
            new SignDocumentDialog(this, _originalFile ?? _currentFile!).ShowDialog();
        }

        // ---- generic busy overlay (indeterminate spinner) for blocking background work ----

        /// <summary>
        /// Dims the window and shows a spinning ring plus a message while a background task runs.
        /// Returned Border is passed to HideBusyOverlay when the work completes.
        /// </summary>
        private Border ShowBusyOverlay(string message)
        {
            var spinner = new System.Windows.Shapes.Ellipse
            {
                Width = 34, Height = 34,
                Stroke = AccentBrush(),
                StrokeThickness = 3,
                StrokeDashArray = [5.5, 3.5], // dashed ring reads as "spinning"
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 14),
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            var rot = new RotateTransform();
            spinner.RenderTransform = rot;
            rot.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(0.9)))
                { RepeatBehavior = RepeatBehavior.Forever });

            var text = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI, Nirmala UI"),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            panel.Children.Add(spinner);
            panel.Children.Add(text);

            var overlay = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(190, 0x12, 0x12, 0x12)),
                Child = panel
            };
            Panel.SetZIndex(overlay, 10050); // above the Settings/Shortcuts/About overlays

            // Cover the whole window for a uniform dim, but let the user drag the window by pressing
            // anywhere on the overlay - so a long operation (e.g. repair) doesn't trap the window in place.
            overlay.Cursor = Cursors.SizeAll;
            overlay.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
            };
            if (SettingsOverlay?.Parent is Grid host)
            {
                if (host.RowDefinitions.Count > 0) Grid.SetRowSpan(overlay, host.RowDefinitions.Count);
                host.Children.Add(overlay);
            }
            else
            {
                RootClipGrid?.Children.Add(overlay);
            }
            return overlay;
        }

        private static void HideBusyOverlay(Border overlay)
            => (overlay.Parent as Panel)?.Children.Remove(overlay);

        /// <summary>
        /// Encodes raw BGRA pixel data from pdfium to PNG without touching the UI thread.
        /// GDI+ Format32bppArgb is BGRA in memory - matches pdfium output exactly.
        /// </summary>
        private static byte[] RenderToPng(byte[] bgra, int width, int height)
        {
            var pin = GCHandle.Alloc(bgra, GCHandleType.Pinned);
            try
            {
                using var bmp = new System.Drawing.Bitmap(
                    width, height, width * 4,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb,
                    pin.AddrOfPinnedObject());
                using var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.ToArray();
            }
            finally { pin.Free(); }
        }

        // ============================================================
        // Bitmap rotation helper
        // ============================================================

        /// <summary>
        /// Rotates a raw BGRA (4 bytes/pixel) bitmap clockwise by degrees.
        /// Used because Docnet's FPDF_RenderPageBitmapWithMatrix uses a pure-scaling
        /// matrix, so PDFium renders the page in its MediaBox orientation (no rotation).
        /// We strip /Rotate from the temp file so content is never clipped, then rotate
        /// the pixel buffer here to match the intended visual orientation.
        /// </summary>
        internal static (byte[] bytes, int w, int h) RotateBitmapStatic(byte[] src, int w, int h, int degrees)
            => RotateBitmap(src, w, h, degrees);

        private static (byte[] bytes, int w, int h) RotateBitmap(byte[] src, int w, int h, int degrees)
        {
            degrees = ((degrees % 360) + 360) % 360;
            if (degrees == 0) return (src, w, h);
            int newW = (degrees == 90 || degrees == 270) ? h : w;
            int newH = (degrees == 90 || degrees == 270) ? w : h;
            byte[] dst = new byte[newW * newH * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int srcIdx = (y * w + x) * 4;
                    int dstX, dstY;
                    switch (degrees)
                    {
                        case 90:  dstX = h - 1 - y; dstY = x;         break; // CW
                        case 180: dstX = w - 1 - x; dstY = h - 1 - y; break;
                        default:  dstX = y;          dstY = w - 1 - x; break; // 270 CW
                    }
                    int dstIdx = (dstY * newW + dstX) * 4;
                    dst[dstIdx]     = src[srcIdx];
                    dst[dstIdx + 1] = src[srcIdx + 1];
                    dst[dstIdx + 2] = src[srcIdx + 2];
                    dst[dstIdx + 3] = src[srcIdx + 3];
                }
            }
            return (dst, newW, newH);
        }

        // ============================================================
        // Temp save/reload
        // ============================================================

        private void SaveTempAndReload(bool keepAnnotations = false)
        {
            if (_doc is null || _currentFile is null) return;
            // Overlay annotations are unsaved, still-editable user work. Callers that don't change
            // page identity (crop) pass keepAnnotations:true so annotations on other pages survive
            // the reload and stay selectable/movable; they are re-rendered after the doc reopens.
            if (!keepAnnotations) _annotations.Clear();
            _renderDims.Clear();
            ClearSelection();
            MarkDirty();
            var doc = _doc;
            int selectedIdx = PageList.SelectedIndex;

            // Capture page rotations, then strip them from the document before saving.
            // Docnet uses FPDF_GetPageWidth/Height (MediaBox, no rotation) to size the bitmap,
            // then renders with PDFium's page CTM which *does* include /Rotate.  For 90°/270°
            // the rendered landscape content overflows the portrait-sized bitmap and gets clipped.
            // Stripping /Rotate to 0 before saving means Docnet renders clean unrotated content
            // that fits the bitmap; RotateBitmap is applied in each render path instead.
            _pageRotations.Clear();
            for (int i = 0; i < doc.PageCount; i++)
            {
                int rot = ((doc.Pages[i].Rotate % 360) + 360) % 360;
                _pageRotations[i] = rot;
                doc.Pages[i].Rotate = 0;
            }

            var tempPath = App.MakeTempFile("temp");
            try
            {
                doc.Save(tempPath);
                doc.Close();
            }
            catch (Exception saveEx) when (IsXRefException(saveEx))
            {
                // PdfSharpCore fails to re-save encrypted PDFs (e.g. owner-restricted RC4 files)
                // because it encounters cross-reference tokens while serialising dirty objects.
                // Primary fallback: use PDFium (already initialised for the page preview) to
                // load the source, strip all /Rotate values, remove encryption, and save.
                // Secondary fallback: PdfSharpCore Import mode (works on some non-encrypted xref
                // issues but fails on encrypted files; kept as a last resort).
                doc.Close();
                _doc = null;
                if (!TryPdfiumSaveWithZeroRotations(_currentFile!, tempPath) &&
                    !TryImportRepairToPath(_currentFile!, tempPath, stripRotations: true))
                    throw; // re-throw original if both fallbacks fail
            }
            // PdfSharpCore sometimes saves a file where one object's xref offset points at the
            // xref table itself (object N offset = xref table position). When PdfSharp then tries
            // to re-open that file in Modify mode it seeks to the xref table, reads the keyword
            // "xref" as a token in an object context, and throws "Unexpected token 'xref'".
            // Fix: catch the reopen failure, pipe the saved file through PDFium (which has
            // robust error recovery and will rewrite a correct xref), then retry the open.
            try
            {
                _doc = PdfReader.Open(tempPath, PdfDocumentOpenMode.Modify);
            }
            catch (Exception openEx) when (IsXRefException(openEx))
            {
                var fixedPath = App.MakeTempFile("fixed");
                if (!TryPdfiumSaveWithZeroRotations(tempPath, fixedPath))
                    throw; // PDFium also failed - re-throw original reopen error
                tempPath = fixedPath;
                _doc = PdfReader.Open(tempPath, PdfDocumentOpenMode.Modify);
            }
            _currentFile = tempPath;

            // Restore rotations in the reopened in-memory doc so saves, form fields,
            // and all other operations see the correct rotation values.
            foreach (var kv in _pageRotations)
                _doc.Pages[kv.Key].Rotate = kv.Value;

            RefreshPageList();
            if (selectedIdx >= 0 && selectedIdx < PageList.Items.Count)
                PageList.SelectedIndex = selectedIdx;
            else if (PageList.Items.Count > 0)
                PageList.SelectedIndex = 0;

            // In Continuous view the strip caches one rendered slot per page. After a
            // page-modifying reload (e.g. crop) it must be rebuilt so the main view reflects the
            // new pages; the slot-sizing in RenderContinuousPages makes cropped pages fit cleanly.
            if (_viewMode == ViewMode.Continuous)
            {
                int contIdx = PageList.SelectedIndex;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                    (Action)(() => SetupContinuousView(contIdx)));
                return;
            }

            // Refit synchronously so the first rendered frame uses the correct zoom.
            PagePreviewPanel.ScrollToHorizontalOffset(0);
            ReapplyGridOrFit();

            // Deferred refit after layout settles for accurate ActualWidth.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, (Action)(() =>
            {
                PagePreviewPanel.ScrollToHorizontalOffset(0);
                ReapplyGridOrFit();
            }));
        }

        // ============================================================
        // Drag/drop: file open
        // ============================================================

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                if (files.Length > 0 && files[0].EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    OpenInNewTab(files[0]);
            }
        }

        private void DropZone_Click(object sender, MouseButtonEventArgs e) => Open_Click(sender, e);

        // ============================================================
        // Drag/drop: page reorder
        // ============================================================

        private bool _pageDragArmed;
        private void PageList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            // Only arm a page-reorder drag when the press lands on a page thumbnail, not the
            // scrollbar - otherwise grabbing the scrollbar starts a page-move drag (the "insert"
            // cursor) instead of scrolling.
            _pageDragArmed = false;
            for (var d = e.OriginalSource as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
            {
                if (d is System.Windows.Controls.Primitives.ScrollBar) break;
                if (d is ListBoxItem) { _pageDragArmed = true; break; }
            }
        }

        private void PageList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_pageDragArmed || e.LeftButton != MouseButtonState.Pressed) return;
            var diff = _dragStartPoint - e.GetPosition(null);
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (PageList.SelectedIndex >= 0)
                    DragDrop.DoDragDrop(PageList, PageList.SelectedIndex, DragDropEffects.Move);
            }
        }

        private void PageList_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(int)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void PageList_Drop(object sender, DragEventArgs e)
        {
            if (_doc is null || !e.Data.GetDataPresent(typeof(int))) return;
            var doc = _doc;
            int fromIdx = (int)e.Data.GetData(typeof(int))!;
            var pos = e.GetPosition(PageList);
            int toIdx = PageList.Items.Count - 1;
            for (int i = 0; i < PageList.Items.Count; i++)
            {
                if (PageList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
                {
                    var itemPos = item.TranslatePoint(new Point(0, item.ActualHeight / 2), PageList);
                    if (pos.Y < itemPos.Y) { toIdx = i; break; }
                }
            }
            if (fromIdx == toIdx) return;
            var page = doc.Pages[fromIdx];
            doc.Pages.RemoveAt(fromIdx);
            if (toIdx > fromIdx) toIdx--;
            doc.Pages.Insert(toIdx, page);
            SaveTempAndReload();
            PageList.SelectedIndex = toIdx;
        }
    }
}
