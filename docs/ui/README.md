# Desktop UI — selected dark direction

共享页面动画的调用方式、预算与生命周期见 [轻量动画系统](MOTION.md)。

## Settings

The settings button is fixed at the bottom of the instance rail. Opening settings collapses the channel/account column from 220px to zero over 200ms (CubicEaseOut), reclaiming its width for settings; closing or Escape expands it again. The fixed-width channel contents are clipped and faded during the transition, with input disabled while collapsed. Rapid reversals continue from the current width. Reduced motion removes these transitions. The community header and channel tabs are hidden in settings. This layout update does not change the protocol or roadmap phase.

Shell layout verification (2026-09-21): Release build passed with zero warnings/errors using a separate artifacts directory to avoid concurrent builds. A real macOS window verified the bottom-left entry, collapsed settings layout, Escape restoring the channel/account column, and repeated open/close ending in the correct state. This checks native layout and input, not frame pacing or other desktop platforms.

Settings has a compact 148px sidebar for General, Appearance, Voice and Profile. The content area has a fixed section header and independently scrolling body. Labels and controls align in 72px rows; the sidebar remains visible while changing categories.

- General: language and send shortcut are collapsed 220×36 dropdowns, with styled dark popup menus. Two-way selection updates the existing preferences; changing locale preserves option identity and updates the section title.
- Appearance: compact layout and reduced motion use compact switches. Expanded radio tiles, keyboard illustrations and density diagrams have been removed. Category transitions use the shared MotionHost for a 160ms fade with an 8px entry offset, unless reduced motion is enabled.
- Voice: existing input/output device selectors use the same dropdown dimensions; device errors remain visible. This is presentation of existing device controls, not new media functionality.
- Profile: avatar actions, aligned username/display-name inputs, save/status footer and signed-out entry remain intact. Close and Escape return to the previous content.

Verification (2026-09-21): Release build passed with zero warnings/errors. An Avalonia Headless + Skia harness rendered the production views at 908×738 and 688×546, including an expanded language menu. It checked dropdown selection writes, repeated-selection behavior, language option identity and toggle binding. Captures were inspected for alignment/overflow; the profile capture uses an explicit rendering fixture rather than a signed-in account.

Native macOS window reads timed out in the preceding verification attempt. The images below establish offscreen rendering, not live OS input, hardware device routing or server profile-save acceptance. No protocol or roadmap phase changes are part of this work.

Rendered evidence: [general](settings/general.png), [language dropdown](settings/general-dropdown.png), [appearance](settings/appearance.png), [general at minimum pane size](settings/general-small.png), [profile fixture at minimum pane size](settings/profile-small.png).

### Settings motion and switches

Settings control feedback is scoped in `SettingsMotion.axaml`. Switches use a 42×24 track, a 20px light thumb, subtle edge/shadow separation, an 18px travel over 180ms with CubicEaseOut, 160ms track/colour blending and 90ms press feedback. Sidebar selection markers animate opacity/height; dropdown arrows rotate, menus fade/slide in, buttons respond to press, and field focus/hover colours blend over 100–140ms.

`SettingsMotionScope` enables these transitions only while the settings view and ancestors are visible, the window is not minimized, and reduced motion is off. It unsubscribes on detach; removing the motion class cancels control transitions and snaps to the bound state. Rapid toggles retarget the ongoing transition. Page motion is owned by the shared MotionHost, so the old per-page fade is removed to avoid double animation. There are no looping UI animations or dedicated timers.

Verification (2026-09-21): Release build passed with zero warnings/errors. A focused offscreen rendering check recorded 102 real frames, confirmed intermediate thumb positions and final values after rapid reversal, a single visible page after rapid navigation, and transition removal when hidden/minimized or reduced motion is enabled. Intermediate frames were visually checked; a transparent-colour interpolation flash in sidebar selection was fixed. This does not establish native OS input acceptance. [Animated rendering](settings/motion.gif).

## Authentication form

The real authentication view uses a centered form capped at 380 logical pixels, with equal-width 46px inputs and a full-width violet submit button. Sign-in and sign-up have separate tabs; display name appears only during sign-up. The selected instance name and host identify the destination. Existing localized labels are reused without additional explanatory copy.

`Auth/AuthFormViewModel` owns only mode and busy presentation state. Both tabs share one submission command, preventing concurrent sign-in/sign-up requests; authentication still runs through the existing instance session. Enter in the password field submits the selected mode. Small windows can scroll the form.

Validation (2026-09-20): Release desktop compilation completed with zero warnings/errors and client boundary checks passed. Native macOS visual/input verification was blocked because the Mac was locked; layout, focus, and tab interaction still require an unlocked-window check. This UI change does not advance the roadmap phase or change the protocol.

Implemented in the existing Avalonia desktop client. Neutral charcoal surfaces, a quiet violet active marker, server rail, grouped channel navigation, channel tabs, message timeline, member sidebar and account strip follow `selected-reference.png`. The native OS owns window controls; no fake traffic lights are drawn.

## Run

```sh
dotnet run --project src/client/App -c Release -- --design-preview
```

The explicit `--design-preview` flag loads bounded local UI fixtures, marks the window and account area as **设计预览**, skips instance/cache hydration, and blocks adding instances. No fixture data enters Core, the network, or SQLite. Preview channel/tab navigation, current-channel text search, group collapse and member visibility work locally. Sending, reactions, attachment/emoji actions, account settings and voice actions show **Not implemented yet**; drafts are preserved and no message or media success is invented.

Normal startup (without the flag) retains real instance discovery/cache behavior and shows no sample channels, people, avatars or messages. New users see the styled add-instance screen. This is UI work, not an advancement of chat/RTC backend implementation.

## Components and assets

- `Shell/MainWindow`: layout and shared transient notice only.
- `Instances`: real instance rail and add-instance form; explicit preview server visuals.
- `Channels/ChannelSidebar`: grouped navigation.
- `Workspace`: tabs, conversation, member and account views with presentation state.
- `Styles/Theme.axaml`: dark tokens and focus/hover/selection presentation.
- `Styles/Icons.axaml`: official Microsoft Fluent System Icons geometries; MIT license in `Assets`.
- Five generated avatars are stored at 256px; the preview owns five 128px decoded bitmaps and disposes them at application exit. Normal mode does not decode them.
- The timeline uses Avalonia ListBox's virtualizing panel. Preview fixtures are bounded at five messages; real paging belongs to the future message pipeline.

## Verification

Release desktop build: zero warnings/errors. Client dependency boundaries pass. One native macOS visual smoke session covered populated preview and isolated empty normal startup. Seven focused presentation-state checks covered fixture isolation, loading, synchronized selection, search, member visibility, explicit unimplemented send/draft retention and notice dismissal.

Native OS mouse/keyboard automation was unavailable: the computer-use runtime could not start after the workspace move, and System Events denied accessibility. State checks do not establish OS input, screen-reader, Windows/Linux or chat/RTC acceptance. No deployment, SSH, microphone access or server changes were performed for this UI task.

See `../../design-qa.md` for the visual comparison and screenshot evidence.
