using Em.Api.Core;
using Em.Test.Api;

/*
   Configuration: copy emapi-config.example.json to ..\.artefacts\em-system\config\emapi-config.json next to the
   repo, then fill in the database and the initial admin password. The build copies that file to the output; it is not
   part of publish. EM_* environment variables override the file's content (see Helper.cs).
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
