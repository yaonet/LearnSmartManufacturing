using System.Windows.Controls;
using System.Windows.Input;
using LearnSmartManufacturing.ViewModels;

namespace LearnSmartManufacturing.Views
{
    public partial class LensView : UserControl
    {
        public LensView()
        {
            InitializeComponent();
        }

        private void Diagram_MouseDown(object sender, MouseButtonEventArgs e) => Diagram.Focus();

        // Left/Right (or Down/Up) on the diagram adjusts the focal length by 1 mm.
        private void Diagram_KeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not LensViewModel vm)
            {
                return;
            }

            var step = e.Key switch
            {
                Key.Right or Key.Up => 1,
                Key.Left or Key.Down => -1,
                _ => 0,
            };

            if (step != 0)
            {
                vm.FocalLength = Math.Clamp(vm.FocalLength + step, vm.MinFocalLength, vm.MaxFocalLength);
                e.Handled = true;
            }
        }
    }
}
