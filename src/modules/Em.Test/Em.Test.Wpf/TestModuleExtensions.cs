using Em.Test.Models;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using FontAwesome6;
using FontAwesome6.Fonts.Extensions;
using System.Windows.Media;

namespace Em.Test.Wpf
{
   /// <summary>Pemasangan module uji ke aplikasi WPF.</summary>
   public static class TestModuleExtensions
   {
      private const string MenuRoot = "Em Test";

      /// <summary>
      /// Registers the client service, all navigations, and the approval panels of the test module. All
      /// navigations are bound to the module's base claim, so the menu and <c>NavigateTo</c> refuse a user who
      /// does not hold it.
      /// </summary>
      public static void AddTestModule(this EmAppBuilder builder) {
         ArgumentNullException.ThrowIfNull(builder);

         builder.AddServices<ITestServices, TestService>();

         // Managers appear in the menu; editors do not, because they only make sense with a payload.
         builder.AddNavigation<TestService>(Manager("test.home", "Em Test Console", "Probes, self-test and launcher",
            100, BodyType.Of<TestHome>(), EFontAwesomeIcon.Solid_FlaskVial, "Console"), ITestServices.RunClaim);
         builder.AddNavigation<TestService>(Manager("test.items", "Test Items", "Lists, paging, UiModel, data approval",
            110, BodyType.Of<TestItems>(), EFontAwesomeIcon.Solid_BoxesStacked, "Data"), ITestServices.RunClaim);
         builder.AddNavigation<TestService>(Manager("test.docs", "Test Documents", "Document approval and PDF stamp",
            120, BodyType.Of<TestDocs>(), EFontAwesomeIcon.Solid_FileSignature, "Data"), ITestServices.RunClaim);
         builder.AddNavigation<TestService>(Manager("test.tasks", "Test Tasks & CDN", "Business tasks and CDN files",
            130, BodyType.Of<TestTasks>(), EFontAwesomeIcon.Solid_ListCheck, "Tools"), ITestServices.RunClaim);
         builder.AddNavigation<TestService>(Manager("test.ui", "Test UI Lab", "PDF viewer, navigation, dialogs, controls",
            140, BodyType.Of<TestUiLab>(), EFontAwesomeIcon.Solid_Palette, "Tools"), ITestServices.RunClaim);

         builder.AddNavigation<TestService>(Editor("test.items.editor", "Test Item Editor", "Edit one test item",
            BodyType.Of<TestItemEditor>(), EFontAwesomeIcon.Solid_PenToSquare), ITestServices.RunClaim);
         builder.AddNavigation<TestService>(Editor("test.ui.child", "Test Child", "A navigation lifecycle probe",
            BodyType.Of<TestChild>(), EFontAwesomeIcon.Regular_WindowRestore), ITestServices.RunClaim);

         // Approval Manager extensions: an input panel for one step, an information card, and the way back
         // to the document from a request.
         builder.AddApprovalStepPanel<TestQaStepPanel, TestQaStepPanelVm>(ITestServices.DocType, "QA Check");
         builder.AddApprovalInfoPanel<TestDocInfoPanel>(ITestServices.DocType, input: host => host.Request.DocKey);
         builder.AddApprovalDocumentOpener(ITestServices.DocType, "test.docs", host => host.Request.DocKey);
      }

      private static Navigation Manager(string name, string title, string subtitle, int order, BodyType body,
         EFontAwesomeIcon icon, string group) => new() {
         Name = name,
         Title = title,
         Subtitle = subtitle,
         Description = subtitle,
         OrderIndex = order,
         BodyType = body,
         Kind = NavigationKind.Manager,
         RequireParameter = false,
         IsMenuVisible = true,
         MenuPath = MenuPath.Set($"{MenuRoot}/{group}"),
         NavigationIcon = icon.CreateImageSource(Brushes.Gray)
      };

      private static Navigation Editor(string name, string title, string subtitle, BodyType body,
         EFontAwesomeIcon icon) => new() {
         Name = name,
         Title = title,
         Subtitle = subtitle,
         Description = subtitle,
         OrderIndex = -1,
         BodyType = body,
         Kind = NavigationKind.Editor,
         RequireParameter = true,
         IsMenuVisible = false,
         NavigationIcon = icon.CreateImageSource(Brushes.Gray)
      };
   }
}
