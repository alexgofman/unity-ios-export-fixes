# Changelog

All notable changes to this package are recorded in this file. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the package uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-10-02

First public version. The code was taken out of the build pipeline of a shipped game and turned into a package.

### Added

- `PodfilePatcher.Patch`: a pure function that edits the Podfile of an iOS export. It adds a `post_install`
  block that raises pod deployment targets to a minimum iOS version, compared with `Gem::Version` and across
  all pod projects, and it declares dynamic-framework pods on the application target so that CocoaPods embeds
  them. It only inserts lines, keeps the line breaks of the file, extends an existing `post_install do |name|`
  hook instead of adding a second one, and can be run again on its own output.
- `PodfilePostProcessor`: a `[PostProcessBuild(45)]` hook that runs the patcher between EDM4U writing the
  Podfile (order 40) and EDM4U running `pod install` (order 50).
- `InfoPlistPostProcessor`: a `[PostProcessBuild(999)]` hook that writes `CFBundleLocalizations`,
  `CFBundleDevelopmentRegion` and `NSUserTrackingUsageDescription`.
- `IosExportFixesSettings`: a settings asset for the minimum version, the application target name, the embed
  rules, the language list and the two texts.
- EditMode tests for the Unity-free code and for the plist writer, and `DotnetTests~`: two plain .NET projects
  that run the former without Unity and check the patched Podfiles with a Ruby interpreter.
