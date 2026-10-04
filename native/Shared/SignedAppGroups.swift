import Foundation

enum SharedStorageConfigurationError: LocalizedError {
    case invalidSignature, noCommonGroup, unavailableGroup(String)
    var errorDescription: String? {
        switch self {
        case .invalidSignature: "Cannot read shared-storage signing information. Re-sign the app and both extensions with matching App Groups."
        case .noCommonGroup: "The app, widget, and Save Notification extension need the same authorized App Group. Re-sign all three with matching App Groups."
        case .unavailableGroup(let group): "iOS cannot open the signed App Group (\(group)). Check the app and extension provisioning profiles and unlock your iPhone once after restarting."
        }
    }
}

// Read only installed signature metadata. iOS enforces signature/provisioning
// validity; selecting a group here does not grant access to its container.
enum SignedAppGroups {
    static let preferred = "group.com.faikal.notificationhistory"
    private static let maximumMetadataSize = 1024 * 1024

    static func read(_ executable: URL) throws -> Set<String> {
        let handle = try FileHandle(forReadingFrom: executable)
        defer { try? handle.close() }
        let fileLength = try handle.seekToEnd()
        func readAt(_ offset: UInt64, _ count: Int) throws -> Data {
            guard count >= 0, count <= maximumMetadataSize, UInt64(count) <= fileLength,
                  offset <= fileLength - UInt64(count) else { throw SharedStorageConfigurationError.invalidSignature }
            try handle.seek(toOffset: offset)
            var bytes = Data()
            while bytes.count < count {
                guard let chunk = try handle.read(upToCount: count - bytes.count), !chunk.isEmpty else {
                    throw SharedStorageConfigurationError.invalidSignature
                }
                bytes.append(chunk)
            }
            return bytes
        }
        func little(_ data: Data, _ offset: Int) -> UInt32 {
            UInt32(data[offset]) | UInt32(data[offset + 1]) << 8 | UInt32(data[offset + 2]) << 16 | UInt32(data[offset + 3]) << 24
        }
        func big(_ data: Data, _ offset: Int) -> UInt32 {
            UInt32(data[offset]) << 24 | UInt32(data[offset + 1]) << 16 | UInt32(data[offset + 2]) << 8 | UInt32(data[offset + 3])
        }
        let header = try readAt(0, 32)
        guard little(header, 0) == 0xfeedfacf else { throw SharedStorageConfigurationError.invalidSignature }
        let count = Int(little(header, 16)), size = Int(little(header, 20))
        guard count <= 4096, size <= maximumMetadataSize else { throw SharedStorageConfigurationError.invalidSignature }
        let commands = try readAt(32, size)
        var cursor = 0
        for _ in 0..<count {
            guard cursor <= commands.count - 8 else { throw SharedStorageConfigurationError.invalidSignature }
            let command = little(commands, cursor), commandSize = Int(little(commands, cursor + 4))
            guard commandSize >= 8, commandSize <= commands.count - cursor else { throw SharedStorageConfigurationError.invalidSignature }
            if command == 0x1d { // LC_CODE_SIGNATURE
                guard commandSize >= 16 else { throw SharedStorageConfigurationError.invalidSignature }
                let signatureOffset = UInt64(little(commands, cursor + 8))
                let signatureSize = UInt64(little(commands, cursor + 12))
                let signature = try readAt(signatureOffset, 12)
                let signatureLength = UInt64(big(signature, 4)), blobCount = Int(big(signature, 8))
                guard big(signature, 0) == 0xfade0cc0, signatureLength <= signatureSize, blobCount <= 128,
                      signatureLength >= UInt64(12 + blobCount * 8), signatureLength <= fileLength,
                      signatureOffset <= fileLength - signatureLength else { throw SharedStorageConfigurationError.invalidSignature }
                let entries = try readAt(signatureOffset + 12, blobCount * 8)
                for index in 0..<blobCount {
                    if big(entries, index * 8) != 5 { continue } // CSSLOT_ENTITLEMENTS
                    let offset = UInt64(big(entries, index * 8 + 4))
                    guard offset >= UInt64(12 + blobCount * 8), offset <= signatureLength - 8 else {
                        throw SharedStorageConfigurationError.invalidSignature
                    }
                    let blob = try readAt(signatureOffset + offset, 8), length = Int(big(blob, 4))
                    guard big(blob, 0) == 0xfade7171, length >= 8, length <= maximumMetadataSize,
                          UInt64(length) <= signatureLength - offset else { throw SharedStorageConfigurationError.invalidSignature }
                    let xml = try readAt(signatureOffset + offset + 8, length - 8)
                    guard let dict = try PropertyListSerialization.propertyList(from: xml, options: [], format: nil) as? [String: Any] else {
                        throw SharedStorageConfigurationError.invalidSignature
                    }
                    guard let value = dict["com.apple.security.application-groups"] else { return [] }
                    guard let groups = value as? [String] else { throw SharedStorageConfigurationError.invalidSignature }
                    return Set(groups.filter { value in
                        value.hasPrefix("group.") && value.count > 6 && value.utf8.allSatisfy { byte in
                            (byte >= 65 && byte <= 90) || (byte >= 97 && byte <= 122) || (byte >= 48 && byte <= 57) || byte == 46 || byte == 45 || byte == 95
                        }
                    })
                }
                return []
            }
            cursor += commandSize
        }
        return []
    }

    static func selectCommon(_ signatures: [Set<String>]) throws -> String {
        guard signatures.count == 3, let first = signatures.first else { throw SharedStorageConfigurationError.noCommonGroup }
        let common = signatures.dropFirst().reduce(first) { $0.intersection($1) }
        guard !common.isEmpty else { throw SharedStorageConfigurationError.noCommonGroup }
        return common.contains(preferred) ? preferred : common.sorted().first!
    }

    private static let selected: Result<String, Error> = Result {
        var appURL = Bundle.main.bundleURL
        while appURL.pathExtension != "app" {
            let parent = appURL.deletingLastPathComponent()
            guard parent.path != appURL.path else { throw SharedStorageConfigurationError.invalidSignature }
            appURL = parent
        }
        let urls = [appURL, appURL.appendingPathComponent("PlugIns/NotificationHistoryWidget.appex"),
                    appURL.appendingPathComponent("Extensions/NotificationHistoryIntents.appex")]
        let signatures = try urls.map { url -> Set<String> in
            guard let executable = Bundle(url: url)?.executableURL else { throw SharedStorageConfigurationError.invalidSignature }
            return try read(executable)
        }
        return try selectCommon(signatures)
    }
    static func group() throws -> String { try selected.get() }
}
