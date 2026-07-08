using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using System.Data;

namespace InfinLimit
{
    [ValueConversion(typeof(object), typeof(double))]
    public class MathConverter : MarkupExtension, IValueConverter, IMultiValueConverter
    {
        private static MathConverter _instance;

        #region IValueConverter Members

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Make the conversion work with both IValueConverter and IMultiValueConverter
            if (value is object[] values)
            {
                return Convert(values, targetType, parameter, culture);
            }
            else
            {
                return Convert(new object[] { value }, targetType, parameter, culture);
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("MathConverter does not support two-way conversion.");
        }

        #endregion

        #region IMultiValueConverter Members

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                string expression = parameter as string;
                if (string.IsNullOrWhiteSpace(expression))
                    return 0;

                // Replace variables in the expression
                for (int i = 0; i < values.Length; i++)
                {
                    double val = 0;
                    if (values[i] != null)
                    {
                        if (double.TryParse(values[i].ToString(), out val))
                        {
                            // Replace variables like 'x', 'y', 'z', etc.
                            char variable = (char)('x' + i);
                            expression = expression.Replace(variable.ToString(), val.ToString(CultureInfo.InvariantCulture));
                        }
                    }
                }

                // Use DataTable.Compute for safe expression evaluation
                var table = new DataTable();
                var result = table.Compute(expression, null);
                
                if (result != null && double.TryParse(result.ToString(), out double finalResult))
                {
                    return finalResult;
                }
            }
            catch (Exception ex)
            {
                // Log the exception or handle it as needed
                System.Diagnostics.Debug.WriteLine($"MathConverter error: {ex.Message}");
            }

            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("MathConverter does not support two-way conversion.");
        }

        #endregion

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return _instance ?? (_instance = new MathConverter());
        }
    }
}
