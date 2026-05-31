using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class AiAgentMemoryStore
{
    private static string MemoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VPet",
        "AiAgentMemory.json");

    public string Remember(string fact)
    {
        fact = (fact ?? "").Trim();
        if (string.IsNullOrWhiteSpace(fact))
            return "\u6c92\u6709\u53ef\u4ee5\u8a18\u4f4f\u7684\u5167\u5bb9\u3002";

        var memories = Load();
        if (!memories.Any(memory => memory.Equals(fact, StringComparison.OrdinalIgnoreCase)))
            memories.Add(fact);

        Save(memories);
        return "\u6211\u8a18\u4f4f\u4e86\uff1a" + fact;
    }

    public string Recall()
    {
        var memories = Load();
        if (memories.Count == 0)
            return "\u76ee\u524d\u9084\u6c92\u6709\u8a18\u61b6\u3002";

        var builder = new StringBuilder();
        builder.AppendLine("\u5df2\u8a18\u4f4f\u7684\u4e8b\uff1a");
        foreach (var memory in memories.Take(20))
            builder.AppendLine("- " + memory);
        return builder.ToString();
    }

    private static List<string> Load()
    {
        try
        {
            if (!File.Exists(MemoryPath))
                return new List<string>();

            var json = File.ReadAllText(MemoryPath);
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static void Save(List<string> memories)
    {
        var directory = Path.GetDirectoryName(MemoryPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(memories, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(MemoryPath, json);
    }
}
