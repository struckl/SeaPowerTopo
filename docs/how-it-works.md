# How it works

The stock map is a Unity scene - a unit globe made of lon/lat tiles - that a map camera renders into the texture the
map window shows (`SeaPower.LiveMap`, orthographic, looking at the view's centre). The mod adds meshes to that scene,
so the overlay turns and zooms with the globe for free. Each overlay image (a lon/lat rectangle) is cut along the stock
tiles' own 1° grid and the diagonal that splits each cell into two triangles, and every piece is placed in the plane of
the stock triangle under it with texture coordinates linear in lon/lat like the stock ones (`Render/GlobeMesh.cs`) - so
it lies exactly on the stock map at any zoom. The map texture has no depth buffer, so only the cells facing the camera
are meshed (the mesh is remade once the view has turned past its margin). An image's pixels span cos(latitude) more
degrees of longitude than of latitude, as wide on screen as high. The view, its scale and the part of the globe in
sight come from the map camera; the layers from the largest live map control (`RenderedLayer`).

Every layer is drawn the same way: heights copied out of the DEM on the main thread in 3 ms slices per frame, then
resampled (Catmull-Rom when zoomed in past the DEM) and coloured on half the CPU cores (a 4K view in about 70 ms). The
globe map keeps its terrain on the graphics card only, so the mod reads its own: the 3′ world grid (`lowres.raw`, 52 MB,
once, `Terrain/LowresGrid.cs`) and the 30″ chunks of the view, read with the game's reader on a worker thread, the last
eight kept (`Terrain/DemCache.cs`). On the globe build the mod never takes a handle on the 3D terrain's own chunks: the game
keeps only 16, and handles held by the mod kept it from preloading the area around the units (land vanished); both
from the files the game resolves, a mod's replacements included. The pre-drawn layers (mission area ×1, ×4, ×16 and the
world at 0.1°, up to 8 megapixels each, with mipmaps) stand in for the live layer while the view's scale is within 1.4×
zoomed in or 3× zoomed out of theirs. The live layer's image covers the view plus a margin (15-40 %, as much as an
8-megapixel budget allows) and is redrawn when the view leaves it, when a pan has used up most of the margin, or when
the zoom changes a lot. Layers in front cut holes into the ones behind (their mesh is the image minus the others'
rectangles), so the translucent colours never stack. The mission editor's map is a flat map of its own (Noesis tiles,
not the globe): there the live layer's image is a picture laid on its lon/lat rectangle by a `MapPanel` among the map's
background elements (`Render/FlatMapRef.cs`). The thermal layer comes from the game's own tile cache where a tile is
settled (`Environment._layerDepths`), otherwise from the same rule and the same seeded `UnityEngine.Random` draw, with
the generator's state put back.
A look is a plain parameter set (`Render/Theme.cs`): key colours spread over the height bands, in steps or
blended, or one flat colour; relief strength with a tinted shadow and highlight; contour lines with an optional glow;
a coastline; the sea's key colours or a chart's four depth shades; depth lines. The first release's look is one of
them, drawn by its original rules (its output is byte for byte unchanged). Adding a look is adding such a block, or a
theme file: `Render/ThemeIni.cs` reads and writes the file format and `Render/ThemeLibrary.cs` finds the files, resolves
`Base=`, keeps the last working version of a file that breaks, and swaps the result into `Themes` when the folder changes
(polled every 1.5 s); both are plain C# and tested offline by the preview tool's `--check-themes`.
The title bar buttons, the look list, the legend and the cursor readout are Noesis elements: the buttons go into the map
window's title bar button row (found like Pop-Out finds it), the look list is a popup under its button, the rest into a grid laid over the map control, handed over by a Harmony
postfix on `RenderedLayer.OnRender` that is applied only once NoesisGUI runs; the editor's map is found through its view
model. The few game internals the mod needs (the night state of the map's palette, a new Layers menu entry) are
reached by reflection (`GameReflection`, from the shared skeleton).

Depths are the simulation's: the DEM depth times the game's under-water depth factor, doubled once more where the DEM
is shallower than 48 m (as every terrain lookup of the game does) - so a thin ring of shallower water can sit just
outside a coastal strip. That is how the simulation sees the seabed, not a drawing error.
