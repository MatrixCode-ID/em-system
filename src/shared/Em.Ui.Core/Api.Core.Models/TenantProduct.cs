using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// UI model of one tenant product. An application derives from it to lift its own fields out of
   /// <c>json_object</c>: read them with <c>GetJson</c> in <see cref="ReadFrom"/> and write them in
   /// <see cref="BuildJson"/>, always after calling <c>base</c>. Fields a model does not know are preserved.
   /// <para>
   /// The product actions need the Business Module Manager claim over HTTP. A wrapper with a claim of its own
   /// overrides <see cref="FetchAsync"/>, <see cref="UpdateAsync"/> and <see cref="InsertAsync"/> to go through its own
   /// service, and finishes both saves with <see cref="ApplySaved"/>.
   /// </para>
   /// </summary>
   public class TenantProduct : UiModel<vi_TenantProduct, ITenantProductServices>
   {
      #region Statics

      /// <summary>Gets one product by its id, or <c>null</c> when it does not exist.</summary>
      public static async Task<TenantProduct?> GetTenantProduct_ByIdAsync(IEmApp app, string productId) {
         var data = await app.ServiceProvider.GetRequiredService<ITenantProductServices>().GetVi_TenantProduct_ById(productId);
         return data != null ? new TenantProduct(app, data) : null;
      }

      /// <summary>Wraps a product view row in a model.</summary>
      public static TenantProduct Build(IEmApp app, vi_TenantProduct data) => new(app, data);

      /// <summary>
      /// Creates an unsaved product of a tenant. It reaches the server on the first save, and its parent cannot
      /// change afterwards.
      /// </summary>
      /// <param name="app">The application object.</param>
      /// <param name="tenantId">Tenant of the product.</param>
      /// <param name="parentId">Parent product; <c>null</c> for a root product.</param>
      public static TenantProduct NewBlank(IEmApp app, string tenantId, string? parentId = null) =>
         new(app, new vi_TenantProduct { cTenantId = tenantId, cTenantProductParentId = parentId }, true);

      #endregion

      /// <summary>Creates the model from a view row.</summary>
      /// <param name="app">The application object.</param>
      /// <param name="data">The row that fills the model.</param>
      /// <param name="blank">Whether the row has never been saved.</param>
      protected TenantProduct(IEmApp app, vi_TenantProduct data, bool blank = false) : base(app, data) {
         if (!blank) return;
         IsBlank = true;
         IsDirty = false;
      }

      #region Properties

      /// <summary>Id of the product; empty until the first save.</summary>
      public string cTenantProductId { get; private set => SetField(ref field, value); } = "";
      /// <summary>Tenant of the product; cannot change after creation.</summary>
      public string cTenantId { get; private set => SetField(ref field, value); } = "";
      /// <summary>Product type of the same tenant.</summary>
      public string cTenantProductTypeId { get; set => SetField(ref field, value); } = "";
      /// <summary>Parent product; cannot change after creation.</summary>
      public string? cTenantProductParentId { get; private set => SetField(ref field, value); }
      /// <summary>Unit of measure.</summary>
      public string cUnitId { get; set => SetField(ref field, value); } = "";
      /// <summary>Product code, unique within the tenant.</summary>
      public string cTenantProductCode { get; set => SetField(ref field, value); } = "";
      /// <summary>Product name.</summary>
      public string cTenantProductName { get; set => SetField(ref field, value); } = "";
      /// <summary>Active or inactive state.</summary>
      public int cTenantProductState { get; set { if (SetField(ref field, value)) OnPropertyChanged(nameof(IsActive)); } }
      /// <summary>Display order among the products with the same parent.</summary>
      public int cTenantProductOrder { get; set => SetField(ref field, value); } = -1;
      /// <summary>Optional note.</summary>
      public string? cTenantProductNote { get; set => SetField(ref field, value); }
      /// <summary>Code of the product type; read-only.</summary>
      public string cvTenantProductTypeCode { get; private set => SetField(ref field, value); } = "";
      /// <summary>Name of the product type; read-only.</summary>
      public string cvTenantProductTypeName { get; private set => SetField(ref field, value); } = "";
      /// <summary>Kind of the product, taken from its type; read-only.</summary>
      public ProductKind cvTenantProductTypeKind { get; private set => SetField(ref field, value); }
      /// <summary>Symbol of the unit; read-only.</summary>
      public string cvUnitSymbol { get; private set => SetField(ref field, value); } = "";
      /// <summary>Number of direct children; read-only.</summary>
      public int ccTenantProductChildCount { get; private set => SetField(ref field, value); }

      /// <summary>Whether the product is active.</summary>
      public bool IsActive => cTenantProductState == ta_TenantProduct.StateActive;

      #endregion

      #region Methods

      /// <inheritdoc />
      protected override void ReadFrom(vi_TenantProduct source) {
         cTenantProductId = source.cTenantProductId;
         cTenantId = source.cTenantId;
         cTenantProductTypeId = source.cTenantProductTypeId;
         cTenantProductParentId = source.cTenantProductParentId;
         cUnitId = source.cUnitId;
         cTenantProductCode = source.cTenantProductCode;
         cTenantProductName = source.cTenantProductName;
         cTenantProductState = source.cTenantProductState;
         cTenantProductOrder = source.cTenantProductOrder;
         cTenantProductNote = source.cTenantProductNote;
         cvTenantProductTypeCode = source.cvTenantProductTypeCode;
         cvTenantProductTypeName = source.cvTenantProductTypeName;
         cvTenantProductTypeKind = source.cvTenantProductTypeKind;
         cvUnitSymbol = source.cvUnitSymbol;
         ccTenantProductChildCount = source.ccTenantProductChildCount;
         ustamp = source.ustamp;
         datestamp = source.datestamp;
         json_object = source.json_object;
      }

      /// <inheritdoc />
      protected override void WriteTo(vi_TenantProduct target) {
         target.cTenantProductId = cTenantProductId;
         target.cTenantId = cTenantId;
         target.cTenantProductTypeId = cTenantProductTypeId;
         target.cTenantProductParentId = cTenantProductParentId;
         target.cUnitId = cUnitId;
         target.cTenantProductCode = cTenantProductCode;
         target.cTenantProductName = cTenantProductName;
         target.cTenantProductState = cTenantProductState;
         target.cTenantProductOrder = cTenantProductOrder;
         target.cTenantProductNote = cTenantProductNote;
         target.cvTenantProductTypeCode = cvTenantProductTypeCode;
         target.cvTenantProductTypeName = cvTenantProductTypeName;
         target.cvTenantProductTypeKind = cvTenantProductTypeKind;
         target.cvUnitSymbol = cvUnitSymbol;
         target.ccTenantProductChildCount = ccTenantProductChildCount;
         // SaveAsync refills ustamp with server time, while the product save compares the stamp last read.
         target.ustamp = Original.ustamp;
         target.datestamp = datestamp;
         target.json_object = json_object;
      }

      /// <inheritdoc />
      protected override JsonObject BuildJson(JsonObject patch) => patch;

      /// <inheritdoc />
      protected override Task<vi_TenantProduct?> FetchAsync() => Service.GetVi_TenantProduct_ById(cTenantProductId);

      /// <inheritdoc />
      protected override async Task UpdateAsync(vi_TenantProduct entity) =>
         ApplySaved(entity, await Service.PostGetTa_TenantProduct_Update(entity));

      /// <inheritdoc />
      protected override async Task InsertAsync(vi_TenantProduct entity) =>
         ApplySaved(entity, await Service.PostGetTa_TenantProduct_New(entity));

      /// <summary>
      /// Copies the row returned by a save to the row being saved and refreshes the model from it, so the next
      /// save carries the new stamp. Overrides of <see cref="UpdateAsync"/> and <see cref="InsertAsync"/> must call it.
      /// </summary>
      /// <param name="entity">The row received by the override.</param>
      /// <param name="saved">The row the server returned.</param>
      protected void ApplySaved(vi_TenantProduct entity, vi_TenantProduct saved) {
         entity.cTenantProductId = saved.cTenantProductId;
         entity.cTenantId = saved.cTenantId;
         entity.cTenantProductTypeId = saved.cTenantProductTypeId;
         entity.cTenantProductParentId = saved.cTenantProductParentId;
         entity.cTenantProductState = saved.cTenantProductState;
         entity.cTenantProductOrder = saved.cTenantProductOrder;
         entity.cvTenantProductTypeCode = saved.cvTenantProductTypeCode;
         entity.cvTenantProductTypeName = saved.cvTenantProductTypeName;
         entity.cvTenantProductTypeKind = saved.cvTenantProductTypeKind;
         entity.cvUnitSymbol = saved.cvUnitSymbol;
         entity.ccTenantProductChildCount = saved.ccTenantProductChildCount;
         entity.ustamp = saved.ustamp;
         entity.datestamp = saved.datestamp;
         entity.json_object = saved.json_object;
         Load(() => ReadFrom(entity));
      }

      #endregion
   }
}
