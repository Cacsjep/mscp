using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LowLatencyVideoWall.Model;

namespace LowLatencyVideoWall.Admin
{
    /// <summary>One rectangle on the wall while it is being edited, in grid cells.</summary>
    internal sealed class LayoutPane
    {
        internal int Id { get; set; }
        internal int Col { get; set; }
        internal int Row { get; set; }
        internal int ColSpan { get; set; } = 1;
        internal int RowSpan { get; set; } = 1;

        internal Guid CameraId { get; set; }
        internal string CameraName { get; set; } = "";

        internal bool HasCamera => CameraId != Guid.Empty;

        internal string Label => HasCamera
            ? CameraName
            : "no camera";

        internal bool Contains(int col, int row)
            => col >= Col && col < Col + ColSpan && row >= Row && row < Row + RowSpan;

        internal bool Overlaps(LayoutPane other)
            => Col < other.Col + other.ColSpan && Col + ColSpan > other.Col
               && Row < other.Row + other.RowSpan && Row + RowSpan > other.Row;
    }

    /// <summary>
    /// The wall layout editor: drag on empty space to create a pane, drag a pane to move it,
    /// drag an edge or a corner to resize it, right click or Delete to remove it, double click
    /// to pick its camera.
    ///
    /// The interaction model is the proven one from the FlexView grid editor and has not
    /// changed with the move to WPF: a 60x60 cell grid, a two cell minimum span, snapping by
    /// construction since every coordinate is a cell index, overlap rejected on drop rather
    /// than during the drag, and hit testing on edges within a few pixels so a pane can be
    /// grabbed by its border.
    ///
    /// Everything is drawn as real elements on a <see cref="Canvas"/> that is letterboxed to
    /// 16 by 9, and the panes themselves are not hit testable so every mouse event lands on
    /// the canvas and the arithmetic stays in cell space.
    ///
    /// Cells map straight onto the 0 to 1000 space PaneDefinition stores, so a wall is
    /// resolution independent.
    /// </summary>
    internal sealed class LayoutEditorControl : System.Windows.Controls.UserControl
    {
        private const int GridCols = 60;
        private const int GridRows = 60;
        private const int MinSpan = 2;
        private const double ResizeThreshold = 7;

        // 16 by 9 canvas, letterboxed inside whatever the pane gives us, so a layout drawn on
        // a small Management Client window looks the same as the wall it ends up on.
        private const double CanvasAspect = 16.0 / 9.0;

        private static readonly Brush CanvasBack = Freeze(Color.FromRgb(250, 250, 250));
        private static readonly Brush GridLine = Freeze(Color.FromRgb(234, 234, 234));
        private static readonly Brush GridLineMajor = Freeze(Color.FromRgb(214, 214, 214));
        private static readonly Brush CanvasBorder = Freeze(Color.FromRgb(170, 170, 170));

        private static readonly Brush PaneFill = Freeze(Color.FromRgb(235, 243, 251));
        private static readonly Brush PaneBorder = Freeze(Color.FromRgb(120, 150, 185));
        private static readonly Brush PaneHoverFill = Freeze(Color.FromRgb(224, 236, 249));
        private static readonly Brush SelectedFill = Freeze(Color.FromRgb(210, 229, 247));
        private static readonly Brush SelectedBorder = Freeze(Color.FromRgb(50, 110, 180));
        private static readonly Brush PreviewFill = Freeze(Color.FromArgb(60, 88, 166, 255));
        private static readonly Brush PreviewBorder = Freeze(Color.FromRgb(88, 166, 255));
        private static readonly Brush OverlapFill = Freeze(Color.FromArgb(70, 248, 81, 73));
        private static readonly Brush OverlapBorder = Freeze(Color.FromRgb(200, 60, 55));
        private static readonly Brush EmptyPaneFore = Freeze(Color.FromRgb(150, 150, 150));
        private static readonly Brush LabelFore = Freeze(Color.FromRgb(40, 60, 90));
        private static readonly Brush HandleFill = Freeze(Color.FromRgb(160, 160, 160));
        private static readonly Brush HandleBorder = Freeze(Color.FromRgb(90, 90, 90));
        private static readonly Brush HintFore = Freeze(Color.FromRgb(140, 140, 140));

