using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace 币安量化机器人.Controls
{
    public partial class Icon : UserControl
    {
        public static readonly DependencyProperty GeometryProperty = DependencyProperty.Register(
            nameof(Geometry), typeof(Geometry), typeof(Icon), new PropertyMetadata(default(Geometry), OnGeometryChanged));

        public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
            nameof(Fill), typeof(Brush), typeof(Icon), new PropertyMetadata(default(Brush)));

        public Geometry? Geometry
        {
            get => (Geometry?)GetValue(GeometryProperty);
            set => SetValue(GeometryProperty, value);
        }

        public Brush? Fill
        {
            get => (Brush?)GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        public Icon()
        {
            InitializeComponent();
            Loaded += (s, e) => UpdateVisual();
            Unloaded += (s, e) => ClearVisual();
        }

        private static void OnGeometryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Icon icon)
            {
                icon.UpdateVisual();
            }
        }

        private void UpdateVisual()
        {
            if (Geometry != null)
            {
                IconPath.Visibility = Visibility.Visible;
                IconRect.Visibility = Visibility.Collapsed;
                IconPath.Data = Geometry;
            }
            else
            {
                IconPath.Visibility = Visibility.Collapsed;
                IconRect.Visibility = Visibility.Visible;
            }
        }

        private void ClearVisual()
        {
            // Avoid caching brushes/geometries on unload
            IconPath.Data = null;
            IconPath.Visibility = Visibility.Collapsed;
            IconRect.Fill = null;
            IconRect.Visibility = Visibility.Collapsed;
        }
    }
}
