namespace Em.Ui.Wpf.Shared;
/// <summary>Display helpers for NuPak values.</summary>
public static class NuPakDisplay
{
   /// <summary>Formats a size in bytes in a human-friendly form.</summary>
   public static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.##} GB" :
      bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.##} MB" : bytes >= 1024 ? $"{bytes / 1024d:0.##} KB" : $"{bytes} B";
}
