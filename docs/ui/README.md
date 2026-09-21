# Desktop UI — selected dark direction

共享页面动画的调用方式、预算与生命周期见 [轻量动画系统](MOTION.md)。

## Compact chat header (2026-09-21)

Text channels use one 42px header: channel tabs on the left, search and participant toggles fixed on the right. The repeated 52px channel title row is collapsed, giving that height to the conversation. Tabs scroll within the remaining width. macOS no longer reserves right-side window-button space; other platforms retain the existing 112px reserve. Non-chat page titles remain unchanged. No protocol or roadmap phase change.

Verified in a real macOS window: the duplicate title is absent, and both search and participant toggles work. Release validation used an isolated HEAD copy with these two layout files, the existing working-tree wallpaper fixes and a temporary correction of the baseline account-tooltip XAML. It built with one existing Watermark deprecation warning. The concurrently changing workspace build was blocked by unrelated edits; Windows/Linux layout and full workspace integration were not verified. No messages were sent.

## Settings

The settings button is fixed at the bottom of the instance rail. Clicking it while settings is open returns to the previous workspace view, matching the close button, Escape, and ⌘/,. The channel/account column stays visible; choosing a channel closes settings and opens that channel. Channel tabs stay hidden while settings occupies the main pane. This does not change the protocol or roadmap phase.

Settings has a 184px sidebar for General, Appearance, Keyboard, Voice, Profile and Server, with 42px navigation targets and a restrained 2px selection surface. The 28px section title and left-aligned body share a consistent starting edge, 40px from the sidebar. The header and body fill the remaining window width with 40px side insets; the body is inset from the vertical scrollbar so controls are not flush with the bar. The compact 32px close button stays at the page upper right. Form pages use the same 10px charcoal groups as Profile, with 52px inset rows and thin separators; Keyboard bindings stay in a 560px list with a 160px key column. The sidebar and header remain visible while the body scrolls.

- General: language and language packs stay in one grouped card, with styled dark popup menus and 14px text. Two-way selection updates the existing preferences; changing locale preserves option identity and updates the section title.
- Appearance: color scheme, compact layout, reduced motion and custom background share one grouped card. Dark/Light switches theme tokens immediately. Toggle and file rows are full-row targets; the color label opens the dropdown. Custom background accepts a dropped image or video (video files up to 2 GiB on disk). File picker and blur/brightness appear after the background is on. Still images decode at most 1920×1080, video at most 1280×720 / 20 fps so RAM stays bounded; the source file stays on disk. Blur/brightness sliders do not restart video. Playback pauses when the window is minimized, hidden, or reduced motion is on.
- Keyboard: send (Enter vs Ctrl/⌘ Enter) sits at the top. Workspace shortcuts are grouped; the whole row starts capture, keys render as compact caps, and a customized row shows a reset. Escape or a click outside cancels capture; Backspace clears; Reset all appears only when something has been changed. Overrides stay in `preferences.json`. Tab 1–9 and Escape stay reserved.
- Voice: existing input/output device selectors use the same dropdown dimensions; device errors remain visible. Headset media keys is an opt-in switch (default off): while in a voice channel it registers a system Now Playing session so headset play/pause mutes the microphone, and it clears on leave. This is presentation of existing mute, not new media functionality.
- Profile: username, avatar actions and display name share one full-width charcoal group with 10px corners and thin row separators. Labels and editors use consistent 2:3 columns; the 56px avatar and action buttons wrap within the editor column. Inputs and buttons use 6px corners; save/status remain outside the group. Signed-out authentication, existing bindings, Close and Escape are preserved.
- Server: the address field accepts an origin or a shareable `{origin}/join/{invite_code}` link. After a session is up, the page shows Connected, instance name, host, community, `/health/live` latency, protocol, and the community invite with copy. Copy writes the join link so another client can paste it into this field to discover the instance and join that community. Disconnect stops Gateway/voice for that instance and keeps the saved account so Connect can restore it. Changing the address while connected shows Connect again to switch.

