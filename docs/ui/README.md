# Desktop UI — selected dark direction

## Settings

Settings now has a persistent header and category navigation, with a separately scrolling content pane capped at 600 logical pixels. The old content accidentally occupied the header grid row; the new layout explicitly places it beneath the header. Views and shared scoped styles live in `UI/Settings`:

- General: native-language radio choices and explicit Enter / Ctrl-or-Command+Enter send choices. Selecting the current send choice is idempotent; changing language updates existing option objects to retain keyboard focus.
- Appearance: compact layout and reduced motion use accessible `ToggleSwitch` controls bound to the existing persisted preferences. Section changes have a 120ms fade, disabled by reduced motion.
- Profile: avatar actions, aligned username/display-name fields, save action and visible success/error feedback. Signed-out users see the existing sign-in requirement. No fake profile is inserted into the live application.
- Close button and Escape return to the previous content; category selection stays local to the settings presentation model.

Verification (2026-09-20): desktop Release build passed; dependency boundaries and diff whitespace checks passed. A temporary Avalonia Headless + Skia harness rendered the actual production views at 908×738 and 688×546, checked radio selection idempotence, stable language option identity, and the two-way compact toggle binding. Screenshots were inspected for layout and overflow. The profile screenshot uses an explicit local rendering fixture, not a signed-in account.

Native macOS window/input verification remains blocked by the locked Mac. These captures establish offscreen rendering, not live OS keyboard/mouse or server profile-save acceptance. A separate `DesignPreviewWindow` runtime-loader warning appeared during the shared-checkout desktop build. No protocol or roadmap phase changes are part of this work.

Rendered evidence: [general](settings/general.png), [appearance](settings/appearance.png), [general at minimum pane size](settings/general-small.png), [profile layout fixture at minimum pane size](settings/profile-small.png).

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
