using System.Net;
using System.Runtime.InteropServices;
using VRRecorder.Infrastructure.Osc;

namespace VRRecorder.IntegrationTests.Osc;

public sealed class ShellWindowsDnsServiceNativeApiTests
{
    [Fact]
    public void BrowseRootsCallbackMapsPtrRecordsAndKeepsCancelHandleAlive()
    {
        var interop = new StubWindowsDnsServiceInterop();
        var api = new ShellWindowsDnsServiceNativeApi(interop);
        uint? callbackStatus = null;
        IReadOnlyList<string>? callbackNames = null;

        using var operation = api.StartBrowse(
            "_oscjson._tcp.local",
            (status, names) =>
            {
                callbackStatus = status;
                callbackNames = names;
            });

        Assert.Equal(1u, interop.BrowseRequest.Version);
        Assert.Equal(0u, interop.BrowseRequest.InterfaceIndex);
        Assert.Equal(
            "_oscjson._tcp.local",
            Marshal.PtrToStringUni(interop.BrowseRequest.QueryName));
        Assert.NotEqual(0, interop.BrowseRequest.Callback);
        Assert.NotEqual(0, interop.BrowseRequest.QueryContext);
        Assert.NotEqual(0, interop.BrowseCancelPointer);
        Assert.Equal(
            0,
            Marshal.PtrToStructure<TestDnsServiceCancel>(
                interop.BrowseCancelPointer).Reserved);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        interop.EmitBrowse(
            status: 0,
            [
                "VRChat-Client-zeta._oscjson._tcp.local.",
                "VRChat-Client-alpha._oscjson._tcp.local.",
            ]);

        Assert.Equal(0u, callbackStatus);
        Assert.Equal(
        [
            "VRChat-Client-zeta._oscjson._tcp.local.",
            "VRChat-Client-alpha._oscjson._tcp.local.",
        ], callbackNames);
        Assert.Equal(1, interop.FreeRecordListCallCount);

        operation.Cancel();

        Assert.Equal(1, interop.BrowseCancelCallCount);
        Assert.Equal(interop.BrowseCancelPointer, interop.CancelledBrowsePointer);

        interop.EmitBrowse(status: 1223, []);
    }

