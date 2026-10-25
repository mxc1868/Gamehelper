namespace GameHelper.Build;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

internal sealed record OverlayPatchInspection(bool Patched, int[] FlagFileOffsets, Guid ModuleId, string Hash);

/// <summary>
/// An intentionally narrow build-time fix for the unsigned CTO 11.1.0 package.
/// Its non-VSync Present(0, None) calls block the same thread that polls input
/// and updates game data. Set only those two flag constants to DO_NOT_WAIT (8).
/// The pinned normalized SHA-256 rejects every other binary modification.
/// No metadata, call target, VSync branch, frame limiter, or NuGet cache is changed.
/// </summary>
internal static class OverlayPresentPatcher
{
    internal const string OriginalHash = "B60BDF884B48949AB9376C8CD390A6E5E5D2D6F7DB0804C0ED77E59ED50AE89A";
    private static readonly int[] FlagIlOffsets = [0xC9, 0x109];

    internal static OverlayPatchInspection Inspect(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata || pe.PEHeaders.CorHeader == null ||
            (pe.PEHeaders.CorHeader.Flags & CorFlags.StrongNameSigned) != 0 ||
            pe.PEHeaders.PEHeader?.CertificateTableDirectory.Size > 0)
            throw new InvalidDataException("Only the pinned unsigned CTO package is supported.");
        var metadata = pe.GetMetadataReader();
        if (metadata.GetString(metadata.GetAssemblyDefinition().Name) != "ClickableTransparentOverlay")
            throw new InvalidDataException("Not the expected overlay assembly.");
        var type = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition).Single(t =>
            metadata.GetString(t.Namespace) == "ClickableTransparentOverlay" && metadata.GetString(t.Name) == "Overlay");
        var method = type.GetMethods().Select(metadata.GetMethodDefinition).Single(m => metadata.GetString(m.Name) == "RunInfiniteLoop");
        var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes() ?? throw new InvalidDataException("Missing render-loop IL.");
        var section = pe.PEHeaders.SectionHeaders.Single(s => method.RelativeVirtualAddress >= s.VirtualAddress &&
            method.RelativeVirtualAddress < s.VirtualAddress + s.SizeOfRawData);
        var methodOffset = section.PointerToRawData + method.RelativeVirtualAddress - section.VirtualAddress;
        var headerBytes = (bytes[methodOffset] & 3) == 2 ? 1 : (BitConverter.ToUInt16(bytes, methodOffset) >> 12) * 4;
        var codeOffset = methodOffset + headerBytes;
        if (!bytes.AsSpan(codeOffset, il.Length).SequenceEqual(il) || il.Length <= 0x110 || il[0xAF] != 0x17 || il[0xB0] != 0x16)
            throw new InvalidDataException("Unexpected render-loop layout or VSync branch.");
        var flags = new List<int>();
        foreach (var offset in FlagIlOffsets)
        {
            // ldc.i4.0 (sync interval), ldc.i4.0/8 (flags), callvirt Present, pop.
            if (il[offset - 1] != 0x16 || il[offset] is not (0x16 or 0x1E) || il[offset + 1] != 0x6F || il[offset + 6] != 0x26)
                throw new InvalidDataException("Unexpected non-VSync Present call.");
            var member = metadata.GetMemberReference((MemberReferenceHandle)MetadataTokens.EntityHandle(BitConverter.ToInt32(il, offset + 2)));
            var owner = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            if (metadata.GetString(member.Name) != "Present" || metadata.GetString(owner.Namespace) != "Vortice.DXGI" || metadata.GetString(owner.Name) != "IDXGISwapChain")
                throw new InvalidDataException("Unexpected presentation call target.");
            flags.Add(codeOffset + offset);
        }
        var normalized = (byte[])bytes.Clone();
        foreach (var offset in flags) normalized[offset] = 0x16;
        if (Convert.ToHexString(SHA256.HashData(normalized)) != OriginalHash)
            throw new InvalidDataException("Unsupported overlay binary; review the patch before changing the package version.");
        if (flags.Select(offset => bytes[offset]).Distinct().Count() != 1)
            throw new InvalidDataException("Partially patched overlay binary.");
        return new(bytes[flags[0]] == 0x1E, flags.ToArray(), metadata.GetGuid(metadata.GetModuleDefinition().Mvid), Convert.ToHexString(SHA256.HashData(bytes)));
    }

    internal static bool Patch(string path)
    {
        path = Path.GetFullPath(path);
        if (path.Replace('\\', '/').Contains("/.nuget/packages/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to modify the NuGet package cache; patch a build-output copy.");
        var bytes = File.ReadAllBytes(path);
        var inspection = Inspect(bytes);
        if (inspection.Patched) return false;
        foreach (var offset in inspection.FlagFileOffsets) bytes[offset] = 0x1E;
        if (!Inspect(bytes).Patched) throw new InvalidDataException("Presentation patch verification failed.");
        var temporary = path + ".nonblocking-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            // Replace rather than write in place, so even a hard-linked build copy
            // cannot mutate the package cache. A locked destination must fail normally.
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return true;
    }
}
