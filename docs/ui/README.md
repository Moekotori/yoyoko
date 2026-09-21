# Desktop UI — selected dark direction

共享页面动画的调用方式、预算与生命周期见 [轻量动画系统](MOTION.md)。

## Compact chat header (2026-09-21)

Text channels use one 42px header: channel tabs on the left, search and participant toggles fixed on the right. The repeated 52px channel title row is collapsed, giving that height to the conversation. Tabs scroll within the remaining width. macOS no longer reserves right-side window-button space; other platforms retain the existing 112px reserve. Non-chat page titles remain unchanged. No protocol or roadmap phase change.

Verified in a real macOS window: the duplicate title is absent, and both search and participant toggles work. Release validation used an isolated HEAD copy with these two layout files, the existing working-tree wallpaper fixes and a temporary correction of the baseline account-tooltip XAML. It built with one existing Watermark deprecation warning. The concurrently changing workspace build was blocked by unrelated edits; Windows/Linux layout and full workspace integration were not verified. No messages were sent.

## Member pane (2026-09-21)

The conversation member column is 300px (wider than the 260px channel nav), with 40px avatars and 14/12 name/handle type so long display names stay readable. It shares the chat canvas/wallpaper, keeps a left divider, and groups people as Online / Offline with counts. Loaded-message authors and the signed-in account sit under Online. Ten local layout names (Mika, 苏晚, Nova, 林栖迟, Alexander Whitfield online; 陈默, 江河, 月见里, Ryo, 阿布杜勒·拉赫曼 offline) fill the pane until presence exists. `start.cmd` / `dev.cmd` seed fixture channels (`日常`, `设计`, `深夜电台`), members and eight layout messages as soon as the window opens, before sign-in. Real community data replaces fixture channels after connect. They are not membership, not persisted, and do not appear in ⌘K or unread/read cursors. Presence protocol is **Not implemented yet**.

Release Chat.UI compiled with zero warnings/errors. Native window click-through and wallpaper contrast on Windows/macOS/Linux were not revalidated in this pass.

## Message layout (2026-09-21)

The timeline follows Discord: every message stays on the left. Wheel scrolling eases over 160ms instead of jumping 50px per notch, and the virtualizing panel keeps one extra viewport cached so rows do not pop in. The avatar occupies a fixed leading column; the name and timestamp sit on one line, with the body underneath. Consecutive messages from the same author hide the avatar and name but keep the column so wrapped text still lines up. Hover actions stay on the trailing edge: copy, reply, edit (own) and delete (own or community owner). Reply shows a one-line quote when the parent is still in the loaded window. Composer shows an edit/reply banner; typing from others appears above it. Listing a channel that returns 403/404 clears that channel cache and shows no access. Presence protocol is **Not implemented yet**.

Release Chat.UI compiled with zero warnings/errors. Native signed-in click-through on Windows/macOS/Linux was not repeated in this pass.

## Send motion and typing (2026-09-21)

Sending clears the composer and reply banner immediately and leaves the command free for the next Enter. The optimistic row appears at once; the timeline pins to the bottom for own sends (and for incoming messages only when already near the bottom), then follows one layout pass so virtualization does not leave a gap. Own sends do not play enter motion or insert the “新消息” divider. Incoming rows play a 120ms fade-and-rise via `ItemEnter`; pending text is slightly dimmed instead of inserting a “Sending” line. Cancel stays on the hover/pending toolbar. History pages and channel switches do not play enter motion. Reduced motion skips it. Composer keystrokes call `POST /channels/{id}/typing` at most once every 3s; peers see “X is typing…” above the composer for 8s or until that user sends. No looping dots. `TYPING_START` is not persisted.

Release Chat.UI compiled with zero warnings/errors. Native signed-in send click-through on Windows/macOS/Linux was not repeated in this pass.

## Settings

The settings button is fixed at the bottom of the instance rail. Clicking it while settings is open returns to the previous workspace view, matching Escape and ⌘/,. The redundant upper-right settings close button has been removed. Opening settings collapses the 260px channel/account column and the community title so the 240px settings sidebar sits next to the instance rail; the current instance or Escape closes settings and restores that column. Channel tabs stay hidden while settings occupies the main pane. This does not change the protocol or roadmap phase.

