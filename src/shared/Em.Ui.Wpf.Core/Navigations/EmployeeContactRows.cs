using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Navigations;

/// <summary>Editable contact communication retaining its identity and concurrency stamp.</summary>
public sealed class EmployeeCommunicationVm : MvvmModelBase
{
   private readonly ta_Comm _original;
   /// <summary>Loads one saved or draft entry.</summary>
   public EmployeeCommunicationVm(ta_Comm row) {
      _original = row;
      Type = row.cCommType;
      Value = row.cCommValue;
      Note = row.cCommNote ?? "";
   }
   /// <summary>Stable identity of this entry.</summary>
   public string Id => _original.cCommId;
   /// <summary>Original state used to guard default selection.</summary>
   public ContactCommunicationState State => _original.cCommState;
   /// <summary>Editable type.</summary>
   public CommunationType Type { get => Get<CommunationType>(); set => Set(value, _ => NotifyChanged(nameof(Caption))); }
   /// <summary>Editable value.</summary>
   public string Value { get => Get<string>() ?? ""; set => Set(value, _ => NotifyChanged(nameof(Caption))); }
   /// <summary>Editable note.</summary>
   public string Note { get => Get<string>() ?? ""; set => Set(value, _ => NotifyChanged(nameof(Caption))); }
   /// <summary>Readable picker caption.</summary>
   public string Caption => Value;
   /// <summary>Builds the entry for an atomic employee save.</summary>
   public ta_Comm ToRow() => new() {
      cCommId = Id, cContactId = _original.cContactId, ustamp = _original.ustamp, datestamp = _original.datestamp, json_object = _original.json_object,
      cCommType = Type, cCommValue = Value, cCommNote = Note, cCommState = State
   };
}

/// <summary>Editable contact address retaining its identity and concurrency stamp.</summary>
public sealed class EmployeeAddressVm : MvvmModelBase
{
   private readonly ta_Address _original;
   /// <summary>Loads one saved or draft entry.</summary>
   public EmployeeAddressVm(ta_Address row) {
      _original = row;
      Name = row.cAddressName;
      Location = row.cAddressLocation;
      Zip = row.cAddressZip;
   }
   /// <summary>Stable identity of this entry.</summary>
   public string Id => _original.cAddressId;
   /// <summary>Original state used to guard default selection.</summary>
   public AddressState State => _original.cAddressState;
   /// <summary>Editable name.</summary>
   public string Name { get => Get<string>() ?? ""; set => Set(value, _ => NotifyChanged(nameof(Caption))); }
   /// <summary>Editable location.</summary>
   public string Location { get => Get<string>() ?? ""; set => Set(value, _ => NotifyChanged(nameof(Caption))); }
   /// <summary>Editable zip.</summary>
   public string Zip { get => Get<string>() ?? ""; set => Set(value, _ => NotifyChanged(nameof(Caption))); }
   /// <summary>Readable picker caption.</summary>
   public string Caption => Name + " — " + Location;
   /// <summary>Builds the entry for an atomic employee save.</summary>
   public ta_Address ToRow() => new() {
      cAddressId = Id, cContactId = _original.cContactId, ustamp = _original.ustamp, datestamp = _original.datestamp, json_object = _original.json_object,
      cAddressName = Name, cAddressLocation = Location, cAddressZip = Zip, cAddressState = State
   };
}