        private enum DragMode { None, Creating, Moving, Resizing }
        private enum ResizeEdge { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

        private readonly List<LayoutPane> _panes = new List<LayoutPane>();
        private LayoutPane _selected;
        private LayoutPane _hovered;
        private int _nextPaneId = 1;

        private DragMode _dragMode = DragMode.None;
        private int _dragStartCol, _dragStartRow;
        private int _createEndCol, _createEndRow;
        private int _moveOrigCol, _moveOrigRow;

        private ResizeEdge _resizeEdge;
        private int _resizeOrigCol, _resizeOrigRow, _resizeOrigColSpan, _resizeOrigRowSpan;

        private readonly Border _frame;
        private readonly Canvas _canvas;
        private readonly TextBlock _hint;
        private readonly List<Line> _lines = new List<Line>();
        private readonly List<UIElement> _paneVisuals = new List<UIElement>();

        internal LayoutEditorControl()
        {
            Focusable = true;
            Background = Brushes.White;

            _canvas = new Canvas { Background = Brushes.Transparent, ClipToBounds = true };

            _hint = new TextBlock
            {
                Text = "Drag to create a pane. Drag a pane to move it, drag its edge to resize." +
                       Environment.NewLine +
                       "Double click a pane to pick its camera, right click to delete it.",
                Foreground = HintFore,
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            var inner = new Grid();
            inner.Children.Add(_canvas);
            inner.Children.Add(_hint);

            _frame = new Border
            {
                Background = CanvasBack,
                BorderBrush = CanvasBorder,
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true,
                Child = inner
            };

            BuildGridLines();

            var host = new Grid();
            host.Children.Add(_frame);
            host.SizeChanged += (s, e) => Letterbox(e.NewSize);
            Content = host;

            _canvas.SizeChanged += (s, e) => Redraw();
            _canvas.MouseLeftButtonDown += OnCanvasLeftDown;
            _canvas.MouseRightButtonDown += OnCanvasRightDown;
            _canvas.MouseMove += OnCanvasMouseMove;
            _canvas.MouseLeftButtonUp += OnCanvasLeftUp;
            _canvas.MouseLeave += OnCanvasMouseLeave;
            KeyDown += OnEditorKeyDown;
        }

        /// <summary>Raised whenever the panes changed in a way worth saving.</summary>
        internal event EventHandler LayoutChanged;

        /// <summary>Raised when the selected pane changed, so the host can enable its buttons.</summary>
        internal event EventHandler SelectionChanged;

        /// <summary>Raised on double click, which is how a camera gets picked for a pane.</summary>
        internal event EventHandler PaneActivated;

        internal LayoutPane SelectedPane => _selected;

        internal int PaneCount => _panes.Count;

        internal bool IsEnabledForEditing { get; set; } = true;

        // ---- model in and out ------------------------------------------------------------

        internal void LoadPanes(IEnumerable<PaneDefinition> panes)
        {
            _panes.Clear();
            _selected = null;
            _hovered = null;
            _nextPaneId = 1;
            _dragMode = DragMode.None;

            if (panes != null)
            {
                foreach (var p in panes)
                {
                    if (p == null) continue;

                    int col = ClosestGridCol(p.X);
                    int row = ClosestGridRow(p.Y);
                    int colEnd = ClosestGridCol(p.X + p.Width);
                    int rowEnd = ClosestGridRow(p.Y + p.Height);

                    int colSpan = Math.Max(MinSpan, colEnd - col);
                    int rowSpan = Math.Max(MinSpan, rowEnd - row);

                    col = Math.Max(0, Math.Min(col, GridCols - 1));
                    row = Math.Max(0, Math.Min(row, GridRows - 1));
                    colSpan = Math.Min(colSpan, GridCols - col);
                    rowSpan = Math.Min(rowSpan, GridRows - row);

                    _panes.Add(new LayoutPane
                    {
                        Id = _nextPaneId++,
                        Col = col,
                        Row = row,
                        ColSpan = colSpan,
                        RowSpan = rowSpan,
                        CameraId = p.CameraId,
                        CameraName = p.CameraName ?? ""
                    });
                }
            }

            Redraw();
            OnSelectionChanged();
        }

        internal List<PaneDefinition> GetPanes()
        {
            var result = new List<PaneDefinition>();

            foreach (var p in _panes.OrderBy(p => p.Row).ThenBy(p => p.Col))
            {
                int x = PaneDefinition.Scale * p.Col / GridCols;
                int y = PaneDefinition.Scale * p.Row / GridRows;
                int x2 = PaneDefinition.Scale * (p.Col + p.ColSpan) / GridCols;
                int y2 = PaneDefinition.Scale * (p.Row + p.RowSpan) / GridRows;

                result.Add(new PaneDefinition
                {
                    CameraId = p.CameraId,
                    CameraName = p.CameraName ?? "",
                    X = x,
                    Y = y,
                    Width = x2 - x,
                    Height = y2 - y
                });
            }

            return result;
        }

        internal void AssignCameraToSelected(Guid cameraId, string cameraName)
        {
            if (_selected == null) return;
            _selected.CameraId = cameraId;
            _selected.CameraName = cameraName ?? "";
            Redraw();
            OnLayoutChanged();
            OnSelectionChanged();
        }

        internal void ClearCameraOnSelected()
        {
            if (_selected == null) return;
            _selected.CameraId = Guid.Empty;
            _selected.CameraName = "";
            Redraw();
            OnLayoutChanged();
            OnSelectionChanged();
        }

        internal void DeleteSelected()
        {
            if (_selected == null) return;
            RemovePane(_selected);
        }

        internal void ClearAll()
        {
            if (_panes.Count == 0) return;
            _panes.Clear();
            _selected = null;
            _hovered = null;
            _nextPaneId = 1;
            Redraw();
            OnLayoutChanged();
            OnSelectionChanged();
        }

        /// <summary>Lays out n panes in the tightest even grid that fits them, overwriting everything.</summary>
        internal void FillWithGrid(int columns, int rows)
        {
            if (columns < 1 || rows < 1) return;

            _panes.Clear();
            _nextPaneId = 1;
            _selected = null;
            _hovered = null;

            int colSpan = GridCols / columns;
            int rowSpan = GridRows / rows;
            if (colSpan < MinSpan || rowSpan < MinSpan) return;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    _panes.Add(new LayoutPane
                    {
                        Id = _nextPaneId++,
                        Col = c * colSpan,
                        Row = r * rowSpan,
                        ColSpan = colSpan,
                        RowSpan = rowSpan
                    });
                }
            }