Settings takes the space freed by the channel column. The 240px sidebar lists General, Appearance, Keyboard, Voice, Profile and Server as 46px rows with 15px labels. The current section uses brighter text and icon; hover still uses a quiet fill, but selected no longer paints a full-row slab. The 26px section title and body share a 36px starting edge from the sidebar; the header and form still fill the remaining width. Form pages keep the same 12px raised groups as Profile, with 44px rows, 14px labels and a shared 240px control column so dropdowns, actions and switches line up on the right. Groups, fields, dropdowns and actions use 8–12px corners. The sidebar and header remain visible while the body scrolls.

- General: language and language packs stay in one grouped card, with styled dark popup menus and 14px text. Two-way selection updates the existing preferences; changing locale preserves option identity and updates the section title.
- Appearance: color scheme, compact layout, reduced motion and custom background share one grouped card. Dark/Light switches theme tokens immediately. Toggle and file rows are full-row targets; the color label opens the dropdown. Custom background accepts a dropped image or video (video files up to 2 GiB on disk). File picker and blur/brightness appear after the background is on. Still images decode at most 1920×1080, video at most 1280×720 / 20 fps so RAM stays bounded; the source file stays on disk. Blur/brightness sliders do not restart video. Playback pauses when the window is minimized, hidden, or reduced motion is on.
- Keyboard: send (Enter vs Ctrl/⌘ Enter) sits at the top in the same 240px control column. Workspace shortcuts are grouped; the whole row starts capture, keys render as compact caps, and a customized row shows a reset. Escape or a click outside cancels capture; Backspace clears; Reset all appears only when something has been changed. Overrides stay in `preferences.json`. Tab 1–9 and Escape stay reserved.
- Voice: input/output selectors share the 30×240 dropdown used on other settings pages; the device-check action stays content-sized and right-aligned in that column. Audio quality and the owner-only channel cap sit in the same group. Device errors remain visible. Headset media keys is an opt-in switch (default off): while in a voice channel it registers a system Now Playing session so headset play/pause mutes the microphone, and it clears on leave. This is presentation of existing mute, not new media functionality. Joining a voice channel opens that channel’s text chat instead of this settings page.
- Profile: a single settings group for the current account. A 120px banner strip sits above the 88px avatar well; both are clickable (hover camera scrim). Change/remove actions cover avatar and banner beside the preview name and handle. Display name and username use the shared 240px field column; save and status sit in the group footer. No sign-in or sign-up form. Signed-out instances show the same layout disabled.

Profile grouping (2026-09-21): settings groups use a 12px raised surface; fields, secondary actions and the save button use 8px corners; row dividers are inset. `Chat.UI` Release compiled with zero warnings/errors. An Avalonia Headless + Skia render of the production Settings/Profile view used a local signed-in fixture. [Rounded profile](settings/profile-rounded.png). This is offscreen layout evidence, not a native Windows click-through or profile-save acceptance.
- Server: the address field accepts an origin or a shareable `{origin}/join/{invite_code}` link. After a session is up, the page shows Connected, instance name, host, community, `/health/live` latency, protocol, and the community invite with copy. Copy writes the join link so another client can paste it into this field to discover the instance and join that community. Disconnect stops Gateway/voice for that instance and keeps the saved account so Connect can restore it. Changing the address while connected shows Connect again to switch.

Server connection status (2026-09-21): Release Chat.UI then Chat.App built with zero warnings/errors. 58 client foundation checks passed, including zh/en/ja copy and a loopback `/health/live` RTT probe. The LAN instance `http://10.19.144.83:8080/health/live` returned 200. Native window click-through of disconnect/reconnect was not repeated in this pass; Windows/Linux were not exercised.

Server invite link (2026-09-21): Settings → Server copies `{origin}/join/{code}`; the address field parses that link (and a bare 8-character code against the configured default instance). Release Chat.UI built with zero warnings/errors. 92 client foundation checks passed, including join-URL parse/format. Native click-through of copy/paste join was not repeated in this pass.

Verification (2026-09-21): Release build passed with zero warnings/errors. An Avalonia Headless + Skia harness rendered the production views at 908×738 and 688×546, including an expanded language menu. It checked dropdown selection writes, repeated-selection behavior, language option identity and toggle binding. Captures were inspected for alignment/overflow; the profile capture uses an explicit rendering fixture rather than a signed-in account.

