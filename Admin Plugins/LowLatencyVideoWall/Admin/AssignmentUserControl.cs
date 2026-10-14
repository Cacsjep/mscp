using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LowLatencyVideoWall.Admin.Wpf;
using LowLatencyVideoWall.Config;
using LowLatencyVideoWall.Model;
using Material.Icons;
using VideoOS.Platform;

namespace LowLatencyVideoWall.Admin
{
    /// <summary>
    /// Edits one wall assignment: a Milestone user name and the wall view that user gets when
    /// their Smart Client starts.
    ///
    /// One configuration item is one assignment, and the item name is the user name, because
    /// a rule action picks its target from a dropdown the Management Client builds out of
    /// these items: the user name is the only thing the rule author ever sees.
    ///
    /// The user is picked from the management server's own list, but the field stays typable:
    /// a user who has never logged in, or one behind a trust this server cannot enumerate,
    /// still has to be assignable. Either way what gets stored is the plain name, matched
    /// without case and without a domain prefix at runtime.
    ///
    /// A row with no view selected is legitimate and must stay saveable. It exists so that a
    /// user who has no default wall can still be targeted by a rule that switches their wall
    /// at runtime, since a rule author has nowhere to type a name.
    /// </summary>
    internal sealed class AssignmentUserControl : System.Windows.Controls.UserControl
    {
        private const string NoViewLabel = "(no default view, rule target only)";

        private readonly ResourceDictionary _res = AdminTheme.BuildResources();

        private readonly TextBox _userName = AdminLayout.BareTextBox();
        private readonly ComboBox _viewProfile = new ComboBox();

        private TextBlock _status;

        private Guid _currentId = Guid.Empty;
        private bool _suppressChangeEvents;

        internal event EventHandler ConfigurationChangedByUser;

        internal AssignmentUserControl()
        {
            Resources.MergedDictionaries.Add(_res);
            Background = Brushes.White;
            Content = BuildUi();

            _userName.TextChanged += (s, e) => OnUserChange();
            _viewProfile.SelectionChanged += (s, e) => OnUserChange();

            UpdateStatus();
        }

        // ---- the contract the ItemManager uses -------------------------------------------

        internal string DisplayName => (_userName.Text ?? "").Trim();

        internal void FillContent(Item item)
        {
            if (item == null)
            {
                ClearContent();
                return;
            }

            _suppressChangeEvents = true;
            try
            {
                _currentId = item.FQID?.ObjectId ?? Guid.Empty;

                WallAssignment assignment = null;
                try
                {
                    assignment = WallConfigStore.LoadAssignments()
                        .FirstOrDefault(a => a.Id == _currentId);
                }
                catch (Exception ex)
                {
                    AdminUi.ShowError("Low Latency Video Wall",
                        "This assignment could not be read from the configuration.", ex);
                }

                _userName.Text = assignment?.UserName ?? item.Name ?? "";
                PopulateProfiles(assignment?.ViewProfileId ?? Guid.Empty);
            }
            finally
            {
                _suppressChangeEvents = false;
            }

            UpdateStatus();
        }

        internal void ClearContent()
        {
            _suppressChangeEvents = true;
            try
            {
                _currentId = Guid.Empty;
                _userName.Text = "";
                PopulateProfiles(Guid.Empty);
            }
            finally
            {
                _suppressChangeEvents = false;
            }

            UpdateStatus();
        }

        /// <summary>
        /// Null when the form is valid. The view is deliberately not required: an assignment
        /// with no view is how a user becomes targetable by a rule.
        /// </summary>
        internal string ValidateInput()
        {
            var name = DisplayName;
            if (name.Length == 0)
                return "Please pick or enter the Milestone user name this assignment is for.";

            try
            {
                // Two rows for the same user would make the resolved default view arbitrary,
                // and the rule dropdown would show the same name twice.
                var clash = WallConfigStore.LoadAssignments()
                    .FirstOrDefault(a => a.Id != _currentId && WallConfigStore.UserMatches(a.UserName, name));

                if (clash != null)
                    return $"There is already an assignment for \"{clash.UserName}\". " +
                           "One row per user, the domain prefix is ignored when matching.";
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error(
                    $"Checking assignment user names failed: {AdminUi.Flatten(ex)}");
            }

            return null;
        }

        internal WallAssignment BuildAssignment(Guid id) => new WallAssignment
        {
            Id = id == Guid.Empty ? Guid.NewGuid() : id,
            UserName = DisplayName,
            ViewProfileId = SelectedProfileId()
        };

        /// <summary>Keeps the tree label in step with the user name after a save.</summary>
        internal void UpdateItem(Item item)
        {
            if (item == null) return;
            item.Name = DisplayName;
        }

        internal void SetDisplayName(string name)
        {
            _suppressChangeEvents = true;
            try
            {
                _userName.Text = name ?? "";
            }
            finally
            {
                _suppressChangeEvents = false;
            }
        }

        // ---- layout ----------------------------------------------------------------------

        private FrameworkElement BuildUi()
        {
            var page = new StackPanel();
            page.Children.Add(BuildHeaderCard());
            page.Children.Add(BuildFieldsCard());

            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = page
            };
        }