            Redraw();
            OnLayoutChanged();
            OnSelectionChanged();
        }

        // ---- geometry --------------------------------------------------------------------

        /// <summary>Sizes the 16 by 9 canvas to the biggest one that fits, and centres it.</summary>
        private void Letterbox(Size available)
        {
            double w = Math.Max(1, available.Width - 2);
            double h = Math.Max(1, available.Height - 2);

            double cw, ch;
            if (w / h > CanvasAspect)
            {
                ch = h;
                cw = h * CanvasAspect;
            }
            else
            {
                cw = w;
                ch = w / CanvasAspect;
            }

            _frame.Width = Math.Max(GridCols, cw) + 2;
            _frame.Height = Math.Max(GridRows, ch) + 2;
        }

        private double CellWidth => Math.Max(1, _canvas.ActualWidth) / GridCols;

        private double CellHeight => Math.Max(1, _canvas.ActualHeight) / GridRows;

        private Rect PaneBounds(int col, int row, int colSpan, int rowSpan)
            => new Rect(col * CellWidth, row * CellHeight, colSpan * CellWidth, rowSpan * CellHeight);

        private void GridPosition(Point canvasPos, out int col, out int row)
        {
            col = (int)(canvasPos.X / CellWidth);
            row = (int)(canvasPos.Y / CellHeight);
            col = Math.Max(0, Math.Min(col, GridCols - 1));
            row = Math.Max(0, Math.Min(row, GridRows - 1));
        }