Native macOS window reads timed out in the preceding verification attempt. The images below establish offscreen rendering, not live OS input, hardware device routing or server profile-save acceptance. No protocol or roadmap phase changes are part of this work.

Rendered evidence: [general](settings/general.png), [language dropdown](settings/general-dropdown.png), [appearance](settings/appearance.png), [general at minimum pane size](settings/general-small.png), [profile fixture at minimum pane size](settings/profile-small.png).

Layout polish (2026-09-21): Release compilation passed with zero warnings/errors. Native macOS checks covered General, Appearance and the signed-out Profile view, plus opening the language selector. [Previous native General screenshot](settings/general-polished-native.png). Earlier offscreen captures above document the previous spacing; signed-in profile submission and Windows/Linux were not revalidated for this presentation-only change.

Width regression fix (2026-09-21): removed the 560px cap that left an unused band on the right. Header and form now stretch to the available page width, retaining 40px side insets. Release build passed with zero warnings/errors; the corrected full-width layout was checked in a real macOS window. [Current full-width screenshot](settings/general-full-width-native.png).

Shortcut width regression fix (2026-09-21): the shortcut page still had its own 560px cap and left alignment, shrinking its groups to their desired width while the scrollbar stayed at the far right. Removed those page-local constraints so shortcut groups fill the available width like the other settings pages. Release client build passed with zero warnings/errors; verified the corrected page in a real macOS window using an isolated, disconnected client. No protocol or phase change; Windows/Linux were not revalidated.

Right-edge spacing correction (2026-09-21): removed the shared container's stacked 40px outer, 8px scroll and 20px content right margins. Settings content now extends toward the window edge with one 16px content inset, and the scrollbar sits at the edge. All sections share this layout; profile row sizing and full-width behavior are unchanged. Removed the upper-right close button. Release build passed with zero warnings/errors; an isolated native macOS window verified the shortcut page's right edge, absent close button and Escape returning to the workspace. Windows/Linux were not revalidated.

Profile reference styling (2026-09-21): Release build passed with zero warnings/errors. The production Settings/Profile controls were inspected in a native macOS window using explicitly labeled local fixture data, including an existing sample avatar. [Native profile fixture](settings/profile-grouped-native-fixture.png). No account data was saved and no upload/removal endpoint was exercised; this establishes layout rendering, not server-write or cross-platform acceptance.

### Settings motion and switches

Settings control feedback is scoped in `SettingsMotion.axaml`. Switches use a 42×24 track, a 20px light thumb, subtle edge/shadow separation, an 18px travel over 180ms with CubicEaseOut, 160ms track/colour blending and 90ms press feedback. Sidebar hover blends a quiet fill; selected state is text/icon contrast only. Dropdown arrows rotate, menus fade/slide in, buttons respond to press, and field focus/hover colours blend over 100–140ms. Section changes use a 140ms fade from 0.92 opacity plus an 8px directional slide that follows nav order.

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

⌘ K opens a filterable list. With no query it shows recently visited channels first. Typing filters channels by name and also matches people in the current conversation or voice roster; Enter on a person inserts an @mention. ↑/↓ moves the highlight, Enter opens a text channel or joins a voice channel, Escape or a click on the dimmed backdrop closes it. Selecting a text channel focuses the composer. Typing while focus is not in a field inserts into the composer. Middle-click closes a channel tab. Community-menu fields submit with Enter. An empty composer ↑ edits your last sent message (Escape cancels). Unread text channels are bold; mentions show a red mark. Right-click a text channel to mark read/unread or set All / Mentions / Mute. A new-messages divider and jump-to-present bar appear when you are not at the latest. Drafts persist per channel in the local cache.

## Message markup (2026-09-21)

