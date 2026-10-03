# Channel card 2.0.0-rc.2

This card update remains compatible with the Windows bridge 2.0.0-rc.1.

- Silence lets the meter fall smoothly instead of clearing it immediately.
- Segmented luminous meters have configurable lower and upper limits, with +12 dBFS visual headroom by default.
- Peak numbers, scale, status, hold marker, clip indicator, source ID and tap labels have independent visibility options.
- Meter and active control colours are configurable in YAML and the visual editor.
- Mute, solo, mono and routes use lit buttons. Help and readback explanations move to tooltips; failed actions remain visible.
- Processing amounts use fader/number pairs where appropriate; timings, frequency, ratio and Q use exact numbers. EQ types use named choices, EQ memory uses A/B buttons, and parametric EQ uses channel/band selection.
- The EQ-cell checkbox suggests real entity mappings when bridge discovery is enabled and explains missing discovery otherwise.

See USER-GUIDE.md for every option and setup step, and MIXER-REFRESH-REVIEW.md for checks and limits. Existing release tags remain unchanged.
