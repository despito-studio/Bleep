# Blackpine farmyard assets

The goat and domestic chicken use downloaded, animated CC0 meshes by **Gobkit / Alsomind Tech Co., Ltd.** The publisher names the bird model `Rooster`; the in-game interaction calls it a chicken. Original meshes, skeletons, textures and animation clips remain in `Goat.glb` and `Rooster.glb`.

Downloaded and license checked on **18 October 2026**:

- Publisher's model and license manifest: https://gobkit.com/api/free
- Goat pack page: https://gobkit.itch.io/gobkit-free-animal-pack-vol-2
- Goat source: https://gobkit.com/freebies/animalB/Goat.glb
- Chicken source: https://gobkit.com/freebies/birds/Rooster.glb
- Original goat archive: https://gobkit.com/freebies/animalB/gobkit-animalB-pack.zip
- Original bird archive: https://gobkit.com/freebies/birds/gobkit-birds-pack.zip
- License: https://creativecommons.org/publicdomain/zero/1.0/

The publisher's `LICENSE.txt` files are preserved as `goat-LICENSE.txt` and `bird-LICENSE.txt`; both permit copying, modification, commercial use and distribution without attribution. `source-manifest.json` records the relevant publisher metadata. Archives preserve the complete downloaded distribution and are outside Unity's Assets folder.

## Native Unity conversion

Run `node Art/Farmyard/convert-models.mjs` from the repository root. This small offline converter reads the two GLBs, evaluates their original skeletal idle/walk/attack clips at 24 frames per second, reflects the glTF coordinate system for Unity, grounds and scales them, and writes:

- `Assets/Resources/Farmyard/goat.bytes` and `goat.png` (1.22 m tall, 396 triangles).
- `Assets/Resources/Farmyard/rooster.bytes` and `rooster.png` (0.47 m tall, 580 triangles).

`DeadAirFarmyardModel` loads these bundled assets. It samples the baked vertices of one small mesh per animal at the original animation rate; no runtime download, external importer, animation package or extra UPM dependency is used. The source rig animations move the goat's feet, face, ears, mouth and tail and the chicken's feet, wings and face. All gameplay state remains host authored in `RoomState.blackpine`.

`preview-models.py` renders an offline contact sheet of actual converted geometry for inspection. `validate-models.py` verifies the binary assets, animation movement, finite coordinates and triangle indices.

The comparison uses one shared metre-to-pixel scale, a 1.75 m human reference and metre gridlines. The adult goat is 2.60 times the chicken's height. It is not enlarged independently to fill its preview panel.

The open-front caretaker shelter, coop, trough, hay, snow-covered tin roofs and carry gloves are original code-authored geometry in `DeadAirFarmyard`. The quiet clucks, startled squawks and goat bleats are original procedural audio. These objects have no movement-blocking colliders. Their footprints are behind the office and clear of the existing generator shelter, rear doors, lamps and service route.

## Shared position contract

`DeadAirFarmyard.Position(world, room, id)` returns **data coordinates** and is shared by host proximity checks and all renderers. Only presentation reflects X. Default roaming stays in a small bounded area beside the generator. A startled chicken retreats toward the coop for three seconds. Dropped chicken uses the host's exact drop coordinates. The rescued chicken occupies the van's cargo perch and follows van transit. The owner's carry view follows the first-person camera; other players see the same chicken held at the carrier's chest.
