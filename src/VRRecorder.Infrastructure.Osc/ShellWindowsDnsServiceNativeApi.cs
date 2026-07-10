using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace VRRecorder.Infrastructure.Osc;

public sealed class ShellWindowsDnsServiceNativeApi
    : IWindowsDnsServiceNativeApi
{
    // windns.h / WinError.h values used by the Version 1 DNS-SD APIs.
    private const uint DnsQueryRequestVersion1 = 1;
    private const uint DnsRequestPending = 9506;
    private const uint ErrorSuccess = 0;
    private const uint ErrorCancelled = 1223;
    private const uint ErrorInvalidData = 13;
    private const ushort DnsTypePtr = 12;
    private const int MaximumBrowseRecords = 1024;
    private const uint MaximumTextProperties = 256;
    private static readonly WindowsDnsServiceBrowseNativeCallback BrowseCallback =
        OnBrowse;
    private static readonly WindowsDnsServiceResolveNativeCallback ResolveCallback =
        OnResolve;
    private readonly IWindowsDnsServiceInterop _interop;

    public ShellWindowsDnsServiceNativeApi()
        : this(new WindowsDnsServiceInterop())
    {
    }

    public ShellWindowsDnsServiceNativeApi(IWindowsDnsServiceInterop interop)
    {
        ArgumentNullException.ThrowIfNull(interop);
        _interop = interop;
    }

    public bool IsSupported => _interop.IsSupported;

    public IWindowsDnsServiceOperation StartBrowse(
        string queryName,
        WindowsDnsServiceBrowseCallback callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryName);
        ArgumentNullException.ThrowIfNull(callback);
        EnsureSupported();
        return new BrowseOperation(_interop, queryName, callback);
    }

    public IWindowsDnsServiceOperation StartResolve(
        string serviceInstanceName,
        WindowsDnsServiceResolveCallback callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceInstanceName);
        ArgumentNullException.ThrowIfNull(callback);
        EnsureSupported();
        return new ResolveOperation(_interop, serviceInstanceName, callback);
    }

    private void EnsureSupported()
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException(
                "Windows DNS-SD requires Windows 10 or later.");
        }
    }

    private static void OnBrowse(
        uint status,
        nint queryContext,
        nint records)
    {
        try
        {
            if (queryContext == 0)
            {
                return;
            }

            var handle = GCHandle.FromIntPtr(queryContext);
            if (handle.Target is BrowseOperation operation)
            {
                operation.Process(status, records);
            }
        }
        catch
        {
            // Managed failures must never unwind through a DNSAPI callback.
        }
    }

    private static void OnResolve(
        uint status,
        nint queryContext,
        nint serviceInstance)
    {
        try
        {
            if (queryContext == 0)
            {
                return;
            }

            var handle = GCHandle.FromIntPtr(queryContext);
            if (handle.Target is ResolveOperation operation)
            {
                operation.Process(status, serviceInstance);
            }
        }
        catch
        {
            // Managed failures must never unwind through a DNSAPI callback.
        }
    }

    private abstract class Operation : IWindowsDnsServiceOperation
    {
        private readonly nint _queryName;
        private readonly nint _request;
        private GCHandle _contextHandle;
        private int _cancelled;
        private int _disposed;
        private int _terminal;

        protected Operation(string queryName, int requestSize)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(queryName);
            _queryName = Marshal.StringToCoTaskMemUni(queryName);
            CancelHandle = Marshal.AllocHGlobal(
                Marshal.SizeOf<DnsServiceCancel>());
            Marshal.StructureToPtr(
                new DnsServiceCancel(),
                CancelHandle,
                fDeleteOld: false);
            _request = Marshal.AllocHGlobal(requestSize);
            _contextHandle = GCHandle.Alloc(this);
        }

        protected nint CancelHandle { get; }

        protected nint QueryContext => GCHandle.ToIntPtr(_contextHandle);

        protected nint QueryName => _queryName;

        protected nint Request => _request;

        public void Cancel()
        {
            if (Interlocked.Exchange(ref _cancelled, 1) != 0)
            {
                return;
            }

            var status = CancelCore(CancelHandle);
            if (status is not (ErrorSuccess or ErrorCancelled))
            {
                Interlocked.Exchange(ref _cancelled, 0);
                throw CreateFailure("cancel", status);
            }
        }

        public void Dispose()
        {
            if (Volatile.Read(ref _terminal) == 0)
            {
                throw new InvalidOperationException(
                    "The Windows DNS-SD operation is still active.");
            }

            Cleanup();
        }

        protected void MarkTerminal() =>
            Interlocked.Exchange(ref _terminal, 1);

        protected void ThrowIfStartFailed(uint status, string operation)
        {
            if (status == DnsRequestPending)
            {
                return;
            }

            MarkTerminal();
            Cleanup();
            throw CreateFailure(operation, status);
        }

        protected abstract uint CancelCore(nint cancelHandle);

        private void Cleanup()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Marshal.FreeHGlobal(_request);
            Marshal.FreeHGlobal(CancelHandle);
            Marshal.FreeCoTaskMem(_queryName);
            if (_contextHandle.IsAllocated)
            {
                _contextHandle.Free();
            }
        }
    }

    private sealed class BrowseOperation : Operation
    {
        private readonly WindowsDnsServiceBrowseCallback _callback;
        private readonly IWindowsDnsServiceInterop _interop;

        public BrowseOperation(
            IWindowsDnsServiceInterop interop,
            string queryName,
            WindowsDnsServiceBrowseCallback callback)
            : base(queryName, Marshal.SizeOf<DnsServiceBrowseRequest>())
        {
            ArgumentNullException.ThrowIfNull(callback);
            _interop = interop;
            _callback = callback;
            Marshal.StructureToPtr(
                new DnsServiceBrowseRequest
                {
                    Version = DnsQueryRequestVersion1,
                    InterfaceIndex = 0,
                    QueryName = QueryName,
                    Callback = Marshal.GetFunctionPointerForDelegate(
                        BrowseCallback),
                    QueryContext = QueryContext,
                },
                Request,
                fDeleteOld: false);
            ThrowIfStartFailed(
                _interop.Browse(Request, CancelHandle),
                "browse");
        }

        public void Process(uint status, nint records)
        {
            IReadOnlyList<string> serviceInstanceNames = [];
            try
            {
                if (records != 0)
                {
                    serviceInstanceNames = ReadPtrRecords(records);
                }
            }
            finally
            {
                if (records != 0)
                {
                    _interop.FreeRecordList(records);
                }
            }

            if (status != ErrorSuccess)
            {
                MarkTerminal();
            }

            _callback(status, serviceInstanceNames);
        }

        protected override uint CancelCore(nint cancelHandle) =>
            _interop.BrowseCancel(cancelHandle);

        private static List<string> ReadPtrRecords(nint records)
        {
            var names = new List<string>();
            var visited = new HashSet<nint>();
            var current = records;
            while (current != 0 &&
                   names.Count < MaximumBrowseRecords &&
                   visited.Add(current))
            {
                var record = Marshal.PtrToStructure<DnsRecord>(current);
                if (record.Type == DnsTypePtr && record.Data != 0)
                {
                    var name = Marshal.PtrToStringUni(record.Data);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name);
                    }
                }

                current = record.Next;
            }

            return names;
        }
    }

    private sealed class ResolveOperation : Operation
    {
        private readonly IWindowsDnsServiceInterop _interop;
        private readonly WindowsDnsServiceResolveCallback _callback;

        public ResolveOperation(
            IWindowsDnsServiceInterop interop,
            string serviceInstanceName,
            WindowsDnsServiceResolveCallback callback)
            : base(
                serviceInstanceName,
                Marshal.SizeOf<DnsServiceResolveRequest>())
        {
            ArgumentNullException.ThrowIfNull(callback);
            _interop = interop;
            _callback = callback;
            Marshal.StructureToPtr(
                new DnsServiceResolveRequest
                {
                    Version = DnsQueryRequestVersion1,
                    InterfaceIndex = 0,
                    QueryName = QueryName,
                    Callback = Marshal.GetFunctionPointerForDelegate(
                        ResolveCallback),
                    QueryContext = QueryContext,
                },
                Request,
                fDeleteOld: false);
            ThrowIfStartFailed(
                _interop.Resolve(Request, CancelHandle),
                "resolve");
        }

        public void Process(uint status, nint serviceInstance)
        {
            WindowsDnsSdResolvedService? resolved = null;
            var callbackStatus = status;
            try
            {
                if (status == ErrorSuccess && serviceInstance != 0)
                {
                    resolved = TryReadServiceInstance(serviceInstance);
                    if (resolved is null)
                    {
                        callbackStatus = ErrorInvalidData;
                    }
                }
            }
            catch
            {
                callbackStatus = ErrorInvalidData;
            }
            finally
            {
                if (serviceInstance != 0)
                {
                    _interop.FreeServiceInstance(serviceInstance);
                }

                MarkTerminal();
            }

            _callback(callbackStatus, resolved);
        }

        protected override uint CancelCore(nint cancelHandle) =>
            _interop.ResolveCancel(cancelHandle);

        private static WindowsDnsSdResolvedService? TryReadServiceInstance(
            nint serviceInstance)
        {
            var native = Marshal.PtrToStructure<DnsServiceInstance>(
                serviceInstance);
            var instanceName = Marshal.PtrToStringUni(native.InstanceName);
            var hostName = Marshal.PtrToStringUni(native.HostName);
            if (string.IsNullOrWhiteSpace(instanceName) ||
                string.IsNullOrWhiteSpace(hostName) ||
                native.Port == 0 ||
                native.PropertyCount > MaximumTextProperties)
            {
                return null;
            }

            var addresses = ReadAddresses(native);
            if (addresses.Length == 0)
            {
                return null;
            }

            var textProperties = ReadTextProperties(native);
            return textProperties is null
                ? null
                : new WindowsDnsSdResolvedService(
                    instanceName,
                    hostName,
                    addresses,
                    native.Port,
                    textProperties);
        }

        private static IPAddress[] ReadAddresses(
            DnsServiceInstance native)
        {
            var addresses = new List<IPAddress>(capacity: 2);
            if (native.Ip4Address != 0)
            {
                var bytes = new byte[4];
                Marshal.Copy(native.Ip4Address, bytes, 0, bytes.Length);
                addresses.Add(new IPAddress(bytes));
            }

            if (native.Ip6Address != 0)
            {
                var bytes = new byte[16];
                Marshal.Copy(native.Ip6Address, bytes, 0, bytes.Length);
                addresses.Add(new IPAddress(bytes));
            }

            return addresses
                .Where(address => address.AddressFamily is
                    AddressFamily.InterNetwork or
                    AddressFamily.InterNetworkV6)
                .ToArray();
        }

        private static Dictionary<string, string>? ReadTextProperties(
            DnsServiceInstance native)
        {
            var properties = new Dictionary<string, string>(
                StringComparer.Ordinal);
            if (native.PropertyCount == 0)
            {
                return properties;
            }

            if (native.Keys == 0 || native.Values == 0)
            {
                return null;
            }

            for (var index = 0; index < native.PropertyCount; index++)
            {
                var offset = checked((int)index * nint.Size);
                var key = Marshal.PtrToStringUni(
                    Marshal.ReadIntPtr(native.Keys, offset));
                var value = Marshal.PtrToStringUni(
                    Marshal.ReadIntPtr(native.Values, offset));
                if (string.IsNullOrWhiteSpace(key) ||
                    value is null ||
                    !properties.TryAdd(key, value))
                {
                    return null;
                }
            }

            return properties;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceBrowseRequest
    {
        public uint Version;
        public uint InterfaceIndex;
        public nint QueryName;
        public nint Callback;
        public nint QueryContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceResolveRequest
    {
        public uint Version;
        public uint InterfaceIndex;
        public nint QueryName;
        public nint Callback;
        public nint QueryContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceCancel
    {
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsRecord
    {
        public nint Next;
        public nint Name;
        public ushort Type;
        public ushort DataLength;
        public uint Flags;
        public uint Ttl;
        public uint Reserved;
        public nint Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceInstance
    {
        public nint InstanceName;
        public nint HostName;
        public nint Ip4Address;
        public nint Ip6Address;
        public ushort Port;
        public ushort Priority;
        public ushort Weight;
        public uint PropertyCount;
        public nint Keys;
        public nint Values;
        public uint InterfaceIndex;
    }

    private static IOException CreateFailure(string operation, uint status) =>
        new($"Windows DNS-SD {operation} failed with status {status}.");
}
