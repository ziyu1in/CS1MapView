
using CS2MapView.Drawing;
using CS2MapView.Theme;
using CS2MapView.Util;
using Gfw.Common;
using SkiaSharp;
using SD = System.Drawing;

namespace CS2MapView.Form
{
    public partial class SaveImageForm : System.Windows.Forms.Form
    {
        public required MainForm AppRoot { get; init; }

        private SKBitmap? MapImage;
        private BetaOutputHost? PreviewHost;

        private SKRect PrintArea;
        private readonly bool _areaSelectionOnly;

        private const int MinImageWidth = 32;
        private const int MaxImageWidth = 16384;
        private const int DefaultImageWidth = 2048;
        private const int PrintAreaMinWidth = 20;
        private const int PrintAreaMinHeight = 20;

        public SaveImageForm(bool areaSelectionOnly = false)
        {
            _areaSelectionOnly = areaSelectionOnly;
            InitializeComponent();
            Disposed += (_, _) => DisposePreview();
            if (areaSelectionOnly)
            {
                Text = "Choose print/export area";
                foreach (var control in new Control[] { label5, label6, label7, label8, OutputImageSizeTextbox, OutputImageSizeLabel,
                    OutputFileFormatCombobox, OutputPathTextbox, OutputFileNameTextbox, OpenFileDialogButton }) control.Visible = false;
                SaveButton.Text = "Use selected area";
                SaveButton.Width = 190; SaveButton.Location = new Point(548, 82);
                ClientSize = new Size(ClientSize.Width, 540);
            }
        }

        internal void CommitSelectedArea()
        {
            AppRoot.SetOutputArea(GetRotatedCrop());
        }

