﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Npoi.Mapper.Attributes;
using NPOI.SS.UserModel;
using System.Linq;

namespace Npoi.Mapper
{
    /// <summary>
    /// Provide static supportive functionalities for <see cref="Mapper"/> class.
    /// </summary>
    public static class MapHelper
    {
        #region Fields

        // Default chars that will be removed when mapping by column header name.
        private static readonly char[] DefaultIgnoredChars =
        {'`', '~', '!', '@', '#', '$', '%', '^', '&', '*', '-', '_', '+', '=', '|', ',', '.', '/', '?'};

        // Default chars to truncate column header name during mapping.
        private static readonly char[] DefaultTruncateChars = { '[', '<', '(', '{' };

        // Binding flags to lookup object properties.
        public const BindingFlags BindingFlag = BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance;

        /// <summary>
        /// Collection of numeric types.
        /// </summary>
        private static readonly List<Type> NumericTypes = new List<Type>
        {
            typeof(decimal),
            typeof(byte), typeof(sbyte),
            typeof(short), typeof(ushort),
            typeof(int), typeof(uint),
            typeof(long), typeof(ulong),
            typeof(float), typeof(double)
        };

        /// <summary>
        /// Store cached built-in styles to avoid create new ICellStyle for each cell.
        /// </summary>
        private static readonly Dictionary<short, ICellStyle> BuiltinStyles = new Dictionary<short, ICellStyle>();

        /// <summary>
        /// Store cached custom styles to avoid create new ICellStyle for each customized cell.
        /// </summary>
        private static readonly Dictionary<string, ICellStyle> CustomStyles = new Dictionary<string, ICellStyle>();

        #endregion

        #region Public Methods

        /// <summary>
        /// Load attributes to a dictionary.
        /// </summary>
        /// <typeparam name="T">The target type.</typeparam>
        /// <param name="attributes">Container to hold loaded attributes.</param>
        public static void LoadAttributes<T>(Dictionary<PropertyInfo, ColumnAttribute> attributes)
        {
            var type = typeof(T);

            foreach (var pi in type.GetProperties(BindingFlag))
            {
                var columnMeta = pi.GetCustomAttribute<ColumnAttribute>();
                var ignore = Attribute.IsDefined(pi, typeof(IgnoreAttribute));
                var useLastNonBlank = Attribute.IsDefined(pi, typeof(UseLastNonBlankValueAttribute));

                if (columnMeta == null && !ignore && !useLastNonBlank) continue;

                if (columnMeta == null) columnMeta = new ColumnAttribute
                {
                    Ignored = ignore ? new bool?(true) : null,
                    UseLastNonBlankValue = useLastNonBlank ? new bool?(true) : null
                };

                columnMeta.Property = pi;

                // Note that attribute from Map method takes precedence over Attribute meta data.
                columnMeta.MergeTo(attributes, false);
            }
        }

        /// <summary>
        /// Extension for <see cref="IEnumerable{T}"/> object to handle each item.
        /// </summary>
        /// <typeparam name="T">The item type.</typeparam>
        /// <param name="sequence">The enumerable sequence.</param>
        /// <param name="action">Action to apply to each item.</param>
        public static void ForEach<T>(this IEnumerable<T> sequence, Action<T> action)
        {
            if (sequence == null) return;

            foreach (var item in sequence)
            {
                action(item);
            }
        }

        /// <summary>
        /// Clear cached data for cell styles and tracked column info.
        /// </summary>
        public static void ClearCache()
        {
            BuiltinStyles.Clear();
            CustomStyles.Clear();
        }

        /// <summary>
        /// Check if the given type is a numeric type.
        /// </summary>
        /// <param name="type">The type to be checked.</param>
        /// <returns><c>true</c> if it's numeric; otherwise <c>false</c>.</returns>
        public static bool IsNumeric(this Type type)
        {
            return NumericTypes.Contains(type);
        }

        /// <summary>
        /// Load cell data format by a specified row.
        /// </summary>
        /// <typeparam name="T">The target type.</typeparam>
        /// <param name="dataRow">The row to load format from.</param>
        /// <param name="columns">The column collection to load formats into.</param>
        public static void LoadDataFormats<T>(IRow dataRow, IEnumerable<ColumnInfo<T>> columns)
        {
            if (dataRow == null || columns == null) return;

            foreach (var column in columns)
            {
                var cell = dataRow.GetCell(column.Attribute.Index);

                if (cell != null) column.DataFormat = cell.CellStyle.DataFormat;
            }
        }