Server connection status (2026-09-21): Release Chat.UI then Chat.App built with zero warnings/errors. 58 client foundation checks passed, including zh/en/ja copy and a loopback `/health/live` RTT probe. The LAN instance `http://10.19.144.83:8080/health/live` returned 200. Native window click-through of disconnect/reconnect was not repeated in this pass; Windows/Linux were not exercised.

Server invite link (2026-09-21): Settings → Server copies `{origin}/join/{code}`; the address field parses that link (and a bare 8-character code against the configured default instance). Release Chat.UI built with zero warnings/errors. 92 client foundation checks passed, including join-URL parse/format. Native click-through of copy/paste join was not repeated in this pass.

Verification (2026-09-21): Release build passed with zero warnings/errors. An Avalonia Headless + Skia harness rendered the production views at 908×738 and 688×546, including an expanded language menu. It checked dropdown selection writes, repeated-selection behavior, language option identity and toggle binding. Captures were inspected for alignment/overflow; the profile capture uses an explicit rendering fixture rather than a signed-in account.

Native macOS window reads timed out in the preceding verification attempt. The images below establish offscreen rendering, not live OS input, hardware device routing or server profile-save acceptance. No protocol or roadmap phase changes are part of this work.

Rendered evidence: [general](settings/general.png), [language dropdown](settings/general-dropdown.png), [appearance](settings/appearance.png), [general at minimum pane size](settings/general-small.png), [profile fixture at minimum pane size](settings/profile-small.png).

Layout polish (2026-09-21): Release compilation passed with zero warnings/errors. Native macOS checks covered General, Appearance and the signed-out Profile view, plus opening the language selector. [Previous native General screenshot](settings/general-polished-native.png). Earlier offscreen captures above document the previous spacing; signed-in profile submission and Windows/Linux were not revalidated for this presentation-only change.

Width regression fix (2026-09-21): removed the 560px cap that left an unused band on the right. Header and form now stretch to the available page width, retaining 40px side insets. Release build passed with zero warnings/errors; the corrected full-width layout was checked in a real macOS window. [Current full-width screenshot](settings/general-full-width-native.png).

Profile reference styling (2026-09-21): Release build passed with zero warnings/errors. The production Settings/Profile controls were inspected in a native macOS window using explicitly labeled local fixture data, including an existing sample avatar. [Native profile fixture](settings/profile-grouped-native-fixture.png). No account data was saved and no upload/removal endpoint was exercised; this establishes layout rendering, not server-write or cross-platform acceptance.

### Settings motion and switches

Settings control feedback is scoped in `SettingsMotion.axaml`. Switches use a 42×24 track, a 20px light thumb, subtle edge/shadow separation, an 18px travel over 180ms with CubicEaseOut, 160ms track/colour blending and 90ms press feedback. Sidebar selection markers animate opacity/height; dropdown arrows rotate, menus fade/slide in, buttons respond to press, and field focus/hover colours blend over 100–140ms.

`SettingsMotionScope` enables these transitions only while the settings view and ancestors are visible, the window is not minimized, and reduced motion is off. It unsubscribes on detach; removing the motion class cancels control transitions and snaps to the bound state. Rapid toggles retarget the ongoing transition. Page motion is owned by the shared MotionHost, so the old per-page fade is removed to avoid double animation. There are no looping UI animations or dedicated timers.

Verification (2026-09-21): Release build passed with zero warnings/errors. A focused offscreen rendering check recorded 102 real frames, confirmed intermediate thumb positions and final values after rapid reversal, a single visible page after rapid navigation, and transition removal when hidden/minimized or reduced motion is enabled. Intermediate frames were visually checked; a transparent-colour interpolation flash in sidebar selection was fixed. This does not establish native OS input acceptance. [Animated rendering](settings/motion.gif).

## Product surface polish (2026-09-21)

The production UI keeps its neutral dark palette and flat pane structure. Inputs and selectors now use an 8px radius, dropdown surfaces use 10px with inset 6px selection rows, and ordinary action buttons use 6px. `Styles/Inputs.axaml` owns the shared input and popup styles for settings, authentication and voice. The dropdown surface uses its open-state selector so the Fluent template does not override its padding and corner radius.

