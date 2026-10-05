#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$root/artifacts"
testdir="$(mktemp -d "${TMPDIR:-/tmp}/notificationhistory-storage.XXXXXX")"
trap 'rm -rf "$testdir"' EXIT
database="$testdir/history.sqlite3"
dotnet run --project "$root/tests/NotificationHistory.Tests" -c Release -- --signing-fixtures "$testdir/signing"
dotnet run --project "$root/tests/NotificationHistory.Tests" -c Release -- --seed-shared "$database"
xcrun swiftc -parse-as-library "$root/native/Shared/SharedDatabase.swift" \
  "$root/native/Shared/SharedWriterLock.swift" \
  "$root/native/Shared/SignedAppGroups.swift" \
  "$root/native/NotificationHistoryIntents/CaptureDiagnostics.swift" \
  "$root/tests/NativeStorageTests.swift" -o "$testdir/native-storage-tests" -lsqlite3
"$testdir/native-storage-tests" --verify-signing "$testdir/signing"
dotnet run --project "$root/tests/NotificationHistory.Tests" -c Release --no-build -- --seed-legacy-shared "$testdir/legacy.sqlite3"
"$testdir/native-storage-tests" "$testdir/legacy.sqlite3" "$root/shared/schema.sql" --verify-legacy
dotnet run --project "$root/tests/NotificationHistory.Tests" -c Release --no-build -- --verify-legacy-shared "$testdir/legacy.sqlite3"
"$testdir/native-storage-tests" "$database" "$root/shared/schema.sql" --verify-seed
dotnet run --project "$root/tests/NotificationHistory.Tests" -c Release --no-build -- --burst-shared "$database" &
managed_pid=$!
"$testdir/native-storage-tests" "$database" "$root/shared/schema.sql"
wait "$managed_pid"
dotnet run --project "$root/tests/NotificationHistory.Tests" -c Release --no-build -- --verify-shared "$database"
