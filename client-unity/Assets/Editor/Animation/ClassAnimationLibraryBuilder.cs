#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Armament.Client.Animation;
using UnityEditor;
using UnityEngine;

namespace Armament.Client.Editor.Animation
{

public static class ClassAnimationLibraryBuilder
{
    private const string CharactersRoot = "Assets/Art/Characters";
    private const string OutputRoot = "Assets/Resources/Animation";

    private static bool _building;

    [MenuItem("Armament/Animation/Rebuild All Class Animation Libraries")]
    public static void RebuildAllFromMenu()
    {
        EnsureOutputFolders();
        DeleteBrokenAnimationAssets();
        RebuildAll(forceLog: true);
    }

    public static void RebuildAll(bool forceLog = false)
    {
        if (_building)
        {
            return;
        }

        if (!Directory.Exists(CharactersRoot))
        {
            if (forceLog)
            {
                Debug.LogWarning($"[ClassAnimationLibraryBuilder] Missing path: {CharactersRoot}");
            }

            return;
        }

        try
        {
            _building = true;
            EnsureOutputFolders();

            var classDirs = Directory.GetDirectories(CharactersRoot);
            var rebuiltCount = 0;
            for (var i = 0; i < classDirs.Length; i++)
            {
                var classDir = NormalizePath(classDirs[i]);
                var classId = Path.GetFileName(classDir).Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(classId))
                {
                    continue;
                }

                var animationsDir = $"{classDir}/Animations";
                if (!Directory.Exists(animationsDir))
                {
                    continue;
                }

                var clips = LoadClassClips(animationsDir);
                if (clips.Count == 0)
                {
                    continue;
                }

                clips.Sort((a, b) => string.CompareOrdinal(a.clipId, b.clipId));
                DeduplicateClipIds(clips);
                UpsertLibraryAsset(classId, clips);
                UpsertMapAsset(classId, clips);
                rebuiltCount++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (forceLog)
            {
                Debug.Log($"[ClassAnimationLibraryBuilder] Rebuilt animation assets for {rebuiltCount} class(es) in {OutputRoot}.");
            }
        }
        finally
        {
            _building = false;
        }
    }

    private static List<AtlasAnimationClip> LoadClassClips(string animationsDir)
    {
        var clips = new List<AtlasAnimationClip>();
        var jsonGuids = AssetDatabase.FindAssets("t:TextAsset", new[] { animationsDir });
        for (var i = 0; i < jsonGuids.Length; i++)
        {
            var jsonPath = AssetDatabase.GUIDToAssetPath(jsonGuids[i]);
            if (jsonPath.Contains("/bastion.archive/", StringComparison.OrdinalIgnoreCase) ||
                jsonPath.Contains("/.archive/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!jsonPath.EndsWith("_atlas.json", StringComparison.OrdinalIgnoreCase) ||
                jsonPath.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);
            if (jsonAsset is null)
            {
                continue;
            }

            AtlasExportData? parsed;
            try
            {
                parsed = JsonUtility.FromJson<AtlasExportData>(jsonAsset.text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClassAnimationLibraryBuilder] Failed parsing {jsonPath}: {ex.Message}");
                continue;
            }

            if (parsed is null || string.IsNullOrWhiteSpace(parsed.clip) || parsed.frames is null || parsed.frames.Length == 0)
            {
                continue;
            }

            var texturePath = jsonPath[..^".json".Length] + ".png";
            EnsureAtlasTextureImportSettings(texturePath, parsed.atlasWidth, parsed.atlasHeight);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture is null)
            {
                Debug.LogWarning($"[ClassAnimationLibraryBuilder] Missing atlas texture for {jsonPath}: {texturePath}");
                continue;
            }

            var clip = new AtlasAnimationClip
            {
                clipId = parsed.clip,
                atlasTexture = texture,
                directions = Math.Max(1, parsed.directions),
                framesPerDirection = Math.Max(1, parsed.framesPerDirection),
                fps = parsed.fps <= 0 ? 24f : parsed.fps,
                atlasWidth = parsed.atlasWidth <= 0 ? texture.width : parsed.atlasWidth,
                atlasHeight = parsed.atlasHeight <= 0 ? texture.height : parsed.atlasHeight,
                pixelsPerUnit = Math.Max(1, parsed.targetCellHeight),
                row0AtTop = parsed.row0AtTop,
                clockwise = parsed.clockwise,
                startYawDegrees = parsed.startYawDegrees,
                frames = new List<AtlasAnimationFrame>(parsed.frames.Length)
            };

            for (var fi = 0; fi < parsed.frames.Length; fi++)
            {
                var frame = parsed.frames[fi];
                clip.frames.Add(new AtlasAnimationFrame
                {
                    direction = frame.direction,
                    frame = frame.frame,
                    time = frame.time,
                    x = frame.x,
                    y = frame.y,
                    w = frame.w,
                    h = frame.h,
                    pivotX = frame.pivotX,
                    pivotY = frame.pivotY
                });
            }

            clips.Add(clip);
        }

        return clips;
    }

