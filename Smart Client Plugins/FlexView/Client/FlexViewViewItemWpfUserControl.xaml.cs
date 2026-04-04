using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FlexView.Models;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using SdkRectangle = System.Drawing.Rectangle;

namespace FlexView.Client
{
    public partial class FlexViewViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private const int GridCols = 60;
        private const int GridRows = 60;
        private const double CanvasWidth = 800.0;
        private const double CanvasHeight = 450.0;
        private const double CellWidth = CanvasWidth / GridCols;   // 50.0
        private const double CellHeight = CanvasHeight / GridRows; // 50.0
        private const int SdkMax = 1000;
        private const double ResizeThreshold = 8.0;

        // Brushes
        private static readonly SolidColorBrush GridLineBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
        private static readonly SolidColorBrush GridLineAccentBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        private static readonly SolidColorBrush PaneFill = new SolidColorBrush(Color.FromArgb(50, 40, 40, 40));
        private static readonly SolidColorBrush PaneBorder = new SolidColorBrush(Color.FromRgb(80, 80, 80));
        private static readonly SolidColorBrush PaneHoverFill = new SolidColorBrush(Color.FromArgb(70, 60, 60, 60));
        private static readonly SolidColorBrush PaneHoverBorder = new SolidColorBrush(Color.FromRgb(110, 110, 110));
        private static readonly SolidColorBrush SelectedFill = new SolidColorBrush(Color.FromArgb(80, 80, 80, 80));
        private static readonly SolidColorBrush SelectedBorder = new SolidColorBrush(Color.FromRgb(140, 140, 140));
        private static readonly SolidColorBrush PreviewFill = new SolidColorBrush(Color.FromArgb(60, 88, 166, 255));
        private static readonly SolidColorBrush PreviewBorder = new SolidColorBrush(Color.FromRgb(88, 166, 255));
        private static readonly SolidColorBrush OverlapFill = new SolidColorBrush(Color.FromArgb(80, 248, 81, 73));
        private static readonly SolidColorBrush OverlapBorder = new SolidColorBrush(Color.FromRgb(248, 81, 73));
        private static readonly SolidColorBrush CameraLabelBrush = new SolidColorBrush(Color.FromRgb(88, 166, 255));
        private static readonly SolidColorBrush ResizeHandleFill = new SolidColorBrush(Color.FromRgb(160, 160, 160));
        private static readonly SolidColorBrush ResizeHandleBorder = new SolidColorBrush(Color.FromRgb(100, 100, 100));

        // Pane state
        private readonly List<GridPane> _panes = new List<GridPane>();
        private GridPane _selectedPane;
        private GridPane _hoveredPane;
        private int _nextPaneId = 1;

        // Drag state
        private enum DragMode { None, Creating, Moving, Resizing }
        private DragMode _dragMode = DragMode.None;
        private int _dragStartCol, _dragStartRow;
        private int _createEndCol, _createEndRow;

        // Move state
        private int _moveOrigCol, _moveOrigRow;

        // Resize state
        private enum ResizeEdge { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }
        private ResizeEdge _resizeEdge;
        private int _resizeOrigCol, _resizeOrigRow, _resizeOrigColSpan, _resizeOrigRowSpan;

        // Edit mode
        private ViewAndLayoutItem _editingView;
        private Item _editingParent;
        private int _originalSlotCount;
        private bool _isEditMode;

        // Save target
        private Item _targetFolder;

        public FlexViewViewItemWpfUserControl()
        {
            InitializeComponent();
        }

        public override void Init()
        {
            FlexViewDefinition.Log.Info("ViewItemWpfUserControl Init called");
            RedrawCanvas();
            UpdateStatus();
            FlexViewDefinition.Log.Info("ViewItemWpfUserControl Init completed");
        }

        public override void Close() { }

        public override bool Maximizable => true;
        public override bool Selectable => false;
        public override bool ShowToolbar => false;

        #region Canvas Drawing

