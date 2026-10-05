import Foundation
import Darwin

final class SharedWriterLock {
    private var descriptor: Int32
    init(path: String) throws {
        descriptor = open(path + ".lock", O_CREAT | O_RDWR, S_IRUSR | S_IWUSR)
        guard descriptor >= 0 else { throw HistoryError.lockFailed(errno) }
        let start = ProcessInfo.processInfo.systemUptime
        while flock(descriptor, LOCK_EX | LOCK_NB) != 0 {
            let failure = errno
            guard failure == EWOULDBLOCK || failure == EAGAIN || failure == EINTR else {
                close(descriptor); descriptor = -1
                throw HistoryError.lockFailed(failure)
            }
            if ProcessInfo.processInfo.systemUptime - start >= 5 {
                close(descriptor)
                descriptor = -1
                throw HistoryError.lockTimedOut
            }
            usleep(20_000)
        }
    }
    deinit { if descriptor >= 0 { flock(descriptor, LOCK_UN); close(descriptor) } }
}