- Settings uses icon-and-label navigation, fewer separators and monochrome switches while retaining the existing motion and preference bindings.
- The connect screen has one full-width address field and primary action. Login/register uses a segmented selector. User-facing instance terminology is now server terminology in Chinese, English and Japanese; localization keys, discovery and protocol identifiers are unchanged.
- `Channels/CommunityMenu` owns the community flyout presentation. The signed-in header shows the account avatar, display name and handle; clicking the avatar or Change avatar opens the existing image picker (`PATCH /users/me`), and Remove avatar clears it. Invite codes copy to the clipboard. Create, join and moderation fields expand on demand; commands and server authorization remain unchanged. The menu scrolls within a bounded height and keeps operation feedback visible.
- Channel tabs use a short bottom selection marker. Message attachments and actions have softer corners; the composer no longer has a redundant internal separator. Voice options scroll at smaller window heights. Signed-out navigation hides empty channel groups.

Verification: the Release client build passed with zero warnings/errors; dependency boundaries and diff formatting passed. Native macOS checks covered the connect form, local server discovery, sign-in/register switching and settings navigation. The language popup appeared in the native accessibility tree, but the window screenshot did not include the separate popup; a focused Avalonia/Skia render confirmed its actual radius/padding, selection binding and the settings layout at a 908×598 pane. Profile fields in the render are explicitly a local fixture. No account registration, message send, moderation save, voice hardware or Windows/Linux acceptance was performed in this polish pass.

Evidence: [native authentication](polish/auth-native.png), [rendered dropdown](polish/general-dropdown.png), [minimum settings pane](polish/general-small.png), [switches](polish/appearance-small.png), [profile rendering fixture](polish/profile-fixture-small.png).

## Keyboard

Settings → Keyboard lists the workspace shortcuts and lets them be rebound. Click a key chip, then press a combination; Escape cancels capture, Backspace clears a binding, and Reset restores defaults. Overrides are stored in the client `preferences.json`. Send (Enter vs Ctrl/⌘ Enter) stays in General. Channel-editor dialogs keep their own Enter / Escape handling. Tab 1–9 and Escape stay reserved.

Default chords:

| Action | macOS | Windows / Linux |
| --- | --- | --- |
| Jump to channel | ⌘ K | Ctrl K |
| Search in channel | ⌘ F | Ctrl F |
| Previous / next channel | ⌥ ↑ / ⌥ ↓ | Alt ↑ / ↓ |
| Previous / next tab | Ctrl ⇧ Tab / Ctrl Tab | Ctrl Shift Tab / Ctrl Tab |
| Open tab 1–9 | ⌘ 1–9 | Ctrl 1–9 |
| Close tab | ⌘ W | Ctrl W |
| Settings | ⌘ , | Ctrl , |
| Toggle members | ⌘ ⇧ U | Ctrl Shift U |
| Attach file | ⌘ U | Ctrl U |
| Mute / deafen (in voice) | ⌘ ⇧ M / ⌘ ⇧ D | Ctrl Shift M / Ctrl Shift D |
| Close overlay | Escape | Escape |

⌘ K opens a filterable channel list over the workspace; typing filters by name, ↑/↓ moves the highlight, Enter opens a text channel or joins a voice channel, Escape or a click on the dimmed backdrop closes it. Selecting a text channel focuses the composer. Typing while focus is not in a field inserts into the composer. Middle-click closes a channel tab. Community-menu fields submit with Enter. An empty composer ↑ edits your last sent message (Escape cancels). Unread text channels are bold; mentions show a red mark. Right-click a text channel to mark read/unread or set All / Mentions / Mute. A new-messages divider and jump-to-present bar appear when you are not at the latest. Drafts persist per channel in the local cache.

## Message markup (2026-09-21)

The composer still sends plain UTF-8. The timeline renders a bounded subset in-process: `**bold**`, `_italic_`, `~~strike~~`, `` `code` ``, fenced blocks, headings, lists, quotes, simple tables, `||spoilers||`, `@username`, autolinks, `[label](https://…)`, and TeX between `$…$` / `$$…$$` (including `pmatrix`). Plain messages skip parsing. Parsed markup and math trees are cached (96 / 48 slots). Simple formulas such as `$x^2$` flatten to Unicode instead of a visual tree; fractions, roots and matrices stay as light panels. Unknown commands stay visible as source. Copy still copies the original source. HTML, markdown images, syntax highlighting and full TeX packages are **Not implemented yet**. Protocol and fixtures are unchanged.

