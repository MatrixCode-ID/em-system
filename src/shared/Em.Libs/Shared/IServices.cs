namespace Em.Shared
{
   /// <summary>
   /// Empty marker interface every module service interface must derive from (e.g.
   /// <c>IContactServices</c>). The <c>EmApp</c> dispatcher uses it to recognize which types are
   /// "services" that can be registered and mapped to HTTP actions, without adding any method contract.
   /// </summary>
   public interface IServices
   {

   }
}
