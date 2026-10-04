namespace Em.Shared
{
   /// <summary>
   /// Hasil action <c>Core/Handshake</c>: public key server beserta bukti bahwa server memang memegang private
   /// key pasangannya, yaitu tanda tangan atas nonce yang dikirim client. Dipakai bersama oleh backend (menyusun
   /// respons) dan UI (membaca respons), jadi nama property-nya harus tetap sama di kedua sisi karena
   /// <c>Defaults.ResponseJsonOptions</c> sengaja case-sensitive.
   /// </summary>
   public class ServerHandshakeResult
   {
      /// <summary>
      /// Public key RSA server, berupa Base64 dari DER PKCS#1. Jangan dipercaya sebelum <see cref="Signature"/>
      /// berhasil diverifikasi terhadap nonce yang dikirim.
      /// </summary>
      public string PublicKey { get; set; } = string.Empty;

      /// <summary>
      /// Tanda tangan server (Base64Url) atas payload hasil <c>ProbeProtocol.BuildSignaturePayload</c>, yaitu
      /// prefix domain diikuti nonce dari client.
      /// </summary>
      public string Signature { get; set; } = string.Empty;

      /// <summary>
      /// Ukuran key RSA server dalam bit, untuk keperluan diagnostik di sisi client.
      /// </summary>
      public int KeySize { get; set; }
   }
}
