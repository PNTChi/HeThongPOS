using System.Security.Cryptography;
using System.Text;
using Cashier.Core;

namespace Cashier.WinForms;

// Every SalesForm uses this lease, including forms opened by an existing host.
// Controllers sharing one checkout journal must never run at the same time.
internal sealed class CashierWindowLease : IDisposable
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> Held = new(StringComparer.Ordinal);
    private readonly string _name;
    private Mutex? _mutex;

    private CashierWindowLease(string name, Mutex mutex) { _name = name; _mutex = mutex; }

    public static CashierWindowLease Acquire(string directory)
    {
        var path = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        var name = "Local\\Nhom11.Cashier.Journal." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
        lock (Gate)
        {
            if (!Held.Add(name)) throw AlreadyOpen();
            Mutex? mutex = null;
            try
            {
                mutex = new Mutex(false, name);
                bool acquired;
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw AlreadyOpen();
                return new CashierWindowLease(name, mutex);
            }
            catch
            {
                mutex?.Dispose();
                Held.Remove(name);
                throw;
            }
        }
    }

    private static BusinessException AlreadyOpen() => new(
        "Cashier đang mở và dùng cùng dữ liệu giao dịch. Hãy đóng cửa sổ Cashier hiện tại trước khi mở cửa sổ khác.");

    public void Dispose()
    {
        lock (Gate)
        {
            if (_mutex is null) return;
            try { _mutex.ReleaseMutex(); }
            finally { _mutex.Dispose(); _mutex = null; Held.Remove(_name); }
        }
    }
}