Typing `@` in the composer opens a bounded picker for `@everyone`, `@here`, and matching community, conversation, or voice members (including display names and Unicode tokens); ↑/↓ moves the highlight, Enter or Tab inserts the token, Escape closes it. The composer still sends plain UTF-8. The timeline renders a bounded subset in-process: `**bold**`, `_italic_`, `~~strike~~`, `` `code` ``, fenced blocks, headings, lists, quotes, simple tables, `||spoilers||`, `@username`, `@everyone`, `@here`, autolinks, `[label](https://…)`, TeX between `$…$` / `$$…$$` (including `pmatrix`), and a safe HTML subset (`b`/`strong`, `i`/`em`, `u`, `s`/`del`, `code`, `pre`, `a href`, `p`, `br`, `h1`–`h3`, `ul`/`ol`/`li`, `blockquote`, `table`, plus common entities). Script/style/iframe, event handlers, `javascript:` links and remote `<img src>` are dropped; `<img alt>` keeps the alt text only. Clicking `@user` opens the member card. Messages that mention you, `@everyone`, or `@here` use a light row tint and stronger chips. Plain messages skip parsing. Parsed markup and math trees are cached (96 / 48 slots). Simple formulas such as `$x^2$` flatten to Unicode instead of a visual tree; fractions, roots and matrices stay as light panels. Unknown commands stay visible as source. Copy still copies the original source. Markdown images, syntax highlighting and full TeX packages are **Not implemented yet**. Protocol Message DTO includes optional `mention_everyone` and `mention_here`.

## Automatic connection and account settings (2026-09-21)

The server field accepts `localhost` or `http://localhost` as a shortcut to the configured default instance, including its port; explicit ports and other hosts retain their meaning. Verified by entering `localhost` in a real macOS Release window: the field resolved to `http://10.19.144.83:8080/`, the connection succeeded, and the same account/channels remained active. Release build passed; 45 client foundation checks passed, including alias configuration and explicit-address boundaries.

Startup opens the real default channel without a credential form. The server rail plus button opens Settings → Server. Connecting to a server without a saved account shows a username field; empty uses `User`, and a taken default becomes `User2` and so on. Settings → Profile edits the current instance avatar, banner, display name and username. The Core connection coordinator stores the last successful server address and restores its session; a failed connection does not replace the current account or saved address. Settings → Server reflects that live session (connected/disconnected, name, latency) without adding a second connection protocol.

Verified in an actual macOS Release window with an isolated cache: first-use registration and default channel, saving username/display name to the LAN server, restarting into the same account, opening server settings via the plus button, failed connection feedback and reconnecting to the configured LAN endpoint. No messages were sent. Build passed with zero warnings/errors and client dependency checks passed. Windows/Linux, expired-session recovery and cross-server switching were not exercised in this pass. Existing development credential-file storage remains unchanged; OS vault support is still pending.

## Authentication form

Settings → Profile no longer shows sign-in or sign-up. Account creation remains the first-use auto-register path; Settings → Server asks for a username when the instance has no saved account, defaulting to `User`. Profile is avatar, banner, display name and username only. `Auth/AuthFormViewModel` is still constructed for Core session sign-in, but the settings page does not present those fields. The startup page never asks for credentials.

Implemented in the existing Avalonia desktop client. Neutral charcoal surfaces, a quiet violet active marker, server rail, grouped channel navigation, channel tabs, message timeline, member sidebar and account strip follow `selected-reference.png`. The native OS owns window controls; no fake traffic lights are drawn.

## Run

```sh
dotnet run --project src/client/App -c Release -- --design-preview
```

The explicit `--design-preview` flag loads bounded local UI fixtures, marks the window and account area as **设计预览**, skips instance/cache hydration, and blocks adding instances. No fixture data enters Core, the network, or SQLite. Preview channel/tab navigation, current-channel text search, group collapse and member visibility work locally. Sending, reactions, attachment/emoji actions, account settings and voice actions show **Not implemented yet**; drafts are preserved and no message or media success is invented.

For everyday UI work, run `dev.cmd` / `./dev.command` (hot reload), `start.cmd` / `./start.command` (Release), or `dotnet run --project src/client/App`. All read `default_instance_url` from App/appsettings.json (currently `http://localhost:8080`), with `CHAT_DEFAULT_INSTANCE_URL` as an override. Windows `start.cmd` also starts the local LAN server; macOS `start.command` does not. Startup uses the last successfully selected server, falling back to the configured default. It restores the account or registers one on first use (username from Settings → Server, otherwise `User`); an existing account with an expired session is never silently replaced. On a LAN instance without communities, it creates a real community and selects `general`. Settings → Profile edits the account; Settings → Server changes the connection and persists it. Connecting and startup connect can be cancelled from that page (or Escape); a cancelled attempt leaves the previous session untouched. Discovery keeps its same-origin checks. `--design-preview` remains isolated.

