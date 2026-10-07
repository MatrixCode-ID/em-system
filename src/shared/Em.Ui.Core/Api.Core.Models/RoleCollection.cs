using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.Extensions.DependencyInjection;
using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Container of one page of roles together with the numbers that go with it. Its content moves by
   /// itself on screen when a role is added or removed - the list is an
   /// <see cref="ObservableCollection{T}"/> and its changes are passed on through
   /// <see cref="INotifyCollectionChanged"/>.
   /// <para>
   /// Filtering and searching are deliberately not here: both are display matters, and the
   /// <c>CollectionView</c> in the view model already has its own route for them.
   /// </para>
   /// </summary>
   public class RoleCollection : IEnumerable<Role>, INotifyCollectionChanged
   {
      private readonly ObservableCollection<Role> _roles = [];

      private RoleCollection(IEmApp app) {
         App = app;
         _roles.CollectionChanged += (_, e) => CollectionChanged?.Invoke(this, e);
      }

      #region Statics

      /// <summary>
      /// Loads one page of roles together with the member count and right count of each role in it.
      /// </summary>
      /// <param name="app">The application object that owns the data service.</param>
      /// <param name="page">The page to load, starting from 1.</param>
      /// <param name="pageSize">Number of roles per page.</param>
      public static async Task<RoleCollection> LoadAsync(IEmApp app, int page = 1, int pageSize = 50) {
         var result = new RoleCollection(app) {
            Page = page < 1 ? 1 : page,
            PageSize = pageSize < 1 ? 50 : pageSize
         };
         await result.ReloadAsync();
         return result;
      }

      #endregion

      #region IEnumerable<T> Implementation

      /// <inheritdoc />
      public IEnumerator<Role> GetEnumerator() => _roles.GetEnumerator();

      IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

      #endregion

      #region INotifyCollectionChanged Implementation

      /// <inheritdoc />
      public event NotifyCollectionChangedEventHandler? CollectionChanged;

      #endregion

      #region Properties

      /// <summary>The application object this container lives in, the source of the DI container and the server time.</summary>
      public IEmApp App { get; }

      /// <summary>Number of roles on the page this container currently holds.</summary>
      public int Count => _roles.Count;

      /// <summary>Total number of roles on the server, not just those on this page.</summary>
      public int TotalCount {
         get;
         private set;
      }

      /// <summary>Total number of role assignments on the server - how many times roles are held by someone.</summary>
      public int TotalAssignments {
         get;
         private set;
      }

      /// <summary>The page currently loaded, starting from 1.</summary>
      public int Page {
         get;
         private set;
      } = 1;

      /// <summary>Number of roles per page.</summary>
      public int PageSize {
         get;
         private set;
      } = 50;

      /// <summary>The role at the given position in this page.</summary>
      public Role this[int index] => _roles[index];

      /// <summary>
      /// The role named <paramref name="name"/>. Role names are unique, so there is no possibility of two
      /// answers. A name that does not exist is treated as the caller's mistake - use
      /// <see cref="Contains"/> first when "not found" is a normal answer.
      /// </summary>
      /// <exception cref="InvalidOperationException">When there is no role with that name on this page.</exception>
      public Role this[string name] {
         get {
            var result = _roles.SingleOrDefault(r => r.cRoleName == name);
            if (result == null) {
               throw new InvalidOperationException(
                  $"There is no role named '{name}' in this collection. Check the spelling, or test with 'Contains' first.");
            }

            return result;
         }
      }

      /// <summary><c>true</c> when there is a role named <paramref name="name"/> on this page.</summary>
      public bool Contains(string name) => _roles.Any(r => r.cRoleName == name);

      #endregion

      #region Methods

      private ICredentialServices Service => App.ServiceProvider.GetRequiredService<ICredentialServices>();

      /// <summary>
      /// Reloads the page currently held, together with all its numbers.
      /// </summary>
      public async Task ReloadAsync() {
         var svc = Service;
         var rows = await svc.GetVi_Roles_InPage(Page, PageSize);

         _roles.Clear();
         foreach (var row in rows) {
            _roles.Add(Role.Build(App, row));
         }

         // One request for the numbers of the whole list, not one per role: a page holds dozens of roles, and
         // counting them one by one would mean dozens of trips to the server for two numbers per row.
         var counters = (await svc.GetMeta_RoleCounters()).ToDictionary(r => r.cRoleId);
         foreach (var role in _roles) {
            if (!counters.TryGetValue(role.cRoleId, out var counter)) continue;
            role.MemberCount = counter.MemberCount;
            role.ClaimCount = counter.ClaimCount;
         }

         TotalCount = await svc.GetTa_Roles_Count();
         TotalAssignments = await svc.GetTa_UserRoles_Count();
      }

      /// <summary>
      /// Moves to another page and loads it.
      /// </summary>
      /// <param name="page">The target page, starting from 1.</param>
      public Task GoToPageAsync(int page) {
         Page = page < 1 ? 1 : page;
         return ReloadAsync();
      }

      /// <summary>
      /// Creates a new role on the server and then adds it to this list, so a screen bound to it sees it
      /// immediately without reloading the whole page.
      /// </summary>
      /// <param name="name">Name of the new role. Role names are unique across the server.</param>
      /// <param name="description">A short note of its content, may be <c>null</c>.</param>
      public async Task<Role> NewRoleAsync(string name, string? description) {
         var role = Role.CreateNewRole(App);
         role.cRoleName = name;
         role.cRoleDescription = description;
         await role.SaveAsync();

         _roles.Add(role);
         TotalCount++;
         return role;
      }

      /// <summary>
      /// Adds a role whose object the caller has already created - usually a draft row that already lives on
      /// screen before there is anything on the server. A row that has never been saved is saved first here,
      /// so the caller does not need to remember the order "save first, then add".
      /// <para>
      /// A pair with <see cref="NewRoleAsync(string,string?)"/>, not its replacement: that one is for flows
      /// whose name is known up front, this one for flows whose object already exists first.
      /// </para>
      /// </summary>
      /// <param name="role">The role to add; may already be saved, may still be a draft.</param>
      /// <returns>The same role, already saved and already in this list.</returns>
      public async Task<Role> NewRoleAsync(Role role) {
         ArgumentNullException.ThrowIfNull(role);

         if (role.IsBlank) await role.SaveAsync();

         // Checked first, not simply added: calling this twice on the same object - e.g. because the first save
         // failed at another step and was repeated - must not produce two rows that are really one role.
         if (!_roles.Contains(role)) {
            _roles.Add(role);
            TotalCount++;
         }

         return role;
      }

      /// <summary>
      /// Deletes one role for real and then removes it from this list. Its rights and assignments disappear
      /// with it - see <see cref="Role.DeleteAsync"/>.
      /// </summary>
      public async Task RemoveAsync(Role role) {
         await role.DeleteAsync();

         // The assignment count drops by the number of members of the role that just disappeared, and that
         // number is already in hand - reading the whole page again just for two toolbar counters is a price
         // that need not be paid.
         TotalAssignments -= role.MemberCount;
         if (_roles.Remove(role)) TotalCount--;
      }

      #endregion
   }
}