        /// <summary>
        /// Get the cell style.
        /// </summary>
        /// <param name="cell">The cell.</param>
        /// <param name="customFormat">The custom format string.</param>
        /// <param name="builtinFormat">The built-in format number.</param>
        /// <param name="columnFormat">The default column format number.</param>
        /// <returns><c>ICellStyle</c> object for the given cell.</returns>
        public static ICellStyle GetCellStyle(ICell cell, string customFormat, short builtinFormat, short? columnFormat)
        {
            ICellStyle style = null;
            var workbook = cell?.Row.Sheet.Workbook;

            if (customFormat != null)
            {
                if (CustomStyles.ContainsKey(customFormat))
                {
                    style = CustomStyles[customFormat];
                }
                else if (workbook != null)
                {
                    style = workbook.CreateCellStyle();
                    style.DataFormat = workbook.CreateDataFormat().GetFormat(customFormat);
                    CustomStyles[customFormat] = style;
                }
            }
            else if (workbook != null)
            {
                var format = builtinFormat != 0 ? builtinFormat : columnFormat ?? 0; /*default to 0*/

                if (BuiltinStyles.ContainsKey(format))
                {
                    style = BuiltinStyles[format];
                }
                else
                {
                    style = workbook.CreateCellStyle();
                    style.DataFormat = format;
                    BuiltinStyles[format] = style;
                }
            }

            return style;
        }

        /// <summary>
        /// Get underline cell type if the cell is in formula.
        /// </summary>
        /// <param name="cell">The cell.</param>
        /// <returns>The underline cell type.</returns>
        public static CellType GetCellType(ICell cell)
        {
            return cell.CellType == CellType.Formula ? cell.CachedFormulaResultType : cell.CellType;
        }

        /// <summary>
        /// Try get cell value.
        /// </summary>
        /// <param name="cell">The cell to retrieve value.</param>
        /// <param name="targetType">Type of target property.</param>
        /// <param name="value">The returned value for cell.</param>
        /// <returns><c>true</c> if get value successfully; otherwise false.</returns>
        public static bool TryGetCellValue(ICell cell, Type targetType, out object value)
        {
            value = null;
            if (cell == null) return true;

            var success = true;

            switch (GetCellType(cell))
            {
                case CellType.String:

                    if (targetType?.IsEnum == true) // Enum type.
                    {
                        value = Enum.Parse(targetType, cell.StringCellValue, true);
                    }
                    else // String type.
                    {
                        value = cell.StringCellValue;
                    }

                    break;

                case CellType.Numeric:

                    if (DateUtil.IsCellDateFormatted(cell) || targetType == typeof(DateTime)) // DateTime type.
                    {
                        value = cell.DateCellValue;
                    }
                    else if (targetType?.IsEnum == true) // Enum type.
                    {
                        value = Enum.Parse(targetType, cell.NumericCellValue.ToString(CultureInfo.InvariantCulture));
                    }
                    else // Number type
                    {
                        value = cell.NumericCellValue;
                    }

                    break;

                case CellType.Boolean:

                    value = cell.BooleanCellValue;
                    break;

                case CellType.Error:
                case CellType.Unknown:
                case CellType.Blank:
                    // Dose nothing to keep return value null.
                    break;

                default:

                    success = false;

                    break;
            }

            return success;
        }

        /// <summary>
        /// Get mapped <c>PropertyInfo</c> by property selector expression.
        /// </summary>
        /// <typeparam name="T">The object type that property belongs to.</typeparam>
        /// <param name="propertySelector">The property selector expression.</param>
        /// <returns>The mapped <c>PropertyInfo</c> object.</returns>
        public static PropertyInfo GetPropertyInfoByExpression<T>(Expression<Func<T, object>> propertySelector)
        {
            var expression = propertySelector as LambdaExpression;

            if (expression == null)
                throw new ArgumentException("Only LambdaExpression is allowed!", nameof(propertySelector));

            var body = expression.Body.NodeType == ExpressionType.MemberAccess ?
                (MemberExpression)expression.Body :
                (MemberExpression)((UnaryExpression)expression.Body).Operand;

            // body.Member will return the MemberInfo of base class, so we have to get it from T...
            //return (PropertyInfo)body.Member;
            return typeof(T).GetMember(body.Member.Name)[0] as PropertyInfo;
        }

        /// <summary>
        /// Get refined name by removing specified chars and truncating by specified chars.
        /// </summary>
        /// <param name="name">The name to be refined.</param>
        /// <param name="ignoringChars">Chars will be removed from the name string.</param>
        /// <param name="truncatingChars">Chars used truncate the name string.</param>
        /// <returns>Refined name string.</returns>
        public static string GetRefinedName(string name, char[] ignoringChars, char[] truncatingChars)
        {
            if (name == null) return null;

            name = Regex.Replace(name, @"\s", "");
            var ignoredChars = ignoringChars ?? DefaultIgnoredChars;
            var truncateChars = truncatingChars ?? DefaultTruncateChars;

            name = ignoredChars.Aggregate(name, (current, c) => current.Replace(c, '\0'));

            var index = name.IndexOfAny(truncateChars);
            if (index >= 0) name = name.Remove(index);

            return name;
        }

        #endregion
    }
}
