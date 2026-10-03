# Native source labels

Read through `VBVMR_GetParameterStringW` on Potato 3.1.3.0 on 2026-10-03. This record contains no broker credentials, tokens, hardware device IDs or private dashboard configuration.

| Canonical source | Native label | Verification |
|---|---|---|
| strip:0 | Main Mic | Label read; physical routing pending |
| strip:1 | Guest Mic | Label read; physical routing pending |
| strip:2 | External PS5 | Label read; physical routing pending |
| strip:3 | blank | Generic Hardware Input 4 |
| strip:4 | blank | Generic Hardware Input 5 |
| strip:5 | System | Label read; isolated audio injection pending |
| strip:6 | Chat | Label read; isolated audio injection pending |
| strip:7 | Music | Label read; isolated audio injection pending |
| bus:0–4 | blank | Generic A1–A5 |
| bus:5–7 | blank | Generic B1–B3 |

No owner-specific audio profile is approved by these labels alone. Confirm Guest, External, System, Chat and Music by feeding each source separately and checking that its own card responds, the other cards remain independent, and incoming/post-mute semantics match mute state. Do not infer PS5 or microphone routing from label text.

Read-only full-registry probes accepted 261 controls per physical strip, four per virtual strip and 243 per bus. Virtual strips expose mono and three tone EQ gains, without physical compressor/gate/denoiser/parametric cells. These probes verify API readback, not audible processing.
