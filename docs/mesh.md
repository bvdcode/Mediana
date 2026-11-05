# Mesh loading and previews

`Mediana.Models.Mesh` reads STL, OBJ and 3MF geometry and renders managed CPU
previews. It uses Mediana's shared drawing, OPC package and frame contracts. It
has no Render.NET package dependency, native graphics dependency, GPU requirement
or external renderer process. The source originates from the MIT-licensed
Render.NET Mesh module.

## CLI

```text
mediana -i model.stl preview.png
mediana -i model.obj preview.png
mediana -i assembly.3mf preview.png
mediana probe model.stl
```

Input is recognized by content, including files with an arbitrary extension.
Binary STL recognition checks the complete byte length against its triangle
count. The common image registry receives the input length; decoding a sequential
source of unknown length finishes recognition after bounded input reading. A
`solid` header alone does not distinguish binary and ASCII STL. OBJ recognition
uses a bounded UTF-8 text prefix.

3MF shares one OPC registration with Word documents. Root relationship type and
main-part content type select the decoder, independently of ZIP member ordering
or filename extensions. Multiple registered roots, mismatched relationship/content
types, external resources and ambiguous package part names are rejected. OPC
part-name matching is case insensitive, retaining the stored spelling.

The default preview is 512 × 512 pixels with a transparent background. Model
probing reports this as a **preview canvas**, rather than an intrinsic image
size. It does not rasterize the model. Probe recognition and basic package/XML
inspection do not validate every triangle or guarantee successful rendering.

## Library

```csharp
using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Drawing;
using Mediana.Models.Mesh;

TriangleMesh mesh = await MeshReader.ReadFileAsync("model.obj", cancellationToken);
MediaBufferAllocator allocator = new(256L * 1024 * 1024);
MeshRenderOptions options = new()
{
    Width = 800,
    Height = 600,
    Supersampling = 2,
    ShadowMapSize = 1024
};
using PixelBuffer pixels = MeshRenderer.Render(mesh, options, cancellationToken, allocator);
using VideoFrame frame = pixels.PublishVideoFrame(cancellationToken);
await using FileStream output = File.Create("preview.png");
await PngEncoder.EncodeAsync(frame, output, allocator, cancellationToken);
```

Format-specific readers expose their own limits and leave caller streams open.
When reading an already-open `OpenXmlPackage`, archive, cumulative XML and input
budgets belong to that package's owner. `ThreeMfReaderOptions` still controls
geometry expansion and per-model part bytes; it does not replace the owner's
package/XML limits. Complete-memory overloads borrow encoded storage during parsing; returned models
own independent geometry. Public `TriangleMesh` constructors copy caller geometry.
Internal reader results transfer their final arrays without another complete
vertex/index/normal copy. Its memory views are read-only by contract. A mesh can
be reused by concurrent renders with independent camera and material options.

3MF selection is explicit:

```csharp
await using FileStream input = File.OpenRead("assembly.3mf");
ThreeMfDocument document = await ThreeMfReader.ReadAsync(input, cancellationToken: cancellationToken);
TriangleMesh completeBuild = document.CreateBuildMesh(cancellationToken);
TriangleMesh instance = document.CreateBuildItemMesh(0, cancellationToken);
TriangleMesh resource = document.CreateObjectMesh(1, cancellationToken: cancellationToken);
```

`CreateObjectMesh` accepts the model part for cross-part object IDs. Root build,
component and unit transforms are applied to selected geometry; object selection
without a root build transform remains distinct. Empty builds require explicit
object selection. `ReadThumbnailAsync` extracts an embedded PNG/JPEG separately;
a package thumbnail is never substituted for a geometry render.

For registered previews, add `MeshImageDecoder` for STL/OBJ and
`OpenXmlImageDecoder([new ThreeMfPageDecoder()])` for 3MF. Include `WordPageDecoder`
in the same OPC decoder when accepting DOCX. Registering two independent ZIP
signature decoders would be ambiguous. Library callers can configure each
reader and the `MeshRenderOptions` used by its preview adapter.

## Implemented scope

| Area | Behavior |
| --- | --- |
| STL | Binary and ASCII triangles, UTF-8 BOM, multiple solids, finite coordinates, count/length checks and reconstructed normals |
| OBJ | Indexed polygon geometry, negative indices, planar concave polygon triangulation, continuations, source normals and smoothing groups; all objects/groups combined |
| 3MF | Core resources, build instances, nested components, unit conversion to millimetres, mirrored winding and Production Extension model-part references |
| Camera and geometry | Orthographic framing, padding, model rotation, source/generated normals and crease smoothing |
| Lighting | Configurable surface color, roughness, metallic fraction and exposure; studio lights, filtered shadows, ambient occlusion and tone mapping |
| Output | Supersampled straight-alpha sRGB RGBA, transparent or configured background, owned frames and PNG through the existing codec |

OBJ texture-coordinate syntax is checked but UVs are not retained. MTL files,
source textures/material colors, textured PBR, general 3D scenes, perspective
cameras, animation, glTF/FBX/PLY, CAD, point clouds and model writing remain
separate tasks. The renderer uses the caller's uniform surface material.

## Ownership, limits and scheduling

Prepared positions, corner/face normals, smoothing adjacency, projected vertices,
sample normals, depth maps, shadow maps and final RGBA storage are charged to the
supplied `MediaBufferAllocator`. Disposal, cancellation and failures release all
owned render storage. After `Render` returns, only the output pixel allocation
remains charged. `PublishVideoFrame` transfers that storage without an image copy;
frame leases keep it alive after the pixel owner is disposed.

Without an explicit allocator, `MaximumWorkingBytes` bounds live render buffers.
A supplied allocator defines the shared live-buffer budget. Source models, encoded
input, parser collections/XML trees, object overhead and PNG stream storage are
separate from that render-buffer accounting. Reader byte, vertex, triangle,
object, instance, XML and nesting limits bound their corresponding work; they do
not establish a whole-process memory cap.

Normal adjacency uses bounded arrays rather than a dictionary of per-vertex lists.
Its face accumulation order and existing smoothing work bound are retained.
`MaximumDegreeOfParallelism` controls row shading; the default is the runtime's
parallel policy. Geometry preparation and triangle rasterization are sequential.
Concurrent calls share no model mutation or render scratch state.

## Validation and specifications

Authored reader tests cover malformed/truncated inputs, finite/index bounds,
concave polygons, normals, smoothing groups, assemblies, transforms, units,
Production references, cycles, external targets, XML entities and explicit limits.
Rendering checks cover depth ordering, extreme scales, padding, rotation, source
normals, shadows/AO, transparent antialiased edges and PNG output. Integration
checks cover byte-source and complete-memory routing, Word/3MF coexistence,
shared allocation failures, cancellation, concurrency, frame ownership and CLI
conversion by content. Release tests pass all 3,413 cases. The strict Linux x64
Native AOT runner exercises the same owned mesh/frame/PNG path. Per-operation
acceptance remains in the
[format checklist](formats/README.md#3d-meshes-and-manufacturing-interchange).

CPU, memory and exact-output comparisons with the source renderer are recorded in
[Mesh performance](mesh-performance.md).

The [3MF Core specification](https://github.com/3MFConsortium/spec_core/blob/master/3MF%20Core%20Specification.md)
defines model relationships and content types. The
[Production Extension](https://github.com/3MFConsortium/spec_production) describes
cross-part manufacturing assemblies. OPC part comparison follows the case-folding
semantics also implemented by Microsoft's
[`PackUriHelper`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.IO.Packaging/src/System/IO/Packaging/PackUriHelper.cs).
