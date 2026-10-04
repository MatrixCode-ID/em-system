using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Pemilih database untuk context-context yang berbagi satu koneksi di dalam transaksi approval:
   /// sebelum setiap perintah dijalankan, koneksinya dipindahkan ke database milik context yang
   /// menjalankannya.
   /// </summary>
   /// <remarks>
   /// Satu koneksi hanya berada di satu database pada satu saat, sedangkan model sebuah modul memakai
   /// nama tabel tanpa nama database. Karena itu perpindahannya tidak dilakukan sekali saat giliran
   /// diserahkan ke modul, tetapi di setiap perintah: handler modul yang menyentuh dua context di
   /// database berbeda tetap benar, dan perintah context inti sesudah giliran modul tidak salah alamat.
   /// Context yang tidak terdaftar di sini tidak disentuh sama sekali, sehingga di luar transaksi approval
   /// interceptor ini tidak berbuat apa-apa.
   /// </remarks>
   internal sealed class ApprovalDatabaseSwitch : DbCommandInterceptor
   {
      /// <summary>Satu-satunya instance; ditempelkan ke setiap context SQL Server yang dibuat engine.</summary>
      public static ApprovalDatabaseSwitch Instance { get; } = new();

      // Weak: a context lives as long as its request scope, and nothing here may keep it alive longer.
      private static readonly ConditionalWeakTable<DbContext, Attachment> Attachments = new();

      /// <summary>Database milik context itu dan koneksi bersama yang sedang dipakainya.</summary>
      internal sealed class Attachment(string database, DbConnection connection)
      {
         /// <summary>Database tempat model context ini hidup.</summary>
         public string Database { get; } = database;

         /// <summary>Koneksi bersama yang sudah dipasang ke context ini.</summary>
         public DbConnection Connection { get; set; } = connection;
      }

      /// <summary>
      /// Database milik sebuah context yang pernah dipasang, atau <c>null</c> kalau belum pernah. Dicatat
      /// sejak pemasangan pertama karena sesudahnya context itu memakai koneksi bersama, yang connection
      /// string-nya milik context inti.
      /// </summary>
      public static Attachment? Find(DbContext context) =>
         Attachments.TryGetValue(context, out var attachment) ? attachment : null;

      /// <summary>Mencatat bahwa <paramref name="context"/> memakai <paramref name="connection"/>.</summary>
      public static void Attach(DbContext context, string database, DbConnection connection) {
         if (Attachments.TryGetValue(context, out var attachment)) {
            attachment.Connection = connection;
            return;
         }

         Attachments.Add(context, new Attachment(database, connection));
      }

      public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
         InterceptionResult<DbDataReader> result) {
         Ensure(command, eventData);
         return result;
      }

      public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
         CommandEventData eventData, InterceptionResult<DbDataReader> result,
         CancellationToken cancellationToken = default) {
         await EnsureAsync(command, eventData, cancellationToken);
         return result;
      }

      public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData,
         InterceptionResult<int> result) {
         Ensure(command, eventData);
         return result;
      }

      public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
         CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) {
         await EnsureAsync(command, eventData, cancellationToken);
         return result;
      }

      public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData,
         InterceptionResult<object> result) {
         Ensure(command, eventData);
         return result;
      }

      public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
         CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) {
         await EnsureAsync(command, eventData, cancellationToken);
         return result;
      }

      private static void Ensure(DbCommand command, CommandEventData eventData) {
         if (TargetOf(command, eventData) is { } database) {
            command.Connection!.ChangeDatabase(database);
         }
      }

      private static async Task EnsureAsync(DbCommand command, CommandEventData eventData, CancellationToken ct) {
         if (TargetOf(command, eventData) is { } database) {
            await command.Connection!.ChangeDatabaseAsync(database, ct);
         }
      }

      // The database the command has to run in, or null when the connection is already there or the
      // context is not part of an approval transaction.
      private static string? TargetOf(DbCommand command, CommandEventData eventData) {
         if (eventData.Context is not { } context || command.Connection is not { } connection) return null;
         if (!Attachments.TryGetValue(context, out var attachment)) return null;

         return string.Equals(connection.Database, attachment.Database, StringComparison.OrdinalIgnoreCase)
            ? null
            : attachment.Database;
      }
   }

   /// <summary>
   /// Transaksi satu keputusan approval yang mencakup database inti dan database milik modul: satu
   /// koneksi, satu transaksi SQL Server, tanpa MSDTC.
   /// </summary>
   /// <remarks>
   /// Tanpa ini, hook modul yang menulis ke tabel modulnya berjalan di koneksi sendiri: gagal di tengah
   /// meninggalkan dokumen sudah berubah padahal request masih menunggu, atau request sudah selesai
   /// padahal perubahannya tidak pernah masuk. Context yang ikut serta adalah yang diminta service
   /// modulnya lewat constructor (lihat <see cref="ApprovalFlowDeclaration.ModuleDbContextTypes"/>);
   /// author modul tidak mengurus koneksi sama sekali. Batasannya hanya SQL Server, dan semua database
   /// yang ikut serta harus satu server dengan satu login; <see cref="ApprovalStartupChecks"/> menolak
   /// aplikasi yang melanggarnya sebelum menerima request pertama.
   /// <para>
   /// Context modul tetap memakai koneksi bersama itu sampai akhir permintaan, termasuk untuk hook
   /// sesudah commit; pemilih database di <see cref="ApprovalDatabaseSwitch"/> yang menjaga setiap
   /// perintahnya jatuh di database yang benar. Yang dilepas di akhir transaksi hanyalah transaksinya.
   /// </para>
   /// </remarks>
   internal sealed class ApprovalTransaction : IAsyncDisposable
   {
      private readonly IDbContextTransaction _transaction;
      private readonly List<DbContext> _enlisted;

      private ApprovalTransaction(IDbContextTransaction transaction, List<DbContext> enlisted) {
         _transaction = transaction;
         _enlisted = enlisted;
      }

      /// <summary>
      /// Membuka transaksi di database inti, lalu mengikutsertakan context-context milik modul
      /// <paramref name="flow"/>.
      /// </summary>
      /// <param name="core">Context database inti.</param>
      /// <param name="flow">Alur yang sedang dikerjakan; dari situ diketahui context modulnya.</param>
      /// <param name="provider">Penyedia service permintaan ini.</param>
      /// <param name="ct">Pembatalan pekerjaan.</param>
      public static async Task<ApprovalTransaction> BeginAsync(ApiCoreContext core, ApprovalFlowDeclaration flow,
         IServiceProvider provider, CancellationToken ct = default) {
         var transaction = await core.Database.BeginTransactionAsync(ct);
         var enlisted = new List<DbContext>();

         try {
            if (core.Database.GetDbConnection() is SqlConnection connection) {
               var coreDatabase = CatalogOf(connection.ConnectionString);
               ApprovalDatabaseSwitch.Attach(core, coreDatabase, connection);

               foreach (var type in flow.ModuleDbContextTypes) {
                  if (provider.GetRequiredService(type) is not DbContext module || ReferenceEquals(module, core)) continue;

                  if (!module.Database.IsSqlServer()) {
                     throw new InvalidOperationException(
                        $"Context {type.Name} of approval flow '{flow.DocType}' is not on SQL Server, so it cannot share the approval transaction.");
                  }

                  // Seen before in this request: its connection string is the shared one by now, so the
                  // database it owns is the one written down the first time.
                  var own = ApprovalDatabaseSwitch.Find(module)?.Database ??
                            CatalogOf(module.Database.GetConnectionString() ?? string.Empty);

                  if (!ReferenceEquals(ApprovalDatabaseSwitch.Find(module)?.Connection, connection)) {
                     module.Database.SetDbConnection(connection);
                  }

                  await module.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
                  ApprovalDatabaseSwitch.Attach(module, own, connection);

                  // Whatever an earlier decision of the same request left tracked is not what is on the
                  // tables now; a decision starts from what it reads itself.
                  module.ChangeTracker.Clear();
                  enlisted.Add(module);
               }
            }
         }
         catch {
            await ReleaseAsync(enlisted);
            await transaction.DisposeAsync();
            throw;
         }

         return new ApprovalTransaction(transaction, enlisted);
      }

      /// <summary>Menyimpan seluruh perubahan di semua database yang ikut serta.</summary>
      public Task CommitAsync(CancellationToken ct = default) => _transaction.CommitAsync(ct);

      /// <summary>
      /// Membatalkan seluruh perubahan di semua database yang ikut serta, dan membuang apa yang dilacak
      /// context-context modul: entitas yang sudah ditambah atau diubah di sana tidak ada lagi di
      /// database, dan tidak boleh ikut tersimpan oleh keputusan berikutnya dalam permintaan yang sama.
      /// </summary>
      public async Task RollbackAsync(CancellationToken ct = default) {
         try {
            await _transaction.RollbackAsync(ct);
         }
         finally {
            foreach (var module in _enlisted) module.ChangeTracker.Clear();
         }
      }

      /// <inheritdoc />
      public async ValueTask DisposeAsync() {
         await ReleaseAsync(_enlisted);
         await _transaction.DisposeAsync();
      }

      // A context left holding a finished transaction would refuse the next command it runs.
      private static async Task ReleaseAsync(List<DbContext> enlisted) {
         foreach (var module in enlisted) {
            await module.Database.UseTransactionAsync(null);
         }
      }

      private static string CatalogOf(string connectionString) =>
         new SqlConnectionStringBuilder(connectionString).InitialCatalog;
   }
}
