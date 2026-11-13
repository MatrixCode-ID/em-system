// Ported from GridCraft (core/src/date.rs @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
namespace Em.Sheet.Cells;

/// <summary>Converts between dates and spreadsheet serial numbers.</summary>
public static class SheetDates
{
    private static readonly DateTime ExcelEpoch = new(1899, 12, 31);
    private static readonly DateTime MacEpoch = new(1904, 1, 1);
    private static readonly DateTime LeapBoundary = new(1900, 3, 1);

    /// <summary>Converts a date to a serial number.</summary>
    /// <param name="date">The date and time.</param>
    /// <param name="system">The date system.</param>
    /// <returns>The serial number; the fraction is the time of day.</returns>
    public static double ToSerial(DateTime date, SheetDateSystem system = SheetDateSystem.Excel1900) =>
        system == SheetDateSystem.Excel1904 ? (date - MacEpoch).TotalDays :
            (date - ExcelEpoch).TotalDays + (date >= LeapBoundary ? 1 : 0);

    /// <summary>Converts a serial number to a date.</summary>
    /// <param name="serial">The serial number.</param>
    /// <param name="system">The date system.</param>
    /// <returns>The date, or null when the serial is outside the supported range.</returns>
    public static DateTime? ToDateTime(double serial, SheetDateSystem system = SheetDateSystem.Excel1900)
    {
        if (!double.IsFinite(serial) || serial < 0 || serial >= (system == SheetDateSystem.Excel1900 ? 2_958_466 : 2_957_004))
            return null;
        if (system == SheetDateSystem.Excel1904)
            return MacEpoch.AddDays(serial);
        if (serial is >= 60 and < 61)
            return null;
        return ExcelEpoch.AddDays(serial >= 61 ? serial - 1 : serial);
    }
}