        private LayoutPane PaneAt(int col, int row)
        {
            for (int i = _panes.Count - 1; i >= 0; i--)
                if (_panes[i].Contains(col, row)) return _panes[i];
            return null;
        }

        private ResizeEdge EdgeAt(LayoutPane pane, Point canvasPos)
        {
            var r = PaneBounds(pane.Col, pane.Row, pane.ColSpan, pane.RowSpan);

            bool nearLeft = Math.Abs(canvasPos.X - r.Left) < ResizeThreshold;
            bool nearRight = Math.Abs(canvasPos.X - r.Right) < ResizeThreshold;
            bool nearTop = Math.Abs(canvasPos.Y - r.Top) < ResizeThreshold;
            bool nearBottom = Math.Abs(canvasPos.Y - r.Bottom) < ResizeThreshold;

            if (nearTop && nearLeft) return ResizeEdge.TopLeft;
            if (nearTop && nearRight) return ResizeEdge.TopRight;
            if (nearBottom && nearLeft) return ResizeEdge.BottomLeft;
            if (nearBottom && nearRight) return ResizeEdge.BottomRight;
            if (nearLeft) return ResizeEdge.Left;
            if (nearRight) return ResizeEdge.Right;
            if (nearTop) return ResizeEdge.Top;
            if (nearBottom) return ResizeEdge.Bottom;

            return ResizeEdge.None;
        }

        private bool InBounds(LayoutPane pane)
            => pane.Col >= 0 && pane.Row >= 0
               && pane.Col + pane.ColSpan <= GridCols
               && pane.Row + pane.RowSpan <= GridRows;

        private bool HasOverlap(LayoutPane pane)
            => _panes.Any(other => other != pane && pane.Overlaps(other));

        private static int ClosestGridCol(int normalisedX)
        {
            int best = 0, bestDist = int.MaxValue;
            for (int c = 0; c <= GridCols; c++)
            {
                int dist = Math.Abs(PaneDefinition.Scale * c / GridCols - normalisedX);
                if (dist < bestDist) { bestDist = dist; best = c; }
            }
            return Math.Min(best, GridCols);
        }

        private static int ClosestGridRow(int normalisedY)
        {
            int best = 0, bestDist = int.MaxValue;
            for (int r = 0; r <= GridRows; r++)
            {
                int dist = Math.Abs(PaneDefinition.Scale * r / GridRows - normalisedY);
                if (dist < bestDist) { bestDist = dist; best = r; }
            }
            return Math.Min(best, GridRows);
        }

        // ---- mouse and keyboard ----------------------------------------------------------

        private void OnCanvasLeftDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsEnabledForEditing) return;

            Focus();

            var position = e.GetPosition(_canvas);
            GridPosition(position, out int col, out int row);
            var pane = PaneAt(col, row);

