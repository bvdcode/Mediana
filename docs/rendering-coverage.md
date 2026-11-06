# Rendering coverage and remaining modules

This comparison uses Render.NET source revision
[`8d2a437`](https://github.com/bvdcode/Render.NET/tree/8d2a4375c719c9bc394f755a4c989ed685bd36b6).
It separates features already implemented in Mediana from reusable source and
features that neither implementation supplies. The
[format catalog](formats/README.md) remains the operation checklist; a feature in
Render.NET does not mark a Mediana operation as implemented.

## Existing Mediana coverage

| Render.NET area | Mediana implementation | Remaining distinction |
| --- | --- | --- |
| Mesh | `Mediana.Models.Mesh`: STL/OBJ/3MF readers, explicit assembly selection and CPU previews | Geometry ownership and render scratch use Mediana contracts; see [Mesh API](mesh.md). |
| Drawing, paths and compositing | `Mediana.Drawing`: paths, clipping, shared scenes, effects, fonts and owned RGBA surfaces | Reuse the common drawing layer; another drawing core would duplicate storage and contracts. |
| SVG | `Mediana.Images.Svg`: static SVG, text, masks, filters and embedded PNG | Compare individual rendering changes and tests against the existing implementation. |
| TGS / Lottie | `Mediana.Images.Tgs`: scene parsing, keyframes, effects and timed rendering | Expression execution is separate from stored-value rendering; see below. |
| JPEG 2000 | `Mediana.Codecs.Jpeg2000`: codestreams, JP2, native components and optional RGBA previews | Existing [coding scope](jpeg2000.md) applies; a new wrapper is not another codec implementation. |
| JBIG2 | `Mediana.Codecs.Jbig2`: pages, globals and optional RGBA previews | Existing [segment scope](jbig2.md) applies. |
| PNG output | `Mediana.Codecs.Png`: encoding and bounded stream output, alongside PNG decoding | Reuse this encoder for all new previews. |
| PDF | `Mediana.Documents.Pdf`: independent parsing, paths, text, images, masks and supported blending | Render.NET has additional painting operations described below. |
| Font resources | `Mediana.Drawing.Fonts`: TrueType, supported CFF, WOFF 1 reconstruction, first TTC face and pair kerning | WOFF/TTC are resources used by rendering. They are not standalone CLI image inputs or full text shaping. |

Word layout and rasterization are implemented in `Mediana.Documents.Word`, on
`Mediana.Documents.OpenXml` and the shared drawing layer. Render.NET does not
supply Word, Excel or PowerPoint layout. XLSX and PPTX rendering remain open in
the catalog independently of OPC package reading and DOCX rendering.

## Mesh: implemented model readers and preview renderer

The complete supported scope of
[`Render.NET.Mesh`](https://github.com/bvdcode/Render.NET/tree/8d2a4375c719c9bc394f755a4c989ed685bd36b6/Sources/Render.NET.Mesh)
is implemented in `Mediana.Models.Mesh` with Mediana ownership and OPC integration.
The Render.NET source project targets .NET 10 and
references only the Render.NET drawing project, with no third-party runtime
package or native graphics dependency. The source is MIT-licensed.

| Area | Available source behavior | Boundary |
| --- | --- | --- |
| STL | Binary and ASCII triangle meshes, including multiple ASCII solids; generated normals | No STL writer or scene/material model. Binary detection must check structure, not only a `solid` prefix. |
| OBJ | Polygon triangulation, positive/negative indices, source normals, smoothing groups, line continuations and combined objects/groups | Texture-coordinate syntax and indices are checked, but UV coordinates are not retained. No MTL loading, texture rendering or object/group selection API. |
| 3MF | Core mesh resources, root build, nested components, transforms, unit conversion to millimetres and Production Extension model-part references | Materials/textures are not rendered; preview uses the caller's surface color. This is not complete coverage of all 3MF extensions. |
| 3MF selection | Whole build, one build instance or one object resource; cycle and expansion limits | Preserve explicit selection and transforms instead of choosing an arbitrary first object. |
| Embedded 3MF thumbnail | Original PNG/JPEG bytes can be extracted separately from geometry | Thumbnail extraction and geometry rendering are distinct operations. |
| Geometry | Indexed triangles, optional per-corner normals, model rotation, normalization and smooth/generated normals | No general scene graph, UV/tangent streams, skinning or animation. |
| Rendering | Orthographic camera, automatic framing, depth testing, studio lighting, roughness/metallic controls, filtered shadows, ambient occlusion, tone mapping and supersampling | CPU rendering of opaque mesh surfaces; no textured PBR renderer, perspective scene camera or GPU backend. |
| Output | Straight-alpha sRGB RGBA with transparent background support | Mediana's existing frame and PNG output contracts should own publication and encoding. |

STL, OBJ, 3MF Core and Production reference read/render operations are marked as
implemented subsets in the catalog. Their exact scope, resource contracts and
public examples are documented in [Mesh loading and previews](mesh.md). Model
writing remains a separate task; the module supplies readers and a preview renderer.

glTF/GLB, FBX, PLY, CAD/BIM, point clouds and volume rendering are not supplied by
this Mesh module. PDF mesh shadings interpolate colors on a PDF page; they are
not a 3D model importer.

### Integration boundaries

Typed geometry loading is separate from rasterization. Library callers can retain
meshes and select 3MF builds/objects; model previews publish through the existing
image-to-frame-to-PNG path. Audio/video packet contracts do not represent geometry,
and the model module stays outside the common 2D drawing core.

The source convenience reader dispatches by extension. Mediana's `MeshReader`
uses content and exact binary STL length/count checks. OBJ uses a bounded text
probe. 3MF and DOCX share the OPC adapter, selected by root relationship and part
content type. A ZIP signature alone does not select a model or Word decoder.

Prepared geometry, adjacency, projection, normals, depth/shadow maps and output
pixels use the shared allocation budget. Source model/parser storage has separate
byte and geometry limits. Internal readers transfer final arrays instead of
re-copying them into the mesh. Public constructors retain caller isolation.
Publishing the owned output as a frame does not copy its RGBA array.

The source reader/render regression cases run in Mediana alongside additional
allocation, ownership, cancellation, concurrency, OPC routing and CLI checks.
The Native AOT runner includes model loading, shared Word/3MF registration,
rendering, frame publication and PNG checks. Per-operation correctness and
performance acceptance remain independent; successful integration is not a claim
of complete format conformance. Render.NET benchmark results are not Mediana
performance measurements.

## PDF: additional painting operations

Render.NET's
[PDF renderer](https://github.com/bvdcode/Render.NET/blob/8d2a4375c719c9bc394f755a4c989ed685bd36b6/docs/pdf.md)
contains reusable rendering algorithms beyond Mediana's current
[PDF scope](pdf.md).

| Additional area | Reusable source | Mediana status |
| --- | --- | --- |
| Gradients and mesh shadings | `PdfShadingProcessor`, `PdfMeshStream`, `PdfShadingMesh`, `PdfMeshIndex`, shading commands | Function, axial/radial, triangle, Coons and tensor shadings are open. |
| Tiling and shading patterns | `PdfPatternProcessor`, `PatternInk`, `TilingPatternCommand`, `PatternPaintCommand` | Colored/stencil pattern painting for paths, text and masks is open. |
| Group transparency and soft masks | `PdfTransparencyProcessor`, `GroupCommand`, `MaskedCommand`, `MaskPlan` | Isolated/knockout Form groups and graphics-state alpha/luminosity masks are open. Existing ordinary alpha/blending must be reused. |
| Annotation and form appearances | `PdfAnnotationProcessor` | Visible appearance streams and selected widget states are open. Non-painting annotations are already accepted. |
| Optional content | `PdfOptionalContent` | Default layer configuration, membership policies and visibility expressions are open. |

The Render.NET PDF package references PdfPig and StbImageSharp. Its parsing,
resource and color types are therefore not drop-in Mediana contracts. Transfer
painting algorithms through Mediana's existing parsed objects, resource resolver
and drawing commands, preserving their operation and allocation limits. Reuse
Mediana JPEG/JPEG 2000/JBIG2 decoders rather than installing a second decoder or
replacing the PDF parser solely to import these features.

Inline images, LZW/CCITT, encryption and broader font/color handling visible in
Render.NET also involve its dependencies. They are separate implementation tasks,
not capabilities gained by copying the shading code. Type 3 glyph interpretation
already exists in Mediana and should not be implemented twice.

## Expressions and further 2D improvements

Render.NET's TGS expression host references Jint. General JavaScript expression
execution is not part of Mediana's current expression contract. The existing
library policy and explicit CLI warnings for stored-value rendering remain in
place. An expression module would require a separate dependency and Native AOT
assessment; it is not part of the dependency-free Mesh transfer.

SVG/TGS effects, bounded temporary surfaces, font containers and visual regression
cases can be compared individually. Both repositories already contain scene and
effect infrastructure, so newer source files do not establish a missing feature
by themselves. Keep the shared Mediana drawing implementation and validate each
accepted fix with the affected output and memory/CPU measurements.

## Adaptable order

STL/OBJ/3MF geometry-to-PNG rendering is implemented, including reader and renderer
regression cases. PDF shading/pattern support and transparency/appearance handling
are independent next increments on the existing PDF API. Update operation marks only when
those increments work through Mediana's public contracts. Encoding, model export,
new 3D formats and general expression execution remain independent tasks.
