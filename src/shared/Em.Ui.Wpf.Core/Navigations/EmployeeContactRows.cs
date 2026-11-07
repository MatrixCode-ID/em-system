using System.Globalization;
using System.IO;
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

/// <summary>An attachment kind with a readable caption.</summary>
/// <param name="Value">Stored kind.</param>
/// <param name="Caption">Text displayed in the picker.</param>
public sealed record EmployeeAttachmentKindOption(ContactAttachmentKind Value, string Caption);

/// <summary>Editable contact attachment retaining its identity and concurrency stamp.</summary>
public sealed class EmployeeAttachmentVm : MvvmModelBase
{
   private ContactAttachmentInfo _info;

   /// <summary>Attachment kinds in display order.</summary>
   public static EmployeeAttachmentKindOption[] Kinds { get; } = [new(ContactAttachmentKind.Photo, "Photo"), new(ContactAttachmentKind.IdCard, "ID card"), new(ContactAttachmentKind.Diploma, "Diploma"), new(ContactAttachmentKind.Other, "Other")];

   /// <summary>Loads one saved attachment.</summary>
   public EmployeeAttachmentVm(ContactAttachmentInfo info) {
      _info = info;
      Kind = info.Row.cContactAttachmentKind;
      Name = info.Row.cContactAttachmentName;
      Note = info.Row.cContactAttachmentNote ?? "";
   }

   /// <summary>Stable identity of this attachment.</summary>
   public string Id => _info.Row.cContactAttachmentId;
   /// <summary>Last-read stamp, sent with every change.</summary>
   public DateTime Ustamp => _info.Row.ustamp;
   /// <summary>Editable kind.</summary>
   public ContactAttachmentKind Kind { get => Get<ContactAttachmentKind>(); set => Set(value, _ => NotifyChanged(nameof(KindCaption))); }
   /// <summary>Editable display name.</summary>
   public string Name { get => Get<string>() ?? ""; set => Set(value); }
   /// <summary>Editable note.</summary>
   public string Note { get => Get<string>() ?? ""; set => Set(value); }
   /// <summary>Readable kind.</summary>
   public string KindCaption => Kinds.FirstOrDefault(x => x.Value == Kind)?.Caption ?? "Other";
   /// <summary>Name of the stored file.</summary>
   public string FileName => _info.FileName ?? _info.Row.cContactAttachmentName;
   /// <summary>Type, size and upload date of the stored file.</summary>
   public string DetailCaption => _info.UploadedAt is { } date
      ? $"{Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant()} · {SizeCaption(_info.Size)} · {date.ToLocalTime().ToString("dd-MMM-yy", CultureInfo.GetCultureInfo("en-US"))}"
      : "The file is not available.";
   /// <summary>Name and size of the stored file.</summary>
   public string FileCaption => _info.UploadedAt is null ? FileName : $"{FileName} · {SizeCaption(_info.Size)}";
   /// <summary>Whether the editable values differ from the saved attachment.</summary>
   public bool IsDirty => Kind != _info.Row.cContactAttachmentKind || Name != _info.Row.cContactAttachmentName || Note != (_info.Row.cContactAttachmentNote ?? "");
   /// <summary>Whether the attachment can be saved.</summary>
   public bool IsValid => !string.IsNullOrWhiteSpace(Name);

   /// <summary>Takes the file details and stamp of a replaced file, keeping unsaved edits.</summary>
   public void ApplyFile(ContactAttachmentInfo saved) {
      _info = new ContactAttachmentInfo {
         Row = _info.Row, FileName = saved.FileName, Mime = saved.Mime, Size = saved.Size, UploadedAt = saved.UploadedAt, Version = saved.Version
      };
      _info.Row.ustamp = saved.Row.ustamp;
      foreach (var name in (string[])[nameof(FileName), nameof(FileCaption), nameof(DetailCaption)]) NotifyChanged(name);
   }

   /// <summary>Builds the attachment for an atomic employee save.</summary>
   public ta_ContactAttachment ToRow() => new() {
      cContactAttachmentId = Id, cContactId = _info.Row.cContactId, cStoreObjectId = _info.Row.cStoreObjectId, cContactAttachmentName = Name,
      cContactAttachmentKind = Kind, cContactAttachmentNote = Note.Length == 0 ? null : Note, cContactAttachmentState = _info.Row.cContactAttachmentState,
      cContactAttachmentOrder = _info.Row.cContactAttachmentOrder, ustamp = _info.Row.ustamp, datestamp = _info.Row.datestamp, json_object = _info.Row.json_object
   };

   private static string SizeCaption(long bytes) {
      var culture = CultureInfo.GetCultureInfo("en-US");
      return bytes >= 1024 * 1024 ? (bytes / 1048576d).ToString("0.0", culture) + " MB" : bytes >= 1024 ? (bytes / 1024d).ToString("0", culture) + " KB" : bytes.ToString(culture) + " B";
   }
}
