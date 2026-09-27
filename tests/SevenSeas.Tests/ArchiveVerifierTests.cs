using System.Text;
using SevenSeas.Core.Models;
using SevenSeas.Core.Services;

namespace SevenSeas.Tests;

public sealed class ArchiveVerifierTests
{
    private readonly ArchiveVerifier _verifier = new();

    [Fact]
    public void Verify_AcceptsZip()
    {
        using var workspace = new TempWorkspace();
        var file = workspace.CreateFile("a.zip", new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00 });

        var result = _verifier.Verify(file);

        Assert.True(result.IsValid);
        Assert.Equal(ArchiveKind.Zip, result.Kind);
    }

    [Fact]
    public void Verify_AcceptsRar()
    {
        using var workspace = new TempWorkspace();
        var file = workspace.CreateFile("a.rar", new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00, 0x01 });

        Assert.Equal(ArchiveKind.Rar, _verifier.Verify(file).Kind);
    }

    [Fact]
    public void Verify_AcceptsSevenZip()
    {
        using var workspace = new TempWorkspace();
        var file = workspace.CreateFile("a.7z", new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00 });

        Assert.Equal(ArchiveKind.SevenZip, _verifier.Verify(file).Kind);
    }

    [Fact]
    public void Verify_AcceptsGZip()
    {
        using var workspace = new TempWorkspace();
        var file = workspace.CreateFile("a.gz", new byte[] { 0x1F, 0x8B, 0x08, 0x00 });

        Assert.Equal(ArchiveKind.GZip, _verifier.Verify(file).Kind);
    }

    [Fact]
    public void Verify_AcceptsTar()
    {
        using var workspace = new TempWorkspace();
        var header = new byte[512];
        Encoding.ASCII.GetBytes("ustar").CopyTo(header, 257);
        var file = workspace.CreateFile("a.tar", header);

        Assert.Equal(ArchiveKind.Tar, _verifier.Verify(file).Kind);
    }

    [Fact]
    public void Verify_RejectsEmptyFile()
    {
        using var workspace = new TempWorkspace();
        var file = workspace.CreateFile("a.zip", Array.Empty<byte>());

        var result = _verifier.Verify(file);

        Assert.False(result.IsValid);
        Assert.Contains("empty", result.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_RejectsUnknownSignature()
    {
        using var workspace = new TempWorkspace();
        var file = workspace.CreateFile("a.zip", Encoding.ASCII.GetBytes("hello world, not an archive"));

        var result = _verifier.Verify(file);

        Assert.False(result.IsValid);
        Assert.Equal(ArchiveKind.Unknown, result.Kind);
    }

    [Fact]
    public void Verify_RejectsMissingFile()
    {
        var result = _verifier.Verify(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid()));

        Assert.False(result.IsValid);
        Assert.Contains("no longer exists", result.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_RejectsBlankPath()
    {
        Assert.False(_verifier.Verify(string.Empty).IsValid);
    }

    [Fact]
    public void Verify_RejectsTooSmallFile()
    {
        using var workspace = new TempWorkspace();
        var file = workspace.CreateFile("a.zip", new byte[] { 0x50, 0x4B });

        var result = _verifier.Verify(file);

        Assert.False(result.IsValid);
        Assert.Contains("too small", result.Reason!, StringComparison.OrdinalIgnoreCase);
    }
}
