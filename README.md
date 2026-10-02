# unity-ios-export-fixes

Editor build hooks for Unity iOS exports that get their native SDKs through CocoaPods and
[External Dependency Manager for Unity](https://github.com/googlesamples/unity-jar-resolver) (EDM4U).

| What goes wrong | What the package does |
|---|---|
| Xcode 27 stops the build: `The iOS deployment target 'IPHONEOS_DEPLOYMENT_TARGET' is set to 12.0, but the range of supported deployment target versions is 15.0 to 27.0.x.` | Adds a `post_install` block to the Podfile that raises every pod to the app's minimum iOS version. |
| The app builds, then stops at launch: `Library not loaded: @rpath/FBAudienceNetwork.framework/FBAudienceNetwork` | Declares the dynamic-framework pod on the `Unity-iPhone` target as well, so CocoaPods embeds it in the app. |
| The App Store takes an app's languages from the binary, and a Unity export declares none | Writes `CFBundleLocalizations` (and, if you want, the development region and the tracking-prompt text) to `Info.plist`. |

The Podfile logic is one pure function, `PodfilePatcher.Patch(string, PodfilePatchOptions)`. It has no Unity
dependency and is tested with plain `dotnet test`.

The code comes from the build pipeline of a free-to-play mobile game that is live on the App Store. For this
package the project-specific values became settings, the Podfile logic was separated from the Unity hook so
that it can be tested, and the patcher was reworked on the way: version comparison, line endings and the
handling of hooks that are already in the Podfile.

## The deployment-target error

CocoaPods gives each pod target the deployment target of that pod's own podspec, whatever the `platform :ios`
line of the Podfile says. Those are often lower than the app's. In the export this code was written for, 9 of
the 19 pods whose podspec could be checked declared a minimum between iOS 9.0 and 13.0, while the app's own
minimum was 15.0.

Xcode 27.0 does not build such a target:

```
Pods/Pods.xcodeproj: error: The iOS deployment target 'IPHONEOS_DEPLOYMENT_TARGET' is set to 12.0, but the range of supported deployment target versions is 15.0 to 27.0.x. (in target '<Pod>' from project 'Pods')
** BUILD FAILED **
```

What this rests on:

- Observed with Xcode 27.0 (27A266a) on 2 October 2026: a target at 12.0, 14.0 or 14.8 stops with this error, the
  same target at 15.0 builds. The iOS 27.0 SDK declares `MinimumDeploymentTarget` 15.0 in its `SDKSettings.json`.
- Apple's [Xcode 27 release notes](https://developer.apple.com/documentation/xcode-release-notes/xcode-27-release-notes)
  do not mention the limit. They point to the [Xcode support page](https://developer.apple.com/support/xcode/),
  whose table gives "iOS 15–27" as the deployment targets of Xcode 27.
- That table gives iOS 15 as the lower end for Xcode 16 and Xcode 26 too. How those versions treat a lower
  target was not checked here.

The fix is a block in the Podfile's `post_install` hook that sets `IPHONEOS_DEPLOYMENT_TARGET` to the app's
minimum on every pod target below it. Pod targets at or above the minimum keep their own value.

The pods are raised to the app's own minimum, not to a fixed number. The limit applies to the app's targets
too, so with Xcode 27 **Target minimum iOS Version** in Player Settings has to be 15.0 or higher in any case.

## The dynamic-framework crash

EDM4U declares every pod on the `UnityFramework` target. CocoaPods adds its `[CP] Embed Pods Frameworks` build
phase to application and test targets, not to a framework target. A pod that ships as a dynamic framework is
therefore linked into `UnityFramework` but never copied into the app, and dyld stops the app at launch.

Reproduced on the iOS 27.0 simulator with a stand-in dynamic framework called `DynVendor` (paths shortened):

```
dyld[63562]: Library not loaded: @rpath/DynVendor.framework/DynVendor
  Referenced from: <…> …/Unity-iPhone.app/Frameworks/UnityFramework.framework/UnityFramework
  Reason: tried: '…/Unity-iPhone.app/Frameworks/DynVendor.framework/DynVendor' (no such file), …
```

In the game this code comes from, the framework was `FBAudienceNetwork` (Meta Audience Network), which arrives as
a dependency of the LevelPlay adapter pod `IronSourceFacebookAdapter`. The 6.x release used there is a dynamic
xcframework with the install name `@rpath/FBAudienceNetwork.framework/FBAudienceNetwork`.

The fix is one more line in the Podfile: the pod is declared on the application target too, which puts it into
that target's embed phase. The rule only applies when the Podfile actually uses the pod (see
[Settings](#settings)). It is meant for pods that ship as dynamic frameworks; do not list a static pod, because
declaring one on both targets links its code into both binaries.

EDM4U has its own switch for pods that you declare yourself: `addToAllTargets="true"` on an `<iosPod>` entry of
a `*Dependencies.xml` file. That route was not tried here. The rule in this package covers the case where the
pod is only a dependency of somebody else's adapter.

## Before and after

A Podfile as EDM4U writes it, and what the hook adds (minimum iOS 15.0, one embed rule):

```diff
 source 'https://cdn.cocoapods.org/'

 platform :ios, '15.0'

 target 'UnityFramework' do
   pod 'Firebase/Analytics', '11.4.0'
   pod 'IronSourceSDK', '8.4.0'
   pod 'IronSourceFacebookAdapter', '4.3.47'
 end
 target 'Unity-iPhone' do
+  pod 'FBAudienceNetwork' # ios-export-fixes: dynamic framework, the app target has to embed it
 end
 use_frameworks! :linkage => :static
+
+# ios-export-fixes: build every pod for iOS 15.0 or later
+post_install do |installer|
+  installer.generated_projects.each do |project|
+    project.targets.each do |target|
+      target.build_configurations.each do |config|
+        next unless Gem::Version.correct?(config.build_settings['IPHONEOS_DEPLOYMENT_TARGET'].to_s)
+        next unless Gem::Version.new(config.build_settings['IPHONEOS_DEPLOYMENT_TARGET'].to_s) < Gem::Version.new('15.0')
+        config.build_settings['IPHONEOS_DEPLOYMENT_TARGET'] = '15.0'
+      end
+    end
+  end
+end
```

(The pod versions in this example are placeholders.)

Details that matter:

- **One hook only.** CocoaPods refuses a Podfile that registers two hooks
  (``Specifying multiple `post_install` hooks is unsupported.``). If the Podfile already has a
  `post_install do |name|` line, the block is inserted as the first statements of that hook and uses its block
  variable. A hook in any other shape (braces, no block variable, `post_install` mentioned on several lines) is
  left alone and reported as a warning, and no second hook is added.
- **Every pod project.** The block walks `installer.generated_projects`, not `installer.pods_project`. With the
  CocoaPods option `generate_multiple_pod_projects` the pod targets are in projects of their own, and with
  `incremental_installation` on top of it `pods_project` is nil when nothing has to be rebuilt.
- **Versions, not floats.** The comparison uses `Gem::Version`. As floats, `13.10` is smaller than `13.4`, and a
  third component is lost. A target with no setting at all is raised; a setting that is not a version number is
  left alone.
- **Comments are not code.** A hook, target or pod line that is commented out with `#`, sits inside a
  `=begin`/`=end` block or comes after `__END__` is ignored.
- **No version on the embed line** when the pod is only a dependency of another pod: CocoaPods resolves one
  version of a pod for the whole Podfile, and the dependent pod already constrains it. When the Podfile declares
  the pod itself on one line, that declaration is repeated, version or path included.
- **One declaration of the application target.** Without a `target 'Unity-iPhone' do` line the block is
  appended. If the Podfile names the target in some other way (in a loop over target names, with braces),
  nothing is added and a warning says so, because CocoaPods rejects a target that is declared twice.
- **Lines are only inserted.** No existing line is rewritten or removed; at most the last line gets the line
  break it was missing. Inserted lines use CRLF if the file does (going by its source code, EDM4U writes CRLF
  when the export is made on Windows).
- **Running it again is safe.** Each fix looks for its own result: the hook block is recognised by its comment
  line, an embed line by the pod already being on the application target. A second run changes nothing, and a
  later call with more rules adds only what is missing.
- **Checked input.** The minimum version, pod names and target name end up inside Ruby source, so they are
  validated first; a typo gives a clear message in the Unity console instead of a syntax error during
  `pod install`.

## Why callback order 45

EDM4U does its iOS work in `[PostProcessBuild]` steps with fixed orders (`IOSResolver.cs`, read on the master
branch on 2 October 2026, when the latest release was 1.2.190):

| Order | EDM4U step |
|---|---|
| 40 | writes the Podfile |
| 45 | **`PodfilePostProcessor` (this package)** |
| 50 | runs `pod install` |

A step that edits the Podfile has to run between the two. Any earlier and EDM4U overwrites the edit; any later
and the pods are already installed from the unedited file. EDM4U's README gives the same rule for scripts that
append to the Podfile and uses 45 in its own example.

The Info.plist hook runs at order 999, after the hooks of most other packages, so that a hook which rewrites
`Info.plist` earlier does not drop the keys again.

## The language list

Apple's [Technical Q&A QA1828](https://developer.apple.com/library/archive/qa/qa1828/_index.html) describes
where the App Store's "Languages" field comes from: the `.lproj` folders in the app bundle, or
`CFBundleLocalizations` for an app that manages its localised resources without `.lproj` folders. It does not
come from the store metadata.

Unity's iOS project template (checked in 6000.0.84f1) has no `CFBundleLocalizations` key and no `.lproj` folder
for the application target. A game with its own localisation system therefore has to add the key, which is
what `InfoPlistPostProcessor` does with the list from the settings asset.

A project that uses Unity's Localization package does not need this hook: with its App Info metadata set up,
that package writes `CFBundleLocalizations` and the `.lproj` folders itself (read in the source of version
1.5.8).

## Install

The package declares Unity 6000.0 as its minimum version; it was compiled against 6000.0.84f1. The Podfile hook
only has something to do in a project that uses EDM4U.

In the Package Manager choose **Install package from git URL** and enter the clone URL of this repository, or
copy the folder into `Packages/`.

Without any configuration the Podfile hook raises pod deployment targets to
**Player Settings > Other Settings > Target minimum iOS Version**. Everything else needs a settings asset.

### Settings

Create the asset with **Assets > Create > iOS Export Fixes > Settings** and keep it in an `Editor` folder.

| Field | Meaning | Default |
|---|---|---|
| Raise Pod Deployment Targets | Add the `post_install` block. | on |
| Minimum Deployment Target | Minimum iOS version for pods, e.g. `15.0`. Empty uses the Player Settings value. | empty |
| App Target Name | Application target of the exported project. | `Unity-iPhone` |
| Embedded Dynamic Pods | List of `pod` + `requiredBy`. The pod is declared on the application target when the Podfile declares the pod itself or one of the `requiredBy` pods. | empty |
| Localizations | Language identifiers for `CFBundleLocalizations`, e.g. `en`, `pt-BR`, `zh-Hans`. Empty leaves the key alone. | empty |
| Development Region | Value for `CFBundleDevelopmentRegion`. Empty leaves the key alone. | empty |
| User Tracking Usage Description | Text for `NSUserTrackingUsageDescription`. Empty leaves the key alone. | empty |

For Meta Audience Network behind the LevelPlay adapter: `pod` = `FBAudienceNetwork`, `requiredBy` =
`IronSourceFacebookAdapter`.

### Using the patcher from your own build script

```csharp
using System.IO;
using IosExportFixes;

var options = new PodfilePatchOptions { MinimumDeploymentTarget = "15.0" };
options.EmbeddedPods.Add(new EmbeddedPodRule("FBAudienceNetwork", "IronSourceFacebookAdapter"));

PodfilePatchResult result = PodfilePatcher.Patch(File.ReadAllText(podfilePath), options);
if (result.Changed) File.WriteAllText(podfilePath, result.Podfile);
// result.Summary, result.PostInstall, result.EmbeddedPods and result.Warnings say what happened.
```

Call it from a step with an order above 40 and below 50. The package's own hook at 45 does not get in the way:
whichever of the two runs second adds only what the first did not.

## Tests

```
dotnet test DotnetTests~
```

runs 164 test cases with no Unity installation. `DotnetTests~` holds two plain .NET projects: `Core` builds the
Unity-free part of the package (`Editor/Core`) as a .NET Standard 2.1 library with C# 9, which is what Unity 6
gives a package, and `DotnetTests.csproj` compiles the same test files Unity does (`Tests/Editor/Core`) against
it. The `~` hides the folder from the Unity editor.

| Tests | Cases | What they check |
|---|---|---|
| `PodfilePatcherTests` | 122 | The inserted text, on 13 fixture Podfiles and a number of small ones: plain export, existing hook with another block variable, hooks in other shapes, commented-out hooks and targets, no `Unity-iPhone` target, target declared in another way, dynamic pod not in use or already declared, nested targets, declarations over several lines, CRLF export, running twice, and the option checks. |
| `InfoPlistOptionsTests` | 7 | Clean-up of the language list and texts from the settings. |
| `RubyChecks` (`DotnetTests~` only) | 35 | Every patched fixture is run through `ruby` with a stand-in for the CocoaPods DSL (`podfile_harness.rb`): the file is valid Ruby, has one hook, the hook raises exactly the targets below the minimum, also when pods have projects of their own, an existing hook body still runs, and the embed line lands in the right target. Skipped when there is no `ruby` on the PATH. |
| `InfoPlistPatcherTests` | 5 | The plist writer, through Unity's `PlistDocument`. Compiled only while iOS is the active build target, so not part of `dotnet test`. |

To run the tests in the Unity Test Runner, add the package name to `testables` in the project's
`Packages/manifest.json`.

## Status and limits

What was run, on 2 October 2026:

- `dotnet test DotnetTests~`: 164 passed, 0 failed, 0 skipped, with Ruby 3.1.2 and again with Ruby 2.6.10. Without
  a `ruby` on the PATH the 35 Ruby checks are skipped and the other 129 pass.
- The editor and test assemblies were compiled with the C# compiler, reference assemblies and scripting defines
  of Unity 6000.0.84f1, with and without `UNITY_IOS`, for both API compatibility levels: no errors, no warnings.
- Those compiled test assemblies were then run outside the editor, on the Mono runtime and with the NUnit build
  that ship with Unity 6000.0.84f1: 134 passed with `UNITY_IOS` (the 129 cases shared with `dotnet test` plus the
  five `InfoPlistPatcherTests`, which use the real `UnityEditor.iOS.Extensions.Xcode.dll`), 129 without it.
- End to end, in a throwaway Xcode project shaped like a Unity export (an app target `Unity-iPhone`, a
  framework target `UnityFramework`, two local pods at iOS 12.0, one of them a dynamic xcframework), with
  CocoaPods 1.16.2 and Xcode 27.0: the unpatched Podfile fails with the deployment-target error; with the
  `post_install` block it builds, but the app stops at launch with the dyld error above; with the embed line as
  well it builds and launches on the iOS 27.0 simulator.
- `pod install` also accepted a patched Podfile with an existing hook (whose own body still ran), one with CRLF
  line breaks, and one with `generate_multiple_pod_projects` and `incremental_installation`, installed twice.

What was not:

- **The package has not been run inside the Unity editor in this form.** The hooks, the settings asset and its
  Create menu entry are compile-checked only. The in-game code they were extracted from runs in production
  builds, but it reads constants instead of a settings asset.
- Installing through the Package Manager and running the tests in the Unity Test Runner were not tried.
- The workflow in `.github/workflows/ci.yml` runs the `dotnet test` command above on GitHub.
- The embed rule was checked end to end with a pod that the Podfile declares directly. The case it was written
  for, a dynamic framework that is only a dependency of another pod, is covered by the text tests and by the
  production build of the game, not by a reproducible test here.
- The CRLF fixture follows EDM4U's source code. No export from a Windows editor was available.
- Other Xcode and CocoaPods versions than the ones named were not tried.

Known limits:

- iOS only. tvOS and visionOS exports are not touched.
- The Info.plist hook is compiled only while iOS is the active build target, because the Xcode API it uses comes
  with iOS Build Support. A build script that builds iOS while another target is active gets the Podfile hook,
  which then warns that the Info.plist keys were not written.
- The patcher reads the Podfile line by line and does not parse Ruby. It knows `#` comments, `=begin`/`=end`
  and `__END__`, but not heredocs or `%`-literals, and it cannot see a hook that is defined in another file and
  pulled in with `eval` or `require`.
- Only a hook written as `post_install do |name|` on a line of its own is extended. The block goes to the top of
  that hook, so code further down the hook that assigns `IPHONEOS_DEPLOYMENT_TARGET` still wins; a warning says
  so. A hook inside a conditional is extended with a warning that it only counts when the conditional runs.
- The inserted block calls `installer.generated_projects`. That accessor was checked in CocoaPods 1.10.1, 1.15.2
  and 1.16.2; the multi-project option it belongs to dates from CocoaPods 1.7. Older versions were not looked at.
- If another script edits the Podfile at the same callback order 45, Unity does not define which of the two
  runs first. A script that appends its own `post_install` hook after this one has run produces a Podfile with
  two hooks. Give that script a lower order, and its hook gets extended instead.
- The application target block is taken to end at the first `end` indented like its `target` line. That holds
  for EDM4U's output and for conventionally indented files.
- The hook block is not rewritten when the minimum changes while the Podfile still carries the block for the
  old minimum; a warning says so. EDM4U writes a fresh Podfile on every export, so this only concerns Podfiles
  that are kept between exports.
- Whether the App Store listing changes after `CFBundleLocalizations` is added is Apple's side of the process
  and cannot be tested here.

## Licence

MIT. See [LICENSE](LICENSE).
