namespace Em.Ui.Core.Shared
{
   public abstract class NavigationPayloadBase(object? data)
   {
      protected object? _data = data;
      public object? Data => _data;
      public DataState DataState { get; protected set; }

      /// <summary>
      /// Judul entri yang dibuka dengan parameter ini, atau <c>null</c> untuk memakai judul bawaan
      /// navigasinya. Judul ini sekaligus kunci unik entri: membuka layar dengan judul yang sudah
      /// terbuka hanya memindahkan tampilan ke entri itu.
      /// <para>
      /// Karena itu, judul sebuah dokumen <b>wajib mengandung penanda unik dokumennya</b> - mis. nomor
      /// referensi atau nama akun - bukan teks yang bisa kembar seperti nama customer. Kalau dua
      /// dokumen berbeda menghasilkan judul yang sama, dokumen kedua tidak akan pernah bisa dibuka:
      /// yang tampil selalu dokumen pertama.
      /// </para>
      /// </summary>
      public virtual string? Title => null;
   }

   public enum DataState
   {
      NewData,
      EditData,
   }
}
