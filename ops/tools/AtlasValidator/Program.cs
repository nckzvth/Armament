using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

var options = CliOptions.Parse(args);
if (!options.IsValid(out var optionsError))
{
    Console.Error.WriteLine(optionsError);
    Console.Error.WriteLine(CliOptions.Usage);
    return 2;
}

var scanner = new AtlasScanner(options);
var result = scanner.Run();

if (!string.IsNullOrWhiteSpace(options.ReportOut))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportOut)) ?? ".");
    File.WriteAllText(options.ReportOut, result.BuildTextReport());
    Console.WriteLine($"Atlas validation report written: {Path.GetFullPath(options.ReportOut)}");
}

if (!string.IsNullOrWhiteSpace(options.CatalogOut))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.CatalogOut)) ?? ".");
    File.WriteAllText(options.CatalogOut, JsonSerializer.Serialize(result.Catalog, JsonOptions.Pretty));
    Console.WriteLine($"Atlas catalog written: {Path.GetFullPath(options.CatalogOut)}");
}

Console.WriteLine($"Atlas validation complete: files={result.FilesValidated}, errors={result.Errors.Count}, warnings={result.Warnings.Count}");
if (result.Errors.Count > 0)
{
    Console.Error.WriteLine(result.BuildFailureSummary());
}

if (options.FailOnError && result.Errors.Count > 0)
{
    return 1;
}

return 0;

internal sealed class AtlasScanner
{
    private readonly CliOptions options;
    private readonly List<ValidationIssue> errors = new();
    private readonly List<ValidationIssue> warnings = new();
    private readonly AnimationCatalog catalog = new();
    private int filesValidated;

    public AtlasScanner(CliOptions options)
    {
        this.options = options;
    }

    public AtlasValidationResult Run()
    {
        if (!Directory.Exists(options.InputDir))
        {
            errors.Add(new ValidationIssue("INPUT", -1, $"Input directory not found: {options.InputDir}", true));
            return Build();
        }

        var atlasJsonFiles = Directory.GetFiles(options.InputDir, "*.json", SearchOption.AllDirectories)
            .Where(IsAtlasJson)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (atlasJsonFiles.Length == 0)
        {
            warnings.Add(new ValidationIssue("INPUT", -1, "No atlas json files found (expected *_atlas.json).", false));
            return Build();
        }

        var clipsByClass = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var jsonPath in atlasJsonFiles)
        {
            ValidateAtlasJson(jsonPath, clipsByClass);
        }

        ValidateClipmaps(clipsByClass);

        if (!string.IsNullOrWhiteSpace(options.MappingPatchIn))
        {
            ApplyMappingPatch(clipsByClass);
        }

        if (options.GenerateClipmaps)
        {
            GenerateClipmaps(clipsByClass);
        }

