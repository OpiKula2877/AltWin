using AltAccounts.Core.Sharing;

namespace AltAccounts.Core.Tests;

public class SharingTests
{
    private static SharePathValidator V() => new(
        [@"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData"],
        @"C:\Users",
        _ => true);

    [Theory]
    [InlineData(@"C:\Shared")]
    [InlineData(@"C:\Shared\Games")]
    [InlineData(@"D:\Games\Library")]
    [InlineData(@"C:\Shared\")]
    public void DedicatedFolders_AreAllowed(string path)
    {
        var r = V().Check(path);
        Assert.True(r.Ok, r.Error);
        Assert.Null(r.Warning);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
    [InlineData(@"C:")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\windows")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files\Steam")]
    [InlineData(@"C:\Program Files (x86)")]
    [InlineData(@"C:\ProgramData\Thing")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\Users\Alice")]
    [InlineData(@"C:\Users\Alice\")]
    [InlineData(@"\\server\share\x")]
    [InlineData(@"relative\path")]
    [InlineData("")]
    public void ProtectedOrInvalid_AreRefused(string path)
    {
        Assert.False(V().Check(path).Ok);
    }

    [Fact]
    public void ProgramFilesPrefix_DoesNotBlockSimilarNames()
    {
        Assert.True(V().Check(@"C:\Program FilesBackup").Ok);
        Assert.True(V().Check(@"C:\WindowsGames").Ok);
    }

    [Fact]
    public void FolderInsideProfile_AllowedWithWarning()
    {
        var r = V().Check(@"C:\Users\Alice\Games");
        Assert.True(r.Ok);
        Assert.NotNull(r.Warning);
    }

    [Fact]
    public void NonLocalDrive_Refused()
    {
        var v = new SharePathValidator([], @"C:\Users", _ => false);
        Assert.False(v.Check(@"Z:\Stuff").Ok);
    }

    [Theory]
    [InlineData(ShareLevel.Read, "R")]
    [InlineData(ShareLevel.Modify, "M")]
    [InlineData(ShareLevel.Full, "F")]
    public void IcaclsGrant_UsesSidAndInheritance(ShareLevel level, string letter)
    {
        var args = IcaclsArgs.Grant(@"C:\Shared Games", "S-1-5-21-1-2-3-1001", level);
        Assert.Equal([@"C:\Shared Games", "/grant", $"*S-1-5-21-1-2-3-1001:(OI)(CI){letter}"], args);
    }

    [Fact]
    public void IcaclsRemove_RemovesGrantsBySid()
    {
        Assert.Equal([@"C:\Shared", "/remove:g", "*S-1-5-21-1-2-3-1001"], IcaclsArgs.Remove(@"C:\Shared", "S-1-5-21-1-2-3-1001"));
    }

    [Theory]
    [InlineData("Everyone")]
    [InlineData("S-1-5-21-1 /T")]
    [InlineData("S-1-5:(F)")]
    public void IcaclsArgs_RejectNonSids(string sid)
    {
        Assert.Throws<ArgumentException>(() => IcaclsArgs.Grant(@"C:\Shared", sid, ShareLevel.Read));
    }
}