## Automatic connection and account settings (2026-09-21)

The server field accepts `localhost` or `http://localhost` as a shortcut to the configured default instance, including its port; explicit ports and other hosts retain their meaning. Verified by entering `localhost` in a real macOS Release window: the field resolved to `http://10.19.144.83:8080/`, the connection succeeded, and the same account/channels remained active. Release build passed; 45 client foundation checks passed, including alias configuration and explicit-address boundaries.

Startup opens the real default channel without a credential form. The server rail plus button opens Settings → Server. Settings → Profile edits the current instance username, display name and avatar; the authentication form is only shown there for a signed-out instance. The Core connection coordinator stores the last successful server address and restores its session; a failed connection does not replace the current account or saved address. Settings → Server reflects that live session (connected/disconnected, name, latency) without adding a second connection protocol.

Verified in an actual macOS Release window with an isolated cache: first-use registration and default channel, saving username/display name to the LAN server, restarting into the same account, opening server settings via the plus button, failed connection feedback and reconnecting to the configured LAN endpoint. No messages were sent. Build passed with zero warnings/errors and client dependency checks passed. Windows/Linux, expired-session recovery and cross-server switching were not exercised in this pass. Existing development credential-file storage remains unchanged; OS vault support is still pending.

## Authentication form

The real authentication view uses a centered form capped at 380 logical pixels, with equal-width 46px inputs and a full-width light submit button. Sign-in and sign-up have separate tabs; display name appears only during sign-up. The form is available only in Settings → Profile when the selected instance needs account recovery; the startup page never asks for credentials. Existing localized labels are reused without additional explanatory copy.

`Auth/AuthFormViewModel` owns credential fields, mode, status and busy presentation state. Both tabs share one submission command, preventing concurrent sign-in/sign-up requests; authentication still runs through the existing instance session. Enter in the password field submits the selected mode. Small windows can scroll the form.

Validation (2026-09-20): Release desktop compilation completed with zero warnings/errors and client boundary checks passed. Native macOS visual/input verification was blocked because the Mac was locked; layout, focus, and tab interaction still require an unlocked-window check. This UI change does not advance the roadmap phase or change the protocol.

Implemented in the existing Avalonia desktop client. Neutral charcoal surfaces, a quiet violet active marker, server rail, grouped channel navigation, channel tabs, message timeline, member sidebar and account strip follow `selected-reference.png`. The native OS owns window controls; no fake traffic lights are drawn.

## Run

```sh
dotnet run --project src/client/App -c Release -- --design-preview
```

The explicit `--design-preview` flag loads bounded local UI fixtures, marks the window and account area as **设计预览**, skips instance/cache hydration, and blocks adding instances. No fixture data enters Core, the network, or SQLite. Preview channel/tab navigation, current-channel text search, group collapse and member visibility work locally. Sending, reactions, attachment/emoji actions, account settings and voice actions show **Not implemented yet**; drafts are preserved and no message or media success is invented.

For everyday UI work, run `./dev.command` (hot reload), `./start.command` (Release), or `dotnet run --project src/client/App`. All read `default_instance_url` from App/appsettings.json (currently `http://10.19.144.83:8080`), with `CHAT_DEFAULT_INSTANCE_URL` as an override. Launchers do not start or stop the server. Startup uses the last successfully selected server, falling back to the configured default. It restores the account or registers one on first use; an existing account with an expired session is never silently replaced. On a LAN instance without communities, it creates a real community and selects `general`. Settings → Profile edits the account; Settings → Server changes the connection and persists it. Discovery keeps its same-origin checks. `--design-preview` remains isolated.

The community menu now lets owners create text or voice channels through the existing API. Newly created text channels open immediately; creating voice channels does not join voice. Creating a community selects its own default text channel. These are server-backed operations, without fixture messages or permission bypasses; no protocol version change.

