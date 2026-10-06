using System.Globalization;
using System.Windows.Data;

namespace PrintTool.Ui.Shared.Converters;

/// <summary>Inverte um bool — usado, por exemplo, pra desabilitar um botão quando uma flag está ligada.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
