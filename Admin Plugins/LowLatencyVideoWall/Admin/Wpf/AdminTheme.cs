using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using Material.Icons;
using Material.Icons.WPF;

namespace LowLatencyVideoWall.Admin.Wpf
{
    /// <summary>
    /// Shared visual theme and builders for the WPF admin console, ported unchanged from the
    /// Race Commander plugin so both products read as one design. Light modern look: white
    /// cards, boxed icon-input fields, accent and secondary buttons, striped DataGrid with
    /// borderless icon actions. Styles are authored as a XAML resource dictionary and merged
    /// per control; icon buttons are built in code so their Click handlers stay wired to the
    /// caller.
    /// </summary>
    internal static class AdminTheme
    {
        public static readonly Brush Muted = Frozen("#5A6470");
        public static readonly Brush Accent = Frozen("#006ED2");
        public static readonly Brush Danger = Frozen("#DC3545");
        public static readonly Brush IconGrey = Frozen("#6B7480");
        public static readonly Brush Text = Frozen("#2B2F33");
        public static readonly Brush Ok = Frozen("#20C997");
        public static readonly Brush Amber = Frozen("#FD7E14");
        public static readonly Brush Grey = Frozen("#9AA0A6");

        private static Brush Frozen(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        /// <summary>Parse a fresh resource dictionary of the shared styles. One per control instance.</summary>
        public static ResourceDictionary BuildResources() => (ResourceDictionary)XamlReader.Parse(Xaml);

        /// <summary>Apply the behavioural + visual defaults that are not in the implicit style.</summary>
        public static void ConfigureGrid(DataGrid grid)
        {
            grid.AutoGenerateColumns = false;
            grid.CanUserAddRows = false;
            grid.CanUserDeleteRows = false;
            grid.CanUserSortColumns = true;
            grid.CanUserResizeRows = false;
            grid.IsReadOnly = false;
            grid.SelectionMode = DataGridSelectionMode.Single;
            grid.SelectionUnit = DataGridSelectionUnit.FullRow;
            grid.HeadersVisibility = DataGridHeadersVisibility.All;
            grid.RowHeaderWidth = 24;
            grid.MinRowHeight = 32;
            grid.Cursor = Cursors.Arrow;
            grid.AlternationCount = 2;
            grid.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            grid.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }

        /// <summary>A rounded search box with a leading magnifier. Returns the container; the created
        /// <see cref="TextBox"/> is handed back via <paramref name="textBox"/>.</summary>
        public static Border SearchBox(string placeholder, out TextBox textBox)
        {
            textBox = new TextBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                VerticalContentAlignment = VerticalAlignment.Center,
                MinWidth = 200,
                FontSize = 13
            };
            return IconInputBox(MaterialIconKind.Magnify, textBox, double.NaN, null, placeholder);
        }

