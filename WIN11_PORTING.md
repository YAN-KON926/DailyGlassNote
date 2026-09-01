# Windows 11 Porting Guide

## Objective

Add a native Windows 11 composition path while preserving every behavior in `PROJECT_SPEC.md` and the approved appearance in `VISUAL_REFERENCE.md`. Do not rewrite the application or change its JSON data format as part of the port.

## Current issue

The stable Windows 10 build uses composition behavior that is not reliable on Windows 11. On tested Windows 11 hardware, the intended transparent frosted background may render as a solid black surface.

## Required architecture

1. Detect the Windows build at runtime.
2. Keep the current Windows 10 blur path unchanged for the stable platform.
3. Use supported Windows 11 DWM backdrop APIs for the Windows 11 path, such as `DWMWA_SYSTEMBACKDROP_TYPE`, with an appropriate Mica or transient-window backdrop where available.
4. Keep rounded-region and non-client rendering behavior isolated by platform rather than sharing fragile flags blindly.
5. If native composition fails, fall back to a readable translucent light surface. Never fall back to black.
6. Preserve local data compatibility with existing `%AppData%\daily-sticky` JSON files.

## Visual requirements

- Match the reference window opacity, white tint, blur strength, spacing, corner radius, text hierarchy, and task-state styling.
- Ensure the blur is clipped to the rounded window shape.
- Ensure focus changes do not introduce a rectangular border.
- Verify light and dark desktop backgrounds.
- Verify 100%, 125%, 150%, and 200% display scaling.
- Verify hardware graphics acceleration on a physical Windows 11 machine before claiming support.

## Testing strategy

- Use a Windows 11 virtual machine for startup, storage, controls, resizing, DPI, and regression testing.
- Use a physical Windows 11 machine in a local console session for final DWM, blur, corner, shadow, and inactive-window validation.
- Do not treat Remote Desktop, Windows Sandbox, or CI screenshots as final evidence for backdrop rendering.
- Complete every item in `TEST_CHECKLIST.md` on Windows 10 and Windows 11 before publishing a cross-platform release.

## Recommended implementation prompt

> Clone this repository and read README.md, PROJECT_SPEC.md, VISUAL_REFERENCE.md, TEST_CHECKLIST.md, and WIN11_PORTING.md completely. Do not rewrite the project or change existing behavior, storage format, or visual hierarchy. Add a Windows 11 native backdrop path while preserving the current Windows 10 implementation. Select the composition implementation by OS build, provide a non-black readable fallback, and validate every applicable item in TEST_CHECKLIST.md against the public reference screenshot.

