using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Material.Icons;
using Material.Icons.WPF;
using VideoOS.Platform;
using VideoOS.Platform.ConfigurationItems;

namespace LowLatencyVideoWall.Admin.Wpf
{
    /// <summary>
    /// Picks the Milestone user an assignment is for. Milestone's own ItemPickerWpfWindow has
    /// no user kind, so this is a themed list of its own: the basic users first, then the
    /// Windows and AD users the roles know about, each group labelled because a site usually
    /// has one of each with the same person behind it.
    ///
    /// The configuration read is a server roundtrip, so it runs on a task and the list fills
    /// in behind a loading row.
    /// </summary>
    internal sealed class UserPickerWindow : Window
    {
        private const string BasicGroup = "Basic users";
        private const string WindowsGroup = "Windows and AD users";

        private readonly ResourceDictionary _res = AdminTheme.BuildResources();
        private readonly ResourceDictionary _local = (ResourceDictionary)XamlReader.Parse(LocalXaml);

        private readonly ObservableCollection<UserChoice> _users = new ObservableCollection<UserChoice>();
        private ListCollectionView _view;

        private readonly ListBox _list = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.White,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        private TextBox _search;
        private TextBlock _status;
        private ProgressBar _progress;
        private Button _select;

        /// <summary>The name the admin picked, or null when the dialog was cancelled.</summary>
        internal string SelectedUser { get; private set; }

        /// <summary>Opens the picker and returns the chosen user name, or null.</summary>
        internal static string Pick(string current)
        {
            var window = new UserPickerWindow(current);
            return AdminUi.ShowModal(window) == true ? window.SelectedUser : null;
        }

        private UserPickerWindow(string current)
        {
            Title = "Select user";
            Width = 560;
            Height = 600;
            Background = Brushes.White;
            ShowInTaskbar = false;

            Resources.MergedDictionaries.Add(_res);
            Resources.MergedDictionaries.Add(_local);

            Content = BuildUi();
            StartLoad(current);
        }

        // ---- layout ----------------------------------------------------------------------

        private FrameworkElement BuildUi()
        {
            var panel = new DockPanel { LastChildFill = true };

            var header = AdminLayout.SectionHeader(_res, "USERS");
            DockPanel.SetDock(header, Dock.Top);
            panel.Children.Add(header);

            var searchBox = AdminTheme.SearchBox("Filter by user name", out _search);
            searchBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            searchBox.Margin = new Thickness(0, 0, 0, 8);
            _search.TextChanged += (s, e) => Refilter();
            DockPanel.SetDock(searchBox, Dock.Top);
            panel.Children.Add(searchBox);

            _progress = new ProgressBar
            {
                IsIndeterminate = true,
                Height = 3,
                Width = 110,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = AdminTheme.Accent
            };
            _status = new TextBlock
            {
                Text = "Reading users from the management server...",
                Foreground = AdminTheme.Muted,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            var statusRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            statusRow.Children.Add(_progress);
            statusRow.Children.Add(_status);
            DockPanel.SetDock(statusRow, Dock.Top);
            panel.Children.Add(statusRow);

            var buttons = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 12, 0, 0) };
            _select = AdminTheme.AccentButton(_res, MaterialIconKind.Check, "Select");
            _select.IsEnabled = false;
            _select.Margin = new Thickness(8, 0, 0, 0);
            _select.Click += (s, e) => Accept();
            var cancel = AdminTheme.SecondaryButton(_res, MaterialIconKind.CloseCircleOutline, "Cancel");
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            DockPanel.SetDock(cancel, Dock.Right);
            DockPanel.SetDock(_select, Dock.Right);
            buttons.Children.Add(cancel);
            buttons.Children.Add(_select);
            DockPanel.SetDock(buttons, Dock.Bottom);
            panel.Children.Add(buttons);

            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            _list.ItemContainerStyle = (Style)_local["UserItemStyle"];
            _list.ItemTemplate = BuildItemTemplate();
            _list.GroupStyle.Add(new GroupStyle { HeaderTemplate = (DataTemplate)_local["GroupHeaderTemplate"] });
            _list.SelectionChanged += (s, e) => _select.IsEnabled = _list.SelectedItem is UserChoice;
            _list.MouseDoubleClick += (s, e) => Accept();

