import re
from pathlib import Path

src = Path(__file__).resolve().parents[1] / "Services" / "FaceApiService.cs"
text = src.read_text(encoding="utf-8")
text = text.replace("public class FaceApiService", "public partial class FaceApiService", 1)
text = text.replace("private const int MaxPersistedFacesPerPerson = 100;", "")
text = text.replace("private const int MaxEnrichmentFramesPerLogin = 5;", "")
text = text.replace("private const double EnrichmentConfidenceMargin = 0.05;", "")
text = text.replace(
    "private static readonly TimeSpan ConfidenceThresholdCacheTtl = TimeSpan.FromSeconds(45);", ""
)
text = text.replace("MaxPersistedFacesPerPerson", "FaceApiConstants.MaxPersistedFacesPerPerson")
text = text.replace("MaxEnrichmentFramesPerLogin", "FaceApiConstants.MaxEnrichmentFramesPerLogin")
text = text.replace("EnrichmentConfidenceMargin", "FaceApiConstants.EnrichmentConfidenceMargin")
text = text.replace("ConfidenceThresholdCacheTtl", "FaceApiConstants.ConfidenceThresholdCacheTtl")
text = text.replace("MapErrorToCategory(", "FaceRecognitionErrorMapper.MapErrorToCategory(")
text = re.sub(
    r"\n    internal string MapErrorToCategory\(string\? error\)\s*\{.*?\n    \}\n",
    "\n",
    text,
    flags=re.S,
)
if "using Africanideasfaceapi.Services.FaceApi;" not in text:
    text = text.replace(
        "namespace Africanideasfaceapi.Services;",
        "using Africanideasfaceapi.Services.FaceApi;\n\nnamespace Africanideasfaceapi.Services;",
    )

lines = text.splitlines(keepends=True)
out_dir = Path(__file__).resolve().parents[1] / "Services" / "FaceApi"
out_dir.mkdir(exist_ok=True)

ranges = {
    "FaceApiService.Core.cs": (0, 112),
    "FaceApiService.Enrollment.cs": (112, 354),
    "FaceApiService.Recognition.cs": (354, 814),
    "FaceApiService.Support.cs": (488, 910),
    "FaceApiService.Analysis.cs": (910, 1162),
    "FaceApiService.DocumentEnrollment.cs": (1162, 1489),
    "FaceApiService.RecognitionById.cs": (1489, len(lines)),
}

partial_header = (
    "using Africanideasfaceapi.Helpers;\n"
    "using Microsoft.Azure.CognitiveServices.Vision.Face.Models;\n"
    "using Africanideasfaceapi.Models;\n"
    "using SixLabors.ImageSharp;\n"
    "using SixLabors.ImageSharp.Processing;\n"
    "using Africanideasfaceapi.Data;\n"
    "using Microsoft.EntityFrameworkCore;\n"
    "using Africanideasfaceapi.Hubs;\n"
    "using Africanideasfaceapi.Services.Interfaces;\n"
    "using Microsoft.AspNetCore.SignalR;\n"
    "using Microsoft.Extensions.Options;\n"
    "using Africanideasfaceapi.Models.Configuration;\n"
    "using System.Diagnostics;\n"
    "using Africanideasfaceapi.Services.FaceApi;\n\n"
    "namespace Africanideasfaceapi.Services;\n\n"
    "public partial class FaceApiService\n{\n"
)

core_lines = lines[0:22] + ["public partial class FaceApiService\n", "{\n"] + lines[22:112] + ["}\n"]
(out_dir / "FaceApiService.Core.cs").write_text("".join(core_lines), encoding="utf-8")

for fname, (start, end) in ranges.items():
    if fname == "FaceApiService.Core.cs":
        continue
    body = "".join(lines[start:end]).rstrip() + "\n"
    content = partial_header + body + "}\n"
    (out_dir / fname).write_text(content, encoding="utf-8")

src.unlink()
print("Wrote partials to", out_dir)