        /// <summary>The shared input-box look: a rounded control with a leading icon in its own boxed
        /// segment (grey, divider on the right), then the supplied content (a borderless TextBox /
        /// PasswordBox / ComboBox) and an optional trailing element. Used for every field so the
        /// header, filter and dialogs read as one design.</summary>
        public static Border IconInputBox(MaterialIconKind leadingIcon, FrameworkElement content,
            double width = double.NaN, FrameworkElement trailing = null, string tooltip = null)
        {
            var icon = new MaterialIcon { Kind = leadingIcon, Width = 16, Height = 16, Foreground = IconGrey, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var iconBox = new Border
            {
                Width = 34,
                Background = Frozen("#F1F3F5"),
                BorderBrush = Frozen("#DDE1E6"),
                BorderThickness = new Thickness(0, 0, 1, 0),
                CornerRadius = new CornerRadius(5, 0, 0, 5),
                Child = icon
            };

            var contentHost = new DockPanel { Margin = new Thickness(9, 0, 9, 0), LastChildFill = true, Background = Brushes.White };
            if (trailing != null) { DockPanel.SetDock(trailing, Dock.Right); contentHost.Children.Add(trailing); }
            contentHost.Children.Add(content);

            var inner = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(iconBox, Dock.Left);
            inner.Children.Add(iconBox);
            inner.Children.Add(contentHost);

            var border = new Border
            {
                Background = Brushes.White,
                BorderBrush = Frozen("#DDE1E6"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Height = 32,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                SnapsToDevicePixels = true,
                Child = inner,
                ToolTip = tooltip
            };
            if (!double.IsNaN(width)) border.Width = width;
            return border;
        }

        /// <summary>A small borderless clickable icon button (toolbar actions).</summary>
        public static Button IconButton(ResourceDictionary res, MaterialIconKind kind, string tooltip, RoutedEventHandler click, Brush fg = null)
        {
            var icon = new MaterialIcon { Kind = kind, Width = 18, Height = 18, Foreground = fg ?? IconGrey };
            var btn = new Button
            {
                Style = (Style)res["IconButtonStyle"],
                Content = icon,
                Padding = new Thickness(4),
                Focusable = false,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = tooltip
            };
            if (click != null) btn.Click += click;
            return btn;
        }

        /// <summary>A non-interactive help (?) icon that only shows a tooltip on hover.</summary>
        public static MaterialIcon HelpIcon(string tooltip)
        {
            return new MaterialIcon
            {
                Kind = MaterialIconKind.HelpCircleOutline,
                Width = 18,
                Height = 18,
                Foreground = IconGrey,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0),
                ToolTip = tooltip
            };
        }

        /// <summary>Accent (filled) button with a leading icon, e.g. Save / Add.</summary>
        public static Button AccentButton(ResourceDictionary res, MaterialIconKind kind, string text)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new MaterialIcon { Kind = kind, Width = 16, Height = 16, Foreground = Brushes.White, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = text, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
            return new Button { Content = sp, Style = (Style)res["AccentButtonStyle"] };
        }

        /// <summary>Secondary (outline) button with a leading icon, matching the accent button's shape.</summary>
        public static Button SecondaryButton(ResourceDictionary res, MaterialIconKind kind, string text)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new MaterialIcon { Kind = kind, Width = 16, Height = 16, Foreground = Accent, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = text, Foreground = Text, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
            return new Button { Content = sp, Style = (Style)res["SecondaryButtonStyle"] };
        }

        /// <summary>A collapsible red validation-summary banner (icon + multi-line text).</summary>
        public static Border ValidationBanner(out TextBlock text)
        {
            text = new TextBlock { Foreground = Frozen("#B02A37"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var icon = new MaterialIcon { Kind = MaterialIconKind.AlertCircleOutline, Width = 18, Height = 18, Foreground = Danger, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Top };
            var sp = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            sp.Children.Add(icon);
            sp.Children.Add(text);
            return new Border
            {
                Background = Frozen("#FDECEE"),
                BorderBrush = Frozen("#F5C2C7"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(8, 0, 8, 8),
                Visibility = Visibility.Collapsed,
                Child = sp
            };
        }

        /// <summary>A borderless icon-only action column (e.g. Delete).</summary>
        public static DataGridTemplateColumn IconActionColumn(ResourceDictionary res, string header,
            MaterialIconKind kind, Brush fg, string tooltip, RoutedEventHandler click, double width,
            string styleKey = "IconButtonStyle", double iconSize = 16)
        {
            var btn = new FrameworkElementFactory(typeof(Button));
            btn.SetValue(FrameworkElement.StyleProperty, (Style)res[styleKey]);
            btn.SetValue(FrameworkElement.ToolTipProperty, tooltip);
            btn.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, click);

            var icon = new FrameworkElementFactory(typeof(MaterialIcon));
            icon.SetValue(MaterialIcon.KindProperty, kind);
            icon.SetValue(FrameworkElement.WidthProperty, iconSize);
            icon.SetValue(FrameworkElement.HeightProperty, iconSize);
            icon.SetValue(Control.ForegroundProperty, fg);
            btn.AppendChild(icon);

            return Template(header, width, btn);
        }

        /// <summary>A rounded "pill" button column with a leading icon and text bound to
        /// <paramref name="bindPath"/> (e.g. the camera summary).</summary>
        public static DataGridTemplateColumn PillColumn(ResourceDictionary res, string header,
            MaterialIconKind kind, string bindPath, string tooltip, RoutedEventHandler click, double width)
        {
            var btn = new FrameworkElementFactory(typeof(Button));
            btn.SetValue(FrameworkElement.StyleProperty, (Style)res["PillButtonStyle"]);
            btn.SetValue(FrameworkElement.ToolTipProperty, tooltip);
            btn.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, click);

            var sp = new FrameworkElementFactory(typeof(StackPanel));
            sp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var icon = new FrameworkElementFactory(typeof(MaterialIcon));
            icon.SetValue(MaterialIcon.KindProperty, kind);
            icon.SetValue(FrameworkElement.WidthProperty, 15.0);
            icon.SetValue(FrameworkElement.HeightProperty, 15.0);
            icon.SetValue(Control.ForegroundProperty, Accent);
            icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var txt = new FrameworkElementFactory(typeof(TextBlock));
            txt.SetBinding(TextBlock.TextProperty, new Binding(bindPath));
            txt.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            txt.SetValue(TextBlock.FontSizeProperty, 12.0);

            sp.AppendChild(icon);
            sp.AppendChild(txt);
            btn.AppendChild(sp);

            return Template(header, width, btn);
        }

        /// <summary>Make cells enter edit mode on a single click; commits when focus leaves the grid.</summary>
        public static void EnableSingleClickEdit(DataGrid grid, ResourceDictionary res)
        {
            var style = new Style(typeof(DataGridCell), (Style)res["DataGridCellStyle"]);
            style.Setters.Add(new EventSetter(UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(OnCellPreviewMouseDown)));
            grid.CellStyle = style;

            grid.LostKeyboardFocus += (s, e) =>
            {
                if (FindParent<DataGrid>(e.NewFocus as DependencyObject) == grid) return;
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
            };

            grid.PreviewMouseDown += (s, e) =>
            {
                for (var d = e.OriginalSource as DependencyObject; d != null && d != grid; d = VisualTreeHelper.GetParent(d))
                    if (d is DataGridCell) return;
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
            };
        }

        private static void OnCellPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is DataGridCell cell) || cell.IsEditing || cell.IsReadOnly) return;
            var grid = FindParent<DataGrid>(cell);
            if (grid == null) return;
            if (!cell.IsFocused) cell.Focus();
            var row = FindParent<DataGridRow>(cell);
            if (row != null) row.IsSelected = true;
            grid.BeginEdit(e);
        }

