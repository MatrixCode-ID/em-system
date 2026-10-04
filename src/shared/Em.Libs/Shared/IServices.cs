namespace Em.Shared
{
   /// <summary>
   /// Marker interface kosong yang wajib diturunkan oleh setiap interface service module
   /// (mis. <c>IEmSampleServices</c>). Dipakai dispatcher <c>EmApp</c> untuk mengenali
   /// tipe mana yang merupakan "service" yang bisa didaftarkan dan dipetakan ke action HTTP,
   /// tanpa menambah kontrak method apa pun.
   /// </summary>
   public interface IServices
   {

   }
}
