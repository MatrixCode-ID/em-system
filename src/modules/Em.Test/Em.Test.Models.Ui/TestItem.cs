using System.Text.Json.Nodes;
using Em.Shared;
using Em.Test.Models;
using Em.Ui.Core.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Test.Models.Ui
{
   /// <summary>
   /// Model UI satu item uji. Menunjukkan pola <see cref="UiModel{TEntity,TService}"/>: pelacakan perubahan,
   /// batal perubahan (<see cref="UiModel{TEntity,TService}.RollBack"/>), muat ulang dari server, dan baris
   /// baru yang baru lahir saat disimpan.
   /// </summary>
   public class TestItem : UiModel<vi_TestItem, ITestServices>
   {
      #region Statics

      /// <summary>Membuat item kosong yang belum tersimpan; barisnya baru ada di server setelah disimpan.</summary>
      public static TestItem CreateNew(IEmApp app) =>
         new(app, new vi_TestItem {
            cTestItemId = "Save To Generate ID",
            cTestItemState = TestItemState.Active,
            cTestItemQty = 1,
            cTestItemPrice = 1000m
         }) { IsBlank = true };

      public static TestItem Build(IEmApp app, vi_TestItem data) => new(app, data);

      public static async Task<TestItem?> GetByIdAsync(IEmApp app, string cTestItemId) {
         var data = await Api(app).GetVi_TestItem_ById(cTestItemId);
         return data is null ? null : Build(app, data);
      }

      /// <summary>Satu halaman item dari pencarian di server, bersama jumlah seluruh hasilnya.</summary>
      public static async Task<(TestItem[] Items, int Total)> SearchAsync(IEmApp app, TestItemQuery query) {
         var page = await Api(app).GetVi_TestItems_Search(query.Search, query.State, query.Page, query.PageSize);
         return ([.. page.Items.Select(r => Build(app, r))], page.Total);
      }

      private static ITestServices Api(IEmApp app) => app.ServiceProvider.GetRequiredService<ITestServices>();

      #endregion

      private TestItem(IEmApp app, vi_TestItem data) : base(app, data) { }

      private ITestServices Api() => Api(App);

      #region Properties

      public string cTestItemId {
         get;
         private set => SetField(ref field, value);
      } = string.Empty;

      public string cTestItemCode {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      public string cTestItemName {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      public int cTestItemQty {
         get;
         set => SetField(ref field, value);
      }

      public decimal cTestItemPrice {
         get;
         set => SetField(ref field, value);
      }

      public TestItemState cTestItemState {
         get;
         set => SetField(ref field, value);
      }

      public string cTestItemNote {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      #endregion

      #region Contract

      protected override void ReadFrom(vi_TestItem source) {
         cTestItemId = source.cTestItemId;
         cTestItemCode = source.cTestItemCode;
         cTestItemName = source.cTestItemName;
         cTestItemQty = source.cTestItemQty;
         cTestItemPrice = source.cTestItemPrice;
         cTestItemState = source.cTestItemState;
         cTestItemNote = source.cTestItemNote ?? string.Empty;
         ustamp = source.ustamp;
         datestamp = source.datestamp;
         json_object = source.json_object;
      }

      protected override void WriteTo(vi_TestItem target) {
         target.cTestItemId = cTestItemId;
         target.cTestItemCode = cTestItemCode;
         target.cTestItemName = cTestItemName;
         target.cTestItemQty = cTestItemQty;
         target.cTestItemPrice = cTestItemPrice;
         target.cTestItemState = cTestItemState;
         target.cTestItemNote = string.IsNullOrWhiteSpace(cTestItemNote) ? null : cTestItemNote;
         target.ustamp = ustamp;
         target.datestamp = datestamp;
         target.json_object = json_object;
      }

      protected override JsonObject BuildJson(JsonObject patch) => patch;

      protected override Task<vi_TestItem?> FetchAsync() => Api().GetVi_TestItem_ById(cTestItemId);

      protected override Task UpdateAsync(vi_TestItem entity) => Api().PostTa_TestItem_Update(entity);

      protected override Task InsertAsync(vi_TestItem entity) {
         entity.cTestItemId = $"{Ulid.NewUlid()}";
         entity.datestamp = entity.ustamp;
         return Api().PostTa_TestItem_New(entity);
      }

      #endregion
   }
}