        private static T FindParent<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T)) d = VisualTreeHelper.GetParent(d);
            return d as T;
        }

        /// <summary>Keep the blank "add row" (IsNew) pinned to the bottom regardless of column sort.</summary>
        public static void EnablePinnedAddRowSort(DataGrid grid)
        {
            grid.Sorting += (sender, e) =>
            {
                e.Handled = true;
                var dir = e.Column.SortDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
                ApplyPinnedSort((DataGrid)sender, e.Column, dir);
            };
        }

        public static void ApplyPinnedSort(DataGrid grid, DataGridColumn column, ListSortDirection dir)
        {
            var view = (grid.ItemsSource as ListCollectionView)
                       ?? (CollectionViewSource.GetDefaultView(grid.ItemsSource) as ListCollectionView);
            if (view == null || column == null) return;
            foreach (var c in grid.Columns) c.SortDirection = null;
            column.SortDirection = dir;
            view.CustomSort = new AddRowLastComparer(column.SortMemberPath, dir);
        }

        private class AddRowLastComparer : System.Collections.IComparer
        {
            private readonly string _path;
            private readonly ListSortDirection _dir;
            public AddRowLastComparer(string path, ListSortDirection dir) { _path = path; _dir = dir; }

            public int Compare(object x, object y)
            {
                if (string.IsNullOrEmpty(_path)) return 0;
                object xv = Get(x, _path), yv = Get(y, _path);
                string sx = xv as string, sy = yv as string;

                bool xPin = IsNew(x) && string.IsNullOrEmpty(sx);
                bool yPin = IsNew(y) && string.IsNullOrEmpty(sy);
                if (xPin != yPin) return xPin ? 1 : -1;
                if (xPin) return 0;

                int c = (sx != null && sy != null)
                    ? NaturalCompare(sx, sy)
                    : System.Collections.Comparer.Default.Compare(xv, yv);
                return _dir == ListSortDirection.Ascending ? c : -c;
            }

            private static int NaturalCompare(string a, string b)
            {
                int i = 0, j = 0;
                while (i < a.Length && j < b.Length)
                {
                    if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                    {
                        int si = i, sj = j;
                        while (i < a.Length && char.IsDigit(a[i])) i++;
                        while (j < b.Length && char.IsDigit(b[j])) j++;
                        var na = a.Substring(si, i - si).TrimStart('0');
                        var nb = b.Substring(sj, j - sj).TrimStart('0');
                        if (na.Length != nb.Length) return na.Length - nb.Length;
                        int cmp = string.CompareOrdinal(na, nb);
                        if (cmp != 0) return cmp;
                    }
                    else
                    {
                        int cmp = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                        if (cmp != 0) return cmp;
                        i++; j++;
                    }
                }
                return (a.Length - i) - (b.Length - j);
            }

            private static bool IsNew(object o) => (o?.GetType().GetProperty("IsNew")?.GetValue(o) as bool?) == true;
            private static object Get(object o, string path) => o?.GetType().GetProperty(path)?.GetValue(o);
        }

        private static DataGridTemplateColumn Template(string header, double width, FrameworkElementFactory root)
        {
            return new DataGridTemplateColumn
            {
                Header = header,
                Width = new DataGridLength(width),
                IsReadOnly = true,
                CanUserSort = false,
                CellTemplate = new DataTemplate { VisualTree = root }
            };
        }

        private const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <Style TargetType='DataGrid'>
    <Setter Property='Background' Value='White'/>
    <Setter Property='RowBackground' Value='White'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='GridLinesVisibility' Value='Horizontal'/>
    <Setter Property='HorizontalGridLinesBrush' Value='#EDEFF2'/>
    <Setter Property='VerticalGridLinesBrush' Value='Transparent'/>
    <Setter Property='RowHeight' Value='40'/>
    <Setter Property='ColumnHeaderHeight' Value='40'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='FontWeight' Value='Normal'/>
    <Setter Property='Foreground' Value='#2B2F33'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
  </Style>

  <Style TargetType='DataGridColumnHeader'>
    <Setter Property='Background' Value='#F7F8FA'/>
    <Setter Property='Foreground' Value='#5A6470'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Height' Value='40'/>
    <Setter Property='Padding' Value='12,0,8,0'/>
    <Setter Property='BorderThickness' Value='0,0,0,1'/>
    <Setter Property='BorderBrush' Value='#E3E6EA'/>
    <Setter Property='HorizontalContentAlignment' Value='Left'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='SeparatorBrush' Value='#EDEFF2'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='DataGridColumnHeader'>
          <Grid>
            <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                    BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'>
              <DockPanel LastChildFill='True'>
                <Path x:Name='chev' DockPanel.Dock='Right' Width='9' Height='6' Stretch='None'
                      Stroke='#006ED2' StrokeThickness='1.6' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                      StrokeLineJoin='Round' VerticalAlignment='Center' Margin='6,0,2,0' Visibility='Collapsed'/>
                <ContentPresenter VerticalAlignment='Center'
                                  HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'/>
              </DockPanel>
            </Border>
            <Thumb x:Name='PART_LeftHeaderGripper' HorizontalAlignment='Left' Width='8' Cursor='SizeWE'>
              <Thumb.Template>
                <ControlTemplate TargetType='Thumb'><Border Background='Transparent'/></ControlTemplate>
              </Thumb.Template>
            </Thumb>
            <Thumb x:Name='PART_RightHeaderGripper' HorizontalAlignment='Right' Width='8' Cursor='SizeWE'>
              <Thumb.Template>
                <ControlTemplate TargetType='Thumb'><Border Background='Transparent'/></ControlTemplate>
              </Thumb.Template>
            </Thumb>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='SortDirection' Value='Ascending'>
              <Setter TargetName='chev' Property='Visibility' Value='Visible'/>
              <Setter TargetName='chev' Property='Data' Value='M0,5 L4,1 L8,5'/>
            </Trigger>
            <Trigger Property='SortDirection' Value='Descending'>
              <Setter TargetName='chev' Property='Visibility' Value='Visible'/>
              <Setter TargetName='chev' Property='Data' Value='M0,1 L4,5 L8,1'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='DataGridRowHeader'>
    <Setter Property='Background' Value='#F7F8FA'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='DataGridRowHeader'>
          <Border Background='{TemplateBinding Background}' BorderBrush='#EDEFF2'
                  BorderThickness='0,0,1,1' TextElement.Foreground='#7A828C' TextElement.FontSize='11' TextElement.FontWeight='Bold'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='DataGridRow'>
    <Setter Property='Background' Value='White'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Style.Triggers>
      <Trigger Property='AlternationIndex' Value='1'>
        <Setter Property='Background' Value='#FAFBFC'/>
      </Trigger>
      <Trigger Property='IsMouseOver' Value='True'>
        <Setter Property='Background' Value='#F1F6FB'/>
      </Trigger>
      <Trigger Property='IsSelected' Value='True'>
        <Setter Property='Background' Value='#E4F0FC'/>
      </Trigger>
      <DataTrigger Binding='{Binding HasError}' Value='True'>
        <Setter Property='Background' Value='#FFF1F2'/>
      </DataTrigger>
    </Style.Triggers>
  </Style>

  <Style x:Key='DataGridCellStyle' TargetType='DataGridCell'>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='Padding' Value='12,0,8,0'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Foreground' Value='#2B2F33'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='DataGridCell'>
          <Border Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}' BorderThickness='0'>
            <ContentPresenter VerticalAlignment='Center'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property='IsSelected' Value='True'>
        <Setter Property='Background' Value='Transparent'/>
        <Setter Property='Foreground' Value='#12314F'/>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style x:Key='ModernCheckBoxStyle' TargetType='CheckBox'>
    <Setter Property='HorizontalAlignment' Value='Center'/>
    <Setter Property='VerticalAlignment' Value='Center'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <Border x:Name='box' Width='18' Height='18' CornerRadius='4'
                  Background='White' BorderBrush='#C7CDD4' BorderThickness='1.5'
                  SnapsToDevicePixels='True'>
            <Path x:Name='check' Width='11' Height='11' Stretch='Uniform'
                  Data='M 0 5 L 4 9 L 11 0' Stroke='White' StrokeThickness='2'
                  StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'
                  Visibility='Collapsed' HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='box' Property='Background' Value='#006ED2'/>
              <Setter TargetName='box' Property='BorderThickness' Value='0'/>
              <Setter TargetName='check' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='box' Property='BorderBrush' Value='#006ED2'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='IconButtonStyle' TargetType='Button'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='Padding' Value='6'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='HorizontalAlignment' Value='Center'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bd' Background='{TemplateBinding Background}' CornerRadius='6' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='bd' Property='Background' Value='#ECEFF2'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='bd' Property='Background' Value='#DFE3E8'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='DeleteButtonStyle' TargetType='Button'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='Padding' Value='6'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='HorizontalAlignment' Value='Center'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bd' Background='{TemplateBinding Background}' CornerRadius='6' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='bd' Property='Background' Value='#FBE7E9'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='bd' Property='Background' Value='#F6D3D7'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='PillButtonStyle' TargetType='Button'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='HorizontalAlignment' Value='Left'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Padding' Value='9,3,10,3'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bd' Background='#F4F6F8' BorderBrush='#DDE1E6' BorderThickness='1'
                  CornerRadius='11' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='bd' Property='Background' Value='#E9EFF5'/>
              <Setter TargetName='bd' Property='BorderBrush' Value='#BFD4EA'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='bd' Property='Background' Value='#DCE7F2'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='AccentButtonStyle' TargetType='Button'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Padding' Value='12,6,14,6'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bd' Background='#006ED2' CornerRadius='6' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='bd' Property='Background' Value='#0A5FB0'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='bd' Property='Background' Value='#094F94'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property='IsEnabled' Value='False'>
        <Setter Property='Opacity' Value='0.45'/>
        <Setter Property='Cursor' Value='Arrow'/>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style x:Key='SecondaryButtonStyle' TargetType='Button'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Padding' Value='12,6,14,6'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bd' Background='White' BorderBrush='#DDE1E6' BorderThickness='1' CornerRadius='6' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='bd' Property='Background' Value='#F2F4F7'/>
              <Setter TargetName='bd' Property='BorderBrush' Value='#C7CDD4'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='bd' Property='Background' Value='#E7EBF0'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property='IsEnabled' Value='False'>
        <Setter Property='Opacity' Value='0.45'/>
        <Setter Property='Cursor' Value='Arrow'/>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style x:Key='SectionHeaderStyle' TargetType='TextBlock'>
    <Setter Property='Foreground' Value='#8A9099'/>
    <Setter Property='FontSize' Value='11'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Margin' Value='0,0,0,10'/>
  </Style>

  <Style x:Key='FieldLabelStyle' TargetType='TextBlock'>
    <Setter Property='Foreground' Value='#5A6470'/>
    <Setter Property='FontSize' Value='12.5'/>
    <Setter Property='VerticalAlignment' Value='Center'/>
  </Style>

  <Style x:Key='FieldTextBoxStyle' TargetType='TextBox'>
    <Setter Property='Height' Value='30'/>
    <Setter Property='Padding' Value='5,0'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='BorderBrush' Value='#DDE1E6'/>
    <Setter Property='Background' Value='White'/>
    <Setter Property='Foreground' Value='#2B2F33'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <Border x:Name='bd' CornerRadius='6' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' Background='{TemplateBinding Background}'>
            <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='#006ED2'/></Trigger>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='#BFC6CE'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='EditingTextBoxStyle' TargetType='TextBox'>
    <Setter Property='BorderBrush' Value='#006ED2'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Background' Value='White'/>
    <Setter Property='Foreground' Value='#2B2F33'/>
    <Setter Property='Padding' Value='3,0'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='MinHeight' Value='27'/>
    <Setter Property='VerticalAlignment' Value='Stretch'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <Border CornerRadius='5' Margin='2,1,2,1' VerticalAlignment='Stretch' Background='{TemplateBinding Background}'
                  BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'>
            <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='FilterComboBoxItemStyle' TargetType='ComboBoxItem'>
    <Setter Property='Padding' Value='9,5'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Foreground' Value='#2B2F33'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBoxItem'>
          <Border x:Name='bd' Background='Transparent' Padding='{TemplateBinding Padding}'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='bd' Property='Background' Value='#EAF2FB'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='FilterComboBoxStyle' TargetType='ComboBox'>
    <Setter Property='Height' Value='30'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Foreground' Value='#2B2F33'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBox'>
          <Grid>
            <ToggleButton Focusable='False' ClickMode='Press'
                          IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
              <ToggleButton.Template>
                <ControlTemplate TargetType='ToggleButton'>
                  <Border x:Name='bd' CornerRadius='6' Background='White' BorderBrush='#DDE1E6' BorderThickness='1'>
                    <Path HorizontalAlignment='Right' Margin='0,0,10,0' VerticalAlignment='Center' Data='M0,0 L8,0 L4,5 Z' Fill='#6B7480'/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='#BFC6CE'/></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter Margin='9,0,26,0' VerticalAlignment='Center' IsHitTestVisible='False'
                              Content='{TemplateBinding SelectionBoxItem}'
                              ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'/>
            <Popup Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False'>
              <Border Background='White' BorderBrush='#DDE1E6' BorderThickness='1' CornerRadius='6' Margin='0,2,0,4'
                      MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'>
                <Border.Effect>
                  <DropShadowEffect BlurRadius='10' ShadowDepth='1' Opacity='0.18' Color='Black'/>
                </Border.Effect>
                <ItemsPresenter/>
              </Border>
            </Popup>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Borderless combo for hosting inside an IconInputBox (segmented look). -->
  <Style x:Key='FilterComboBoxBorderlessStyle' TargetType='ComboBox'>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Foreground' Value='#2B2F33'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBox'>
          <Grid>
            <ToggleButton Focusable='False' ClickMode='Press'
                          IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
              <ToggleButton.Template>
                <ControlTemplate TargetType='ToggleButton'>
                  <Border Background='Transparent'>
                    <Path HorizontalAlignment='Right' Margin='0,0,2,0' VerticalAlignment='Center' Data='M0,0 L8,0 L4,5 Z' Fill='#6B7480'/>
                  </Border>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter Margin='0,0,18,0' VerticalAlignment='Center' IsHitTestVisible='False'
                              Content='{TemplateBinding SelectionBoxItem}'
                              ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'/>
            <Popup Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False'>
              <Border Background='White' BorderBrush='#DDE1E6' BorderThickness='1' CornerRadius='6' Margin='0,4,0,4'
                      MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'>
                <Border.Effect>
                  <DropShadowEffect BlurRadius='10' ShadowDepth='1' Opacity='0.18' Color='Black'/>
                </Border.Effect>
                <ItemsPresenter/>
              </Border>
            </Popup>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

</ResourceDictionary>";
    }
}
