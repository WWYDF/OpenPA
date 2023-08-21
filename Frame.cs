using System.Runtime.InteropServices;
using System;
using System.Text;

namespace mumblelib
{
    using wchar = UInt16;

    class Text
    {
        public static Encoding Encoding = Encoding.Unicode;
    }


    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public unsafe struct Frame
    {
        public uint uiVersion;

        public uint uiTick;

        public fixed float fAvatarPosition[3];

        public fixed float fAvatarFront[3];

        public fixed float fAvatarTop[3];

        public fixed wchar name[256];

        public fixed float fCameraPosition[3];

        public fixed float fCameraFront[3];

        public fixed float fCameraTop[3];

        public fixed wchar id[256];

        public uint context_len;

        public fixed byte context[256];

        public fixed wchar description[2048];

        public void SetName(string name)
        {
            fixed (Frame* ptr = &this)
            {
                byte[] bytes = Text.Encoding.GetBytes(name + "\u0000");
                Marshal.Copy(bytes, 0, new IntPtr(ptr->name), bytes.Length);
            }
        }

        public void SetDescription(string desc)
        {
            fixed (Frame* ptr = &this)
            {
                byte[] bytes = Text.Encoding.GetBytes(desc + "\u0000");
                Marshal.Copy(bytes, 0, new IntPtr(ptr->description), bytes.Length);
            }
        }

        public void SetID(string id)
        {
            fixed (Frame* ptr = &this)
            {
                byte[] bytes = Text.Encoding.GetBytes(id + "\u0000");
                Marshal.Copy(bytes, 0, new IntPtr(ptr->id), bytes.Length);
            }
        }

        public void SetContext(string context)
        {
            fixed (Frame* ptr = &this)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(context);
                this.context_len = (uint)Math.Min(256, bytes.Length);
                Marshal.Copy(bytes, 0, new IntPtr(ptr->context), (int)this.context_len);
            }
        }
    }
}