        private Border BuildHeaderCard()
        {
            var subtitle = new TextBlock
            {
                Text = "The wall view this user sees when their Smart Client starts. " +
                       "User names are matched without case and without a domain prefix.",
                Foreground = AdminTheme.Muted,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };

            var panel = new StackPanel();
            panel.Children.Add(AdminLayout.PageTitle("Wall assignment"));
            panel.Children.Add(subtitle);

            return AdminLayout.Card(panel, 14);
        }

        private Border BuildFieldsCard()
        {
            var browse = AdminTheme.IconButton(_res, MaterialIconKind.AccountSearchOutline,
                "Pick a user from the management server", (s, e) => Guarded(PickUser));
            var userBox = AdminTheme.IconInputBox(MaterialIconKind.AccountOutline, _userName, 340, browse);

            _viewProfile.Style = (Style)_res["FilterComboBoxBorderlessStyle"];
            _viewProfile.ItemContainerStyle = (Style)_res["FilterComboBoxItemStyle"];
            _viewProfile.VerticalAlignment = VerticalAlignment.Center;
            var viewBox = AdminTheme.IconInputBox(MaterialIconKind.ViewGridOutline, _viewProfile, 340);

            var fields = new StackPanel();
            fields.Children.Add(AdminLayout.Labeled(_res, "Milestone user name", userBox));
            var viewField = AdminLayout.Labeled(_res, "Default wall view", viewBox);
            viewField.Margin = new Thickness(0, 12, 14, 0);
            fields.Children.Add(viewField);

            _status = new TextBlock
            {
                Foreground = AdminTheme.Muted,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(1, 16, 0, 0)
            };
            fields.Children.Add(_status);

            var panel = new StackPanel();
            panel.Children.Add(AdminLayout.SectionHeader(_res, "ASSIGNMENT"));
            panel.Children.Add(fields);

            return AdminLayout.Card(panel, 16);
        }

        // ---- user picker ------------------------------------------------------------------

        private void PickUser()
        {
            var picked = UserPickerWindow.Pick(DisplayName);
            if (string.IsNullOrWhiteSpace(picked)) return;

            // Stored exactly as the management server reports it, which is the same plain
            // string the field has always held.
            _userName.Text = picked.Trim();
        }

        // ---- view profile dropdown -------------------------------------------------------

        private sealed class ProfileChoice
        {
            internal Guid Id;
            internal string Label = "";
            public override string ToString() => Label;
        }

        private void PopulateProfiles(Guid selectedId)
        {
            _viewProfile.Items.Clear();
            _viewProfile.Items.Add(new ProfileChoice { Id = Guid.Empty, Label = NoViewLabel });

            List<ViewProfile> profiles;
            try
            {
                profiles = WallConfigStore.LoadViewProfiles();
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error(
                    $"Reading wall views for the assignment dropdown failed: {AdminUi.Flatten(ex)}");
                profiles = new List<ViewProfile>();
            }

            foreach (var profile in profiles)
            {
                _viewProfile.Items.Add(new ProfileChoice
                {
                    Id = profile.Id,
                    Label = string.IsNullOrWhiteSpace(profile.Name) ? profile.Id.ToString() : profile.Name
                });
            }

            // A view that was deleted out from under this assignment stays visible rather than
            // silently turning into "no view" on the next save.
            if (selectedId != Guid.Empty && profiles.All(p => p.Id != selectedId))
            {
                _viewProfile.Items.Add(new ProfileChoice
                {
                    Id = selectedId,
                    Label = $"(missing view {selectedId})"
                });
            }

            var index = 0;
            for (int i = 0; i < _viewProfile.Items.Count; i++)
            {
                if (_viewProfile.Items[i] is ProfileChoice choice && choice.Id == selectedId)
                {
                    index = i;
                    break;
                }
            }

            _viewProfile.SelectedIndex = index;
        }

        private Guid SelectedProfileId()
            => _viewProfile.SelectedItem is ProfileChoice choice ? choice.Id : Guid.Empty;

        // ---- plumbing --------------------------------------------------------------------

        private void UpdateStatus()
        {
            if (DisplayName.Length == 0)
            {
                _status.Foreground = AdminTheme.Muted;
                _status.Text = "Pick the user, or type the name as the Smart Client reports it.";
                return;
            }

            if (SelectedProfileId() == Guid.Empty)
            {
                _status.Foreground = AdminTheme.Amber;
                _status.Text =
                    "No default view. This is allowed on purpose: the row still makes " +
                    $"\"{DisplayName}\" selectable in the wall rule action, " +
                    "so a rule can switch this user's wall at runtime.";
                return;
            }

            _status.Foreground = AdminTheme.Ok;
            _status.Text = $"\"{DisplayName}\" starts on \"{_viewProfile.SelectedItem}\".";
        }

        private void OnUserChange()
        {
            if (_suppressChangeEvents) return;
            UpdateStatus();
            ConfigurationChangedByUser?.Invoke(this, EventArgs.Empty);
        }

        private void Guarded(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                AdminUi.ShowError("Low Latency Video Wall",
                    "The assignment editor hit an unexpected error.", ex);
            }
        }
    }
}
