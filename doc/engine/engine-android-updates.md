# Android application updates

MAUI hosts use their own EmAppId, a permanent Android ApplicationId and a SemVer Version.
Release Manager Platform Android publishes one signed arm64-v8a APK and a P-256-signed
release manifest. It embeds EmReleaseAddress (full release folder URL with trailing slash)
and EmReleasePublicKey (Base64 SPKI DER) in the host assembly, scoped to that host only.
Builds without both attributes, including ordinary debug builds, have no update source.

Before login (including session restore), EmApp reads the metadata from
Application.Current.GetType().Assembly. The shared ReleaseUpdateChecker downloads release.json
and release.json.sig without caching, under a five-second timeout. It checks the signature
over the original bytes before parsing, requires Android metadata and the installed package id,
and compares Android versionCode. An equal or older release is up to date.
A newer release is mandatory if the installed code is below minVersionCode, otherwise optional.
Startup check failures/timeouts are logged and allow login. Manual checks explain errors or
show Up to date / Updates are not configured for this build.

Update available offers Update now, Later and Exit. Later is offered again on the next start.
Update required blocks access to the application; only Update and Exit are available.
Downloads show progress, allow cancellation and can be retried. A downloaded APK must match
the signed size and SHA-256; corrupt or cancelled downloads are removed from the update cache.
AccountPanel and LoginControl display versionName (versionCode) and Check for updates.

The engine declares REQUEST_INSTALL_PACKAGES through an Android assembly UsesPermission.
Verify the merged manifest of a consuming host after building. Hosts whose tooling does not merge
library permissions must declare that permission in Platforms/Android/AndroidManifest.xml.
On Android 8+, enable Allow from this source when requested, return to the application and retry.
The engine writes to a PackageInstaller session, commits with a mutable package-scoped PendingIntent
and handles the runtime receiver's pending-user-action, success and failure statuses. Android owns
the final confirmation and enforces matching package signing certificates.

Only arm64-v8a is released. There is no Play Store/AAB flow, scheduled/background check,
rollback or downgrade. Fix a release by publishing a larger versionCode. Switching between a debug
keystore and a production keystore requires uninstalling the existing package first.
Visual interaction, device installation/update and actual environment installation require separate verification.

Maintainer notes: console smoke sources/output live under
../.artefacts/em-system/scripts/release-android-smoke/. PowerShell execution is stored in RunBay
actions build-release-android, test-release-android and smoke-release-android.
