namespace VPet_Simulator.Windows.AiAgent;

internal sealed class OllamaSkillCall
{
    public static readonly OllamaSkillCall None = new("none");

    public OllamaSkillCall(
        string skillName,
        string location = "",
        string fact = "",
        string title = "",
        string time = "",
        string note = "",
        string target = "",
        string query = "")
    {
        SkillName = string.IsNullOrWhiteSpace(skillName) ? "none" : skillName.Trim();
        Location = location?.Trim() ?? "";
        Fact = fact?.Trim() ?? "";
        Title = title?.Trim() ?? "";
        Time = time?.Trim() ?? "";
        Note = note?.Trim() ?? "";
        Target = target?.Trim() ?? "";
        Query = query?.Trim() ?? "";
    }

    public string SkillName { get; }
    public string Location { get; }
    public string Fact { get; }
    public string Title { get; }
    public string Time { get; }
    public string Note { get; }
    public string Target { get; }
    public string Query { get; }

    public bool HasSkill => !SkillName.Equals("none", System.StringComparison.OrdinalIgnoreCase);
}
