using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LowLatencyVideoWall.Admin.Wpf;
using Material.Icons;

namespace LowLatencyVideoWall.Admin
{
    /// <summary>
    /// Small shared helpers for the admin pages: the one way background code is allowed back
    /// onto the UI thread, modal windows that are still modal to the WinForms Management
    /// Client, and the two dialogs that report what happened.
    ///
    /// Everything visual lives in AdminTheme; this file holds no colours of its own.
    /// </summary>
    internal static class AdminUi
    {
        /// <summary>
        /// The only way background work touches the UI. Swallows the race where the node is
        /// closed between the post and the pump, which is routine when an admin clicks
        /// through the tree while a 200 camera read is still running.
        /// </summary>
        internal static void Post(DispatcherObject target, Action action)
        {
            if (target == null || action == null) return;

            var dispatcher = target.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            try
            {
                dispatcher.BeginInvoke((Action)(() =>
                {
                    // Unlike a WinForms BeginInvoke, a queued dispatcher operation still runs
                    // after the page is gone, so the handler itself has to be the safety net:
                    // a throw here would surface as a Management Client crash dialog.
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        LowLatencyVideoWallDefinition.Log.Error(
                            $"An admin page update was dropped: {Flatten(ex)}");
                    }
                }));
            }
            catch
            {
            }
        }

        internal static string Flatten(Exception ex)
        {
            if (ex == null) return "";

            if (ex is AggregateException aggregate)
            {
                return string.Join("; ", aggregate.Flatten().InnerExceptions
                    .Select(e => e.Message)
                    .Where(m => !string.IsNullOrWhiteSpace(m)));
            }

            var sb = new StringBuilder(ex.Message);
            var inner = ex.InnerException;
            while (inner != null)
            {
                sb.Append(" -> ").Append(inner.Message);
                inner = inner.InnerException;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Shows a window modal to the Management Client. A WPF window hosted through an
        /// ElementHost has no WPF owner to inherit, so the owning HWND is handed over
        /// explicitly: without it the dialog is modal to nothing and the admin can click
        /// straight back into the tree behind it.
        /// </summary>
        internal static bool? ShowModal(Window window)
        {
            if (window == null) return null;

            try
            {
                // Centre on the screen rather than on the owner: WPF cannot measure a WinForms
                // owner, so CenterOwner would park the dialog in the top left corner.
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

                var owner = System.Windows.Forms.Form.ActiveForm?.Handle
                            ?? System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (owner != IntPtr.Zero)
                    new WindowInteropHelper(window).Owner = owner;
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Owning a dialog failed: {ex.Message}");
            }

            return window.ShowDialog();
        }

        /// <summary>
        /// Reports a failure to the admin and to the log. Nothing in the admin UI is allowed
        /// to let an exception reach the Management Client, so every catch ends up here.
        /// </summary>
        internal static void ShowError(string title, string message, Exception ex)
        {
            try
            {
                LowLatencyVideoWallDefinition.Log.Error($"{title}: {message} {(ex == null ? "" : ex.ToString())}");
            }
            catch
            {
            }

            try
            {
                var text = ex == null ? message : message + Environment.NewLine + Environment.NewLine + Flatten(ex);
                MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
            }
        }

        /// <summary>A page that failed to build, so the node still shows something readable.</summary>
        internal static System.Windows.Controls.UserControl ErrorPage(string message, Exception ex)
        {
            var res = AdminTheme.BuildResources();

            var text = new TextBox
            {
                Text = message + Environment.NewLine + Environment.NewLine + Flatten(ex),
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = AdminTheme.Text,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12
            };

            var panel = new DockPanel { LastChildFill = true };
            var header = new TextBlock
            {
                Text = "This page could not be built",
                Style = (Style)res["SectionHeaderStyle"]
            };
            DockPanel.SetDock(header, Dock.Top);
            panel.Children.Add(header);
            panel.Children.Add(text);

            var page = new System.Windows.Controls.UserControl { Background = Brushes.White };
            page.Resources.MergedDictionaries.Add(res);
            page.Content = AdminLayout.Card(panel, 16);
            return page;
        }

        /// <summary>
        /// The result summary shown after a bulk change. A message box is useless for 200
        /// lines of old value and new value, so this is a scrollable, copyable list.
        /// </summary>
        internal static void ShowResultReport(string title, string summary, IEnumerable<string> lines)
        {
            try
            {
                var res = AdminTheme.BuildResources();

                var body = new TextBox
                {
                    Text = string.Join(Environment.NewLine, lines ?? Enumerable.Empty<string>()),
                    IsReadOnly = true,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Foreground = AdminTheme.Text,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.NoWrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                var summaryText = new TextBlock
                {
                    Text = summary ?? "",
                    Foreground = AdminTheme.Text,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                };

                var close = AdminTheme.AccentButton(res, MaterialIconKind.Check, "Close");
                close.HorizontalAlignment = HorizontalAlignment.Right;
                close.Margin = new Thickness(0, 12, 0, 0);

                var panel = new DockPanel { LastChildFill = true };
                DockPanel.SetDock(summaryText, Dock.Top);
                DockPanel.SetDock(close, Dock.Bottom);
                panel.Children.Add(summaryText);
                panel.Children.Add(close);
                panel.Children.Add(body);

                var window = new Window
                {
                    Title = title,
                    Width = 860,
                    Height = 560,
                    Background = Brushes.White,
                    ShowInTaskbar = false,
                    Content = AdminLayout.Card(panel, 16)
                };
                window.Resources.MergedDictionaries.Add(res);
                close.Click += (s, e) => window.Close();

                ShowModal(window);
            }
            catch (Exception ex)
            {
                ShowError(title, "The result summary could not be shown. The log has the detail.", ex);
            }
        }
    }
}