    [Fact]
    public void ResolveMapsServiceInstanceAndFreesItAfterManagedProjection()
    {
        var interop = new StubWindowsDnsServiceInterop();
        var api = new ShellWindowsDnsServiceNativeApi(interop);
        var serviceId = "VRChat-Client-alpha._oscjson._tcp.local.";
        uint? callbackStatus = null;
        WindowsDnsSdResolvedService? callbackService = null;

        using var operation = api.StartResolve(
            serviceId,
            (status, service) =>
            {
                callbackStatus = status;
                callbackService = service;
            });

        Assert.Equal(1u, interop.ResolveRequest.Version);
        Assert.Equal(0u, interop.ResolveRequest.InterfaceIndex);
        Assert.Equal(
            serviceId,
            Marshal.PtrToStringUni(interop.ResolveRequest.QueryName));
        Assert.NotEqual(0, interop.ResolveRequest.Callback);
        Assert.NotEqual(0, interop.ResolveRequest.QueryContext);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        interop.EmitResolve(
            status: 0,
            serviceId,
            hostName: "alpha-host.local.",
            ipv4: [127, 0, 0, 1],
            ipv6: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1],
            port: 19001,
            textProperties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "alpha",
                ["txtvers"] = "1",
            });

        Assert.Equal(0u, callbackStatus);
        Assert.NotNull(callbackService);
        Assert.Equal(serviceId, callbackService.ServiceInstanceName);
        Assert.Equal("alpha-host.local.", callbackService.HostName);
        Assert.Equal(
            [IPAddress.Loopback, IPAddress.IPv6Loopback],
            callbackService.Addresses);
        Assert.Equal(19001, callbackService.Port);
        Assert.Equal("alpha", callbackService.TextProperties["name"]);
        Assert.Equal("1", callbackService.TextProperties["txtvers"]);
        Assert.Equal(1, interop.FreeServiceInstanceCallCount);
        Assert.Equal(0, interop.ResolveCancelCallCount);
    }

    [Fact]
    public void ProductionInteropFailsBeforeNativeCallOutsideWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var api = new ShellWindowsDnsServiceNativeApi();

        Assert.False(api.IsSupported);
        Assert.Throws<PlatformNotSupportedException>(() =>
            api.StartBrowse("_oscjson._tcp.local", (_, _) => { }));
    }

    private sealed class StubWindowsDnsServiceInterop
        : IWindowsDnsServiceInterop
    {
        private readonly List<nint> _browseAllocations = [];
        private readonly List<nint> _resolveAllocations = [];

        public bool IsSupported => true;

        public TestDnsServiceBrowseRequest BrowseRequest { get; private set; }

        public TestDnsServiceResolveRequest ResolveRequest { get; private set; }

        public nint BrowseCancelPointer { get; private set; }

        public nint ResolveCancelPointer { get; private set; }

        public nint CancelledBrowsePointer { get; private set; }

        public int BrowseCancelCallCount { get; private set; }

        public int ResolveCancelCallCount { get; private set; }

        public int FreeRecordListCallCount { get; private set; }

        public int FreeServiceInstanceCallCount { get; private set; }

        public uint Browse(nint request, nint cancelHandle)
        {
            BrowseRequest = Marshal.PtrToStructure<
                TestDnsServiceBrowseRequest>(request);
            BrowseCancelPointer = cancelHandle;
            return 9506;
        }

        public uint BrowseCancel(nint cancelHandle)
        {
            BrowseCancelCallCount++;
            CancelledBrowsePointer = cancelHandle;
            return 0;
        }

        public uint Resolve(nint request, nint cancelHandle)
        {
            ResolveRequest = Marshal.PtrToStructure<
                TestDnsServiceResolveRequest>(request);
            ResolveCancelPointer = cancelHandle;
            return 9506;
        }

        public uint ResolveCancel(nint cancelHandle)
        {
            ResolveCancelCallCount++;
            return 0;
        }

        public void FreeRecordList(nint records)
        {
            FreeRecordListCallCount++;
            FreeAll(_browseAllocations);
        }

        public void FreeServiceInstance(nint serviceInstance)
        {
            FreeServiceInstanceCallCount++;
            FreeAll(_resolveAllocations);
        }

        public void EmitBrowse(uint status, IReadOnlyList<string> serviceIds)
        {
            var records = CreatePtrRecordList(serviceIds);
            var callback = Marshal.GetDelegateForFunctionPointer<
                TestBrowseCallback>(BrowseRequest.Callback);
            callback(status, BrowseRequest.QueryContext, records);
        }

        public void EmitResolve(
            uint status,
            string serviceId,
            string hostName,
            byte[] ipv4,
            byte[] ipv6,
            ushort port,
            IReadOnlyDictionary<string, string> textProperties)
        {
            var serviceInstance = CreateServiceInstance(
                serviceId,
                hostName,
                ipv4,
                ipv6,
                port,
                textProperties);
            var callback = Marshal.GetDelegateForFunctionPointer<
                TestResolveCallback>(ResolveRequest.Callback);
            callback(status, ResolveRequest.QueryContext, serviceInstance);
        }

        private nint CreatePtrRecordList(IReadOnlyList<string> serviceIds)
        {
            nint next = 0;
            for (var index = serviceIds.Count - 1; index >= 0; index--)
            {
                var owner = AllocateString(
                    "_oscjson._tcp.local.",
                    _browseAllocations);
                var target = AllocateString(
                    serviceIds[index],
                    _browseAllocations);
                var record = Marshal.AllocHGlobal(
                    Marshal.SizeOf<TestDnsRecord>());
                _browseAllocations.Add(record);
                Marshal.StructureToPtr(
                    new TestDnsRecord
                    {
                        Next = next,
                        Name = owner,
                        Type = 12,
                        DataLength = checked((ushort)nint.Size),
                        Data = target,
                    },
                    record,
                    fDeleteOld: false);
                next = record;
            }

            return next;
        }

        private nint CreateServiceInstance(
            string serviceId,
            string hostName,
            byte[] ipv4,
            byte[] ipv6,
            ushort port,
            IReadOnlyDictionary<string, string> textProperties)
        {
            var instanceName = AllocateString(serviceId, _resolveAllocations);
            var nativeHostName = AllocateString(hostName, _resolveAllocations);
            var ip4Address = AllocateBytes(ipv4, _resolveAllocations);
            var ip6Address = AllocateBytes(ipv6, _resolveAllocations);
            var keys = AllocateStringPointerArray(
                textProperties.Keys,
                _resolveAllocations);
            var values = AllocateStringPointerArray(
                textProperties.Values,
                _resolveAllocations);
            var instance = Marshal.AllocHGlobal(
                Marshal.SizeOf<TestDnsServiceInstance>());
            _resolveAllocations.Add(instance);
            Marshal.StructureToPtr(
                new TestDnsServiceInstance
                {
                    InstanceName = instanceName,
                    HostName = nativeHostName,
                    Ip4Address = ip4Address,
                    Ip6Address = ip6Address,
                    Port = port,
                    PropertyCount = checked((uint)textProperties.Count),
                    Keys = keys,
                    Values = values,
                },
                instance,
                fDeleteOld: false);
            return instance;
        }

        private static nint AllocateString(
            string value,
            ICollection<nint> allocations)
        {
            var pointer = Marshal.StringToHGlobalUni(value);
            allocations.Add(pointer);
            return pointer;
        }

        private static nint AllocateBytes(
            byte[] value,
            ICollection<nint> allocations)
        {
            var pointer = Marshal.AllocHGlobal(value.Length);
            allocations.Add(pointer);
            Marshal.Copy(value, 0, pointer, value.Length);
            return pointer;
        }

        private static nint AllocateStringPointerArray(
            IEnumerable<string> values,
            ICollection<nint> allocations)
        {
            var strings = values
                .Select(value => AllocateString(value, allocations))
                .ToArray();
            var array = Marshal.AllocHGlobal(strings.Length * nint.Size);
            allocations.Add(array);
            Marshal.Copy(strings, 0, array, strings.Length);
            return array;
        }

        private static void FreeAll(ICollection<nint> allocations)
        {
            foreach (var allocation in allocations)
            {
                Marshal.FreeHGlobal(allocation);
            }

            allocations.Clear();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TestDnsServiceBrowseRequest
    {
        public uint Version;
        public uint InterfaceIndex;
        public nint QueryName;
        public nint Callback;
        public nint QueryContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TestDnsServiceResolveRequest
    {
        public uint Version;
        public uint InterfaceIndex;
        public nint QueryName;
        public nint Callback;
        public nint QueryContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TestDnsServiceCancel
    {
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TestDnsRecord
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
    private struct TestDnsServiceInstance
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void TestBrowseCallback(
        uint status,
        nint queryContext,
        nint records);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void TestResolveCallback(
        uint status,
        nint queryContext,
        nint serviceInstance);
}
