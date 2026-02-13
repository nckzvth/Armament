using System.Text.Json;

namespace Armament.ServerHost;

public static class TiledCampaignMapLoader
{
    public static WorldZoneLayoutContent Load(string zoneId, string mapPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(mapPath));
        var root = doc.RootElement;
        var milliPerPixel = ReadNumberProperty(root, "worldMilliPerPixel", fallback: 100m);
        var invertY = ReadBoolProperty(root, "invertY", fallback: true);

        var layout = new WorldZoneLayoutContent { ZoneId = zoneId };
        if (!root.TryGetProperty("layers", out var layers) || layers.ValueKind != JsonValueKind.Array)
        {
            return layout;
        }

        for (var i = 0; i < layers.GetArrayLength(); i++)
        {
            var layer = layers[i];
            if (!layer.TryGetProperty("type", out var layerType) ||
                !string.Equals(layerType.GetString(), "objectgroup", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!layer.TryGetProperty("objects", out var objects) || objects.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            for (var j = 0; j < objects.GetArrayLength(); j++)
            {
                var obj = objects[j];
                var type = obj.TryGetProperty("type", out var typeValue)
                    ? typeValue.GetString() ?? string.Empty
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(type))
                {
                    continue;
                }

                var x = obj.TryGetProperty("x", out var xValue) ? xValue.GetDecimal() : 0m;
                var y = obj.TryGetProperty("y", out var yValue) ? yValue.GetDecimal() : 0m;
                var xMilli = ToMilli(x, milliPerPixel);
                var yMilli = ToMilli(invertY ? -y : y, milliPerPixel);
                var normalizedType = type.Trim().ToLowerInvariant();
                if (normalizedType == "campaign_npc")
                {
                    var npcId = ReadStringProperty(obj, "npcId");
                    if (!string.IsNullOrWhiteSpace(npcId))
                    {
                        layout.NpcPlacements.Add(new WorldNpcPlacementContent
                        {
                            NpcId = npcId,
                            XMilli = xMilli,
                            YMilli = yMilli
                        });
                    }

                    continue;
                }

                var encounterId = ReadStringProperty(obj, "encounterId");
                if (string.IsNullOrWhiteSpace(encounterId))
                {
                    continue;
                }

                if (!layout.Encounters.TryGetValue(encounterId, out var encounter))
                {
                    encounter = new WorldEncounterLayoutContent { EncounterId = encounterId };
                    layout.Encounters[encounterId] = encounter;
                }

                switch (normalizedType)
                {
                    case "encounter_anchor":
                        encounter.AnchorXMilli = xMilli;
                        encounter.AnchorYMilli = yMilli;
                        break;
                    case "campaign_object":
                    {
                        var objectDefId = ReadStringProperty(obj, "objectDefId");
                        if (!string.IsNullOrWhiteSpace(objectDefId))
                        {
                            encounter.ObjectPlacements.Add(new WorldObjectPlacementContent
                            {
                                ObjectDefId = objectDefId,
                                XMilli = xMilli,
                                YMilli = yMilli
                            });
                        }

                        break;
                    }
                    case "campaign_hazard":
                    {
                        var hazardId = ReadStringProperty(obj, "hazardId");
                        if (!string.IsNullOrWhiteSpace(hazardId))
                        {
                            encounter.HazardPlacements.Add(new WorldHazardPlacementContent
                            {
                                HazardId = hazardId,
                                XMilli = xMilli,
                                YMilli = yMilli
                            });
                        }

                        break;
                    }
                }
            }
        }

        return layout;
    }

    private static int ToMilli(decimal value, decimal milliPerPixel)
    {
        return (int)Math.Round(value * milliPerPixel, MidpointRounding.AwayFromZero);
    }

    private static string? ReadStringProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        for (var i = 0; i < properties.GetArrayLength(); i++)
        {
            var prop = properties[i];
            if (!prop.TryGetProperty("name", out var name) ||
                !string.Equals(name.GetString(), propertyName, StringComparison.Ordinal))
            {
                continue;
            }

            if (prop.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static decimal ReadNumberProperty(JsonElement element, string propertyName, decimal fallback)
    {
        if (!element.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        for (var i = 0; i < properties.GetArrayLength(); i++)
        {
            var prop = properties[i];
            if (!prop.TryGetProperty("name", out var name) ||
                !string.Equals(name.GetString(), propertyName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!prop.TryGetProperty("value", out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    private static bool ReadBoolProperty(JsonElement element, string propertyName, bool fallback)
    {
        if (!element.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        for (var i = 0; i < properties.GetArrayLength(); i++)
        {
            var prop = properties[i];
            if (!prop.TryGetProperty("name", out var name) ||
                !string.Equals(name.GetString(), propertyName, StringComparison.Ordinal))
            {
                continue;
            }

            if (prop.TryGetProperty("value", out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }
        }

        return fallback;
    }
}

public sealed class WorldZoneLayoutContent
{
    public string ZoneId { get; set; } = string.Empty;
    public Dictionary<string, WorldEncounterLayoutContent> Encounters { get; set; } = new(StringComparer.Ordinal);
    public List<WorldNpcPlacementContent> NpcPlacements { get; set; } = new();
}

public sealed class WorldEncounterLayoutContent
{
    public string EncounterId { get; set; } = string.Empty;
    public int? AnchorXMilli { get; set; }
    public int? AnchorYMilli { get; set; }
    public List<WorldObjectPlacementContent> ObjectPlacements { get; set; } = new();
    public List<WorldHazardPlacementContent> HazardPlacements { get; set; } = new();
}

public sealed class WorldObjectPlacementContent
{
    public string ObjectDefId { get; set; } = string.Empty;
    public int XMilli { get; set; }
    public int YMilli { get; set; }
}

public sealed class WorldHazardPlacementContent
{
    public string HazardId { get; set; } = string.Empty;
    public int XMilli { get; set; }
    public int YMilli { get; set; }
}

public sealed class WorldNpcPlacementContent
{
    public string NpcId { get; set; } = string.Empty;
    public int XMilli { get; set; }
    public int YMilli { get; set; }
}