The community menu now lets owners create text or voice channels through the existing API. Newly created text channels open immediately; creating voice channels does not join voice. Creating a community selects its own default text channel. These are server-backed operations, without fixture messages or permission bypasses; no protocol version change.

LAN startup verification (2026-09-21): Release build completed with zero warnings/errors; dependency boundaries and launcher syntax passed. A native process with an isolated cache connected to the configured LAN endpoint and synchronized its default channels. A focused check using the production startup/session/network code then restored the same LAN account, created and synchronized one text and one voice channel, and confirmed no voice join. The native window accessibility read timed out, so menu input and visual layout remain unverified. No messages were sent and no server process was changed.

## Components and assets

- `Shell/MainWindow`: layout, workspace keyboard routing, and shared transient notice.
- `Channels/QuickSwitcher`: ⌘ K / Ctrl K channel jump overlay.
- `Instances`: real instance rail and add-instance form; explicit preview server visuals.
- `Channels/ChannelSidebar`: grouped navigation with category chevrons and selected-row weight.
- `Chat/MemberSidebar`: 300px conversation authors plus the signed-in account, grouped Online / Offline. 40px letter avatars, 14px names and 12px handles. Ten local layout names fill the list; they are not persisted or sent to the server. Left-click opens a 360px user card (16px corners, custom JPEG/PNG/GIF/WebP banner or ID-tinted fallback, overlapping avatar, presence, primary action, labeled user ID). Own card can change the banner. Right-click offers message, mention, copy ID/username, own profile, and owner kick/ban entries that currently show Not implemented yet. Real accounts open a 1:1 DM; fixture names cannot. Server membership and presence are **Not implemented yet**.
- `Chat/ProfileDrawer`: account-bar profile is a 360px rounded popout above the account strip (custom JPEG/PNG/GIF/WebP banner, overlapping avatar, ID, compact fields). Click the banner to change it. Full profile editing in Settings is unchanged.
- `Components/Banner`: rectangular cover with color fallback and optional GIF playback; used by the user card, profile drawer and Settings → Profile.
- `Workspace`: tabs, conversation, member and account views with presentation state.
- `Styles/Theme.axaml`: dark tokens and focus/hover/selection presentation.
- `Styles/Icons.axaml`: official Microsoft Fluent System Icons geometries; MIT license in `Assets`.
- Five generated avatars are stored at 256px; the preview owns five 128px decoded bitmaps and disposes them at application exit. Normal mode does not decode them.
- The timeline uses Avalonia ListBox's virtualizing panel. Design-preview fixtures stay in the preview window; the live shell prepends eight local layout messages that never enter Core, SQLite, or the network. Real paging belongs to the message pipeline.

## Verification

Release desktop build: zero warnings/errors. Client dependency boundaries pass. One native macOS visual smoke session covered populated preview and isolated empty normal startup. Seven focused presentation-state checks covered fixture isolation, loading, synchronized selection, search, member visibility, explicit unimplemented send/draft retention and notice dismissal.

Native OS mouse/keyboard automation was unavailable: the computer-use runtime could not start after the workspace move, and System Events denied accessibility. State checks do not establish OS input, screen-reader, Windows/Linux or chat/RTC acceptance. No deployment, SSH, microphone access or server changes were performed for this UI task.

See `../../design-qa.md` for the visual comparison and screenshot evidence.

### 侧栏与频道管理（2026-09-21）

频道/账号列为 260 px，给底部头像、用户名和麦克风/耳机控件留出间距。已实现紧凑频道分组、2 px 选中行圆角和分组旁的新增按钮。所有者可添加文字/语音频道，文字频道右键打开、重命名、添加同类频道或删除。语音频道单击加入并打开该频道的文字聊天；右键可复制网页语音链接，或打开设置中的设备/音质页。成员列在该语音频道下方，账号条上方可离开。添加与重命名直接在侧栏原位输入，Enter 保存、Esc 取消，无遮罩和居中弹窗；删除在原行展开确认，失败就地显示错误，创建语音频道不会自动加入通话。管理请求走 HTTP，变更通过 Gateway 与 SQLite 同步。

