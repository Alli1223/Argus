# Web UI design

The design plan the web UI follows. Argus is named after Argus Panoptes, the hundred-eyed watchman
of Greek myth whose eyes ended up on the peacock's tail. The UI borrows from that: every host is an
*eye*, and the palette comes from a peacock feather's eye (bronze ring, green, turquoise, a deep
blue-violet centre).

**Audience:** sysadmins and home-lab owners keeping a fleet healthy, often on a second monitor.
**Primary job:** show at a glance whether anything needs attention, then make diagnosing one host fast.

## Principles

1. **Status first.** The worst thing on screen is always the most visible thing. Colour is reserved
   for status and interaction; nothing is coloured for decoration.
2. **One memorable element: the watch.** The fleet dashboard shows each host as an eye whose rings
   carry CPU, memory and disk; offline hosts are closed eyes. Everything around it stays quiet.
3. **Density with rhythm.** Tables over card grids, tabular figures, semi-condensed type for data,
   left-aligned text, generous space only around the watch.
4. **Plain words.** Sentence case everywhere; labels say what things are ("Add a system", not
   "Enroll agent"); errors say what happened and what to do.

## Colour

| Token | Light | Dark | Use |
| --- | --- | --- | --- |
| `ink` | `#1a2140` | `#dfe2f3` | Text |
| `paper` | `#f5f6fa` | `#1b2042` | Page background |
| `surface` | `#ffffff` | `#262c50` | Panels, inputs |
| `iris` | `#414cc7` | `#8c95ff` | Interactive: links, buttons, focus |
| `healthy` | `#0d8566` | `#36c9a0` | OK / online |
| `bronze` | `#c2860c` | `#e8b030` | Warning |
| `crimson` | `#d0342a` | `#f15a4e` | Critical |
| `slate` | `#6a7099` | `#8d93b9` | Offline, muted text |

Dark mode is a deep indigo ("the pupil"), not a tinted near-black.

## Type

- **Archivo** (variable, weight and width axes) for everything. Headings and the wordmark use a
  wider cut (`font-stretch: 112%`); tables and data use a semi-condensed cut (`font-stretch: 88%`)
  with tabular figures so columns of numbers line up.
- **Martian Mono** only for things people copy: install commands, tokens, IDs.
- Scale (px): 12 · 13 · 14 (body) · 16 · 20 · 26 · 34.

## Layout

```
┌──────┬──────────────────────────────────────────────────────────────┐
│ ◉    │  Fleet status              [12 hosts] [1 offline] [2 alerts] │
│      ├──────────────────────────────────────────────────────────────┤
│ Watch│  The watch: one eye per host, worst first                    │
│ Hosts│   ◎ web-1   ◎ web-2   ◎ db-1   ◌ backup   …                   │
│ Alerts├─────────────────────────────┬────────────────────────────────┤
│ Rules│  Firing alerts (table)       │  Busiest hosts (table)         │
│      │                              │                                │
│ ⚙    │                              │                                │
└──────┴──────────────────────────────┴────────────────────────────────┘
```

- A slim left rail (icons with labels when wide), content left-aligned, full width for data.
- Radius carries hierarchy: pills for status chips, 4 px for inputs and tables, 10 px only for the
  watch panel. No drop shadows on tables; surfaces are separated by tone and a 1 px border.
- Motion: none on load except the watch, whose eyes open once when data first arrives; menus,
  drawers and confirmations animate only in response to the user. `prefers-reduced-motion` disables it.