    private static void UpsertLibraryAsset(string classId, List<AtlasAnimationClip> clips)
    {
        var path = $"{OutputRoot}/{classId}.animationlibrary.asset";
        var library = AssetDatabase.LoadAssetAtPath<AtlasAnimationLibrary>(path);
        if (library is null)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) is not null)
            {
                AssetDatabase.DeleteAsset(path);
            }

            library = ScriptableObject.CreateInstance<AtlasAnimationLibrary>();
            library.ReplaceClips(clips);
            AssetDatabase.CreateAsset(library, path);
            EnsureScriptReference(library);
        }
        else
        {
            library.ReplaceClips(clips);
            EnsureScriptReference(library);
            EditorUtility.SetDirty(library);
        }
    }

    private static void UpsertMapAsset(string classId, IReadOnlyDictionary<string, AtlasAnimationClip> clipsById)
    {
        var path = $"{OutputRoot}/{classId}.animationmap.asset";
        var map = AssetDatabase.LoadAssetAtPath<ClassAnimationMap>(path);
        if (map is null)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) is not null)
            {
                AssetDatabase.DeleteAsset(path);
            }

            map = ScriptableObject.CreateInstance<ClassAnimationMap>();
            ApplyDefaultMap(map, clipsById);
            AssetDatabase.CreateAsset(map, path);
            EnsureScriptReference(map);
            return;
        }

        if (string.IsNullOrWhiteSpace(map.idleClipId) || string.IsNullOrWhiteSpace(map.moveClipId))
        {
            ApplyDefaultMap(map, clipsById);
            EditorUtility.SetDirty(map);
        }

        EnsureScriptReference(map);
    }

    private static void UpsertMapAsset(string classId, List<AtlasAnimationClip> clips)
    {
        var byId = new Dictionary<string, AtlasAnimationClip>(StringComparer.Ordinal);
        for (var i = 0; i < clips.Count; i++)
        {
            byId[clips[i].clipId] = clips[i];
        }

        UpsertMapAsset(classId, byId);
    }

    private static void ApplyDefaultMap(ClassAnimationMap map, IReadOnlyDictionary<string, AtlasAnimationClip> clips)
    {
        map.idleClipId = FindClipContaining(clips, "idle") ?? FindAny(clips);
        map.moveClipId = FindClipContaining(clips, "run01_forward") ?? FindClipContaining(clips, "run") ?? map.idleClipId;
        map.blockLoopClipId = FindClipContaining(clips, "blockshield01 - loop") ??
                              FindClipContaining(clips, "blockshield01") ??
                              FindClipContaining(clips, "block") ??
                              map.idleClipId;
        map.fastAttackClipId = FindClipContaining(clips, "attackshield01") ?? FindClipContaining(clips, "attack-r1") ?? map.moveClipId;
        map.heavyAttackClipId = FindClipContaining(clips, "attackshield02") ?? FindClipContaining(clips, "attack-r2") ?? map.fastAttackClipId;
        map.stunClipId = FindClipContaining(clips, "stun") ?? map.heavyAttackClipId;
        map.hitReactClipId = FindClipContaining(clips, "blockshield01 - hit") ?? map.stunClipId;
        map.deathClipId = FindClipContaining(clips, "death") ?? map.stunClipId;
        map.lootClipId = map.idleClipId;
        map.interactClipId = map.idleClipId;
        map.fastAttackChainClipIds = new[]
        {
            FindClipContaining(clips, "attackshield01") ?? map.fastAttackClipId,
            FindClipContaining(clips, "unarmed-attack-r1") ?? FindClipContaining(clips, "attack-r1") ?? map.fastAttackClipId,
            FindClipContaining(clips, "attackshield02") ?? map.heavyAttackClipId
        };
        map.castClipIds = new[]
        {
            FindClipContaining(clips, "attack-r1") ?? map.fastAttackClipId,
            FindClipContaining(clips, "attack-r2") ?? map.heavyAttackClipId,
            FindClipContaining(clips, "attack-r3") ?? map.heavyAttackClipId,
            map.heavyAttackClipId,
            map.fastAttackClipId,
            map.stunClipId,
            map.heavyAttackClipId,
            map.heavyAttackClipId
        };
    }

    private static string? FindClipContaining(IReadOnlyDictionary<string, AtlasAnimationClip> clips, string token)
    {
        foreach (var kvp in clips)
        {
            if (kvp.Key.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return kvp.Key;
            }
        }

        return null;
    }

    private static string FindAny(IReadOnlyDictionary<string, AtlasAnimationClip> clips)
    {
        foreach (var kvp in clips)
        {
            return kvp.Key;
        }

        return string.Empty;
    }

    private static void EnsureOutputFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }

        if (!AssetDatabase.IsValidFolder(OutputRoot))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "Animation");
        }
    }

    private static void DeleteBrokenAnimationAssets()
    {
        if (!Directory.Exists(OutputRoot))
        {
            return;
        }

        var files = Directory.GetFiles(OutputRoot, "*.asset", SearchOption.TopDirectoryOnly);
        for (var i = 0; i < files.Length; i++)
        {
            var path = NormalizePath(files[i]);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                path = NormalizePath(Path.GetRelativePath(Directory.GetCurrentDirectory(), files[i]));
            }

            var isAnimationAsset = path.EndsWith(".animationlibrary.asset", StringComparison.OrdinalIgnoreCase) ||
                                   path.EndsWith(".animationmap.asset", StringComparison.OrdinalIgnoreCase);
            if (!isAnimationAsset)
            {
                continue;
            }

            var asLibrary = AssetDatabase.LoadAssetAtPath<AtlasAnimationLibrary>(path);
            var asMap = AssetDatabase.LoadAssetAtPath<ClassAnimationMap>(path);
            if (asLibrary is null && asMap is null)
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
    }

    private static void DeduplicateClipIds(List<AtlasAnimationClip> clips)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < clips.Count; i++)
        {
            var id = clips[i].clipId;
            if (!seen.TryAdd(id, 0))
            {
                seen[id]++;
                clips[i].clipId = $"{id}#{seen[id]}";
            }
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static void EnsureAtlasTextureImportSettings(string texturePath, int atlasWidth, int atlasHeight)
    {
        var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer is null)
        {
            return;
        }

        var changed = false;
        var requiredMaxSize = Mathf.NextPowerOfTwo(Mathf.Max(32, Mathf.Max(atlasWidth, atlasHeight)));
        requiredMaxSize = Mathf.Clamp(requiredMaxSize, 32, 8192);

        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            changed = true;
        }

        if (importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
            changed = true;
        }

        if (importer.npotScale != TextureImporterNPOTScale.None)
        {
            importer.npotScale = TextureImporterNPOTScale.None;
            changed = true;
        }

        if (importer.maxTextureSize < requiredMaxSize)
        {
            importer.maxTextureSize = requiredMaxSize;
            changed = true;
        }

        if (!importer.alphaIsTransparency)
        {
            importer.alphaIsTransparency = true;
            changed = true;
        }

        if (importer.textureCompression != TextureImporterCompression.Uncompressed)
        {
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            changed = true;
        }

        if (importer.mipmapEnabled)
        {
            importer.mipmapEnabled = false;
            changed = true;
        }

        if (changed)
        {
            importer.SaveAndReimport();
        }
    }

    private static void EnsureScriptReference(ScriptableObject asset)
    {
        var script = MonoScript.FromScriptableObject(asset);
        if (script is null)
        {
            return;
        }

        var serialized = new SerializedObject(asset);
        var scriptProperty = serialized.FindProperty("m_Script");
        if (scriptProperty is null || scriptProperty.objectReferenceValue == script)
        {
            return;
        }

        scriptProperty.objectReferenceValue = script;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
    }

    [Serializable]
    private sealed class AtlasExportData
    {
        public string clip = string.Empty;
        public int directions;
        public int framesPerDirection;
        public float fps;
        public int atlasWidth;
        public int atlasHeight;
        public int targetCellHeight;
        public bool row0AtTop = true;
        public bool clockwise = true;
        public float startYawDegrees;
        public AtlasExportFrame[]? frames;
    }

    [Serializable]
    private sealed class AtlasExportFrame
    {
        public int direction;
        public int frame;
        public float time;
        public int x;
        public int y;
        public int w;
        public int h;
        public int pivotX;
        public int pivotY;
    }
}
}
