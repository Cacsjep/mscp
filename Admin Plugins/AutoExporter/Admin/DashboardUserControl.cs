using System;
using System.Drawing;
using System.Windows.Forms;
using VideoOS.Platform;

namespace AutoExporter.Admin
{
    /// <summary>
    /// Single admin page that hosts both the per-job storage Status table (left)
    /// and the run Executions table (right) in one split view, so the operator does
    /// not have to switch between two nodes. Each side is the existing self-contained
    /// control; this wrapper just lays them out and forwards the lifecycle calls.
    /// </summary>
    public class DashboardUserControl : UserControl
    {
        private readonly StatusUserControl _status = new StatusUserControl();
        private readonly ExecutionsUserControl _executions = new ExecutionsUserControl();
        private readonly SplitContainer _split;

        public DashboardUserControl()
        {
            // Give the control a real width BEFORE the split is added so the host's
            // initial layout has room. Min sizes are left at the framework default
            // (small): a large Panel1MinSize would exceed the default SplitterDistance
            // and crash layout with "SplitterDistance must be between Panel1MinSize
            // and Width - Panel2MinSize". We position the splitter ourselves on resize.
            Size = new Size(1100, 560);

            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal   // jobs on top, executions below
            };

            _split.Panel1.Controls.Add(Wrap("Jobs and storage status", _status));
            _split.Panel2.Controls.Add(Wrap("Executions", _executions));

            Controls.Add(_split);
            ApplySplitterDistance();
        }

        // Puts the splitter at ~42% (jobs get the top ~42%, executions the rest), with
        // safe bounds. Wrapped because SplitContainer throws if the computed distance
        // ever falls outside its allowed range, which can happen at tiny transient
        // sizes during host activation. Uses Height because the split is horizontal.
        private void ApplySplitterDistance()
        {
            try
            {
                int extent = _split.Orientation == Orientation.Horizontal ? _split.Height : _split.Width;
                if (extent <= 0) return;
                int min = _split.Panel1MinSize;
                int max = extent - _split.Panel2MinSize;
                if (max <= min) return;
                int target = (int)(extent * 0.42);
                _split.SplitterDistance = Math.Max(min, Math.Min(target, max));
            }
            catch { /* transient size during activation; retried on next resize */ }
        }

        // Each side gets a small header label above the hosted control.
        private static Control Wrap(string title, Control content)
        {
            var panel = new Panel { Dock = DockStyle.Fill };
            content.Dock = DockStyle.Fill;
            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Text = title,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                Padding = new Padding(4, 4, 0, 0),
                ForeColor = Color.FromArgb(60, 60, 60)
            };
            panel.Controls.Add(content);
            panel.Controls.Add(header);
            return panel;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplySplitterDistance();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ApplySplitterDistance();
        }

        public void FillContent(Item item)
        {
            _status.FillContent(item);
            _executions.FillContent(item);
        }

        public void ClearContent()
        {
            _status.ClearContent();
            _executions.ClearContent();
        }

        internal void Shutdown()
        {
            try { _status.Shutdown(); } catch { }
            try { _executions.Shutdown(); } catch { }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            Shutdown();
            base.OnHandleDestroyed(e);
        }
    }
}
