using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Lug
{
    internal sealed class MotionButton : Button
    {
        internal static bool AnimationsEnabled = true;
        private Timer animation;
        private long started;
        private float from, level, target;
        private bool hovered, mouseDown, spaceDown;

        internal MotionButton()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            UseVisualStyleBackColor = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hovered = true;
            Animate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = false;
            Animate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            mouseDown = true;
            Animate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            mouseDown = false;
            Animate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (Capture || !mouseDown) return;
            mouseDown = false;
            Animate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode != Keys.Space || spaceDown) return;
            spaceDown = true;
            Animate();
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                spaceDown = false;
                Animate();
            }
            base.OnKeyUp(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            spaceDown = false;
            Animate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            if (!Enabled) mouseDown = spaceDown = false;
            Animate();
        }

        private void Animate()
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            float next = !Enabled ? 0 : spaceDown || (mouseDown && hovered) ? 2 : hovered ? 1 : 0;
            if (!AnimationsEnabled || !SystemInformation.UIEffectsEnabled || SystemInformation.HighContrast)
            {
                if (animation != null) animation.Stop();
                level = target = next;
                Invalidate();
                return;
            }
            if (next == target) return;
            from = level;
            target = next;
            started = Stopwatch.GetTimestamp();
            if (animation == null)
            {
                animation = new Timer { Interval = 15 };
                animation.Tick += Advance;
            }
            animation.Start();
        }

        private void Advance(object sender, EventArgs e)
        {
            var milliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
            double progress = Math.Min(1, milliseconds / (target > 1 ? 72 : 114));
            if (!AnimationsEnabled || !Visible) progress = 1;
            level = from + (target - from) * (float)(1 - Math.Pow(1 - progress, 3));
            Invalidate();
            if (progress == 1) animation.Stop();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (SystemInformation.HighContrast)
            {
                base.OnPaint(e);
                return;
            }
            if (Width < 4 || Height < 4) return;

            Color surround = Parent == null ? Theme.Bg : Parent.BackColor;
            Color idle = BackColor;
            Color hover = FlatAppearance.MouseOverBackColor;
            Color pressed = Blend(hover, Color.Black, 0.20f);
            var fill = !Enabled ? Blend(idle, surround, 0.70f) :
                level <= 1 ? Blend(idle, hover, level) : Blend(hover, pressed, level - 1);

            e.Graphics.Clear(surround);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bounds = RoundedOutline(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1)))
            using (var brush = new SolidBrush(fill))
            {
                e.Graphics.FillPath(brush, bounds);
                if (FlatAppearance.BorderSize > 0)
                {
                    var border = FlatAppearance.BorderColor;
                    using (var pen = new Pen(Blend(border, fill, !Enabled ? 0.65f : 0.25f * Math.Min(1, level))))
                        e.Graphics.DrawPath(pen, bounds);
                }
            }
            if (Focused && ShowFocusCues && Enabled && Width > 6 && Height > 6)
            {
                using (var outline = RoundedOutline(new RectangleF(3, 3, Width - 6, Height - 6)))
                using (var ring = new Pen(ForeColor))
                    e.Graphics.DrawPath(ring, outline);
            }
            int shift = level > 1.5 ? 1 : 0;
            var textBounds = Rectangle.FromLTRB(Padding.Left + 3, shift, Width - Padding.Right - 3, Height + shift);
            var horizontal = TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? ForeColor : Blend(Theme.Muted, surround, 0.35f),
                horizontal | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        private static GraphicsPath RoundedOutline(RectangleF box)
        {
            var path = new GraphicsPath();
            float corner = Math.Min(10f, Math.Min(box.Width, box.Height));
            path.AddArc(box.Left, box.Top, corner, corner, 180, 90);
            path.AddArc(box.Right - corner, box.Top, corner, corner, 270, 90);
            path.AddArc(box.Right - corner, box.Bottom - corner, corner, corner, 0, 90);
            path.AddArc(box.Left, box.Bottom - corner, corner, corner, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Color Blend(Color a, Color b, float fraction)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * fraction),
                (int)(a.G + (b.G - a.G) * fraction),
                (int)(a.B + (b.B - a.B) * fraction));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && animation != null) animation.Dispose();
            base.Dispose(disposing);
        }
    }
}