LAN startup verification (2026-09-21): Release build completed with zero warnings/errors; dependency boundaries and launcher syntax passed. A native process with an isolated cache connected to the configured LAN endpoint and synchronized its default channels. A focused check using the production startup/session/network code then restored the same LAN account, created and synchronized one text and one voice channel, and confirmed no voice join. The native window accessibility read timed out, so menu input and visual layout remain unverified. No messages were sent and no server process was changed.

## Components and assets

- `Shell/MainWindow`: layout, workspace keyboard routing, and shared transient notice.
- `Channels/QuickSwitcher`: ⌘ K / Ctrl K channel jump overlay.
- `Instances`: real instance rail and add-instance form; explicit preview server visuals.
- `Channels/ChannelSidebar`: grouped navigation with category chevrons and selected-row weight.
- `Chat/MemberSidebar`: conversation participants from loaded messages, with letter avatars when no image is present. Left-click opens a compact Discord-style user card (banner, overlapping avatar, display name, username, user ID); right-click offers mention, copy ID/username, own profile, and owner kick/ban entries that currently show Not implemented yet. This is not server membership or online presence.
- `Chat/ProfileDrawer`: account-bar profile is a 300px rounded popout above the account strip (banner, overlapping avatar, ID, compact fields). Full profile editing in Settings is unchanged.
- `Workspace`: tabs, conversation, member and account views with presentation state.
- `Styles/Theme.axaml`: dark tokens and focus/hover/selection presentation.
- `Styles/Icons.axaml`: official Microsoft Fluent System Icons geometries; MIT license in `Assets`.
- Five generated avatars are stored at 256px; the preview owns five 128px decoded bitmaps and disposes them at application exit. Normal mode does not decode them.
- The timeline uses Avalonia ListBox's virtualizing panel. Preview fixtures are bounded at five messages; real paging belongs to the future message pipeline.

## Verification

Release desktop build: zero warnings/errors. Client dependency boundaries pass. One native macOS visual smoke session covered populated preview and isolated empty normal startup. Seven focused presentation-state checks covered fixture isolation, loading, synchronized selection, search, member visibility, explicit unimplemented send/draft retention and notice dismissal.

Native OS mouse/keyboard automation was unavailable: the computer-use runtime could not start after the workspace move, and System Events denied accessibility. State checks do not establish OS input, screen-reader, Windows/Linux or chat/RTC acceptance. No deployment, SSH, microphone access or server changes were performed for this UI task.

See `../../design-qa.md` for the visual comparison and screenshot evidence.

### 侧栏与频道管理（2026-09-21）

已实现紧凑频道分组、2 px 选中行圆角和分组旁的新增按钮。所有者可添加文字/语音频道，文字频道右键打开、重命名、添加同类频道或删除。语音频道双击加入，右键「设置」打开设备/音质页；加入后文字频道仍留在主栏，成员列在该语音频道下方，账号条上方可离开。添加与重命名直接在侧栏原位输入，Enter 保存、Esc 取消，无遮罩和居中弹窗；删除在原行展开确认，失败就地显示错误，创建语音频道不会自动加入通话。管理请求走 HTTP，变更通过 Gateway 与 SQLite 同步。

成员栏使用 52 px 标题、会话作者计数（空时隐藏）和悬停行。没有头像时显示名字首字，不表示在线或社区成员名单。左键打开用户卡片（显示名、用户名、用户 ID），右键复制或提及；社区所有者对其他人可见踢出/封禁，这两项目前提示尚未实现。

验证：macOS Release 构建、本地 API / Gateway 聚焦检查；原生窗口见 [live](polish/sidebar-live.png) 与 [design preview](polish/sidebar-preview.png)。远端部署、PostgreSQL 实际删除和三平台窗口尚未验证。

语音交互（2026-09-21）：Release Chat.UI / Chat.App 零警告编译；86 项客户端契约检查通过。对本机 API 用两个账号加入同一语音频道，成员名单为 2，并签发 LiveKit JWT。`chat-media-worker` 现已链接 LiveKit Rust SDK；对端听筒还需要本机 LiveKit SFU（默认 `localhost:7880`）。Windows/Linux 窗口未验证。