            // The second click of a double click is the camera pick, and by then the first
            // click has already selected the pane under the pointer.
            if (e.ClickCount == 2)
            {
                if (pane == null) return;
                _selected = pane;
                _dragMode = DragMode.None;
                Redraw();
                OnSelectionChanged();
                PaneActivated?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (pane != null)
            {
                _selected = pane;

                var edge = EdgeAt(pane, position);
                if (edge != ResizeEdge.None)
                {
                    _dragMode = DragMode.Resizing;
                    _resizeEdge = edge;
                    _resizeOrigCol = pane.Col;
                    _resizeOrigRow = pane.Row;
                    _resizeOrigColSpan = pane.ColSpan;
                    _resizeOrigRowSpan = pane.RowSpan;
                    _dragStartCol = col;
                    _dragStartRow = row;
                }
                else
                {
                    _dragMode = DragMode.Moving;
                    _moveOrigCol = pane.Col;
                    _moveOrigRow = pane.Row;
                    _dragStartCol = col;
                    _dragStartRow = row;
                }
            }
            else
            {
                _selected = null;
                _dragMode = DragMode.Creating;
                _dragStartCol = col;
                _dragStartRow = row;
                _createEndCol = col;
                _createEndRow = row;
            }

            _hovered = null;
            _canvas.CaptureMouse();
            Redraw();
            OnSelectionChanged();
        }

