# Android packaging

The installable PWA/web URL is the primary Golden Hour AI deliverable. Capacitor is an optional shell around the same built React app; it does not introduce a separate emergency, clinical, authentication, or offline implementation.

No native Android project is checked in. The commands/workflows below generate one from `apps/web/dist` when JDK/Android tooling is available. The current Capacitor origin (`https://localhost`) has not been wired and device-tested against a deployed same-origin API/cookie-auth topology, so a generated package must not be described as a functional production client on the strength of packaging alone. No APK or AAB was produced during the recorded local verification.

- Application ID: `ai.goldenhour.app`
- App name: `Golden Hour AI`
- Web source: `apps/web/dist`
- Configuration: `apps/web/capacitor.config.ts`
- Native project when generated: `apps/web/android`
- Production keystore: **not included and must never be committed**

No APK or AAB is present or claimed merely because these files/workflows exist.

## Tooling

Use Node.js 22, npm 10+, JDK 21, and a current Android SDK/command-line tools installation accepted by the generated Capacitor 7 project. Set `ANDROID_HOME`/`ANDROID_SDK_ROOT` as required by your platform and accept only the licenses you are authorized to accept.

## Generate and sync the native project

From the repository root:

```powershell
npm ci --prefix apps/web
npm run build --prefix apps/web
Push-Location apps/web
if (-not (Test-Path android)) { npx cap add android }
npx cap sync android
Pop-Location
./android/patch-manifest.ps1 -ManifestPath apps/web/android/app/src/main/AndroidManifest.xml
```

`cap sync` copies the current PWA build. Run it after every web build that should enter an APK/AAB. Review generated Gradle and manifest diffs when upgrading Capacitor; the patch script intentionally fails instead of silently accepting an unexpected manifest.

## Permissions

The wrapper declares only:

- `INTERNET` for same-origin HTTPS API/SignalR access.
- `RECORD_AUDIO` for user-initiated, maximum 30-second incident recording.
- `ACCESS_COARSE_LOCATION` and `ACCESS_FINE_LOCATION` for a user-initiated location action.

Microphone/location hardware is optional. The app must explain the purpose immediately before the Android runtime prompt, support denial, and fall back to text/typed location. It must not request background location. The patch removes background location and legacy external-storage permissions, disables cleartext traffic, and disables Android cloud backup for the wrapper.

Re-review Data Safety disclosures, WebView behavior, SDK manifests, and every transitive plugin before Play distribution; merged manifests are the authority.

## Debug APK

After generation/sync/patch:

```powershell
./apps/web/android/gradlew.bat -p apps/web/android assembleDebug --no-daemon
```

On macOS/Linux:

```bash
./apps/web/android/gradlew -p apps/web/android assembleDebug --no-daemon
```

Only after success, the expected file is under:

```text
apps/web/android/app/build/outputs/apk/debug/app-debug.apk
```

Install on an explicitly authorized connected test device:

```powershell
adb install -r apps/web/android/app/build/outputs/apk/debug/app-debug.apk
```

The `.github/workflows/android-debug.yml` manual workflow performs the same build and uploads a short-lived workflow artifact. A successful workflow run and inspected artifact are required before reporting an APK.

## Release versioning

Android requires:

- `versionName`: user-visible semantic version, for example `0.1.0`.
- `versionCode`: monotonically increasing positive integer for every Play upload.

For a generated project:

```powershell
./android/set-version.ps1 `
  -BuildGradlePath apps/web/android/app/build.gradle `
  -VersionName 0.1.0 `
  -VersionCode 1
```

The signed workflow requires both values as manual inputs. It does not publish to Play automatically.

## Signed AAB secrets

Create and protect a production upload keystore outside this repository according to the repository owner's key-management policy. Add these GitHub **environment secrets** to a protected `android-release` environment:

| Secret | Value |
| --- | --- |
| `ANDROID_KEYSTORE_BASE64` | Base64 of the binary upload keystore, with no line wrapping |
| `ANDROID_KEYSTORE_PASSWORD` | Keystore password |
| `ANDROID_KEY_ALIAS` | Upload-key alias |
| `ANDROID_KEY_PASSWORD` | Key password |

The release workflow decodes the keystore only into the runner's temporary directory, writes ignored `key.properties`, applies `android/signing.gradle`, builds `bundleRelease`, verifies the archive signature, uploads the AAB artifact, and removes temporary signing files. GitHub masking is defense-in-depth; secrets must not be printed or passed as Gradle command-line arguments.

## Play internal testing

After a successful signed workflow:

1. Download and verify the workflow artifact and recorded SHA-256 digest in a controlled environment.
2. Confirm package ID `ai.goldenhour.app`, version name/code, upload certificate, merged permissions, privacy policy/Data Safety form, emergency disclaimers, offline behavior, and production API origin.
3. In Google Play Console, select the application, open **Testing → Internal testing**, create/select a release, and upload the verified AAB.
4. Add release notes that label this as a prototype, select only authorized testers, review warnings, and roll out to internal testing.
5. Test install/update, cookie/session behavior, microphone/location denial, offline mode, deep links, accessibility, and emergency-call behavior on real supported devices.

Do not promote beyond internal testing without clinical, privacy/legal, security, accessibility, provider, store-policy, and operational approval. A Play upload does not make the protocol clinically approved.