        private async void SaveImageForm_Load(object sender, EventArgs e)
        {
            try { await InitializePreviewAsync(); }
            catch (Exception ex) { if (!IsDisposed && !Disposing) MessageBox.Show(ex.Message, "Preview failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        internal Task InitializeForChecks(Action<BetaOutputHost>? whenBuilt = null) => InitializePreviewAsync(whenBuilt);
        internal SKRect PreviewAreaForChecks => PrintArea;
        internal SKRect PreviewCropForChecks => GetRotatedCrop();
        private SKRect GetRotatedCrop()
        {
            if (PreviewHost == null) throw new InvalidOperationException("Preview is not ready.");
            var view = PreviewHost.Context.ViewContext;
            var scale = view.ViewScaleFromWorld;
            return SKRect.Create(view.ViewLeftTop.x + PrintArea.Left / scale, view.ViewLeftTop.y + PrintArea.Top / scale, PrintArea.Width / scale, PrintArea.Height / scale);
        }
        private async Task InitializePreviewAsync(Action<BetaOutputHost>? whenBuilt = null)
        {
            if (IsDisposed || Disposing) return;
            var rect = new SKRect(0, 0, SubSkiaControl.DisplayRectangle.Width, SubSkiaControl.DisplayRectangle.Height);
            if (AppRoot.MapData != null)
            {
                var rotatedWorld = SKPathEx.GetTransformedRectBound(AppRoot.Context.ViewContext.WorldRect, SKMatrix.CreateRotation(AppRoot.Context.ViewContext.Angle));
                BetaOutputHost? host = await BetaOutput.FromScreenAsync(AppRoot, rotatedWorld, (int)rect.Width, (int)rect.Height);
                SKBitmap? image = null;
                try
                {
                    whenBuilt?.Invoke(host);
                    if (IsDisposed || Disposing) return;
                    image = BetaOutput.DrawBitmap(host, (int)rect.Width, (int)rect.Height);
                    if (IsDisposed || Disposing) return;
                    DisposePreview();
                    PreviewHost = host; MapImage = image;
                    host = null; image = null;
                }
                finally { image?.Dispose(); host?.Dispose(); }
            }
            if (IsDisposed || Disposing) return;
            PrintArea = rect;
            if (PreviewHost != null && AppRoot.OutputArea is { } selected)
            {
                var view = PreviewHost.Context.ViewContext;
                var scale = view.ViewScaleFromWorld;
                PrintArea = new SKRect((selected.Left - view.ViewLeftTop.x) * scale,
                    (selected.Top - view.ViewLeftTop.y) * scale,
                    (selected.Right - view.ViewLeftTop.x) * scale,
                    (selected.Bottom - view.ViewLeftTop.y) * scale);
            }
            var size = AppRoot.Context.UserSettings.OutputImageSize;
            if (size == 0)
            {
                size = DefaultImageWidth;
            }
            size = Math.Clamp(size, MinImageWidth, MaxImageWidth);
            OutputImageSizeTextbox.Text = $"{size}";
            RefreshPrintAreaTextboxes();

            OutputFileFormatCombobox.Items.AddRange(["png", "bmp", "jpeg", "jpg", "svg", "svgz"]);
            OutputFileFormatCombobox.Text = AppRoot.Context.UserSettings.OutputImageFormat ?? "png";
            if (AppRoot.Context.UserSettings.SaveImageDir is not null && Directory.Exists(AppRoot.Context.UserSettings.SaveImageDir))
            {
                OutputPathTextbox.Text = AppRoot.Context.UserSettings.SaveImageDir;
            }
            else
            {
                OutputPathTextbox.Text = AppContext.BaseDirectory;
            }
            string? mapName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(AppRoot.MapData?.FileName))?? AppRoot.MapData?.MapName;
            OutputFileNameTextbox.Text = $"{mapName}.png";
        }

        private void RefreshPrintAreaTextboxes()
        {
            PrintAreaLeftTextbox.Text = $"{PrintArea.Left}";
            PrintAreaTopTextbox.Text = $"{PrintArea.Top}";
            PrintAreaRightTextbox.Text = $"{PrintArea.Right}";
            PrintAreaBottomTextbox.Text = $"{PrintArea.Bottom}";

            var outputSize = OutputImageSizeTextbox.Text.ParseIntInvariant();
            if (outputSize.HasValue)
            {
                float rate = PrintArea.Height / PrintArea.Width;
                if (rate >= 1f)
                {
                    OutputImageSizeLabel.Text = $"{(int)(outputSize.Value / rate)}x{outputSize.Value}";
                }
                else
                {
                    OutputImageSizeLabel.Text = $"{outputSize.Value}x{(int)(outputSize.Value * rate)}";
                }


            }
        }

        private void SubSkiaControl_PaintSurface(object sender, SkiaSharp.Views.Desktop.SKPaintGLSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            //  var rect = e.Info.Rect;
            canvas.Clear();
            if (MapImage is not null)
            {
                canvas.DrawBitmap(MapImage, 0, 0);
            }
            canvas.DrawRect(PrintArea, SKPaintCache.Instance.GetStroke(new SKPaintCache.StrokeKey(2f, SKColors.Red, StrokeType.Flat)));
            canvas.DrawRect(PrintArea, SKPaintCache.Instance.GetFill(new SKColor(100, 255, 100, 80)));

        }

        private void SaveImageForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            DisposePreview();
        }

        private void DisposePreview()
        {
            MapImage?.Dispose(); MapImage = null;
            PreviewHost?.Dispose(); PreviewHost = null;
        }
        #region printarea
        private (MoveType moveType, Cursor cursor) CalcCursor(Point mousePosition)
        {
            const int marginOuter = 2;
            const int marginInner = 5;
            if (mousePosition.X >= PrintArea.Left - marginOuter && mousePosition.X <= PrintArea.Left + marginInner)
            {
                if (mousePosition.Y >= PrintArea.Top - marginOuter && mousePosition.Y <= PrintArea.Top + marginInner)
                {
                    return (MoveType.LeftTop, Cursors.SizeNWSE);
                }
                else if (mousePosition.Y >= PrintArea.Bottom - marginInner && mousePosition.Y <= PrintArea.Bottom + marginOuter)
                {
                    return (MoveType.LeftBottom, Cursors.SizeNESW);
                }
                else
                {
                    return (MoveType.Left, Cursors.SizeWE);
                }
            }
            else if (mousePosition.X >= PrintArea.Right - marginOuter && mousePosition.X <= PrintArea.Right + marginInner)
            {
                if (mousePosition.Y >= PrintArea.Top - marginOuter && mousePosition.Y <= PrintArea.Top + marginInner)
                {
                    return (MoveType.RightTop, Cursors.SizeNESW);
                }
                else if (mousePosition.Y >= PrintArea.Bottom - marginInner && mousePosition.Y <= PrintArea.Bottom + marginOuter)
                {
                    return (MoveType.RightBottom, Cursors.SizeNWSE);
                }
                else
                {
                    return (MoveType.Right, Cursors.SizeWE);
                }
            }
            else if (mousePosition.Y >= PrintArea.Top - marginOuter && mousePosition.Y <= PrintArea.Top + marginInner)
            {
                return (MoveType.Top, Cursors.SizeNS);
            }
            else if (mousePosition.Y >= PrintArea.Bottom - marginInner && mousePosition.Y <= PrintArea.Bottom + marginOuter)
            {
                return (MoveType.Bottom, Cursors.SizeNS);
            }
            else if (mousePosition.X >= PrintArea.Left && mousePosition.X <= PrintArea.Right && mousePosition.Y >= PrintArea.Top && mousePosition.Y <= PrintArea.Bottom)
            {
                return (MoveType.Move, Cursors.Hand);
            }
            else
            {
                return (MoveType.None, Cursors.Default);
            }
        }

        private void SubSkiaControl_MouseMove(object sender, MouseEventArgs e)
        {

            MouseUpOrMove(e);


            Cursor = CalcCursor(e.Location).cursor;



        }

        private void MouseUpOrMove(MouseEventArgs e)
        {

            if (!LastMouseMovePoint.HasValue)
            {
                return;
            }
            int dx = e.X - LastMouseMovePoint.Value.X;
            int dy = e.Y - LastMouseMovePoint.Value.Y;
            var rect = PrintArea;
            if (CurrentMoveType.HasFlag(MoveType.Left))
            {
                rect.Left += dx;
                if (rect.Width < PrintAreaMinWidth)
                {
                    rect.Left = rect.Right - PrintAreaMinWidth;
                }
            }
            if (CurrentMoveType.HasFlag(MoveType.Right))
            {
                rect.Right += dx;
                if (rect.Width < PrintAreaMinWidth)
                {
                    rect.Right = rect.Left + PrintAreaMinWidth;
                }
            }
            if (CurrentMoveType.HasFlag(MoveType.Top))
            {
                rect.Top += dy;
                if (rect.Height < PrintAreaMinHeight)
                {
                    rect.Top = rect.Bottom - PrintAreaMinHeight;
                }
            }
            if (CurrentMoveType.HasFlag(MoveType.Bottom))
            {
                rect.Bottom += dy;
                if (rect.Height < PrintAreaMinHeight)
                {
                    rect.Bottom = rect.Top + PrintAreaMinHeight;
                }
            }

            if (rect.Left < 0)
            {
                rect.Left = 0;
                if (rect.Width < PrintAreaMinWidth)
                {
                    rect.Right = PrintAreaMinWidth;
                }
            }
            if (rect.Right > SubSkiaControl.DisplayRectangle.Width)
            {
                rect.Right = SubSkiaControl.DisplayRectangle.Width;
                if (rect.Width < PrintAreaMinWidth)
                {
                    rect.Left = rect.Right - PrintAreaMinWidth;
                }
            }
            if (rect.Top < 0)
            {
                rect.Top = 0;
                if (rect.Height < PrintAreaMinHeight)
                {
                    rect.Bottom = PrintAreaMinHeight;
                }
            }
            if (rect.Bottom > SubSkiaControl.DisplayRectangle.Height)
            {
                rect.Bottom = SubSkiaControl.DisplayRectangle.Height;
                if (rect.Height < PrintAreaMinHeight)
                {
                    rect.Top = rect.Bottom - PrintAreaMinHeight;
                }
            }


            PrintArea = rect;
            RefreshPrintAreaTextboxes();
            LastMouseMovePoint = e.Location;
            SubSkiaControl.Invalidate();
        }

        private Point? LastMouseMovePoint;
        private MoveType CurrentMoveType = MoveType.None;
        private enum MoveType
        {
            None = 0x0,
            Left = 0x1,
            Right = 0x2,
            Top = 0x4,
            Bottom = 0x8,
            LeftTop = Left | Top,
            RightTop = Right | Top,
            LeftBottom = Left | Bottom,
            RightBottom = Right | Bottom,
            Move = Left | Right | Top | Bottom
        }

        private void SubSkiaControl_MouseDown(object sender, MouseEventArgs e)
        {
            LastMouseMovePoint = e.Location;
            CurrentMoveType = CalcCursor(e.Location).moveType;
        }

        private void SubSkiaControl_MouseUp(object sender, MouseEventArgs e)
        {

            MouseUpOrMove(e);

            LastMouseMovePoint = null;
            CurrentMoveType = MoveType.None;
        }
        #endregion

        private void SubSkiaControl_MouseLeave(object sender, EventArgs e)
        {
            Cursor = Cursors.Default;
        }

        private void PrintAreaTextboxs_Leave(object sender, EventArgs e)
        {
            if (sender is not TextBox t)
            {
                return;
            }
            bool vertical = t == PrintAreaTopTextbox || t == PrintAreaBottomTextbox;
            int max = vertical ? SubSkiaControl.DisplayRectangle.Height : SubSkiaControl.DisplayRectangle.Width;

            var val = t.Text.Trim().ParseIntInvariant();
            if (!val.HasValue)
            {
                if (t.Name == PrintAreaTopTextbox.Name)
                {
                    val = (int)PrintArea.Top;

                }
                else if (t.Name == PrintAreaLeftTextbox.Name)
                {
                    val = (int)PrintArea.Left;
                }
                else if (t.Name == PrintAreaRightTextbox.Name)
                {
                    val = (int)PrintArea.Right;
                }
                else
                {
                    val = (int)PrintArea.Bottom;
                }
                t.Text = $"{val}";
            }
            else
            {
                if (val.Value < 0)
                {
                    t.Text = "0";

                }
                else if (val.Value > max)
                {
                    t.Text = $"{max}";

                }
            }


            var left = PrintAreaLeftTextbox.Text.ParseIntInvariant();
            var right = PrintAreaRightTextbox.Text.ParseIntInvariant();
            var top = PrintAreaTopTextbox.Text.ParseIntInvariant();
            var bottom = PrintAreaBottomTextbox.Text.ParseIntInvariant();
            if (left > right)
            {
                if (right < PrintAreaMinWidth)
                {
                    PrintAreaRightTextbox.Text = $"{PrintAreaMinWidth}";
                    PrintAreaLeftTextbox.Text = "0";
                }
                else
                {
                    PrintAreaLeftTextbox.Text = $"{right - PrintAreaMinWidth}";
                }
            }
            if (top > bottom)
            {
                if (bottom < PrintAreaMinHeight)
                {
                    PrintAreaBottomTextbox.Text = $"{PrintAreaMinHeight}";
                    PrintAreaTopTextbox.Text = "0";
                }
                else
                {
                    PrintAreaTopTextbox.Text = $"{bottom - PrintAreaMinHeight}";
                }

            }

            if (!PrintAreaLeftTextbox.Text.ParseIntInvariant().HasValue
                || !PrintAreaTopTextbox.Text.ParseIntInvariant().HasValue
                || !PrintAreaRightTextbox.Text.ParseIntInvariant().HasValue
                || !PrintAreaBottomTextbox.Text.ParseIntInvariant().HasValue)
            {
                return;
            }
            PrintArea = new(PrintAreaLeftTextbox.Text.ParseIntInvariant()!.Value,
                PrintAreaTopTextbox.Text.ParseIntInvariant()!.Value,
                PrintAreaRightTextbox.Text.ParseIntInvariant()!.Value,
                PrintAreaBottomTextbox.Text.ParseIntInvariant()!.Value);

            RefreshPrintAreaTextboxes();
            SubSkiaControl.Invalidate();
        }



        private void OutputImageSizeTextbox_Leave(object sender, EventArgs e)
        {
            var val = OutputImageSizeTextbox.Text?.ParseIntInvariant();
            if (!val.HasValue)
            {
                OutputImageSizeTextbox.Text = $"{DefaultImageWidth}";
            }
            else
            {
                if (val.Value < MinImageWidth)
                {
                    OutputImageSizeTextbox.Text = $"{MinImageWidth}";
                }
                else if (val.Value > MaxImageWidth)
                {
                    OutputImageSizeTextbox.Text = $"{MaxImageWidth}";
                }
            }
            RefreshPrintAreaTextboxes();
        }

        private void OpenFileDialogButton_Click(object sender, EventArgs e)
        {
            var dir = string.IsNullOrEmpty(OutputPathTextbox.Text) ? AppContext.BaseDirectory : Path.GetDirectoryName(OutputPathTextbox.Text);

            var sfd = new SaveFileDialog()
            {
                AddExtension = true,
                AddToRecent = true,
                Filter = $"*.{OutputFileFormatCombobox.Text}|*.{OutputFileFormatCombobox.Text}",
                InitialDirectory = dir,

            };
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                OutputPathTextbox.Text = Path.GetDirectoryName(sfd.FileName);
                OutputFileNameTextbox.Text = Path.GetFileName(sfd.FileName);
            }
        }

