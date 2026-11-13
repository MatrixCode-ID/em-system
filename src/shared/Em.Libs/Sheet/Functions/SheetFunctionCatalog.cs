// Signatures and descriptions follow GridCraft (crates/functions/src, crates/engine/src/cmd/formulas.rs @ c0ee120ac2d9b59630f5abf1151dd9b471fe2926), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
namespace Em.Sheet.Functions;

/// <summary>A worksheet function as formula autocomplete and the argument hint show it.</summary>
public sealed record SheetFunctionInfo(string Name, string Signature, string Description);

/// <summary>The functions the sheet calculates, sorted by name.</summary>
public static class SheetFunctionCatalog
{
    /// <summary>Gets every supported function, sorted by name.</summary>
    public static IReadOnlyList<SheetFunctionInfo> All { get; } = new SheetFunctionInfo[]
    {
        new("ABS", "ABS(number)", "Absolute value of a number."),
        new("AND", "AND(logical1, [logical2], ...)", "TRUE when every argument is TRUE."),
        new("AVERAGE", "AVERAGE(number1, [number2], ...)", "Arithmetic mean of the numbers."),
        new("AVERAGEIF", "AVERAGEIF(range, criteria, [average_range])", "Average of the cells that meet a condition."),
        new("CHOOSE", "CHOOSE(index_num, value1, [value2], ...)", "Picks a value from a list by position."),
        new("COLUMN", "COLUMN([reference])", "Column number of a reference."),
        new("COLUMNS", "COLUMNS(array)", "Number of columns in a reference or array."),
        new("CONCAT", "CONCAT(text1, [text2], ...)", "Joins text from values and every cell of ranges, without a separator."),
        new("CONCATENATE", "CONCATENATE(text1, [text2], ...)", "Joins several text values into one."),
        new("COUNT", "COUNT(value1, [value2], ...)", "Counts the numbers."),
        new("COUNTA", "COUNTA(value1, [value2], ...)", "Counts the non-empty values."),
        new("COUNTBLANK", "COUNTBLANK(range)", "Counts empty cells and empty text in a range."),
        new("COUNTIF", "COUNTIF(range, criteria)", "Counts the cells that meet a condition."),
        new("COUNTIFS", "COUNTIFS(criteria_range1, criteria1, ...)", "Counts the cells that meet every condition."),
        new("DATE", "DATE(year, month, day)", "Builds a date serial number from a year, month and day."),
        new("DATEDIF", "DATEDIF(start_date, end_date, unit)", "Counts whole years, months or days between two dates. unit: Y, M, D, MD, YM or YD."),
        new("DAY", "DAY(serial_number)", "Returns the day of the month of a date."),
        new("EDATE", "EDATE(start_date, months)", "Returns the date a number of months before or after a date."),
        new("EOMONTH", "EOMONTH(start_date, months)", "Returns the last day of the month a number of months away."),
        new("FALSE", "FALSE()", "Returns the logical value FALSE."),
        new("FIND", "FIND(find_text, within_text, [start_num])", "Finds the position of one text in another (case-sensitive)."),
        new("HLOOKUP", "HLOOKUP(lookup_value, table_array, row_index_num, [range_lookup])", "Finds a value in the first row of a table and returns the value in the same column of another row."),
        new("HOUR", "HOUR(serial_number)", "Returns the hour (0-23) of a time."),
        new("IF", "IF(logical_test, [value_if_true], [value_if_false])", "Returns one value if a condition is true and another if it's false."),
        new("IFERROR", "IFERROR(value, value_if_error)", "Returns a fallback when a value is an error."),
        new("IFNA", "IFNA(value, value_if_na)", "Returns a fallback when a value is #N/A."),
        new("IFS", "IFS(test1, value1, ...)", "Returns the value of the first true condition."),
        new("INDEX", "INDEX(array, row_num, [column_num], [area_num])", "The value or reference at a position."),
        new("INDIRECT", "INDIRECT(ref_text, [a1])", "The reference named by a text string."),
        new("INT", "INT(number)", "Rounds a number down to the nearest integer."),
        new("ISBLANK", "ISBLANK(value)", "TRUE when the value is an empty cell."),
        new("ISERROR", "ISERROR(value)", "TRUE when the value is any error."),
        new("ISNA", "ISNA(value)", "TRUE when the value is the #N/A error."),
        new("ISNUMBER", "ISNUMBER(value)", "TRUE when the value is a number."),
        new("ISTEXT", "ISTEXT(value)", "TRUE when the value is text."),
        new("LEFT", "LEFT(text, [num_chars])", "Returns the first characters of a text."),
        new("LEN", "LEN(text)", "Counts the characters in a text."),
        new("LOWER", "LOWER(text)", "Converts text to lower case."),
        new("MATCH", "MATCH(lookup_value, lookup_array, [match_type])", "Returns the 1-based position of a value in a row or column."),
        new("MAX", "MAX(number1, [number2], ...)", "Largest number (0 when there are none)."),
        new("MID", "MID(text, start_num, num_chars)", "Returns characters from the middle of a text."),
        new("MIN", "MIN(number1, [number2], ...)", "Smallest number (0 when there are none)."),
        new("MINUTE", "MINUTE(serial_number)", "Returns the minute (0-59) of a time."),
        new("MOD", "MOD(number, divisor)", "Remainder of a division, with the sign of the divisor."),
        new("MONTH", "MONTH(serial_number)", "Returns the month (1-12) of a date."),
        new("NA", "NA()", "Returns the #N/A error value."),
        new("NETWORKDAYS", "NETWORKDAYS(start_date, end_date, [holidays])", "Counts working days (Monday to Friday) between two dates, excluding holidays."),
        new("NOT", "NOT(logical)", "Reverses a logical value."),
        new("NOW", "NOW()", "Returns the current date and time."),
        new("OFFSET", "OFFSET(reference, rows, cols, [height], [width])", "A reference shifted from a starting point."),
        new("OR", "OR(logical1, [logical2], ...)", "TRUE when at least one argument is TRUE."),
        new("POWER", "POWER(number, power)", "Raises a number to a power."),
        new("PRODUCT", "PRODUCT(number1, [number2], ...)", "Multiplies all the numbers in the arguments."),
        new("PROPER", "PROPER(text)", "Capitalises the first letter of each word and lower-cases the rest."),
        new("REPLACE", "REPLACE(old_text, start_num, num_chars, new_text)", "Replaces part of a text by position."),
        new("REPT", "REPT(text, number_times)", "Repeats a text a given number of times."),
        new("RIGHT", "RIGHT(text, [num_chars])", "Returns the last characters of a text."),
        new("ROUND", "ROUND(number, num_digits)", "Rounds a number to a given number of digits, halves away from zero."),
        new("ROUNDDOWN", "ROUNDDOWN(number, num_digits)", "Rounds a number toward zero to a given number of digits."),
        new("ROUNDUP", "ROUNDUP(number, num_digits)", "Rounds a number away from zero to a given number of digits."),
        new("ROW", "ROW([reference])", "Row number of a reference."),
        new("ROWS", "ROWS(array)", "Number of rows in a reference or array."),
        new("SEARCH", "SEARCH(find_text, within_text, [start_num])", "Finds the position of one text in another, ignoring case and allowing wildcards."),
        new("SECOND", "SECOND(serial_number)", "Returns the second (0-59) of a time."),
        new("SQRT", "SQRT(number)", "Positive square root."),
        new("SUBSTITUTE", "SUBSTITUTE(text, old_text, new_text, [instance_num])", "Replaces occurrences of one text with another, optionally only a given occurrence."),
        new("SUM", "SUM(number1, [number2], ...)", "Adds all the numbers in the arguments."),
        new("SUMIF", "SUMIF(range, criteria, [sum_range])", "Adds the cells that meet a condition."),
        new("SUMIFS", "SUMIFS(sum_range, criteria_range1, criteria1, ...)", "Adds the cells that meet every condition."),
        new("SUMPRODUCT", "SUMPRODUCT(array1, [array2], ...)", "Multiplies matching elements of equally sized arrays and adds the products."),
        new("SWITCH", "SWITCH(expression, value1, result1, ..., [default])", "Matches a value against a list of cases."),
        new("TEXT", "TEXT(value, format_text)", "Formats a number as text."),
        new("TEXTJOIN", "TEXTJOIN(delimiter, ignore_empty, text1, [text2], ...)", "Joins text from values and ranges with a delimiter, optionally skipping empty items."),
        new("TODAY", "TODAY()", "Returns the current date."),
        new("TRIM", "TRIM(text)", "Removes leading and trailing spaces and collapses runs of spaces to one."),
        new("TRUE", "TRUE()", "Returns the logical value TRUE."),
        new("UPPER", "UPPER(text)", "Converts text to upper case."),
        new("VALUE", "VALUE(text)", "Converts text that looks like a number, date or time into a number."),
        new("VLOOKUP", "VLOOKUP(lookup_value, table_array, col_index_num, [range_lookup])", "Finds a value in the first column of a table and returns the value in the same row of another column."),
        new("WEEKDAY", "WEEKDAY(serial_number, [return_type])", "Returns the day of the week of a date as a number."),
        new("WORKDAY", "WORKDAY(start_date, days, [holidays])", "Returns the date a number of working days before or after a date."),
        new("XLOOKUP", "XLOOKUP(lookup_value, lookup_array, return_array, [if_not_found], [match_mode], [search_mode])", "Searches a row or column and returns the matching item, row or column from another array."),
        new("XMATCH", "XMATCH(lookup_value, lookup_array, [match_mode], [search_mode])", "Returns the position of a value in a row or column with flexible match and search modes."),
        new("YEAR", "YEAR(serial_number)", "Returns the year of a date."),
    };

    /// <summary>Finds a function by name, ignoring case.</summary>
    /// <param name="name">The function name.</param>
    /// <returns>The function, or null when it is not supported.</returns>
    public static SheetFunctionInfo? Find(string name) =>
        All.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
}
