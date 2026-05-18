using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NoteFlow.Studio.Views
{
    public partial class AddToKnowledgeBaseView
    {
        private bool _canDraw = false;
        private double _cropLeftRatio = 0;
        private double _cropTopRatio = 0;
        private double _cropWidthRatio = 0;
        private double _cropHeightRatio = 0;

        private void BtnDrawCropArea_Click(object sender, RoutedEventArgs e)
        {
            _canDraw = true;
            CropOverlayPopup.IsOpen = true;
            BtnConfirmCropArea.Visibility = Visibility.Visible;
            if (CropRectangle != null) CropRectangle.Visibility = Visibility.Collapsed;
            TxtCropArea.Text = "请在视频画面上拖击鼠标框选";
        }

        private void BtnConfirmCropArea_Click(object sender, RoutedEventArgs e)
        {
            _canDraw = false;
            CropOverlayPopup.IsOpen = false;
            BtnConfirmCropArea.Visibility = Visibility.Collapsed;
        }

        private void CanvasCropOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_canDraw) return;
            _isDrawing = true;
            _startPoint = e.GetPosition(CanvasCropOverlay);
            Canvas.SetLeft(CropRectangle, _startPoint.X);
            Canvas.SetTop(CropRectangle, _startPoint.Y);
            CropRectangle.Width = 0;
            CropRectangle.Height = 0;
            CropRectangle.Visibility = Visibility.Visible;
            CanvasCropOverlay.CaptureMouse();
        }

        private void CanvasCropOverlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawing) return;

            var pos = e.GetPosition(CanvasCropOverlay);

            // Clamp coordinates to canvas boundaries
            double clampedX = Math.Max(0, Math.Min(pos.X, CanvasCropOverlay.ActualWidth));
            double clampedY = Math.Max(0, Math.Min(pos.Y, CanvasCropOverlay.ActualHeight));
            double startX = Math.Max(0, Math.Min(_startPoint.X, CanvasCropOverlay.ActualWidth));
            double startY = Math.Max(0, Math.Min(_startPoint.Y, CanvasCropOverlay.ActualHeight));

            var x = Math.Min(clampedX, startX);
            var y = Math.Min(clampedY, startY);
            var w = Math.Abs(clampedX - startX);
            var h = Math.Abs(clampedY - startY);

            Canvas.SetLeft(CropRectangle, x);
            Canvas.SetTop(CropRectangle, y);
            CropRectangle.Width = w;
            CropRectangle.Height = h;
        }

        private void CanvasCropOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawing) return;

            _isDrawing = false;
            CanvasCropOverlay.ReleaseMouseCapture();

            if (CanvasCropOverlay.ActualWidth > 0 && CanvasCropOverlay.ActualHeight > 0)
            {
                _cropLeftRatio = Canvas.GetLeft(CropRectangle) / CanvasCropOverlay.ActualWidth;
                _cropTopRatio = Canvas.GetTop(CropRectangle) / CanvasCropOverlay.ActualHeight;
                _cropWidthRatio = CropRectangle.Width / CanvasCropOverlay.ActualWidth;
                _cropHeightRatio = CropRectangle.Height / CanvasCropOverlay.ActualHeight;
            }

            TxtCropArea.Text = $"已框选 (宽: {(int)CropRectangle.Width}, 高: {(int)CropRectangle.Height})";
            // Do not hide Canvas immediately so users can see the box.
            // CanvasCropOverlay.Visibility = Visibility.Collapsed;
        }

        private void VideoContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (CropRectangle.Visibility == Visibility.Visible && _cropWidthRatio > 0 && _cropHeightRatio > 0)
            {
                double newWidth = e.NewSize.Width;
                double newHeight = e.NewSize.Height;

                if (newWidth > 0 && newHeight > 0)
                {
                    double newLeft = _cropLeftRatio * newWidth;
                    double newTop = _cropTopRatio * newHeight;
                    double newW = _cropWidthRatio * newWidth;
                    double newH = _cropHeightRatio * newHeight;

                    Canvas.SetLeft(CropRectangle, newLeft);
                    Canvas.SetTop(CropRectangle, newTop);
                    CropRectangle.Width = newW;
                    CropRectangle.Height = newH;
                    
                    TxtCropArea.Text = $"已框选 (宽: {(int)newW}, 高: {(int)newH})";
                }
            }
        }
    }
}
