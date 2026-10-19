DEAD AIR ENVIRONMENT ART SOURCES
19 October 2026

Powered by Poly Haven
The Poly Haven models and scanned PBR surfaces are CC0-1.0 assets.
Provider license: https://polyhaven.com/license
Public API documentation: https://github.com/Poly-Haven/Public-API

PolyHaven/manifest.json records every selected asset, the original download URL,
provider MD5, local SHA-256 and byte length. The cached glTF, binary payloads and
original textures remain in PolyHaven/Models. No network connection is required
by the game. Assets/ArtProduction contains the converted Unity sources.

The Blender conversion preserves separate mesh/material parts, bakes transforms
into metre-space vertices and exports detail and distance FBXs. Unity creates
two-level LOD prefabs, GLTF material bindings and static mesh collision. Fitted
scene instances retain unscaled pivots; sizing is baked into mesh assets.
Moving doors, drawers, sensors, handles and rotors remain separate from static
batches. Office/Authoring stores complete sources before the runtime batch.
Docs/ArtProduction.md explains artist overrides, articulation IDs and regeneration.
Assets/Office/ArtOverrides/Instances and Types take precedence over generated art;
the builder never overwrites these artist-owned prefabs.

Surface maps use RGB ARM packing: ambient occlusion, roughness, metalness.
Albedo is sRGB; normal, packed masks and scalar data are linear. OpenGL normals
are imported as Unity normal maps. Original scalar metallic/roughness/base-colour
factors are preserved, including nonmetal foliage and translucent clock glass.
Uniform porcelain, rubber, paper, brass and clear-glass materials are authored
physical values rather than invented photographic texture scans.

The fictional 1988 broadcast-network map in Assets/ArtProduction/Graphics was
generated using the built-in image_gen tool, then edited to remove a state emblem
incompatible with its date. It is an illustrative fictional map, not geographic
or historical source evidence. Its provenance is recorded in generated-graphics.json.
It is separate from the CC0 Poly Haven library.

REPRODUCTION
1. Run Tools/ArtProduction/fetch_polyhaven.py with Python 3.
2. Run Blender --background --python Tools/ArtProduction/convert_models.py.
3. Run Unity 6000.6.4f1 executeMethod:
   DeadAir.Editor.DeadAirArtProductionBuilder.Rebuild
4. GPU review (without -nographics):
   DeadAir.Editor.DeadAirProductionReview.CaptureFresh
   DeadAir.Editor.DeadAirProductionCatalogCapture.CaptureCatalog
5. Windows build:
   DeadAir.Editor.DeadAirEditorSetup.BuildWindows

Use the Unity CLI to invoke the Editor. The project uses its existing Built-in
render pipeline. The import operation reuses stable material/prefab paths and
updates fitted meshes after source changes. Downloading is cached and hash-checked.