        private void OnCanvasRightDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsEnabledForEditing) return;

            Focus();
            GridPosition(e.GetPosition(_canvas), out int col, out int row);
            var hit = PaneAt(col, row);
            if (hit != null) RemovePane(hit);
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (!IsEnabledForEditing) return;

            var position = e.GetPosition(_canvas);
            GridPosition(position, out int col, out int row);

            if (_dragMode == DragMode.Creating)
            {
                _createEndCol = col;
                _createEndRow = row;
                Redraw();
                return;
            }

            if (_dragMode == DragMode.Moving && _selected != null)
            {
                int newCol = _moveOrigCol + (col - _dragStartCol);
                int newRow = _moveOrigRow + (row - _dragStartRow);

                newCol = Math.Max(0, Math.Min(newCol, GridCols - _selected.ColSpan));
                newRow = Math.Max(0, Math.Min(newRow, GridRows - _selected.RowSpan));

                if (newCol != _selected.Col || newRow != _selected.Row)
                {
                    _selected.Col = newCol;
                    _selected.Row = newRow;
                    Redraw();
                }
                return;
            }

            if (_dragMode == DragMode.Resizing && _selected != null)
            {
                ApplyResize(col, row);
                Redraw();
                return;
            }

            var hover = PaneAt(col, row);
            if (hover != _hovered)
            {
                _hovered = hover;
                Redraw();
            }

            _canvas.Cursor = hover == null ? Cursors.Cross : CursorForEdge(EdgeAt(hover, position));
        }

        private void OnCanvasLeftUp(object sender, MouseButtonEventArgs e)
        {
            if (!IsEnabledForEditing) return;

            _canvas.ReleaseMouseCapture();
            bool changed = false;

            if (_dragMode == DragMode.Creating)
            {
                changed = FinalizeCreate();
            }
            else if (_dragMode == DragMode.Moving && _selected != null)
            {
                changed = FinalizeMove();
            }
            else if (_dragMode == DragMode.Resizing && _selected != null)
            {
                changed = FinalizeResize();
            }

            _dragMode = DragMode.None;
            _hovered = null;
            Redraw();
            OnSelectionChanged();
            if (changed) OnLayoutChanged();
        }

        private void OnCanvasMouseLeave(object sender, MouseEventArgs e)
        {
            if (_hovered != null)
            {
                _hovered = null;
                Redraw();
            }
            _canvas.Cursor = Cursors.Arrow;
        }

        private void OnEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (!IsEnabledForEditing) return;

            if (e.Key == Key.Delete && _selected != null)
            {
                RemovePane(_selected);
                e.Handled = true;
            }
        }

        private void RemovePane(LayoutPane pane)
        {
            if (pane == null) return;

            _panes.Remove(pane);
            if (_selected == pane) _selected = null;
            if (_hovered == pane) _hovered = null;
            Renumber();
            Redraw();
            OnSelectionChanged();
            OnLayoutChanged();
        }

        private bool FinalizeCreate()
        {
            int startCol = Math.Min(_dragStartCol, _createEndCol);
            int startRow = Math.Min(_dragStartRow, _createEndRow);
            int endCol = Math.Max(_dragStartCol, _createEndCol);
            int endRow = Math.Max(_dragStartRow, _createEndRow);

            int colSpan = endCol - startCol + 1;
            int rowSpan = endRow - startRow + 1;

            // A click, or a flick of a drag, is not a pane: it would create a sliver nobody
            // can grab again.
            if (colSpan < MinSpan || rowSpan < MinSpan) return false;

            var pane = new LayoutPane
            {
                Id = _nextPaneId++,
                Col = startCol,
                Row = startRow,
                ColSpan = colSpan,
                RowSpan = rowSpan
            };

            if (!InBounds(pane) || HasOverlap(pane))
            {
                _nextPaneId--;
                return false;
            }

            _panes.Add(pane);
            _selected = pane;
            return true;
        }

        private bool FinalizeMove()
        {
            if (HasOverlap(_selected))
            {
                _selected.Col = _moveOrigCol;
                _selected.Row = _moveOrigRow;
                return false;
            }

            return _selected.Col != _moveOrigCol || _selected.Row != _moveOrigRow;
        }

        private bool FinalizeResize()
        {
            if (!InBounds(_selected) || HasOverlap(_selected))
            {
                _selected.Col = _resizeOrigCol;
                _selected.Row = _resizeOrigRow;
                _selected.ColSpan = _resizeOrigColSpan;
                _selected.RowSpan = _resizeOrigRowSpan;
                return false;
            }

            return _selected.Col != _resizeOrigCol || _selected.Row != _resizeOrigRow
                   || _selected.ColSpan != _resizeOrigColSpan || _selected.RowSpan != _resizeOrigRowSpan;
        }

        private void ApplyResize(int currentCol, int currentRow)
        {
            var pane = _selected;
            int deltaCol = currentCol - _dragStartCol;
            int deltaRow = currentRow - _dragStartRow;

            switch (_resizeEdge)
            {
                case ResizeEdge.Right:
                    pane.ColSpan = Math.Max(MinSpan, _resizeOrigColSpan + deltaCol);
                    break;

                case ResizeEdge.Bottom:
                    pane.RowSpan = Math.Max(MinSpan, _resizeOrigRowSpan + deltaRow);
                    break;

                case ResizeEdge.BottomRight:
                    pane.ColSpan = Math.Max(MinSpan, _resizeOrigColSpan + deltaCol);
                    pane.RowSpan = Math.Max(MinSpan, _resizeOrigRowSpan + deltaRow);
                    break;

                case ResizeEdge.Left:
                {
                    int newCol = _resizeOrigCol + deltaCol;
                    int newSpan = _resizeOrigColSpan - deltaCol;
                    if (newCol >= 0 && newSpan >= MinSpan)
                    {
                        pane.Col = newCol;
                        pane.ColSpan = newSpan;
                    }
                    break;
                }

                case ResizeEdge.Top:
                {
                    int newRow = _resizeOrigRow + deltaRow;
                    int newSpan = _resizeOrigRowSpan - deltaRow;
                    if (newRow >= 0 && newSpan >= MinSpan)
                    {
                        pane.Row = newRow;
                        pane.RowSpan = newSpan;
                    }
                    break;
                }

                case ResizeEdge.TopLeft:
                {
                    int newCol = _resizeOrigCol + deltaCol;
                    int newColSpan = _resizeOrigColSpan - deltaCol;
                    int newRow = _resizeOrigRow + deltaRow;
                    int newRowSpan = _resizeOrigRowSpan - deltaRow;
                    if (newCol >= 0 && newColSpan >= MinSpan && newRow >= 0 && newRowSpan >= MinSpan)
                    {
                        pane.Col = newCol;
                        pane.ColSpan = newColSpan;
                        pane.Row = newRow;
                        pane.RowSpan = newRowSpan;
                    }
                    break;
                }

                case ResizeEdge.TopRight:
                {
                    int newRow = _resizeOrigRow + deltaRow;
                    int newRowSpan = _resizeOrigRowSpan - deltaRow;
                    if (newRow >= 0 && newRowSpan >= MinSpan)
                    {
                        pane.ColSpan = Math.Max(MinSpan, _resizeOrigColSpan + deltaCol);
                        pane.Row = newRow;
                        pane.RowSpan = newRowSpan;
                    }
                    break;
                }

                case ResizeEdge.BottomLeft:
                {
                    int newCol = _resizeOrigCol + deltaCol;
                    int newColSpan = _resizeOrigColSpan - deltaCol;
                    if (newCol >= 0 && newColSpan >= MinSpan)
                    {
                        pane.Col = newCol;
                        pane.ColSpan = newColSpan;
                        pane.RowSpan = Math.Max(MinSpan, _resizeOrigRowSpan + deltaRow);
                    }
                    break;
                }
            }

            pane.Col = Math.Max(0, pane.Col);
            pane.Row = Math.Max(0, pane.Row);
            pane.ColSpan = Math.Min(pane.ColSpan, GridCols - pane.Col);
            pane.RowSpan = Math.Min(pane.RowSpan, GridRows - pane.Row);
        }

        private void Renumber()
        {
            for (int i = 0; i < _panes.Count; i++) _panes[i].Id = i + 1;
            _nextPaneId = _panes.Count + 1;
        }

        private static Cursor CursorForEdge(ResizeEdge edge)
        {
            switch (edge)
            {
                case ResizeEdge.Left:
                case ResizeEdge.Right: return Cursors.SizeWE;
                case ResizeEdge.Top:
                case ResizeEdge.Bottom: return Cursors.SizeNS;
                case ResizeEdge.TopLeft:
                case ResizeEdge.BottomRight: return Cursors.SizeNWSE;
                case ResizeEdge.TopRight:
                case ResizeEdge.BottomLeft: return Cursors.SizeNESW;
                default: return Cursors.SizeAll;
            }
        }

        private void OnLayoutChanged() => LayoutChanged?.Invoke(this, EventArgs.Empty);

        private void OnSelectionChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);

        // ---- drawing ---------------------------------------------------------------------

        /// <summary>
        /// The grid lines are created once and only repositioned, because they are the one
        /// part of the canvas that never changes while the admin drags a pane around.
        /// </summary>
        private void BuildGridLines()
        {
            for (int i = 0; i <= GridCols + GridRows + 1; i++)
            {
                var line = new Line
                {
                    StrokeThickness = 1,
                    SnapsToDevicePixels = true,
                    IsHitTestVisible = false
                };
                Panel.SetZIndex(line, 0);
                _lines.Add(line);
                _canvas.Children.Add(line);
            }
        }

        private void LayoutGridLines()
        {
            double width = _canvas.ActualWidth;
            double height = _canvas.ActualHeight;
            int index = 0;

            for (int i = 0; i <= GridCols; i++, index++)
            {
                var line = _lines[index];
                double x = Math.Round(i * CellWidth) + 0.5;
                line.X1 = line.X2 = x;
                line.Y1 = 0;
                line.Y2 = height;
                line.Stroke = i % 4 == 0 ? GridLineMajor : GridLine;
            }

            for (int i = 0; i <= GridRows; i++, index++)
            {
                var line = _lines[index];
                double y = Math.Round(i * CellHeight) + 0.5;
                line.Y1 = line.Y2 = y;
                line.X1 = 0;
                line.X2 = width;
                line.Stroke = i % 4 == 0 ? GridLineMajor : GridLine;
            }
        }

        /// <summary>
        /// Rebuilds the panes and the drag preview. Everything else on the canvas is static,
        /// and a wall has tens of panes rather than thousands, so rebuilding the lot beats
        /// keeping a visual per pane in step with a model that can be reloaded wholesale.
        /// </summary>
        private void Redraw()
        {
            if (_canvas.ActualWidth <= 0 || _canvas.ActualHeight <= 0) return;

            LayoutGridLines();

            foreach (var visual in _paneVisuals) _canvas.Children.Remove(visual);
            _paneVisuals.Clear();

            foreach (var pane in _panes) DrawPane(pane);
            DrawCreatePreview();

            _hint.Visibility = _panes.Count == 0 && _dragMode == DragMode.None
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void DrawPane(LayoutPane pane)
        {
            bool overlapping = _panes.Any(other => other != pane && pane.Overlaps(other));
            bool selected = pane == _selected;
            bool hovered = pane == _hovered;

            var fill = overlapping ? OverlapFill
                : selected ? SelectedFill
                : hovered ? PaneHoverFill
                : PaneFill;

            var border = overlapping ? OverlapBorder
                : selected ? SelectedBorder
                : PaneBorder;

            var label = new TextBlock
            {
                Text = $"{pane.Id}. {pane.Label}",
                Foreground = pane.HasCamera ? LabelFore : EmptyPaneFore,
                FontStyle = pane.HasCamera ? FontStyles.Normal : FontStyles.Italic,
                FontSize = 11.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };

            var box = new Border
            {
                Background = fill,
                BorderBrush = border,
                BorderThickness = new Thickness(selected ? 2 : 1),
                SnapsToDevicePixels = true,
                IsHitTestVisible = false,
                Child = label
            };

            // A pane sits one pixel inside its cells so two neighbours read as two panes
            // instead of one block with a line through it.
            var inner = Deflate(PaneBounds(pane.Col, pane.Row, pane.ColSpan, pane.RowSpan));
            Place(box, inner, 1);

            if (selected) DrawResizeHandle(inner);
        }

        private void DrawResizeHandle(Rect bounds)
        {
            const double size = 7;
            var handle = new Rectangle
            {
                Width = size,
                Height = size,
                Fill = HandleFill,
                Stroke = HandleBorder,
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            Place(handle, new Rect(bounds.Right - size - 1, bounds.Bottom - size - 1, size, size), 2);
        }

        private void DrawCreatePreview()
        {
            if (_dragMode != DragMode.Creating) return;

            int startCol = Math.Min(_dragStartCol, _createEndCol);
            int startRow = Math.Min(_dragStartRow, _createEndRow);
            int endCol = Math.Max(_dragStartCol, _createEndCol);
            int endRow = Math.Max(_dragStartRow, _createEndRow);

            int colSpan = endCol - startCol + 1;
            int rowSpan = endRow - startRow + 1;

            var candidate = new LayoutPane
            {
                Col = startCol, Row = startRow, ColSpan = colSpan, RowSpan = rowSpan
            };
            bool blocked = _panes.Any(p => candidate.Overlaps(p)) || colSpan < MinSpan || rowSpan < MinSpan;

            var outline = new Rectangle
            {
                Fill = blocked ? OverlapFill : PreviewFill,
                Stroke = blocked ? OverlapBorder : PreviewBorder,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
                IsHitTestVisible = false
            };

            var size = new TextBlock
            {
                Text = $"{colSpan}x{rowSpan}",
                Foreground = blocked ? OverlapBorder : SelectedBorder,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var host = new Grid { IsHitTestVisible = false };
            host.Children.Add(outline);
            host.Children.Add(size);

            Place(host, Deflate(PaneBounds(startCol, startRow, colSpan, rowSpan)), 3);
        }

        private static Rect Deflate(Rect bounds)
            => new Rect(bounds.X + 1, bounds.Y + 1,
                Math.Max(0, bounds.Width - 2), Math.Max(0, bounds.Height - 2));

        private void Place(FrameworkElement element, Rect bounds, int zIndex)
        {
            Canvas.SetLeft(element, bounds.X);
            Canvas.SetTop(element, bounds.Y);
            element.Width = Math.Max(0, bounds.Width);
            element.Height = Math.Max(0, bounds.Height);
            Panel.SetZIndex(element, zIndex);
            _canvas.Children.Add(element);
            _paneVisuals.Add(element);
        }

        private static Brush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
