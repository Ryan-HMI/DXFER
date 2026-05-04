export function downloadTextFile(fileName, text, contentType = "text/plain") {
  const blob = new Blob([String(text ?? "")], { type: contentType });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = String(fileName || "drawing.dxf");
  anchor.style.display = "none";
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}