成员栏与消息区共用画布/壁纸，按「在线 / 离线」分组并显示人数。当前账号和已加载消息的作者列在在线；另有十个本地占位名（Mika、苏晚、Nova、林栖迟、Alexander Whitfield 在线，陈默、江河、月见里、Ryo、阿布杜勒·拉赫曼离线）只用于排版。`start.cmd` / `dev.cmd` 打开窗口即出现假频道（日常、设计、深夜电台）、假成员和八条假消息，不必等登录；连上真实社区后会换成真频道。这些数据不入库、不上网、不进 ⌘K，也不参与已读游标。没有头像时显示名字首字。左键打开 300px 圆角用户卡片（横幅、叠放头像、在线状态、发私信/提及和用户 ID）；右键可发私信、提及或复制。真实账号会 get-or-create 一条私信频道并出现在频道列「私信」分组；占位名不能私信。社区所有者对其他人可见踢出/封禁，这两项目前提示尚未实现。服务端成员名单与 presence **尚未实现**。离屏渲染见 [user-card](polish/user-card.png)。

验证：macOS Release 构建、本地 API / Gateway 聚焦检查；原生窗口见 [live](polish/sidebar-live.png) 与 [design preview](polish/sidebar-preview.png)。远端部署、PostgreSQL 实际删除和三平台窗口尚未验证。

语音交互（2026-09-21）：Release Chat.UI / Chat.App 零警告编译；86 项客户端契约检查通过。对本机 API 用两个账号加入同一语音频道，成员名单为 2，并签发 LiveKit JWT。`chat-media-worker` 现已链接 LiveKit Rust SDK。

Windows 本机双端媒体（2026-09-21）：本机 `livekit-server` 1.9.0（`127.0.0.1:7880`）+ 本地 API。两个账号加入同一语音频道后，两个 `chat-media-worker` 均 `join ok`；LiveKit `ListParticipants` 为 2 名 ACTIVE、各 1 条 `microphone` 音轨（一侧开麦、一侧静音以免本机回授）。这是与桌面客户端相同的 worker 进房路径，不是两个 Avalonia 窗口里的听筒验收。

随后补上局域网 RTC 改写、频道内有界重连，以及 LiveKit 活跃说话人驱动的侧栏 2 px 绿圈。2026-09-21 本机验收：`Host: 192.168.1.6:8080` 时发现 RTC 为 `http://192.168.1.6:7880`、join 为 `ws://192.168.1.6:7880`；LiveKit 1.9.0 用 `--node-ip` 绑 `0.0.0.0:7880`（YAML 顶层 `node_ip` 会启动失败）。说话事件已进 worker 源码，当前 `chat-media-worker` 发布二进制因 webrtc-sys MT/MD 链接冲突未重编，绿圈要等该 exe 编过才会亮。两个真实窗口对听仍未记录。

## Account controls (2026-09-21)

The account bar opens an editable profile panel above the account area using the existing profile API. The current compact surface uses a 180ms slide/fade that can reverse during dismissal, respects reduced motion and stops when minimized. Escape and the backdrop dismiss it; focus cycles inside and returns to the opener.

Microphone/deafen controls work before joining voice and carry the chosen state into the next join. Undeafen restores the previous microphone preference. Hover resolves the selected device name (including the current system default); each icon has its own arrow: the microphone opens only inputs, and headphones open only outputs. Each 240px menu lists devices directly in 32px rows, marks the current selection and dismisses on selection; there is no nested dropdown. Enumeration runs on demand with coalesced refreshes, not an idle polling loop. These controls now drive a LiveKit client in `chat-media-worker`. Windows host LiveKit now accepted two workers in one room with published microphone tracks; two-window hearing is still not recorded.

频道原位编辑保持透明背景，移除输入框底色、边框及整行下划线，仅保留文字、光标和保存/取消操作。

Validation: the isolated Release source snapshot compiled successfully (one existing `Watermark` deprecation warning). The 54 foundation checks included five checks that pre-join mute/deafen never call the API or media layer; module boundaries passed. In the real macOS window, controls toggled while a text channel was selected, both system devices were enumerated and explicitly selected, and a temporary local account's username/display name saved and survived closing/reopening the drawer. Avatar upload and remote audio were not re-tested. Device menus use a single overlay popup to retain focus without native popup windows.

The final shared-workspace Release build also passed with zero warnings and zero errors. Native interaction checks used the isolated snapshot; concurrent compact profile-card styling was retained and was not separately re-tested here.

