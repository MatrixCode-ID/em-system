namespace Em.Shared;

/// <summary>What a file attached to a contact is.</summary>
public enum ContactAttachmentKind
{
   /// <summary>A photo of the person.</summary>
   Photo = 1,
   /// <summary>An identity card.</summary>
   IdCard = 2,
   /// <summary>A diploma or certificate of education.</summary>
   Diploma = 3,
   /// <summary>Any other file.</summary>
   Other = 99
}