            _view = new ListCollectionView(_users) { Filter = o => Matches(o as UserChoice) };
            _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(UserChoice.Group)));
            _list.ItemsSource = _view;

            var listFrame = new Border
            {
                BorderBrush = (Brush)_local["FrameBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Background = Brushes.White,
                Child = _list
            };
            panel.Children.Add(listFrame);

            // Enter picks, Escape closes: a picker gets used with the keyboard as often as
            // with the mouse once a site has a few hundred users.
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) Accept();
                else if (e.Key == Key.Escape) { DialogResult = false; Close(); }
            };

            return AdminLayout.Card(panel, 16);
        }

        private DataTemplate BuildItemTemplate()
        {
            var row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var icon = new FrameworkElementFactory(typeof(MaterialIcon));
            icon.SetBinding(MaterialIcon.KindProperty, new Binding(nameof(UserChoice.Icon)));
            icon.SetValue(FrameworkElement.WidthProperty, 16.0);
            icon.SetValue(FrameworkElement.HeightProperty, 16.0);
            icon.SetValue(Control.ForegroundProperty, AdminTheme.IconGrey);
            icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 9, 0));
            icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding(nameof(UserChoice.Name)));
            text.SetValue(TextBlock.FontSizeProperty, 13.0);
            text.SetValue(Control.ForegroundProperty, AdminTheme.Text);
            text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);

            row.AppendChild(icon);
            row.AppendChild(text);

            return new DataTemplate { VisualTree = row };
        }

        // ---- loading ---------------------------------------------------------------------

        private void StartLoad(string current)
        {
            Task.Run(() =>
            {
                var found = ReadUsers();
                AdminUi.Post(this, () => Fill(found, current));
            });
        }

        /// <summary>
        /// Basic users first, then every user a role knows about, which is where the Windows
        /// and AD accounts live. A failure on either half is logged and the other half still
        /// shows: a picker with some of the users beats a dialog with none.
        /// </summary>
        private static List<UserChoice> ReadUsers()
        {
            var result = new List<UserChoice>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var management = new ManagementServer(EnvironmentManager.Instance.MasterSite);

                try
                {
                    foreach (var basicUser in management.BasicUserFolder.BasicUsers)
                    {
                        if (string.IsNullOrWhiteSpace(basicUser?.Name)) continue;
                        if (!seen.Add(basicUser.Name)) continue;
                        result.Add(new UserChoice
                        {
                            Name = basicUser.Name,
                            Group = BasicGroup,
                            Icon = MaterialIconKind.AccountKeyOutline
                        });
                    }
                }
                catch (Exception ex)
                {
                    LowLatencyVideoWallDefinition.Log.Error($"Reading basic users failed: {ex.Message}");
                }

                try
                {
                    foreach (var role in management.RoleFolder.Roles)
                    {
                        try
                        {
                            foreach (var user in role.UserFolder.Users)
                            {
                                if (string.IsNullOrWhiteSpace(user?.Name)) continue;
                                if (!seen.Add(user.Name)) continue;
                                result.Add(new UserChoice
                                {
                                    Name = user.Name,
                                    Group = WindowsGroup,
                                    Icon = MaterialIconKind.MicrosoftWindows
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            LowLatencyVideoWallDefinition.Log.Error(
                                $"Reading users of role '{role?.Name}' failed: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LowLatencyVideoWallDefinition.Log.Error($"Reading roles failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error(
                    $"Reading the user list failed: {AdminUi.Flatten(ex)}");
            }

            // Sorted inside each group, and the groups keep the order they were added in
            // because that is how a property group description orders them.
            return result
                .OrderBy(u => u.Group == BasicGroup ? 0 : 1)
                .ThenBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private void Fill(List<UserChoice> users, string current)
        {
            _users.Clear();
            foreach (var user in users) _users.Add(user);

            _progress.Visibility = Visibility.Collapsed;
            _status.Text = users.Count == 0
                ? "No users could be read. Type the name on the page instead."
                : $"{users.Count} user(s).";

            if (!string.IsNullOrWhiteSpace(current))
            {
                var match = _users.FirstOrDefault(
                    u => string.Equals(u.Name, current.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    _list.SelectedItem = match;
                    _list.ScrollIntoView(match);
                }
            }

            _view.Refresh();
        }

        // ---- plumbing --------------------------------------------------------------------

        private void Refilter()
        {
            _view?.Refresh();
        }

        private bool Matches(UserChoice user)
        {
            if (user == null) return false;
            var needle = (_search?.Text ?? "").Trim();
            if (needle.Length == 0) return true;
            return (user.Name ?? "").IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private void Accept()
        {
            if (!(_list.SelectedItem is UserChoice choice)) return;
            SelectedUser = choice.Name;
            DialogResult = true;
            Close();
        }

        /// <summary>One selectable user.</summary>
        private sealed class UserChoice
        {
            public string Name { get; set; } = "";
            public string Group { get; set; } = "";
            public MaterialIconKind Icon { get; set; }
        }

        private const string LocalXaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <SolidColorBrush x:Key='FrameBrush' Color='#DDE1E6'/>

  <Style x:Key='UserItemStyle' TargetType='ListBoxItem'>
    <Setter Property='Padding' Value='10,7'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ListBoxItem'>
          <Border x:Name='bd' Background='Transparent' CornerRadius='6' Margin='3,1'
                  Padding='{TemplateBinding Padding}'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='bd' Property='Background' Value='#F1F6FB'/>
            </Trigger>
            <Trigger Property='IsSelected' Value='True'>
              <Setter TargetName='bd' Property='Background' Value='#E4F0FC'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <DataTemplate x:Key='GroupHeaderTemplate'>
    <Border Background='#F7F8FA' BorderBrush='#EDEFF2' BorderThickness='0,0,0,1' Padding='10,6'>
      <TextBlock Text='{Binding Name}' Foreground='#8A9099' FontSize='11' FontWeight='SemiBold'/>
    </Border>
  </DataTemplate>

</ResourceDictionary>";
    }
}
