using System.Text.Json;
using Em.Test.Models;
using Em.Ui.Core.Shared;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Test.Wpf
{
   /// <summary>
   /// Kartu informasi tambahan di Approval Manager untuk dokumen uji. Approval Manager menyerahkan kunci
   /// dokumen lewat <see cref="Input"/>; kartu membaca dokumennya sendiri dari module, jadi yang tampil
   /// adalah keadaan dokumen saat ini, bukan potret saat diajukan.
   /// </summary>
   public partial class TestDocInfoPanel : UserControl, IApprovalPanelInput
   {
      private readonly ITestServices _service;

      public TestDocInfoPanel(ITestServices service) {
         _service = service;
         InitializeComponent();
      }

      public object? Input {
         get;
         set {
            field = value;
            _ = ShowAsync(value as string);
         }
      }

      private async Task ShowAsync(string? canonicalKey) {
         try {
            var id = string.IsNullOrWhiteSpace(canonicalKey) ? null : JsonSerializer.Deserialize<string[]>(canonicalKey)?.FirstOrDefault();
            var doc = id is null ? null : await _service.GetVi_TestDoc_ById(id);
            text.Text = doc is null
               ? "The document is not available."
               : $"No      : {doc.cTestDocNo}\nTitle   : {doc.cTestDocTitle}\nAmount  : {doc.cTestDocAmount:N2}\nStatus  : {doc.cTestDocStatus}" +
                 (doc.cTestDocQaPassed is { } passed ? $"\nQA      : {(passed ? "passed" : "did not pass")} {doc.cTestDocQaRemarks}" : "");
         }
         catch (Exception x) {
            text.Text = $"Could not read the document: {x.Message}";
         }
      }
   }
}
