using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LearnSmartManufacturing.Models.Motion;
using LearnSmartManufacturing.ViewModels;

namespace LearnSmartManufacturing.Views
{
    public partial class MotionControlView : UserControl
    {
        private bool _isJogHeld;

        public MotionControlView()
        {
            InitializeComponent();
            Unloaded += OnUnloaded;
        }

        private void JogNegButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MotionControlViewModel vm)
            {
                _isJogHeld = true;
                vm.StartJog(-1);
            }
        }

        private void JogPosButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MotionControlViewModel vm)
            {
                _isJogHeld = true;
                vm.StartJog(1);
            }
        }

        private void JogButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => ReleaseJog();

        private void JogButton_LostMouseCapture(object sender, MouseEventArgs e) => ReleaseJog();

        private void OnUnloaded(object sender, RoutedEventArgs e) => ReleaseJog();

        private void XyCanvas_StageClicked(Point2D point)
        {
            if (DataContext is MotionControlViewModel vm)
            {
                vm.XyStage.SetTargetFromCanvas(point);
            }
        }

        private void ReleaseJog()
        {
            if (!_isJogHeld)
            {
                return;
            }

            _isJogHeld = false;
            if (DataContext is MotionControlViewModel vm)
            {
                vm.StopJog();
            }
        }
    }
}
