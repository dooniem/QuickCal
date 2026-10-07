using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace QuickCal.Controls
{
    /// <summary>Same look as the UWP version: a ☐ / 🗹 symbol that toggles on click.</summary>
    public partial class CustomCheckbox : UserControl
    {
        public static readonly DependencyProperty IsCheckedProperty =
            DependencyProperty.Register(
                nameof(IsChecked), typeof(bool), typeof(CustomCheckbox),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsCheckedChanged));

        public CustomCheckbox()
        {
            InitializeComponent();
            MouseLeftButtonUp += CustomCheckbox_Click;
            UpdateCheckboxText();
        }

        public bool IsChecked
        {
            get => (bool)GetValue(IsCheckedProperty);
            set => SetValue(IsCheckedProperty, value);
        }

        private void CustomCheckbox_Click(object sender, MouseButtonEventArgs e)
        {
            IsChecked = !IsChecked;
        }

        private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((CustomCheckbox)d).UpdateCheckboxText();
        }

        private void UpdateCheckboxText()
        {
            CheckboxTextBlock.Text = IsChecked ? "🗹" : "☐";
            CheckboxTextBlock.Foreground = IsChecked ? Brushes.Blue : Brushes.Black;
        }
    }
}
