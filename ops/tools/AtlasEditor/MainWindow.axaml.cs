using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Armament.AtlasEditor;

public partial class MainWindow : Window
{
    private static readonly string[] EntityMarkers = { "characters", "enemies", "npcs", "props" };
    private static readonly string[] DirectionLabels = { "NE", "E", "SE", "S", "SW", "W", "NW", "N" };

    private readonly string repoRoot;
    private readonly JsonSerializerOptions prettyJson = new() { WriteIndented = true };

    private AtlasDocument? currentAtlas;
    private string currentAtlasPath = string.Empty;
    private Bitmap? currentBitmap;
    private string currentClassId = string.Empty;
    private string currentEntityType = string.Empty;
    private string currentClipmapPath = string.Empty;
    private ClipmapFile currentClipmap = new();
    private string currentFullClipId = string.Empty;
    private string currentConciseClipId = string.Empty;
    private List<AssetNode> allTreeRoots = new();

    private bool suppressFrameEvents;

    public MainWindow()
    {
        InitializeComponent();
        repoRoot = ResolveRepoRoot();
        var defaultInput = ResolveInitialInputDir(repoRoot);
        InputDirBox.Text = defaultInput;
        Log("Atlas Editor ready.");
    }

    private void OnLoadClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var inputDir = NormalizeInputDir();
        BuildTree(inputDir);
    }

    private void OnClipFilterChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyTreeFilter((ClipFilterBox.Text ?? string.Empty).Trim());
    }

    private void OnZoomChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        ApplyZoom();
    }

    private void OnZoomInClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ZoomSlider.Value = Math.Min(8, ZoomSlider.Value + 0.25);
        ApplyZoom();
    }

    private void OnZoomOutClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ZoomSlider.Value = Math.Max(0.25, ZoomSlider.Value - 0.25);
        ApplyZoom();
    }

    private void OnZoomToFrameClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ZoomSlider.Value = 4;
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        var zoom = Math.Clamp(ZoomSlider.Value, 0.25, 8);
        AtlasCanvas.RenderTransform = new ScaleTransform(zoom, zoom);
    }

    private async void OnValidateClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var inputDir = NormalizeInputDir();
        var reportPath = Path.Combine(repoRoot, ".artifacts", "atlas-validation-report.txt");
        var catalogPath = Path.Combine(repoRoot, ".artifacts", "atlas-catalog.json");
        var overlayDir = Path.Combine(repoRoot, ".artifacts", "atlas-overlays");

        Directory.CreateDirectory(Path.Combine(repoRoot, ".artifacts"));

        var cmd = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Arguments =
                $"run --project \"{Path.Combine(repoRoot, "ops", "tools", "AtlasValidator")}\" -- " +
                $"--input-dir \"{inputDir}\" --fail-on-error --report-out \"{reportPath}\" --catalog-out \"{catalogPath}\" --overlay-dir \"{overlayDir}\""
        };

        Log("Running AtlasValidator...");
        using var proc = Process.Start(cmd);
        if (proc is null)
        {
            Log("Failed to launch AtlasValidator process.");
            return;
        }

        var output = await proc.StandardOutput.ReadToEndAsync();
        var error = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (!string.IsNullOrWhiteSpace(output))
        {
            Log(output.Trim());
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            Log(error.Trim());
        }

        Log($"AtlasValidator exit code: {proc.ExitCode}");
        Log($"Report: {reportPath}");
        Log($"Catalog: {catalogPath}");
    }

    private void OnSaveClipClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (currentAtlas is null || string.IsNullOrWhiteSpace(currentAtlasPath))
        {
            Log("No clip selected.");
            return;
        }

        File.WriteAllText(currentAtlasPath, currentAtlas.Root.ToJsonString(prettyJson));
        Log($"Saved clip: {currentAtlasPath}");
    }

    private void OnSaveMappingClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(currentClassId))
        {
            Log("No class selected for mapping.");
            return;
        }

        var clipmapDir = Path.Combine(NormalizeInputDir(), "clipmaps");
        Directory.CreateDirectory(clipmapDir);
        currentClipmapPath = Path.Combine(clipmapDir, $"{currentClassId}.json");

        var def = currentClipmap.Default;
        def.IdleClipId = IdleClipBox.Text?.Trim() ?? string.Empty;
        def.MoveClipId = MoveClipBox.Text?.Trim() ?? string.Empty;
        def.RunForwardClipId = RunForwardClipBox.Text?.Trim() ?? string.Empty;
        def.RunBackwardClipId = RunBackwardClipBox.Text?.Trim() ?? string.Empty;
        def.RunLeftClipId = RunLeftClipBox.Text?.Trim() ?? string.Empty;
        def.RunRightClipId = RunRightClipBox.Text?.Trim() ?? string.Empty;
        def.RunForwardLeftClipId = RunForwardLeftClipBox.Text?.Trim() ?? string.Empty;
        def.RunForwardRightClipId = RunForwardRightClipBox.Text?.Trim() ?? string.Empty;
        def.RunBackwardLeftClipId = RunBackwardLeftClipBox.Text?.Trim() ?? string.Empty;
        def.RunBackwardRightClipId = RunBackwardRightClipBox.Text?.Trim() ?? string.Empty;
        def.StrafeLeftClipId = StrafeLeftClipBox.Text?.Trim() ?? string.Empty;
        def.StrafeRightClipId = StrafeRightClipBox.Text?.Trim() ?? string.Empty;
        def.StrafeForwardLeftClipId = StrafeForwardLeftClipBox.Text?.Trim() ?? string.Empty;
        def.StrafeForwardRightClipId = StrafeForwardRightClipBox.Text?.Trim() ?? string.Empty;
        def.StrafeBackwardLeftClipId = StrafeBackwardLeftClipBox.Text?.Trim() ?? string.Empty;
        def.StrafeBackwardRightClipId = StrafeBackwardRightClipBox.Text?.Trim() ?? string.Empty;
        def.TurnLeftClipId = TurnLeftClipBox.Text?.Trim() ?? string.Empty;
        def.TurnRightClipId = TurnRightClipBox.Text?.Trim() ?? string.Empty;
        def.BlockLoopClipId = BlockClipBox.Text?.Trim() ?? string.Empty;
        def.HeavyClipId = HeavyClipBox.Text?.Trim() ?? string.Empty;

        def.FastChainClipIds = (FastChainBox.Text ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        def.CastClipBySlotLabel["E"] = CastEBox.Text?.Trim() ?? string.Empty;
        def.CastClipBySlotLabel["R"] = CastRBox.Text?.Trim() ?? string.Empty;
        def.CastClipBySlotLabel["Q"] = CastQBox.Text?.Trim() ?? string.Empty;
        def.CastClipBySlotLabel["T"] = CastTBox.Text?.Trim() ?? string.Empty;
        def.CastClipBySlotLabel["1"] = Cast1Box.Text?.Trim() ?? string.Empty;
        def.CastClipBySlotLabel["2"] = Cast2Box.Text?.Trim() ?? string.Empty;
        def.CastClipBySlotLabel["3"] = Cast3Box.Text?.Trim() ?? string.Empty;
        def.CastClipBySlotLabel["4"] = Cast4Box.Text?.Trim() ?? string.Empty;

        File.WriteAllText(currentClipmapPath, JsonSerializer.Serialize(currentClipmap, prettyJson));
        Log($"Saved mapping: {currentClipmapPath}");
    }

    private void OnAutoRemapFrameClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (currentAtlas is null || currentBitmap is null)
        {
            return;
        }

        var idx = (int)(FrameIndexBox.Value ?? 0);
        if (idx < 0 || idx >= currentAtlas.Frames.Count)
        {
            return;
        }

        var padding = ReadAutoPadding();
        var alphaThreshold = ReadAutoAlphaThreshold();
        var safetyPad = ReadAutoSafetyPad();
        var borderExpand = ReadAutoBorderExpand();
        if (TryAutoRemapFrame(currentAtlas, idx, padding, alphaThreshold, safetyPad, borderExpand))
        {
            SetFrameToUi(idx);
            DrawOverlay();
            Log($"Auto-remapped frame {idx} (padding={padding}, alpha>={alphaThreshold}, safetyPad={safetyPad}, borderExpand={borderExpand}).");
        }
        else
        {
            Log($"Auto-remap found no pixels above alpha threshold for frame {idx}.");
        }
    }

    private void OnAutoRemapClipClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (currentAtlas is null || currentBitmap is null)
        {
            return;
        }

        var result = AutoRemapClipInternal();

        SetFrameToUi((int)(FrameIndexBox.Value ?? 0));
        DrawOverlay();
        Log($"Auto-remapped clip frames: {result.changed}/{currentAtlas.Frames.Count} (padding={result.padding}, alpha>={result.alphaThreshold}).");
        if (result.recovered.Count > 0)
        {
            var preview = string.Join(", ", result.recovered.Take(20));
            var suffix = result.recovered.Count > 20 ? ", ..." : string.Empty;
            Log($"Recovered with catch-all fallback: {preview}{suffix}");
        }
        if (result.missed.Count > 0)
        {
            var preview = string.Join(", ", result.missed.Take(24));
            var suffix = result.missed.Count > 24 ? ", ..." : string.Empty;
            Log($"Frames with no remap match: {preview}{suffix}");
        }
    }

    private void OnAutoPivotFrameClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (currentAtlas is null || currentBitmap is null)
        {
            return;
        }

        var idx = (int)(FrameIndexBox.Value ?? 0);
        if (idx < 0 || idx >= currentAtlas.Frames.Count)
        {
            return;
        }

        var alphaThreshold = ReadAutoAlphaThreshold();
        var footBand = ReadPivotFootBand();
        if (TryComputePivot(currentAtlas, idx, alphaThreshold, footBand, out var pivotX, out var pivotY))
        {
            var frame = currentAtlas.Frames[idx];
            frame.PivotX = pivotX;
            frame.PivotY = pivotY;
            SetFrameToUi(idx);
            DrawOverlay();
            Log($"Auto-pivoted frame {idx}: ({pivotX},{pivotY})");
            return;
        }

        Log($"Auto-pivot could not find alpha pixels for frame {idx}.");
    }

    private void OnAutoPivotClipClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (currentAtlas is null || currentBitmap is null)
        {
            return;
        }

        var result = AutoPivotClipInternal();

        SetFrameToUi((int)(FrameIndexBox.Value ?? 0));
        DrawOverlay();
        Log($"Auto-pivot clip applied: {result.updated}/{currentAtlas.Frames.Count} (mode={ReadPivotMode()}, lockX={result.lockX}, perDirX={result.perDirectionLockX})");
        if (result.medianByDirection is not null && result.medianByDirection.Count > 0)
        {
            var parts = result.medianByDirection
                .OrderBy(k => k.Key)
                .Select(k => $"{DirectionLabel(k.Key)}={k.Value}");
            Log($"Per-direction X medians: {string.Join(", ", parts)}");
        }

        if (result.missed.Count > 0)
        {
            var preview = string.Join(", ", result.missed.Take(24));
            var suffix = result.missed.Count > 24 ? ", ..." : string.Empty;
            Log($"Frames with no pivot match: {preview}{suffix}");
        }
    }

    private void OnPropagateReferencePivotClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (currentAtlas is null)
        {
            return;
        }

        var refDir = ReadReferenceDirectionIndex();
        var propagateY = PivotPropagateYFromRefBox.IsChecked ?? true;
        var result = PropagatePivotFromReference(currentAtlas, refDir, propagateY);
        SetFrameToUi((int)(FrameIndexBox.Value ?? 0));
        DrawOverlay();
        Log($"Reference propagation ({DirectionLabel(refDir)}) applied to {result.applied}/{currentAtlas.Frames.Count} frames.");
        if (result.missingReferenceFrames > 0)
        {
            Log($"Reference direction missing frame matches: {result.missingReferenceFrames}");
        }
    }

    private void OnNormalizeClipClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (currentAtlas is null || currentBitmap is null)
        {
            return;
        }

        var remap = AutoRemapClipInternal();
        var pivot = AutoPivotClipInternal();
        var refDir = ReadReferenceDirectionIndex();
        var propagateY = PivotPropagateYFromRefBox.IsChecked ?? true;
        var propagate = PropagatePivotFromReference(currentAtlas, refDir, propagateY);

        SetFrameToUi((int)(FrameIndexBox.Value ?? 0));
        DrawOverlay();
        Log($"Normalize clip complete: remap={remap.changed}/{currentAtlas.Frames.Count}, pivot={pivot.updated}/{currentAtlas.Frames.Count}, propagate={propagate.applied}/{currentAtlas.Frames.Count}.");
        if (remap.missed.Count > 0)
        {
            var preview = string.Join(", ", remap.missed.Take(20));
            var suffix = remap.missed.Count > 20 ? ", ..." : string.Empty;
            Log($"Remap misses: {preview}{suffix}");
        }

        if (pivot.missed.Count > 0)
        {
            var preview = string.Join(", ", pivot.missed.Take(20));
            var suffix = pivot.missed.Count > 20 ? ", ..." : string.Empty;
            Log($"Pivot misses: {preview}{suffix}");
        }

        if (propagate.missingReferenceFrames > 0)
        {
            Log($"Reference misses ({DirectionLabel(refDir)}): {propagate.missingReferenceFrames}");
        }
    }

    private void OnNormalizeClassClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(currentClassId))
        {
            Log("Select a class in the Assets tree before running class normalization.");
            return;
        }

        var inputDir = NormalizeInputDir();
        var files = Directory.GetFiles(inputDir, "*.json", SearchOption.AllDirectories)
            .Where(IsAtlasJson)
            .Where(f => string.Equals(ResolveClassId(inputDir, f), currentClassId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            Log($"No clips found for class '{currentClassId}'.");
            return;
        }

        var padding = ReadAutoPadding();
        var alphaThreshold = ReadAutoAlphaThreshold();
        var useCatchAll = CatchAllRemapBox.IsChecked ?? true;
        var footBand = ReadPivotFootBand();
        var lockX = PivotLockXBox.IsChecked ?? true;
        var perDirectionLockX = PivotLockXPerDirectionBox.IsChecked ?? true;
        var lockXFromReference = PivotLockXFromReferenceBox.IsChecked ?? true;
        var temporalSmoothY = PivotTemporalSmoothYBox.IsChecked ?? true;
        var temporalSmoothX = PivotTemporalSmoothXBox.IsChecked ?? false;
        var refDir = ReadReferenceDirectionIndex();
        var propagateY = PivotPropagateYFromRefBox.IsChecked ?? true;

        var totalFrames = 0;
        var remapChanged = 0;
        var remapMissed = 0;
        var pivotUpdated = 0;
        var pivotMissed = 0;
        var propagateApplied = 0;
        var propagateMissing = 0;

        foreach (var path in files)
        {
            var doc = AtlasDocument.Load(path);
            totalFrames += doc.Frames.Count;

            var safetyPad = ReadAutoSafetyPad();
            var borderExpand = ReadAutoBorderExpand();
            var remap = AutoRemapClip(doc, padding, alphaThreshold, useCatchAll, safetyPad, borderExpand);
            var pivot = AutoPivotClip(doc, alphaThreshold, footBand, lockX, perDirectionLockX, refDir, lockXFromReference, temporalSmoothX, temporalSmoothY);
            var propagate = PropagatePivotFromReference(doc, refDir, propagateY);

            remapChanged += remap.changed;
            remapMissed += remap.missed.Count;
            pivotUpdated += pivot.updated;
            pivotMissed += pivot.missed.Count;
            propagateApplied += propagate.applied;
            propagateMissing += propagate.missingReferenceFrames;

            File.WriteAllText(path, doc.Root.ToJsonString(prettyJson));
        }

        if (!string.IsNullOrWhiteSpace(currentAtlasPath) && File.Exists(currentAtlasPath))
        {
            LoadClip(currentAtlasPath, currentClassId, currentEntityType);
        }

        Log($"Normalized class '{currentClassId}': clips={files.Length}, frames={totalFrames}, remap={remapChanged}, pivot={pivotUpdated}, propagate={propagateApplied}.");
        if (remapMissed > 0 || pivotMissed > 0 || propagateMissing > 0)
        {
            Log($"Remaining misses: remap={remapMissed}, pivot={pivotMissed}, reference={propagateMissing}");
        }
    }

    private async void OnCopyFullIdClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(currentFullClipId))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(currentFullClipId);
            Log($"Copied full ID: {currentFullClipId}");
        }
    }

    private async void OnCopyConciseIdClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var concise = (ConciseClipIdBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(concise))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(concise);
            Log($"Copied concise ID: {concise}");
        }
    }

    private void OnApplyConciseIdsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(currentClassId))
        {
            Log("Select a class first.");
            return;
        }

        var inputDir = NormalizeInputDir();
        var classClipFiles = Directory.GetFiles(inputDir, "*.json", SearchOption.AllDirectories)
            .Where(IsAtlasJson)
            .Where(f => string.Equals(ResolveClassId(inputDir, f), currentClassId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var oldToNew = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in classClipFiles)
        {
            var doc = AtlasDocument.Load(path);
            var oldId = doc.GetResolvedClipId();
            var newId = BuildConciseId(currentClassId, path, oldToNew.Values);
            oldToNew[oldId] = newId;
            doc.ClipId = newId;
            File.WriteAllText(path, doc.Root.ToJsonString(prettyJson));
        }

        var clipmapPath = Path.Combine(inputDir, "clipmaps", $"{currentClassId}.json");
        if (File.Exists(clipmapPath))
        {
            var json = File.ReadAllText(clipmapPath);
            foreach (var pair in oldToNew)
            {
                json = json.Replace($"\"{pair.Key}\"", $"\"{pair.Value}\"", StringComparison.Ordinal);
            }

            File.WriteAllText(clipmapPath, json);
        }

        Log($"Applied concise IDs for class '{currentClassId}': {oldToNew.Count} clips updated.");
        BuildTree(inputDir);
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (AssetTree.SelectedItem is not AssetNode node)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(node.ClassId))
        {
            currentClassId = node.ClassId;
            currentEntityType = node.EntityType;
            LoadClipmap(node.ClassId);
        }

        if (!string.IsNullOrWhiteSpace(node.ClipPath))
        {
            LoadClip(node.ClipPath, node.ClassId, node.EntityType);
        }
    }

    private void OnFrameIndexChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (suppressFrameEvents || currentAtlas is null)
        {
            return;
        }

        var idx = (int)(FrameIndexBox.Value ?? 0);
        idx = Math.Clamp(idx, 0, Math.Max(0, currentAtlas.Frames.Count - 1));
        SetFrameToUi(idx);
        DrawOverlay();
    }

    private void OnFrameFieldChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (suppressFrameEvents || currentAtlas is null)
        {
            return;
        }

        var idx = (int)(FrameIndexBox.Value ?? 0);
        if (idx < 0 || idx >= currentAtlas.Frames.Count)
        {
            return;
        }

        var f = currentAtlas.Frames[idx];
        f.Direction = (int)(DirectionBox.Value ?? 0);
        f.Frame = (int)(FrameNumberBox.Value ?? 0);
        f.Time = (float)(TimeBox.Value ?? 0);
        f.X = (int)(XBox.Value ?? 0);
        f.Y = (int)(YBox.Value ?? 0);
        f.W = (int)(WBox.Value ?? 1);
        f.H = (int)(HBox.Value ?? 1);
        f.PivotX = (int)(PivotXBox.Value ?? 0);
        f.PivotY = (int)(PivotYBox.Value ?? 0);

        DrawOverlay();
    }

    private string NormalizeInputDir()
    {
        var text = (InputDirBox.Text ?? string.Empty).Trim();
        var normalized = string.IsNullOrWhiteSpace(text) ? Path.Combine(repoRoot, "content", "animations") : text;
        normalized = Path.GetFullPath(normalized);
        InputDirBox.Text = normalized;
        return normalized;
    }

    private void BuildTree(string inputDir)
    {
        if (!Directory.Exists(inputDir))
        {
            Log($"Input directory not found: {inputDir}");
            return;
        }

        var files = Directory.GetFiles(inputDir, "*.json", SearchOption.AllDirectories)
            .Where(IsAtlasJson)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var grouped = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            var entityType = ResolveEntityType(inputDir, f);
            var classId = ResolveClassId(inputDir, f);

            if (!grouped.TryGetValue(entityType, out var byClass))
            {
                byClass = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                grouped[entityType] = byClass;
            }

            if (!byClass.TryGetValue(classId, out var list))
            {
                list = new List<string>();
                byClass[classId] = list;
            }

            list.Add(f);
        }

        var roots = new List<AssetNode>();
        foreach (var entity in grouped.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var entityNode = new AssetNode { Name = entity.Key, EntityType = entity.Key };
            foreach (var cls in entity.Value.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                var classNode = new AssetNode { Name = cls.Key, EntityType = entity.Key, ClassId = cls.Key };
                foreach (var clipPath in cls.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    classNode.Children.Add(new AssetNode
                    {
                        Name = Path.GetFileNameWithoutExtension(clipPath),
                        EntityType = entity.Key,
                        ClassId = cls.Key,
                        ClipPath = clipPath
                    });
                }

                entityNode.Children.Add(classNode);
            }

            roots.Add(entityNode);
        }

        allTreeRoots = roots;
        ApplyTreeFilter((ClipFilterBox.Text ?? string.Empty).Trim());
        Log($"Loaded {files.Length} clip json files from {inputDir}");
    }

    private void ApplyTreeFilter(string filter)
    {
        if (allTreeRoots.Count == 0)
        {
            AssetTree.ItemsSource = Array.Empty<AssetNode>();
            return;
        }

        if (string.IsNullOrWhiteSpace(filter))
        {
            AssetTree.ItemsSource = allTreeRoots;
            return;
        }

        var needle = filter.Trim();
        var filtered = new List<AssetNode>();
        foreach (var entity in allTreeRoots)
        {
            var entityCopy = new AssetNode
            {
                Name = entity.Name,
                EntityType = entity.EntityType,
                ClassId = entity.ClassId,
                ClipPath = entity.ClipPath
            };

            foreach (var cls in entity.Children)
            {
                var classCopy = new AssetNode
                {
                    Name = cls.Name,
                    EntityType = cls.EntityType,
                    ClassId = cls.ClassId,
                    ClipPath = cls.ClipPath
                };

                var classMatch = ContainsText(cls.Name, needle) ||
                                 ContainsText(cls.ClassId, needle) ||
                                 ContainsText(cls.EntityType, needle);
                foreach (var clip in cls.Children)
                {
                    if (classMatch ||
                        ContainsText(clip.Name, needle) ||
                        ContainsText(clip.ClassId, needle) ||
                        ContainsText(clip.ClipPath, needle))
                    {
                        classCopy.Children.Add(new AssetNode
                        {
                            Name = clip.Name,
                            EntityType = clip.EntityType,
                            ClassId = clip.ClassId,
                            ClipPath = clip.ClipPath
                        });
                    }
                }

                if (classCopy.Children.Count > 0 || classMatch)
                {
                    entityCopy.Children.Add(classCopy);
                }
            }

            if (entityCopy.Children.Count > 0 || ContainsText(entity.Name, needle))
            {
                filtered.Add(entityCopy);
            }
        }

        AssetTree.ItemsSource = filtered;
    }

    private static bool ContainsText(string? source, string needle)
    {
        return !string.IsNullOrWhiteSpace(source) &&
               source.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAtlasJson(string path)
    {
        if (path.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (path.Contains($"{Path.DirectorySeparatorChar}clipmaps{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var file = Path.GetFileName(path);
        return file.Contains("_atlas", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveEntityType(string inputDir, string file)
    {
        var parts = Path.GetRelativePath(inputDir, file)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToArray();

        for (var i = 0; i < parts.Length; i++)
        {
            if (EntityMarkers.Contains(parts[i], StringComparer.OrdinalIgnoreCase))
            {
                return parts[i].ToLowerInvariant();
            }
        }

        return "classes";
    }

    private static string ResolveClassId(string inputDir, string file)
    {
        var parts = Path.GetRelativePath(inputDir, file)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToArray();

        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (EntityMarkers.Contains(parts[i], StringComparer.OrdinalIgnoreCase) && i + 1 < parts.Length)
            {
                return parts[i + 1].ToLowerInvariant();
            }
        }

        return parts.Length > 0 ? parts[0].ToLowerInvariant() : "unknown";
    }

    private void LoadClip(string clipPath, string classId, string entityType)
    {
        try
        {
            currentAtlasPath = clipPath;
            currentClassId = classId;
            currentEntityType = entityType;
            currentAtlas = AtlasDocument.Load(clipPath);
            currentFullClipId = currentAtlas.GetResolvedClipId();
            currentConciseClipId = BuildConciseId(classId, clipPath, Array.Empty<string>());
            FullClipIdBox.Text = currentFullClipId;
            ConciseClipIdBox.Text = currentConciseClipId;

            ClipPathText.Text = clipPath;
            ClassIdText.Text = $"Class: {currentClassId} ({currentEntityType})";

            currentBitmap?.Dispose();
            var atlasPath = currentAtlas.ResolveAtlasPath();
            currentBitmap = File.Exists(atlasPath) ? new Bitmap(atlasPath) : null;
            AtlasImage.Source = currentBitmap;

            if (currentBitmap is not null)
            {
                AtlasCanvas.Width = currentBitmap.PixelSize.Width;
                AtlasCanvas.Height = currentBitmap.PixelSize.Height;
            }
            else
            {
                AtlasCanvas.Width = 10;
                AtlasCanvas.Height = 10;
                Log($"Atlas image missing for clip: {atlasPath}");
            }

            suppressFrameEvents = true;
            FrameIndexBox.Maximum = Math.Max(0, currentAtlas.Frames.Count - 1);
            FrameIndexBox.Value = 0;
            suppressFrameEvents = false;

            SetFrameToUi(0);
            DrawOverlay();
            LoadClipmap(classId);
            Log($"Loaded clip: {Path.GetFileName(clipPath)} ({currentAtlas.Frames.Count} frames)");
        }
        catch (Exception ex)
        {
            Log($"Failed to load clip: {ex.Message}");
        }
    }

    private static bool TryAutoRemapFrame(AtlasDocument atlas, int frameIndex, int expandPixels, int alphaThreshold, int safetyPad, int maxBorderExpandSteps)
    {
        var atlasPath = atlas.ResolveAtlasPath();
        if (!File.Exists(atlasPath))
        {
            return false;
        }

        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(atlasPath);
        var frame = atlas.Frames[frameIndex];
        var threshold = Math.Clamp(alphaThreshold, 0, 255);
        var maxDim = Math.Max(image.Width, image.Height);

        var radii = BuildAdaptiveSearchRadii(expandPixels, maxDim);
        for (var ri = 0; ri < radii.Count; ri++)
        {
            var radius = radii[ri];
            if (!TryFindBoundsInSearchWindow(image, frame, radius, threshold, out var minX, out var minY, out var maxX, out var maxY))
            {
                continue;
            }

            // Use a softer edge threshold to include anti-aliased perimeter pixels that would otherwise clip.
            var softEdgeThreshold = Math.Min(Math.Max(1, threshold), 8);
            ExpandBoundsWhileBorderHasOpaque(image, softEdgeThreshold, ref minX, ref minY, ref maxX, ref maxY, Math.Clamp(maxBorderExpandSteps, 0, 64));
            ExpandBoundsIntoSoftHalo(image, softEdgeThreshold, ref minX, ref minY, ref maxX, ref maxY, Math.Clamp(maxBorderExpandSteps, 0, 64));
            ApplySafetyPad(image, Math.Clamp(safetyPad, 0, 32), ref minX, ref minY, ref maxX, ref maxY);

            var newW = Math.Max(1, maxX - minX + 1);
            var newH = Math.Max(1, maxY - minY + 1);
            var deltaX = frame.X - minX;
            var deltaY = frame.Y - minY;

            frame.X = minX;
            frame.Y = minY;
            frame.W = newW;
            frame.H = newH;
            frame.PivotX = Math.Clamp(frame.PivotX + deltaX, 0, newW);
            frame.PivotY = Math.Clamp(frame.PivotY + deltaY, 0, newH);
            return true;
        }

        return false;
    }

    private static bool TryFindBoundsInSearchWindow(
        Image<Rgba32> image,
        AtlasFrame frame,
        int radius,
        int alphaThreshold,
        out int minX,
        out int minY,
        out int maxX,
        out int maxY)
    {
        minX = int.MaxValue;
        minY = int.MaxValue;
        maxX = int.MinValue;
        maxY = int.MinValue;

        var width = image.Width;
        var height = image.Height;
        var threshold = Math.Clamp(alphaThreshold, 0, 255);
        var cx = frame.X + (frame.W / 2);
        var cy = frame.Y + (frame.H / 2);
        var x0 = Math.Max(0, cx - radius);
        var x1 = Math.Min(width - 1, cx + radius);
        var y0 = Math.Max(0, cy - radius);
        var y1 = Math.Min(height - 1, cy + radius);

        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                if (image[x, y].A < threshold)
                {
                    continue;
                }

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        return minX != int.MaxValue && minY != int.MaxValue && maxX != int.MinValue && maxY != int.MinValue;
    }

    private static bool TryAutoRemapFrameWithFallbacks(AtlasDocument atlas, int frameIndex, int padding, int alphaThreshold, int safetyPad, int maxBorderExpandSteps, out int usedThreshold, out int usedPadding)
    {
        usedThreshold = alphaThreshold;
        usedPadding = padding;

        var thresholds = BuildFallbackThresholds(alphaThreshold);
        var paddings = BuildFallbackPaddings(padding);

        for (var pi = 0; pi < paddings.Count; pi++)
        {
            for (var ti = 0; ti < thresholds.Count; ti++)
            {
                var p = paddings[pi];
                var t = thresholds[ti];
                if (p == padding && t == alphaThreshold)
                {
                    continue;
                }

                if (TryAutoRemapFrame(atlas, frameIndex, p, t, safetyPad, maxBorderExpandSteps))
                {
                    usedThreshold = t;
                    usedPadding = p;
                    return true;
                }
            }
        }

        return false;
    }

    private static List<int> BuildAdaptiveSearchRadii(int start, int maxDim)
    {
        var radii = new List<int>();
        var cursor = Math.Max(0, start);
        if (cursor == 0)
        {
            cursor = 2;
        }

        while (cursor < maxDim && radii.Count < 10)
        {
            radii.Add(cursor);
            cursor *= 2;
        }

        if (radii.Count == 0 || radii[^1] != maxDim)
        {
            radii.Add(maxDim);
        }

        return radii;
    }

    private static List<int> BuildFallbackThresholds(int startAlpha)
    {
        var start = Math.Clamp(startAlpha, 1, 255);
        var thresholds = new List<int> { start };

        var cursor = start;
        while (cursor > 1)
        {
            cursor = Math.Max(1, cursor / 2);
            if (!thresholds.Contains(cursor))
            {
                thresholds.Add(cursor);
            }
        }

        if (!thresholds.Contains(1))
        {
            thresholds.Add(1);
        }

        return thresholds;
    }

    private static List<int> BuildFallbackPaddings(int startPadding)
    {
        var start = Math.Clamp(startPadding, 0, 64);
        var paddings = new List<int> { start };
        var candidates = new[] { Math.Max(2, start + 2), Math.Max(6, start + 6), Math.Max(12, start + 12), 24, 40, 64 };
        for (var i = 0; i < candidates.Length; i++)
        {
            var c = Math.Clamp(candidates[i], 0, 64);
            if (!paddings.Contains(c))
            {
                paddings.Add(c);
            }
        }

        return paddings;
    }

    private static bool TryFindNearestSeed(Image<Rgba32> image, AtlasFrame frame, int radius, int alphaThreshold, out int seedX, out int seedY)
    {
        var cx = frame.X + Math.Max(1, frame.W) / 2;
        var cy = frame.Y + Math.Max(1, frame.H) / 2;
        var x0 = Math.Clamp(cx - radius, 0, image.Width - 1);
        var y0 = Math.Clamp(cy - radius, 0, image.Height - 1);
        var x1 = Math.Clamp(cx + radius, 0, image.Width - 1);
        var y1 = Math.Clamp(cy + radius, 0, image.Height - 1);

        var bestDist = long.MaxValue;
        seedX = -1;
        seedY = -1;

        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                if (image[x, y].A < alphaThreshold)
                {
                    continue;
                }

                var dx = x - cx;
                var dy = y - cy;
                var dist = (long)dx * dx + (long)dy * dy;
                if (dist >= bestDist)
                {
                    continue;
                }

                bestDist = dist;
                seedX = x;
                seedY = y;
            }
        }

        return seedX >= 0 && seedY >= 0;
    }

    private static bool TryFloodBounds(Image<Rgba32> image, int seedX, int seedY, int alphaThreshold, out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = int.MaxValue;
        minY = int.MaxValue;
        maxX = int.MinValue;
        maxY = int.MinValue;

        var width = image.Width;
        var height = image.Height;
        if (seedX < 0 || seedY < 0 || seedX >= width || seedY >= height)
        {
            return false;
        }

        if (image[seedX, seedY].A < alphaThreshold)
        {
            return false;
        }

        var visited = new bool[width * height];
        var stack = new Stack<(int x, int y)>();
        stack.Push((seedX, seedY));

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            var idx = y * width + x;
            if (visited[idx])
            {
                continue;
            }

            visited[idx] = true;
            if (image[x, y].A < alphaThreshold)
            {
                continue;
            }

            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;

            if (x > 0) stack.Push((x - 1, y));
            if (x < width - 1) stack.Push((x + 1, y));
            if (y > 0) stack.Push((x, y - 1));
            if (y < height - 1) stack.Push((x, y + 1));
            if (x > 0 && y > 0) stack.Push((x - 1, y - 1));
            if (x < width - 1 && y > 0) stack.Push((x + 1, y - 1));
            if (x > 0 && y < height - 1) stack.Push((x - 1, y + 1));
            if (x < width - 1 && y < height - 1) stack.Push((x + 1, y + 1));
        }

        return minX != int.MaxValue && minY != int.MaxValue && maxX != int.MinValue && maxY != int.MinValue;
    }

    private int ReadAutoPadding()
    {
        var raw = AutoPaddingBox.Value ?? 4;
        var value = (int)raw;
        return Math.Clamp(value, 0, 64);
    }

    private int ReadAutoSafetyPad()
    {
        var raw = AutoSafetyPadBox.Value ?? 2;
        var value = (int)raw;
        return Math.Clamp(value, 0, 16);
    }

    private int ReadAutoBorderExpand()
    {
        var raw = AutoBorderExpandBox.Value ?? 12;
        var value = (int)raw;
        return Math.Clamp(value, 0, 32);
    }

    private (int changed, List<int> missed, List<string> recovered, int padding, int alphaThreshold) AutoRemapClipInternal()
    {
        if (currentAtlas is null)
        {
            return (0, new List<int>(), new List<string>(), 0, 0);
        }

        var padding = ReadAutoPadding();
        var alphaThreshold = ReadAutoAlphaThreshold();
        var useCatchAll = CatchAllRemapBox.IsChecked ?? true;
        var safetyPad = ReadAutoSafetyPad();
        var borderExpand = ReadAutoBorderExpand();

        var result = AutoRemapClip(currentAtlas, padding, alphaThreshold, useCatchAll, safetyPad, borderExpand);
        return (result.changed, result.missed, result.recovered, padding, alphaThreshold);
    }

    private (int changed, List<int> missed, List<string> recovered) AutoRemapClip(
        AtlasDocument atlas,
        int padding,
        int alphaThreshold,
        bool useCatchAll,
        int safetyPad,
        int borderExpand)
    {
        var changed = 0;
        var missed = new List<int>();
        var recovered = new List<string>();

        for (var i = 0; i < atlas.Frames.Count; i++)
        {
            if (TryAutoRemapFrame(atlas, i, padding, alphaThreshold, safetyPad, borderExpand))
            {
                changed++;
            }
            else if (useCatchAll && TryAutoRemapFrameWithFallbacks(atlas, i, padding, alphaThreshold, safetyPad, borderExpand, out var usedThreshold, out var usedPadding))
            {
                changed++;
                recovered.Add($"{i}(a>={usedThreshold},pad={usedPadding})");
            }
            else
            {
                missed.Add(i);
            }
        }

        return (changed, missed, recovered);
    }

    private (int updated, List<int> missed, Dictionary<int, int>? medianByDirection, bool lockX, bool perDirectionLockX) AutoPivotClipInternal()
    {
        if (currentAtlas is null)
        {
            return (0, new List<int>(), null, false, false);
        }

        var alphaThreshold = ReadAutoAlphaThreshold();
        var footBand = ReadPivotFootBand();
        var lockX = PivotLockXBox.IsChecked ?? true;
        var perDirectionLockX = PivotLockXPerDirectionBox.IsChecked ?? true;
        var referenceDirection = ReadReferenceDirectionIndex();
        var lockXFromReference = PivotLockXFromReferenceBox.IsChecked ?? true;
        var temporalSmoothX = PivotTemporalSmoothXBox.IsChecked ?? false;
        var temporalSmoothY = PivotTemporalSmoothYBox.IsChecked ?? true;
        var result = AutoPivotClip(
            currentAtlas,
            alphaThreshold,
            footBand,
            lockX,
            perDirectionLockX,
            referenceDirection,
            lockXFromReference,
            temporalSmoothX,
            temporalSmoothY);
        return (result.updated, result.missed, result.medianByDirection, lockX, perDirectionLockX);
    }

    private (int updated, List<int> missed, Dictionary<int, int>? medianByDirection) AutoPivotClip(
        AtlasDocument atlas,
        int alphaThreshold,
        int footBand,
        bool lockX,
        bool perDirectionLockX,
        int referenceDirection,
        bool lockXFromReference,
        bool temporalSmoothX,
        bool temporalSmoothY)
    {
        var updated = 0;
        var missed = new List<int>();
        var xSamples = new List<int>();
        var computed = new Dictionary<int, (int x, int y)>();
        var directionXSamples = new Dictionary<int, List<int>>();
        var directionByFrame = new Dictionary<int, int>();
        var yToDirection = BuildDirectionLookupByY(atlas);

        for (var i = 0; i < atlas.Frames.Count; i++)
        {
            if (TryComputePivot(atlas, i, alphaThreshold, footBand, out var pivotX, out var pivotY))
            {
                computed[i] = (pivotX, pivotY);
                xSamples.Add(pivotX);
                if (TryResolveDirectionIndex(atlas, i, yToDirection, out var directionIndex))
                {
                    directionByFrame[i] = directionIndex;
                    if (!directionXSamples.TryGetValue(directionIndex, out var bucket))
                    {
                        bucket = new List<int>();
                        directionXSamples[directionIndex] = bucket;
                    }

                    bucket.Add(pivotX);
                }
            }
            else
            {
                missed.Add(i);
            }
        }

        int? medianX = null;
        if (lockX && xSamples.Count > 0)
        {
            medianX = Median(xSamples);
        }

        Dictionary<int, int>? medianByDirection = null;
        if (lockX && perDirectionLockX && directionXSamples.Count > 0)
        {
            medianByDirection = new Dictionary<int, int>();
            foreach (var pair in directionXSamples)
            {
                if (pair.Value.Count == 0)
                {
                    continue;
                }

                medianByDirection[pair.Key] = Median(pair.Value);
            }
        }

        var referenceByFrame = BuildReferenceFrameMap(atlas, computed, directionByFrame, referenceDirection);

        foreach (var pair in computed)
        {
            var frame = atlas.Frames[pair.Key];
            var targetX = medianX ?? pair.Value.x;
            if (medianByDirection is not null &&
                directionByFrame.TryGetValue(pair.Key, out var dirIdx) &&
                medianByDirection.TryGetValue(dirIdx, out var dirMedian))
            {
                targetX = dirMedian;
            }

            if (lockXFromReference &&
                directionByFrame.TryGetValue(pair.Key, out var frameDir) &&
                frameDir != referenceDirection &&
                referenceByFrame.TryGetValue(frame.Frame, out var refFrame))
            {
                var refRatio = refFrame.W > 1 ? (float)refFrame.PivotX / (refFrame.W - 1) : 0.5f;
                targetX = (int)MathF.Round(refRatio * Math.Max(1, frame.W - 1));
            }

            frame.PivotX = Math.Clamp(targetX, 0, Math.Max(0, frame.W - 1));
            frame.PivotY = Math.Clamp(pair.Value.y, 0, Math.Max(0, frame.H - 1));
            updated++;
        }

        if (temporalSmoothX || temporalSmoothY)
        {
            ApplyTemporalPivotSmoothing(atlas, temporalSmoothX, temporalSmoothY);
        }

        return (updated, missed, medianByDirection);
    }

    private static Dictionary<int, AtlasFrame> BuildReferenceFrameMap(
        AtlasDocument atlas,
        Dictionary<int, (int x, int y)> computed,
        Dictionary<int, int> directionByFrame,
        int referenceDirection)
    {
        var map = new Dictionary<int, AtlasFrame>();
        foreach (var pair in computed)
        {
            if (!directionByFrame.TryGetValue(pair.Key, out var dir) || dir != referenceDirection)
            {
                continue;
            }

            var frame = atlas.Frames[pair.Key];
            if (!map.ContainsKey(frame.Frame))
            {
                map[frame.Frame] = frame;
            }
        }

        return map;
    }

    private static void ApplyTemporalPivotSmoothing(AtlasDocument atlas, bool smoothX, bool smoothY)
    {
        var groups = atlas.Frames
            .Select((frame, index) => new { frame, index })
            .GroupBy(x => Math.Clamp(x.frame.Direction, 0, 7));

        foreach (var group in groups)
        {
            var ordered = group.OrderBy(x => x.frame.Frame).ThenBy(x => x.index).ToList();
            if (ordered.Count < 3)
            {
                continue;
            }

            var smoothedX = new int[ordered.Count];
            var smoothedY = new int[ordered.Count];
            for (var i = 0; i < ordered.Count; i++)
            {
                smoothedX[i] = ordered[i].frame.PivotX;
                smoothedY[i] = ordered[i].frame.PivotY;
            }

            for (var i = 0; i < ordered.Count; i++)
            {
                var a = ordered[Math.Max(0, i - 1)].frame;
                var b = ordered[i].frame;
                var c = ordered[Math.Min(ordered.Count - 1, i + 1)].frame;
                if (smoothX)
                {
                    smoothedX[i] = Median(new List<int> { a.PivotX, b.PivotX, c.PivotX });
                }

                if (smoothY)
                {
                    smoothedY[i] = Median(new List<int> { a.PivotY, b.PivotY, c.PivotY });
                }
            }

            for (var i = 0; i < ordered.Count; i++)
            {
                var frame = ordered[i].frame;
                if (smoothX)
                {
                    frame.PivotX = Math.Clamp(smoothedX[i], 0, Math.Max(0, frame.W - 1));
                }

                if (smoothY)
                {
                    frame.PivotY = Math.Clamp(smoothedY[i], 0, Math.Max(0, frame.H - 1));
                }
            }
        }
    }

    private static void ExpandBoundsWhileBorderHasOpaque(
        Image<Rgba32> image,
        int alphaThreshold,
        ref int minX,
        ref int minY,
        ref int maxX,
        ref int maxY,
        int maxSteps)
    {
        for (var step = 0; step < maxSteps; step++)
        {
            if (!BorderHasOpaque(image, alphaThreshold, minX, minY, maxX, maxY))
            {
                return;
            }

            if (minX > 0) minX--;
            if (minY > 0) minY--;
            if (maxX < image.Width - 1) maxX++;
            if (maxY < image.Height - 1) maxY++;
        }
    }

    private static void ExpandBoundsIntoSoftHalo(
        Image<Rgba32> image,
        int alphaThreshold,
        ref int minX,
        ref int minY,
        ref int maxX,
        ref int maxY,
        int maxSteps)
    {
        for (var step = 0; step < maxSteps; step++)
        {
            var expanded = false;
            if (minX > 0 && AnyOpaqueInVertical(image, minX - 1, minY, maxY, alphaThreshold))
            {
                minX--;
                expanded = true;
            }

            if (maxX < image.Width - 1 && AnyOpaqueInVertical(image, maxX + 1, minY, maxY, alphaThreshold))
            {
                maxX++;
                expanded = true;
            }

            if (minY > 0 && AnyOpaqueInHorizontal(image, minY - 1, minX, maxX, alphaThreshold))
            {
                minY--;
                expanded = true;
            }

            if (maxY < image.Height - 1 && AnyOpaqueInHorizontal(image, maxY + 1, minX, maxX, alphaThreshold))
            {
                maxY++;
                expanded = true;
            }

            if (!expanded)
            {
                return;
            }
        }
    }

    private static bool AnyOpaqueInVertical(Image<Rgba32> image, int x, int minY, int maxY, int alphaThreshold)
    {
        for (var y = minY; y <= maxY; y++)
        {
            if (image[x, y].A >= alphaThreshold)
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnyOpaqueInHorizontal(Image<Rgba32> image, int y, int minX, int maxX, int alphaThreshold)
    {
        for (var x = minX; x <= maxX; x++)
        {
            if (image[x, y].A >= alphaThreshold)
            {
                return true;
            }
        }

        return false;
    }

    private static bool BorderHasOpaque(Image<Rgba32> image, int alphaThreshold, int minX, int minY, int maxX, int maxY)
    {
        for (var x = minX; x <= maxX; x++)
        {
            if (image[x, minY].A >= alphaThreshold || image[x, maxY].A >= alphaThreshold)
            {
                return true;
            }
        }

        for (var y = minY; y <= maxY; y++)
        {
            if (image[minX, y].A >= alphaThreshold || image[maxX, y].A >= alphaThreshold)
            {
                return true;
            }
        }

        return false;
    }

    private static void ApplySafetyPad(Image<Rgba32> image, int safetyPad, ref int minX, ref int minY, ref int maxX, ref int maxY)
    {
        if (safetyPad <= 0)
        {
            return;
        }

        minX = Math.Max(0, minX - safetyPad);
        minY = Math.Max(0, minY - safetyPad);
        maxX = Math.Min(image.Width - 1, maxX + safetyPad);
        maxY = Math.Min(image.Height - 1, maxY + safetyPad);
    }

    private int ReadReferenceDirectionIndex()
    {
        var idx = PivotReferenceDirectionBox.SelectedIndex;
        if (idx < 0 || idx > 7)
        {
            return 2; // SE default
        }

        return idx;
    }

    private (int applied, int missingReferenceFrames) PropagatePivotFromReference(AtlasDocument atlas, int referenceDirection, bool propagateY)
    {
        var grouped = atlas.Frames
            .Select((frame, index) => new { frame, index })
            .GroupBy(x => Math.Clamp(x.frame.Direction, 0, 7))
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.frame.Frame).ThenBy(x => x.index).ToList());

        if (!grouped.TryGetValue(referenceDirection, out var referenceFrames) || referenceFrames.Count == 0)
        {
            return (0, atlas.Frames.Count);
        }

        var refByFrameNumber = new Dictionary<int, AtlasFrame>();
        for (var i = 0; i < referenceFrames.Count; i++)
        {
            var f = referenceFrames[i].frame;
            if (!refByFrameNumber.ContainsKey(f.Frame))
            {
                refByFrameNumber[f.Frame] = f;
            }
        }

        var applied = 0;
        var missingRef = 0;

        for (var i = 0; i < atlas.Frames.Count; i++)
        {
            var target = atlas.Frames[i];
            if (Math.Clamp(target.Direction, 0, 7) == referenceDirection)
            {
                continue;
            }

            if (!refByFrameNumber.TryGetValue(target.Frame, out var reference))
            {
                // fallback: modulo by frame sequence in reference row
                var listIndex = Math.Abs(target.Frame) % referenceFrames.Count;
                reference = referenceFrames[listIndex].frame;
                if (reference is null)
                {
                    missingRef++;
                    continue;
                }
            }

            if (propagateY)
            {
                var refFootOffset = Math.Max(0, reference.H - reference.PivotY);
                var targetPivotY = target.H - refFootOffset;
                target.PivotY = Math.Clamp(targetPivotY, 0, Math.Max(1, target.H));
            }

            applied++;
        }

        return (applied, missingRef);
    }

    private int ReadAutoAlphaThreshold()
    {
        var raw = AutoAlphaBox.Value ?? 96;
        var value = (int)raw;
        return Math.Clamp(value, 0, 255);
    }

    private int ReadPivotFootBand()
    {
        var raw = PivotFootBandBox.Value ?? 6;
        var value = (int)raw;
        return Math.Clamp(value, 1, 32);
    }

    private PivotMode ReadPivotMode()
    {
        var idx = PivotModeBox.SelectedIndex;
        return idx == 1 ? PivotMode.BottomCenter : PivotMode.FootContact;
    }

    private bool TryComputePivot(AtlasDocument atlas, int frameIndex, int alphaThreshold, int footBand, out int pivotX, out int pivotY)
    {
        pivotX = 0;
        pivotY = 0;
        var atlasPath = atlas.ResolveAtlasPath();
        if (!File.Exists(atlasPath))
        {
            return false;
        }

        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(atlasPath);
        var frame = atlas.Frames[frameIndex];
        var fx = Math.Clamp(frame.X, 0, image.Width - 1);
        var fy = Math.Clamp(frame.Y, 0, image.Height - 1);
        var fw = Math.Clamp(Math.Max(1, frame.W), 1, image.Width - fx);
        var fh = Math.Clamp(Math.Max(1, frame.H), 1, image.Height - fy);
        if (fw <= 0 || fh <= 0)
        {
            return false;
        }

        if (ReadPivotMode() == PivotMode.BottomCenter)
        {
            pivotX = fw / 2;
            pivotY = fh - 1;
            return true;
        }

        var threshold = Math.Clamp(alphaThreshold, 1, 255);
        var maxAlphaY = -1;
        for (var y = fy; y < fy + fh; y++)
        {
            for (var x = fx; x < fx + fw; x++)
            {
                if (image[x, y].A >= threshold)
                {
                    if (y > maxAlphaY)
                    {
                        maxAlphaY = y;
                    }
                }
            }
        }

        if (maxAlphaY < 0)
        {
            return false;
        }

        var bandTop = Math.Max(fy, maxAlphaY - Math.Max(1, footBand) + 1);
        var xSamples = new List<int>();
        for (var y = bandTop; y <= maxAlphaY; y++)
        {
            for (var x = fx; x < fx + fw; x++)
            {
                if (image[x, y].A >= threshold)
                {
                    xSamples.Add(x - fx);
                }
            }
        }

        if (xSamples.Count == 0)
        {
            for (var y = fy; y < fy + fh; y++)
            {
                for (var x = fx; x < fx + fw; x++)
                {
                    if (image[x, y].A >= threshold)
                    {
                        xSamples.Add(x - fx);
                    }
                }
            }
        }

        if (xSamples.Count == 0)
        {
            return false;
        }

        pivotX = Median(xSamples);
        pivotY = maxAlphaY - fy;
        pivotX = Math.Clamp(pivotX, 0, fw - 1);
        pivotY = Math.Clamp(pivotY, 0, fh - 1);
        return true;
    }

    private static int Median(List<int> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        values.Sort();
        var mid = values.Count / 2;
        if (values.Count % 2 == 1)
        {
            return values[mid];
        }

        return (values[mid - 1] + values[mid]) / 2;
    }

    private static Dictionary<int, int> BuildDirectionLookupByY(AtlasDocument atlas)
    {
        var yRows = atlas.Frames
            .Select(f => f.Y)
            .Distinct()
            .OrderByDescending(v => v) // bottom row first
            .Take(8)
            .ToArray();

        var map = new Dictionary<int, int>();
        for (var i = 0; i < yRows.Length; i++)
        {
            map[yRows[i]] = i;
        }

        return map;
    }

    private static bool TryResolveDirectionIndex(AtlasDocument atlas, int frameIndex, Dictionary<int, int> yToDirection, out int directionIndex)
    {
        directionIndex = -1;
        var f = atlas.Frames[frameIndex];
        if (f.Direction >= 0 && f.Direction < 8)
        {
            directionIndex = f.Direction;
            return true;
        }

        if (yToDirection.TryGetValue(f.Y, out var byY))
        {
            directionIndex = byY;
            return true;
        }

        return false;
    }

    private static string DirectionLabel(int directionIndex)
    {
        if (directionIndex < 0 || directionIndex >= DirectionLabels.Length)
        {
            return $"D{directionIndex}";
        }

        return DirectionLabels[directionIndex];
    }

    private enum PivotMode
    {
        FootContact = 0,
        BottomCenter = 1
    }

    private static string BuildConciseId(string classId, string clipPath, IEnumerable<string> reserved)
    {
        var stem = Path.GetFileNameWithoutExtension(clipPath);
        if (stem.EndsWith("_atlas", StringComparison.OrdinalIgnoreCase))
        {
            stem = stem[..^6];
        }

        var lowered = stem.ToLowerInvariant();
        lowered = lowered.Replace($"{classId}_", string.Empty, StringComparison.OrdinalIgnoreCase);
        lowered = lowered.Replace("bastion_basic_shield_", string.Empty, StringComparison.OrdinalIgnoreCase);

        var sb = new StringBuilder(lowered.Length);
        var prevUnderscore = false;
        foreach (var ch in lowered)
        {
            var keep = char.IsLetterOrDigit(ch);
            var c = keep ? ch : '_';
            if (c == '_')
            {
                if (prevUnderscore)
                {
                    continue;
                }
                prevUnderscore = true;
            }
            else
            {
                prevUnderscore = false;
            }

            sb.Append(c);
        }

        var slug = sb.ToString().Trim('_');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "clip";
        }

        var baseId = $"{classId}.{slug}";
        var id = baseId;
        var i = 2;
        var reservedSet = new HashSet<string>(reserved, StringComparer.OrdinalIgnoreCase);
        while (reservedSet.Contains(id))
        {
            id = $"{baseId}_{i++}";
        }

        return id;
    }

    private void SetFrameToUi(int idx)
    {
        if (currentAtlas is null || idx < 0 || idx >= currentAtlas.Frames.Count)
        {
            return;
        }

        var f = currentAtlas.Frames[idx];
        suppressFrameEvents = true;
        DirectionBox.Value = f.Direction;
        FrameNumberBox.Value = f.Frame;
        TimeBox.Value = (decimal)f.Time;
        XBox.Value = f.X;
        YBox.Value = f.Y;
        WBox.Value = f.W;
        HBox.Value = f.H;
        PivotXBox.Value = f.PivotX;
        PivotYBox.Value = f.PivotY;
        suppressFrameEvents = false;
    }

    private void DrawOverlay()
    {
        if (currentAtlas is null || currentBitmap is null)
        {
            FrameRect.IsVisible = false;
            PivotPoint.IsVisible = false;
            return;
        }

        var idx = (int)(FrameIndexBox.Value ?? 0);
        if (idx < 0 || idx >= currentAtlas.Frames.Count)
        {
            FrameRect.IsVisible = false;
            PivotPoint.IsVisible = false;
            return;
        }

        var f = currentAtlas.Frames[idx];

        var x = f.X;
        var y = f.Y;
        var w = Math.Max(1, f.W);
        var h = Math.Max(1, f.H);

        Canvas.SetLeft(FrameRect, x);
        Canvas.SetTop(FrameRect, y);
        FrameRect.Width = w;
        FrameRect.Height = h;
        FrameRect.IsVisible = true;

        var pivotX = x + f.PivotX - 4;
        var pivotY = y + f.PivotY - 4;
        Canvas.SetLeft(PivotPoint, pivotX);
        Canvas.SetTop(PivotPoint, pivotY);
        PivotPoint.IsVisible = true;
    }

    private void LoadClipmap(string classId)
    {
        if (string.IsNullOrWhiteSpace(classId))
        {
            return;
        }

        var clipmapDir = Path.Combine(NormalizeInputDir(), "clipmaps");
        currentClipmapPath = Path.Combine(clipmapDir, $"{classId}.json");

        if (File.Exists(currentClipmapPath))
        {
            currentClipmap = JsonSerializer.Deserialize<ClipmapFile>(File.ReadAllText(currentClipmapPath)) ?? new ClipmapFile();
        }
        else
        {
            currentClipmap = new ClipmapFile();
        }

        IdleClipBox.Text = currentClipmap.Default.IdleClipId;
        MoveClipBox.Text = currentClipmap.Default.MoveClipId;
        RunForwardClipBox.Text = currentClipmap.Default.RunForwardClipId;
        RunBackwardClipBox.Text = currentClipmap.Default.RunBackwardClipId;
        RunLeftClipBox.Text = currentClipmap.Default.RunLeftClipId;
        RunRightClipBox.Text = currentClipmap.Default.RunRightClipId;
        RunForwardLeftClipBox.Text = currentClipmap.Default.RunForwardLeftClipId;
        RunForwardRightClipBox.Text = currentClipmap.Default.RunForwardRightClipId;
        RunBackwardLeftClipBox.Text = currentClipmap.Default.RunBackwardLeftClipId;
        RunBackwardRightClipBox.Text = currentClipmap.Default.RunBackwardRightClipId;
        StrafeLeftClipBox.Text = currentClipmap.Default.StrafeLeftClipId;
        StrafeRightClipBox.Text = currentClipmap.Default.StrafeRightClipId;
        StrafeForwardLeftClipBox.Text = currentClipmap.Default.StrafeForwardLeftClipId;
        StrafeForwardRightClipBox.Text = currentClipmap.Default.StrafeForwardRightClipId;
        StrafeBackwardLeftClipBox.Text = currentClipmap.Default.StrafeBackwardLeftClipId;
        StrafeBackwardRightClipBox.Text = currentClipmap.Default.StrafeBackwardRightClipId;
        TurnLeftClipBox.Text = currentClipmap.Default.TurnLeftClipId;
        TurnRightClipBox.Text = currentClipmap.Default.TurnRightClipId;
        BlockClipBox.Text = currentClipmap.Default.BlockLoopClipId;
        HeavyClipBox.Text = currentClipmap.Default.HeavyClipId;
        FastChainBox.Text = string.Join(", ", currentClipmap.Default.FastChainClipIds);

        CastEBox.Text = GetCast("E");
        CastRBox.Text = GetCast("R");
        CastQBox.Text = GetCast("Q");
        CastTBox.Text = GetCast("T");
        Cast1Box.Text = GetCast("1");
        Cast2Box.Text = GetCast("2");
        Cast3Box.Text = GetCast("3");
        Cast4Box.Text = GetCast("4");

        string GetCast(string slot)
        {
            return currentClipmap.Default.CastClipBySlotLabel.TryGetValue(slot, out var v) ? v : string.Empty;
        }
    }

    private void Log(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var stamp = DateTime.Now.ToString("HH:mm:ss");
            LogBox.Text = string.IsNullOrWhiteSpace(LogBox.Text)
                ? $"[{stamp}] {text}"
                : $"{LogBox.Text}\n[{stamp}] {text}";
            LogBox.CaretIndex = LogBox.Text.Length;
        });
    }

    private static string ResolveRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 10; i++)
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

    private static string ResolveInitialInputDir(string root)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--input-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return Path.GetFullPath(args[i + 1]);
            }
        }

        var defaultInput = Path.Combine(root, "content", "animations");
        return Directory.Exists(defaultInput) ? defaultInput : "/Users/nckzvth/Desktop/Art/Characters";
    }
}

