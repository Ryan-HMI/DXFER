# Corner Input And Feedback Contract

Preserve independent palettes, canvas commands and undo transactions. This is
validation and footer containment, not the visual-design overhaul.

- Display the exact in-progress numeric text; invalid/empty/nonfinite values
  block both fillet and chamfer before any geometry or history mutation.
- Correcting the value reenables the existing gesture. Never silently reuse
  the previous accepted value or clamp a positive displayed value to another.
- Footer first row is a flexible, wrapping status region and fixed-size corner
  input. Measurements occupy a separate full-width wrapping row.
- Long entity/vertex IDs truncate within bounded measurement chips with their
  full value in a hover title. They cannot widen the footer or narrow feedback.
- On narrow windows the input follows the prompt. Controls remain reachable;
  palettes, active tool, selection and document history retain their behavior.
- Verify actual invalid-input gestures and unchanged geometry/history, valid
  recovery, plus screenshots at1188/1440 and390 widths before deployment claims.
