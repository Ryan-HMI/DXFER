# Measurement Coverage Matrix

Use this matrix before changing measurement behavior. Mark rows complete only after focused core coverage passes; add running-app evidence only when the behavior is wired into visible UI.

| Target | Expected measurement behavior | Current Stage 7 status | Evidence |
| --- | --- | --- | --- |
| Point-to-point | Signed X/Y delta and scalar distance. | Covered before Stage 7 | `MeasurementService.Measure(Point2, Point2)` exists. |
| Line | Signed X/Y delta and scalar line length. | Covered before Stage 7 | Existing `TryMeasureEntity` line path. |
| Polyline | Full path length, not first segment only; X/Y delta from first to last vertex. | Done 7B1 | `MeasurementServiceTests.MeasuresFullPolylinePathLengthInsteadOfFirstSegmentOnly`. |
| Polygon | Perimeter of closed polygon; bounds available through bounds measurement. | Done 7B1 | `MeasurementServiceTests.MeasuresClosedPolygonPerimeterWithBoundsDeltas`. |
| Circle | Diameter readout; bounds width/height both equal diameter. | Covered before Stage 7 | Existing `TryMeasureEntity` circle path. |
| Arc | Arc length from radius and positive sweep; X/Y delta from start point to end point. | Done 7B1 | `MeasurementServiceTests.MeasuresArcLengthFromPositiveSweep`; rebuilt-app inspector verified selected arc `X -5, Y 5, D 7.854`. |
| Ellipse / elliptical arc | Approximate curve length from sampled points; X/Y delta from sampled start to end. | Done 7B1 | `MeasurementServiceTests.MeasuresCircularEllipseArcFromSampledCurve`. |
| Spline | Approximate curve length from sampled points; X/Y delta from sampled start to end. | Done 7B1 | `MeasurementServiceTests.MeasuresSplineFromSampledCurve`. |
| Point entity | Point coordinate readout with zero scalar distance. | Done 7B1 | `MeasurementServiceTests.MeasuresPointEntityCoordinatesWithZeroDistance`. |
| Selected entity bounds | Width, height, and diagonal of selected entity bounds. | Done 7B1 | `DrawingPrepServiceTests.ReportsSelectedEntityBoundsWhenMultipleEntitiesAreSelected`; rebuilt-app inspector verified selected line+circle `X 9, Y 4, D 9.849`. |
| Full drawing bounds | Width, height, and diagonal of document bounds. | Done 7B1 | `MeasurementServiceTests.MeasuresEntityBoundsAsWidthHeightAndDiagonal`; document-level UI already reports bounds separately. |
| Dimension entities | Not a measurement target in Core; dimensions expose their own values. | N/A | Deliberate non-entity. |
| Constraint glyph/reference | Not a measurement target; constraints are relations, not measurable geometry. | N/A | Deliberate non-entity. |
| Entity-to-entity shortest distance | Required by V1 audit, with exact support for point, line, and circle pairs plus sampled fallback for other curve entities. | Done 7B2 core | `MeasurementServiceTests` covers point-line, crossing line-line, parallel line-line, circle-circle, line-circle, and sampled spline geometry. UI wiring for a shortest-distance command/readout is a separate future slice. |