public sealed class AssetNode
{
    public string Name { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string ClassId { get; set; } = string.Empty;
    public string ClipPath { get; set; } = string.Empty;
    public List<AssetNode> Children { get; } = new();
}

public sealed class AtlasDocument
{
    public JsonObject Root { get; private init; } = new();
    public string SourcePath { get; private init; } = string.Empty;
    public List<AtlasFrame> Frames { get; } = new();

    public static AtlasDocument Load(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidDataException("Clip JSON root is not an object.");

        var doc = new AtlasDocument
        {
            Root = root,
            SourcePath = path
        };

        var framesNode = root["frames"] as JsonArray
            ?? throw new InvalidDataException("Clip JSON missing 'frames' array.");

        for (var i = 0; i < framesNode.Count; i++)
        {
            if (framesNode[i] is not JsonObject f)
            {
                continue;
            }

            doc.Frames.Add(new AtlasFrame(f));
        }

        return doc;
    }

    public string ResolveAtlasPath()
    {
        var atlasRaw = (Root["atlasTexture"]?.GetValue<string>() ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(atlasRaw))
        {
            atlasRaw = (Root["atlas"]?.GetValue<string>() ?? string.Empty).Trim();
        }

        if (string.IsNullOrWhiteSpace(atlasRaw))
        {
            var sidecar = Path.ChangeExtension(SourcePath, ".png");
            if (File.Exists(sidecar))
            {
                return sidecar;
            }

            var dir = Path.GetDirectoryName(SourcePath) ?? string.Empty;
            return Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly).FirstOrDefault() ?? sidecar;
        }

