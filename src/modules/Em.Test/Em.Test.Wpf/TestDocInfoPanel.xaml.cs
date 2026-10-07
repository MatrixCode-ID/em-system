using System.Text.Json;
using Em.Test.Models;
using Em.Ui.Core.Shared;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Test.Wpf
{
   /// <summary>
   /// An extra info card in the Approval Manager for test documents. The Approval Manager hands over the
   /// document key through <see cref="Input"/>; the card reads the document itself from the module, so what
   /// it shows is the current state of the document, not a snapshot from when it was submitted.
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
