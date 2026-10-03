# Card RC5 deployment verification

On 3 October 2026, published bridge source commit `8614283` to Dec64's `v2-release-candidate` branch and standalone card commit `3a29d85` to Dec64's `main`. Immutable prerelease `v2.0.0-rc.5` includes the packaged repository and checksum.

Updated the existing card through HACS Redownload, selecting published commit `3a29d85`, and accepted HACS's normal browser Reload. All 21 installed JavaScript modules matched the RC5 package after line-ending normalization.

The existing live mixer loaded larger Expanded meters, compressor transfer preview, gate timing preview, rotary controls, denoiser controls and the six-band parametric EQ graph. Selecting band 2 displayed the existing 300 Hz, -3 dB and Q 2 readback; this was selection only, with no live audio command. Existing routing and source mappings remained intact.

Local validation passed 95 logic tests, 12 control/editor browser scenarios and 9 meter browser scenarios. Pointer drags were exercised against mock readback, with desktop/narrow and light/dark visual checks. See AUDIO-RACK-REVIEW.md for the review receipt and rendering-stall diagnostic qualification.

Private dashboard settings and the live screenshot remain outside the public repository. Windows bridge RC1 remains compatible; no executable update was needed. Physical audio, full load/recovery and Fire tablet acceptance remain qualification work. Graphs preview settings and do not measure live frequency response or gain reduction.
