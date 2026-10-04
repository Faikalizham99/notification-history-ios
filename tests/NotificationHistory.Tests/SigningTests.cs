using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using NotificationHistory.Core.Services;

static class SigningTests
{
    public static void WriteFixtures(string directory)
    {
        Directory.CreateDirectory(directory);
        void Write(string name, byte[] bytes) => File.WriteAllBytes(Path.Combine(directory, name + ".macho"), bytes);
        byte[] Groups(params string[] groups) => Executable(new XDocument(new XElement("plist", new XElement("dict",
            new XElement("key", "com.apple.security.application-groups"),
            new XElement("array", groups.Select(group => new XElement("string", group)))))).ToString());
        Write("original", Groups("group.common.other", SignedAppGroups.PreferredGroup));
        Write("provider-app", Groups("group.provider.z", "group.provider.a", "group.app.only"));
        Write("provider-widget", Groups("group.widget.only", "group.provider.a", "group.provider.z"));
        Write("provider-intents", Groups("group.provider.z", "group.intents.only", "group.provider.a"));
        Write("missing-groups", Executable("<plist><dict/></plist>"));
        Write("invalid-groups", Groups("group.", "group.with spaces", "group.provider.a", "group.provider.a"));
        Write("unsigned", Executable(null));
        Write("truncated", new byte[24]);
        var badOffset = Groups("group.provider.a");
        BinaryPrimitives.WriteUInt32BigEndian(badOffset.AsSpan(112, 4), uint.MaxValue);
        Write("bad-offset", badOffset);
        var badLoad = Groups("group.provider.a");
        BinaryPrimitives.WriteUInt32LittleEndian(badLoad.AsSpan(36, 4), uint.MaxValue);
        Write("bad-load", badLoad);
        Write("bad-plist", Executable("<plist><dict><key>com.apple.security.application-groups</key><true/></dict></plist>"));
    }

    private static byte[] Executable(string? plist)
    {
        var xml = plist is null ? [] : Encoding.UTF8.GetBytes(plist);
        var bytes = new byte[plist is null ? 32 : 124 + xml.Length];
        void Little(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
        void Big(int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), value);
        Little(0, 0xfeedfacf); Little(4, 0x0100000c);
        if (plist is null) return bytes;
        Little(16, 1); Little(20, 16);
        Little(32, 0x1d); Little(36, 16); Little(40, 96); Little(44, (uint)(28 + xml.Length));
        Big(96, 0xfade0cc0); Big(100, (uint)(28 + xml.Length)); Big(104, 1);
        Big(108, 5); Big(112, 20); Big(116, 0xfade7171); Big(120, (uint)(8 + xml.Length));
        xml.CopyTo(bytes, 124);
        return bytes;
    }

    public static void Run(string directory)
    {
        WriteFixtures(directory);
        HashSet<string> Read(string name) => SignedAppGroups.Read(Path.Combine(directory, name + ".macho"));
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); }
        var original = Read("original");
        Check(SignedAppGroups.SelectCommon([original, original, original]) == SignedAppGroups.PreferredGroup,
            "Original App Group preserved when all signatures authorize it");
        var app = Read("provider-app"); var widget = Read("provider-widget"); var intents = Read("provider-intents");
        Check(SignedAppGroups.SelectCommon([app, widget, intents]) == "group.provider.a",
            "Rewritten App Groups resolve deterministically across all three signatures");
        Check(Read("invalid-groups").SetEquals(["group.provider.a"]), "Invalid and duplicate App Group IDs filtered");
        Check(Read("unsigned").Count == 0 && Read("missing-groups").Count == 0, "Unsigned and missing-group signatures grant no groups");
        var rejected = false;
        try { SignedAppGroups.SelectCommon([app, widget, original]); } catch (SharedStorageConfigurationException) { rejected = true; }
        Check(rejected, "Different app/extension App Groups rejected without private storage fallback");
        rejected = false;
        try { SignedAppGroups.SelectCommon([app, widget]); } catch (SharedStorageConfigurationException) { rejected = true; }
        Check(rejected, "Missing extension signature rejected");
        foreach (var name in new[] { "truncated", "bad-offset", "bad-load", "bad-plist" })
        {
            rejected = false;
            try { Read(name); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Malformed signature rejected: " + name);
        }
    }

    public static void InspectIpa(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var paths = new[] { "Payload/NotificationHistory.app/NotificationHistory",
            "Payload/NotificationHistory.app/PlugIns/NotificationHistoryWidget.appex/NotificationHistoryWidget",
            "Payload/NotificationHistory.app/Extensions/NotificationHistoryIntents.appex/NotificationHistoryIntents" };
        var groups = paths.Select(executable =>
        {
            var entry = archive.GetEntry(executable) ?? throw new IOException("Missing executable: " + executable);
            using var input = entry.Open(); using var stream = new MemoryStream(); input.CopyTo(stream);
            return SignedAppGroups.Read(stream);
        }).ToArray();
        Console.WriteLine("Common signed App Group: " + SignedAppGroups.SelectCommon(groups));
        Console.WriteLine("PASS all three executable signatures agree on a shared group (container access still requires device validation).");
    }
}