        if (Path.IsPathRooted(atlasRaw))
        {
            return atlasRaw;
        }

        var baseDir = Path.GetDirectoryName(SourcePath) ?? string.Empty;
        return Path.GetFullPath(Path.Combine(baseDir, atlasRaw));
    }

    public string GetResolvedClipId()
    {
        var id = (Root["clipId"]?.GetValue<string>() ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(id))
        {
            return id;
        }

        return Path.GetFileNameWithoutExtension(SourcePath);
    }

    public string ClipId
    {
        get => GetResolvedClipId();
        set => Root["clipId"] = value;
    }
}

public sealed class AtlasFrame
{
    private readonly JsonObject node;

    public AtlasFrame(JsonObject node)
    {
        this.node = node;
    }

    public int Direction { get => GetInt("direction"); set => node["direction"] = value; }
    public int Frame { get => GetInt("frame"); set => node["frame"] = value; }
    public float Time { get => GetFloat("time"); set => node["time"] = value; }
    public int X { get => GetInt("x"); set => node["x"] = value; }
    public int Y { get => GetInt("y"); set => node["y"] = value; }
    public int W { get => GetInt("w"); set => node["w"] = value; }
    public int H { get => GetInt("h"); set => node["h"] = value; }
    public int PivotX { get => GetInt("pivotX"); set => node["pivotX"] = value; }
    public int PivotY { get => GetInt("pivotY"); set => node["pivotY"] = value; }

