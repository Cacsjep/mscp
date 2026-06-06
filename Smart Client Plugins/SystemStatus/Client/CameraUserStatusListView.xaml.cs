using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace SystemStatus.Client
{
    /// <summary>
    /// Stateless renderer for the two Folder &amp; Role lists. Hosted by both the live view item and the
    /// configuration preview, so what an operator sees while editing is exactly what the tile shows.
    /// Section visibility, order, spacer and the empty overlay are driven by <see cref="Render"/>.
    /// </summary>
    public partial class CameraUserStatusListView : UserControl
    {
        public CameraUserStatusListView()
        {
            InitializeComponent();
        }

        public void Render(CameraUserStatusSettings settings,
                           IReadOnlyList<CameraUserStatusDisplayRow> folders,
                           IReadOnlyList<CameraUserStatusDisplayRow> roles)
        {
            // List mode = stacked rows; dashboard mode = wrapping rectangular cards.
            var template = (DataTemplate)Resources[settings.Dashboard ? "StatusCard" : "StatusRow"];
            var panel = (ItemsPanelTemplate)Resources[settings.Dashboard ? "WrapPanelTemplate" : "StackPanelTemplate"];
            folderList.ItemTemplate = roleList.ItemTemplate = template;
            folderList.ItemsPanel = roleList.ItemsPanel = panel;

            folderList.ItemsSource = folders;
            roleList.ItemsSource = roles;

            bool fVis = settings.ShowFolders && folders != null && folders.Count > 0;
            bool rVis = settings.ShowRoles && roles != null && roles.Count > 0;

            folderSection.Visibility = fVis ? Visibility.Visible : Visibility.Collapsed;
            roleSection.Visibility = rVis ? Visibility.Visible : Visibility.Collapsed;

            // Place the visible section(s): first in the top row, second (if any) in the bottom row.
            var order = new List<Border>();
            if (settings.FoldersFirst)
            {
                if (fVis) order.Add(folderSection);
                if (rVis) order.Add(roleSection);
            }
            else
            {
                if (rVis) order.Add(roleSection);
                if (fVis) order.Add(folderSection);
            }

            if (order.Count >= 1) Grid.SetRow(order[0], 0);
            if (order.Count >= 2) Grid.SetRow(order[1], 2);

            bool both = order.Count == 2;
            spacer.Height = both ? new GridLength(12) : new GridLength(0);
            row2.Height = both ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

            emptyLabel.Visibility = order.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Shows the centered overlay text (e.g. "Loading…" / "No data.") and clears the lists.</summary>
        public void ShowMessage(string message)
        {
            folderList.ItemsSource = null;
            roleList.ItemsSource = null;
            folderSection.Visibility = Visibility.Collapsed;
            roleSection.Visibility = Visibility.Collapsed;
            emptyLabel.Text = message;
            emptyLabel.Visibility = Visibility.Visible;
        }
    }
}