        return Build();
    }

    private AtlasValidationResult Build()
    {
        return new AtlasValidationResult(filesValidated, errors, warnings, catalog);
    }

    private bool IsAtlasJson(string path)
    {
        if (path.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var file = Path.GetFileName(path);
        if (file.StartsWith(".", StringComparison.Ordinal))
        {
            return false;
        }

        if (file.Equals("clipmap.json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return file.EndsWith("_atlas.json", StringComparison.OrdinalIgnoreCase) ||
               file.Contains("_atlas", StringComparison.OrdinalIgnoreCase);
    }

    private void ValidateAtlasJson(string jsonPath, Dictionary<string, HashSet<string>> clipsByClass)
    {
        AtlasJson? doc;
        try
        {
            doc = JsonSerializer.Deserialize<AtlasJson>(File.ReadAllText(jsonPath), JsonOptions.Default);
        }
        catch (Exception ex)
        {
            errors.Add(new ValidationIssue(jsonPath, -1, $"JSON parse failed: {ex.Message}", true));
            return;
        }

        if (doc is null)
        {
            errors.Add(new ValidationIssue(jsonPath, -1, "JSON parse returned null document.", true));
            return;
        }

        var classId = ResolveClassId(jsonPath);
        var entityType = ResolveEntityType(jsonPath);
        var clipId = ResolveClipId(jsonPath, doc);

        if (!clipsByClass.TryGetValue(classId, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            clipsByClass[classId] = set;
        }

        set.Add(clipId);
        catalog.Add(entityType, classId, clipId, jsonPath);

        var atlasPath = ResolveAtlasPath(jsonPath, doc);
        if (string.IsNullOrWhiteSpace(atlasPath) || !File.Exists(atlasPath))
        {
            errors.Add(new ValidationIssue(jsonPath, -1, $"Atlas PNG missing. Resolved path: '{atlasPath}'", true));
            return;
        }

        if (!TryReadPngSize(atlasPath, out var imageW, out var imageH, out var sizeError))
        {
            errors.Add(new ValidationIssue(jsonPath, -1, $"Unable to read PNG dimensions from '{atlasPath}': {sizeError}", true));
            return;
        }

        if (doc.AtlasWidth > 0 && doc.AtlasHeight > 0)
        {
            if (doc.AtlasWidth != imageW || doc.AtlasHeight != imageH)
            {
                errors.Add(new ValidationIssue(
                    jsonPath,
                    -1,
                    $"Atlas dimension mismatch. JSON={doc.AtlasWidth}x{doc.AtlasHeight} PNG={imageW}x{imageH}",
                    true));
            }
        }

        if (doc.Frames.Count == 0)
        {
            errors.Add(new ValidationIssue(jsonPath, -1, "No frames in atlas json.", true));
            return;
        }

        var directions = Math.Max(1, doc.Directions);
        var framesPerDirection = Math.Max(0, doc.FramesPerDirection);
        var frameCounts = new Dictionary<int, int>();
        var lastTimePerDirection = new Dictionary<int, float>();
        var timesByDirection = new Dictionary<int, List<float>>();

        var firstErrorPerFile = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < doc.Frames.Count; i++)
        {
            var frame = doc.Frames[i];

            Check(i, frame.X >= 0 && frame.Y >= 0, "x,y must be non-negative.");
            Check(i, frame.W > 0 && frame.H > 0, "w,h must be > 0.");
            Check(i, frame.X + frame.W <= imageW, $"(x+w) must be <= imageWidth ({imageW}). Got {frame.X + frame.W}.");
            Check(i, frame.Y + frame.H <= imageH, $"(y+h) must be <= imageHeight ({imageH}). Got {frame.Y + frame.H}.");
            Check(i, frame.Direction >= 0 && frame.Direction < directions, $"direction out of range [0,{directions - 1}] => {frame.Direction}.");

            Check(i, frame.PivotX >= 0 && frame.PivotY >= 0, "pivotX/pivotY must be non-negative.");

            var frameLocalOk = frame.PivotX <= frame.W && frame.PivotY <= frame.H;
            var cellW = doc.TargetCellWidth;
            var cellH = doc.TargetCellHeight;
            var cellLocalOk = cellW > 0 && cellH > 0 && frame.PivotX <= cellW && frame.PivotY <= cellH;

            if (options.PivotMode == PivotMode.Frame)
            {
                Check(i, frameLocalOk, $"frame-local pivot invalid. pivot=({frame.PivotX},{frame.PivotY}) frame=({frame.W},{frame.H}).");
            }
            else if (options.PivotMode == PivotMode.Cell)
            {
                Check(i, cellLocalOk, $"cell-local pivot invalid. pivot=({frame.PivotX},{frame.PivotY}) cell=({cellW},{cellH}).");
            }
            else
            {
                Check(i, frameLocalOk || cellLocalOk, $"pivot invalid for both frame-local and cell-local bounds. pivot=({frame.PivotX},{frame.PivotY}) frame=({frame.W},{frame.H}) cell=({cellW},{cellH}).");
            }

            frameCounts.TryGetValue(frame.Direction, out var count);
            frameCounts[frame.Direction] = count + 1;
            if (!timesByDirection.TryGetValue(frame.Direction, out var times))
            {
                times = new List<float>();
                timesByDirection[frame.Direction] = times;
            }

            times.Add(frame.Time);

            if (lastTimePerDirection.TryGetValue(frame.Direction, out var lastTime))
            {
                if (frame.Time < lastTime)
                {
                    Check(i, false, $"time is not monotonic within direction {frame.Direction}. previous={lastTime}, current={frame.Time}.");
                }
            }

            lastTimePerDirection[frame.Direction] = frame.Time;

            void Check(int frameIndex, bool ok, string message)
            {
                if (ok)
                {
                    return;
                }

                var key = $"{jsonPath}:{frameIndex}:{message}";
                if (!firstErrorPerFile.Contains(key))
                {
                    errors.Add(new ValidationIssue(jsonPath, frameIndex, message, true));
                    firstErrorPerFile.Add(key);
                }
            }
        }

        if (framesPerDirection > 0)
        {
            for (var d = 0; d < directions; d++)
            {
                frameCounts.TryGetValue(d, out var count);
                if (count != framesPerDirection)
                {
                    errors.Add(new ValidationIssue(jsonPath, -1, $"Direction {d} frame count mismatch. expected={framesPerDirection}, actual={count}.", true));
                }
            }

            var expectedTotal = directions * framesPerDirection;
            if (doc.Frames.Count != expectedTotal)
            {
                errors.Add(new ValidationIssue(jsonPath, -1, $"Total frame count mismatch. expected={expectedTotal}, actual={doc.Frames.Count}.", true));
            }
        }

        EvaluateTimelineModes(jsonPath, doc.Fps, timesByDirection);

        if (!string.IsNullOrWhiteSpace(options.OverlayDir))
        {
            TryWriteOverlay(atlasPath, jsonPath, clipId, imageW, imageH, doc.Frames);
        }

        if (options.WriteFixes)
        {
            TryWriteFixedJson(doc, jsonPath, imageW, imageH);
        }

        filesValidated++;
    }

    private void EvaluateTimelineModes(string jsonPath, float fps, Dictionary<int, List<float>> timesByDirection)
    {
        if (timesByDirection.Count == 0)
        {
            return;
        }

        var cumulative = 0;
        var duration = 0;
        var unknown = 0;
        foreach (var pair in timesByDirection)
        {
            var mode = ClassifyTimelineMode(pair.Value, fps);
            switch (mode)
            {
                case TimelineMode.Cumulative:
                    cumulative++;
                    break;
                case TimelineMode.Duration:
                    duration++;
                    break;
                default:
                    unknown++;
                    break;
            }
        }

        if (cumulative > 0 && duration > 0)
        {
            warnings.Add(new ValidationIssue(jsonPath, -1,
                $"Mixed frame-time modes detected across directions (cumulative={cumulative}, duration={duration}, unknown={unknown}).",
                false));
        }
    }

    private static TimelineMode ClassifyTimelineMode(List<float> times, float fps)
    {
        if (times.Count < 3)
        {
            return TimelineMode.Unknown;
        }

        var positives = new List<float>(times.Count);
        for (var i = 0; i < times.Count; i++)
        {
            if (times[i] > 0f)
            {
                positives.Add(times[i]);
            }
        }

        if (positives.Count < 3)
        {
            return TimelineMode.Unknown;
        }

        var monotonic = true;
        for (var i = 1; i < positives.Count; i++)
        {
            if (positives[i] < positives[i - 1])
            {
                monotonic = false;
                break;
            }
        }

        var max = positives.Max();
        var defaultDuration = 1f / MathF.Max(1f, fps <= 0 ? 24f : fps);
        if (monotonic && max > defaultDuration * 2.5f)
        {
            return TimelineMode.Cumulative;
        }

        return TimelineMode.Duration;
    }

    private void TryWriteFixedJson(AtlasJson doc, string jsonPath, int imageW, int imageH)
    {
        var changed = false;
        var fixedFrames = new List<AtlasFrameJson>(doc.Frames.Count);

        foreach (var frame in doc.Frames)
        {
            var f = frame;
            if (f.X < 0) { f.X = 0; changed = true; }
            if (f.Y < 0) { f.Y = 0; changed = true; }
            if (f.W < 1) { f.W = 1; changed = true; }
            if (f.H < 1) { f.H = 1; changed = true; }

            if (f.X + f.W > imageW)
            {
                f.W = Math.Max(1, imageW - f.X);
                changed = true;
            }

            if (f.Y + f.H > imageH)
            {
                f.H = Math.Max(1, imageH - f.Y);
                changed = true;
            }

            if (f.PivotX < 0) { f.PivotX = 0; changed = true; }
            if (f.PivotY < 0) { f.PivotY = 0; changed = true; }

            fixedFrames.Add(f);
        }

        if (!changed)
        {
            return;
        }

        var fixedDoc = doc with { Frames = fixedFrames };
        var fixedPath = Path.ChangeExtension(jsonPath, ".fixed.json");
        File.WriteAllText(fixedPath, JsonSerializer.Serialize(fixedDoc, JsonOptions.Pretty));
        warnings.Add(new ValidationIssue(jsonPath, -1, $"Wrote auto-fixed json: {fixedPath}", false));
    }

    private void TryWriteOverlay(string atlasPath, string jsonPath, string clipId, int imageW, int imageH, List<AtlasFrameJson> frames)
    {
        try
        {
            var outDir = Path.GetFullPath(options.OverlayDir!);
            Directory.CreateDirectory(outDir);

            using var image = Image.Load<Rgba32>(atlasPath);
            if (image.Width != imageW || image.Height != imageH)
            {
                warnings.Add(new ValidationIssue(jsonPath, -1, "Overlay skipped due to inconsistent loaded image size.", false));
                return;
            }

            foreach (var frame in frames)
            {
                DrawRect(image, frame.X, frame.Y, frame.W, frame.H, new Rgba32(255, 0, 0, 255));
            }

            var safeClip = MakeFileSafe(clipId);
            var outPath = Path.Combine(outDir, $"{safeClip}.overlay.png");
            image.SaveAsPng(outPath);
        }
        catch (Exception ex)
        {
            warnings.Add(new ValidationIssue(jsonPath, -1, $"Overlay generation failed: {ex.Message}", false));
        }
    }

    private static void DrawRect(Image<Rgba32> img, int x, int y, int w, int h, Rgba32 color)
    {
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var x0 = Math.Clamp(x, 0, img.Width - 1);
        var y0 = Math.Clamp(y, 0, img.Height - 1);
        var x1 = Math.Clamp(x + w - 1, 0, img.Width - 1);
        var y1 = Math.Clamp(y + h - 1, 0, img.Height - 1);

        for (var px = x0; px <= x1; px++)
        {
            img[px, y0] = color;
            img[px, y1] = color;
        }

        for (var py = y0; py <= y1; py++)
        {
            img[x0, py] = color;
            img[x1, py] = color;
        }
    }

    private void ValidateClipmaps(Dictionary<string, HashSet<string>> clipsByClass)
    {
        if (string.IsNullOrWhiteSpace(options.ClipmapDir) || !Directory.Exists(options.ClipmapDir))
        {
            return;
        }

        var clipmapFiles = Directory.GetFiles(options.ClipmapDir, "*.json", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).StartsWith("_", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var clipmapPath in clipmapFiles)
        {
            var classId = Path.GetFileNameWithoutExtension(clipmapPath);
            if (!clipsByClass.TryGetValue(classId, out var knownClipIds))
            {
                errors.Add(new ValidationIssue(clipmapPath, -1, $"No clips discovered for class '{classId}'.", true));
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(clipmapPath));
                var root = doc.RootElement;

                if (!TryGetObjectProperty(root, "default", out var defaultNode))
                {
                    errors.Add(new ValidationIssue(clipmapPath, -1, "Missing 'default' object.", true));
                    continue;
                }

                ValidateClipmapNode(clipmapPath, knownClipIds, defaultNode);

                if (TryGetObjectProperty(root, "specs", out var specsNode))
                {
                    foreach (var spec in specsNode.EnumerateObject())
                    {
                        if (spec.Value.ValueKind == JsonValueKind.Object)
                        {
                            ValidateClipmapNode(clipmapPath, knownClipIds, spec.Value, $"spec:{spec.Name}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(new ValidationIssue(clipmapPath, -1, $"Clipmap parse failed: {ex.Message}", true));
            }
        }
    }

    private void ApplyMappingPatch(Dictionary<string, HashSet<string>> clipsByClass)
    {
        var patchPath = Path.GetFullPath(options.MappingPatchIn!);
        if (!File.Exists(patchPath))
        {
            errors.Add(new ValidationIssue(patchPath, -1, "Mapping patch file not found.", true));
            return;
        }

        MappingPatchFile? patch;
        try
        {
            patch = JsonSerializer.Deserialize<MappingPatchFile>(File.ReadAllText(patchPath), JsonOptions.Default);
        }
        catch (Exception ex)
        {
            errors.Add(new ValidationIssue(patchPath, -1, $"Failed to parse mapping patch: {ex.Message}", true));
            return;
        }

        if (patch is null || string.IsNullOrWhiteSpace(patch.ClassId) || patch.Updates.Count == 0)
        {
            errors.Add(new ValidationIssue(patchPath, -1, "Mapping patch missing classId or updates.", true));
            return;
        }

        var classId = patch.ClassId.Trim().ToLowerInvariant();
        if (!clipsByClass.TryGetValue(classId, out var knownClipIds))
        {
            errors.Add(new ValidationIssue(patchPath, -1, $"No discovered clip IDs for class '{classId}'.", true));
            return;
        }

        var clipmapDir = options.ClipmapDir;
        if (string.IsNullOrWhiteSpace(clipmapDir))
        {
            errors.Add(new ValidationIssue(patchPath, -1, "No clipmap directory configured.", true));
            return;
        }

        Directory.CreateDirectory(clipmapDir);
        var clipmapPath = Path.Combine(clipmapDir, $"{classId}.json");
        JsonDocument? existingDoc = null;
        if (File.Exists(clipmapPath))
        {
            existingDoc = JsonDocument.Parse(File.ReadAllText(clipmapPath));
        }

        var root = existingDoc is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(existingDoc.RootElement.GetRawText(), JsonOptions.Default)
              ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (!root.TryGetValue("default", out var defaultNode) || defaultNode is null)
        {
            root["default"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        if (!root.TryGetValue("specs", out var specsNode) || specsNode is null)
        {
            root["specs"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        var defaultMap = ToMutableMap(root["default"]);
        var specsMap = ToMutableMap(root["specs"]);

        foreach (var upd in patch.Updates)
        {
            if (string.IsNullOrWhiteSpace(upd.Value))
            {
                errors.Add(new ValidationIssue(patchPath, -1, "Patch update has empty value.", true));
                continue;
            }

            if (!knownClipIds.Contains(upd.Value))
            {
                errors.Add(new ValidationIssue(patchPath, -1, $"Patch references unknown clipId '{upd.Value}'.", true));
                continue;
            }

            var scope = string.IsNullOrWhiteSpace(upd.Scope) ? "default" : upd.Scope.Trim();
            var field = upd.Field.Trim();
            if (string.IsNullOrWhiteSpace(field))
            {
                errors.Add(new ValidationIssue(patchPath, -1, "Patch update field is empty.", true));
                continue;
            }

            var target = scope.Equals("default", StringComparison.OrdinalIgnoreCase)
                ? defaultMap
                : EnsureSpecNode(specsMap, scope);

            if (field.StartsWith("cast:", StringComparison.OrdinalIgnoreCase))
            {
                var slot = field["cast:".Length..].Trim().ToUpperInvariant();
                if (!target.TryGetValue("castClipBySlotLabel", out var castNode) || castNode is null)
                {
                    target["castClipBySlotLabel"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                }

                var castMap = ToMutableMap(target["castClipBySlotLabel"]);
                castMap[slot] = upd.Value;
                target["castClipBySlotLabel"] = castMap;
            }
            else
            {
                target[field] = upd.Value;
            }
        }

        root["default"] = defaultMap;
        root["specs"] = specsMap;

        File.WriteAllText(clipmapPath, JsonSerializer.Serialize(root, JsonOptions.Pretty));
        warnings.Add(new ValidationIssue(clipmapPath, -1, $"Applied mapping patch from {patchPath}", false));
    }

    private static Dictionary<string, object?> EnsureSpecNode(Dictionary<string, object?> specsMap, string specId)
    {
        if (!specsMap.TryGetValue(specId, out var existing) || existing is null)
        {
            var created = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            specsMap[specId] = created;
            return created;
        }

        return ToMutableMap(existing);
    }

    private static Dictionary<string, object?> ToMutableMap(object? node)
    {
        if (node is Dictionary<string, object?> dict)
        {
            return dict;
        }

        if (node is JsonElement el && el.ValueKind == JsonValueKind.Object)
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(el.GetRawText(), JsonOptions.Default)
                ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private void ValidateClipmapNode(string clipmapPath, HashSet<string> knownClipIds, JsonElement node, string scope = "default")
    {
        ValidateField("idleClipId");
        ValidateField("moveClipId");
        ValidateField("blockLoopClipId");
        ValidateField("heavyClipId");

        if (TryGetArrayProperty(node, "fastChainClipIds", out var chain))
        {
            foreach (var c in chain.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var clipId = c.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(clipId) && !knownClipIds.Contains(clipId))
                {
                    errors.Add(new ValidationIssue(clipmapPath, -1, $"[{scope}] fastChainClipIds unknown clipId '{clipId}'.", true));
                }
            }
        }

        if (TryGetObjectProperty(node, "castClipBySlotLabel", out var cast))
        {
            foreach (var pair in cast.EnumerateObject())
            {
                if (pair.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var clipId = pair.Value.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(clipId) && !knownClipIds.Contains(clipId))
                {
                    errors.Add(new ValidationIssue(clipmapPath, -1, $"[{scope}] slot '{pair.Name}' unknown clipId '{clipId}'.", true));
                }
            }
        }

        void ValidateField(string name)
        {
            if (!TryGetStringProperty(node, name, out var clipId))
            {
                return;
            }
            if (!string.IsNullOrWhiteSpace(clipId) && !knownClipIds.Contains(clipId))
            {
                errors.Add(new ValidationIssue(clipmapPath, -1, $"[{scope}] field '{name}' unknown clipId '{clipId}'.", true));
            }
        }
    }

    private static bool TryGetObjectProperty(JsonElement node, string logicalName, out JsonElement value)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in node.EnumerateObject())
            {
                if (prop.Name.Equals(logicalName, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Object)
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetArrayProperty(JsonElement node, string logicalName, out JsonElement value)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in node.EnumerateObject())
            {
                if (prop.Name.Equals(logicalName, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetStringProperty(JsonElement node, string logicalName, out string value)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in node.EnumerateObject())
            {
                if (prop.Name.Equals(logicalName, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                {
                    value = prop.Value.GetString() ?? string.Empty;
                    return true;
                }
            }
        }

        value = string.Empty;
        return false;
    }

    private void GenerateClipmaps(Dictionary<string, HashSet<string>> clipsByClass)
    {
        if (string.IsNullOrWhiteSpace(options.GenerateClipmapsDir))
        {
            return;
        }

        Directory.CreateDirectory(options.GenerateClipmapsDir);
        foreach (var pair in clipsByClass)
        {
            var classId = pair.Key;
            var clips = pair.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            if (clips.Length == 0)
            {
                continue;
            }

            string Pick(params string[] terms)
            {
                foreach (var c in clips)
                {
                    var ok = true;
                    foreach (var t in terms)
                    {
                        if (!c.Contains(t, StringComparison.OrdinalIgnoreCase))
                        {
                            ok = false;
                            break;
                        }
                    }

                    if (ok)
                    {
                        return c;
                    }
                }

                return string.Empty;
            }

            var generated = new GeneratedClipmap
            {
                Default = new GeneratedClipmapDef
                {
                    IdleClipId = Pick("idle"),
                    MoveClipId = Pick("run", "forward"),
                    BlockLoopClipId = Pick("block", "loop"),
                    HeavyClipId = Pick("attack", "r3"),
                    FastChainClipIds = new List<string>
                    {
                        Pick("attackshield01"),
                        Pick("attack-r1"),
                        Pick("attackshield02")
                    }.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    CastClipBySlotLabel = new Dictionary<string, string>
                    {
                        ["E"] = Pick("attackshield01"),
                        ["R"] = Pick("block", "hit"),
                        ["Q"] = Pick("attack", "r2"),
                        ["T"] = Pick("turn", "right"),
                        ["1"] = Pick("attackshield02"),
                        ["2"] = Pick("stun"),
                        ["3"] = Pick("attack", "r3"),
                        ["4"] = Pick("block", "hit")
                    }
                }
            };

            var outPath = Path.Combine(options.GenerateClipmapsDir, $"{classId}.generated.json");
            File.WriteAllText(outPath, JsonSerializer.Serialize(generated, JsonOptions.Pretty));
            warnings.Add(new ValidationIssue(outPath, -1, "Generated clipmap skeleton.", false));
        }
    }

    private static string ResolveClipId(string jsonPath, AtlasJson doc)
    {
        if (!string.IsNullOrWhiteSpace(doc.ClipId))
        {
            return doc.ClipId.Trim();
        }

        return Path.GetFileNameWithoutExtension(jsonPath);
    }

    private static string ResolveAtlasPath(string jsonPath, AtlasJson doc)
    {
        var atlasRaw = string.IsNullOrWhiteSpace(doc.AtlasTexture) ? doc.Atlas : doc.AtlasTexture;
        if (string.IsNullOrWhiteSpace(atlasRaw))
        {
            var sidecar = Path.ChangeExtension(jsonPath, ".png");
            if (File.Exists(sidecar))
            {
                return sidecar;
            }

            var dir = Path.GetDirectoryName(jsonPath) ?? string.Empty;
            return Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly).FirstOrDefault() ?? string.Empty;
        }

        if (Path.IsPathRooted(atlasRaw))
        {
            return atlasRaw;
        }

        var baseDir = Path.GetDirectoryName(jsonPath) ?? string.Empty;
        return Path.GetFullPath(Path.Combine(baseDir, atlasRaw));
    }

    private static bool TryReadPngSize(string path, out int width, out int height, out string error)
    {
        width = 0;
        height = 0;
        error = string.Empty;

        try
        {
            var info = Image.Identify(path);
            if (info is null)
            {
                error = "Image.Identify returned null";
                return false;
            }

            width = info.Width;
            height = info.Height;
            if (width <= 0 || height <= 0)
            {
                error = $"invalid dimensions {width}x{height}";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private string ResolveClassId(string jsonPath)
    {
        var rel = Path.GetRelativePath(options.InputDir, jsonPath);
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

        var markers = new[] { "characters", "enemies", "npcs", "props" };
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (markers.Contains(parts[i], StringComparer.OrdinalIgnoreCase) && i + 1 < parts.Length)
            {
                return parts[i + 1].Trim().ToLowerInvariant();
            }
        }

        if (parts.Length > 0)
        {
            return parts[0].Trim().ToLowerInvariant();
        }

        return "unknown";
    }

    private string ResolveEntityType(string jsonPath)
    {
        var rel = Path.GetRelativePath(options.InputDir, jsonPath);
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        var markers = new[] { "characters", "enemies", "npcs", "props" };
        for (var i = 0; i < parts.Length; i++)
        {
            if (markers.Contains(parts[i], StringComparer.OrdinalIgnoreCase))
            {
                return parts[i].Trim().ToLowerInvariant();
            }
        }

        return "general";
    }

    private static string MakeFileSafe(string input)
    {
        var sb = new StringBuilder(input.Length);
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in input)
        {
            sb.Append(invalid.Contains(c) ? '_' : c);
        }

        return sb.ToString();
    }
}

internal sealed class AtlasValidationResult
{
    public AtlasValidationResult(int filesValidated, List<ValidationIssue> errors, List<ValidationIssue> warnings, AnimationCatalog catalog)
    {
        FilesValidated = filesValidated;
        Errors = errors;
        Warnings = warnings;
        Catalog = catalog;
    }

    public int FilesValidated { get; }
    public List<ValidationIssue> Errors { get; }
    public List<ValidationIssue> Warnings { get; }
    public AnimationCatalog Catalog { get; }

    public string BuildFailureSummary()
    {
        if (Errors.Count == 0)
        {
            return "No errors.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Atlas Validation Failures:");
        foreach (var e in Errors.Take(120))
        {
            sb.AppendLine($"- {e.FilePath} (frame {(e.FrameIndex < 0 ? "n/a" : e.FrameIndex)}): {e.Message}");
        }

        if (Errors.Count > 120)
        {
            sb.AppendLine($"... and {Errors.Count - 120} more.");
        }

        return sb.ToString();
    }

    public string BuildTextReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Atlas Validation Report");
        sb.AppendLine($"Generated: {DateTime.UtcNow:O}");
        sb.AppendLine($"Files validated: {FilesValidated}");
        sb.AppendLine($"Errors: {Errors.Count}");
        sb.AppendLine($"Warnings: {Warnings.Count}");
        sb.AppendLine();

        sb.AppendLine("Errors");
        if (Errors.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var e in Errors)
            {
                sb.AppendLine($"- file: {e.FilePath}");
                sb.AppendLine($"  frame: {(e.FrameIndex < 0 ? "n/a" : e.FrameIndex)}");
                sb.AppendLine($"  reason: {e.Message}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Warnings");
        if (Warnings.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var w in Warnings)
            {
                sb.AppendLine($"- file: {w.FilePath}");
                sb.AppendLine($"  frame: {(w.FrameIndex < 0 ? "n/a" : w.FrameIndex)}");
                sb.AppendLine($"  reason: {w.Message}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Catalog Summary");
        foreach (var type in Catalog.Groups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"- {type.Key}");
            foreach (var cls in type.Value.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"  - {cls.Key}: {cls.Value.Count} clip(s)");
            }
        }

        return sb.ToString();
    }
}

internal sealed record ValidationIssue(string FilePath, int FrameIndex, string Message, bool IsError);

internal sealed class AnimationCatalog
{
    public Dictionary<string, Dictionary<string, List<AnimationCatalogEntry>>> Groups { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string entityType, string classId, string clipId, string jsonPath)
    {
        if (!Groups.TryGetValue(entityType, out var classes))
        {
            classes = new Dictionary<string, List<AnimationCatalogEntry>>(StringComparer.OrdinalIgnoreCase);
            Groups[entityType] = classes;
        }

        if (!classes.TryGetValue(classId, out var entries))
        {
            entries = new List<AnimationCatalogEntry>();
            classes[classId] = entries;
        }

        entries.Add(new AnimationCatalogEntry { ClipId = clipId, JsonPath = jsonPath });
    }
}

internal sealed class AnimationCatalogEntry
{
    public string ClipId { get; set; } = string.Empty;
    public string JsonPath { get; set; } = string.Empty;
}

internal enum PivotMode
{
    Auto,
    Frame,
    Cell
}

internal enum TimelineMode
{
    Unknown = 0,
    Cumulative = 1,
    Duration = 2
}

internal sealed class CliOptions
{
    public string InputDir { get; private set; } = string.Empty;
    public string? ClipmapDir { get; private set; }
    public string? OverlayDir { get; private set; }
    public string? ReportOut { get; private set; }
    public string? CatalogOut { get; private set; }
    public bool FailOnError { get; private set; }
    public bool WriteFixes { get; private set; }
    public PivotMode PivotMode { get; private set; } = PivotMode.Auto;
    public bool GenerateClipmaps { get; private set; }
    public string? GenerateClipmapsDir { get; private set; }
    public string? MappingPatchIn { get; private set; }

    public static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true
    };

    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string Usage =>
        "Usage:\n" +
        "  dotnet run --project ops/tools/AtlasValidator -- --input-dir <dir> [--fail-on-error]\n" +
        "Options:\n" +
        "  --input-dir <dir>            Root directory to scan (required).\n" +
        "  --clipmap-dir <dir>          Clipmap directory (default: <input-dir>/clipmaps if exists).\n" +
        "  --pivot-mode auto|frame|cell Pivot validation mode (default: auto).\n" +
        "  --fail-on-error              Return exit code 1 when validation errors exist.\n" +
        "  --report-out <file>          Write human-readable validation report file.\n" +
        "  --catalog-out <file>         Write organized animation catalog JSON.\n" +
        "  --overlay-dir <dir>          Write overlay PNGs with frame rectangles.\n" +
        "  --write-fixes                Write <clip>.fixed.json with clamped rect/pivot fixes.\n" +
        "  --generate-clipmaps          Generate per-class clipmap skeletons.\n" +
        "  --generate-clipmaps-dir <d>  Output directory for generated clipmaps.\n" +
        "  --mapping-patch-in <file>    Apply a clipmap mapping patch file.\n";

    public bool IsValid(out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(InputDir))
        {
            error = "Missing required --input-dir";
            return false;
        }

        if (GenerateClipmaps && string.IsNullOrWhiteSpace(GenerateClipmapsDir))
        {
            error = "--generate-clipmaps requires --generate-clipmaps-dir";
            return false;
        }

        return true;
    }

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions
        {
            InputDir = Path.Combine(ResolveRepoRoot(), "content", "animations")
        };

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--input-dir":
                    if (i + 1 < args.Length)
                    {
                        o.InputDir = Path.GetFullPath(args[++i]);
                    }
                    break;
                case "--clipmap-dir":
                    if (i + 1 < args.Length)
                    {
                        o.ClipmapDir = Path.GetFullPath(args[++i]);
                    }
                    break;
                case "--overlay-dir":
                    if (i + 1 < args.Length)
                    {
                        o.OverlayDir = Path.GetFullPath(args[++i]);
                    }
                    break;
                case "--report-out":
                    if (i + 1 < args.Length)
                    {
                        o.ReportOut = Path.GetFullPath(args[++i]);
                    }
                    break;
                case "--catalog-out":
                    if (i + 1 < args.Length)
                    {
                        o.CatalogOut = Path.GetFullPath(args[++i]);
                    }
                    break;
                case "--fail-on-error":
                    o.FailOnError = true;
                    break;
                case "--write-fixes":
                    o.WriteFixes = true;
                    break;
                case "--generate-clipmaps":
                    o.GenerateClipmaps = true;
                    break;
                case "--generate-clipmaps-dir":
                    if (i + 1 < args.Length)
                    {
                        o.GenerateClipmapsDir = Path.GetFullPath(args[++i]);
                    }
                    break;
                case "--mapping-patch-in":
                    if (i + 1 < args.Length)
                    {
                        o.MappingPatchIn = Path.GetFullPath(args[++i]);
                    }
                    break;
                case "--pivot-mode":
                    if (i + 1 < args.Length)
                    {
                        var v = args[++i].Trim().ToLowerInvariant();
                        o.PivotMode = v switch
                        {
                            "frame" => PivotMode.Frame,
                            "cell" => PivotMode.Cell,
                            _ => PivotMode.Auto
                        };
                    }
                    break;
            }
        }

        o.InputDir = Path.GetFullPath(o.InputDir);
        if (string.IsNullOrWhiteSpace(o.ClipmapDir))
        {
            var defaultClipmap = Path.Combine(o.InputDir, "clipmaps");
            if (Directory.Exists(defaultClipmap))
            {
                o.ClipmapDir = defaultClipmap;
            }
        }

        return o;
    }

    private static string ResolveRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 8; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "content")) && Directory.Exists(Path.Combine(dir, "ops")))
            {
                return dir;
            }

            var parent = Directory.GetParent(dir);
            if (parent is null)
            {
                break;
            }

            dir = parent.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}

internal sealed record AtlasJson
{
    [JsonPropertyName("clipId")] public string ClipId { get; init; } = string.Empty;
    [JsonPropertyName("atlasTexture")] public string AtlasTexture { get; init; } = string.Empty;
    [JsonPropertyName("atlas")] public string Atlas { get; init; } = string.Empty;
    [JsonPropertyName("atlasWidth")] public int AtlasWidth { get; init; }
    [JsonPropertyName("atlasHeight")] public int AtlasHeight { get; init; }
    [JsonPropertyName("directions")] public int Directions { get; init; }
    [JsonPropertyName("framesPerDirection")] public int FramesPerDirection { get; init; }
    [JsonPropertyName("fps")] public float Fps { get; init; } = 24f;
    [JsonPropertyName("targetCellWidth")] public int TargetCellWidth { get; init; }
    [JsonPropertyName("targetCellHeight")] public int TargetCellHeight { get; init; }
    [JsonPropertyName("frames")] public List<AtlasFrameJson> Frames { get; init; } = new();
}

internal record AtlasFrameJson
{
    [JsonPropertyName("direction")] public int Direction { get; set; }
    [JsonPropertyName("frame")] public int Frame { get; set; }
    [JsonPropertyName("time")] public float Time { get; set; }
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("w")] public int W { get; set; }
    [JsonPropertyName("h")] public int H { get; set; }
    [JsonPropertyName("pivotX")] public int PivotX { get; set; }
    [JsonPropertyName("pivotY")] public int PivotY { get; set; }
}

internal sealed class GeneratedClipmap
{
    [JsonPropertyName("default")] public GeneratedClipmapDef Default { get; set; } = new();
    [JsonPropertyName("specs")] public Dictionary<string, GeneratedClipmapDef> Specs { get; set; } = new();
}

internal sealed class GeneratedClipmapDef
{
    [JsonPropertyName("idleClipId")] public string IdleClipId { get; set; } = string.Empty;
    [JsonPropertyName("moveClipId")] public string MoveClipId { get; set; } = string.Empty;
    [JsonPropertyName("blockLoopClipId")] public string BlockLoopClipId { get; set; } = string.Empty;
    [JsonPropertyName("heavyClipId")] public string HeavyClipId { get; set; } = string.Empty;
    [JsonPropertyName("fastChainClipIds")] public List<string> FastChainClipIds { get; set; } = new();
    [JsonPropertyName("castClipBySlotLabel")] public Dictionary<string, string> CastClipBySlotLabel { get; set; } = new();
}

internal sealed class MappingPatchFile
{
    [JsonPropertyName("classId")] public string ClassId { get; set; } = string.Empty;
    [JsonPropertyName("updates")] public List<MappingPatchUpdate> Updates { get; set; } = new();
}

internal sealed class MappingPatchUpdate
{
    [JsonPropertyName("scope")] public string Scope { get; set; } = "default";
    [JsonPropertyName("field")] public string Field { get; set; } = string.Empty;
    [JsonPropertyName("value")] public string Value { get; set; } = string.Empty;
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