        private unsafe static Bitmap ToWinBitmap(SKBitmap bitmap)
        {

            Bitmap winBmp = new(bitmap.Width, bitmap.Height, SD.Imaging.PixelFormat.Format32bppArgb);
            var bitmapData = winBmp.LockBits(new Rectangle(0, 0, winBmp.Width, winBmp.Height), SD.Imaging.ImageLockMode.ReadWrite, winBmp.PixelFormat);
            Span<byte> lines = new(bitmapData.Scan0.ToPointer(), bitmapData.Height * bitmapData.Stride);
            //中身同じであることを前提とする
            ReadOnlySpan<byte> origSpan = new(bitmap.GetPixels().ToPointer(), bitmapData.Height * bitmapData.Stride);
            origSpan.CopyTo(lines);
            winBmp.UnlockBits(bitmapData);
            return winBmp;
        }

        private async void SaveButton_Click(object sender, EventArgs e)
        {
            if (_areaSelectionOnly)
            {
                try { CommitSelectedArea(); DialogResult = DialogResult.OK; Close(); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Area selection failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                return;
            }
            SaveButton.Enabled = false;
            try
            {
                if (AppRoot.MapData is null)
                {
                    //TODO
                    MessageBox.Show($"Map was not loaded.");
                    return;
                }
                var outputSize = OutputImageSizeTextbox.Text.ParseIntInvariant();
                var format = OutputFileFormatCombobox.Text;

                if (string.IsNullOrEmpty(format))
                {
                    //TODO
                    MessageBox.Show($"Format was undefined.");
                    return;
                }
                var outDir = OutputPathTextbox.Text;
                if (string.IsNullOrEmpty(outDir))
                {
                    //TODO
                    MessageBox.Show($"File path  was undefined.");
                    return;
                }
                var outFn = OutputFileNameTextbox.Text;
                if (string.IsNullOrEmpty(outFn))
                {
                    //TODO
                    MessageBox.Show($"File name was undefined.");
                    return;
                }
                var fn = Path.Combine(outDir, outFn);

                SKRect imageSize;
                if (outputSize.HasValue)
                {
                    float rate = PrintArea.Height / PrintArea.Width;
                    if (rate >= 1f)
                    {
                        imageSize = new(0, 0, (int)(outputSize.Value / rate), outputSize.Value);
                    }
                    else
                    {
                        imageSize = new(0, 0, outputSize.Value, (int)(outputSize.Value * rate));
                    }

                }
                else
                {
                    //TODO
                    MessageBox.Show($"Image size was undefined");
                    return;
                }
                if (PreviewHost == null) throw new InvalidOperationException("Preview is not ready.");
                var rotatedCrop = GetRotatedCrop();
                AppRoot.OutputArea = rotatedCrop;
                using var host = await BetaOutput.FromScreenAsync(AppRoot, rotatedCrop, (int)imageSize.Width, (int)imageSize.Height);
                BetaOutput.Save(host, fn, (int)imageSize.Width, (int)imageSize.Height);
                AppRoot.Context.UserSettings.SaveImageDir = outDir;
                AppRoot.Context.UserSettings.OutputImageFormat = OutputFileFormatCombobox.Text;
                AppRoot.Context.UserSettings.OutputImageSize = outputSize ?? DefaultImageWidth;
                await AppRoot.Context.UserSettings.SaveAsync();
                MessageBox.Show($"Image saved to\n{fn}");

            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally
            {
                SaveButton.Enabled = true;
            }
        }

        private void SaveImageForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Preview and export use independent map layers; interactive geometry stays intact.
        }

        private void OutputFileFormatCombobox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if(!string.IsNullOrEmpty(OutputFileNameTextbox.Text))
            {
                var ext = OutputFileFormatCombobox.Text;
                var fnbase = Path.GetFileNameWithoutExtension(OutputFileNameTextbox.Text);
                OutputFileNameTextbox.Text = $"{fnbase}.{ext}";
            }
        }
    }
}
