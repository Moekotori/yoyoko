# Dark desktop UI — visual QA

final result: passed

Scope: the selected dark visual direction in the Avalonia desktop shell. This result covers visual fidelity at the inspected macOS viewport, not completion of chat/RTC or OS input/accessibility acceptance.

## Evidence and normalization

- Source visual truth: `docs/ui/selected-reference.png`, 1487 × 1058 pixels.
- Implementation: `docs/ui/preview.png`, actual native-window capture, 2400 × 1708 pixels.
- Logical viewport: 1200 × 854 device-independent pixels; macOS density 2×. CSS size: not applicable to Avalonia.
- Source resized proportionally to 1200 × 854 (subpixel aspect rounding only); native screenshot downsampled 2× to the same dimensions.
- State: explicit `--design-preview`, 日常 selected, member pane open, five bounded sample messages. The reference's sample data is intentionally restricted to preview mode. The account label and native window title identify it as design preview.
- Full-view comparison: `docs/ui/comparison.png` (reference and actual in the same image, reviewed together).
- Focused comparisons: `docs/ui/compare-navigation.png`, `docs/ui/compare-conversation.png`, `docs/ui/compare-composer.png`, each contains both normalized source and actual regions and was visually inspected.
- Normal startup: `docs/ui/live-empty.png`, a real window with an isolated empty cache; no sample channels or people. This is a different product state and is not used to claim pixel fidelity to the populated reference.

## Comparison history

1. Initial native capture (`docs/ui/preview-first.png`, 1280 × 862 logical after the OS fitted the window): [P2] main pane too wide and members too narrow; reference aspect ratio not matched. Server rail reused people instead of separate community photos; body text was slightly undersized. Adjusted viewport to 1200 × 854, left grid to 88 + 236, members to 228; added city and plant photos; increased body to 19 and sender to 18. Added active server marker.
2. Same-viewport comparison: [P2] default ListBox hover painted an oversized gray message block inconsistent with the flat timeline. Removed the container hover background, retaining individual control feedback. Tightened voice-section spacing; used official filled heart/send icons and a circular attachment control matching the reference.
3. Final capture and combined full/focused comparisons above: previous issues resolved. No outstanding P0/P1/P2 visual findings at this viewport.

## Required fidelity surfaces

- Fonts/typography: PingFang SC first on macOS, platform fallbacks for Windows/Linux. 19px message body, 18px sender, 12px timestamps and quiet sidebar hierarchy. Text is readable, complete and not clipped. Native antialiasing differs slightly from generated reference typography (P3).
- Spacing/layout: server rail, grouped channels, native title area, tabs, conversation, member pane, bottom account area and composer track the reference's hierarchy/proportions. Main controls fit the inspected viewport. Default minimum window is 980 × 640; smaller-window OS interaction and Windows/Linux layout remain unverified.
- Colors/tokens: neutral #191919 canvas, #1B1B1D sidebar, #151516 rail, #303032 separators and #B7A4EE accent. Flat fills intentionally replace the generation's incidental texture. The active marker and selected channel are visible without decorative cards.
- Image quality: five local generated photographic assets preserve forest/city/plant/portrait/silhouette subjects; they are replacements matching art direction rather than exact crops. 256px files, decoded to 128px in preview; circle masks are clean. Icons come from licensed official Fluent System Icons, not custom approximations.
- Copy/content: selected reference's five conversation lines, channel names, members and timestamps retained. Preview identification is the intentional product-boundary difference. Product name remains configured. Incomplete actions explicitly show `Not implemented yet` and never append fake sent messages.

## Interaction evidence and limits

- Desktop Release build: 0 warnings, 0 errors. Dependency boundaries pass; diff whitespace check passes.
- Seven focused checks passed: normal-mode fixture isolation; preview asset/timeline loading; synchronized channel/tab selection; current-channel text search; member visibility; unimplemented send preserving draft and message count; notice dismissal.
- Native preview and normal empty window visually inspected. No runtime error output observed during the smoke session.
- OS mouse/keyboard automation is unverified: computer-use runtime failed to initialize after workspace relocation; System Events denied accessibility. Presentation-state checks are not represented as full UI input acceptance. No permission changes were attempted.
- Backend chat, auth, RTC, devices, deployment, other operating systems, and performance benchmarks are outside this UI verification.

## Remaining polish (P3)

Native font rendering and official icon contours differ slightly from the generated image; regenerated photographs match subjects and tone but not exact faces/crops. Group rhythm has small optical differences. These do not block the selected direction.

## Implementation checklist

- [x] Implement chosen layout and dark tokens in modular Avalonia views.
- [x] Local tab/channel, search, group and member-pane presentation controls.
- [x] Explicit opt-in fixtures; real default startup remains data-empty until real data exists.
- [x] Keep unavailable actions explicit, including while add-instance is open.
- [x] Check actual native rendering against normalized source and focused regions.
- [x] Preserve unrelated concurrent client/server/documentation changes; no commit or deployment.
