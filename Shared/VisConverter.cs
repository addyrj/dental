using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DentalX
{
    // Shared by any page with a "placeholder text" TextBlock overlaid on an empty TextBox
    // (hides the placeholder once the user has typed something).
    public class VisConverter : IValueConverter
    {
        public static readonly VisConverter Instance = new();
        public object Convert(object value, Type t, object p, CultureInfo c)
            => (value is int len && len > 0) ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }
}
