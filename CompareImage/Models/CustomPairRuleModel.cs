using System.Collections.Generic;

namespace CompareImage.Models;

public class CustomPairRuleDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Script { get; set; } = string.Empty;
}

public class CustomPairRuleConfigFile
{
    public List<CustomPairRuleDefinition> Rules { get; set; } = new();
}
