using Microsoft.Win32;

namespace AltAccounts.Core.Edition;

public enum WindowsEditionKind { Home, Pro, Other }

/// <summary>Windows edition read from HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion.</summary>
public sealed record EditionInfo(string EditionId, string ProductName, int Build, int Ubr)
{
    public WindowsEditionKind Kind => Classify(EditionId);

    /// <summary>Pro-only extras (direct switch) are enabled only on Professional editions.</summary>
    public bool ProFeaturesEnabled => Kind == WindowsEditionKind.Pro;

    public bool IsWindows11 => Build >= 22000;

    public string DisplayName => Kind switch
    {
        WindowsEditionKind.Home => $"Windows Home ({EditionId}), build {Build}.{Ubr}",
        WindowsEditionKind.Pro => $"Windows Pro ({EditionId}), build {Build}.{Ubr}",
        _ => $"{ProductName} ({EditionId}), build {Build}.{Ubr}",
    };

    public static WindowsEditionKind Classify(string? editionId) => editionId switch
    {
        "Core" or "CoreSingleLanguage" or "CoreN" or "CoreCountrySpecific" => WindowsEditionKind.Home,
        "Professional" or "ProfessionalN" or "ProfessionalWorkstation" or "ProfessionalWorkstationN"
            or "ProfessionalEducation" or "ProfessionalEducationN" => WindowsEditionKind.Pro,
        _ => WindowsEditionKind.Other,
    };

    public static EditionInfo Detect()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        string id = key?.GetValue("EditionID") as string ?? "Unknown";
        string name = key?.GetValue("ProductName") as string ?? "Windows";
        int build = int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out var b) ? b : Environment.OSVersion.Version.Build;
        int ubr = key?.GetValue("UBR") is int u ? u : 0;
        return new EditionInfo(id, name, build, ubr);
    }
}
