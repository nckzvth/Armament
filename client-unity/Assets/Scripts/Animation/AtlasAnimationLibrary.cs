#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Armament.Client.Animation
{

[CreateAssetMenu(menuName = "Armament/Animation/Atlas Animation Library", fileName = "atlas.animationlibrary")]
public sealed class AtlasAnimationLibrary : ScriptableObject
{
    [SerializeField] private List<AtlasAnimationClip> clips = new();

    public IReadOnlyList<AtlasAnimationClip> Clips => clips;

    public AtlasAnimationClip? FindClip(string clipId)
    {
        for (var i = 0; i < clips.Count; i++)
        {
            if (string.Equals(clips[i].clipId, clipId, StringComparison.Ordinal))
            {
                return clips[i];
            }
        }

        return null;
    }

    public void ReplaceClips(List<AtlasAnimationClip> updated)
    {
        clips = updated ?? new List<AtlasAnimationClip>();
    }
}

[Serializable]
public sealed class AtlasAnimationClip
{
    public string clipId = string.Empty;
    public Texture2D? atlasTexture;
    public int directions = 8;
    public int framesPerDirection;
    public float fps = 24f;
    public int atlasWidth;
    public int atlasHeight;
    public int pixelsPerUnit = 64;
    public bool row0AtTop = true;
    public bool clockwise = true;
    public float startYawDegrees;
    public List<AtlasAnimationFrame> frames = new();
}

[Serializable]
public struct AtlasAnimationFrame
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
