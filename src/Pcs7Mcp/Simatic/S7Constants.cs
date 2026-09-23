namespace Pcs7Mcp.Simatic;

/// <summary>Constants from the S7ABATCX type library (STEP 7 V5.7 command interface).</summary>
public static class S7Constants
{
    public const int ProjectTypeProject = 1122305;
    public const int ProjectTypeLibrary = 1122306;
    public const int ProjectTypeMultiproject = 1122320;

    public const int SwBlock = 18;
    public const int SwContainer = 63;
    public const int SwSource = 65;
    public const int SwPlan = 68;

    public const int ContainerBlocks = 1138689;
    public const int ContainerSources = 1122308;
    public const int ContainerCharts = 17829889;

    public const int PlanCfc = 17829890;
    public const int PlanSfc = 17829891;

    public const int OverwriteAll = 2;

    public const int GsfDoOverwrite = 128;
    public const int GsfIncludeUsedBlocks = 16;

    public static string ProjectType(int t) => t switch
    {
        ProjectTypeProject => "Project",
        ProjectTypeLibrary => "Library",
        ProjectTypeMultiproject => "Multiproject",
        _ => $"Unknown({t})"
    };

    public static string ContainerKind(int t) => t switch
    {
        ContainerBlocks => "blocks",
        ContainerSources => "sources",
        ContainerCharts => "charts",
        _ => $"container({t})"
    };

    public static int ContainerTypeFromKind(string kind) => kind.Trim().ToLowerInvariant() switch
    {
        "blocks" or "blocchi" or "bausteine" => ContainerBlocks,
        "sources" or "sorgenti" or "quellen" => ContainerSources,
        "charts" or "cfc" or "sfc" or "plans" or "pläne" => ContainerCharts,
        _ => throw new ArgumentException($"Unknown container kind '{kind}' (use blocks, sources or charts)")
    };

    public static string BlockType(int t) => t switch
    {
        1138945 => "FB", 1138946 => "FC", 1138947 => "DB", 1138948 => "OB",
        1138949 => "SDB", 1138955 => "SystemData", 1138950 => "UDT",
        1138951 => "SFC", 1138952 => "SFB", 1138953 => "VAT",
        _ => $"Block({t})"
    };

    public static string SourceType(int t) => t switch
    {
        1122309 => "STL", 1122310 => "SCL", 1122311 => "GRAPH", 1122312 => "SCL-Make",
        1122313 => "HiGraph-GG", 1122314 => "HiGraph-ZG", 1122315 => "NET",
        1139210 => "STL-Encrypted", 1139211 => "SCL-Encrypted",
        _ => $"Source({t})"
    };

    public static string PlanType(int t) => t switch
    {
        PlanCfc => "CFC", PlanSfc => "SFC", _ => $"Chart({t})"
    };

    public static string ModuleState(int s) => s switch
    {
        256 => "RUN", 512 => "STOP", 1024 => "HALT", 2048 => "DEFECT", 4096 => "STARTUP",
        _ => $"Unknown({s})"
    };

    public static int SymbolImportFlag(string mode) => mode.Trim().ToLowerInvariant() switch
    {
        "insert" => 0,
        "overwrite-name" or "overwritenameleading" => 1,
        "overwrite-operand" or "overwriteoperandleading" => 2,
        _ => throw new ArgumentException("mode must be insert, overwrite-name or overwrite-operand")
    };
}
