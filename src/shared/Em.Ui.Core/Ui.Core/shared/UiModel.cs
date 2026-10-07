using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Em.Shared;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Base class of all UI models that represent one data row of a table. Provides property change
   /// notification, the standard columns every table has, status markers (<c>IsBusy</c>,
   /// <see cref="IsDirty"/>) that can be bound directly, and a uniform read-write flow: keeping a copy of
   /// the original data, cancelling changes, saving to the server, and reloading. A derived class only
   /// needs to fill in the column mapping and two service calls.
   /// <para>
   /// Besides columns, this class also takes care of the content of <see cref="json_object"/> - including
   /// <see cref="UiIcon"/>, which lives inside that JSON and is not a column of its own.
   /// </para>
   /// </summary>
   /// <typeparam name="TEntity">The raw data type of the table this model represents.</typeparam>
   /// <typeparam name="TService">The module service interface that provides access to its data.</typeparam>
   public abstract class UiModel<TEntity, TService> : INotifyPropertyChanged
      where TEntity : class, new()
      where TService : class, IServices
   {
      #region Standard Columns

      /// <summary>Time of the last change of this row. Refilled automatically every time it is saved.</summary>
      public DateTime ustamp {
         get;
         set => SetField(ref field, value);
      }

      /// <summary>Time this row was first created.</summary>
      public DateTime datestamp {
         get;
         set => SetField(ref field, value);
      }

      /// <summary>Free additional data as JSON, for needs that have no column of their own.</summary>
      public string? json_object {
         get;
         set => SetField(ref field, value);
      }

      #endregion

      #region Json Object

      private JsonObject? _json;
      private string? _jsonSource;

      private JsonObject CurrentJson() {
         if (_json is not null && _jsonSource == json_object) return _json;

         _json = string.IsNullOrWhiteSpace(json_object)
            ? new JsonObject()
            : JsonNode.Parse(json_object) as JsonObject ?? new JsonObject();
         _jsonSource = json_object;
         return _json;
      }

      /// <summary>
      /// Reads one field from <see cref="json_object"/>. Used by derived classes inside
      /// <see cref="ReadFrom"/> to lift a JSON field into an ordinary property, so it can be bound and is
      /// tracked as a change like other columns.
      /// </summary>
      /// <typeparam name="T">The expected value type.</typeparam>
      /// <param name="fieldName">Name of the field inside the JSON.</param>
      /// <returns>The value of that field, or the default value if the field does not exist.</returns>
      protected T? GetJson<T>(string fieldName) {
         return CurrentJson().TryGetPropertyValue(fieldName, out var node) && node is not null
            ? node.Deserialize<T>()
            : default;
      }

      // Rebuilds json_object through BuildJson right before the model is mapped to raw data, and also makes
      // the result the new JSON cache so the next GetJson does not parse again. Deliberately does not mark
      // the model as changed: its content is only derived from properties that already exist, so calling
      // ToEntity() must not make the model dirty by itself.
      private void RefreshJson() {
         // Written before BuildJson so a derived class still has a last chance to change it, and removed when
         // not chosen so "not chosen" leaves no trace in the data.
         var patch = CurrentJson();
         if (UiIcon == UiIconType.Unspecified) patch.Remove("UiIcon");
         else patch["UiIcon"] = UiIcons.ToToken(UiIcon);

         var json = BuildJson(patch);
         var text = json.Count == 0 ? null : json.ToJsonString();

         var wasLoading = _isLoading;
         _isLoading = true;
         try {
            json_object = text;
         }
         finally {
            _isLoading = wasLoading;
         }

         _json = json;
         _jsonSource = text;
      }

      #endregion

      #region Ui Icon

      /// <summary>
      /// The icon chosen by the user for this row. Not a database column - its value lives inside
      /// <see cref="json_object"/> as the field <c>UiIcon</c>, and <see cref="UiIconType.Unspecified"/> means
      /// the user has not chosen anything.
      /// </summary>
      public UiIconType UiIcon {
         get;
         set {
            if (SetField(ref field, value)) OnPropertyChanged(nameof(EffectiveUiIcon));
         }
      }

      /// <summary>
      /// The default icon of this entity, used as long as the user has not chosen one. A derived class that is
      /// shown with an icon overrides it; one that is not need not - and nothing is written to the data
      /// because of it.
      /// <para>
      /// The default is attached to the entity, not to the screen, so the same row is not shown with a
      /// different icon on two different screens.
      /// </para>
      /// </summary>
      protected virtual UiIconType DefaultUiIcon => UiIconType.Unspecified;

      /// <summary>
      /// The icon that is actually drawn: the user's choice when there is one, otherwise the entity default.
      /// This is what is used for binding, not <see cref="UiIcon"/>.
      /// </summary>
      public UiIconType EffectiveUiIcon => UiIcon == UiIconType.Unspecified ? DefaultUiIcon : UiIcon;

      // Read directly from its JSON node, not through GetJson<string>: the content of json_object may come
      // from an import, from a manual SQL edit, or from another client, and Deserialize<string> would throw
      // as soon as the node turns out to be a number or an object. Anything other than text is treated as
      // absent.
      //
      // It falls back to Unspecified, not to DefaultUiIcon: if the entity default were written back to the
      // property, it would be saved on the next save - a row whose user never chose an icon would end up with
      // an icon written, and a default that changes later would no longer apply to that row.
      private void ReadUiIcon() {
         var token = CurrentJson().TryGetPropertyValue("UiIcon", out var node)
                     && node is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : null;

         UiIcon = UiIcons.Parse(token, UiIconType.Unspecified);
      }

      #endregion

      #region State

      /// <summary>
      /// Indicates there are changes that have not been saved. Becomes <c>true</c> automatically as soon as
      /// a data property changes, and returns to <c>false</c> after being saved or cancelled.
      /// </summary>
      public bool IsDirty {
         get;
         protected set {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
         }
      }

      /// <summary>
      /// Runs the filling of properties from a data source without marking the model as changed, then resets
      /// <see cref="IsDirty"/>. Used when loading data and when cancelling changes.
      /// </summary>
      /// <param name="loadAction">The action that fills the model's properties.</param>
      protected void Load(Action loadAction) {
         _isLoading = true;
         try {
            loadAction();
         }
         finally {
            _isLoading = false;
         }

         IsDirty = false;
      }

      private bool _isLoading;

      /// <summary>
      /// Indicates this row has never been saved on the server - it still lives in memory only, and the key
      /// it carries is only a placeholder until <see cref="SaveAsync"/> issues the real one.
      /// <para>
      /// Read by screens, not just derived classes: a row that is not yet born has no id to point anything
      /// at, so a screen needs to know which parts to lock and what to show in place of the missing key. Only
      /// derived classes change it.
      /// </para>
      /// </summary>
      public bool IsBlank {
         get;
         protected set => SetField(ref field, value);
      } = false;

      #endregion

      #region INotifyPropertyChanged Changed Handler

      /// <inheritdoc />
      public event PropertyChangedEventHandler? PropertyChanged;

      /// <summary>Raises the <see cref="PropertyChanged"/> event for a given property.</summary>
      /// <param name="propertyName">Name of the property that changed. Filled automatically with the caller's member name.</param>
      protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
         PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
      }

      /// <summary>
      /// Changes the value of a data property's backing field, then raises <see cref="PropertyChanged"/> and
      /// marks the model as changed (<see cref="IsDirty"/>) - unless running inside <see cref="Load"/>.
      /// Status properties such as <c>IsBusy</c> deliberately do not go through here.
      /// </summary>
      /// <typeparam name="T">Type of the property value.</typeparam>
      /// <param name="field">Reference to the backing field to change.</param>
      /// <param name="value">The new value.</param>
      /// <param name="propertyName">Name of the property, filled automatically with the caller's member name.</param>
      /// <returns><c>true</c> if the value really changed; <c>false</c> if it is the same as before.</returns>
      protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) {
         if (EqualityComparer<T>.Default.Equals(field, value)) return false;
         field = value;
         OnPropertyChanged(propertyName);
         if (!_isLoading) IsDirty = true;
         return true;
      }

      #endregion

      #region Data Access

      private TService? _service;

      /// <summary>
      /// A copy of the data as last known to equal what is stored on the server. It is the reference when
      /// changes are cancelled through <see cref="RollBack"/>.
      /// </summary>
      protected TEntity Original { get; private set; }

      /// <summary>
      /// Creates the model, binds it to the application object it lives in, then immediately fills it from
      /// raw data. Because the data is filled here, there is no separate initialization step that could be
      /// missed - the model is never in a half-finished state.
      /// </summary>
      /// <param name="app">The application object, the source of the DI container and the server time.</param>
      /// <param name="entity">The raw data that becomes the model's initial content.</param>
      /// <remarks>
      /// This constructor calls the virtual <see cref="ReadFrom"/>. That is safe because the field
      /// initializers of the derived class have already run before the base constructor. What has not run yet
      /// is the derived constructor body, so anything filled there - for example a reference to a parent
      /// model - is not yet available while the column mapping happens. For that reason
      /// <see cref="ReadFrom"/> may only read from its own parameter.
      /// </remarks>
      protected UiModel(IEmApp app, TEntity entity) {
         App = app;
         Original = entity;

         // ReSharper disable once VirtualMemberCallInConstructor
         Load(() => {
            ReadFrom(entity);
            ReadUiIcon();
         });
      }

      /// <summary>The application object this model lives in, the source of the DI container and the server time.</summary>
      public IEmApp App { get; }

      /// <summary>The module service this model uses, taken from the DI container the first time it is needed.</summary>
      internal TService Service => _service ??= App.ServiceProvider.GetRequiredService<TService>();

      #endregion

      #region Contract

      /// <summary>Copies the model's current property values to <paramref name="target"/>.</summary>
      /// <param name="target">The raw data to fill.</param>
      protected abstract void WriteTo(TEntity target);

      /// <summary>
      /// Fills the model's properties from <paramref name="source"/>. May only read from
      /// <paramref name="source"/>: this method runs from the base constructor, when the derived
      /// constructor body has not run yet, so anything filled there - including a reference to a parent
      /// model - is still empty at this point.
      /// </summary>
      /// <param name="source">The raw data that is the source of values.</param>
      protected abstract void ReadFrom(TEntity source);

      /// <summary>
      /// Fetches this data row again from the server. The derived class decides which key column is used,
      /// because only it knows the name of its key column.
      /// </summary>
      /// <returns>The latest raw data, or <c>null</c> if the row no longer exists.</returns>
      protected abstract Task<TEntity?> FetchAsync();

      /// <summary>Sends the changes of one data row to the server.</summary>
      /// <param name="entity">The raw data mapped from the model.</param>
      protected abstract Task UpdateAsync(TEntity entity);

      /// <summary>
      /// Builds the content of <see cref="json_object"/> that will be saved with it. Called automatically
      /// right before the model is mapped to raw data - including inside <see cref="SaveAsync"/> - so
      /// derived classes need not call it themselves. <paramref name="patch"/> already holds the current JSON
      /// content, only needing its fields added or removed; fields this model does not know - for example
      /// written by another version or another module - are carried along intact as long as they are not
      /// removed, so saving data never deletes content this model does not understand. A model that stores
      /// nothing in the JSON just returns <paramref name="patch"/> as it is, and one that wants to empty the
      /// content returns an empty <see cref="JsonObject"/>.
      /// </summary>
      /// <param name="patch">The current JSON content, ready for fields to be added or removed.</param>
      /// <returns>The JSON content to save. When empty, the column is saved as <c>null</c>.</returns>
      /// <example>
      /// <code>
      /// protected override JsonObject BuildJson(JsonObject patch) {
      ///    patch["Theme"] = Theme;
      ///    patch.Remove("FontSize");
      ///    return patch;
      /// }
      /// </code>
      /// </example>
      protected abstract JsonObject BuildJson(JsonObject patch);

      #endregion

      #region Methods

      /// <summary>Creates new raw data holding the model's current property values.</summary>
      /// <returns>The mapped raw data, ready to send to the server.</returns>
      public TEntity ToEntity() {
         var entity = new TEntity();
         RefreshJson();
         WriteTo(entity);
         return entity;
      }

      /// <summary>Cancels all unsaved changes, returning to the original data values.</summary>
      public void RollBack() {
         Load(() => {
            ReadFrom(Original);
            ReadUiIcon();
         });
      }

      /// <summary>
      /// Makes the model's current property values the new original data, without sending anything to the
      /// server. Used by derived classes when the change is already confirmed saved through another path
      /// (e.g. saving many rows at once).
      /// </summary>
      protected void Commit() {
         WriteTo(Original);
         IsDirty = false;
      }

      /// <summary>
      /// Saves the changes to the server. <see cref="ustamp"/> is refilled with the server time before the
      /// data is sent, and the original data is updated after sending succeeds.
      /// </summary>
      public async Task SaveAsync() {
         try {
            ustamp = await App.GetDateStampAsync();
            var entity = ToEntity();

            if (IsBlank) {
               await InsertAsync(entity);
               IsBlank = false;

               // The key and the stamps the insert filled in only exist on the entity, so the model
               // is refilled from it rather than left holding the placeholder it was created with.
               Load(() => {
                  ReadFrom(entity);
                  ReadUiIcon();
               });
            }
            else await UpdateAsync(entity);

            Original = entity;
            IsDirty = false;
         }
         catch {
            throw;
         }
      }
      
      /// <summary>Inserts the row on the server. Override in models whose rows can be created.</summary>
      protected virtual Task InsertAsync(TEntity entity) => Task.FromException(new NotImplementedException());

      /// <summary>
      /// Fetches the data from the server again, then discards all unsaved changes. A row that has never been
      /// saved has nothing to fetch, so for such a row this method does nothing. Input already typed is
      /// deliberately left alone: what was asked for is a reload, not clearing - to clear it use
      /// <see cref="RollBack"/>.
      /// </summary>
      /// <exception cref="InvalidOperationException">If the row no longer exists on the server.</exception>
      public async Task ResetAsync() {
         try {
            // A row that was never sent has nothing on the server to be re-read, and asking for it
            // by a key that does not exist yet would come back empty and read as a deleted row.
            // Nothing is thrown away either: losing what was typed would be a strange answer to a
            // request to refresh.
            if (IsBlank) return;

            Original = await FetchAsync()
                       ?? throw new InvalidOperationException(
                          $"{typeof(TEntity).Name} was not found on the server.");
            RollBack();
         }
         catch {
            throw;
         }
      }

      #endregion
   }
}