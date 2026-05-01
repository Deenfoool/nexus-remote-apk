# Nexus Remote Android Release

## Build

```bat
gradlew.bat :app:assembleRelease :app:bundleRelease
```

Outputs:

```text
app\build\outputs\apk\release\app-release.apk
app\build\outputs\bundle\release\app-release.aab
```

## Signing

Release signing is configured through `keystore.properties`.

Local secret files are ignored by git:

```text
keystore.properties
*.jks
*.p12
*.keystore
```

Keep the release key safe. Losing it means future APK updates with the same package id will not install over old releases.