Split device menus: Release build passed with zero warnings/errors. The real macOS window showed separate input/output lists; choosing the MacBook microphone or speakers updated the corresponding preference and dismissed the menu.

## Instance rail avatars (2026-09-21)

The left rail now binds each visited instance account avatar to the shared circular Avatar control. Missing avatars retain the instance-name initial. Replacement/removal refreshes the rail; identity changes invalidate its thumbnail. The rail owns up to 32 static 64px thumbnails (at most 512 KiB), reuses account downloads, and releases them when hidden or disposed. Participant avatars also receive playback updates before old images are disposed; obsolete download completions cannot restore a replaced/removed avatar.

Validation: Release desktop build passed with zero warnings/errors. An isolated local account uploaded two generated test images through the production session/API in a real macOS window; checks confirmed replacement reached the rail binding and removal cleared rail/account images. [Window render](polish/instance-avatar-native-render.png) was captured from the live control tree and visually checked for circular clipping; it is not an OS screenshot or mouse-input test. Existing-window accessibility inspection timed out. No chat messages were sent; Windows/Linux were not tested. No protocol or phase change.

Rail selection refinement: selected items use a quiet `#2C2C2C` fill (`#303030` on hover), 2px corners, no selection border and no avatar outline. The changed Avatar/InstanceRail XAML compiled in isolation against the preceding successful client build; a native host check confirmed zero selection border and hidden outline, and the [updated window render](polish/instance-selection-native-render.png) was visually inspected. The final full workspace build was blocked by concurrent message/inbox code errors (MessageText, ShellViewModel.Inbox and Presentation); that integration is not claimed as passing.

### 设置页空白频道列修复（2026-09-21）

恢复 ShellSurface 的 ShowSettings 折叠绑定；设置打开时频道栏宽度降为 0，关闭后恢复 220 px，保留短促过渡及减少动效偏好。折叠样式由可重建的 ShellSurface 自己持有，UltraLight 恢复后也适用，不改变资料页的头像与名称编辑。

验证：使用 d0173f8 的独立源码快照加本次布局改动及当前 SettingsViewModel，完成 Release 构建、设置栏宽度归零与 UltraLight 恢复检查；macOS 原生验证窗口已核对资料页，空白列消失。隔离构建用于避开其他任务正在改写的源码/程序集，不代表所有并行改动已验收。

### 设置侧栏与分类切换（2026-09-21）

设置页只占主内容区：240px 导航不再挤扁频道列，频道/账号列和社区标题保持可见。点频道或左侧实例会关闭设置并回到工作区。分类切换立即换页，不再做上下位移或淡入。当前分类只提高文字和图标对比，不再铺整行选中底。

验证：`Chat.UI` Release 0 警告 0 错误。隔离输出的 UltraLight 确认设置打开后频道列仍为 260px、分类导航 240px 留在主栏。离屏见 [channel-stay.png](settings/channel-stay.png)。本机已有占用默认输出的 Chat.App，未再开第二个原生窗口点频道；Windows 原生点击与 macOS/Linux 未在本轮验收。

### 设置入场收起频道列（2026-09-21）

进入设置时频道/账号列与社区标题在 160ms 内收起并淡出（260→0），设置页以 `SlideLeft` 从右侧 8px、透明度 0.88 入场，分类导航贴着实例栏。减少动效时立即到位。关闭设置后频道列恢复 260px。UltraLight 重建后折叠状态仍由 ShellSurface 样式绑定。

验证：隔离输出目录的 `Chat.UI` / UltraLight Release 0 警告 0 错误；UltraLight 确认设置打开及恢复后频道列宽与透明度归零。离屏见 [channel-collapse.png](settings/channel-collapse.png)。未再开第二个原生窗口点设置；本机已有占用默认输出的 Chat.App。

选中态减重（2026-09-21）：导航选中不再铺 `SelectedBrush` 整行底，也不再 SemiBold；当前项只提高文字/图标对比，悬停仍用浅底。隔离 Release 构建 `Chat.UI` / UltraLight 0 警告 0 错误，UltraLight 布局检查通过。离屏见 [sidebar-select-quiet.png](settings/sidebar-select-quiet.png)。未再开原生窗口点选分类。
