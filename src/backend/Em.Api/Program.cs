using Em.Api.Core;
using Em.Test.Api;

/*
   Konfigurasi: salin emapi-config.example.json ke ..\.artefacts\em-system\config\emapi-config.json di sebelah
   repo, lalu isi database dan password awal admin. Build menyalin berkas itu ke output; tidak ikut publish.
   Environment variable EM_* mengesampingkan isi berkas (lihat Helper.cs).
 */

var app = EmApp.BuildApp(args, builder => {
   var config = Em.Api.Helper.ApplyConfig(builder);
   builder.AddManagedStorageSettings();
   // Requires doc/sqlscript/mssql/sets/NuPak.sql (schema version 2).
   builder.AddNuPak();

   // Penyimpanan berkas untuk PDF approval, dan module uji yang mencakup semua fitur engine.
   // Module uji butuh skrip doc/sqlscript/mssql/sets/EmTest.sql sudah dijalankan pada database inti.
   builder.AddLocalBinaryStorage(config.Storage.BinaryPath);
   builder.AddTestModule();
});

app.Run();
