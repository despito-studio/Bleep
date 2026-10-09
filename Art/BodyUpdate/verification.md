# First-person body and gaze verification — v1.7.10

Windows x64 / Unity 6000.6.4f1, source 21f285a3538c361af88a222b4f5ff0172ded8976.
Build succeeded at 2026-10-09T17:14:32Z with clean source. Latest master c136bd7c, including the independently updated Chapter 2 and journalist mechanics, is merged.

- Actual source eye landmarks fit all four first-person bodies. Authored materials/UVs and a separate full source shadow are preserved.
- Native Windows frames were inspected: the engineer shirt stays below the camera; the dispatcher displays clothed torso and legs, with the removed neck outside the viewport. The intrusive experimental skin disk was removed.
- Actual crouch camera/capsule transitions complete with grounded source soles and no clothing across the upper view.
- A real loopback LAN peer replicates head pitch/yaw, keeps small glances planted and brings the torso around during a 180-degree turn. Neck yaw/pitch remain bounded to 55 degrees.
- Outfit controls and old saved clothing preferences are removed; all four classes use their authored outfits.
- The source arm animation rotated around the forward axis and flapped laterally. Upright gait now follows the sampled opposite thigh in the sagittal plane, preserving authored foot stride. Idle, walk and run have 10-degree lateral arm posture instead of the baked 20–25-degree A-pose.
- All four roles passed eight phases of idle/walk/run: bone lengths remain intact, elbows stay outside the torso and downward, and arm/foot fore-aft motion remains measurable. Ordinary held-item pitch uses the correct world axis and special-tool elbow hints point downward.
- Full source bodies were rendered and inspected in idle, both walking/running half-cycles and held-flashlight poses. Standing, walking and flashlight frames are saved beside this report.

The three focused native checks are recorded in body-runtime-report.txt. These checks concern this body/gaze change; they are not a complete walkthrough of every chapter.
