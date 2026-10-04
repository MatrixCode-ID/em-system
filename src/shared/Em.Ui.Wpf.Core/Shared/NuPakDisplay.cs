namespace Em.Ui.Wpf.Shared;
public static class NuPakDisplay
{
   public static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.##} GB" :
      bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.##} MB" : bytes >= 1024 ? $"{bytes / 1024d:0.##} KB" : $"{bytes} B";
}