        private void RedrawCanvas()
        {
            gridCanvas.Children.Clear();
            DrawGridLines();
            DrawDragPreview();
            DrawPanes();
            hintOverlay.Visibility = _panes.Count == 0 && _dragMode == DragMode.None
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DrawGridLines()
        {
            for (int i = 0; i <= GridCols; i++)
            {
                double x = i * CellWidth;
                bool isMajor = i % 4 == 0;
                gridCanvas.Children.Add(new Line
                {
                    X1 = x, Y1 = 0, X2 = x, Y2 = CanvasHeight,
                    Stroke = isMajor ? GridLineAccentBrush : GridLineBrush,
                    StrokeThickness = 0.5
                });
            }
            for (int i = 0; i <= GridRows; i++)
            {
                double y = i * CellHeight;
                bool isMajor = i % 3 == 0;
                gridCanvas.Children.Add(new Line
                {
                    X1 = 0, Y1 = y, X2 = CanvasWidth, Y2 = y,
                    Stroke = isMajor ? GridLineAccentBrush : GridLineBrush,
                    StrokeThickness = 0.5
                });
            }
        }

        private void DrawPanes()
        {
            foreach (var pane in _panes)
            {
                bool isSelected = pane == _selectedPane;
                bool isHovered = pane == _hoveredPane && !isSelected;
                bool hasOverlap = _panes.Any(other => other != pane && pane.Overlaps(other));

                double x = pane.Col * CellWidth;
                double y = pane.Row * CellHeight;
                double w = pane.ColSpan * CellWidth;
                double h = pane.RowSpan * CellHeight;

                SolidColorBrush fill, border;
                if (hasOverlap)
                {
                    fill = OverlapFill;
                    border = OverlapBorder;
                }
                else if (isSelected)
                {
                    fill = SelectedFill;
                    border = SelectedBorder;
                }
                else if (isHovered)
                {
                    fill = PaneHoverFill;
                    border = PaneHoverBorder;
                }
                else
                {
                    fill = PaneFill;
                    border = PaneBorder;
                }

                var rect = new Rectangle
                {
                    Width = w - 2,
                    Height = h - 2,
                    Fill = fill,
                    Stroke = border,
                    StrokeThickness = 1
                };
                Canvas.SetLeft(rect, x + 1);
                Canvas.SetTop(rect, y + 1);
                gridCanvas.Children.Add(rect);

                // Camera name (if editing existing view)
                if (!string.IsNullOrEmpty(pane.CameraName) && w > 60 && h > 40)
                {
                    var camLabel = new TextBlock
                    {
                        Text = pane.CameraName,
                        Foreground = CameraLabelBrush,
                        FontSize = 10,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = w - 12
                    };
                    Canvas.SetLeft(camLabel, x + 6);
                    Canvas.SetTop(camLabel, y + h - 20);
                    gridCanvas.Children.Add(camLabel);
                }

                // Size label (center)
                if (w > 60 && h > 40)
                {
                    var sizeLabel = new TextBlock
                    {
                        Text = $"{pane.ColSpan}x{pane.RowSpan}",
                        Foreground = Brushes.White,
                        FontSize = 11,
                        Opacity = 0.4,
                        TextAlignment = TextAlignment.Center
                    };
                    sizeLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(sizeLabel, x + (w - sizeLabel.DesiredSize.Width) / 2);
                    Canvas.SetTop(sizeLabel, y + (h - sizeLabel.DesiredSize.Height) / 2);
                    gridCanvas.Children.Add(sizeLabel);
                }

                // Resize handle (bottom-right corner only)
                if (isSelected || isHovered)
                {
                    DrawResizeHandle(x + w - 7, y + h - 7);
                }
            }
        }

        private void DrawResizeHandle(double x, double y)
        {
            var handle = new Polygon
            {
                Points = new PointCollection
                {
                    new Point(0, 5),
                    new Point(5, 5),
                    new Point(5, 0)
                },
                Fill = ResizeHandleFill,
                Stroke = ResizeHandleBorder,
                StrokeThickness = 0.5,
                Opacity = 0.7
            };
            Canvas.SetLeft(handle, x);
            Canvas.SetTop(handle, y);
            gridCanvas.Children.Add(handle);
        }

        private void DrawDragPreview()
        {
            if (_dragMode != DragMode.Creating) return;

            int startCol = Math.Min(_dragStartCol, _createEndCol);
            int startRow = Math.Min(_dragStartRow, _createEndRow);
            int endCol = Math.Max(_dragStartCol, _createEndCol);
            int endRow = Math.Max(_dragStartRow, _createEndRow);

            double x = startCol * CellWidth;
            double y = startRow * CellHeight;
            double w = (endCol - startCol + 1) * CellWidth;
            double h = (endRow - startRow + 1) * CellHeight;

            var preview = new GridPane
            {
                Col = startCol, Row = startRow,
                ColSpan = endCol - startCol + 1,
                RowSpan = endRow - startRow + 1
            };
            bool overlaps = _panes.Any(p => preview.Overlaps(p));

            var rect = new Rectangle
            {
                Width = w - 2,
                Height = h - 2,
                Fill = overlaps ? OverlapFill : PreviewFill,
                Stroke = overlaps ? OverlapBorder : PreviewBorder,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            };
            Canvas.SetLeft(rect, x + 1);
            Canvas.SetTop(rect, y + 1);
            gridCanvas.Children.Add(rect);

            var label = new TextBlock
            {
                Text = $"{endCol - startCol + 1}x{endRow - startRow + 1}",
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.8
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, x + (w - label.DesiredSize.Width) / 2);
            Canvas.SetTop(label, y + (h - label.DesiredSize.Height) / 2);
            gridCanvas.Children.Add(label);
        }

        #endregion

        #region Mouse Event Handlers

        private (int col, int row) GetGridPosition(Point canvasPos)
        {
            int col = (int)(canvasPos.X / CellWidth);
            int row = (int)(canvasPos.Y / CellHeight);
            col = Math.Max(0, Math.Min(col, GridCols - 1));
            row = Math.Max(0, Math.Min(row, GridRows - 1));
            return (col, row);
        }

        private GridPane GetPaneAt(int col, int row)
        {
            for (int i = _panes.Count - 1; i >= 0; i--)
            {
                if (_panes[i].Contains(col, row))
                    return _panes[i];
            }
            return null;
        }

        private ResizeEdge GetResizeEdge(GridPane pane, Point canvasPos)
        {
            double px = pane.Col * CellWidth;
            double py = pane.Row * CellHeight;
            double pw = pane.ColSpan * CellWidth;
            double ph = pane.RowSpan * CellHeight;

            bool nearLeft = Math.Abs(canvasPos.X - px) < ResizeThreshold;
            bool nearRight = Math.Abs(canvasPos.X - (px + pw)) < ResizeThreshold;
            bool nearTop = Math.Abs(canvasPos.Y - py) < ResizeThreshold;
            bool nearBottom = Math.Abs(canvasPos.Y - (py + ph)) < ResizeThreshold;

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

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(gridCanvas);
            var (col, row) = GetGridPosition(pos);

            var pane = GetPaneAt(col, row);

            if (pane != null)
            {
                _selectedPane = pane;

                var edge = GetResizeEdge(pane, pos);
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
                _selectedPane = null;
                _dragMode = DragMode.Creating;
                _dragStartCol = col;
                _dragStartRow = row;
                _createEndCol = col;
                _createEndRow = row;
            }

            _hoveredPane = null;
            gridCanvas.CaptureMouse();
            RedrawCanvas();
            e.Handled = true;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(gridCanvas);
            var (col, row) = GetGridPosition(pos);

            if (_dragMode == DragMode.Creating)
            {
                _createEndCol = col;
                _createEndRow = row;
                RedrawCanvas();
            }
            else if (_dragMode == DragMode.Moving && _selectedPane != null)
            {
                int deltaCol = col - _dragStartCol;
                int deltaRow = row - _dragStartRow;
                int newCol = _moveOrigCol + deltaCol;
                int newRow = _moveOrigRow + deltaRow;

                newCol = Math.Max(0, Math.Min(newCol, GridCols - _selectedPane.ColSpan));
                newRow = Math.Max(0, Math.Min(newRow, GridRows - _selectedPane.RowSpan));

                _selectedPane.Col = newCol;
                _selectedPane.Row = newRow;
                RedrawCanvas();
            }
            else if (_dragMode == DragMode.Resizing && _selectedPane != null)
            {
                ApplyResize(col, row);
                RedrawCanvas();
            }
            else
            {
                // Hover tracking
                var pane = GetPaneAt(col, row);
                if (pane != _hoveredPane)
                {
                    _hoveredPane = pane;
                    RedrawCanvas();
                }

                if (pane != null)
                {
                    var edge = GetResizeEdge(pane, pos);
                    gridCanvas.Cursor = GetCursorForEdge(edge);
                }
                else
                {
                    gridCanvas.Cursor = Cursors.Cross;
                }
            }
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            gridCanvas.ReleaseMouseCapture();

            if (_dragMode == DragMode.Creating)
            {
                FinalizeCreate();
                _selectedPane = null;
            }
            else if (_dragMode == DragMode.Moving && _selectedPane != null)
            {
                FinalizeMove();
                _selectedPane = null;
            }
            else if (_dragMode == DragMode.Resizing && _selectedPane != null)
            {
                FinalizeResize();
                _selectedPane = null;
            }

            _dragMode = DragMode.None;
            _hoveredPane = null;
            RedrawCanvas();
            UpdateStatus();
            FireClickEvent();
        }

        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(gridCanvas);
            var (col, row) = GetGridPosition(pos);
            var pane = GetPaneAt(col, row);

            if (pane != null)
            {
                _panes.Remove(pane);
                if (_selectedPane == pane) _selectedPane = null;
                if (_hoveredPane == pane) _hoveredPane = null;
                RenumberPanes();
                RedrawCanvas();
                UpdateStatus();
            }

            e.Handled = true;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && _selectedPane != null)
            {
                _panes.Remove(_selectedPane);
                _selectedPane = null;
                RenumberPanes();
                RedrawCanvas();
                UpdateStatus();
                e.Handled = true;
            }
        }

        #endregion

        #region Drag Finalization

        private void FinalizeCreate()
        {
            int startCol = Math.Min(_dragStartCol, _createEndCol);
            int startRow = Math.Min(_dragStartRow, _createEndRow);
            int endCol = Math.Max(_dragStartCol, _createEndCol);
            int endRow = Math.Max(_dragStartRow, _createEndRow);

            int colSpan = endCol - startCol + 1;
            int rowSpan = endRow - startRow + 1;
            if (colSpan < 2 || rowSpan < 2)
                return;

            var newPane = new GridPane
            {
                Id = _nextPaneId++,
                Col = startCol,
                Row = startRow,
                ColSpan = colSpan,
                RowSpan = rowSpan
            };

            if (!IsInBounds(newPane) || HasOverlap(newPane))
                return;

            _panes.Add(newPane);
            _selectedPane = newPane;
        }

        private void FinalizeMove()
        {
            if (HasOverlap(_selectedPane))
            {
                _selectedPane.Col = _moveOrigCol;
                _selectedPane.Row = _moveOrigRow;
            }
        }

        private void FinalizeResize()
        {
            if (!IsInBounds(_selectedPane) || HasOverlap(_selectedPane))
            {
                _selectedPane.Col = _resizeOrigCol;
                _selectedPane.Row = _resizeOrigRow;
                _selectedPane.ColSpan = _resizeOrigColSpan;
                _selectedPane.RowSpan = _resizeOrigRowSpan;
            }
        }

        private void ApplyResize(int currentCol, int currentRow)
        {
            var pane = _selectedPane;
            int deltaCol = currentCol - _dragStartCol;
            int deltaRow = currentRow - _dragStartRow;

            switch (_resizeEdge)
            {
                case ResizeEdge.Right:
                    pane.ColSpan = Math.Max(2, _resizeOrigColSpan + deltaCol);
                    break;
                case ResizeEdge.Bottom:
                    pane.RowSpan = Math.Max(2, _resizeOrigRowSpan + deltaRow);
                    break;
                case ResizeEdge.BottomRight:
                    pane.ColSpan = Math.Max(2, _resizeOrigColSpan + deltaCol);
                    pane.RowSpan = Math.Max(2, _resizeOrigRowSpan + deltaRow);
                    break;
                case ResizeEdge.Left:
                    {
                        int newCol = _resizeOrigCol + deltaCol;
                        int newSpan = _resizeOrigColSpan - deltaCol;
                        if (newCol >= 0 && newSpan >= 2)
                        {
                            pane.Col = newCol;
                            pane.ColSpan = newSpan;
                        }
                    }
                    break;
                case ResizeEdge.Top:
                    {
                        int newRow = _resizeOrigRow + deltaRow;
                        int newSpan = _resizeOrigRowSpan - deltaRow;
                        if (newRow >= 0 && newSpan >= 2)
                        {
                            pane.Row = newRow;
                            pane.RowSpan = newSpan;
                        }
                    }
                    break;
                case ResizeEdge.TopLeft:
                    {
                        int newCol = _resizeOrigCol + deltaCol;
                        int newColSpan = _resizeOrigColSpan - deltaCol;
                        int newRow = _resizeOrigRow + deltaRow;
                        int newRowSpan = _resizeOrigRowSpan - deltaRow;
                        if (newCol >= 0 && newColSpan >= 2 && newRow >= 0 && newRowSpan >= 2)
                        {
                            pane.Col = newCol;
                            pane.ColSpan = newColSpan;
                            pane.Row = newRow;
                            pane.RowSpan = newRowSpan;
                        }
                    }
                    break;
                case ResizeEdge.TopRight:
                    {
                        int newRow = _resizeOrigRow + deltaRow;
                        int newRowSpan = _resizeOrigRowSpan - deltaRow;
                        if (newRow >= 0 && newRowSpan >= 2)
                        {
                            pane.ColSpan = Math.Max(2, _resizeOrigColSpan + deltaCol);
                            pane.Row = newRow;
                            pane.RowSpan = newRowSpan;
                        }
                    }
                    break;
                case ResizeEdge.BottomLeft:
                    {
                        int newCol = _resizeOrigCol + deltaCol;
                        int newColSpan = _resizeOrigColSpan - deltaCol;
                        if (newCol >= 0 && newColSpan >= 2)
                        {
                            pane.Col = newCol;
                            pane.ColSpan = newColSpan;
                            pane.RowSpan = Math.Max(2, _resizeOrigRowSpan + deltaRow);
                        }
                    }
                    break;
            }

            pane.Col = Math.Max(0, pane.Col);
            pane.Row = Math.Max(0, pane.Row);
            pane.ColSpan = Math.Min(pane.ColSpan, GridCols - pane.Col);
            pane.RowSpan = Math.Min(pane.RowSpan, GridRows - pane.Row);
        }

        #endregion

        #region Helpers

        private bool IsInBounds(GridPane pane)
        {
            return pane.Col >= 0 && pane.Row >= 0 &&
                   pane.Col + pane.ColSpan <= GridCols &&
                   pane.Row + pane.RowSpan <= GridRows;
        }

        private bool HasOverlap(GridPane pane)
        {
            return _panes.Any(other => other != pane && pane.Overlaps(other));
        }

        // Find the grid column whose SDK X (SdkMax * col / GridCols) is closest to sdkX
        private static int ClosestGridCol(int sdkX)
        {
            int best = 0;
            int bestDist = int.MaxValue;
            for (int c = 0; c <= GridCols; c++)
            {
                int dist = Math.Abs(SdkMax * c / GridCols - sdkX);
                if (dist < bestDist) { bestDist = dist; best = c; }
            }
            return Math.Min(best, GridCols);
        }

        private static int ClosestGridRow(int sdkY)
        {
            int best = 0;
            int bestDist = int.MaxValue;
            for (int r = 0; r <= GridRows; r++)
            {
                int dist = Math.Abs(SdkMax * r / GridRows - sdkY);
                if (dist < bestDist) { bestDist = dist; best = r; }
            }
            return Math.Min(best, GridRows);
        }

        private void RenumberPanes()
        {
            for (int i = 0; i < _panes.Count; i++)
                _panes[i].Id = i + 1;
            _nextPaneId = _panes.Count + 1;
        }

        private Cursor GetCursorForEdge(ResizeEdge edge)
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

        private void UpdateStatus()
        {
            string mode = _isEditMode ? "Edit" : "New";
            statusText.Text = $"{mode} | {_panes.Count} pane{(_panes.Count != 1 ? "s" : "")} | {GridCols}x{GridRows} grid";
        }

        private void ShowSavedStatus(string viewName)
        {
            var original = statusText.Foreground;
            statusText.Text = $"Saved \"{viewName}\"";
            statusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4CAF50"));

            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                statusText.Foreground = original;
                UpdateStatus();
            };
            timer.Start();
        }

        #endregion

        #region SDK Coordinate Conversion

        private SdkRectangle[] ConvertPanesToSdkLayout()
        {
            var ordered = _panes
                .Where(p => p.OriginalSlotIndex >= 0)
                .OrderBy(p => p.OriginalSlotIndex)
                .Concat(_panes.Where(p => p.OriginalSlotIndex < 0))
                .ToList();

            var rects = new SdkRectangle[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                var p = ordered[i];
                int x = SdkMax * p.Col / GridCols;
                int y = SdkMax * p.Row / GridRows;
                int x2 = SdkMax * (p.Col + p.ColSpan) / GridCols;
                int y2 = SdkMax * (p.Row + p.RowSpan) / GridRows;
                rects[i] = new SdkRectangle(x, y, x2 - x, y2 - y);
            }
            return rects;
        }

        private void LoadFromSdkLayout(SdkRectangle[] layout)
        {
            _panes.Clear();
            _nextPaneId = 1;

            for (int i = 0; i < layout.Length; i++)
            {
                var rect = layout[i];
                // Find closest grid column/row whose SDK coordinate matches
                int col = ClosestGridCol(rect.X);
                int row = ClosestGridRow(rect.Y);
                int colEnd = ClosestGridCol(rect.X + rect.Width);
                int rowEnd = ClosestGridRow(rect.Y + rect.Height);
                int colSpan = Math.Max(2, colEnd - col);
                int rowSpan = Math.Max(2, rowEnd - row);

                col = Math.Max(0, Math.Min(col, GridCols - 1));
                row = Math.Max(0, Math.Min(row, GridRows - 1));
                colSpan = Math.Min(colSpan, GridCols - col);
                rowSpan = Math.Min(rowSpan, GridRows - row);

                _panes.Add(new GridPane
                {
                    Id = _nextPaneId++,
                    Col = col,
                    Row = row,
                    ColSpan = colSpan,
                    RowSpan = rowSpan,
                    OriginalSlotIndex = i
                });
            }
        }

        #endregion

        #region View Save / Load

        private void SaveNewView(string name, Item folder)
        {
            try
            {
                var configFolder = folder as ConfigItem;
                if (configFolder == null)
                {
                    MessageBox.Show("Selected folder is not valid.", "FlexView", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var rects = ConvertPanesToSdkLayout();
                var view = configFolder.AddChild(name, Kind.View, FolderType.No) as ViewAndLayoutItem;
                if (view == null)
                {
                    MessageBox.Show("Failed to create view. Check folder permissions.", "FlexView", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                view.Layout = rects;
                view.Save();
                configFolder.PropertiesModified();
                ShowSavedStatus(name);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save view:\n{ex.Message}", "FlexView", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveEditedView()
        {
            if (_editingView == null) return;

            try
            {
                var rects = ConvertPanesToSdkLayout();
                var parentConfig = _editingParent as ConfigItem;

                // Try in-place update first (works when slot count hasn't changed)
                bool needsRecreate = rects.Length != _originalSlotCount;

                if (!needsRecreate)
                {
                    try
                    {
                        _editingView.Layout = rects;
                        _editingView.Save();
                        if (parentConfig != null)
                            parentConfig.PropertiesModified();
                        ShowSavedStatus(_editingView.Name);
                        return;
                    }
                    catch
                    {
                        // Layout assignment rejected, fall through to recreate
                        needsRecreate = true;
                    }
                }

                // Recreate: delete old view and create new one with updated layout
                if (parentConfig == null)
                {
                    MessageBox.Show("Cannot update view: parent folder is not valid.", "FlexView", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var viewName = _editingView.Name;

                // Collect camera assignments from current panes (ordered by slot)
                var ordered = _panes
                    .Where(p => p.OriginalSlotIndex >= 0)
                    .OrderBy(p => p.OriginalSlotIndex)
                    .Concat(_panes.Where(p => p.OriginalSlotIndex < 0))
                    .ToList();

                // Delete old view
                parentConfig.RemoveChild(_editingView);

                // Create new view with same name
                var newView = parentConfig.AddChild(viewName, Kind.View, FolderType.No) as ViewAndLayoutItem;
                if (newView == null)
                {
                    MessageBox.Show("Failed to recreate view.", "FlexView", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                newView.Layout = rects;
                newView.Save();

                // Restore camera assignments to matching slots
                var newConfig = newView as ConfigItem;
                if (newConfig != null)
                {
                    var children = newConfig.GetChildren();
                    if (children != null)
                    {
                        for (int i = 0; i < children.Count && i < ordered.Count; i++)
                        {
                            if (ordered[i].CameraId != Guid.Empty)
                            {
                                children[i].Properties["CameraId"] = ordered[i].CameraId.ToString();
                            }
                        }
                    }
                }

                parentConfig.PropertiesModified();

                // Update references for continued editing
                _editingView = newView;
                _originalSlotCount = rects.Length;
                ShowSavedStatus(viewName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update view:\n{ex.Message}", "FlexView", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadViewForEditing(ViewAndLayoutItem view, Item parent)
        {
            _isEditMode = true;
            _editingView = view;
            _editingParent = parent;
            _selectedPane = null;
            _hoveredPane = null;

            var layout = view.Layout;
            if (layout == null || layout.Length == 0)
            {
                MessageBox.Show("Selected view has no layout data.", "FlexView", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _originalSlotCount = layout.Length;
            LoadFromSdkLayout(layout);
            TryReadCameraData(view);

            _targetFolder = parent;
            viewNameLabel.Text = view.Name;

            RedrawCanvas();
            UpdateStatus();
        }

        private void TryReadCameraData(ViewAndLayoutItem view)
        {
            try
            {
                var configItem = view as ConfigItem;
                if (configItem == null) return;

                var children = configItem.GetChildren();
                if (children == null || children.Count == 0) return;

                for (int i = 0; i < children.Count && i < _panes.Count; i++)
                {
                    var child = children[i];
                    if (child.Properties == null) continue;

                    string camIdStr;
                    if (!child.Properties.TryGetValue("CameraId", out camIdStr)) continue;
                    if (!Guid.TryParse(camIdStr, out var camId) || camId == Guid.Empty) continue;

                    _panes[i].CameraId = camId;

                    // Try to resolve camera name via FQID
                    try
                    {
                        var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                        var fqid = new FQID(serverId, Guid.Empty, camId, FolderType.No, Kind.Camera);
                        var camItem = Configuration.Instance.GetItem(fqid);
                        if (camItem != null && !string.IsNullOrEmpty(camItem.Name))
                        {
                            _panes[i].CameraName = camItem.Name;
                            continue;
                        }
                    }
                    catch { }

                    // Fallback: try GetItemsByKind
                    try
                    {
                        var allCams = Configuration.Instance.GetItemsByKind(Kind.Camera);
                        if (allCams != null)
                        {
                            foreach (var cam in allCams)
                            {
                                if (cam.FQID.ObjectId == camId)
                                {
                                    _panes[i].CameraName = cam.Name;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }

                    if (string.IsNullOrEmpty(_panes[i].CameraName))
                        _panes[i].CameraName = camId.ToString().Substring(0, 8) + "...";
                }
            }
            catch
            {
            }
        }

        #endregion

        #region Button Handlers

        private void OnNewClick(object sender, RoutedEventArgs e)
        {
            _panes.Clear();
            _selectedPane = null;
            _hoveredPane = null;
            _nextPaneId = 1;
            _isEditMode = false;
            _editingView = null;
            _editingParent = null;
            _originalSlotCount = 0;
            _targetFolder = null;
            viewNameLabel.Text = "";
            RedrawCanvas();
            UpdateStatus();
        }

        private void OnOpenClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var browser = new ViewBrowserWindow(BrowseMode.SelectView);
                browser.Owner = Application.Current.MainWindow;
                if (browser.ShowDialog() == true && browser.SelectedItem != null)
                {
                    var view = browser.SelectedItem as ViewAndLayoutItem;
                    if (view == null)
                    {
                        MessageBox.Show("Selected item is not a view layout.", "FlexView",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    LoadViewForEditing(view, browser.SelectedParent);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open view:\n{ex.Message}", "FlexView",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            if (_panes.Count == 0) return;

            var result = MessageBox.Show("Clear all panes?", "FlexView",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _panes.Clear();
                _selectedPane = null;
                _hoveredPane = null;
                _nextPaneId = 1;
                RedrawCanvas();
                UpdateStatus();
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_panes.Count == 0)
            {
                MessageBox.Show("Please create at least one pane.", "FlexView", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_isEditMode)
            {
                SaveEditedView();
            }
            else
            {
                var dlg = new SaveViewWindow(null, _targetFolder);
                dlg.Owner = Application.Current.MainWindow;
                if (dlg.ShowDialog() == true)
                {
                    _targetFolder = dlg.SelectedFolder;
                    SaveNewView(dlg.ViewName, dlg.SelectedFolder);
                }
            }
        }

        #endregion
    }
}
