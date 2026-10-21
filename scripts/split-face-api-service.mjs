import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const root = path.join(__dirname, "..");
const srcPath = path.join(root, "Services", "FaceApiService.cs");
const outDir = path.join(root, "Services", "FaceApi");

const rawLines = fs.readFileSync(srcPath, "utf8").split(/\r?\n/);

function transformBody(body) {
  return body
    .replaceAll("MaxPersistedFacesPerPerson", "FaceApiConstants.MaxPersistedFacesPerPerson")
    .replaceAll("MaxEnrichmentFramesPerLogin", "FaceApiConstants.MaxEnrichmentFramesPerLogin")
    .replaceAll("EnrichmentConfidenceMargin", "FaceApiConstants.EnrichmentConfidenceMargin")
    .replaceAll("ConfidenceThresholdCacheTtl", "FaceApiConstants.ConfidenceThresholdCacheTtl")
    .replaceAll("MapErrorToCategory(", "FaceRecognitionErrorMapper.MapErrorToCategory(");
}

function slice(start, end) {
  return transformBody(rawLines.slice(start, end).join("\n").trimEnd() + "\n");
}

const partialHeader = `using Africanideasfaceapi.Helpers;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;
using Africanideasfaceapi.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Africanideasfaceapi.Data;
using Microsoft.EntityFrameworkCore;
using Africanideasfaceapi.Hubs;
using Africanideasfaceapi.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Africanideasfaceapi.Models.Configuration;
using System.Diagnostics;
using Africanideasfaceapi.Services.FaceApi;

namespace Africanideasfaceapi.Services;

public partial class FaceApiService
{
`;

function writePartial(name, ranges) {
  const body = ranges.map(([s, e]) => slice(s, e)).join("\n");
  fs.writeFileSync(path.join(outDir, name), partialHeader + body + "}\n", "utf8");
}

const classIdx = rawLines.findIndex((l) => l.includes("class FaceApiService"));
const openBraceIdx = classIdx + 1;
const nsIdx = rawLines.findIndex((l) => l.startsWith("namespace Africanideasfaceapi.Services"));

const headerLines = rawLines.slice(0, nsIdx).map((l) =>
  l.replace('[assembly: InternalsVisibleTo("UnitTests")]', '[assembly: InternalsVisibleTo("Africanideasfaceapi.Tests")]')
);
if (!headerLines.some((l) => l.includes("using Africanideasfaceapi.Services.FaceApi"))) {
  const asmIdx = headerLines.findIndex((l) => l.startsWith("[assembly:"));
  const insertAt = asmIdx >= 0 ? asmIdx : headerLines.length;
  headerLines.splice(insertAt, 0, "using Africanideasfaceapi.Services.FaceApi;");
}

const coreFields = rawLines
  .slice(openBraceIdx + 1, 112)
  .filter(
    (l) =>
      !l.includes("private const int MaxPersistedFacesPerPerson") &&
      !l.includes("private const int MaxEnrichmentFramesPerLogin") &&
      !l.includes("private const double EnrichmentConfidenceMargin") &&
      !l.includes("private static readonly TimeSpan ConfidenceThresholdCacheTtl")
  )
  .join("\n")
  .trimEnd();

fs.writeFileSync(
  path.join(outDir, "FaceApiService.Core.cs"),
  headerLines.join("\n") +
    "\n\n" +
    rawLines.slice(nsIdx, classIdx).join("\n") +
    "\npublic partial class FaceApiService\n{\n" +
    coreFields +
    "\n}\n",
  "utf8"
);

writePartial("FaceApiService.Enrollment.cs", [[113, 356]]);
writePartial("FaceApiService.Recognition.cs", [
  [356, 488],
  [554, 815],
]);
writePartial("FaceApiService.Support.cs", [
  [488, 554],
  [849, 911],
]);
writePartial("FaceApiService.Analysis.cs", [[911, 1167]]);
writePartial("FaceApiService.DocumentEnrollment.cs", [[1167, 1489]]);
writePartial("FaceApiService.RecognitionById.cs", [[1490, 1535]]);

fs.unlinkSync(srcPath);
console.log("Wrote partials to", outDir);
