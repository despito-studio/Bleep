# School update restored in 1.8.0, revision 2

The original Windows 1.7.12 release contained uncommitted school changes. Its advertised source patch was absent from both the release attachments and the ZIP. The implementation was recovered from that release's `Assembly-CSharp.dll`, compared against a compilation of its recorded 1.7.11 source commit, and merged into 1.8.0.

The restored code includes the 20 m rear teaching wing, all three floors and connecting stairs, rear sports court and playground, redesigned traditional park, detailed cassette and pill models, five shared tape placements and nine pill placements. Pickup and van exit commands enforce proximity and sightlines. School enemy navigation supports the extended building and upper floors. School construction runs in 17 stages behind its original loading screen, and player input waits for construction. Primitive meshes and light properties are reused, HDR render formats are cached, and the corresponding post-processing changes are included.

Blackpine's snow, enlarged goat, carryable chicken, voiced investigation, interception midpoint and escape sequence remain in the merged version. The journal uses individual school tape IDs, the updated locations and the correct pill count. It refreshes when chapters change.

Verification covers the existing Blackpine gameplay checks, a Windows build, actual loopback TCP commands, school construction and pickup/exit commands, and GPU-rendered views of both chapters. Automated review positions and prerequisites are staged; this does not claim a complete manual walkthrough or measured player-device frame rates.

The corrected Windows package is published as revision 2 of game version 1.8.0. Use the revision 2 package for every member of a co-op group.
