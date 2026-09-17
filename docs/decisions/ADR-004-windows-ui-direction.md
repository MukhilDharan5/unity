# ADR-004 — Windows presentation direction

## Status

**Proposed; requires user decision before significant UI/framework changes.** Recorded 17 September 2026. No WPF code or project framework was changed in Stage 0.

## Context

The MVP is a working WPF desktop/flyout with WinForms tray/menu and smoke renders. The user wants native Windows 11 interaction/controls/materials, a tray-first primary UI and a configuration-focused full app. Existing custom templates, fixed palettes and button-based switches do not meet that final standard. Core state and platform adapters can be retained regardless of presentation choice.

## Decision needing approval

| Option | Advantages | Disadvantages |
| --- | --- | --- |
| A — Modernize WPF in place | Retains XAML, render harness and mature tray/window behavior; smallest immediate migration and runtime packaging cost | Requires more custom native interaction/accessibility/material work; greater ongoing visual/behavior maintenance |
| B — Replace only the presentation shell with WinUI 3 during Stage 5 | Uses Microsoft's modern native Windows control family and integration direction; retains Core and feature/provider behavior | Rebuilds XAML/window lifecycle/render checks; adds Windows App Runtime/deployment and tray/flyout ownership decisions; native quality still requires careful review |

Recommend B for the stated native Windows 11 product goal. Stabilize UI-independent state/providers during Stage 1; do not migrate the shell while repairing security/lifecycle foundations. A total cross-platform clean rebuild is unnecessary.

Microsoft describes both [modernization in place and WinUI migration](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/migrate-to-windows-app-sdk/migration-decision-guide); that supports the options, while this recommendation is specific to the product brief.

## Alternatives

An immediate full rewrite discards useful Core/Android/provider work without an architectural reason. A browser-shell visual imitation does not satisfy the requested native Windows experience and is not recommended.

## Consequences and difficult later changes

Framework choice fixes control semantics, UI test tooling, backdrop/window lifecycle and runtime distribution requirements. More custom WPF pages would increase a later migration's cost. WinUI choice must separately resolve packaging/unpackaged deployment, compatibility minimum and tray/flyout integration. Both choices require theme/high-contrast, keyboard/screen-reader and real 100/125/150/200% DPI review. No additional privileged service is required merely to change the UI.
