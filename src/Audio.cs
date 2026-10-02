using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MuteMe {
    public sealed class Microphone {
        public string Id, Name;
        public override string ToString() { return Name; }
    }
    public static class Audio {
        public static void Check(int hr) { Marshal.ThrowExceptionForHR(hr); }
        public static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        public static List<Microphone> List() {
            var result = new List<Microphone>();
            var e = (IMMDeviceEnumerator)new DeviceEnumerator(); IMMDeviceCollection c = null;
            try {
                Check(e.EnumAudioEndpoints(1, 1, out c)); uint count; Check(c.GetCount(out count));
                for (uint i = 0; i < count; i++) {
                    IMMDevice d = null; IPropertyStore p = null;
                    try {
                        Check(c.Item(i, out d)); string id; Check(d.GetId(out id));
                        Check(d.OpenPropertyStore(0, out p));
                        var key = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
                        PropVariant v; Check(p.GetValue(ref key, out v));
                        try { result.Add(new Microphone { Id = id, Name = v.vt == 31 ? Marshal.PtrToStringUni(v.pointer) : "Microphone" }); }
                        finally { PropVariantClear(ref v); }
                    } finally { Release(p); Release(d); }
                }
            } finally { Release(c); Release(e); }
            return result;
        }
        public static string DefaultId() {
            var e = (IMMDeviceEnumerator)new DeviceEnumerator(); IMMDevice d = null;
            try { Check(e.GetDefaultAudioEndpoint(1, 1, out d)); string id; Check(d.GetId(out id)); return id; }
            finally { Release(d); Release(e); }
        }
        public static IAudioEndpointVolume Open(string id) {
            var e = (IMMDeviceEnumerator)new DeviceEnumerator(); IMMDevice d = null;
            try {
                Check(e.GetDevice(id, out d)); object volume;
                var iid = typeof(IAudioEndpointVolume).GUID;
                Check(d.Activate(ref iid, 1, IntPtr.Zero, out volume)); return (IAudioEndpointVolume)volume;
            } finally { Release(d); Release(e); }
        }
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);
    }
    [StructLayout(LayoutKind.Sequential)] public struct PropertyKey { public Guid fmtid; public uint pid; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] public struct PropVariant {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointer;
    }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] public class DeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceCollection {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolume {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint count);
        [PreserveSig] int VolumeStepUp(ref Guid context);
        [PreserveSig] int VolumeStepDown(ref Guid context);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float min, out float max, out float increment);
    }
}
