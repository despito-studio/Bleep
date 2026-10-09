# First-person body and gaze verification — v1.7.10

Windows x64 / Unity 6000.6.4f1, source 4739a8cf77b5b19ca3ee45c6e7f3c31db9a19fe8.
Build succeeded at 2026-10-09T16:46:30Z with clean source. Latest master, including the independently added Chapter 2, is merged.

- Actual source eye landmarks fit all four first-person bodies. Authored materials/UVs and a separate full source shadow are preserved.
- Native Windows frames were inspected: the engineer shirt stays below the camera; the dispatcher displays clothed torso and legs, with the removed neck outside the viewport. The intrusive experimental skin disk was removed.
- Actual crouch camera/capsule transitions complete with grounded source soles and no clothing across the upper view.
- A real loopback LAN peer replicates head pitch/yaw, keeps small glances planted and brings the torso around during a 180-degree turn. Neck yaw/pitch remain bounded to 55 degrees.
- Outfit controls and old saved clothing preferences are removed; all four classes use their authored outfits.

The three focused native checks are recorded in body-runtime-report.txt. These checks concern this body/gaze change; they are not a complete walkthrough of every chapter.