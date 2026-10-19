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
   builder.AddSmtp();
   // Needs tables/060-objectstore.sql; turn off with "modules": { "objectStorage": false } or EM_MODULE_OBJECTSTORAGE=false.
   if (config.IsModuleEnabled("objectStorage", true)) {
      builder.AddObjectStorage();
   }

   // Log engine (telemetry, audit, Log Manager); needs tables/070-log-audit.sql and 071-log-store.sql.
   // Turn off with "modules": { "logServer": false } or EM_MODULE_LOGSERVER=false.
   if (config.IsModuleEnabled("logServer", true)) {
      builder.AddLogServer();
   }

   // Business Module Manager (tenants); needs tables/080-tenant.sql and views/vi_Tenant.sql.
   // Turn off with "modules": { "business": false } or EM_MODULE_BUSINESS=false.
   if (config.IsModuleEnabled("business", true)) {
      builder.AddBusinessModuleManager();
   }

   builder.AddLocalBinaryStorage(config.Storage.BinaryPath);
   if (config.IsModuleEnabled("test")) {
      builder.AddTestModule();
   }
});

app.Run();
