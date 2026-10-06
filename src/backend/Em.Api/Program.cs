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
   builder.AddNuPak();

   builder.AddLocalBinaryStorage(config.Storage.BinaryPath);
   if (config.IsModuleEnabled("test")) {
      builder.AddTestModule();
   }
});

app.Run();
