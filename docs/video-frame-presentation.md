# Video frame presentation

`VideoFrame.Presentation` optionally describes the display of one decoded surface.
`VideoFramePresentation.Frames(count, sourceScan)` describes consecutive frame
displays. `Fields(firstField, count, sourceScan)` describes alternating field
displays. Source scan can be unknown, progressive, interlaced or mixed; it is
independent of the encoded representation and display cadence.

The hint does not replace `Timestamp` or `Duration`, change `VideoFormat`, allocate
pixel copies, or instruct a decoder to reconstruct the same picture again.
Missing hints remain null. The existing frame constructor remains available.

`Retain`, `WithTiming` and `WithFormat` preserve presentation hints.
`WithPresentation` replaces them while retaining the same pixel lease.
Color conversion and resizing also preserve them. `GetField` describes one
selected field, keeps the source scan type, and leaves timing unchanged.
A surface storing only one field rejects hints requesting another field or
alternating between fields.

The presentation, frame ownership, color and resizing test selection passes
306 tests in Release. Tests verify shared pixel storage, independent disposal,
invalid-hint rejection, unchanged container timing and preservation through
RGBA resizing, RGB/YUV conversion and planar downscaling.