## Account controls (2026-09-21)

The account bar opens an editable profile panel above the account area using the existing profile API. The current compact surface uses a 180ms slide/fade that can reverse during dismissal, respects reduced motion and stops when minimized. Escape and the backdrop dismiss it; focus cycles inside and returns to the opener.

Microphone/deafen controls work before joining voice and carry the chosen state into the next join. Undeafen restores the previous microphone preference. Hover resolves the selected device name (including the current system default); each icon has its own arrow: the microphone opens only inputs, and headphones open only outputs. Each 240px menu lists devices directly in 32px rows, marks the current selection and dismisses on selection; there is no nested dropdown. Enumeration runs on demand with coalesced refreshes, not an idle polling loop. These controls now drive a LiveKit client in `chat-media-worker`. Two-client hearing still needs a running LiveKit SFU; that window acceptance is not recorded here.

频道原位编辑保持透明背景，移除输入框底色、边框及整行下划线，仅保留文字、光标和保存/取消操作。

Validation: the isolated Release source snapshot compiled successfully (one existing `Watermark` deprecation warning). The 54 foundation checks included five checks that pre-join mute/deafen never call the API or media layer; module boundaries passed. In the real macOS window, controls toggled while a text channel was selected, both system devices were enumerated and explicitly selected, and a temporary local account's username/display name saved and survived closing/reopening the drawer. Avatar upload and remote audio were not re-tested. Device menus use a single overlay popup to retain focus without native popup windows.

The final shared-workspace Release build also passed with zero warnings and zero errors. Native interaction checks used the isolated snapshot; concurrent compact profile-card styling was retained and was not separately re-tested here.

Split device menus: Release build passed with zero warnings/errors. The real macOS window showed separate input/output lists; choosing the MacBook microphone or speakers updated the corresponding preference and dismissed the menu.

## Instance rail avatars (2026-09-21)

The left rail now binds each visited instance account avatar to the shared circular Avatar control. Missing avatars retain the instance-name initial. Replacement/removal refreshes the rail; identity changes invalidate its thumbnail. The rail owns up to 32 static 64px thumbnails (at most 512 KiB), reuses account downloads, and releases them when hidden or disposed. Participant avatars also receive playback updates before old images are disposed; obsolete download completions cannot restore a replaced/removed avatar.

Validation: Release desktop build passed with zero warnings/errors. An isolated local account uploaded two generated test images through the production session/API in a real macOS window; checks confirmed replacement reached the rail binding and removal cleared rail/account images. [Window render](polish/instance-avatar-native-render.png) was captured from the live control tree and visually checked for circular clipping; it is not an OS screenshot or mouse-input test. Existing-window accessibility inspection timed out. No chat messages were sent; Windows/Linux were not tested. No protocol or phase change.

Rail selection refinement: selected items use a quiet `#2C2C2C` fill (`#303030` on hover), 2px corners, no selection border and no avatar outline. The changed Avatar/InstanceRail XAML compiled in isolation against the preceding successful client build; a native host check confirmed zero selection border and hidden outline, and the [updated window render](polish/instance-selection-native-render.png) was visually inspected. The final full workspace build was blocked by concurrent message/inbox code errors (MessageText, ShellViewModel.Inbox and Presentation); that integration is not claimed as passing.

### 设置页空白频道列修复（2026-09-21）

恢复 ShellSurface 的 ShowSettings 折叠绑定；设置打开时频道栏宽度降为 0，关闭后恢复 220 px，保留短促过渡及减少动效偏好。折叠样式由可重建的 ShellSurface 自己持有，UltraLight 恢复后也适用，不改变资料页的登录/注册逻辑。

验证：使用 d0173f8 的独立源码快照加本次布局改动及当前 SettingsViewModel，完成 Release 构建、设置栏宽度归零与 UltraLight 恢复检查；macOS 原生验证窗口已核对资料页，空白列消失。隔离构建用于避开其他任务正在改写的源码/程序集，不代表所有并行改动已验收。
