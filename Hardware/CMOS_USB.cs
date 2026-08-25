// =============================================================================
// CMOS_USB.cs — P/Invoke wrapper for Hamamatsu CMOS_USB.dll
// Source: Official Hamamatsu SDK (Windows/Source_Code/CS/CMOS_USB.cs)
// Namespace changed to DentalX
// =============================================================================

using System;
using System.Runtime.InteropServices;

namespace DentalX
{
    public class CMOS_USB
    {
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        // ─── Structs ───

        [StructLayout(LayoutKind.Sequential)]
        public struct UnitIntegrationParameter
        {
            public ushort Xray_Incident_Threshold;
            public ushort Xray_Integration_End_Threshold;
            public double Integration_Time;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct UnitXrayImage
        {
            public byte Mode;
            public UnitIntegrationParameter IntegParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct UnitSensorInformation
        {
            public UnitIntegrationParameter IntegParam;
            public ushort Lot_Serial_No;
            public byte Sensor_Type;
            public byte Firmware_Version;
        }

        // ─── Device Open/Close ───

        [DllImport("CMOS_USB.dll")]
        public extern static IntPtr USB_OpenDevice(ushort ProductID);

        [DllImport("CMOS_USB.dll")]
        public extern static IntPtr USB_OpenTargetDevice(ushort ProductID, ushort SerialNo);

        [DllImport("CMOS_USB.dll")]
        public extern static void USB_CloseDevice(IntPtr DeviceHandle);

        [DllImport("CMOS_USB.dll")]
        public extern static IntPtr USB_OpenPipe(IntPtr DeviceHandle);

        [DllImport("CMOS_USB.dll")]
        public extern static IntPtr USB_SetVid(ushort VID, string DeviceGUID);

        // ─── Suspend/Resume ───

        [DllImport("CMOS_USB.dll")]
        public extern static uint USB_SuspendDevice(IntPtr DeviceHandle, uint SuspendDelay);

        [DllImport("CMOS_USB.dll")]
        public extern static uint USB_ResumeDevice(IntPtr DeviceHandle);

        // ─── Image Acquisition ───

        [DllImport("CMOS_USB.dll")]
        public extern static uint HPK_GetXrayImage(
            IntPtr DeviceHandle,
            byte[] ImageData,
            out ulong bufferLength,
            ref UnitXrayImage XrayImage);

        [DllImport("CMOS_USB.dll")]
        public extern static uint HPK_GetXrayCorrectionImage(
            IntPtr DeviceHandle,
            byte[] ImageData,
            out ulong bufferLength,
            ref UnitXrayImage XrayImage);

        [DllImport("CMOS_USB.dll")]
        public extern static uint HPK_ForceTrigAndGetDummy(
            IntPtr DeviceHandle,
            byte[] ImageData,
            out ulong bufferLength,
            ref UnitXrayImage XrayImage);

        // ─── Sensor Info ───

        [DllImport("CMOS_USB.dll")]
        public extern static uint HPK_GetSensorInformation(
            IntPtr DeviceHandle,
            ref UnitIntegrationParameter IntegParam,
            out UnitSensorInformation SensorInfo);

        // ─── Trigger Photodiode ───

        [DllImport("CMOS_USB.dll")]
        public extern static uint HPK_GetTrigPdData(
            IntPtr DeviceHandle,
            double AcquisitionTime,
            ushort[] TrigPdData,
            out ulong bufferLength,
            ref UnitIntegrationParameter IntegParam);

        [DllImport("CMOS_USB.dll")]
        public extern static uint HPK_StopTrigPdData(
            IntPtr DeviceHandle,
            byte[] Buffer,
            out ulong bufferLength,
            ref UnitIntegrationParameter IntegParam);

        // ─── Control ───

        [DllImport("CMOS_USB.dll")]
        public extern static uint HPK_AbortBulkPipe(IntPtr DeviceHandle);

        // ─── Memory ───

        [DllImport("CMOS_USB.dll")]
        public extern static uint HPK_ReadCustomerCode(
            IntPtr DeviceHandle, byte[] Buffer, uint Index, byte[] Code);

        [DllImport("CMOS_USB.dll")]
        public extern static uint HPK_ReadTypeNumber(IntPtr DeviceHandle, byte[] Buffer);

        [DllImport("CMOS_USB.dll")]
        public extern static uint HPK_ReadMemory(
            IntPtr DeviceHandle, byte[] Buffer, uint Address, uint Length);

        // ─── Wait Event ───

        [DllImport("CMOS_USB.dll")]
        public extern static uint HPK_WaitEvent(
            IntPtr DeviceHandle,
            ref ulong pEventStatus,
            ulong timeout,
            IntPtr hAbortEvent);
    }
}
