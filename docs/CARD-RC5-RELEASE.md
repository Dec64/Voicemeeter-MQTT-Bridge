# Voicemeeter channel card v2.0.0-rc.5

Processing panels now behave like an audio rack. Compressor, gate, denoiser and EQ parameters have rotary knobs with exact editable values, fine pointer adjustment and keyboard support. Compressor transfer and gate timing graphs preview the settings.

Parametric EQ has a logarithmic 20 Hz–20 kHz graph with six coloured, draggable band handles, channel and band selection, named filters, bypass and frequency/gain/Q controls. Dragging applies on release and waits for actual mixer readback. Bell and shelf filters support graph gain edits; other filters use frequency and Q.

Standard starts with compact processing drawers closed. Expanded opens larger meters, knobs and graphs as a full rack. Compact remains the smallest channel layout. Narrow cards resize knobs and keep touch targets usable.

New visual-editor/YAML options:

- `appearance.presentation`: `channel` (default) or `meter`. Meter-only hides and deactivates mixer controls while retaining mappings.
- `appearance.name_style`: `full` (default), `minimal` or `hidden`.

The complete guide includes setup, input gestures, all options and the limits of illustrative graph previews. Existing configurations remain valid. Bridge v2.0.0-rc.1 remains compatible and does not need an executable update.

Validation: 95 logic tests, 12 control/editor browser scenarios and 9 meter browser scenarios passed. Real pointer gestures were checked against local mock readback; desktop/narrow and light/dark layouts were inspected. The distributable contains 21 modules with 33 versioned relative imports.

The card remains a release candidate. Setting curves are not live analyzers or a guarantee of Voicemeeter's exact internal processing response. Physical-audio and Fire tablet acceptance remain owner qualification tasks.