    private int GetInt(string name)
    {
        if (node[name] is null)
        {
            return 0;
        }

        return node[name]!.GetValue<int>();
    }

    private float GetFloat(string name)
    {
        if (node[name] is null)
        {
            return 0f;
        }

        if (node[name] is JsonValue value && value.TryGetValue<float>(out var f))
        {
            return f;
        }

        return 0f;
    }
}

public sealed class ClipmapFile
{
    public ClipmapDef Default { get; set; } = new();
    public Dictionary<string, ClipmapDef> Specs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ClipmapDef
{
    public string IdleClipId { get; set; } = string.Empty;
    public string MoveClipId { get; set; } = string.Empty;
    public string RunForwardClipId { get; set; } = string.Empty;
    public string RunBackwardClipId { get; set; } = string.Empty;
    public string RunLeftClipId { get; set; } = string.Empty;
    public string RunRightClipId { get; set; } = string.Empty;
    public string RunForwardLeftClipId { get; set; } = string.Empty;
    public string RunForwardRightClipId { get; set; } = string.Empty;
    public string RunBackwardLeftClipId { get; set; } = string.Empty;
    public string RunBackwardRightClipId { get; set; } = string.Empty;
    public string StrafeLeftClipId { get; set; } = string.Empty;
    public string StrafeRightClipId { get; set; } = string.Empty;
    public string StrafeForwardLeftClipId { get; set; } = string.Empty;
    public string StrafeForwardRightClipId { get; set; } = string.Empty;
    public string StrafeBackwardLeftClipId { get; set; } = string.Empty;
    public string StrafeBackwardRightClipId { get; set; } = string.Empty;
    public string TurnLeftClipId { get; set; } = string.Empty;
    public string TurnRightClipId { get; set; } = string.Empty;
    public string BlockLoopClipId { get; set; } = string.Empty;
    public string HeavyClipId { get; set; } = string.Empty;
    public List<string> FastChainClipIds { get; set; } = new();
    public Dictionary<string, string> CastClipBySlotLabel { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
