Bundled text extractors for the native `read` tool (PDF / Office / CAD).
These files are NOT Skills: they ship with the app under InternalTools/ and are not listed in <available_skills>.
Do NOT add SKILL.md under InternalTools/ — skills belong only under the Skills/ folder; this tree is executables + this README only.

Place executables using the layout expected by OpenLum.Core/Extractors/ExeReadDispatcher.cs:

  pdf/read-pdf.exe          — .pdf
  docx/read-docx.exe        — .docx
  pptx/read-pptx.exe        — .pptx
  docppt/read-docppt.exe    — .doc, .ppt
  dxf/read-dxf.exe          — .dxf
  dwg/read-dwg.exe          — .dwg

CLI (each exe):  "<file>" --start N --limit N

After adding or updating exes, rebuild so CopyToOutputDirectory / publish copies them.
