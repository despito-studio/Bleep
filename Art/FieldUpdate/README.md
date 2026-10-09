# Field update v1.7.9

Windows player source: `ceac9b999bb611bb45d0d8ef2887cef9804114d9`, built successfully on 2026-10-09.

The final Windows player passed the six checks in `runtime-checks.txt`, including actual controller movement over the van steps, crouch clearance, the hanging map, the journal, reflection disabling and a real loopback LAN guest through shared departure.

The rendered frames were inspected for furniture placement, readable van map, wardrobe/body visibility, the replacement monster and guest clothing/crouch presentation. `character-geometry-checks.txt` records source garment removal and finite posed geometry for all four adult rigs.

Run `Tools/verify-field-update.ps1` against a fresh player. `Tools/package-field-release.ps1` packages the build manifest's complete Windows payload, verifies that game sources match, requires a newer native report and writes the archive SHA-256.
