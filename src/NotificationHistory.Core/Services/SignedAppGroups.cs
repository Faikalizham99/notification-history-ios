using System.Buffers.Binary;
using System.Xml;
using System.Xml.Linq;

namespace NotificationHistory.Core.Services;

public sealed class SharedStorageConfigurationException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

// Reads only the entitlement blob in an installed arm64 executable. iOS verifies
// the signature/provisioning at install and launch; this is not a signature verifier.
public static class SignedAppGroups
{
    public const string PreferredGroup = "group.com.faikal.notificationhistory";
    private const int MaximumMetadataSize = 1024 * 1024;

    public static HashSet<string> Read(string executable)
    {
        using var stream = File.OpenRead(executable);
        return Read(stream);
    }

    public static HashSet<string> Read(Stream stream)
    {
        byte[] ReadAt(long offset, int length)
        {
            if (offset < 0 || length < 0 || length > MaximumMetadataSize || offset > stream.Length - length)
                throw new InvalidDataException("Invalid code-signature bounds.");
            stream.Position = offset;
            var data = new byte[length];
            stream.ReadExactly(data);
            return data;
        }
        static uint Little(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
        static uint Big(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));

        var header = ReadAt(0, 32);
        if (Little(header, 0) != 0xfeedfacf) throw new InvalidDataException("Expected an arm64 Mach-O executable.");
        var count = Little(header, 16);
        var commandSize = Little(header, 20);
        if (count > 4096 || commandSize > MaximumMetadataSize) throw new InvalidDataException("Invalid Mach-O load commands.");
        var commands = ReadAt(32, (int)commandSize);
        var cursor = 0;
        for (var index = 0; index < count; index++)
        {
            if (cursor > commands.Length - 8) throw new InvalidDataException("Truncated Mach-O command.");
            var command = Little(commands, cursor);
            var size = Little(commands, cursor + 4);
            if (size < 8 || size > commands.Length - cursor) throw new InvalidDataException("Invalid Mach-O command size.");
            if (command == 0x1d) // LC_CODE_SIGNATURE
            {
                if (size < 16) throw new InvalidDataException("Truncated code-signature command.");
                var signatureOffset = (long)Little(commands, cursor + 8);
                var signatureSize = Little(commands, cursor + 12);
                var signature = ReadAt(signatureOffset, 12);
                var signatureLength = Big(signature, 4);
                var blobCount = Big(signature, 8);
                if (Big(signature, 0) != 0xfade0cc0 || signatureLength > signatureSize || blobCount > 128 ||
                    signatureLength < 12 + blobCount * 8 || signatureOffset > stream.Length - signatureLength)
                    throw new InvalidDataException("Invalid code-signature superblob.");
                var entries = ReadAt(signatureOffset + 12, (int)blobCount * 8);
                for (var blobIndex = 0; blobIndex < blobCount; blobIndex++)
                {
                    if (Big(entries, blobIndex * 8) != 5) continue; // CSSLOT_ENTITLEMENTS
                    var offset = Big(entries, blobIndex * 8 + 4);
                    if (offset < 12 + blobCount * 8 || offset > signatureLength - 8)
                        throw new InvalidDataException("Invalid entitlement offset.");
                    var blob = ReadAt(signatureOffset + offset, 8);
                    var length = Big(blob, 4);
                    if (Big(blob, 0) != 0xfade7171 || length < 8 || length > MaximumMetadataSize || length > signatureLength - offset)
                        throw new InvalidDataException("Invalid entitlement blob.");
                    using var xmlStream = new MemoryStream(ReadAt(signatureOffset + offset + 8, (int)length - 8));
                    using var reader = XmlReader.Create(xmlStream, new XmlReaderSettings
                    { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = MaximumMetadataSize });
                    var dict = XDocument.Load(reader).Root?.Element("dict")
                        ?? throw new InvalidDataException("Invalid entitlement plist.");
                    var key = dict.Elements("key").SingleOrDefault(element => element.Value == "com.apple.security.application-groups");
                    if (key is null) return new(StringComparer.Ordinal);
                    var array = key.ElementsAfterSelf().FirstOrDefault();
                    if (array?.Name != "array" || array.Elements().Any(element => element.Name != "string"))
                        throw new InvalidDataException("App Groups must be an array of strings.");
                    return array.Elements("string").Select(element => element.Value)
                        .Where(IsGroupIdentifier).ToHashSet(StringComparer.Ordinal);
                }
                return new(StringComparer.Ordinal);
            }
            cursor += (int)size;
        }
        return new(StringComparer.Ordinal);
    }

    private static bool IsGroupIdentifier(string value) => value.StartsWith("group.", StringComparison.Ordinal) &&
        value.Length > 6 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    public static string SelectCommon(IEnumerable<HashSet<string>> signatures)
    {
        HashSet<string>? common = null;
        var count = 0;
        foreach (var groups in signatures)
        {
            if (common is null) common = new(groups, StringComparer.Ordinal);
            else common.IntersectWith(groups);
            count++;
        }
        if (count != 3 || common is null || common.Count == 0)
            throw new SharedStorageConfigurationException("The app, widget, and Save Notification extension need the same authorized App Group. Re-sign all three with matching App Groups.");
        return common.Contains(PreferredGroup) ? PreferredGroup : common.Order(StringComparer.Ordinal).First();
    }
}
